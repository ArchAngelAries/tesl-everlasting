using System;
using SPTESL;
namespace OfflineEngine
{
    public static class AfterAttackTests
    {
        private const int Crab = 620729069;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "elder-centaur-cover", 289735337, AttributeType.Cover);
            Case(config, test, "encumbered-explorer-shackle", -1955645154, AttributeType.Freeze);
        }
        private static void Case(GameConfiguration config, Action<string, Action> test, string label, int id, AttributeType attribute)
        {
            test("after-attack-" + label, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(id), "Spec not emitted: " + id + " " + OriginalSpecRules.SkipReason(id));
                    ServerCard source = f.Create(id, me, GameZoneName.CreatureBoard);
                    ServerCard other = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1);
                    f.EndTurn(); f.EndTurn();
                    f.Attack(other, f.Avatar(enemy), me);
                    Require(source.QueryAttribute(attribute).Value == 0, "Another creature's attack triggered this effect.");
                    f.Attack(source, f.Avatar(enemy), me);
                    Require(source.QueryAttribute(attribute).Value > 0 && source.QueryGameJournalCount(CardJournal.Attacked) == 1
                        && other.QueryAttribute(attribute).Value == 0, "Source's own completed attack did not apply only its effect.");
                }
            });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
