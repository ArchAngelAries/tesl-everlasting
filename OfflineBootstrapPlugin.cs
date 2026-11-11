using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AssetBundles;
using BepInEx;
using HarmonyLib;
using SPTESL;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    [BepInPlugin("local.tesl.offline", "TESL: Everlasting", "1.0.0")]   // The Elder Scrolls: Legends - Everlasting (the GUID stays)
    [BepInDependency("TeslRebornPlugin", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class OfflineBootstrapPlugin : BaseUnityPlugin
    {
        public static OfflineBootstrapPlugin Instance;
        public static bool Active;
        public static GameConfiguration LoadedConfiguration;
        public static event Action<GameConfiguration> ConfigurationLoaded;
        private string status = "Preparing offline startup";
        private string failure;
        private bool started;
        private bool ready;
        private float startupTime;

        // Hidden runs (audits, diagnostics, card renders: always -batchmode) are silent; a player's game never is.
        private static IEnumerator MuteHiddenRun()
        {
            while (true) { AudioListener.volume = 0f; yield return new WaitForSeconds(1f); }
        }

        private void Awake()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode") >= 0) StartCoroutine(MuteHiddenRun());
            if (OfflineNativeFlow.Requested)
            {
                // Native first-run flow: original boot/menus against local services.
                // Mutually exclusive with the --tesl-offline test bench below.
                try
                {
                    Harmony native = new Harmony("local.tesl.offline.native");
                    Patch(native, typeof(AssetBundleManager), "SetDevelopmentAssetBundleServer", "UseLocalBundlePath");
                    OfflineNativeFlow.Install(native, this);
                }
                catch (Exception ex)
                {
                    // Fail closed: never continue into the online boot.
                    Logger.LogError("OFFLINE NATIVE: startup failed; quitting instead of going online. " + ex);
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
                Active = false;
                return;
            }
            Active = Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline") >= 0;
            if (!Active)
            {   // online launch: local visual/audio mods and graphics options only (OfflineOnlineSafe); never anything else
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-no-mods") < 0) OfflineOnlineSafe.Install(this);
                return;
            }
            Instance = this;
            startupTime = Time.realtimeSinceStartup;
            try
            {
                Harmony harmony = new Harmony("local.tesl.offline");
                Patch(harmony, typeof(ServicesManager), "Update", "SuppressOriginal");
                Patch(harmony, typeof(SparkleServices.Analytics), "CheckForSends", "SuppressOriginal");
                Patch(harmony, typeof(ServicesManager), "SetCurrentLanguage", "SetOfflineLanguage");
                Patch(harmony, typeof(BNETAuth), "StartProcess", "SuppressOriginal");
                Patch(harmony, typeof(FetchConfig), "StartProcess", "SuppressOriginal");
                Patch(harmony, typeof(Steam), "ConnectToSteam", "FalseResult");
                Patch(harmony, typeof(InitializationState), "Start", "ReplaceInitialization");
                Patch(harmony, typeof(AssetCatalog), "PlatformDownloadsBundles", "FalseResult");
                Patch(harmony, typeof(PlatformUtils), "PlatformDownloadsBundles", "FalseResult");
                Patch(harmony, typeof(AssetBundleManager), "SetDevelopmentAssetBundleServer", "UseLocalBundlePath");
                Patch(harmony, typeof(GameConfiguration), "EnsureFullConfigurationLoad", "PreserveLoadedConfiguration");
                MethodInfo httpSend = AccessTools.Method(typeof(BestHTTP.HTTPManager), "SendRequest", new Type[] { typeof(BestHTTP.HTTPRequest) });
                if (httpSend == null) throw new MissingMethodException("BestHTTP.HTTPManager.SendRequest(HTTPRequest)");
                harmony.Patch(httpSend, new HarmonyMethod(typeof(OfflineBootstrapPlugin), "BlockHttp"), null, null, null, null);
                // Match restoration remains gated until both rule data and engine tests pass.
                Patch(harmony, typeof(GameNetworkManager), "StartSinglePlayerMatch", "BlockUnvalidatedMatch");
                Logger.LogInfo("OFFLINE: startup guards installed; Steam, Bethesda and remote services are inactive. AuthData remains unset.");
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void Patch(Harmony harmony, Type target, string method, string prefix)
        {
            MethodInfo original = AccessTools.Method(target, method);
            if (original == null) throw new MissingMethodException(target.FullName, method);
            harmony.Patch(original, new HarmonyMethod(typeof(OfflineBootstrapPlugin), prefix), null, null, null, null);
        }

        private void InstallRestoration(Harmony harmony, string name)
        {
            Type type = GetType().Assembly.GetType("OfflineEngine." + name);
            MethodInfo install = type == null ? null : AccessTools.Method(type, "Install", new Type[] { typeof(Harmony) });
            if (install == null) { Logger.LogWarning("OFFLINE: " + name + " is not part of this build."); return; }
            install.Invoke(null, new object[] { harmony });
            Logger.LogInfo("OFFLINE: installed " + name);
        }

        private IEnumerator Start()
        {
            if (!Active || failure != null) yield break;
            while (AssetCatalog.Instance == null)
            {
                if (Time.realtimeSinceStartup - startupTime > 30f)
                {
                    Fail(new TimeoutException("The game's AssetCatalog component did not become available."));
                    yield break;
                }
                yield return null;
            }
            if (!started)
            {
                started = true;
                yield return Guard(LoadInstalledConfiguration());
            }
        }

        private static bool SuppressOriginal() { return false; }
        private static bool SetOfflineLanguage(ServicesManager __instance, LanguageCode __0, string __1)
        {
            // Native localization bookkeeping without Shop/Inventory service refreshes.
            AccessTools.Field(typeof(ServicesManager), "_currentLanguage").SetValue(__instance, __0);
            AccessTools.Field(typeof(ServicesManager), "_localizedLanguage").SetValue(__instance, __1);
            return false;
        }
        private static bool BlockUnvalidatedMatch()
        {
            Type adapter = typeof(OfflineBootstrapPlugin).Assembly.GetType("OfflineEngine.OfflineBoardAdapter");
            if (adapter != null && (bool)adapter.GetProperty("IsLaunchingValidated").GetValue(null, null)) return true;
            throw new NotSupportedException("Offline matches are not ready: installed rule data and engine restoration must pass validation first.");
        }
        private static bool FalseResult(ref bool __result) { __result = false; return false; }
        private static bool ReplaceInitialization(ref IEnumerator __result) { __result = Empty(); return false; }
        private static IEnumerator Empty() { yield break; }

        private static bool UseLocalBundlePath()
        {
            AssetBundleManager.BaseDownloadingURL = new Uri(Application.streamingAssetsPath + Path.DirectorySeparatorChar).AbsoluteUri;
            return false;
        }

        private static bool PreserveLoadedConfiguration(GameConfiguration __instance)
        {
            if (__instance != LoadedConfiguration || !__instance.initialized)
                throw new InvalidOperationException("Offline configuration is not ready; source-only developer configuration will not be loaded.");
            return false;
        }

        private static bool BlockHttp(BestHTTP.HTTPRequest __0)
        {
            // No HTTP is needed by this bootstrap; local bundles use AssetBundle.LoadFromFileAsync.
            string host = __0 == null || __0.Uri == null ? "unknown host" : __0.Uri.Host;
            throw new InvalidOperationException("Offline mode blocked an unexpected HTTP request to " + host);
        }

        private IEnumerator LoadInstalledConfiguration()
        {
            SetStatus("Loading installed asset manifests");
            if (!File.Exists(Path.Combine(Application.streamingAssetsPath, "windows")))
                throw new FileNotFoundException("The installed Windows asset manifest is missing.");
            yield return AssetCatalog.Instance.InitializeLocalResources();
            AssetCatalog.PreloadChoiceMade = true;
            AssetCatalog.PreloadChoice = false;
            // Nonempty file URL avoids consulting the original development-server setting.
            yield return AssetCatalog.Instance.Initialize(new Uri(Application.streamingAssetsPath + Path.DirectorySeparatorChar).AbsoluteUri);
            SetStatus("Reading the installed game configuration");
            string configPath = GameConfiguration.BinaryAssetPath + "little_endian_game_configuration";
            AssetBundleLoadAssetOperation operation = AssetCatalog.Instance.LoadAssetFromPathAsync(configPath, typeof(TextAsset), "bytes");
            if (operation == null) throw new InvalidOperationException("The local manifest does not contain the game configuration binary.");
            yield return operation;
            TextAsset binary = operation.GetAsset<TextAsset>();
            if (binary == null || binary.bytes.Length == 0) throw new InvalidOperationException("The installed game configuration binary could not be read.");
            string exportDirectory = OfflinePaths.Exports();
            Directory.CreateDirectory(exportDirectory);
            File.WriteAllBytes(Path.Combine(exportDirectory, "little_endian_game_configuration.bytes"), binary.bytes);
            Logger.LogInfo("OFFLINE: exported installed configuration binary (" + binary.bytes.Length + " bytes).");
            yield return GameManager.Instance.Initialize();
            GameConfiguration config = GameManager.Instance.Config;
            if (config == null || !config.initialized || config.CardCollection == null || config.CardCollection.Count == 0)
                throw new InvalidOperationException("The installed game configuration did not deserialize into a nonempty card collection.");
            LoadedConfiguration = config;
            yield return LoadOriginalLocalization(config, exportDirectory);
            WriteAudit(config, exportDirectory);
            ready = true;
            SetStatus("Installed data loaded. Offline match restoration is under validation.");
            Action<GameConfiguration> callback = ConfigurationLoaded;
            if (callback != null) callback(config);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-play") >= 0)
            {
                yield return OfflinePracticeSession.Initialize(config, exportDirectory);
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-board-diagnostic") >= 0)
            {
                Type diagnostic = GetType().Assembly.GetType("OfflineEngine.OfflineBoardDiagnostic");
                if (diagnostic == null) throw new InvalidOperationException("Board diagnostic is absent from this build.");
                yield return (IEnumerator)diagnostic.GetMethod("Run").Invoke(null, new object[] { config, exportDirectory });
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-engine-tests") >= 0)
            {
                Type tests = GetType().Assembly.GetType("OfflineEngine.EngineTests");
                if (tests == null) throw new InvalidOperationException("Engine test suite is absent from this build.");
                int failures = (int)tests.GetMethod("Run").Invoke(null, new object[] { config, exportDirectory });
                Logger.LogInfo("OFFLINE ENGINE TESTS COMPLETE: " + failures + " failures; exiting local diagnostic.");
                Application.Quit();
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-audit-exit") >= 0)
            {
                Logger.LogInfo("OFFLINE AUDIT COMPLETE: exiting diagnostic run.");
                Application.Quit();
            }
        }

        private IEnumerator LoadOriginalLocalization(GameConfiguration config, string directory)
        {
            SetStatus("Loading original installed English card text");
            LocalizationManager localization = LocalizationManager.Instance;
            if (localization == null) throw new InvalidOperationException("Native localization manager is missing.");
            localization.Initialize(LocalizedLanguage.en_US);
            AssetBundleLoadAssetOperation operation = AssetCatalog.Instance.LoadAssetFromPathAsync(
                "AppBase/Localization/Text/en_US/data", typeof(TextAsset), "json");
            if (operation == null) throw new InvalidOperationException("Original English localization asset is absent.");
            yield return operation;
            TextAsset original = operation.GetAsset<TextAsset>();
            if (original == null) throw new InvalidOperationException("Original English localization asset failed to load.");
            File.WriteAllBytes(Path.Combine(directory, "localization-original-en_US.json"), original.bytes);
            object parsed = null;
            new FullSerializer.fsSerializer().TryDeserialize(FullSerializer.fsJsonParser.Parse(original.text),
                typeof(Dictionary<string, string>), ref parsed).AssertSuccessWithoutWarnings();
            Dictionary<string, string> originalText = parsed as Dictionary<string, string>;
            if (originalText == null || originalText.Count == 0) throw new InvalidOperationException("Original localization dictionary is empty.");
            localization.SetLanguage(LocalizedLanguage.en_US, false);
            float deadline = Time.realtimeSinceStartup + 60f;
            while (localization.IsLoading())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Native full localization did not finish.");
                yield return null;
            }
            // Allow Reborn's local translation overlay to finish; original bundle remains separately preserved.
            yield return null;
            StringBuilder report = new StringBuilder("card_id\tname_key\tdescription_key\toriginal_name_present\toriginal_description_present\toriginal_name\toriginal_description\teffective_name_present\teffective_description_present\teffective_name\teffective_description\n");
            int cards = 0, names = 0, descriptions = 0;
            foreach (CardSet set in config.CardCollection)
            {
                if (set.Cards == null) continue;
                foreach (Card card in set.Cards)
                {
                    string nameKey = card.NameKey.RawKey, descriptionKey = card.DescriptionKey.RawKey;
                    string name, description, effectiveName, effectiveDescription;
                    bool namePresent = nameKey != null && originalText.TryGetValue(nameKey, out name);
                    bool descriptionPresent = descriptionKey != null && originalText.TryGetValue(descriptionKey, out description);
                    originalText.TryGetValue(nameKey ?? "", out name);
                    originalText.TryGetValue(descriptionKey ?? "", out description);
                    bool effectiveNamePresent = localization.TryGetText(nameKey, out effectiveName);
                    bool effectiveDescriptionPresent = localization.TryGetText(descriptionKey, out effectiveDescription);
                    if (namePresent) names++;
                    if (descriptionPresent) descriptions++;
                    cards++;
                    report.AppendLine(card.ResourceId.Id + "\t" + Tsv(nameKey) + "\t" + Tsv(descriptionKey) + "\t" + namePresent + "\t" + descriptionPresent + "\t" + Tsv(name) + "\t" + Tsv(description) + "\t" + effectiveNamePresent + "\t" + effectiveDescriptionPresent + "\t" + Tsv(effectiveName) + "\t" + Tsv(effectiveDescription));
                }
            }
            File.WriteAllText(Path.Combine(directory, "card-localization-en_US.tsv"), report.ToString());
            File.WriteAllText(Path.Combine(directory, "localization-audit.tsv"), "field\tcount\nOriginalKeys\t" + originalText.Count + "\nCards\t" + cards + "\nOriginalNamesPresent\t" + names + "\nOriginalDescriptionsPresent\t" + descriptions + "\n");
            if (names == 0) throw new InvalidOperationException("Original localization resolved no installed card names.");
            Logger.LogInfo("OFFLINE LOCALIZATION: " + originalText.Count + " original keys; " + names + "/" + cards + " card names and " + descriptions + " descriptions recovered.");
        }

        private void WriteAudit(GameConfiguration config, string directory)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("field\tcount");
            string[] fields = { "CardCollection", "CardDefinitions", "Zones", "PlayerDefinitions", "Scenarios", "CustomDecks", "AiOpponents", "CommonAiWeights", "LaneTriggers" };
            foreach (string field in fields)
                report.AppendLine(field + "\t" + Count(typeof(GameConfiguration).GetField(field).GetValue(config)));
            int cards = 0;
            string[] rules = { "TriggeredEffects", "Auras", "ActionPlayInformation", "PlayRequirements", "BetrayRequirements", "ActivatedAbility", "AttackRequirements" };
            int[] nonnull = new int[rules.Length];
            int[] nonempty = new int[rules.Length];
            int[] entries = new int[rules.Length];
            StringBuilder detail = new StringBuilder("card_id\tcard_name\tdefinition");
            foreach (string field in rules) detail.Append("\t" + field);
            detail.AppendLine();
            foreach (CardSet set in config.CardCollection)
            {
                if (set.Cards == null) continue;
                foreach (Card card in set.Cards)
                {
                    cards++;
                    detail.Append(card.ResourceId.Id + "\t" + Tsv(card.Name) + "\t" + Tsv(card.Definition));
                    for (int i = 0; i < rules.Length; i++)
                    {
                        object value = typeof(Card).GetField(rules[i]).GetValue(card);
                        int count = Count(value);
                        if (value != null) nonnull[i]++;
                        if (count > 0) { nonempty[i]++; entries[i] += count; }
                        detail.Append("\t" + count);
                    }
                    detail.AppendLine();
                }
            }
            report.AppendLine("Cards\t" + cards);
            for (int i = 0; i < rules.Length; i++)
            {
                report.AppendLine("Card." + rules[i] + ".NonNull\t" + nonnull[i]);
                report.AppendLine("Card." + rules[i] + ".NonEmpty\t" + nonempty[i]);
                report.AppendLine("Card." + rules[i] + ".Entries\t" + entries[i]);
            }
            File.WriteAllText(Path.Combine(directory, "configuration-audit.tsv"), report.ToString());
            File.WriteAllText(Path.Combine(directory, "card-rule-audit.tsv"), detail.ToString());
            Logger.LogInfo("OFFLINE CONFIG AUDIT:\n" + report.ToString());
            if (nonempty[0] == 0 || nonempty[2] == 0)
                Logger.LogWarning("OFFLINE: installed card effects or action data are absent. Asset loading does not establish a playable rules engine.");
        }

        private static int Count(object value)
        {
            if (value == null) return -1;
            ICollection collection = value as ICollection;
            return collection == null ? 1 : collection.Count;
        }
        private static string Tsv(string value) { return value == null ? "" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        private IEnumerator Guard(IEnumerator routine)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(routine);
            float deadline = Time.realtimeSinceStartup + 180f;
            bool boardBudget = false;
            while (stack.Count > 0)
            {
                object current = null;
                bool moved = false;
                Exception error = null;
                try
                {
                    if (ready && !boardBudget && Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-board-diagnostic") >= 0)
                    {
                        // Loading retains its original deadline. The separately bounded
                        // board fixture includes a 90-second launch and 240-second match.
                        deadline = Time.realtimeSinceStartup + 420f;
                        boardBudget = true;
                    }
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Offline asset/configuration load exceeded 180 seconds.");
                    moved = stack.Peek().MoveNext();
                    if (moved) current = stack.Peek().Current;
                }
                catch (Exception ex) { error = ex; }
                if (error != null) { Fail(error); yield break; }
                if (!moved) { stack.Pop(); continue; }
                IEnumerator nested = current as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        private void SetStatus(string value) { status = value; Logger.LogInfo("OFFLINE: " + value); }
        private void Fail(Exception error) { failure = error.ToString(); status = "Offline startup stopped: " + error.Message; Logger.LogError("OFFLINE: " + failure); }

        private void OnGUI()
        {
            if (!Active) return;
            if (OfflinePracticeSession.Instance != null && failure == null) return;
            GUI.depth = -10000;
            GUI.Box(new Rect(16, 16, 650, 138), "TESL Offline Engine — development build");
            GUI.Label(new Rect(32, 48, 620, 60), status);
            GUI.Label(new Rect(32, 100, 470, 40), ready ? "Data audit saved in .analysis/offline-engine/exports" : "Loading installed files; online authentication is inactive.");
            if (GUI.Button(new Rect(550, 112, 100, 28), "Close game")) Application.Quit();
        }
    }
}
