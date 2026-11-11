using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using AssetBundles;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using SparkleServices;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OfflineEngine
{
    // Presentation/transport adapter only. Reviewed test fixtures, rule validation,
    // the bootstrap launch gate and restored history remain the caller's responsibility.
    public static class OfflineBoardAdapter
    {
        private const string DiagnosticFlag = "--tesl-offline-board-diagnostic";
        private const string LocalName = "Offline Player";
        private static bool installed;
        private static bool prepared;
        private static bool launchingDiagnostic;
        private static bool transitionPending;

        public static bool IsLaunchingDiagnostic { get { return launchingDiagnostic; } }
        public static bool IsLaunchingValidated { get { return launchingDiagnostic; } }
        public static bool IsBoardMode { get { return IsDiagnostic || OfflinePracticeSession.Enabled; } }
        public static bool IsPrepared { get { return prepared; } }

        private static bool IsOffline { get { return OfflineBootstrapPlugin.Active; } }

        public static void Install(Harmony harmony)
        {
            if (!IsOffline) throw new InvalidOperationException("Board adapter requires guarded offline startup.");
            if (installed) return;
            if (harmony == null) throw new ArgumentNullException("harmony");
            Patch(harmony, typeof(GameNetworkManager), "AttemptConnection", Type.EmptyTypes, "AttemptLocalConnection");
            Patch(harmony, typeof(GameNetworkManager), "FixedUpdate", Type.EmptyTypes, "WaitForNewBoardScene");
            SceneManager.sceneLoaded += OnNativeBoardLoaded;
            OfflineCardTeardown.Install(harmony);
            Patch(harmony, typeof(Presence), "SetPresence", new Type[] { typeof(Presence.UserPresence) }, "AllowOnlineOnly");
            Patch(harmony, typeof(Presence), "SetMatchId", new Type[] { typeof(string) }, "AllowOnlineOnly");
            Patch(harmony, typeof(GameNetworkManager), "StopMatch", new Type[] { typeof(bool) }, "StopDiagnosticMatch");
            Patch(harmony, typeof(EndGameState), "OnEnable", Type.EmptyTypes, "EndDiagnosticResults");
            // Keep Initialize/Awake, serialized references and input-delegate registration.
            // These entry points consume remote caches or can expose remote social controls.
            string[] socialMethods = { "Update", "FixedUpdate", "ToggleChatWin", "ToggleFriendsPanel", "ShowFriendsPanel", "HideFriendsPanelAndChatWin", "RecentMessagesOverlayOnShowChat", "ChatWinOnUsernameSelected", "OnPlayMatch", "ReceiveChallenge", "DoNotDisturbOnToggleValueChanged" };
            foreach (string name in socialMethods)
            {
                MethodInfo original = AccessTools.Method(typeof(FriendsWin), name);
                if (original == null) throw new MissingMethodException(typeof(FriendsWin).FullName, name);
                harmony.Patch(original, new HarmonyMethod(typeof(OfflineBoardAdapter), "AllowOnlineOnly"), null, null, null, null);
            }
            installed = true;
        }

        private static void Patch(Harmony harmony, Type type, string name, Type[] arguments, string prefix)
        {
            MethodInfo original = AccessTools.Method(type, name, arguments);
            if (original == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(original, new HarmonyMethod(typeof(OfflineBoardAdapter), prefix), null, null, null, null);
        }

        private static bool AllowOnlineOnly() { return !IsOffline; }

        private static bool WaitForNewBoardScene() { return !IsOffline || !transitionPending; }
        private static void OnNativeBoardLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsOffline || !transitionPending || scene.name != "CDGameBoard") return;
            NetworkField("_matchNetworkClient").SetValue(GameNetworkManager.Instance, null);
            transitionPending = false;
            UnityEngine.Debug.Log("OFFLINE REPLAY: new board scene loaded; native queued transport delivery resumed.");
        }

        private static bool IsDiagnostic
        {
            get { return IsOffline && Array.IndexOf(Environment.GetCommandLineArgs(), DiagnosticFlag) >= 0; }
        }

        private static bool StopDiagnosticMatch(GameNetworkManager __instance, bool __0)
        {
            if (OfflinePracticeSession.Enabled)
            {
                OfflinePracticeSession.Instance.OnNativeMatchStopped(__0);
                return false;
            }
            if (!IsDiagnostic) return true;
            // Returning to the ordinary menu would enter its remote rewards/profile
            // state machine. Diagnostic teardown closes only the original local match.
            NetworkField("_exiting").SetValue(__instance, true);
            MethodInfo close = AccessTools.Method(typeof(GameNetworkManager), "EndConnectionToServer", Type.EmptyTypes);
            if (close == null) throw new MissingMethodException(typeof(GameNetworkManager).FullName, "EndConnectionToServer");
            close.Invoke(__instance, null);
            UnityEngine.Debug.Log("OFFLINE BOARD: local diagnostic stopped (network issue=" + __0 + "); remote menu/results skipped.");
            Application.Quit();
            return false;
        }

        private static bool EndDiagnosticResults()
        {
            if (OfflinePracticeSession.Enabled) return false;
            if (!IsDiagnostic) return true;
            UnityEngine.Debug.Log("OFFLINE BOARD: reached native results state; remote rewards/profile refreshes skipped; diagnostic exiting.");
            Application.Quit();
            return false;
        }

        private static FieldInfo NetworkField(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(GameNetworkManager), name);
            if (field == null) throw new MissingFieldException(typeof(GameNetworkManager).FullName, name);
            return field;
        }

        private static bool AttemptLocalConnection(GameNetworkManager __instance)
        {
            if (!IsOffline) return true;
            // A live original local server is mandatory. Never repurpose a remote
            // match-ready payload, remote secret or public-service identity.
            if (NetworkField("_tcpServer").GetValue(__instance) == null ||
                (int)NetworkField("_hostPort").GetValue(__instance) != 4001)
                throw new InvalidOperationException("Offline connection requires the native local TCP server on port 4001.");
            string secret = (string)NetworkField("_secretKey").GetValue(__instance);
            byte[] encryption = (byte[])NetworkField("_encryptionKey").GetValue(__instance);
            if (string.IsNullOrEmpty(secret) || encryption == null || encryption.Length == 0)
                throw new InvalidOperationException("Native local transport has not generated its session keys.");
            TcpNetworkClient.LoginData login = new TcpNetworkClient.LoginData();
            login.secretKey = secret;
            login.userName = OfflinePracticeSession.Enabled ? OfflinePracticeSession.PlayerName : LocalName;
            NetworkField("_hostServer").SetValue(__instance, "127.0.0.1");
            NetworkField("_tcpClient").SetValue(__instance, new TcpNetworkClient("127.0.0.1", 4001, login, encryption));
            Stopwatch watch = (Stopwatch)NetworkField("ConnectionCompleteStopwatch").GetValue(__instance);
            if (watch == null) throw new InvalidOperationException("Native connection stopwatch is absent.");
            watch.Start();
            return false;
        }

        public static IEnumerator Prepare()
        {
            RequireDiagnostic();
            if (!installed) throw new InvalidOperationException("Install the offline board adapter before preparation.");
            if (prepared) yield break;
            if (!AssetCatalog.IsReady() || GameManager.Instance == null ||
                GameManager.Instance.Config == null || !GameManager.Instance.Config.initialized)
                throw new InvalidOperationException("Native local assets and configuration must be ready before board preparation.");
            if (LocalizationManager.Instance == null || LocalizationManager.Instance.IsLoading())
                throw new InvalidOperationException("Full native localization must finish before board preparation.");
            RequirePresentationObjects();
            FXFactory factory = Singleton<FXFactory>.Instance;
            if (factory == null) throw new InvalidOperationException("Native FXFactory is absent.");
            yield return factory.Initialize();
            FieldInfo prefabsField = AccessTools.Field(typeof(FXFactory), "_cardPrefabList");
            Dictionary<CardPrefabType, AssetBundleLoadAssetOperation> prefabs = prefabsField == null ? null :
                prefabsField.GetValue(factory) as Dictionary<CardPrefabType, AssetBundleLoadAssetOperation>;
            if (factory.IsLoading() || prefabs == null || prefabs.Count == 0)
                throw new InvalidOperationException("Native card graphics were not initialized.");
            foreach (KeyValuePair<CardPrefabType, AssetBundleLoadAssetOperation> prefab in prefabs)
                if (prefab.Value == null || prefab.Value.GetAsset<GameObject>() == null)
                    throw new InvalidOperationException("Native card prefab failed to load: " + prefab.Key);
            yield return AudioPlaybackManager.LoadSfx();
            prepared = true;
            UnityEngine.Debug.Log("OFFLINE BOARD: native card prefabs and local SFX prepared; gameplay remains diagnostic only.");
        }

        private static void RequireDiagnostic()
        {
            if (!IsOffline || !IsBoardMode)
                throw new InvalidOperationException("Native board preparation requires explicit offline board diagnostics or offline play.");
        }

        public static void CloseLocalMatch()
        {
            RequireDiagnostic();
            GameNetworkManager network = GameNetworkManager.Instance;
            NetworkField("_exiting").SetValue(network, true);
            AccessTools.Method(typeof(GameNetworkManager), "EndConnectionToServer", Type.EmptyTypes).Invoke(network, null);
            NetworkField("_matchNetworkClient").SetValue(network, null);
            RuneProphecyRestoration.Clear(OfflinePracticeSession.LastSimulation);
            transitionPending = false;
            prepared = false;
        }

        public static void LaunchValidatedPractice(GameConfiguration config)
        {
            if (!OfflinePracticeSession.Enabled || !OrdinaryMatchRules.Active)
                throw new InvalidOperationException("Offline practice rules and session must be ready.");
            LaunchScenario(config, OrdinaryMatchRules.DefaultScenarioId);
        }

        // Story chapter: the native launcher passes the scenario id through as Campaign
        // mode; installed scenario scripts, intro modal and restrictions stay native.
        public static void LaunchTutorialChapter(GameConfiguration config, TutorialScenarioRules.Chapter chapter)
        {
            if (!OfflinePracticeSession.Enabled || chapter == null || !TutorialScenarioRules.Active
                || TutorialScenarioRules.Current != chapter)
                throw new InvalidOperationException("Offline tutorial rules and session must be ready for this chapter.");
            LaunchScenario(config, chapter.ScenarioId);
        }

        private static void LaunchScenario(GameConfiguration config, int scenarioId)
        {
            RequirePresentationObjects();
            if (!installed || !prepared) throw new InvalidOperationException("Native board preparation is incomplete.");
            GameManager manager = GameManager.Instance;
            OfflineDeckCatalog.ValidateForPlay(config, manager.SelectedDeck);
            OfflineDeckCatalog.ValidateForPlay(config, manager.AiOpponentSelectedDeck);
            GameNetworkManager network = GameNetworkManager.Instance;
            if (!network.IsOutsideOfMatch() || network.LoadGameBoardDelegate != null)
                throw new InvalidOperationException("A native match or scene transition is already active.");
            network.DebugScenarioId = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.Id == scenarioId; }).ResourceId;
            network.LoadGameBoardDelegate = delegate { network.StartCoroutine(LoadNativeBoard(network)); };
            transitionPending = true;
            launchingDiagnostic = true;
            try { network.StartSinglePlayerMatch(false, false, false); }
            catch { network.LoadGameBoardDelegate = null; CloseLocalMatch(); throw; }
            finally { launchingDiagnostic = false; }
        }

        private static void RequirePresentationObjects()
        {
            if (GameNetworkManager.Instance == null || Singleton<SettingsManager>.Instance == null ||
                Singleton<WinManager>.Instance == null || Singleton<LoadingScreenManager>.Instance == null ||
                Singleton<LoadingScreenManager>.Instance.LoadingWin == null)
                throw new InvalidOperationException("Native network/settings/windows/loading objects are absent.");
            if (Singleton<WinManager>.Instance.GetUiInputController() == null)
                throw new InvalidOperationException("Native WinManager UI input controller is absent.");
        }

        // This invokes the original launcher, with AI deliberately disabled during
        // board/transport bring-up. Bootstrap must explicitly permit this scoped call.
        // No fixture, deck, avatar or simulation-readiness claims are created here.
        public static void LaunchNativeDiagnostic()
        {
            RequireDiagnostic();
            RequirePresentationObjects();
            if (!installed || !prepared) throw new InvalidOperationException("Native board preparation is incomplete.");
            GameManager manager = GameManager.Instance;
            if (manager.SelectedDeck == null || manager.AiOpponentSelectedDeck == null)
                throw new InvalidOperationException("Explicit reviewed local diagnostic decks are required.");
            GameNetworkManager network = GameNetworkManager.Instance;
            if (!network.IsOutsideOfMatch()) throw new InvalidOperationException("A native match is already active.");
            if (network.LoadGameBoardDelegate != null)
                throw new InvalidOperationException("Another board scene transition is already registered.");
            network.LoadGameBoardDelegate = delegate { network.StartCoroutine(LoadNativeBoard(network)); };
            transitionPending = true;
            launchingDiagnostic = true;
            try { network.StartSinglePlayerMatch(false, true, false); }
            catch
            {
                network.LoadGameBoardDelegate = null;
                throw;
            }
            finally { launchingDiagnostic = false; }
        }

        private static IEnumerator LoadNativeBoard(GameNetworkManager network)
        {
            RequireDiagnostic();
            Singleton<SettingsManager>.Instance.HasGameLoaded = true;
            LoadingScreenManager loading = Singleton<LoadingScreenManager>.Instance;
            if (!loading.LoadingWin.gameObject.activeInHierarchy)
            {
                loading.SetLoadingWinActive(true, network.GetInstanceID(), true, false, false, 0f);
                yield return new WaitForSeconds(0.5f);
            }
            if (network.IsOutsideOfMatch()) throw new InvalidOperationException("Native local match did not start; board transition cancelled.");
            AudioPlaybackManager.FadeBgMusicVolumeOut(delegate
            {
                OfflineCardTeardown.BeforeSceneUnload();
                OfflineMusicTeardown.BeforeSceneUnload();
                OfflineMusicPreparation.Release();
                SceneManager.LoadScene("CDGameBoard");
            });
        }
    }
}
