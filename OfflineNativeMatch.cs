using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using CcgRpc;
using HarmonyLib;
using SPTESL;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Native matching on local services: the original client path
    //   matching/reserve -> matching/add -> matching/refresh (gameReadyPlayerClient)
    //   -> GameNetworkManager.ProcessMatchReady -> TCP login -> original board.
    // The game server is the client's own embedded TcpNetworkServer + Simulation, started
    // on the main thread exactly as GameNetworkManager.StartSinglePlayerMatch builds it,
    // but keyed by the matching requestId (the human SecretKey) and placed in the
    // manager's own _tcpServer/_embeddedMatchServer fields, so the original FixedUpdate
    // drives it and EndConnectionToServer stops it. The listener binds loopback only.
    // Rules: the engine restorations (as OfflinePracticeSession.Initialize) and, for the
    // story tutorial chapters, TutorialScenarioRules.Prepare for campaignData.scenarioName.
    public static class OfflineNativeMatch
    {
        public const int Port = 4001;
        private static readonly object gate = new object();
        private static OfflinePlayerState state;
        private static string exportDirectory;
        private static string reservedId;
        private static Matching.ClientMatchRequest pending;
        private static Matching.MatchReady ready;
        private static string failure;
        private static readonly ManualResetEvent settled = new ManualResetEvent(false);
        private static bool engineInstalled;
        private static bool teardownRequested;
        private static IDisposable scope;
        private static OfflineDeckCatalog.Entry defaultDeck;

        public static Simulation CurrentSimulation { get; private set; }
        public static OfflinePlayerState State { get { return state; } }
        public static int CurrentChapterEventId { get; private set; }
        public static string CurrentScenarioName { get; private set; }
        public static bool CurrentMastery { get; private set; }
        public static string CurrentMatchId { get; private set; }
        public static int CurrentArenaIndex { get; private set; }
        public static long CurrentEventId { get; private set; }

        // An event plan's game mode (modes.json object), parsed as a mod mode would be.
        private static OfflineModes.Mode EventMode(EverlastingHost.EventMatchPlan plan)
        {
            if (plan == null || string.IsNullOrEmpty(plan.ModeJson)) return null;
            List<string> problems = new List<string>();
            FullSerializer.fsData data = FullSerializer.fsJsonParser.Parse(plan.ModeJson);
            OfflineModes.Mode m = data.IsDictionary ? OfflineModes.Parse(data.AsDictionary, "event", "event", problems) : null;
            foreach (string p in problems) Debug.LogWarning("OFFLINE NATIVE MATCH: event mode: " + p);
            return m;
        }
        public static string CurrentArenaType { get; private set; }
        public static string CurrentRatingMode { get; private set; }
        public static string CurrentXpMode { get; private set; }
        public static string LastResult { get; private set; }
        public static int ResultsRecorded { get; private set; }
        public static string ResultHistory = "";
        private static bool resultRecorded;

        public static void Register(OfflineLocalServices services, OfflinePlayerState playerState, string directory, Harmony harmony, MonoBehaviour host)
        {
            state = playerState;
            exportDirectory = directory;
            // The original listener binds IPAddress.Any; the offline game server is loopback only.
            harmony.Patch(AccessTools.Constructor(typeof(TcpNetworkServer), new Type[] { typeof(int) }), null, null,
                new HarmonyMethod(typeof(OfflineNativeMatch), "LoopbackListener"), null, null);
            services.Register("matching/reserve", Reserve);
            services.Register("matching/add", Add);
            services.Register("matching/refresh", Refresh);
            services.Register("matching/cancel", Cancel);
            host.gameObject.AddComponent<OfflineNativeMatchPump>();
        }

        private static IEnumerable<CodeInstruction> LoopbackListener(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo loopback = AccessTools.Field(typeof(IPAddress), "Loopback");
            int replaced = 0; System.Text.StringBuilder seen = new System.Text.StringBuilder();
            foreach (CodeInstruction instruction in instructions)
            {
                seen.Append(instruction.opcode).Append(' ').Append(instruction.operand).Append("; ");
                FieldInfo field = instruction.operand as FieldInfo;
                if (instruction.opcode == OpCodes.Ldsfld && field != null && field.Name == "Any" && field.DeclaringType != null && field.DeclaringType.FullName == "System.Net.IPAddress")
                {
                    // Never mutate: HarmonyX may run a transpiler more than once.
                    CodeInstruction replacement = new CodeInstruction(OpCodes.Ldsfld, loopback);
                    replacement.labels.AddRange(instruction.labels); replacement.blocks.AddRange(instruction.blocks);
                    replaced++; yield return replacement; continue;
                }
                if (instruction.opcode == OpCodes.Ldsfld && field != null && field.Name == "Loopback" && field.DeclaringType == typeof(IPAddress)) replaced++;
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("TcpNetworkServer listener shape differs; expected one IPAddress.Any. IL: " + seen);
        }

        private static string Reserve(string method, string path, string body)
        {
            Matching.ClientMatchReserveRequestResult result = new Matching.ClientMatchReserveRequestResult();
            lock (gate)
            {
                reservedId = Guid.NewGuid().ToString();
                pending = null; ready = null; failure = null; settled.Reset();
                result.requestId = reservedId;
            }
            result.resultCode = ResultCode.Ok;
            return JsonUtility.ToJson(result);
        }

        private static string Add(string method, string path, string body)
        {
            Matching.ClientMatchRequest request = JsonUtility.FromJson<Matching.ClientMatchRequest>(body);
            Matching.ClientMatchRequestResult result = new Matching.ClientMatchRequestResult();
            lock (gate)
            {
                if (request == null || string.IsNullOrEmpty(request.requestId) || request.requestId != reservedId)
                    return Error(ResultCode.ClientHttpErrorBadRequest);
                bool campaign = request.matchMode == Matching.IMatchMode.Campaign && request.campaignData != null && !string.IsNullOrEmpty(request.campaignData.scenarioName);
                bool eventBattle = request.matchMode == Matching.IMatchMode.EventBattle && EverlastingHost.EventMatchProvider != null;
                if (!campaign && !eventBattle && request.matchMode != Matching.IMatchMode.PracticeBattle && request.matchMode != Matching.IMatchMode.SoloArena && request.matchMode != Matching.IMatchMode.VersusBattle && request.matchMode != Matching.IMatchMode.VersusArena)
                {
                    Debug.LogWarning("OFFLINE NATIVE MATCH: unsupported match request mode " + (request == null ? "?" : request.matchMode.ToString()));
                    return Error(ResultCode.InternalError);
                }
                pending = request; ready = null; failure = null; settled.Reset();
            }
            // The main thread starts the server; the client polls refresh meanwhile.
            if (!settled.WaitOne(30000)) { lock (gate) teardownRequested = true; return Error(ResultCode.InternalError); }
            lock (gate) if (failure != null || ready == null) return Error(ResultCode.InternalError);
            result.resultCode = ResultCode.Ok;
            result.requestId = request.requestId;
            result.poolName = "offline-local";
            result.estimatedWaitSeconds = 1;
            result.refreshPeriodSeconds = 3;
            return JsonUtility.ToJson(result);
        }

        [Serializable]
        private class RefreshRequest { public string requestId; public string poolName; }

        [Serializable]
        private class RefreshResult : HTTPUtils.BaseResult
        {
            public int estimatedWaitSeconds;
            public Matching.MatchReady gameReadyPlayerClient;
        }

        private static string Refresh(string method, string path, string body)
        {
            RefreshRequest request = JsonUtility.FromJson<RefreshRequest>(body);
            settled.WaitOne(30000);
            lock (gate)
            {
                if (request == null || ready == null || request.requestId != ready.requestId) return Error(ResultCode.InternalError);
                RefreshResult result = new RefreshResult();
                result.resultCode = ResultCode.Ok; result.estimatedWaitSeconds = 0; result.gameReadyPlayerClient = ready;
                return JsonUtility.ToJson(result);
            }
        }

        private static string Cancel(string method, string path, string body)
        {
            // A server started for this request but never joined is torn down on the main thread.
            lock (gate) { pending = null; reservedId = null; ready = null; teardownRequested = true; }
            return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.Ok });
        }

        private static string Error(ResultCode code) { return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = code }); }

        // Main thread, once per frame.
        internal static void Pump()
        {
            Matching.ClientMatchRequest request;
            lock (gate) { request = pending; pending = null; }
            if (request != null)
            {
                Matching.MatchReady started = null; string error = null;
                try { started = StartServer(request); }
                catch (Exception ex) { error = ex.Message; Debug.LogError("OFFLINE NATIVE MATCH: start failed: " + ex); }
                lock (gate) { ready = started; failure = error; }
                settled.Set();
            }
            if (CurrentSimulation != null && !resultRecorded) OfflineTitleFeats.Sample(CurrentSimulation); // per-game title feats
            RecordResultOnce();
            bool teardown;
            lock (gate) { teardown = teardownRequested; teardownRequested = false; }
            if (teardown) Teardown();
            // After a match the client has already stopped the server (EndConnectionToServer):
            // release the scenario's rule scope once the client is back outside a match.
            if (CurrentSimulation != null && resultRecorded && GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsOutsideOfMatch()
                && AccessTools.Field(typeof(GameNetworkManager), "_embeddedMatchServer").GetValue(GameNetworkManager.Instance) == null)
            {
                CurrentSimulation = null;
                if (scope != null) { scope.Dispose(); scope = null; }
            }
        }

        // Diagnostic: start (and immediately stop) the native server for every installed campaign scenario, exactly as
        // matching/add would, so every chapter's setup (decks, lanes, avatars, starting boards) is exercised.
        public static System.Collections.IEnumerator SweepScenarios(string reportPath)
        {
            System.Text.StringBuilder rows = new System.Text.StringBuilder("campaign\tchapter\tevent\tscenario\tresult\n");
            int ok = 0, failed = 0;
            List<SPTESL.Campaign> campaigns = OfflineCampaignService.Content;
            if (campaigns != null)
                foreach (SPTESL.Campaign campaign in campaigns)
                    foreach (SPTESL.Act act in campaign.Acts)
                        foreach (SPTESL.Chapter chapter in act.Chapters)
                            foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                                foreach (SPTESL.GatedScenario gated in e.GatedScenarios)
                                {
                                    Scenario s = GameManager.Instance.FindScenario(gated.Scenario.resourceId);
                                    string result;
                                    if (s == null) result = "MISSING scenario " + gated.Scenario.resourceId;
                                    else
                                    {
                                        Matching.ClientMatchRequest request = new Matching.ClientMatchRequest();
                                        request.requestId = Guid.NewGuid().ToString("N");
                                        request.matchMode = Matching.IMatchMode.Campaign;
                                        request.campaignData = new Matching.ICampaignMatchDesc { chapterEventId = e.ResourceId.Id, scenarioName = s.ResourceId.UniqueName };
                                        try { StartServer(request); result = "OK"; ok++; }
                                        catch (Exception ex) { result = "FAIL " + ex.GetType().Name + ": " + ex.Message.Replace('\t', ' ').Replace('\n', ' '); failed++; }
                                        Teardown();
                                    }
                                    rows.Append(campaign.ResourceId.UniqueName).Append('\t').Append(chapter.ResourceId.UniqueName).Append('\t').Append(e.ResourceId.UniqueName)
                                        .Append('\t').Append(s == null ? "-" : s.ResourceId.UniqueName).Append('\t').Append(result).Append('\n');
                                    yield return null;
                                }
            System.IO.File.WriteAllText(reportPath, rows.ToString());
            Debug.Log("OFFLINE SCENARIO SWEEP: " + ok + " started, " + failed + " failed; report " + reportPath);
        }

        // Stops a server/simulation the client never joined (cancelled or timed-out request).
        private static void Teardown()
        {
            GameNetworkManager network = GameNetworkManager.Instance;
            if (network == null || !network.IsOutsideOfMatch()) return;
            FieldInfo serverField = AccessTools.Field(typeof(GameNetworkManager), "_tcpServer");
            FieldInfo simField = AccessTools.Field(typeof(GameNetworkManager), "_embeddedMatchServer");
            TcpNetworkServer server = (TcpNetworkServer)serverField.GetValue(network);
            Simulation sim = (Simulation)simField.GetValue(network);
            if (sim != null) sim.Stop();
            if (server != null) server.Stop();
            serverField.SetValue(network, null); simField.SetValue(network, null);
            if (sim != null || server != null) Debug.Log("OFFLINE NATIVE MATCH: unjoined embedded server stopped.");
            CurrentSimulation = null; resultRecorded = true; OfflineModes.End();
            if (scope != null) { scope.Dispose(); scope = null; }
        }

        private static Matching.MatchReady StartServer(Matching.ClientMatchRequest request)
        {
            GameNetworkManager network = GameNetworkManager.Instance;
            if (network == null || !network.IsOutsideOfMatch()) throw new InvalidOperationException("A native match is already active.");
            GameConfiguration config = GameManager.Instance.Config;
            // The bundled binary configuration is already complete; EnsureFullConfigurationLoad
            // would read the developer source tree (absent in shipped installs).
            if (config == null || !config.initialized) throw new InvalidOperationException("Installed configuration is not loaded.");
            EnsureEngine(config);
            // Versus has no human opponents offline: it is served as an ordinary match against an Expert practice AI deck.
            // Client: Ranked = VersusBattle (Multi); Casual = PracticeBattle with match type Multi; Practice = PracticeBattle (Solo).
            bool versus = request.matchMode == Matching.IMatchMode.VersusBattle;
            bool casual = request.matchMode == Matching.IMatchMode.PracticeBattle && request.matchType == Matching.IMatchType.Multi;
            // An extension's event battle (EverlastingHost.EventMatchProvider): played as a practice match with its plan.
            bool eventBattle = request.matchMode == Matching.IMatchMode.EventBattle;
            EverlastingHost.EventMatchPlan plan = null;
            if (eventBattle)
            {
                plan = EverlastingHost.EventMatchProvider == null ? null : EverlastingHost.EventMatchProvider(request.eventId, request.deckId);
                if (plan == null) throw new InvalidOperationException("No installed expansion runs event " + request.eventId + ".");
                request.aiDifficulty = plan.Difficulty == "Novice" ? Matching.IAiDifficulty.Novice : plan.Difficulty == "Adept" ? Matching.IAiDifficulty.Adept : Matching.IAiDifficulty.Expert;
            }
            bool practice = request.matchMode == Matching.IMatchMode.PracticeBattle || versus || eventBattle;
            if (versus || casual) request.aiDifficulty = Matching.IAiDifficulty.Expert;
            CurrentXpMode = versus || eventBattle ? "ranked" : casual ? "casual" : practice ? "practice" : request.matchMode == Matching.IMatchMode.VersusArena ? "versusArena"
                : request.matchMode == Matching.IMatchMode.SoloArena ? "soloArena"
                : request.campaignData != null && request.campaignData.scenarioName != null && request.campaignData.scenarioName.StartsWith("PUZ_") ? "puzzle" : "story";
            CurrentRatingMode = versus ? OfflineRankedService.RankedPvP : casual ? OfflineRankedService.UnrankedPvP
                : request.matchMode == Matching.IMatchMode.VersusArena ? OfflineRankedService.VersusArena : null;
            bool versusArena = request.matchMode == Matching.IMatchMode.VersusArena;
            bool arena = request.matchMode == Matching.IMatchMode.SoloArena || versusArena;
            string arenaType = versusArena ? OfflineArenaService.Versus : OfflineArenaService.Solo;
            List<int> arenaCards = null; Arena.ArenaClientMatch arenaMatch = null;
            if (arena && !OfflineArenaService.MatchSetup(state.Snapshot(), arenaType, request.arenaMatchOrderIndex, out arenaCards, out arenaMatch))
                throw new InvalidOperationException("No playable " + arenaType + " arena match " + request.arenaMatchOrderIndex);
            bool boss = arena && arenaMatch.scenarioTypeHash != UniqueResourceId.GetInvalid().Id;
            string scenarioName = practice || (arena && !boss) ? "Default" : arena ? null : request.campaignData.scenarioName;
            Scenario scenario = boss ? GameManager.Instance.FindScenario(arenaMatch.scenarioTypeHash)
                : config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.UniqueName == scenarioName; });
            if (scenario == null) throw new InvalidOperationException("Scenario is absent from the installed configuration: " + scenarioName);
            if (scope != null) { scope.Dispose(); scope = null; }
            TutorialScenarioRules.Chapter chapter = practice ? null : TutorialScenarioRules.Find(scenario.ResourceId.Id);
            // Tutorial chapters keep their Reborn-observed values; later story scenarios use the
            // ordinary-rules story scope and, where the scenario allows it, the player's deck.
            // Practice: the Default scenario under ordinary rules, chosen deck vs a practice AI deck.
            // A mod story battle (OfflineStories): its own scenario, played with its game-mode setup.
            OfflineStories.Battle storyBattle = !practice && !arena ? OfflineStories.FindBattle(scenario.ResourceId.UniqueName) : null;
            OfflineModes.Mode mode = storyBattle != null ? storyBattle.Mode : eventBattle ? EventMode(plan) : practice && !versus && !casual ? OfflineModes.Selected : null;
            OfflineModes.Begin(mode);
            if (mode != null) Debug.Log("OFFLINE MODES: " + (storyBattle != null ? "story battle" : "practice match") + " plays mode " + mode.Key);
            if (mode != null && mode.NoMulligan && storyBattle == null) OfflineModes.DisableMulligan(scenario.GameProperties);
            scope = mode != null ? StoryScenarioRules.Prepare(config, scenario.ResourceId.Id) : practice ? OrdinaryMatchRules.Prepare(config) : arena ? StoryScenarioRules.Prepare(config, scenario.ResourceId.Id) : chapter != null ? TutorialScenarioRules.Prepare(config, scenario.ResourceId.Id) : StoryScenarioRules.Prepare(config, scenario.ResourceId.Id);
            Deck playerDeck = arena ? CardsDeck(versusArena ? "Versus Arena" : "Solo Arena", arenaCards) : practice || (chapter == null && scenario.ScenarioAllowsUserDeckSelection()) ? PlayerDeck(request.deckId) : null;
            CustomDeck practiceOpponent = practice ? PracticeOpponent(config, request.aiDifficulty)
                : arena ? config.CustomDecks.Find(delegate(CustomDeck x) { return x.ResourceId.Id == arenaMatch.deckTypeHash; }) : null;
            if (arena && practiceOpponent == null) throw new InvalidOperationException("Arena opponent deck is absent: " + arenaMatch.deckTypeHash);
            Deck aiDeck = practiceOpponent == null ? null : AiDeck(config, practiceOpponent, mode != null ? ModeDifficulty(mode, request.aiDifficulty) : request.aiDifficulty);
            if (storyBattle != null)
            {   // the opponent plays a practice AI deck at the battle's difficulty unless the battle names a deck
                Matching.IAiDifficulty level = ModeDifficulty(mode, Matching.IAiDifficulty.Adept);
                CustomDeck source = PracticeOpponent(config, level);
                if (source != null) aiDeck = AiDeck(config, source, level);
            }
            // A mod AI opponent (OfflineOpponents): named by the mode/story battle, or picked for Practice.
            bool aiDeckNamed = storyBattle != null;   // something other than a practice AI deck decides the AI's deck
            OfflineOpponents.Opponent opp = eventBattle ? null : mode != null ? OfflineOpponents.Find(mode.AiRef) : practice && !versus && !casual && !arena ? OfflineOpponents.PickForPractice() : null;
            OfflineOpponents.Current = opp;
            if (opp != null)
            {
                aiDeckNamed = true;
                bool battleNamesDeck = mode != null && mode.Opponent.Deck != null && mode.Opponent.Deck != "own" && mode.Opponent.Deck != "practice";
                List<int> oppCards = battleNamesDeck ? null : OfflineOpponents.DeckCards(config, opp);
                if (oppCards != null && oppCards.Count > 0) aiDeck = CardsDeck(opp.Name, oppCards);
                else if (practiceOpponent != null && opp.Difficulty != "chosen")
                {
                    Matching.IAiDifficulty level = ModeDifficulty(new OfflineModes.Mode { Difficulty = opp.Difficulty }, request.aiDifficulty);
                    aiDeck = AiDeck(config, practiceOpponent, level);
                }
                Debug.Log("OFFLINE OPPONENTS: " + opp.Key + " (" + opp.Name + ") plays " + (oppCards != null && oppCards.Count > 0 ? opp.Deck : "a practice deck"));
            }
            else if (practice && !versus && !casual && !eventBattle && mode == null)
            {
                DeckInfo modDeck = OfflineOpponents.RandomModDeckForAi();
                if (modDeck != null && modDeck.Cards != null)
                {
                    List<int> cards = new List<int>();
                    foreach (DeckInfo.CardInfo c in modDeck.Cards) for (int n = 0; n < Math.Max(1, c.Count); n++) cards.Add(c.Id);
                    aiDeck = CardsDeck(OfflineMods.Text(modDeck.DisplayName) ?? modDeck.TypeName, cards); aiDeckNamed = true;
                    Debug.Log("OFFLINE OPPONENTS: the practice AI plays the mod deck " + modDeck.TypeName);
                }
            }
            if (eventBattle && plan.OpponentCards != null && plan.OpponentCards.Count > 0) { aiDeck = CardsDeck(plan.OpponentName ?? "Challenger", plan.OpponentCards); aiDeckNamed = true; }
            if (mode != null && !eventBattle)
            {
                List<int> mine = OfflineModes.DeckCards(config, mode.Player), theirs = OfflineModes.DeckCards(config, mode.Opponent);
                if (mine != null && mine.Count > 0) playerDeck = CardsDeck(mode.Name, mine);
                if (theirs != null && theirs.Count > 0) { aiDeck = CardsDeck(mode.OpponentName ?? mode.Name, theirs); aiDeckNamed = true; }
            }

            byte[] key = TESLEncryption.GenerateKey();
            MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo();
            info.RandomNumberGenerator = new RandomIntegerGenerator(DateTime.UtcNow.GetHashCode());
            info.ScenarioId = scenario.ResourceId.Id;
            info.SkipIntro = 0;
            info.MatchMode = practice ? MatchMode.PracticeBattle : versusArena ? MatchMode.VersusArena : arena ? MatchMode.SoloArena : MatchMode.Campaign;
            // Arena lanes: Field plus the match's lane (both players), unless a boss scenario sets its own.
            if (arena && !boss)
                for (int side = 0; side < 2; side++)
                {
                    GameZoneOverride board = new GameZoneOverride { ZoneToOverride = GameZoneName.CreatureBoard };
                    board.LaneOverrides.Add(new LaneOverride { Lane = new SizedCardLane { Capacity = 4, LaneType = (CardLaneType)arenaMatch.laneTypeHash1, ExtraLaneType = CardLaneType.None } });
                    board.LaneOverrides.Add(new LaneOverride { Lane = new SizedCardLane { Capacity = 4, LaneType = (CardLaneType)arenaMatch.laneTypeHash2, ExtraLaneType = CardLaneType.None } });
                    info.OptionalCreatureBoardOverride[side] = board;
                }
            if (mode != null) OfflineModes.AddLanes(info, mode);
            info.MatchType = MatchType.Solo;
            for (int i = 0; i < 2; i++)
            {
                MatchConfiguration.User user = default(MatchConfiguration.User);
                user.CustomDeck = Describe(config, scenario, i == 0 ? PlayerType.Normal : PlayerType.AiOpponent, i == 0 ? playerDeck : aiDeck);
                if (i == 1 && practiceOpponent != null)
                {
                    user.CustomDeck.PlayerName = practiceOpponent.OpponentName.RawKey;
                    // As the client's own local match: the AI shows one of the deck's valid avatars.
                    CustomDeckUtilities.SetAIAvatarData(user.CustomDeck, info.RandomNumberGenerator, practiceOpponent);
                    if (!aiDeckNamed && !arena) LibraryDeck(config, user.CustomDeck, practiceOpponent, request, versus ? "versus" : casual ? "casual" : eventBattle ? "event" : "practice", plan);
                    if (mode != null && !string.IsNullOrEmpty(mode.OpponentName)) user.CustomDeck.PlayerName = mode.OpponentName;
                    if (eventBattle && !string.IsNullOrEmpty(plan.OpponentName)) user.CustomDeck.PlayerName = plan.OpponentName;
                }
                if (i == 1 && opp != null)
                {   // the mod opponent's name and portrait (its AiOpponent entry), as the game's own opponents are shown
                    if (mode == null || string.IsNullOrEmpty(mode.OpponentName)) user.CustomDeck.PlayerName = opp.Name;
                    user.CustomDeck.AiOpponentId = opp.AiOpponentId;
                    int slot = user.CustomDeck.InventoryTypes.IndexOf("avatar");
                    if (slot >= 0) user.CustomDeck.InventoryValues[slot] = opp.UniqueName;
                    else { user.CustomDeck.InventoryTypes.Add("avatar"); user.CustomDeck.InventoryValues.Add(opp.UniqueName); }
                }
                foreach (int card in user.CustomDeck.Deck) user.CustomDeck.Premium.Add(false);
                user.ObserverEncryptionKey = null; user.SpectatorKey = string.Empty;
                user.Rank = i == 0 ? 12 : 11; user.LegendRank = 0; user.AllowTP = true; user.ArenaBonusMatch = false;
                if (i == 0)
                {
                    user.Type = PlayerType.Normal; user.EncryptionKey = key; user.SecretKey = request.requestId;
                    // The client copies RetryInfo into LastRetryMatchRequestData; its end-of-match flow (ranked result,
                    // quickplay retry) reads the mode/type it asked for, not the server's internal practice mapping.
                    user.RetryInfo = new RetryInfo { MatchMode = (int)request.matchMode, MatchType = (int)request.matchType,
                        AiDifficulty = (int)request.aiDifficulty, PlayerDeckId = request.deckId, ArenaMatchOrderIndex = request.arenaMatchOrderIndex };
                }
                else
                {
                    user.Type = PlayerType.AiOpponent; user.EncryptionKey = null; user.SecretKey = Guid.NewGuid().ToString(); user.RetryInfo = null;
                }
                info.MatchStats.Add(new List<int>());
                for (int n = 0; n < 28; n++) info.MatchStats[i].Add(0);
                info.Users[i] = user;
            }

            FieldInfo serverField = AccessTools.Field(typeof(GameNetworkManager), "_tcpServer");
            FieldInfo simField = AccessTools.Field(typeof(GameNetworkManager), "_embeddedMatchServer");
            TcpNetworkServer old = (TcpNetworkServer)serverField.GetValue(network);
            Simulation oldSim = (Simulation)simField.GetValue(network);
            if (oldSim != null) oldSim.Stop();
            if (old != null) old.Stop();
            TcpNetworkServer server = new TcpNetworkServer(Port);
            StoryLaneRules.ResetMatch();
            Simulation sim = new Simulation();
            try
            {
                sim.SetUseHeartbeats(false);
                sim.SetDisableAi(false);
                sim.SetDualSpectateTest(false);
                OfflinePuzzleSetups.Setup puzzle = OfflinePuzzleSetups.Find(scenario.ResourceId.UniqueName);
                LifecycleRestoration.HumanFirst = puzzle != null || (mode != null && mode.First == "player");
                LifecycleRestoration.AiFirst = mode != null && mode.First == "opponent";
                try { sim.Start(server, info); } finally { LifecycleRestoration.HumanFirst = false; LifecycleRestoration.AiFirst = false; }
                if (puzzle != null) OfflinePuzzleSetups.Apply(sim, puzzle);
                if (mode != null) OfflineModes.ApplyAtRegistration(sim);
                OfflineQuestService.AddActiveQuests(sim, state.Snapshot());
            }
            catch { server.Stop(); throw; }
            serverField.SetValue(network, server);
            simField.SetValue(network, sim);
            CurrentSimulation = sim; CurrentChapterEventId = practice || arena ? 0 : request.campaignData.chapterEventId; CurrentMastery = !practice && !arena && request.campaignData.isMastery;
            CurrentArenaIndex = arena ? request.arenaMatchOrderIndex : -1;
            CurrentEventId = eventBattle ? request.eventId : 0;
            CurrentArenaType = arenaType;
            CurrentScenarioName = eventBattle ? "Event:" + request.eventId : storyBattle != null ? scenario.ResourceId.UniqueName : mode != null ? "Mode:" + mode.Key : practice ? "Practice:" + practiceOpponent.ResourceId.UniqueName : arena ? "Arena" + request.arenaMatchOrderIndex + ":" + practiceOpponent.ResourceId.UniqueName : scenario.ResourceId.UniqueName; LastResult = null; resultRecorded = false;

            Matching.MatchReady match = new Matching.MatchReady();
            match.requestId = request.requestId;
            match.encryptionKeyBase64 = Convert.ToBase64String(key);
            match.ip = "127.0.0.1";
            match.port = Port;
            match.matchId = Guid.NewGuid().ToString("N");
            CurrentMatchId = match.matchId;
            match.spectatorId = string.Empty;
            match.opponentAvatarName = string.Empty;
            Debug.Log("OFFLINE NATIVE MATCH: embedded server started for " + CurrentScenarioName + " (event " + CurrentChapterEventId + ", match " + match.matchId + ").");
            return match;
        }

        // A practice-eligible installed AI deck (CustomDeck.AvailableInPractice), chosen at random.
        private static Matching.IAiDifficulty ModeDifficulty(OfflineModes.Mode mode, Matching.IAiDifficulty chosen)
        {
            switch ((mode.Difficulty ?? "").ToLowerInvariant())
            {
                case "novice": return Matching.IAiDifficulty.Novice;
                case "adept": return Matching.IAiDifficulty.Adept;
                case "expert": return Matching.IAiDifficulty.Expert;
                default: return chosen;                       // "chosen": the Practice screen's difficulty
            }
        }

        private static CustomDeck PracticeOpponent(GameConfiguration config, Matching.IAiDifficulty difficulty)
        {
            List<CustomDeck> decks = config.CustomDecks.FindAll(delegate(CustomDeck d) { return d != null && d.AvailableInPractice && d.Production && d.Decks.Count > 0; });
            if (decks.Count == 0) throw new InvalidOperationException("No installed practice AI decks.");
            return decks[new System.Random().Next(decks.Count)];
        }

        // The AI deck's installed recipe, or its reconstruction (OfflineAiDecks), with the base Avatar.
        private static Deck AiDeck(GameConfiguration config, CustomDeck custom, Matching.IAiDifficulty difficulty)
        {
            DeckDifficulty wanted = difficulty == Matching.IAiDifficulty.Expert ? DeckDifficulty.Hardest : difficulty == Matching.IAiDifficulty.Adept ? DeckDifficulty.Hard : DeckDifficulty.Normal;
            PresetDeck preset = custom.Decks.Find(delegate(PresetDeck p) { return p.Difficulty == wanted; }) ?? custom.Decks[0];
            List<DeckCardInfo> recipe = preset.Cards != null && preset.Cards.Count > 0 ? preset.Cards : OfflineAiDecks.Reconstruct(config, custom, preset);
            Deck deck = new Deck();
            deck.Name = custom.ResourceId.UniqueName;
            deck.AvatarCardId = OrdinaryMatchRules.BaseAvatarId;
            foreach (DeckCardInfo card in recipe) for (int n = 0; n < card.Count; n++) deck.AddCard(card.Id);
            return deck;
        }

        // Once per process: the same restorations the offline practice session installs.
        // Called once the configuration is ready, so the first matching/add never waits on it.
        public static void PrepareEngine() { EnsureEngine(GameManager.Instance.Config); }

        // Engine preparation in stages (rule import, data, patches, deck catalog). PrepareEngineGradually runs one stage per
        // frame after the original graphs are predecoded (no long freeze at the main menu); EnsureEngine finishes whatever
        // is left at once when a match needs it. Each stage runs exactly once.
        private static int engineStage;
        private static Harmony engineHarmony;
        private const int EngineStages = 7;

        private static void RunEngineStage(GameConfiguration config)
        {
            switch (engineStage)
            {
                case 0: OfflineGraphics.Timed("RuleImporter.Load (all)", delegate { RuleImporter.Load(config, exportDirectory); }); break;
                case 1:
                    OfflinePuzzleSetups.Load(OfflinePaths.DataNear(exportDirectory, "puzzle-setups.tsv"));
                    OfflineAiDecks.ReportPath = System.IO.Path.Combine(exportDirectory, "offline-ai-deck-reconstructions.tsv");
                    engineHarmony = new Harmony("local.tesl.offline.native.match");
                    break;
                case 2: OfflineGraphics.Timed("CatalogMovementRestoration.Install", delegate { CatalogMovementRestoration.Install(engineHarmony); }); break;
                case 3: OfflineGraphics.Timed("CommandRestoration.Install", delegate { CommandRestoration.Install(engineHarmony); }); break;
                case 4: OfflineGraphics.Timed("DecisionRestoration.Install", delegate { DecisionRestoration.Install(engineHarmony); }); break;
                case 5: OfflineGraphics.Timed("LifecycleRestoration.Install", delegate { LifecycleRestoration.Install(engineHarmony); }); break;
                case 6:
                    OfflineGraphics.Timed("OfflineDeckCatalog.Load", delegate { OfflineDeckCatalog.Load(config, exportDirectory); });
                    defaultDeck = OfflineDeckCatalog.Entries.Find(delegate(OfflineDeckCatalog.Entry entry) {
                        return entry.Key.StartsWith("resource/DefaultDeck/", StringComparison.Ordinal) && entry.CanPlay; });
                    if (defaultDeck == null) throw new InvalidOperationException("The installed default deck is unavailable.");
                    engineInstalled = true;
                    break;
            }
            engineStage++;
        }

        private static void EnsureEngine(GameConfiguration config)
        {
            while (!engineInstalled && engineStage < EngineStages) RunEngineStage(config);
        }

        public static System.Collections.IEnumerator PrepareEngineGradually()
        {
            while (!engineInstalled && engineStage < EngineStages)
            {
                RunEngineStage(GameManager.Instance.Config);
                yield return null;
            }
        }

        // Scenario Deck zone overrides supply the story decks; the base description carries
        // the player identity. Names/portrait follow the practice session's tutorial setup.
        private static Deck CardsDeck(string name, List<int> cards)
        {
            Deck deck = new Deck();
            deck.Name = name; deck.AvatarCardId = OrdinaryMatchRules.BaseAvatarId;
            foreach (int id in cards) deck.AddCard(id);
            return deck;
        }

        // The chosen saved deck (matching/add deckId), with the base Avatar; else the first
        // saved deck; else the installed default deck.
        private static Deck PlayerDeck(long deckId)
        {
            OfflinePlayerStateData d = state.Snapshot();
            SparkleServices.Inventory.Deck saved = d.decks.Find(delegate(SparkleServices.Inventory.Deck x) { return x.id == deckId; });
            if (saved == null && d.decks.Count > 0) saved = d.decks[0];
            if (saved == null || saved.cards == null || saved.cards.Count == 0) return null;
            Deck deck = new Deck();
            deck.Name = saved.DisplayName;
            deck.AvatarCardId = OrdinaryMatchRules.BaseAvatarId;
            foreach (SparkleServices.Inventory.Deck.DeckCard card in saved.cards)
                for (int n = 0; n < card.numRegular + card.numPremium; n++) deck.AddCard(card.typeHash);
            return deck;
        }

        // X11: an extension's AI deck for the opponent the game has just picked (EverlastingHost.ChooseAiDeck); it may show
        // another original opponent (with that opponent's own name and portrait) so deck and opponent fit together.
        private static void LibraryDeck(GameConfiguration config, PlayerDescription ai, CustomDeck identity, Matching.ClientMatchRequest request, string kind, EverlastingHost.EventMatchPlan plan)
        {
            int slot = ai.InventoryTypes.IndexOf("avatar");
            string shown = slot >= 0 ? ai.InventoryValues[slot] : null;
            AiOpponent shownAi = shown == null ? null : config.AiOpponents.Find(delegate(AiOpponent x) { return x != null && x.ResourceId.UniqueName == shown; });
            EverlastingHost.AiDeckRequest q = new EverlastingHost.AiDeckRequest {
                Kind = kind, Difficulty = request.aiDifficulty.ToString(), Opponent = shown, OpponentRace = OfflineEmotes.OpponentRace(shownAi, ai.PlayerName),
                AiDeck = identity.ResourceId.UniqueName, EventId = request.eventId, Seed = OfflineMods.StableId((request.requestId ?? "") + "|" + shown),
                Tags = plan != null && plan.OpponentTags != null ? plan.OpponentTags : new string[0] };
            if (identity.Colors != null) foreach (CardSubtype c in identity.Colors) if (c != CardSubtype.Neutral) q.Colors.Add(c.ToString());
            EverlastingHost.AiDeckChoice choice = EverlastingHost.ChooseAiDeck(q);
            if (choice == null) return;
            ai.Deck.Clear(); ai.Premium.Clear();
            foreach (int id in choice.Cards) if (config.FindCardById(id) != null) ai.Deck.Add(id);
            if (!string.IsNullOrEmpty(choice.Opponent) && choice.Opponent != shown)
            {
                AiOpponent other = config.AiOpponents.Find(delegate(AiOpponent x) { return x != null && x.ResourceId.UniqueName == choice.Opponent; });
                if (other != null)
                {
                    ai.AiOpponentId = other.ResourceId.Id;
                    if (slot >= 0) ai.InventoryValues[slot] = other.ResourceId.UniqueName; else { ai.InventoryTypes.Add("avatar"); ai.InventoryValues.Add(other.ResourceId.UniqueName); }
                    string name = OfflineEmotes.OpponentDisplayName(other);
                    if (!string.IsNullOrEmpty(name)) ai.PlayerName = name;
                }
            }
            Debug.Log("EVERLASTING HOST: " + choice.Provider + " AI deck " + choice.DeckName + " (" + ai.Deck.Count + " cards) for " + (choice.Opponent ?? shown) + " [" + kind + ", " + q.Difficulty + "]");
        }

        private static PlayerDescription Describe(GameConfiguration config, Scenario scenario, PlayerType type, Deck chosen)
        {
            GameManager manager = GameManager.Instance;
            Deck selected = manager.SelectedDeck, opponent = manager.AiOpponentSelectedDeck;
            PlayerDescription description;
            try
            {
                manager.SelectedDeck = OfflineDeckCatalog.Clone(chosen ?? defaultDeck.Deck);
                manager.AiOpponentSelectedDeck = OfflineDeckCatalog.Clone(type == PlayerType.AiOpponent && chosen != null ? chosen : defaultDeck.Deck);
                description = LockablePlayerDescriptions.LoadPlayerDescription(type);
            }
            finally { manager.SelectedDeck = selected; manager.AiOpponentSelectedDeck = opponent; }
            description.AiOpponentId = UniqueResourceId.GetInvalid().Id;
            if (type == PlayerType.Normal)
            {
                ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
                description.PlayerName = services != null && services.AuthData != null ? services.AuthData.nickname : OfflineNativeFlow.Profile.Username;
                // Selected cosmetics travel in the description; the match intro reads "cardback" and "title" from them.
                if (State != null)
                {
                    OfflinePlayerStateData d = State.Snapshot();
                    foreach (string category in new string[] { "avatar", OfflineCosmetics.TitleCategory, OfflineCosmetics.CardBackCategory })
                    {
                        string value = d.GetSelected(category);
                        if (string.IsNullOrEmpty(value) || description.InventoryTypes.Contains(category)) continue;
                        description.InventoryTypes.Add(category); description.InventoryValues.Add(value);
                    }
                }
                return description;
            }
            if (scenario.ResourceId.UniqueName == "Default") return description; // practice: named by the caller
            AiOpponent ai = config.AiOpponents.Find(delegate(AiOpponent x) { return x.ResourceId.Id == scenario.AiOpponent.resourceId; });
            if (ai == null) throw new InvalidOperationException("Scenario AI opponent is absent from the installed configuration.");
            description.PlayerName = ai.DisplayNameKey.RawKey;
            description.AiOpponentId = ai.ResourceId.Id;
            return description;
        }

        // A won story match completes its chapter event (unlocking follows campaign rules).
        internal static bool RecordRewardChanges(OfflinePlayerStateData d, Action action)
        {
            long before = d.nextRewardId;
            action();
            return d.nextRewardId > before;
        }

        private static void RecordResultOnce()
        {
            Simulation sim = CurrentSimulation;
            if (sim == null || resultRecorded || sim._gameState == null || sim._gameState.GamePhase != GamePhase.Results) return;
            resultRecorded = true;
            PlayerState human = sim.Players.Find(delegate(PlayerState p) { return p.PlayerType == (int)PlayerType.Normal; });
            bool won = human != null && sim.WinnerId.Id == human.NetId.Id;
            LastResult = won ? "Win" : "Lose";
            foreach (PlayerState p in sim.Players)
            {
                PlayerUniqueId owner = p.NetId;
                List<ServerCard> avatars = sim.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.OwnerNetId == owner && c.HasSubtype(CardSubtype.Avatar); });
                ServerCard avatar = avatars.Count > 0 ? avatars[0] : null;
                if (avatar != null) Debug.Log("OFFLINE NATIVE MATCH: avatar of " + p.NetId.Id + " in " + avatar.CurrentGameZoneName + " health=" + avatar.CurrentHealth + " starting=" + avatar.QueryAttribute(AttributeType.StartingHealth).Value + " card=" + avatar.CardId);
                Debug.Log("OFFLINE NATIVE MATCH: end state player " + p.NetId.Id + " human=" + (p.PlayerType == (int)PlayerType.Normal) + " turns=" + p.QueryAttribute(AttributeType.TurnCount).Value
                    + " magicka=" + p.QueryAttribute(AttributeType.Mana).Value + " maxMagicka=" + (avatar == null ? -1 : avatar.QueryAttribute(AttributeType.MaxMana).Value)
                    + " health=" + (avatar == null ? -1 : avatar.CurrentHealth));
            }
            ResultsRecorded++; ResultHistory += CurrentScenarioName + "=" + LastResult + ";";
            OfflineModes.Mode playedMode = OfflineModes.Active;
            if (playedMode != null && won && playedMode.RewardGold > 0 && state != null)
            {
                int gold = playedMode.RewardGold;
                state.Mutate(delegate(OfflinePlayerStateData d) { d.gold += gold; });
                Debug.Log("OFFLINE MODES: won " + playedMode.Key + ": +" + gold + " gold");
            }
            int eventId = CurrentChapterEventId;
            bool completed = false;
            bool rewarded = false;
            string ratingMode = CurrentRatingMode;
            if (ratingMode != null)
                try { state.Mutate(delegate(OfflinePlayerStateData d) { rewarded |= RecordRewardChanges(d, delegate { OfflineRankedService.RecordMatch(d, ratingMode, won); }); }); }
                catch (Exception e) { Debug.LogError("OFFLINE NATIVE MATCH: ranked result NOT saved: " + e); }
            try { state.Mutate(delegate(OfflinePlayerStateData d) { rewarded |= RecordRewardChanges(d, delegate { OfflineQuestService.RecordProgress(sim, d); }); }); }
            catch (Exception e) { Debug.LogError("OFFLINE NATIVE MATCH: quest/title progress NOT saved: " + e); }
            try
            {
                if (won || CurrentArenaIndex >= 0) state.Mutate(delegate(OfflinePlayerStateData d) { rewarded |= RecordRewardChanges(d, delegate {
                    if (CurrentArenaIndex >= 0) { OfflineArenaService.RecordResult(d, CurrentArenaType, CurrentArenaIndex, won); return; }
                    completed = OfflineCampaignService.CompleteEvent(d, eventId, CurrentMastery);
                    if (completed) OfflineRewardsService.GrantEvent(d, eventId, CurrentMastery); }); });
            }
            catch (Exception e) { Debug.LogError("OFFLINE NATIVE MATCH: match result NOT saved: " + e); }
            // The original server pushed "matchrewards" for this match id; the client then
            // refreshes rewards/list before its results/rewards screens (else a 5 s timeout).
            Rewards.MatchRewardsNotice notice = new Rewards.MatchRewardsNotice();
            string xpMode = CurrentXpMode;
            try { state.Mutate(delegate(OfflinePlayerStateData d) { rewarded |= RecordRewardChanges(d, delegate { OfflineLevelService.AwardMatch(d, xpMode, won); }); }); }
            catch (Exception e) { Debug.LogError("OFFLINE NATIVE MATCH: match XP NOT saved: " + e); }
            // Extensions (EverlastingHost) see every finished match and may queue their own rewards.
            rewarded |= EverlastingHost.RaiseMatchEnded(new EverlastingHost.MatchResult { Mode = xpMode, RatingMode = ratingMode, Won = won,
                Scenario = CurrentScenarioName, ChapterEventId = eventId, ArenaIndex = CurrentArenaIndex, MatchId = CurrentMatchId, EventId = CurrentEventId });
            notice.receivedRewards = rewarded; notice.matchId = CurrentMatchId;
            PubSub.CreatePushNotification("matchrewards", JsonUtility.ToJson(notice));
            Debug.Log("OFFLINE NATIVE MATCH: " + CurrentScenarioName + " result " + LastResult + "; chapter event " + eventId + " completed=" + completed + " rewarded=" + rewarded + ".");
        }
    }

    public sealed class OfflineNativeMatchPump : MonoBehaviour
    {
        private void Update() { OfflineNativeMatch.Pump(); }
    }
}
