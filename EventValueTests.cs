using System;
using System.Collections.Generic;
using SPTESL;
namespace OfflineEngine
{
    public static class EventValueTests
    {
        private const int Crab = 620729069, Leviathan = 1427457192, Firebolt = -1865755971;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("damage-event-value-troll-sentry-champion-and-ward", delegate {
                foreach (int id in new int[] { -1806219075, -2027529089, -1480308540 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(id), "Damage-value spec missing " + id + ": " + OriginalSpecRules.SkipReason(id));
                    ServerCard source = f.Create(id, me, GameZoneName.CreatureBoard), attacker = f.Create(Leviathan, enemy, GameZoneName.CreatureBoard);
                    int power = source.QueryAttribute(AttributeType.Attack).Value;
                    f.Event(delegate { f.Avatar(me).ModifyCurrentHealth(-4, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    Damage(f, attacker, source, 2);
                    if (id == -1806219075) Require(source.QueryAttribute(AttributeType.Attack).Value == power + 2, "Troll did not gain damage amount in power.");
                    if (id == -2027529089) Require(f.Avatar(me).CurrentHealth == 28, "Sentry did not heal its owner by damage amount.");
                    if (id == -1480308540) Require(f.Avatar(enemy).CurrentHealth == 28, "Champion did not damage opponent by damage amount.");
                    int health = source.CurrentHealth, own = f.Avatar(me).CurrentHealth, opposing = f.Avatar(enemy).CurrentHealth;
                    int beforePower = source.QueryAttribute(AttributeType.Attack).Value;
                    f.Event(delegate { CardActions.Ward.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, source, -1); });
                    Damage(f, attacker, source, 2);
                    Require(source.CurrentHealth == health && source.QueryAttribute(AttributeType.Attack).Value == beforePower
                        && f.Avatar(me).CurrentHealth == own && f.Avatar(enemy).CurrentHealth == opposing,
                        "Ward-prevented damage incorrectly triggered a damage-value ability.");
                }
            });
            test("damage-from-creature-event-source-filter-and-value", delegate {
                foreach (int id in new int[] { -1845196063, 528592204 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(id), "Creature-damage spec missing " + id);
                    ServerCard source = f.Create(id, me, GameZoneName.CreatureBoard), attacker = f.Create(Leviathan, enemy, GameZoneName.CreatureBoard);
                    ServerCard spell = f.Create(Firebolt, enemy, GameZoneName.Hand);
                    // The low-health Skirmisher gets 1 health restored between independent injected hits.
                    Damage(f, spell, source, 1);
                    Require(attacker.CurrentHealth == 8 && attacker.QueryAttribute(AttributeType.Attack).Value == 8,
                        "Non-creature damage triggered a creature-source ability.");
                    f.Event(delegate { source.ModifyCurrentHealth(1, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                    Damage(f, attacker, source, 1);
                    if (id == -1845196063) Require(attacker.CurrentHealth == 7, "Wall did not reflect the damage amount to its creature source.");
                    else Require(attacker.QueryAttribute(AttributeType.Attack).Value == 7 && attacker.QueryAttribute(AttributeType.MaxHealth).Value == 7,
                        "Skirmisher did not debuff its creature damage source by -1/-1.");
                }
            });
            test("health-gained-event-value-including-overheal", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(-2088538292) && OriginalSpecRules.HasRestoredRules(-544257570), "Health-value specs missing.");
                    ServerCard spriggan = f.Create(-2088538292, me, GameZoneName.CreatureBoard);
                    f.Create(-544257570, me, GameZoneName.SupportBoard);
                    ServerCard avatar = f.Avatar(me);
                    f.Event(delegate { avatar.ModifyCurrentHealth(-4, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    Heal(f, avatar, 2); Heal(f, avatar, 10);
                    Require(avatar.CurrentHealth == 38 && spriggan.QueryAttribute(AttributeType.Attack).Value == 13
                        && f.Avatar(enemy).CurrentHealth == 18, "Health observers must use the twelve health gained, including above starting health; avatar="
                        + avatar.CurrentHealth + ", power=" + spriggan.QueryAttribute(AttributeType.Attack).Value + ", enemy=" + f.Avatar(enemy).CurrentHealth);
                    ServerCard creature = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                    f.Event(delegate { creature.ModifyCurrentHealth(-2, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    Heal(f, creature, 2);
                    Require(creature.CurrentHealth == 8 && spriggan.QueryAttribute(AttributeType.Attack).Value == 13 && f.Avatar(enemy).CurrentHealth == 18,
                        "Healing a creature incorrectly triggered player health observers.");
                }
            });
            test("pointy-wall-retaliates-during-real-combat", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard attacker = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard wall = f.Create(-1845196063, enemy, GameZoneName.CreatureBoard);
                    f.EndTurn(); f.EndTurn(); f.Attack(attacker, wall, me);
                    Require(wall.CurrentHealth == 1 && wall.CurrentGameZoneType == GameZoneType.Board && attacker.CurrentGameZoneType != GameZoneType.Board,
                        "Wall did not reflect the attack's two damage and destroy the attacking creature.");
                }
            });
        }
        private static void Damage(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard actor, ServerCard target, int amount)
        {
            f.Event(delegate { CardActions.Damage.StaticExecuteSingleTarget(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), actor, target, amount, false, null); });
            f.DrainEffects();
        }
        private static void Heal(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard target, int amount)
        {
            f.Event(delegate { CardActions.Heal.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), null,
                new List<ServerCard> { target }, amount, -1, false, false, null); });
            f.DrainEffects();
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
