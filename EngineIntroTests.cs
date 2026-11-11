using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Run on an actual settled startup fixture. Uses the installed recipient
    // sanitizer/replay builder, not a hand-written imitation of transport output.
    public static class EngineIntroTests
    {
        public static void AssertStartupHistory(Simulation sim)
        {
            foreach (PlayerServiceClient client in sim.PlayerServiceClients)
            {
                HistoryQueue queue = sim.HistoryLog.GenerateFullReplayForPlayer(client, HistoryLog.FullGameStateSendRule.Exclude);
                int starts = 0, intros = 0, initials = 0, depth = 0;
                bool sawIntro = false;
                foreach (HistoryItem item in queue.Queue)
                {
                    if (HistoryUtilities.IsHeader(item.Type)) depth++;
                    if (item.Type == HistoryType.InitialGameState)
                    {
                        initials++;
                        Require(item.FullGameState != null && item.FullGameState.NeverExclude
                            && item.FullGameState.PlayerStates.Count == 2, "Initial native state was removed by Exclude.");
                        foreach (FullPlayerState player in item.FullGameState.PlayerStates)
                        {
                            foreach (NetDynamicGameZone zone in player.DynamicGameZones)
                                Require(GameManager.Instance.Config.Zones.Find(delegate(GameZone z) { return (int)z.Name == zone.ZoneName; }).Visibility
                                    != GameZone.VisibilityRule.Hidden, "Initial native sanitizer leaked a hidden dynamic zone.");
                            foreach (NetStaticGameZone zone in player.StaticGameZones)
                                Require(GameManager.Instance.Config.Zones.Find(delegate(GameZone z) { return (int)z.Name == zone.ZoneName; }).Visibility
                                    != GameZone.VisibilityRule.Hidden, "Initial native sanitizer leaked a hidden static zone.");
                        }
                    }
                    if (item.Type == HistoryType.EventGameStart) starts++;
                    if (item.Type == HistoryType.ActionMatchIntro)
                    {
                        intros++;
                        Require(depth > 0 && starts == 1, "Native intro must be inside the single game-start event.");
                        Require(item.MatchIntroPlayers.Count == 2, "Intro player descriptions are absent.");
                        for (int p = 0; p < 2; p++)
                        {
                            PlayerState player = sim.Players[p];
                            HistoryPlayerDescription description = item.MatchIntroPlayers[p];
                            Require(description.PlayerInstanceId == player.NetId.Id && description.PlayerName == player.Name
                                && description.IsHuman == player.IsHuman && description.AiOpponentId == player.AiOpponentReference.resourceId
                                && description.NumStartingRunes == player.QueryAttribute(AttributeType.NumStartingRunes).Value
                                && description.InventoryTypes.Count == description.InventoryValues.Count,
                                "Intro player ordering or native metadata differs.");
                            Require(description.DeckColorNames.Count == player.DeckDisplayColors.Count, "Intro deck colors are absent.");
                        }
                        GameZone board = sim.CardCatalog.FindGameZone(sim.Players[0].NetId, GameZoneName.CreatureBoard);
                        Require(item.MatchIntroLanes.Count == board.LaneList.Count, "Intro lane count differs.");
                        for (int lane = 0; lane < item.MatchIntroLanes.Count; lane++)
                        {
                            MessageDynamicCardLane description = item.MatchIntroLanes[lane];
                            Require(description.LaneIndex == lane && description.ZoneName == (int)GameZoneName.CreatureBoard
                                && description.LaneType == (int)board.LaneList[lane].GetCardLaneType()
                                && description.ExtraLaneType == (int)board.LaneList[lane].GetExtraCardLaneType()
                                && description.LaneSize == board.LaneList[lane].GetCapacity() && description.Cards.Count == 0,
                                "Intro lane dimensions or native type differs.");
                        }
                        sawIntro = true;
                    }
                    if (HistoryUtilities.IsAction(item.Type) && item.Type != HistoryType.ActionMatchIntro)
                        Require(sawIntro, "Opening native card action precedes board initialization history.");
                    if (HistoryUtilities.IsFooter(item.Type)) { depth--; Require(depth >= 0, "Unpaired native history footer."); }
                }
                Require(initials == 1 && starts == 1 && intros == 1 && depth == 0,
                    "Startup replay must contain one retained initial state and one balanced intro/game-start event.");
            }
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
