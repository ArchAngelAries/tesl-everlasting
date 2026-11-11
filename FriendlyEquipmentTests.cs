using System;
using SPTESL;
namespace OfflineEngine
{
    public static class FriendlyEquipmentTests
    {
        private const int Crab = 620729069, Sparksmith = 186430116, Greaves = -1975997249, Ship = -949205489;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("sparksmith-friendly-enemy-self-and-silence", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Sparksmith), "Sparksmith spec unavailable: " + OriginalSpecRules.SkipReason(Sparksmith));
                    ServerCard smith = f.Create(Sparksmith, me, GameZoneName.CreatureBoard);
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard first = f.Create(Greaves, me, GameZoneName.Hand), second = f.Create(Greaves, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    f.PlayEquip(first, friend, me);
                    Require(f.Avatar(enemy).CurrentHealth == 29 && f.Avatar(me).CurrentHealth == 30, "Friendly equipment must damage opponent once.");
                    f.Refresh(me); f.PlayEquip(second, smith, me);
                    Require(f.Avatar(enemy).CurrentHealth == 28, "Equipping Sparksmith itself must trigger once.");
                    f.EndTurn();
                    ServerCard opposing = f.Create(Greaves, enemy, GameZoneName.Hand);
                    f.Mana(enemy, 30); f.Refresh(enemy); f.PlayEquip(opposing, foe, enemy);
                    Require(f.Avatar(enemy).CurrentHealth == 28 && f.Avatar(me).CurrentHealth == 30, "Opposing equipment incorrectly triggered Sparksmith.");
                    f.EndTurn();
                    f.Event(delegate { CardActions.Silence.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, smith, s.Players[me], -1); });
                    ServerCard third = f.Create(Greaves, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayEquip(third, friend, me);
                    Require(f.Avatar(enemy).CurrentHealth == 28, "Silenced Sparksmith triggered.");
                }
            });
            test("sparksmith-effect-created-equipment-and-hand-source", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard smith = f.Create(Sparksmith, me, GameZoneName.Hand);
                    ServerCard ship = f.Create(Ship, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.Play(ship, null, me);
                    Require(f.Avatar(enemy).CurrentHealth == 30, "Sparksmith in hand triggered when Ship equipped its Corsair.");
                    f.Refresh(me); f.PlayCreatureLane(smith, 1, me);
                    Require(f.Avatar(enemy).CurrentHealth == 29, "Ship's effect-created equipment did not trigger the summoned Sparksmith once.");
                    ServerCard later = f.Create(Crab, me, GameZoneName.Hand);
                    f.Refresh(me); f.PlayCreatureLane(later, 1, me);
                    Require(f.Avatar(enemy).CurrentHealth == 28, "Another effect-created equipment did not trigger Sparksmith once.");
                }
            });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
