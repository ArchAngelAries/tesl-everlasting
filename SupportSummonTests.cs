using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    public static class SupportSummonTests
    {
        private const int Mudcrab = 620729069, Spider = -2023047694, Guardsman = 1700638025;

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "halls-of-the-dwemer", 883559292, Spider, 6, 3);
            Case(config, test, "imperial-camp", -1308568732, Guardsman, 4, 1);
            test("support-summon-created-by-native-effect", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    int hand = f.HandCount(me);
                    List<ServerCard> created = null;
                    f.Event(delegate {
                        GameZone.Position position = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[me].NetId,
                            GameZoneName.SupportBoard, 0, CardInsertionType.Bottom);
                        created = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, config.FindCardById(883559292), false, GameZoneType.Board, position, f.Sim.Players[me].NetId, 1, -1, null);
                    });
                    Require(created != null && created.Count == 1 && created[0].CurrentGameZoneName == GameZoneName.SupportBoard,
                        "Native effect did not create Halls of the Dwemer on the support board.");
                    List<ServerCard> tokens = f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Spider; }, f.Sim.Players[me].NetId, GameZoneName.Hand);
                    Require(f.HandCount(me) == hand + 1 && tokens.Count == 1 && tokens[0].QueryAttribute(AttributeType.Taunt).Value > 0,
                        "Effect-created support must fire Summon exactly once for its owner.");
                }
            });
        }

        private static void Case(GameConfiguration config, Action<string, Action> test, string label, int supportId, int creatureId, int cost, int bonus)
        {
            test("support-summon-and-aura-" + label, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Require(OriginalSpecRules.HasRestoredRules(supportId), "Support spec not emitted: " + OriginalSpecRules.SkipReason(supportId));
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard support = f.Create(supportId, me, GameZoneName.Hand);
                    ServerCard ally = f.Create(creatureId, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(creatureId, enemy, GameZoneName.CreatureBoard);
                    ServerCard other = f.Create(Mudcrab, me, GameZoneName.CreatureBoard);
                    int basePower = ally.QueryAttribute(AttributeType.Attack).Value;
                    int hand = f.HandCount(me), enemyHand = f.HandCount(enemy);
                    f.Mana(me, 20); f.Refresh(me); f.Play(support, null, me);
                    List<ServerCard> tokens = f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == creatureId; }, f.Sim.Players[me].NetId, GameZoneName.Hand);
                    Require(support.CurrentGameZoneName == GameZoneName.SupportBoard && support.QueryGameJournalCount(CardJournal.Played) == 1
                        && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 20 - cost
                        && f.HandCount(me) == hand && f.HandCount(enemy) == enemyHand && tokens.Count == 1
                        && tokens[0].QueryAttribute(AttributeType.Taunt).Value > 0,
                        "Paid support play must put exactly one original Guard token in its owner's hand.");
                    Require(ally.QueryAttribute(AttributeType.Attack).Value == basePower + bonus
                        && foe.QueryAttribute(AttributeType.Attack).Value == basePower
                        && other.QueryAttribute(AttributeType.Attack).Value == 2,
                        "Support aura must affect matching friendly creatures only.");
                    ServerCard later = f.CreateIn(creatureId, me, GameZoneName.CreatureBoard, 1);
                    f.Event(delegate { OfflineAuraRestoration.Apply(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek())); });
                    Require(later.QueryAttribute(AttributeType.Attack).Value == basePower + bonus, "A later creature did not receive the support aura.");
                    f.Event(delegate {
                        ServerCardCatalog.MoveToCemetery(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), support, ServerCardCatalog.MoveOperationType.Untyped);
                        OfflineAuraRestoration.Apply(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()));
                    });
                    Require(ally.QueryAttribute(AttributeType.Attack).Value == basePower && later.QueryAttribute(AttributeType.Attack).Value == basePower,
                        "Support aura remained after the source left play.");
                }
            });
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
