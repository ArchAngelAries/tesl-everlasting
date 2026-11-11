using System;
using SPTESL;
namespace OfflineEngine
{
    public static class SweetRollTests
    {
        private const int Crab = 620729069, Leviathan = 1427457192, Firebolt = -1865755971;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            foreach (int id in new int[] { 1843354014, -163092025, -2068621696 })
            {
                AttackCase(config, test, id, false);
                AttackCase(config, test, id, true);
            }
            test("sweet-roll-action-damage-does-not-heal-creature", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard attacker = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                    ServerCard roll = f.Create(1843354014, enemy, GameZoneName.CreatureBoard);
                    ServerCard spell = f.Create(Firebolt, me, GameZoneName.Hand);
                    f.Event(delegate { attacker.ModifyCurrentHealth(-4, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                    f.Mana(me, 20); f.Refresh(me); f.Play(spell, roll, me);
                    Require(attacker.CurrentHealth == 4 && f.CemeteryHas(enemy, 1843354014), "An action destroying Sweet Roll must not heal an unrelated creature.");
                }
            });
        }
        private static void AttackCase(GameConfiguration config, Action<string, Action> test, int id, bool silence)
        {
            test("sweet-roll-attacked-" + id + (silence ? "-silenced" : "-heals"), delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(id), "Sweet Roll spec not emitted: " + id + " " + OriginalSpecRules.SkipReason(id));
                    ServerCard attacker = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                    ServerCard other = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 1);
                    ServerCard roll = f.Create(id, enemy, GameZoneName.CreatureBoard);
                    f.EndTurn(); f.EndTurn();
                    f.Event(delegate {
                        ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek());
                        attacker.ModifyCurrentHealth(-4, null, gs, true); other.ModifyCurrentHealth(-4, null, gs, true);
                        if (silence) CardActions.Silence.StaticExecuteSingleTarget(gs, null, roll, f.Sim.Players[me], -1);
                    });
                    f.Refresh(me); f.Attack(attacker, roll, me);
                    Require(f.CemeteryHas(enemy, id) && attacker.CurrentHealth == (silence ? 4 : 8) && other.CurrentHealth == 4,
                        "Attacking " + (silence ? "silenced " : "") + "Sweet Roll must heal only its attacker when its ability is present.");
                }
            });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
