using System;
using System.Collections.Generic;
using SPTESL;
namespace OfflineEngine
{
    public static class SurviveDamageTests
    {
        private const int Crab = 620729069, Brassilisk = 929206989, Dreugh = 792640043, Hall = 1549644931, Recruit = -163473279, Leviathan = 1427457192;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            foreach (int id in new int[] { Brassilisk, Dreugh })
                foreach (int mode in new int[] { 0, 1, 2, 3 }) Case(config, test, id, mode);
            test("brassilisk-survival-summon-in-real-combat", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard attacker = f.Create(995238228, me, GameZoneName.CreatureBoard);
                    ServerCard source = f.CreateIn(Brassilisk, enemy, GameZoneName.CreatureBoard, 0);
                    f.EndTurn(); f.EndTurn(); f.Attack(attacker, source, me);
                    List<ServerCard> copies = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Brassilisk && c.CurrentLaneIndex == 1; }, s.Players[enemy].NetId, GameZoneName.CreatureBoard);
                    Require(source.CurrentHealth == 1 && copies.Count == 1 && copies[0].CurrentHealth == 2,
                        "Brassilisk did not survive combat and summon a fresh one in the other lane.");
                }
            });
            test("brassilisk-survival-full-other-lane", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard source = f.CreateIn(Brassilisk, me, GameZoneName.CreatureBoard, 0);
                    for (int i = 0; i < 4; i++) f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1);
                    ServerCard attacker = f.Create(Leviathan, enemy, GameZoneName.CreatureBoard);
                    Damage(f, attacker, source, 1);
                    Require(source.CurrentHealth == 1 && f.LaneCount(me, 0) == 1 && f.LaneCount(me, 1) == 4,
                        "A full destination lane incorrectly accepted or rerouted a summoned Brassilisk.");
                }
            });
            test("fighters-guild-hall-friendly-damage-on-owner-turn", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Hall), "Hall spec missing: " + OriginalSpecRules.SkipReason(Hall));
                    f.Create(Hall, me, GameZoneName.SupportBoard);
                    ServerCard friend = f.Create(Leviathan, me, GameZoneName.CreatureBoard), foe = f.Create(Leviathan, enemy, GameZoneName.CreatureBoard);
                    Damage(f, foe, friend, 1);
                    Require(friend.QueryAttribute(AttributeType.Attack).Value == 10, "Hall did not give the damaged friendly +2 power.");
                    Damage(f, friend, foe, 1);
                    Require(foe.QueryAttribute(AttributeType.Attack).Value == 8 && friend.QueryAttribute(AttributeType.Attack).Value == 10,
                        "Hall triggered for enemy damage.");
                    f.EndTurn(); Damage(f, foe, friend, 1);
                    Require(friend.QueryAttribute(AttributeType.Attack).Value == 10, "Hall triggered on the opponent's turn.");
                }
            });
        }
        private static void Case(GameConfiguration config, Action<string, Action> test, int id, int mode)
        {
            test("damage-survival-" + id + "-" + mode, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(id), "Survival spec missing " + id + ": " + OriginalSpecRules.SkipReason(id));
                    ServerCard source = f.CreateIn(id, me, GameZoneName.CreatureBoard, 0);
                    ServerCard attacker = f.Create(mode == 2 ? Recruit : Leviathan, enemy, GameZoneName.CreatureBoard);
                    if (id == Brassilisk && mode == 0) f.Event(delegate {
                        SPTESL.Enchantment buff = new SPTESL.Enchantment();
                        buff.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, 2));
                        CardActions.Buff.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, new List<ServerCard> { source }, buff, s.Players[me], -1, false);
                    });
                    if (mode == 3) f.Event(delegate { CardActions.Ward.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, source, -1); });
                    int deck = f.DeckCount(me), hand = f.HandCount(me);
                    Damage(f, attacker, source, mode == 1 ? 10 : 1);
                    List<ServerCard> copies = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Brassilisk && c.CurrentLaneIndex == 1; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    if (mode == 0)
                    {
                        Require(!source.IsDying(), "Survival fixture source died.");
                        if (id == Brassilisk) Require(copies.Count == 1 && copies[0].QueryAttribute(AttributeType.Attack).Value == 2
                            && copies[0].CurrentHealth == 2, "Surviving Brassilisk did not summon a fresh base Brassilisk in the other lane.");
                        else Require(f.DeckCount(me) == deck - 1 && f.HandCount(me) == hand + 1, "Surviving Dreugh did not draw one card.");
                    }
                    else Require(copies.Count == 0 && f.DeckCount(me) == deck && f.HandCount(me) == hand
                        && (mode == 3 ? !source.IsDying() : source.IsDying()), "Lethal or Ward-prevented damage incorrectly triggered survival ability.");
                }
            });
        }
        private static void Damage(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard actor, ServerCard target, int amount)
        {
            f.Event(delegate { CardActions.Damage.StaticExecuteSingleTarget(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), actor, target, amount, false, null); });
            f.DrainEffects();
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
