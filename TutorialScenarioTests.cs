using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Act 1 Chapters 1-3 through the native lifecycle under TutorialScenarioRules.
    // Expected values are the 2026-10-19 TESL-Reborn capture (Reborn-observed).
    // The human seat is registered as Normal (so Human/Ai slot masks apply) and is
    // then driven by the reconstructed strategy for an unattended complete match.
    public static class TutorialScenarioTests
    {
        private sealed class Expect
        {
            public int Id; public bool HumanFirst; public int[] HumanDraws; public int[] AiDraws; public int AiBoardCard;
        }

        public static void Run(GameConfiguration config, Action<string, Action> test, string directory)
        {
            Expect[] cases = {
                new Expect { Id = 1595477420, HumanFirst = false,
                    HumanDraws = new int[] { 494443404, 1128875378, -741858815, 844499977, 1717577005 },
                    AiDraws = new int[] { 1714445403, -1734732697, 1714445403, -1866183707, -675856486 } },
                new Expect { Id = 740282472, HumanFirst = true,
                    HumanDraws = new int[] { 1193892832, 899518525, 1128875378, 1717577005, 373560332, 494443404, -1767873507, 1744549196 },
                    AiDraws = new int[] { -1892047993 } },
                new Expect { Id = 1722475851, HumanFirst = false,
                    HumanDraws = new int[] { 494443404, -452943738, 1128875378, 438381503, -741858815, 1449346166 },
                    AiDraws = new int[] { 175622366 }, AiBoardCard = 741809568 },
            };
            foreach (Expect expect in cases)
            {
                Expect e = expect;
                TutorialScenarioRules.Chapter chapter = TutorialScenarioRules.Find(e.Id);
                test("tutorial-" + chapter.Name.ToLowerInvariant() + "-reborn-observed-setup-and-complete-match", delegate { RunChapter(config, chapter, e, directory); });
            }
            // Regression (2026-10-19 playtest): killed creatures stayed on the client board at
            // 0 health because the server sent ActionDeath, which no client sequence handles.
            test("board-creature-death-history-is-add-to-discard-then-move-cards", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard card = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 0, f.Two);
                int uid = card.NetUid.Id;
                int before = Messages(f.State.Log).Count;
                DecisionRestoration.DiscardCards(f.State, new List<ServerCard> { card }, -1, false);
                Require(card.CurrentGameZoneType == GameZoneType.Deathrattle, "Dying creature did not enter its Deathrattle zone.");
                int discard = -1, move = -1;
                for (int i = before; i < Messages(f.State.Log).Count; i++)
                {
                    HistoryItem item = Messages(f.State.Log)[i];
                    Require(item.Type != HistoryType.ActionDeath, "Creature death emitted ActionDeath, which the client cannot render.");
                    if (item.Type == HistoryType.ActionAddToDiscard && discard < 0) discard = i;
                    if (item.Type == HistoryType.ActionMoveCards && item.ActorCardNetUid == uid) move = i;
                }
                Require(discard >= 0 && move > discard, "Creature death must emit AddToDiscard followed by MoveCards of the dying instance.");
                HistoryItem moved = Messages(f.State.Log)[move];
                Require(moved.AffectedCardsArray.Count == 1 && moved.AffectedCardsArray[0].CardNetUid == uid
                    && moved.AffectedCardsArray[0].CurrentPosition.GameZoneType == (int)GameZoneType.Deathrattle,
                    "MoveCards must carry the dying instance at its Deathrattle position.");
            });
            test("tutorial-scope-restores-original-wrappers-and-rejects-ordinary-overlap", delegate {
                Scenario scenario = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.Id == 1595477420; });
                List<AvatarOverride> avatars = scenario.AvatarOverrides; List<PlayerOverride> players = scenario.PlayerOverrides;
                LaneTrigger[] lanes = config.LaneTriggers;
                using (TutorialScenarioRules.Prepare(config, 1595477420))
                {
                    bool rejected = false;
                    try { OrdinaryMatchRules.Prepare(config); } catch (InvalidOperationException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Ordinary rules scope opened while a tutorial scope was active.");
                }
                Require(Object.ReferenceEquals(avatars, scenario.AvatarOverrides) && Object.ReferenceEquals(players, scenario.PlayerOverrides)
                    && Object.ReferenceEquals(lanes, config.LaneTriggers) && !TutorialScenarioRules.Active, "Tutorial scope did not restore original wrappers.");
                bool unknown = false;
                try { TutorialScenarioRules.Prepare(config, OrdinaryMatchRules.DefaultScenarioId); } catch (NotSupportedException) { unknown = true; }
                Require(unknown, "Tutorial scope accepted a non-tutorial scenario.");
            });
        }

        private static void RunChapter(GameConfiguration config, TutorialScenarioRules.Chapter chapter, Expect e, string directory)
        {
            Simulation sim = new Simulation();
            StringBuilder evidence = new StringBuilder("field\tvalue\nscenario\t" + chapter.Name + "\nprovenance\tReborn-observed capture 20261019-164152\n");
            string file = Path.Combine(directory, "tutorial-" + chapter.Name + ".tsv");
            using (TutorialScenarioRules.Prepare(config, chapter.ScenarioId))
            {
                try
                {
                    MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo { ScenarioId = chapter.ScenarioId, RandomNumberGenerator = new RandomIntegerGenerator(271828) };
                    for (int slot = 0; slot < 2; slot++)
                    {
                        bool human = slot == 0;
                        PlayerDescription description = new PlayerDescription { PlayerName = human ? "Tutorial human" : "Tutorial AI",
                            AvatarCardId = OrdinaryMatchRules.BaseAvatarId, AiOpponentId = UniqueResourceId.GetInvalid().Id };
                        // Unrelated fallback; the scenario Deck zone override must replace it.
                        for (int i = 0; i < 12; i++) { description.Deck.Add(157797006); description.Premium.Add(false); }
                        info.Users[slot] = new MatchConfiguration.User { CustomDeck = description, Type = human ? PlayerType.Normal : PlayerType.AiOpponent,
                            EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "tutorial-test-" + slot, SpectatorKey = "tutorial-observer-" + slot };
                    }
                    TutorialScenarioRules.ValidateStart(info);
                    LifecycleRestoration.Start(sim, null, info);
                    PlayerState humanPlayer = sim.Players.Find(delegate(PlayerState p) { return p.IsHuman; });
                    PlayerState aiPlayer = sim.Players.Find(delegate(PlayerState p) { return !p.IsHuman; });
                    Require(humanPlayer != null && aiPlayer != null, "Tutorial needs one human and one AI seat.");
                    Require((sim.Players[0] == humanPlayer) == e.HumanFirst, "First player differs from capture.");
                    foreach (PlayerState player in sim.Players)
                    {
                        List<ServerCard> avatars = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.AvatarDeck);
                        ServerCard avatar = avatars.Count == 1 ? avatars[0] : null;
                        Require(avatar != null && avatar.CardId == chapter.AvatarId, "Tutorial avatar card differs from captured avatar zone card.");
                        Require(avatar.QueryAttribute(AttributeType.StartingHealth).Value == chapter.Health, "Tutorial starting health differs from capture.");
                        Require(player.QueryAttribute(AttributeType.NumRunes).Value == chapter.Runes
                            && player.QueryAttribute(AttributeType.NumStartingRunes).Value == chapter.Runes, "Tutorial rune count differs from capture.");
                        bool ring = chapter.SecondPlayerRing && player == sim.Players[1];
                        Require(player.QueryAttribute(AttributeType.RingUsesLeft).Value == (ring ? 3 : 0), "Tutorial ring charges differ from capture.");
                        GameZone board = sim.CardCatalog.FindGameZone(player.NetId, GameZoneName.CreatureBoard);
                        Require(board.LaneList.Count == 2 && board.LaneList[0].GetCapacity() == 4 && board.LaneList[1].GetCapacity() == 4, "Tutorial lanes differ.");
                    }
                    AssertPrefix(humanPlayer.StartingDeck, e.HumanDraws, "human");
                    AssertPrefix(aiPlayer.StartingDeck, e.AiDraws, "AI");
                    if (e.AiBoardCard != 0)
                        for (int lane = 0; lane < 2; lane++)
                        {
                            List<ServerCard> cards = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CurrentLaneIndex == lane; }, aiPlayer.NetId, GameZoneName.CreatureBoard);
                            Require(cards.Count == 1 && cards[0].CardId == e.AiBoardCard, "Captured pre-placed AI lane card is missing in lane " + lane + ".");
                        }
                    // Evidence: initial client-facing state, for comparison with the Reborn capture.
                    {
                        Google.Protobuf.JsonFormatter json = new Google.Protobuf.JsonFormatter(new Google.Protobuf.JsonFormatter.Settings(true));
                        StringBuilder state = new StringBuilder();
                        state.AppendLine(json.Format(sim.CreateFullGameState()));
                        foreach (PlayerState player in sim.Players) state.AppendLine(json.Format(player.HistoryCreate_FullPlayerDescription(sim.CardCatalog)));
                        File.WriteAllText(Path.Combine(directory, "tutorial-" + chapter.Name + "-initial-state.json"), state.ToString());
                    }
                    // TEST: let the reconstructed strategy drive the human seat.
                    // No transport exists here, so the human client would wait forever in GatherPlayers.
                    humanPlayer.IsHuman = false;
                    int seat = sim.Players.IndexOf(humanPlayer);
                    sim.PlayerServiceClients[seat].Stop();
                    sim.PlayerServiceClients[seat] = new PlayerServiceClient(false, humanPlayer.NetId, new byte[32],
                        "tutorial-test-human", "", null, false, false, false);
                    int ticks = 0, ringUses = 0, lastRing = humanPlayer.QueryAttribute(AttributeType.RingUsesLeft).Value;
                    while (ticks < 20000 && sim._gameState.GamePhase != GamePhase.Results)
                    {
                        LifecycleRestoration.FixedUpdate(sim); ticks++;
                        int now = humanPlayer.QueryAttribute(AttributeType.RingUsesLeft).Value;
                        if (now < lastRing) ringUses += lastRing - now; lastRing = now;
                    }
                    evidence.AppendLine("ticks\t" + ticks).AppendLine("terminal_phase\t" + sim._gameState.GamePhase)
                        .AppendLine("human_first\t" + e.HumanFirst).AppendLine("avatar\t" + chapter.AvatarId).AppendLine("health\t" + chapter.Health)
                        .AppendLine("runes\t" + chapter.Runes).AppendLine("ring_uses_human\t" + ringUses)
                        .AppendLine("winner_is_human_seat\t" + (sim.WinnerId == humanPlayer.NetId));
                    for (int i = 0; i < 2; i++) evidence.AppendLine("player_" + i + "_turns\t" + sim.Players[i].QueryAttribute(AttributeType.TurnCount).Value)
                        .AppendLine("player_" + i + "_runes_lost\t" + sim.Players[i].QueryAttribute(AttributeType.NumRunesLost).Value);
                    Require(sim._gameState.GamePhase == GamePhase.Results, chapter.Name + " did not finish within 20,000 native lifecycle ticks.");
                    Require(sim.WinnerId != sim.LoserId, chapter.Name + " produced an invalid native result.");
                    Require(sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value > 1, chapter.Name + " ended before alternating gameplay.");
                    if (chapter.Runes == 0) foreach (PlayerState p in sim.Players) Require(p.QueryAttribute(AttributeType.NumRunesLost).Value == 0, "Runeless tutorial broke a rune.");
                    evidence.AppendLine("status\tPASS");
                    File.WriteAllText(file, evidence.ToString());
                }
                catch (Exception error)
                {
                    evidence.AppendLine("status\tFAIL").AppendLine("terminal_phase\t" + sim._gameState.GamePhase)
                        .AppendLine("error\t" + error.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                    File.WriteAllText(file, evidence.ToString());
                    throw;
                }
                finally
                {
                    RuneProphecyRestoration.Clear(sim);
                    if (sim.PlayerServiceClients != null) foreach (PlayerServiceClient client in sim.PlayerServiceClients) if (client != null) client.Stop();
                }
            }
        }

        private static void AssertPrefix(List<int> deck, int[] expected, string who)
        {
            Require(deck.Count >= expected.Length, "Tutorial " + who + " deck is shorter than captured draws.");
            for (int i = 0; i < expected.Length; i++)
                Require(deck[i] == expected[i], "Tutorial " + who + " draw order differs from capture at " + i + ": " + deck[i] + " vs " + expected[i]);
        }
        private static List<HistoryItem> Messages(HistoryLog log)
        { return (List<HistoryItem>)HarmonyLib.AccessTools.Field(typeof(HistoryLog), "_messageLog").GetValue(log); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
