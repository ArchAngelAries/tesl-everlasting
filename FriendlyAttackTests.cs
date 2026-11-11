using System;
using SPTESL;
namespace OfflineEngine
{
    public static class FriendlyAttackTests
    {
        private const int Crab = 620729069, Leader = 489838961, Perch = -623041352, Initiate = -2104243403;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("friendly-attacks-leader-perch-and-drain-source", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Leader) && OriginalSpecRules.HasRestoredRules(Perch), "Attack specs not emitted.");
                    ServerCard leader = f.Create(Leader, me, GameZoneName.CreatureBoard);
                    ServerCard attacker = f.Create(Initiate, me, GameZoneName.CreatureBoard);
                    f.Create(Perch, me, GameZoneName.SupportBoard);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    f.EndTurn(); f.EndTurn();
                    f.Event(delegate { f.Avatar(me).ModifyCurrentHealth(-4, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    int power = attacker.QueryAttribute(AttributeType.Attack).Value;
                    Require(attacker.QueryAttribute(AttributeType.Drain).Value > 0, "Fixture lacks Drain.");
                    f.Attack(attacker, f.Avatar(enemy), me);
                    Require(leader.QueryAttribute(AttributeType.Attack).Value == 1, "Other friendly attack did not buff leader once.");
                    Require(f.Avatar(enemy).CurrentHealth == 29 - power && f.Avatar(me).CurrentHealth == 27 + power,
                        "Perch damage or attacker's Drain credit differs.");
                    f.Attack(leader, f.Avatar(enemy), me);
                    Require(leader.QueryAttribute(AttributeType.Attack).Value == 1 && f.Avatar(enemy).CurrentHealth == 27 - power,
                        "Leader's own attack incorrectly buffed itself or Perch missed it.");
                    f.EndTurn();
                    int mine = f.Avatar(me).CurrentHealth, theirs = f.Avatar(enemy).CurrentHealth;
                    f.Attack(foe, f.Avatar(me), enemy);
                    Require(f.Avatar(me).CurrentHealth == mine - foe.QueryAttribute(AttributeType.Attack).Value
                        && f.Avatar(enemy).CurrentHealth == theirs && leader.QueryAttribute(AttributeType.Attack).Value == 1,
                        "Enemy attack triggered friendly attack effects.");
                }
            });
            test("friendly-attacks-trigger-when-attacker-dies", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard leader = f.Create(Leader, me, GameZoneName.CreatureBoard);
                    ServerCard attacker = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard defender = f.Create(1427457192, enemy, GameZoneName.CreatureBoard);
                    f.Create(Perch, me, GameZoneName.SupportBoard);
                    f.EndTurn(); f.EndTurn();
                    f.Attack(attacker, defender, me);
                    Require(attacker.CurrentGameZoneType != GameZoneType.Board && f.Avatar(enemy).CurrentHealth == 29
                        && leader.QueryAttribute(AttributeType.Attack).Value == 1,
                        "Fatal attack: attacker zone=" + attacker.CurrentGameZoneName + ", enemy health=" + f.Avatar(enemy).CurrentHealth
                        + ", leader power=" + leader.QueryAttribute(AttributeType.Attack).Value);
                }
            });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
