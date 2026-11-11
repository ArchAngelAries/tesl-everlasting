using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Renowned Legate resolves through paid native decisions. Direct actions only prepare the fixture.
    public static class OriginalLegateTests
    {
        private const int Legate = 1964907304, ImperialGrunt = 1150098037, Mudcrab = 620729069;

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "empty-board-counts-source-and-new-grunt", 0, 0, 0, 0, 0, 1, 2, 0);
            Case(config, test, "both-lane-allies-enemies-excluded", 1, 2, 1, 2, 1, 1, 5, 1);
            // Original graph (data/original-rules): CreateToken InsertPosition SameLane with UseOtherLaneIfFull, so a full
            // source lane sends the Grunt to the other lane (corrects the checkpoint-25 "no spill" reconstruction).
            Case(config, test, "full-source-lane-spills-to-other-lane", 2, 3, 1, 1, 1, 1, 7, 0);
        }

        private static void Case(GameConfiguration config, Action<string, Action> test, string label,
            int leftAllies, int rightAllies, int sourceLane, int leftEnemies, int rightEnemies,
            int expectedGrunts, int expectedHeal, int gruntLane)
        {
            test("legate-" + label + "-paid-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Legate), "Renowned Legate was not emitted: " + OriginalSpecRules.SkipReason(Legate));
                    for (int n = 0; n < leftAllies; n++) f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                    for (int n = 0; n < rightAllies; n++) f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 1);
                    for (int n = 0; n < leftEnemies; n++) f.CreateIn(Mudcrab, enemy, GameZoneName.CreatureBoard, 0);
                    for (int n = 0; n < rightEnemies; n++) f.CreateIn(Mudcrab, enemy, GameZoneName.CreatureBoard, 1);
                    int setupHand = f.HandCount(me), setupDeck = f.DeckCount(me);
                    int setupRunes = f.Sim.Players[me].QueryAttribute(AttributeType.NumRunes).Value;
                    f.Event(delegate {
                        PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, f.Sim.Players[me], 10, -1);
                    });
                    Require(f.Avatar(me).CurrentHealth == 20, "Legate fixture must start at 20 health.");
                    // The setup damage crosses two runes. Finish their native draws before
                    // measuring the paid play, so setup draws cannot change its hand delta.
                    f.Until(delegate { return f.Sim.Players[me].QueryAttribute(AttributeType.NumRunes).Value == setupRunes - 2
                        && f.DeckCount(me) == setupDeck - 2 && f.HandCount(me) == setupHand + 2
                        && OriginalTargetedSummonTests.Ready(f.Sim); });
                    ServerCard source = f.Create(Legate, me, GameZoneName.Hand);
                    int handBefore = f.HandCount(me);
                    f.Mana(me, 30); f.Refresh(me);
                    Require(source.QueryAttribute(AttributeType.ManaCost).Value == 7, "Installed Legate cost changed.");
                    f.PlayCreatureLane(source, sourceLane, me);

                    Require(f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 23
                        && source.QueryGameJournalCount(CardJournal.Played) == 1 && f.HandCount(me) == handBefore - 1,
                        "Legate must resolve exactly one paid seven-magicka play: mana="
                        + f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value + "/23, played="
                        + source.QueryGameJournalCount(CardJournal.Played) + "/1, hand="
                        + f.HandCount(me) + "/" + (handBefore - 1) + ".");
                    Require(source.CurrentGameZoneName == GameZoneName.CreatureBoard && source.CurrentLaneIndex == sourceLane
                        && source.OwnerNetId == f.Sim.Players[me].NetId
                        && source.QueryAttribute(AttributeType.Attack).Value == 5 && source.CurrentHealth == 5
                        && source.QueryAttribute(AttributeType.Breakthrough).Value > 0,
                        "Legate lost its installed owner, lane, 5/5 body or Breakthrough.");
                    List<ServerCard> grunts = Cards(f, me, ImperialGrunt);
                    Require(grunts.Count == expectedGrunts, "Legate summoned the wrong number or identity of Imperial Grunts.");
                    foreach (ServerCard grunt in grunts)
                        Require(grunt.OwnerNetId == f.Sim.Players[me].NetId && grunt.CurrentLaneIndex == gruntLane
                            && grunt.QueryAttribute(AttributeType.Attack).Value == 1 && grunt.CurrentHealth == 1
                            && grunt.QueryAttribute(AttributeType.MaxHealth).Value == 1,
                            "Legate's exact 1/1 Imperial Grunt must enter its source lane, or the other lane when that is full.");
                    Require(f.LaneCount(me, 0) == leftAllies + (sourceLane == 0 ? 1 : 0) + (gruntLane == 0 ? expectedGrunts : 0)
                        && f.LaneCount(me, 1) == rightAllies + (sourceLane == 1 ? 1 : 0) + (gruntLane == 1 ? expectedGrunts : 0)
                        && Cards(f, me, Mudcrab).Count == leftAllies + rightAllies,
                        "Legate placed its Grunt in the wrong lane or changed existing allies.");
                    Require(f.BoardCount(me) == leftAllies + rightAllies + 1 + expectedGrunts
                        && f.Avatar(me).CurrentHealth == 20 + expectedHeal,
                        "Legate must heal after its summon, counting itself and every surviving friendly creature in both lanes. Got "
                        + f.Avatar(me).CurrentHealth + " health; expected " + (20 + expectedHeal) + ".");
                    Require(f.LaneCount(enemy, 0) == leftEnemies && f.LaneCount(enemy, 1) == rightEnemies
                        && Cards(f, enemy, ImperialGrunt).Count == 0 && f.Avatar(enemy).CurrentHealth == 30,
                        "Legate must neither count nor change enemy creatures or heal the opposing player.");
                }
            });
        }

        private static List<ServerCard> Cards(OriginalTargetedSummonTests.LifecycleFixture f, int owner, int id)
        { return f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == id; }, f.Sim.Players[owner].NetId, GameZoneName.CreatureBoard); }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
