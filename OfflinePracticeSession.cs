using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CcgRpc;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Local presentation around the original launcher, board, input and results.
    // The reviewed rule subset stays explicit; no remote profile is manufactured.
    public sealed class OfflinePracticeSession : MonoBehaviour
    {
        public static OfflinePracticeSession Instance;
        public static Simulation LastSimulation;
        public static bool Enabled { get { return OfflineBootstrapPlugin.Active && Instance != null; } }
        private GameConfiguration config;
        private string directory;
        private IDisposable ordinary;
        private IDisposable tutorial;
        private TutorialScenarioRules.Chapter selectedChapter;
        private static readonly string[] ChapterTitles = { "Chapter 1 - The Forgotten Hero", "Chapter 2 - The Escape", "Chapter 3 - Dark Omens" };
        private string phase = "Preparing offline practice";
        private string error;
        private bool choosing;
        private bool launching;
        private bool playing;
        private bool finishing;
        private bool restartRequested;
        private bool showOriginals = true;
        private Vector2 scroll;
        private Deck playerDeck;
        private Deck opponentDeck;
        private OfflineDeckCatalog.Entry selectedPlayerEntry;
        private OfflineDeckCatalog.Entry selectedOpponentEntry;
        private string deckSearch = "";
        private int launches;
        private float nextReport;
        private OfflineLocalProfile profile;
        private string username;
        private string profileMessage;
        private string localMatchId;
        public static string PlayerName { get { return Instance != null && Instance.profile != null ? Instance.profile.Username : "Offline Player"; } }

        public static IEnumerator Initialize(GameConfiguration configuration, string output)
        {
            if (!OfflineBootstrapPlugin.Active || Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-play") < 0)
                throw new InvalidOperationException("Offline practice needs its explicit startup flag.");
            if (Instance != null) throw new InvalidOperationException("Offline practice is already initialized.");
            OfflinePracticeSession session = OfflineBootstrapPlugin.Instance.gameObject.AddComponent<OfflinePracticeSession>();
            Instance = session;
            session.config = configuration;
            session.directory = output;
            session.profile = OfflineLocalProfile.Open(Path.GetDirectoryName(Application.dataPath));
            session.username = session.profile.Username;
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.Full);
            File.WriteAllText(Path.Combine(output, "offline-practice-events.tsv"), "utc\tmatch\taction\tclient_result\tauth_data_null\n");
            RuleImporter.Load(configuration, output);
            session.ordinary = OrdinaryMatchRules.Prepare(configuration);
            Harmony harmony = new Harmony("local.tesl.offline.practice");
            CatalogMovementRestoration.Install(harmony);
            CommandRestoration.Install(harmony);
            DecisionRestoration.Install(harmony);
            LifecycleRestoration.Install(harmony);
            OfflineBoardAdapter.Install(harmony);
            OfflineNativeResults.Install(harmony);
            OfflineNativeOptions.Install(harmony, session.RequestRestart);
            harmony.Patch(AccessTools.Method(typeof(LockablePlayerDescriptions), "LoadPlayerDescription"), null,
                new HarmonyMethod(typeof(OfflinePracticeSession), "DescribeLocalPlayer"), null, null, null);
            OfflineDeckCatalog.Load(configuration, output);
            OfflineDeckCatalog.Entry originalDefault = OfflineDeckCatalog.Entries.Find(delegate(OfflineDeckCatalog.Entry entry) {
                return entry.Key.StartsWith("resource/DefaultDeck/", StringComparison.Ordinal) && entry.CanPlay;
            });
            session.selectedPlayerEntry = originalDefault;
            session.selectedOpponentEntry = originalDefault;
            session.playerDeck = originalDefault == null ? OfflineDeckCatalog.CreatePracticeDeck(configuration) : OfflineDeckCatalog.Clone(originalDefault.Deck);
            session.opponentDeck = OfflineDeckCatalog.Clone(session.playerDeck);
            yield return OfflineAudioAssets.InspectAndInstall(output, true);
            session.choosing = true;
            session.phase = "Choose decks for offline practice";
            Singleton<LoadingScreenManager>.Instance.SetLoadingWinActive(false, GameNetworkManager.Instance.GetInstanceID(), true);
            session.WriteState();
            Debug.Log("OFFLINE PRACTICE: local launch screen ready; native decks validated; synthetic pointer hook is not installed.");
            session.Record("launch_screen_ready");
            int storyDiagnostic = OfflineStoryDiagnostic.RequestedChapter();
            if (storyDiagnostic > 0) session.StartCoroutine(OfflineStoryDiagnostic.Run(session, output, storyDiagnostic));
        }

        // Same launcher path as the visible chapter buttons.
        public void StartChapterForDiagnostic(TutorialScenarioRules.Chapter chapter)
        {
            if (!choosing) throw new InvalidOperationException("Story diagnostic requires the launch screen.");
            selectedChapter = chapter;
            StartCoroutine(Guard(Launch()));
        }

        private static void DescribeLocalPlayer(PlayerType __0, PlayerDescription __result)
        {
            if (!Enabled) return;
            __result.PlayerName = __0 == PlayerType.Normal ? PlayerName : "Practice Opponent";
            __result.AiOpponentId = UniqueResourceId.GetInvalid().Id;
            if (__0 != PlayerType.Normal && TutorialScenarioRules.Active)
            {
                // The native intro resolves the original portrait/localized name from this id.
                Scenario scenario = GameManager.Instance.FindScenario(TutorialScenarioRules.Current.ScenarioId);
                AiOpponent ai = scenario == null ? null : GameManager.Instance.Config.AiOpponents.Find(delegate(AiOpponent x) { return x.ResourceId.Id == scenario.AiOpponent.resourceId; });
                if (ai == null) throw new InvalidOperationException("Tutorial AI opponent is absent from the installed configuration.");
                __result.PlayerName = ai.DisplayNameKey.RawKey;
                __result.AiOpponentId = ai.ResourceId.Id;
            }
        }

        private IEnumerator Launch()
        {
            choosing = false; launching = true; playing = false; finishing = false; error = null;
            phase = "Loading original board"; WriteState();
            if (selectedPlayerEntry != null && !selectedPlayerEntry.CanPlay) throw new NotSupportedException(selectedPlayerEntry.Reason);
            if (selectedOpponentEntry != null && !selectedOpponentEntry.CanPlay) throw new NotSupportedException(selectedOpponentEntry.Reason);
            OfflineDeckCatalog.ValidateForPlay(config, playerDeck);
            OfflineDeckCatalog.ValidateForPlay(config, opponentDeck);
            GameManager.Instance.SelectedDeck = OfflineDeckCatalog.Clone(playerDeck);
            GameManager.Instance.AiOpponentSelectedDeck = OfflineDeckCatalog.Clone(opponentDeck);
            OfflineMatchCleanup.ResetForNextMatch(GameNetworkManager.Instance);
            UseRules(selectedChapter);
            BoardConductor previousBoard = UnityEngine.Object.FindObjectOfType<BoardConductor>();
            yield return OfflineBoardAdapter.Prepare();
            yield return OfflineMusicPreparation.Prepare(directory);
            localMatchId = OfflineLocalProfile.CreateMatchId();
            if (selectedChapter != null) OfflineBoardAdapter.LaunchTutorialChapter(config, selectedChapter);
            else OfflineBoardAdapter.LaunchValidatedPractice(config);
            LastSimulation = (Simulation)AccessTools.Field(typeof(GameNetworkManager), "_embeddedMatchServer").GetValue(GameNetworkManager.Instance);
            float deadline = Time.realtimeSinceStartup + 90f;
            BoardConductor board;
            do
            {
                board = UnityEngine.Object.FindObjectOfType<BoardConductor>();
                if (board != null && !object.ReferenceEquals(board, previousBoard) && board.BoardState != null && board.BoardState.RejoinState.DidBoardLayoutInit) break;
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("The original board did not finish loading.");
                yield return null;
            } while (true);
            launches++; launching = false; playing = true; phase = selectedChapter != null ? "Playing " + selectedChapter.Name : "Playing offline";
            Record(selectedChapter != null ? "fresh_board_initialized_" + selectedChapter.Name : "fresh_board_initialized");
            StartCoroutine(Capture("board-ready"));
            WriteState();
            Debug.Log("OFFLINE PRACTICE: native board initialized for match " + launches + "; original input and AI enabled.");
        }

        // Exactly one reversible rules scope is active outside a match: ordinary
        // practice, or the selected tutorial chapter's reconstructed overrides.
        private void UseRules(TutorialScenarioRules.Chapter chapter)
        {
            if (!GameNetworkManager.Instance.IsOutsideOfMatch()) throw new InvalidOperationException("Rules cannot change during a match.");
            if (chapter == null)
            {
                if (tutorial != null) { tutorial.Dispose(); tutorial = null; }
                if (ordinary == null) ordinary = OrdinaryMatchRules.Prepare(config);
                return;
            }
            if (tutorial != null && TutorialScenarioRules.Current == chapter) return;
            if (tutorial != null) { tutorial.Dispose(); tutorial = null; }
            if (ordinary != null) { ordinary.Dispose(); ordinary = null; }
            tutorial = TutorialScenarioRules.Prepare(config, chapter.ScenarioId);
        }

        private IEnumerator Guard(IEnumerator routine)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(routine);
            float deadline = Time.realtimeSinceStartup + 180f;
            try
            {
                while (stack.Count != 0)
                {
                    object value = null; bool moved = false; Exception failure = null;
                    try
                    {
                        if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Offline practice launch exceeded its loading budget.");
                        moved = stack.Peek().MoveNext();
                        if (moved) value = stack.Peek().Current;
                    }
                    catch (Exception ex) { failure = ex; }
                    if (failure != null)
                    {
                        error = failure.Message; phase = "Offline practice stopped";
                        launching = false; playing = false; choosing = true;
                        try { OfflineBoardAdapter.CloseLocalMatch(); } catch (Exception cleanup) { Debug.LogError(cleanup); }
                        WriteState(); Debug.LogError("OFFLINE PRACTICE: " + failure); yield break;
                    }
                    if (!moved) { IDisposable done = stack.Pop() as IDisposable; if (done != null) done.Dispose(); continue; }
                    IEnumerator nested = value as IEnumerator;
                    if (nested != null) { stack.Push(nested); continue; }
                    yield return value;
                }
            }
            finally { while (stack.Count != 0) { IDisposable item = stack.Pop() as IDisposable; if (item != null) item.Dispose(); } }
        }

        public void OnNativeMatchStopped(bool networkIssue)
        {
            if (finishing || choosing) return;
            finishing = true; playing = false; launching = false;
            SettingsData.EndGameType result = Singleton<SettingsManager>.Instance.GameCompletionType;
            bool completed = !networkIssue && LastSimulation != null && LastSimulation._gameState.GamePhase == GamePhase.Results
                && (result == SettingsData.EndGameType.Win || result == SettingsData.EndGameType.Lose);
            try
            {
                if (completed && !restartRequested)
                {
                    try { profile.RecordResult(localMatchId, result == SettingsData.EndGameType.Win); }
                    catch (Exception saveError)
                    {
                        profileMessage = "Could not save this result: " + saveError.Message;
                        Debug.LogError("OFFLINE PROFILE: " + saveError);
                    }
                }
                BoardConductor board = UnityEngine.Object.FindObjectOfType<BoardConductor>();
                if (board != null)
                {
                    BoardInputController input = board.GetComponent<BoardInputController>();
                    if (input != null) input.enabled = false;
                    OfflineMatchCleanup.DetachOutgoingBoard(board);
                }
                OfflineBoardAdapter.CloseLocalMatch();
                phase = completed ? result.ToString() : "Match connection closed";
                Record(completed ? "native_result_" + result : "local_connection_closed");
                if (restartRequested && !networkIssue)
                {
                    restartRequested = false;
                    StartCoroutine(Guard(Launch()));
                }
                else if (completed) { OfflineNativeResults.Show(result, ReturnToDecks); StartCoroutine(Capture("native-result")); }
                else { error = "The local match closed before a completed result."; ReturnToDecks(); }
                WriteState();
            }
            catch (Exception ex) { error = ex.Message; ReturnToDecks(); Debug.LogError("OFFLINE PRACTICE RESULTS: " + ex); }
        }

        private void ReturnToDecks()
        {
            choosing = true; launching = false; playing = false; finishing = false;
            phase = "Choose decks for offline practice";
            WriteState();
            Record("returned_to_decks");
        }

        public void RequestRestart()
        {
            if (launching || finishing) return;
            Record("native_retry_requested");
            if (playing && !GameNetworkManager.Instance.IsOutsideOfMatch())
            {
                restartRequested = true;
                GameNetworkManager.Instance.PlayerExitGameRequested();
            }
            else StartCoroutine(Guard(Launch()));
        }

        private void Record(string action)
        {
            if (directory == null) return;
            string path = Path.Combine(directory, "offline-practice-events.tsv");
            if (!File.Exists(path)) File.WriteAllText(path, "utc\tmatch\taction\tclient_result\tauth_data_null\n");
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + "\t" + launches + "\t" + action + "\t"
                + Singleton<SettingsManager>.Instance.GameCompletionType + "\t" + (ServicesManager.Instance.AuthData == null) + "\n");
        }

        private IEnumerator Capture(string label)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            yield return new WaitForSeconds(2f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "offline-practice-" + launches + "-" + label + ".png"));
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup >= nextReport) { nextReport = Time.realtimeSinceStartup + 5f; WriteState(); }
        }

        private void WriteState()
        {
            if (directory == null) return;
            Simulation sim = LastSimulation;
            StringBuilder rows = new StringBuilder("field\tvalue\n");
            rows.AppendLine("phase\t" + phase);
            rows.AppendLine("error\t" + (error ?? ""));
            rows.AppendLine("matches_started\t" + launches);
            rows.AppendLine("selected_chapter\t" + (selectedChapter == null ? "" : selectedChapter.Name));
            rows.AppendLine("rules_scope\t" + (TutorialScenarioRules.Active ? "tutorial" : OrdinaryMatchRules.Active ? "ordinary" : "none"));
            if (profile != null)
            {
                rows.AppendLine("local_username\t" + profile.Username);
                rows.AppendLine("local_profile_id\t" + profile.Identity);
                rows.AppendLine("local_completed\t" + profile.MatchesCompleted);
                rows.AppendLine("local_wins\t" + profile.MatchesWon);
                rows.AppendLine("local_losses\t" + profile.MatchesLost);
            }
            rows.AppendLine("auth_data_null\t" + (ServicesManager.Instance.AuthData == null));
            rows.AppendLine("synthetic_pointer_hook_installed\tFalse");
            rows.AppendLine("native_result_visible\t" + OfflineNativeResults.IsVisible);
            rows.AppendLine("outside_match\t" + GameNetworkManager.Instance.IsOutsideOfMatch());
            rows.AppendLine("player_deck_size\t" + (playerDeck == null ? 0 : playerDeck.Size));
            rows.AppendLine("opponent_deck_size\t" + (opponentDeck == null ? 0 : opponentDeck.Size));
            rows.AppendLine("client_result\t" + Singleton<SettingsManager>.Instance.GameCompletionType);
            if (sim != null)
            {
                rows.AppendLine("server_phase\t" + sim._gameState.GamePhase);
                rows.AppendLine("winner_id\t" + sim.WinnerId.Id);
                rows.AppendLine("loser_id\t" + sim.LoserId.Id);
                foreach (PlayerState player in sim.Players)
                    rows.AppendLine("player_" + player.NetId.Id + "_turns\t" + player.QueryAttribute(AttributeType.TurnCount).Value);
            }
            File.WriteAllText(Path.Combine(directory, "offline-practice-state.tsv"), rows.ToString());
        }

        private void OnGUI()
        {
            if (!Enabled) return;
            GUI.depth = -10000;
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1280f, Screen.height / 720f, 1f));
            try
            {
                if (playing)
                {
                    GUI.Box(new Rect(8, 8, 240, 32), selectedChapter != null ? "Offline story — " + selectedChapter.Name : "Offline practice — partial card support");
                    return;
                }
                if (!choosing)
                {
                    if (launching) GUI.Box(new Rect(340, 300, 600, 100), phase);
                    return;
                }
                GUI.Box(new Rect(190, 40, 900, 640), "The Elder Scrolls: Legends — Offline practice");
                GUILayout.BeginArea(new Rect(220, 75, 840, 575));
                GUILayout.Label("Original board, cards and controls. Reviewed card rules and reconstructed AI are still being restored.");
                GUILayout.Label("Current practice: Default field/shadow lanes, base avatar. Full original collection remains in progress.");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Player name", GUILayout.Width(90));
                username = GUILayout.TextField(username, 32, GUILayout.Width(250));
                if (GUILayout.Button("Save name", GUILayout.Width(100)))
                {
                    try { profile.SetUsername(username); username = profile.Username; profileMessage = "Player name saved."; WriteState(); }
                    catch (Exception ex) { profileMessage = ex.Message; }
                }
                GUILayout.Label(profile.MatchesWon + " wins / " + profile.MatchesLost + " losses", GUILayout.Width(180));
                GUILayout.EndHorizontal();
                if (profileMessage != null) GUILayout.Label(profileMessage);
                if (error != null) GUILayout.Label("Unable to continue: " + error);
                GUILayout.Space(12);
                GUILayout.Label("Your deck: " + (selectedPlayerEntry != null ? selectedPlayerEntry.Name : "Restoration practice") + " (" + (playerDeck == null ? "recipe pending" : playerDeck.Size + " cards") + ")");
                GUILayout.Label("Opponent: " + (selectedOpponentEntry != null ? selectedOpponentEntry.Name : "Restoration practice") + " (" + (opponentDeck == null ? "recipe pending" : opponentDeck.Size + " cards") + ")");
                if (GUILayout.Button("Play against AI", GUILayout.Height(42))) { selectedChapter = null; StartCoroutine(Guard(Launch())); }
                GUILayout.Label("Story: The Forgotten Hero, Act 1 (original scenarios, decks and instructions; avatar/rune/ring values observed on TESL-Reborn)");
                GUILayout.BeginHorizontal();
                for (int i = 0; i < TutorialScenarioRules.Chapters.Length; i++)
                    if (GUILayout.Button(ChapterTitles[i], GUILayout.Height(32))) { selectedChapter = TutorialScenarioRules.Chapters[i]; StartCoroutine(Guard(Launch())); }
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
                showOriginals = GUILayout.Toggle(showOriginals, "Show installed original decks and availability");
                if (showOriginals) deckSearch = GUILayout.TextField(deckSearch);
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(showOriginals ? 210 : 230));
                if (showOriginals)
                {
                    foreach (OfflineDeckCatalog.Entry entry in OfflineDeckCatalog.Entries)
                    {
                        if (deckSearch.Length != 0 && entry.Name.IndexOf(deckSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(entry.Name + " — " + (entry.CanPlay ? "Ready" : "Data restoration pending"));
                        if (GUILayout.Button("Your deck", GUILayout.Width(90))) { selectedPlayerEntry = entry; playerDeck = entry.Deck == null ? null : OfflineDeckCatalog.Clone(entry.Deck); error = entry.CanPlay ? null : entry.Reason; }
                        if (GUILayout.Button("Opponent", GUILayout.Width(90))) { selectedOpponentEntry = entry; opponentDeck = entry.Deck == null ? null : OfflineDeckCatalog.Clone(entry.Deck); error = entry.CanPlay ? null : entry.Reason; }
                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.Label("Practice deck uses supported original cards. It is a separate local deck.");
                    if (playerDeck != null) foreach (DeckCardInfo card in playerDeck.Cards) GUILayout.Label(card.Count + " × " + config.FindCardById(card.Id).Name);
                }
                GUILayout.EndScrollView();
                if (GUILayout.Button("Select restoration practice deck", GUILayout.Height(28))) { selectedPlayerEntry = null; selectedOpponentEntry = null; playerDeck = OfflineDeckCatalog.CreatePracticeDeck(config); opponentDeck = OfflineDeckCatalog.Clone(playerDeck); error = null; }
                if (GUILayout.Button("Close game", GUILayout.Height(28))) Singleton<SettingsManager>.Instance.QuitApplication();
                GUILayout.EndArea();
            }
            finally { GUI.matrix = original; }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            OfflineNativeResults.Hide();
            if (tutorial != null) tutorial.Dispose();
            if (ordinary != null) ordinary.Dispose();
            Instance = null; LastSimulation = null;
        }
    }
}

