using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Native first-run flow (explicit flag --tesl-offline-native): the original boot,
    // InitializationState, menus and story run unmodified against OfflineLocalServices.
    // Only these are changed: config points at the loopback services; auth uses the
    // original developer path (AuthType.Dev) with the local player's name/identity;
    // Steam, analytics uploads, PubSub sockets and remote bundle downloads stay off; any
    // non-loopback HTTP fails closed.
    public static class OfflineNativeFlow
    {
        public const string Flag = "--tesl-offline-native";
        public const string DiagnosticFlag = "--tesl-offline-native-diagnostic";
        public static bool Active { get; private set; }
        public static OfflineLocalProfile Profile { get; private set; }
        private static string exportDirectory;

        public const string SeedFlag = "--tesl-offline-native-seed=";
        private static string seed;

        private static string Seed()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(SeedFlag, StringComparison.Ordinal))
                {
                    string name = arg.Substring(SeedFlag.Length);
                    foreach (char c in name) if (!char.IsLetterOrDigit(c) && c != '-') throw new ArgumentException("Seed must be a fixture name.");
                    return name;
                }
            return null;
        }

        // Offline by flag, by the saved choice (launch.json) or because TESL-Reborn is absent (OfflineLaunchMode).
        public static bool Requested { get { return OfflineLaunchMode.WantsOffline(); } }

        private static Harmony nativeHarmony;
        private static MonoBehaviour nativeHost;

        public static void Install(Harmony harmony, MonoBehaviour host)
        {
            nativeHarmony = harmony; nativeHost = host;
            Active = true;
            // Guard first: any failure below must never fall back to the online boot.
            harmony.Patch(AccessTools.Method(typeof(BestHTTP.HTTPManager), "SendRequest", new Type[] { typeof(BestHTTP.HTTPRequest) }),
                new HarmonyMethod(typeof(OfflineNativeFlow), "LoopbackOnly"), null, null, null, null);
            string root = Path.GetDirectoryName(Application.dataPath);
            exportDirectory = OfflinePaths.Exports();
            OfflinePackService.StoreArtDirectory = Path.GetFullPath(OfflinePaths.Data("store-art"));
            Directory.CreateDirectory(exportDirectory);
            Profile = OfflineLocalProfile.Open(root);
            bool diagnostic = Array.IndexOf(Environment.GetCommandLineArgs(), DiagnosticFlag) >= 0;
            // Diagnostics always start from a fresh, throwaway first-run player; card previews (TESL Forge) use their own folder.
            string renderFolder = OfflineCardRender.Folder();
            string statePath = renderFolder != null ? Path.Combine(renderFolder, "render-player-state.json")
                : diagnostic ? Path.Combine(exportDirectory, "native-diagnostic-player-state.json")
                : Path.Combine(Path.GetDirectoryName(Profile.StoragePath), "player-state.json");
            if (renderFolder != null) { Directory.CreateDirectory(renderFolder); if (File.Exists(statePath)) File.Delete(statePath); host.StartCoroutine(OfflineCardRender.Run(renderFolder)); }
            if (diagnostic && File.Exists(statePath)) File.Delete(statePath);
            // Optional diagnostic seed: a saved player state from offline-engine/fixtures (name only).
            seed = Seed();
            SelectMenuScript();
            string seedFile = seed == null ? null : Path.Combine(Path.Combine(OfflinePaths.EngineRoot(), "fixtures"), seed + ".json");   // development only
            if (diagnostic && seedFile != null && File.Exists(seedFile)) File.Copy(seedFile, statePath);
            OfflinePlayerState state = new OfflinePlayerState(statePath);
            modState = state;
            OfflineLocalServices.Start(exportDirectory, Profile, state);
            // Diagnostics and card previews are throwaway players: their extension data (daily calendars ...) too.
            if (diagnostic || renderFolder != null)
            {
                string extRoot = Path.Combine(Path.GetDirectoryName(statePath), diagnostic ? "native-diagnostic-user-data" : "render-user-data");
                if (Directory.Exists(extRoot)) Directory.Delete(extRoot, true);
                EverlastingHost.SetDataRoot(extRoot);
            }
            EverlastingHost.SetState(state);
            EverlastingHost.ApplyRoutes(OfflineLocalServices.Instance);
            OfflineCampaignService.Register(OfflineLocalServices.Instance, state);
            OfflineRewardsService.Register(OfflineLocalServices.Instance, state);
            OfflineInventoryService.Register(OfflineLocalServices.Instance, state);
            OfflinePackService.Register(OfflineLocalServices.Instance, state);
            OfflineArenaService.Register(OfflineLocalServices.Instance, state);
            OfflineQuestService.Register(OfflineLocalServices.Instance, state);
            OfflineRankedService.Register(OfflineLocalServices.Instance, state);
            OfflineNativeMatch.Register(OfflineLocalServices.Instance, state, exportDirectory, harmony, host);
            Prefix(harmony, typeof(FetchConfig), "StartProcess", "UseLocalConfig", Priority.Last);
            Postfix(harmony, typeof(ServicesManager), "Start", "UseDevAuth");
            Prefix(harmony, typeof(PubSub), "Update", "Skip", Priority.Normal);
            Prefix(harmony, typeof(SparkleServices.Analytics), "CheckForSends", "Skip", Priority.Normal);
            Prefix(harmony, typeof(Steam), "ConnectToSteam", "FalseResult", Priority.Normal);
            Prefix(harmony, typeof(AssetCatalog), "PlatformDownloadsBundles", "FalseResult", Priority.Normal);
            Prefix(harmony, typeof(PlatformUtils), "PlatformDownloadsBundles", "FalseResult", Priority.Normal);
            Prefix(harmony, typeof(SparkleServices.Analytics), "SendEvent", "Skip", Priority.Normal);
            host.StartCoroutine(PublishAvatarsWhenConfigReady());
            host.StartCoroutine(PublishQuestsWhenLocalized());
            OfflineNameEntry.Install(harmony, host, state);
            OfflineCheatMenu.Install(host, state);
            OfflineMods.Install(harmony);
            OfflineStories.Install(harmony);
            OfflineFairPlay.Install(harmony);
            OfflineEmotes.Install(harmony, host);
            OfflineBoards.Install(harmony);
            OfflineMusic.Install(harmony, host);
            OfflineAudio.Install(harmony, host);
            OfflineGoldStore.Install(host);
            harmony.Patch(AccessTools.Method(typeof(SPTESL.BoardInputController), "ReceiveCardCommandState"), null, new HarmonyMethod(typeof(OfflineNativeFlow).GetMethod("ReceiveCommandsPostfix")), null, null, null);
            OfflineGraphics.Install(host, Path.Combine(root, ".offline-user-data/graphics.json"));
            OfflineStoreArt.Install(harmony);
            Debug.Log("OFFLINE NATIVE: local services " + OfflineLocalServices.Instance.RootUrl + "; player '" + Profile.Username + "'.");
            if (diagnostic) host.StartCoroutine(Diagnose());
        }

        private static void Prefix(Harmony harmony, Type type, string method, string patch, int priority)
        {
            System.Reflection.MethodInfo original = AccessTools.Method(type, method);
            if (original == null) throw new MissingMethodException(type.FullName, method);
            HarmonyMethod prefix = new HarmonyMethod(typeof(OfflineNativeFlow), patch);
            prefix.priority = priority;
            harmony.Patch(original, prefix, null, null, null, null);
        }

        private static void Postfix(Harmony harmony, Type type, string method, string patch)
        {
            System.Reflection.MethodInfo original = AccessTools.Method(type, method);
            if (original == null) throw new MissingMethodException(type.FullName, method);
            harmony.Patch(original, null, new HarmonyMethod(typeof(OfflineNativeFlow), patch), null, null, null);
        }

        // Runs after any other plugin's prefix (e.g. a server-redirect plugin) and wins.
        private static void UseLocalConfig(ref string localConfigUrl, ref string configUrl, ref bool attemptLocalConnect)
        {
            localConfigUrl = OfflineLocalServices.Instance.ConfigUrl;
            configUrl = OfflineLocalServices.Instance.ConfigUrl;
            attemptLocalConnect = false;
        }

        private static void UseDevAuth(ServicesManager __instance)
        {
            AuthDevSettings dev = ScriptableObject.CreateInstance<AuthDevSettings>();
            dev.AuthType = AuthBase.AuthType.Dev;
            dev.Nickname = Profile.Username;
            dev.AccountId = Profile.Identity;
            dev.DevAuthSecret = "offline-local";
            __instance.AuthDevSettings = dev;
        }

        private static bool Skip() { return false; }
        private static bool FalseResult(ref bool __result) { __result = false; return false; }

        // Allow only our loopback services. Anything else is failed natively (Error state,
        // finished coroutine) so callers take their normal error path instead of hanging.
        private static bool LoopbackOnly(BestHTTP.HTTPRequest __0, ref BestHTTP.HTTPRequest __result)
        {
            OfflineLocalServices services = OfflineLocalServices.Instance;
            if (__0 != null && __0.Uri != null && services != null && __0.Uri.Host == "127.0.0.1" && __0.Uri.Port == services.Port) return true;
            string host = __0 == null || __0.Uri == null ? "unknown host" : __0.Uri.Host;
            Debug.LogWarning("OFFLINE NATIVE: blocked non-local HTTP request to " + host);
            if (__0 != null)
            {
                // Internal setters in BestHTTP (not publicized): set via reflection.
                AccessTools.PropertySetter(typeof(BestHTTP.HTTPRequest), "Exception").Invoke(__0, new object[] { new InvalidOperationException("Offline native mode blocked a non-local HTTP request to " + host) });
                AccessTools.PropertySetter(typeof(BestHTTP.HTTPRequest), "State").Invoke(__0, new object[] { BestHTTP.HTTPRequestStates.Error });
            }
            __result = __0;
            return false;
        }

        // Quest goals are reconstructed from localized texts: wait for config and localization.
        // The installed TESL Reborn plugin postfixes PlayerProfileTitleItem.UpdateUi and dereferences _titleData, which is
        // still null when an item is first enabled (before SetData). The exception escapes UIPopupWindow.Show and aborts the
        // profile's Titles window. Offline-native only: skip that postfix while the item has no data (plugin file untouched).
        private static void GuardRebornTitleItemPatch()
        {
            Type patch = null;
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "TeslRebornPlugin") { patch = assembly.GetType("TeslRebornPlugin.Patch.Player.PlayerProfileTitleItemPatch", false); break; }
            System.Reflection.MethodInfo postfix = patch == null ? null : AccessTools.Method(patch, "Postfix_UpdateUi");
            if (postfix == null || nativeHarmony == null) return;
            nativeHarmony.Patch(postfix, new HarmonyMethod(typeof(OfflineNativeFlow).GetMethod("RebornTitleItemGuard")), null, null, null, null);
            Debug.Log("OFFLINE NATIVE: guarded the Reborn title-item postfix against items without data.");
        }

        public static bool RebornTitleItemGuard(PlayerProfileTitleItem __0)
        { return __0 != null && AccessTools.Field(typeof(PlayerProfileTitleItem), "_titleData").GetValue(__0) != null; }

        // Modding Suite mods (OfflineMods): applied once, before any catalog, title or rule publication.
        private static OfflinePlayerState modState;
        private static void ApplyMods()
        {
            try
            {
                if (OfflineMods.EnsureApplied(SPTESL.GameManager.Instance.Config, null) > 0 && modState != null)
                    modState.Mutate(delegate(OfflinePlayerStateData d) { OfflineMods.GrantOwnership(d); });
            }
            catch (Exception e) { Debug.LogError("OFFLINE MODS: not applied: " + e); }
        }

        private static IEnumerator PublishQuestsWhenLocalized()
        {
            while (SPTESL.GameManager.Instance == null || SPTESL.GameManager.Instance.Config == null || !SPTESL.GameManager.Instance.Config.initialized
                || LocalizationManager.Instance == null || LocalizationManager.Instance.IsLoading())
                yield return new WaitForSeconds(0.5f);
            OfflineGraphics.Timed("ApplyMods", delegate { ApplyMods(); });
            OfflineGraphics.Timed("GuardRebornTitleItemPatch", delegate { GuardRebornTitleItemPatch(); });
            OfflineGraphics.Timed("OfflineQuestService.Publish", delegate { OfflineQuestService.Publish(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-quest-goals.tsv")); });
            OfflineGraphics.Timed("OfflineCosmetics.Publish", delegate { OfflineCosmetics.Publish(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-titles.tsv")); });
            OfflineGraphics.Timed("OfflineRankedService.PublishMonthlies", delegate { OfflineRankedService.PublishMonthlies(SPTESL.GameManager.Instance.Config); });
            OfflineGraphics.Timed("OfflineTitles.Publish", delegate { OfflineTitles.Publish(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-title-goals.tsv")); });
            OfflineGraphics.Timed("OfflineGoldStore.PublishTexts", delegate { OfflineGoldStore.PublishTexts(SPTESL.GameManager.Instance.Config); });
            // The client fetched the store catalog before these names and images existed: fetch it again.
            if (Pixelplacement.Singleton<ServicesManager>.Instance != null && Pixelplacement.Singleton<ServicesManager>.Instance.Shop != null)
                Pixelplacement.Singleton<ServicesManager>.Instance.Shop.Refresh();
            ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
            if (services != null && services.Quests != null) services.Quests.Refresh();
            if (services != null && services.GenericInventory != null) services.GenericInventory.Refresh();
        }

        private static IEnumerator BuyFromGoldStore()
        {
            yield return new WaitForSeconds(3f);
            ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
            StringBuilder log = new StringBuilder();
            // Seeded test profile only: enough gold for both purchases.
            if (modState != null) { modState.Mutate(delegate(OfflinePlayerStateData d) { d.gold += 1000; }); services.Profile.Refresh(); yield return new WaitForSeconds(2f); }
            foreach (string prefix in new string[] { "deck_", "offline_cardback" })
            {
                SparkleServices.Shop.CatalogItem item = services.Shop.ChannelCatalog.items.Find(delegate(SparkleServices.Shop.CatalogItem i) {
                    return i.itemKey.StartsWith(prefix, StringComparison.Ordinal) && i.itemState == SparkleServices.Shop.CatalogItem.IChannelCatalogItemState.Normal; });
                if (item == null) { log.Append(prefix).Append(": no offer\n"); continue; }
                int goldBefore = services.Profile.ProfileDescription.gold, decksBefore = services.Inventory.InventoryDescription == null ? -1 : services.Inventory.InventoryDescription.decks.Count;
                bool done = false;
                services.Shop.PurchaseItem(item.itemKey, 1, true, delegate { done = true; });
                for (int i = 0; i < 40 && !done; i++) yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(4f);   // profile, inventory, cosmetics and shop refreshes
                SparkleServices.Shop.CatalogItem after = services.Shop.ChannelCatalog.items.Find(delegate(SparkleServices.Shop.CatalogItem i) { return i.itemKey == item.itemKey; });
                log.Append(item.itemKey).Append(" price ").Append(item.priceGold).Append(": done=").Append(done)
                   .Append(" gold ").Append(goldBefore).Append("->").Append(services.Profile.ProfileDescription.gold)
                   .Append(" decks ").Append(decksBefore).Append("->").Append(services.Inventory.InventoryDescription == null ? -1 : services.Inventory.InventoryDescription.decks.Count)
                   .Append(" stillListed=").Append(after != null).Append("\n");
            }
            File.WriteAllText(Path.Combine(exportDirectory, "store-buy.tsv"), log.ToString());
            Debug.Log("OFFLINE DIAGNOSTIC: store purchases\n" + log);
        }

        // Diagnostic: every command list the client receives while CommandLogOn (time, list uid, commands, display cards, choices).
        private static bool CommandLogOn; private static readonly StringBuilder CommandLog = new StringBuilder("t\tlist\tcommands\tdisplay\tchoices\tmust\n");
        public static void ReceiveCommandsPostfix(CcgRpc.CardCommandList commandList)
        {
            if (!CommandLogOn || commandList == null) return;
            int display = 0, choices = 0;
            foreach (CcgRpc.CardCommandState c in commandList.ValidCommands) { display += c.DisplayCards.Count; choices = Math.Max(choices, c.NumActionChoices); }
            CommandLog.Append(Time.realtimeSinceStartup.ToString("0.000")).Append('\t').Append(commandList.ListUid).Append('\t').Append(commandList.ValidCommands.Count)
                .Append('\t').Append(display).Append('\t').Append(choices).Append('\t').Append(commandList.MustSelectCommand).Append('\n');
        }

        private static IEnumerator ShowModAvatarPage()
        {
            foreach (Pixelplacement.StateMachine machine in UnityEngine.Object.FindObjectsOfType<Pixelplacement.StateMachine>())
                if (machine.transform.Find("AvatarPickerState") != null) { machine.ChangeState("AvatarPickerState"); break; }
            AvatarPickerWin win = null;
            for (int i = 0; i < 40 && (win == null || !win.gameObject.activeInHierarchy); i++)
            {
                yield return new WaitForSeconds(0.25f);
                win = UnityEngine.Object.FindObjectOfType<AvatarPickerWin>();
            }
            if (win == null) { Debug.LogError("OFFLINE DIAGNOSTIC: avatar picker did not open"); yield break; }
            yield return new WaitForSeconds(1f);
            int page = OfflineMods.AvatarPages.Count == 0 ? -1 : SPTESL.GameManager.Instance.Config.AvatarCollection.Races.IndexOf(OfflineMods.AvatarPages[0]);
            if (AvatarPageOfReplaced)
            {
                SPTESL.AvatarCollection col = SPTESL.GameManager.Instance.Config.AvatarCollection;
                SPTESL.AvatarData remastered = col.Avatars.Find(delegate(SPTESL.AvatarData a) { return a != null && a.Image != null && OfflineMods.Replaced.Exists(delegate(string r) { return r.EndsWith(a.Image, StringComparison.OrdinalIgnoreCase) || a.Image.EndsWith(r, StringComparison.OrdinalIgnoreCase); }); });
                page = remastered == null ? -1 : col.Races.FindIndex(delegate(SPTESL.AvatarRaceData r) { return r.ResourceId.Id == remastered.RaceId.resourceId; });
                Debug.Log("OFFLINE DIAGNOSTIC: remastered original avatar " + (remastered == null ? "none" : remastered.ResourceId.UniqueName) + " on page " + page);
            }
            UnityEngine.UI.Toggle[] toggles = (UnityEngine.UI.Toggle[])AccessTools.Field(typeof(AvatarPickerWin), "_avatarToggles").GetValue(win);
            Debug.Log("OFFLINE DIAGNOSTIC: avatar picker open; slots per page " + (toggles == null ? -1 : toggles.Length) + "; mod page index " + page);
            if (page < 0) yield break;
            AccessTools.Method(typeof(AvatarPickerWin), "UpdateVisibleRaceIndex").Invoke(win, new object[] { page });
            AccessTools.Method(typeof(AvatarPickerWin), "UpdateUi").Invoke(win, new object[0]);
            yield return new WaitForSeconds(2f);
            OfflineStoryDiagnostic.Capture(Path.Combine(exportDirectory, "avatar-picker-page.png"));
        }
        private static bool AvatarPageOfReplaced;

        // The match engine (rule import) needs the original graphs decoded: spread over frames first, then prepare.
        private static IEnumerator PrepareEngineGradually()
        {
            yield return OriginalGraphRules.Predecode(exportDirectory, 8f);
            System.Collections.IEnumerator stages = OfflineNativeMatch.PrepareEngineGradually();
            while (true)
            {
                bool more;
                try { more = stages.MoveNext(); }
                catch (Exception ex) { Debug.LogError("OFFLINE NATIVE: engine preparation failed: " + ex); yield break; }
                if (!more) break;
                yield return stages.Current;
            }
        }

        private static IEnumerator PublishAvatarsWhenConfigReady()
        {
            while (SPTESL.GameManager.Instance == null || SPTESL.GameManager.Instance.Config == null || !SPTESL.GameManager.Instance.Config.initialized
                || SPTESL.GameManager.Instance.Config.AvatarCollection == null || SPTESL.GameManager.Instance.Config.AvatarCollection.Avatars == null)
                yield return new WaitForSeconds(0.25f);
            OfflineGraphics.Timed("ApplyMods", delegate { ApplyMods(); });
            List<string> names = new List<string>();
            foreach (SPTESL.AvatarData avatar in SPTESL.GameManager.Instance.Config.AvatarCollection.Avatars)
                if (avatar != null && !string.IsNullOrEmpty(avatar.ResourceId.UniqueName)) names.Add(avatar.ResourceId.UniqueName);
            // The original account always had a selected avatar; new players start on the
            // configured FallbackAvatar until they pick one.
            int fallbackId = SPTESL.GameManager.Instance.Config.AvatarCollection.FallbackAvatar.resourceId;
            SPTESL.AvatarData fallback = SPTESL.GameManager.Instance.Config.AvatarCollection.Avatars.Find(delegate(SPTESL.AvatarData a) { return a != null && a.ResourceId.Id == fallbackId; });
            OfflineGraphics.Timed("OfflineLocalServices.PublishDefaultAvatar", delegate { OfflineLocalServices.PublishDefaultAvatar(fallback == null ? (names.Count > 0 ? names[0] : "") : fallback.ResourceId.UniqueName); });
            OfflineGraphics.Timed("OfflineLocalServices.PublishAvatars", delegate { OfflineLocalServices.PublishAvatars(names.ToArray()); });
            // The client may have fetched before config loaded; refresh through its own path.
            ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
            if (services != null && services.GenericInventory != null) services.GenericInventory.Refresh();
            OfflineGraphics.Timed("OfflineCampaignService.PublishContent", delegate { OfflineCampaignService.PublishContent(SPTESL.GameManager.Instance.Config.Campaigns); });
            OfflineGraphics.Timed("OfflineCampaignService.DumpContent", delegate { OfflineCampaignService.DumpContent(Path.Combine(exportDirectory, "native-campaign-content.tsv")); });
            SPTESL.Scenario puzzle = SPTESL.GameManager.Instance.Config.Scenarios.Find(delegate(SPTESL.Scenario x) { return x.ResourceId.UniqueName == "PUZ_Caius_RedoranInitiation"; });
            if (puzzle != null) File.WriteAllText(Path.Combine(exportDirectory, "native-puzzle-redoran.txt"), OfflineObjectDump.Dump(puzzle.GameProperties, 7) + OfflineObjectDump.Dump(puzzle.TriggeredEffects, 9));
            OfflineGraphics.Timed("OfflineCampaignService.DumpQuests", delegate { OfflineCampaignService.DumpQuests(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-quests.tsv")); });
            OfflineGraphics.Timed("OfflineCampaignService.DumpScenarios", delegate { OfflineCampaignService.DumpScenarios(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-campaign-scenarios.tsv")); });
            OfflineGraphics.Timed("OfflineInventoryService.PublishCatalog", delegate { OfflineInventoryService.PublishCatalog(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-inventory-catalog.tsv")); });
            if (services != null && services.Inventory != null) services.Inventory.Refresh();
            OfflineGraphics.Timed("OfflineArenaService.Publish", delegate { OfflineArenaService.Publish(SPTESL.GameManager.Instance.Config); });
            OfflineGraphics.Timed("OfflinePackService.PublishPools", delegate { OfflinePackService.PublishPools(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-pack-pools.tsv")); });
            nativeHost.StartCoroutine(PrepareEngineGradually());
            OfflineGraphics.Timed("OfflineRewardsService.PublishCards", delegate { OfflineRewardsService.PublishCards(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "native-reward-bundles.tsv")); });
            if (services != null && services.Campaign != null) services.Campaign.Refresh();
            Debug.Log("OFFLINE NATIVE: published " + names.Count + " installed avatars to local services.");
        }

        // Hidden verification: periodic frames (overlay UI included) and visible text
        // whenever the screen text changes, plus the active state machine state.
        private static int MaxStoryMatches = 3;
        private const float SeededSeconds = 300f;
        // Menu labels a seeded diagnostic presses, in order of preference (player-equivalent clicks).
        // Seeded menu scripts (--tesl-offline-native-menu=<name>): labels (later first) and object names.
        private static string[] MenuLabels = { "STORE", "CORE SET PACK", "OPEN", "OPEN PACKS", "DONE" };
        private static string[] MenuButtonNames = { "Btn_Buy", "Item_Currency_Packs" };
        private static void SelectMenuScript()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg == "--tesl-offline-native-menu=collection")
                {
                    MenuLabels = new string[] { "COLLECTION" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=choices")
                {   // practice match; at the human seat's first decision, Explore (DrawOne custom choice) is played
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    ChoiceDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=prophecy")
                {   // practice match; on the AI's turn the human's rune breaks with Lurking Crocodile on top of the deck: the
                    // client's own Prophecy window is captured, then the Crocodile is played through the human's decision channel
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    ProphecyDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=modcard")
                {   // Modding Suite sample mod: Jarl's Housecarl (sample-skyrim) drawn and played by the human seat
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    ChoiceDiagnostic = true; ChoiceCardId = -1417084127;
                }
                if (arg == "--tesl-offline-native-menu=emotes")
                {   // practice match; at the human seat's first decision the human says Hello and the AI (voiced by the sample
                    // mod's Housecarl, text + WAV) answers; bubbles captured.
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    EmoteDiagnostic = true; OfflineEmotes.AiVoiceOverride = "mod.sample-skyrim.avatar.housecarl"; OfflineEmotes.AiPersonalityOverride = "Friendly";
                }
                if (arg == "--tesl-offline-native-menu=mode")
                {   // practice match playing the mode set in graphics.json "mode"; at the human's first decision the setup is recorded (mode-check.tsv)
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    ModeDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=opponent")
                {   // practice match against the sample mod opponent Greybeard Elder; frames and setup recorded like "mode"
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    ModeDiagnostic = true; OfflineOpponents.ForcedKey = "sample-skyrim/greybeard-elder";
                }
                if (arg == "--tesl-offline-native-menu=boardmap")
                {   // practice match; at the human seat's first decision every board is loaded in turn and mapped (OfflineBoardMap)
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    BoardMapDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=summon-target")
                {   // owner report 2026-11-05: Morkul Gatekeeper (Summon: Give a creature +2/+0) stayed hovering when played
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    ChoiceDiagnostic = true; ChoiceCardId = 1106955994;
                }
                if (arg == "--tesl-offline-native-menu=choices-camel")
                {
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    ChoiceDiagnostic = true; ChoiceCardId = -1693406656;
                }
                if (arg == "--tesl-offline-native-menu=cheats")
                {   // practice match; the human seat's first decision prompt fires the offline cheats (OfflineCheats)
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                    CheatDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=practice")
                {
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PRACTICE", "NOVICE" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=ranked")
                {
                    MenuLabels = new string[] { "PLAY", "VERSUS PLAY", "RANKED PLAY", "PLAY" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=quickplay")
                {
                    MenuLabels = new string[] { "PLAY", "QUICKPLAY", "PLAY" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=varena")
                {
                    MenuLabels = new string[] { "PLAY", "VERSUS PLAY", "VERSUS ARENA" };
                    MenuButtonNames = new string[] { "Btn_Buy_Tickets", "Item_ClassEntry" };
                }
                if (arg == "--tesl-offline-native-menu=name" || arg == "--tesl-offline-native-menu=name-restore")
                {
                    // First-run name entry: type a name, confirm with the popup's OK, then open Profile > Overview.
                    string wanted = arg.EndsWith("restore") ? "Offline Player" : "Dovahkiin";
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate {
                        OfflineNameEntry.DiagnosticSubmit(wanted);
                        Pixelplacement.Singleton<MainMenuNavManager>.Instance.SetMajorNavigable(MainMenuNavData.Id.Profile, MainMenuNavData.Id.Profile_Overview, MainMenuNavData.SlideStyle.Normal);
                    };
                    NameEntryDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=avatars-original")
                {   // the picker page of an original avatar a mod remasters (OfflineMods image replacement), captured
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    AvatarPageOfReplaced = true;
                    OneShot = delegate { nativeHost.StartCoroutine(ShowModAvatarPage()); };
                }
                if (arg == "--tesl-offline-native-menu=avatars")
                {
                    // Main menu -> the avatar picker (AvatarPickerState), turned to the first mod avatar page if any.
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate { nativeHost.StartCoroutine(ShowModAvatarPage()); };
                }
                if (arg == "--tesl-offline-native-menu=sweep")
                {
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate { nativeHost.StartCoroutine(OfflineNativeMatch.SweepScenarios(Path.Combine(exportDirectory, "native-scenario-sweep.tsv"))); };
                }
                if (arg == "--tesl-offline-native-menu=modcards")
                {   // plays every loaded mod card in a fresh native match (OfflineModCardSweep)
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate { nativeHost.StartCoroutine(OfflineModCardSweep.Run(Path.Combine(exportDirectory, "mod-card-sweep.tsv"))); };
                }
                if (arg.StartsWith("--tesl-offline-native-menu=deckgauntlet:", StringComparison.Ordinal))
                {   // scheduled AI-vs-AI matches between constructed decks, server-side only (OfflineDeckGauntlet)
                    string gauntletDir = arg.Substring("--tesl-offline-native-menu=deckgauntlet:".Length);
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate { nativeHost.StartCoroutine(OfflineDeckGauntlet.Run(gauntletDir)); };
                }
                if (arg == "--tesl-offline-native-menu=storedump")
                {
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate { nativeHost.StartCoroutine(OfflineStoreArt.Dump(Path.Combine(exportDirectory, "store-art-dump"))); };
                }
                if (arg == "--tesl-offline-native-menu=cardbackart")
                {   // every official card back's store image, loaded as the store does (exports/cardback-store-art/)
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate { nativeHost.StartCoroutine(OfflineStoreArt.DumpCardBackStoreArt(SPTESL.GameManager.Instance.Config, Path.Combine(exportDirectory, "cardback-store-art"))); };
                }
                if (arg.StartsWith("--tesl-offline-native-menu=labels:", StringComparison.Ordinal))
                {   // any path of on-screen labels, e.g. labels:STORE|SPECIAL OFFERS (each step is captured)
                    MenuLabels = arg.Substring("--tesl-offline-native-menu=labels:".Length).Split('|');
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=storebuy")
                {
                    MenuLabels = new string[] { "STORE", "PACKS", "HEROES OF SKYRIM PACK", "OPEN", "OPEN PACKS", "DONE" };
                    MenuButtonNames = new string[] { "Btn_Buy" };
                }
                if (arg == "--tesl-offline-native-menu=modpack")
                {   // buy and open the sample mod's "Skyrim Heroes Pack" (OfflineModPacks) through the store screens
                    MenuLabels = new string[] { "STORE", "PACKS", "SKYRIM HEROES PACK", "OPEN", "OPEN PACKS", "DONE" };
                    MenuButtonNames = new string[] { "Btn_Buy" };
                    OfflineModPacks.ListFirst = true;
                }
                if (arg == "--tesl-offline-native-menu=openpacks")
                {
                    MenuLabels = new string[] { "OPEN", "DONE" };
                    MenuButtonNames = new string[] { "Item_Currency_Packs" };
                    OneShotState = "PackOpenState";
                    OneShot = delegate {
                        PackOpenState open = UnityEngine.Object.FindObjectOfType<PackOpenState>();
                        if (open == null) return;
                        List<string> keys = AccessTools.Field(typeof(PackOpenState), "_validKeyList").GetValue(open) as List<string>;
                        System.Collections.IList groups = AccessTools.Field(typeof(PackOpenState), "_packGroups").GetValue(open) as System.Collections.IList;
                        StringBuilder g = new StringBuilder();
                        if (groups != null) foreach (object o in groups) g.Append(AccessTools.Property(o.GetType(), "Key") != null ? AccessTools.Property(o.GetType(), "Key").GetValue(o, null) : AccessTools.Field(o.GetType(), "Key").GetValue(o)).Append(' ');
                        Debug.Log("OFFLINE PACKS: valid keys " + (keys == null ? "?" : string.Join(",", keys.ToArray())) + " | groups " + g);
                    };
                }
                if (arg == "--tesl-offline-native-menu=store")
                {
                    MenuLabels = new string[] { "STORE", "PACKS" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=voicecheck")
                {   // every attached mod voice clip, originals untouched, shout levels (OfflineVoiceCheck)
                    MenuLabels = new string[] { "COLLECTION" };
                    MenuButtonNames = new string[0];
                    OneShotState = "DeckEditorState";
                    OneShot = delegate { nativeHost.StartCoroutine(OfflineVoiceCheck.Run(exportDirectory)); };
                }
                if (arg == "--tesl-offline-native-menu=audio")
                {   // mod card voice lines and a replaced original sound through the client's own audio path (OfflineAudio)
                    MenuLabels = new string[] { "COLLECTION" };
                    MenuButtonNames = new string[0];
                    OneShotState = "DeckEditorState";
                    OneShot = delegate { nativeHost.StartCoroutine(OfflineAudio.Diagnose(exportDirectory)); };
                }
                if (arg == "--tesl-offline-native-menu=store-buy")
                {   // gold store purchase through the client's own Shop.PurchaseItem (the Buy button's path): a deck, then a card back
                    MenuLabels = new string[] { "STORE", "SPECIAL OFFERS" };
                    MenuButtonNames = new string[0];
                    OneShotState = "StoreState";
                    OneShot = delegate { nativeHost.StartCoroutine(BuyFromGoldStore()); };
                }
                if (arg == "--tesl-offline-native-menu=store-special" || arg == "--tesl-offline-native-menu=store-stories")
                {   // gold store (OfflineGoldStore): decks and card backs, or story acts
                    MenuLabels = new string[] { "STORE", arg.EndsWith("special") ? "SPECIAL OFFERS" : "STORIES" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=profile")
                {
                    // Main menu -> Profile -> Overview (arena rank items, ranked widgets) through client navigation.
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate {
                        Pixelplacement.Singleton<MainMenuNavManager>.Instance.SetMajorNavigable(MainMenuNavData.Id.Profile, MainMenuNavData.Id.Profile_Overview, MainMenuNavData.SlideStyle.Normal);
                    };
                }
                if (arg == "--tesl-offline-native-menu=cardbacks")
                {
                    // Collection -> the card-backs view (the client's own toggle handler).
                    MenuLabels = new string[] { "COLLECTION" };
                    MenuButtonNames = new string[0];
                    OneShotState = "DeckEditorState";
                    OneShot = delegate {
                        SPTESL.MainMenuCardCollection collection = UnityEngine.Object.FindObjectOfType<SPTESL.MainMenuCardCollection>();
                        if (collection != null) AccessTools.Method(typeof(SPTESL.MainMenuCardCollection), "HandleOnCardBacksToggleChanged").Invoke(collection, new object[] { true });
                    };
                }
                if (arg == "--tesl-offline-native-menu=titles")
                {
                    // Main menu -> Profile -> Titles through the client's own navigation (PlayerProfileState/PlayerProfileNavWin).
                    MenuLabels = new string[0];
                    MenuButtonNames = new string[0];
                    OneShotState = "MainMenu";
                    OneShot = delegate {
                        Pixelplacement.Singleton<MainMenuNavManager>.Instance.SetMajorNavigable(MainMenuNavData.Id.Profile, MainMenuNavData.Id.Profile_Titles, MainMenuNavData.SlideStyle.Normal);
                    };
                }
                if (arg == "--tesl-offline-native-menu=versus")
                {
                    MenuLabels = new string[] { "PLAY", "VERSUS PLAY", "CASUAL PLAY", "PLAY" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=arena")
                {
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "SOLO ARENA" };
                    MenuButtonNames = new string[] { "Btn_Buy_Tickets", "Item_ClassEntry" };
                }
                if (arg == "--tesl-offline-native-menu=dbh")
                {
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "STORIES", "THE FALL OF THE DARK BROTHERHOOD", "CHOOSE" };
                    MenuButtonNames = new string[] { "Item_Chapter_Episode" };
                    coinMaps = true;
                }
                if (arg == "--tesl-offline-native-menu=cwc" || arg == "--tesl-offline-native-menu=iom")
                {
                    // Story titles as shown; spelling variants are harmless (unmatched labels never appear).
                    string[] titles = arg.EndsWith("cwc")
                        ? new string[] { "RETURN TO CLOCKWORK CITY", "CLOCKWORK CITY" }
                        : new string[] { "ISLE OF MADNESS", "THE ISLE OF MADNESS" };
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "STORIES", titles[0], titles[1], "CHOOSE" };
                    MenuButtonNames = new string[] { "Item_Chapter_Episode" };
                    coinMaps = true;
                }
                if (arg == "--tesl-offline-native-menu=modstory")
                {   // the sample mod story (OfflineStories): tile, episode, dialogue coins and choices, battles
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "STORIES", "THE GREENWOOD SAGA", "The Greenwood Saga", "CHOOSE",
                        "Continue", "CONTINUE", "Take the gold", "TAKE THE GOLD", "Charge", "CHARGE" };
                    MenuButtonNames = new string[] { "Item_Chapter_Episode" };
                    coinMaps = true;
                    StoryWinDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=modstory2")
                {   // seed native-mod-story-ch2-player-state: Episode 2 of the sample story (second episode slot), "Charge" picks battle 2
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "STORIES", "THE GREENWOOD SAGA", "The Greenwood Saga", "CHOOSE", "Continue", "CONTINUE", "Charge", "CHARGE" };
                    MenuButtonNames = new string[] { "Item_Chapter_Episode (1)" };
                    coinMaps = true;
                    StoryWinDiagnostic = true;
                }
                if (arg == "--tesl-offline-native-menu=modstory-long")
                {   // the many-acts sample story: its episode screen only
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "STORIES", "THE LONG ROAD", "The Long Road" };
                    MenuButtonNames = new string[0];
                }
                if (arg == "--tesl-offline-native-menu=puzzles")
                {
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "PUZZLES", "CAIUS' TRAINING" };
                    MenuButtonNames = new string[0];
                    coinMaps = true;
                }
                if (arg == "--tesl-offline-native-menu=story")
                {
                    MenuLabels = new string[] { "PLAY", "SOLO PLAY", "STORIES", "THE FORGOTTEN HERO", "CHOOSE" };
                    MenuButtonNames = new string[0];
                }
            }
        }

        private static IEnumerator Diagnose()
        {
            string prefix = Path.Combine(exportDirectory, "native-boot-");
            CheatCapturePrefix = prefix;
            StringBuilder texts = new StringBuilder("step\tseconds\tstate\ttext\n");
            float start = Time.realtimeSinceStartup;
            string last = null; int step = 0;
            StringBuilder parity = new StringBuilder("seconds\tserver_phase\tturns\tstatus\tserver_board\tclient_board\n");
            float resultAt = -1f; int results = 0;
            while (Time.realtimeSinceStartup - start < 1500f && step < 400)
            {
                SPTESL.Simulation sim = OfflineNativeMatch.CurrentSimulation;
                bool inMatch = sim != null && sim.Players.Count == 2 && sim._gameState != null && sim._gameState.GamePhase == SPTESL.GamePhase.MainLoop;
                yield return new WaitForSeconds(inMatch ? 2f : 3f);
                OfflineGraphics.Mark("diag-step");
                if (inMatch) { yield return PaceHumanSeat(sim, parity, start); File.WriteAllText(prefix + "parity.tsv", parity.ToString()); }
                // Plays the first-run story chain; stops 150 s after the last result with no new match.
                if (OfflineNativeMatch.ResultsRecorded != results) { results = OfflineNativeMatch.ResultsRecorded; resultAt = Time.realtimeSinceStartup; }
                if (resultAt > 0f && !inMatch && Time.realtimeSinceStartup - resultAt > 150f) break;
                if (seed != null && results == 0 && OfflineNativeMatch.CurrentSimulation == null && Time.realtimeSinceStartup - start > SeededSeconds) break;
                List<string> visible = OfflineStoryDiagnostic.VisibleTexts();
                if (visible.Contains("Tap to open the Elder Scroll!")) AdvanceOnboarding();
                string state = CurrentState();
                // countdown clocks (an event's "31:29:54") tick every second; a screen counts as settled without them
                string signature = state + "|" + System.Text.RegularExpressions.Regex.Replace(string.Join("|", visible.ToArray()), @"\b\d+:\d\d(:\d\d)?\b", "#");
                if (signature == last)
                {
                    // Seeded menu runs: also keep a settled frame of a screen that stopped changing.
                    if (seed != null && !settledCaptured) { settledCaptured = true; yield return new WaitForEndOfFrame(); OfflineStoryDiagnostic.Capture(prefix + "step" + step.ToString("00") + "-settled.png"); }
                    PressDialogButton(texts, step); continue;
                }
                settledCaptured = false;
                last = signature; step++;
                float seconds = Time.realtimeSinceStartup - start;
                texts.Append(step).Append('\t').Append(seconds.ToString("0")).Append('\t').Append(state).Append("\t(state)\n");
                foreach (string line in visible) texts.Append(step).Append('\t').Append(seconds.ToString("0")).Append('\t').Append(state).Append('\t').AppendLine(line);
                // Interactable buttons (parent/name = label), to drive menu flows by their own controls.
                if (seed != null)
                    foreach (UnityEngine.UI.Button button in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>())
                        if (button.isActiveAndEnabled && button.interactable)
                            texts.Append(step).Append("\t-\t(button)\t").Append(button.transform.parent == null ? "" : button.transform.parent.name + "/")
                                .Append(button.name).Append(" = ").AppendLine(OfflineStoryDiagnostic.Label(button));
                File.WriteAllText(prefix + "texts.tsv", texts.ToString());
                yield return new WaitForEndOfFrame();
                OfflineStoryDiagnostic.Capture(prefix + "step" + step.ToString("00") + ".png");
            }
            File.WriteAllText(prefix + "texts.tsv", texts.ToString());
            File.WriteAllText(prefix + "parity.tsv", parity.ToString());
            File.WriteAllText(prefix + "match.tsv", "field\tvalue\nscenario\t" + OfflineNativeMatch.CurrentScenarioName + "\nchapter_event\t"
                + OfflineNativeMatch.CurrentChapterEventId + "\nresult\t" + OfflineNativeMatch.LastResult + "\nhistory\t" + OfflineNativeMatch.ResultHistory
                + "\nwindy_moves\t" + StoryLaneRules.WindyMoves + "\nplunder_equips\t" + StoryLaneRules.PlunderEquips + "\n");
            Debug.Log("OFFLINE NATIVE DIAGNOSTIC COMPLETE: steps=" + step + " result=" + OfflineNativeMatch.LastResult);
            yield return new WaitForSeconds(1f);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        // Diagnostic autoplay: the reconstructed strategy plays the human seat for a moment,
        // only while the client board matches the server (as Run-StoryDiagnostic -Autoplay).
        private static int mismatchStreak;
        private static bool ProphecyDiagnostic, prophecyDone;
        private static bool CheatDiagnostic, cheatsDone, ChoiceDiagnostic, choiceDone, EmoteDiagnostic, emoteDone, BoardMapDiagnostic, boardMapDone, ModeDiagnostic;
        private static int modeSnapshots, modeLastTurn = -1;
        private static int ChoiceCardId = -415371858; // Explore (action, DrawOne); choices-camel: Merchant's Camel (Summon, SelectOneToKeep)

        // Diagnostic: the human seat plays Explore through its own decision channel; frames show the hand, the client's
        // custom-choice popup and the result after choice 0.
        // Draws a card for the human (announced like a native draw) and plays it into lane 0 through the decision channel.
        private static IEnumerator DrawAndPlay(SPTESL.Simulation sim, SPTESL.PlayerState human, int cardId, StringBuilder parity)
        {
            SPTESL.TurnState t = sim._turnStateStack.Peek();
            int seat = t.ActivePlayerIndex;
            SPTESL.ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(sim, t);
            SPTESL.ServerCard card = null;
            sim.HistoryLog.StartEvent(CcgRpc.HistoryType.EventTrigger, human.NetId, SPTESL.CardNetUid.GetInvalid(), SPTESL.CardNetUid.GetInvalid(), -1);
            try
            {
                card = SPTESL.ServerCardCatalog.CreateCard(gs, cardId, false, SPTESL.GameZoneName.Deck, 0, SPTESL.CardInsertionType.Top, human.NetId, null, null);
                SPTESL.PlayerActions.DrawCards.StaticExecute(gs, human, 1, SPTESL.PlayerActions.DrawCards.DrawDeck.Self, SPTESL.ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1);
                SPTESL.PlayerActions.SetMana.StaticExecute(gs, human, 10);
            }
            finally { sim.HistoryLog.EndEvent(); }
            OfflineDoubleCards.SplitHands(sim, t);
            t.CurrentCommandItem = sim.CardCatalog.GenerateCardCommandItem(human, sim.Players[1 - seat], sim.Players);
            sim.SendSequenceHistoryToClients();
            yield return new WaitForSeconds(3f);
            CcgRpc.CardCommandList list = t.CurrentCommandItem.CommandList;
            sim.PlayerServiceClients[seat]._decisionMessages.Enqueue(new CcgRpc.Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid,
                ActorCardNetUid = card.NetUid.Id, TargetCardNetUid = SPTESL.CardNetUid.GetInvalid().Id, EquipCardNetUid = SPTESL.CardNetUid.GetInvalid().Id, LaneIndex = 0 });
            yield return new WaitForSeconds(5f);
            parity.Append("-\tfirst\t-\t").Append(cardId).Append(" zone=").Append(card.CurrentGameZoneName).Append("\t\n");
        }

        private static IEnumerator PlayChoiceCard(SPTESL.Simulation sim, SPTESL.PlayerState human, StringBuilder parity)
        {
            if (ChoiceCardId == 1106955994) yield return DrawAndPlay(sim, human, 620729069, parity);   // a friendly creature to target first
            SPTESL.TurnState t = sim._turnStateStack.Peek();
            int seat = t.ActivePlayerIndex;
            SPTESL.ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(sim, t);
            SPTESL.ServerCard card = null;
            sim.HistoryLog.StartEvent(CcgRpc.HistoryType.EventTrigger, human.NetId, SPTESL.CardNetUid.GetInvalid(), SPTESL.CardNetUid.GetInvalid(), -1);
            try
            {
                card = SPTESL.ServerCardCatalog.CreateCard(gs, ChoiceCardId, false, SPTESL.GameZoneName.Deck, 0, SPTESL.CardInsertionType.Top, human.NetId, null, null);
                // a native draw announces the card to the client (a bare CreateCard into the hand does not)
                SPTESL.PlayerActions.DrawCards.StaticExecute(gs, human, 1, SPTESL.PlayerActions.DrawCards.DrawDeck.Self, SPTESL.ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1);
                SPTESL.PlayerActions.SetMana.StaticExecute(gs, human, 10);
            }
            finally { sim.HistoryLog.EndEvent(); }
            OfflineDoubleCards.SplitHands(sim, t);
            t.CurrentCommandItem = sim.CardCatalog.GenerateCardCommandItem(human, sim.Players[1 - seat], sim.Players);
            sim.SendSequenceHistoryToClients();
            yield return new WaitForSeconds(3f);
            OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "choice-hand.png");
            CcgRpc.CardCommandList list = t.CurrentCommandItem.CommandList;
            CcgRpc.CardCommandState command = null;
            foreach (CcgRpc.CardCommandState c in list.ValidCommands) if (c.CardNetUid == card.NetUid.Id) command = c;
            string log = "explore inHand=" + (card != null && card.CurrentGameZoneName == SPTESL.GameZoneName.Hand) + " playable=" + (command != null && (command.ValidCardCommandMask & 15) != 0);
            if (command != null)
            {
                // As the client sends it: a Summon that targets carries its target with the play (the first one offered).
                int target = command.PlayTargets.Count > 0 ? command.PlayTargets[0] : SPTESL.CardNetUid.GetInvalid().Id;
                log += " targets=" + command.PlayTargets.Count + " prompt=" + command.TargetingPromptLocKey + " lookup=" + CommandRestoration.PromptOf(card) + " effects=" + DescribeEffects(card.CardId) + " text=\"" + (string.IsNullOrEmpty(command.TargetingPromptLocKey) || LocalizationManager.Instance == null ? "" : LocalizationManager.Instance.GetText(command.TargetingPromptLocKey)) + "\"";
                CcgRpc.Decision play = new CcgRpc.Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = card.NetUid.Id,
                    TargetCardNetUid = target, LaneIndex = 0, PositionIndex = target > 0 ? 1 : 0 };   // as GameNetworkManager.PlayCardWithTarget: no equip field, the drop slot (after the creature already there)
                sim.PlayerServiceClients[seat]._decisionMessages.Enqueue(play);
                CommandLogOn = true;
                for (int f = 0; f < 12; f++) { yield return new WaitForSeconds(0.15f); OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "choice-burst-" + f + ".png"); }
                for (int shot = 1; shot <= 4; shot++)
                {
                    yield return new WaitForSeconds(4f);
                    OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "choice-popup-" + shot + ".png");
                    File.WriteAllText(CheatCapturePrefix + "choice-texts-" + shot + ".tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
                }
                CommandLogOn = false; File.WriteAllText(CheatCapturePrefix + "choice-commands.tsv", CommandLog.ToString());
                File.WriteAllText(CheatCapturePrefix + "choice-texts.tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
                SPTESL.TurnState top = sim._turnStateStack.Peek();
                CcgRpc.Decision choose = top.CurrentCommandItem == null ? null : OfflineConsumeChoices.Choose(top.CurrentCommandItem.CommandList);
                if (choose == null && top.CurrentCommandItem != null)   // a Summon's target prompt: its first target
                    foreach (CcgRpc.CardCommandState c in top.CurrentCommandItem.CommandList.ValidCommands)
                        if ((c.ValidCardCommandMask & (int)SPTESL.CardCommand.ChooseTarget) != 0 && c.PlayTargets.Count > 0)
                        {
                            log += " prompt=" + c.TargetingPromptLocKey + " cancel=" + ((c.ValidCardCommandMask & (int)SPTESL.CardCommand.DeclineToPlay) != 0) + " zoneAtPrompt=" + card.CurrentGameZoneName;
                            choose = new CcgRpc.Decision { Type = CcgRpc.DecisionType.Play, CommandUid = top.CurrentCommandItem.CommandList.ListUid, ActorCardNetUid = c.CardNetUid, TargetCardNetUid = c.PlayTargets[0] };
                            break;
                        }
                log += " choiceOffered=" + (choose != null) + (choose == null ? "" : " choices=" + top.CurrentCommandItem.CommandList.ValidCommands[0].NumActionChoices);
                int hand = sim.CardCatalog.FindCardCount(SPTESL.GameZoneName.Hand, human.NetId);
                if (choose != null) sim.PlayerServiceClients[top.ActivePlayerIndex]._decisionMessages.Enqueue(choose);
                yield return new WaitForSeconds(5f);
                OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "choice-after.png");
                log += " hand " + hand + "->" + sim.CardCatalog.FindCardCount(SPTESL.GameZoneName.Hand, human.NetId) + " exploreZone=" + card.CurrentGameZoneName;
            }
            parity.Append("-\tchoices\t-\t").Append(log).Append("\t\n");
        }
        // Diagnostic: the human's rune breaks on the AI's turn with a Prophecy on top of the deck. The client must show its
        // Prophecy window (frames + texts), and the free play must resolve and hand the turn back to the AI.
        private static IEnumerator HumanProphecy(SPTESL.Simulation sim, SPTESL.PlayerState human, StringBuilder parity)
        {
            int seat = sim.Players.IndexOf(human);
            SPTESL.TurnState t = sim._turnStateStack.Peek();
            SPTESL.ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(sim, t);
            SPTESL.ServerCard avatar = sim.CardCatalog.FindCardInDynamicZone(delegate(SPTESL.ServerCard c) { return c.HasSubtype(SPTESL.CardSubtype.Avatar); }, human.NetId, SPTESL.GameZoneName.AvatarBoard);
            int health = avatar == null ? 0 : avatar.CurrentHealth, target = 5 * ((health - 1) / 5);
            string log = "health " + health + "->" + target + " runes " + human.QueryAttribute(SPTESL.AttributeType.NumRunes).Value;
            if (avatar == null || target <= 0) { parity.Append("-\tprophecy\t-\tskipped: ").Append(log).Append("\t\n"); yield break; }
            SPTESL.ServerCard card = null;
            sim.HistoryLog.StartEvent(CcgRpc.HistoryType.EventTrigger, sim.CurrentPlayer.NetId, SPTESL.CardNetUid.GetInvalid(), SPTESL.CardNetUid.GetInvalid(), -1);
            try
            {
                card = SPTESL.ServerCardCatalog.CreateCard(gs, NativeKeywordRules.LurkingCrocodileId, false, SPTESL.GameZoneName.Deck, 0, SPTESL.CardInsertionType.Top, human.NetId, null, null);
                SPTESL.PlayerActions.DamageAvatarOfPlayer.StaticExecute(gs, null, human, health - target, -1);
            }
            finally { sim.HistoryLog.EndEvent(); }
            sim.SendSequenceHistoryToClients();
            bool window = false;
            for (int i = 0; i < 60 && !window; i++)
            {
                yield return new WaitForSeconds(0.25f);
                SPTESL.TurnState top = sim._turnStateStack.Peek();
                window = sim._turnStateStack.Count == 2 && top.ActivePlayerIndex == seat && top.CurrentCommandItem != null && top.CurrentSequence == null
                    && card.CurrentGameZoneName == SPTESL.GameZoneName.Prophecy;
            }
            log += " window=" + window + " zone=" + card.CurrentGameZoneName + " depth=" + sim._turnStateStack.Count;
            for (int f = 0; f < 6; f++) { yield return new WaitForSeconds(0.5f); OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "prophecy-window-" + f + ".png"); }
            yield return new WaitForSeconds(0.5f);
            File.WriteAllText(CheatCapturePrefix + "prophecy-window-texts.tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
            if (window)
            {
                SPTESL.TurnState top = sim._turnStateStack.Peek();
                CcgRpc.CardCommandList list = top.CurrentCommandItem.CommandList;
                CcgRpc.Decision play = DecisionRestoration.ChooseDecision(sim, list);
                log += " offered=" + list.ValidCommands.Count + " decision=" + (play == null ? "none" : play.Type.ToString());
                if (play != null) sim.PlayerServiceClients[seat]._decisionMessages.Enqueue(play);
                for (int i = 0; i < 40 && sim._turnStateStack.Count != 1; i++) yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(3f);
                OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "prophecy-after.png");
                yield return new WaitForSeconds(0.5f);
                log += " after: zone=" + card.CurrentGameZoneName + " depth=" + sim._turnStateStack.Count + " active="
                    + (sim.Players[sim._turnStateStack.Peek().ActivePlayerIndex] == human ? "human" : "ai")
                    + " parity=" + (OfflineStoryDiagnostic.ServerBoard(sim) == OfflineStoryDiagnostic.ClientBoard() ? "match" : "differs");
            }
            parity.Append("-\tprophecy\t-\t").Append(log).Append("\t\n");
        }
        private static string DescribeEffects(int cardId)
        {
            SPTESL.Card def = SPTESL.GameManager.Instance.Config.FindCardById(cardId);
            if (def == null) return "no card";
            List<string> parts = new List<string>();
            foreach (SPTESL.TriggeredEffect e in def.TriggeredEffects) parts.Add(e.TriggerType + "/" + e.ClientTargetingType + "/'" + e.TargetingPrompt.RawKey + "'");
            return parts.Count + "[" + string.Join(";", parts.ToArray()) + "]";
        }
        private static string CheatCapturePrefix;
        private static bool StoryWinDiagnostic;
        private static IEnumerator PaceHumanSeat(SPTESL.Simulation sim, StringBuilder parity, float start)
        {
            SPTESL.PlayerState human = sim.Players.Find(delegate(SPTESL.PlayerState p) { return p.PlayerType == (int)SPTESL.PlayerType.Normal; });
            if (human == null) yield break;
            if (ProphecyDiagnostic && !prophecyDone && OfflineCheats.Unavailable(sim) == null && sim._turnStateStack.Count == 1)
            {
                SPTESL.TurnState t = sim._turnStateStack.Peek();
                // the first settled moment of either turn (AI turns rarely sit still at a 2 s tick)
                if (t.Phase == SPTESL.TurnPhase.PlayCards && t.CurrentSequence == null)
                {
                    prophecyDone = true;
                    yield return HumanProphecy(sim, human, parity);
                    yield break;
                }
            }
            if (ChoiceDiagnostic && !choiceDone && OfflineCheats.Unavailable(sim) == null && sim.Players[sim._turnStateStack.Peek().ActivePlayerIndex] == human)
            {
                choiceDone = true;
                yield return PlayChoiceCard(sim, human, parity);
                yield break;
            }
            // modstory: the story flow is under test, not the autopilot; the human wins with the offline Win cheat on turn 3.
            if (StoryWinDiagnostic && OfflineCheats.Unavailable(sim) == null && sim.Players[sim._turnStateStack.Peek().ActivePlayerIndex] == human
                && human.QueryAttribute(SPTESL.AttributeType.TurnCount).Value >= 3)
            {
                parity.Append("cheat win ").Append(OfflineCheats.Apply(sim, OfflineCheats.MatchCheat.Win) ?? "ok").AppendLine();
                yield break;
            }
            if (ModeDiagnostic && modeSnapshots < 2 && OfflineCheats.Unavailable(sim) == null && sim.Players[sim._turnStateStack.Peek().ActivePlayerIndex] == human
                && human.QueryAttribute(SPTESL.AttributeType.TurnCount).Value != modeLastTurn)
            {
                modeLastTurn = human.QueryAttribute(SPTESL.AttributeType.TurnCount).Value;
                modeSnapshots++;
                yield return new WaitForSeconds(2.5f);
                OfflineStoryDiagnostic.Capture(Path.Combine(exportDirectory, "mode-" + modeSnapshots + ".png"));
                StringBuilder m = new StringBuilder("what\tvalue\n");
                m.Append("mode\t").Append(OfflineModes.Active == null ? "none" : OfflineModes.Active.Key).Append('\n');
                m.Append("scenario\t").Append(OfflineNativeMatch.CurrentScenarioName).Append('\n');
                m.Append("ai opponent\t").Append(OfflineOpponents.Current == null ? "none" : OfflineOpponents.Current.Key).Append('\n');
                m.Append("counters\tturns ").Append(OfflineModes.TurnsSeen).Append(" placed ").Append(OfflineModes.CardsPlaced).Append(" rules ").Append(OfflineModes.RulesApplied).Append('\n');
                foreach (SPTESL.PlayerState p in sim.Players)
                {
                    string who = p.PlayerType == (int)SPTESL.PlayerType.Normal ? "player" : "opponent";
                    SPTESL.ServerCard avatar = sim.CardCatalog.FindCardInDynamicZone(delegate(SPTESL.ServerCard c) { return c.HasSubtype(SPTESL.CardSubtype.Avatar); }, p.NetId, SPTESL.GameZoneName.AvatarBoard);
                    m.Append(who).Append("\thealth ").Append(avatar == null ? -1 : avatar.CurrentHealth).Append(" maxMagicka ").Append(avatar == null ? -1 : avatar.QueryAttribute(SPTESL.AttributeType.MaxMana).Value)
                     .Append(" runes ").Append(p.QueryAttribute(SPTESL.AttributeType.NumRunes).Value).Append(" hand ").Append(sim.CardCatalog.FindCardCount(SPTESL.GameZoneName.Hand, p.NetId)).Append('\n');
                    for (int lane = 0; lane < 2; lane++)
                    {
                        int laneIndex = lane;
                        List<SPTESL.ServerCard> cards = sim.CardCatalog.GatherCardsInZone(delegate(SPTESL.ServerCard c) { return c.CurrentLaneIndex == laneIndex; }, p.NetId, SPTESL.GameZoneName.CreatureBoard);
                        List<string> names = new List<string>();
                        foreach (SPTESL.ServerCard c in cards) { SPTESL.Card def = SPTESL.GameManager.Instance.Config.FindCardById(c.CardId); names.Add(def == null ? c.CardId.ToString() : def.Name); }
                        m.Append(who).Append("\tlane").Append(lane + 1).Append(' ').Append(string.Join(", ", names.ToArray())).Append('\n');
                    }
                }
                File.AppendAllText(Path.Combine(exportDirectory, "mode-check.tsv"), "snapshot " + modeSnapshots + "\n" + m.ToString());
                yield return new WaitForSeconds(2f);   // the capture is written at the end of a frame
                if (modeSnapshots == 1)
                {   // end the human's turn through the client's own end-turn input so the AI plays one turn
                    SPTESL.BoardInputController board = UnityEngine.Object.FindObjectOfType<SPTESL.BoardInputController>();
                    if (board != null) AccessTools.Method(typeof(SPTESL.BoardInputController), "OnButtonActionClicked").Invoke(board, new object[] { SPTESL.BoardInputController.InputCommandType.Action_EndTurn });
                }
                yield break;
            }
            if (BoardMapDiagnostic && !boardMapDone && OfflineCheats.Unavailable(sim) == null && sim.Players[sim._turnStateStack.Peek().ActivePlayerIndex] == human)
            {
                boardMapDone = true;
                yield return OfflineBoardMap.Diagnose(exportDirectory);
                yield break;
            }
            if (EmoteDiagnostic && !emoteDone && OfflineCheats.Unavailable(sim) == null && sim.Players[sim._turnStateStack.Peek().ActivePlayerIndex] == human)
            {
                emoteDone = true;
                SPTESL.BoardInputController board = UnityEngine.Object.FindObjectOfType<SPTESL.BoardInputController>();
                int before = OfflineEmotes.AiEmotesSent;
                UserEmotesWin wheel = UnityEngine.Object.FindObjectOfType<UserEmotesWin>();
                if (wheel == null) foreach (UserEmotesWin w in Resources.FindObjectsOfTypeAll<UserEmotesWin>()) if (w.gameObject.scene.IsValid()) wheel = w;
                if (wheel != null)
                {
                    wheel.EmoteUi_Show(true);
                    yield return new WaitForSeconds(1.2f);
                    OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "emote-panel.png");
                    File.WriteAllText(CheatCapturePrefix + "emote-panel.tsv", OfflineEmotes.DescribeWheel(wheel));
                    if (OfflineExtraEmotes.For(OfflineExtraEmotes.PlayerPackage()).Count > 0)
                    {   // the "More" page, then the first extra through its own button (FindButtonEmoteType -> Emote_TryToSend)
                        yield return new WaitForSeconds(0.5f);   // the capture is written at the end of the frame
                        OfflineExtraEmotes.Turn(wheel, 1);
                        yield return new WaitForSeconds(0.8f);
                        OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "emote-panel-more.png");
                        File.WriteAllText(CheatCapturePrefix + "emote-panel-more.tsv", OfflineEmotes.DescribeWheel(wheel));
                        UnityEngine.UI.Button[] emoteButtons = (UnityEngine.UI.Button[])AccessTools.Field(typeof(UserEmotesWin), "EmoteButtons").GetValue(wheel);
                        int extrasBefore = OfflineEmotes.AiEmotesSent;
                        emoteButtons[0].onClick.Invoke();
                        yield return new WaitForSeconds(1.2f);
                        OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "emote-extra.png");
                        File.WriteAllText(CheatCapturePrefix + "emote-extra-texts.tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
                        yield return new WaitForSeconds(3.5f);
                        OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "emote-extra-reply.png");
                        File.WriteAllText(CheatCapturePrefix + "emote-extra-reply-texts.tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
                        parity.Append("-\textra-emotes\t-\tpage " + OfflineExtraEmotes.Page + " extras " + OfflineExtraEmotes.For(OfflineExtraEmotes.PlayerPackage()).Count
                            + " aiEmotes " + extrasBefore + "->" + OfflineEmotes.AiEmotesSent + "\t\n");
                        OfflineExtraEmotes.Page = 0;
                        yield return new WaitForSeconds(10f);   // the panel's own rate limit (2 per 10 s)
                    }
                    else wheel.EmoteUi_Show(false);
                    yield return new WaitForSeconds(0.6f);
                }
                if (board != null) AccessTools.Method(typeof(SPTESL.BoardInputController), "HandleOnUserEmote").Invoke(board, new object[] { SPTESL.UserEmoteType.Hello });
                yield return new WaitForSeconds(1f);
                OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "emote-player.png");
                File.WriteAllText(CheatCapturePrefix + "emote-player-texts.tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
                for (int shot = 1; shot <= 5; shot++)
                {
                    yield return new WaitForSeconds(2f);
                    OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "emote-ai-" + shot + ".png");
                    File.WriteAllText(CheatCapturePrefix + "emote-ai-texts-" + shot + ".tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
                }
                parity.Append("-\temote-settings\t-\tautoMute=" + Pixelplacement.Singleton<SettingsManager>.Instance.AutoMuteEmotes + "\t\n");
                parity.Append("-\temotes\t-\tboard=" + (board != null) + " aiEmotes " + before + "->" + OfflineEmotes.AiEmotesSent + "\t\n");
            }
            if (CheatDiagnostic && !cheatsDone && OfflineCheats.Unavailable(sim) == null && sim.Players[sim._turnStateStack.Peek().ActivePlayerIndex] == human)
            {
                cheatsDone = true;
                yield return new WaitForEndOfFrame();
                OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "cheat-before.png");
                int mana = human.QueryAttribute(SPTESL.AttributeType.Mana).Value;
                int hand = sim.CardCatalog.FindCardCount(SPTESL.GameZoneName.Hand, human.NetId);
                string a = OfflineCheats.Apply(sim, OfflineCheats.MatchCheat.Magicka), b = OfflineCheats.Apply(sim, OfflineCheats.MatchCheat.Draw);
                parity.Append("-\tcheats\t-\t").Append(a ?? "magicka ok").Append(" / ").Append(b ?? "draw ok").Append("\tmagicka ").Append(mana).Append("->")
                    .Append(human.QueryAttribute(SPTESL.AttributeType.Mana).Value).Append(" hand ").Append(hand).Append("->")
                    .Append(sim.CardCatalog.FindCardCount(SPTESL.GameZoneName.Hand, human.NetId)).Append("\t\n");
                yield return new WaitForSeconds(4f);
                OfflineStoryDiagnostic.Capture(CheatCapturePrefix + "cheat-after.png");
                File.WriteAllText(CheatCapturePrefix + "cheat-texts.tsv", string.Join("\n", OfflineStoryDiagnostic.VisibleTexts().ToArray()));
            }
            human.IsHuman = true;
            string server = OfflineStoryDiagnostic.ServerBoard(sim), client = OfflineStoryDiagnostic.ClientBoard();
            bool same = server == client;
            int turns = sim.Players[0].QueryAttribute(SPTESL.AttributeType.TurnCount).Value + sim.Players[1].QueryAttribute(SPTESL.AttributeType.TurnCount).Value;
            parity.Append((Time.realtimeSinceStartup - start).ToString("0")).Append('\t').Append(sim._gameState.GamePhase).Append('\t').Append(turns)
                .Append('\t').Append(same ? "match" : "differs").Append('\t').Append(server).Append('\t').Append(client ?? "no-board")
                .Append('\t').AppendLine("human_magicka=" + human.QueryAttribute(SPTESL.AttributeType.Mana).Value);
            // A board that never converges is a bug to report (parity "differs"), but must not stall
            // the run: after 20 differing ticks (~40 s) the seat is released anyway.
            mismatchStreak = same ? 0 : mismatchStreak + 1;
            if (!same && mismatchStreak < 20) yield break;
            if (!same) parity.Append("-\t-\t-\tFORCED\t\t\n");
            human.IsHuman = false;
            yield return new WaitForSeconds(0.25f);
            human.IsHuman = true;
        }

        // Diagnostic input only: press the screen's own OK/Continue button (never gameplay).
        private static readonly HashSet<int> openedRewards = new HashSet<int>();
        private static bool packDropped;
        private static bool settledCaptured;
        private static float lastArenaPlay = -100f, lastCoin = -100f;
        private static bool coinMaps;
        // Seeded menus that need a screen with no plain button: one scripted call once the client reaches a state.
        private static string OneShotState;
        private static bool NameEntryDiagnostic;
        private static Action OneShot;

        private static void PressDialogButton(StringBuilder texts, int step)
        {
            if (OneShot != null && NameEntryDiagnostic && !OfflineNameEntry.IsShowing) return; // wait for the first-run popup
            if (OneShot != null && CurrentState().StartsWith(OneShotState))
            {
                texts.Append(step).Append("\t-\t(script)\t").Append(OneShotState).Append('\n');
                Action action = OneShot; OneShot = null;
                action();
                return;
            }
            // Rewards screen: open each dropped container once (its own click handler), then continue.
            foreach (RewardContainer container in UnityEngine.Object.FindObjectsOfType<RewardContainer>())
            {
                if (!container.isActiveAndEnabled || container.onClick == null || !openedRewards.Add(container.GetInstanceID())) continue;
                texts.Append(step).Append("\t-\t(press)\tREWARD ").AppendLine(container.GetBundleKey());
                container.onClick(container);
                return;
            }
            // Pack opening (seeded menus): the player's drag of a pack onto the mat, through the
            // pack's own drag callback and the drop target's own drop callback.
            if (seed != null && !packDropped)
            {
                Pack3D pack = UnityEngine.Object.FindObjectOfType<Pack3D>();
                PackOpenDropTarget target = UnityEngine.Object.FindObjectOfType<PackOpenDropTarget>();
                if (pack != null && pack.isActiveAndEnabled && pack.OnPackDrag != null && target != null && target.OnPackDrop != null)
                {
                    packDropped = true;
                    texts.Append(step).Append("\t-\t(press)\tPACK DRAG+DROP\n");
                    pack.OnPackDrag(pack);
                    target.OnPackDrop(true);
                    return;
                }
            }
            // Story card choice (e.g. Spare/Execute): the first option, through the window's own
            // choice handler (what a card click calls).
            StoryCardChoiceWin choiceWin = UnityEngine.Object.FindObjectOfType<StoryCardChoiceWin>();
            if (choiceWin != null && choiceWin.isActiveAndEnabled && openedRewards.Add(choiceWin.GetInstanceID()))
            {
                texts.Append(step).Append("\t-\t(press)\tSTORY CHOICE 1\n");
                AccessTools.Method(typeof(StoryCardChoiceWin), "OnChoiceMade").Invoke(choiceWin, new object[] { 0 });
                return;
            }
            // Arena draft: pick the first offered card through the draft window's own handler.
            ArenaDraftWin draft = seed == null ? null : UnityEngine.Object.FindObjectOfType<ArenaDraftWin>();
            if (draft != null && draft.isActiveAndEnabled)
            {
                object container = AccessTools.Field(typeof(ArenaDraftWin), "_cardChoiceContainer").GetValue(draft);
                GameObject choices = container is Component ? ((Component)container).gameObject : container as GameObject;
                List<SPTESL.DeckCard> offered = AccessTools.Field(typeof(ArenaDraftWin), "_deckCardDraftChoices").GetValue(draft) as List<SPTESL.DeckCard>;
                if (choices != null && choices.activeInHierarchy && offered != null && offered.Count > 0 && offered[0] != null)
                {
                    texts.Append(step).Append("\t-\t(press)\tDRAFT PICK\n");
                    AccessTools.Method(typeof(ArenaDraftWin), "HandleCardSelected").Invoke(draft, new object[] { offered[0] });
                    return;
                }
            }
            // Arena ladder: start the next open match through SoloArena's own play handler (what
            // selecting the token and pressing Play do), at most every 30 s and never mid-match.
            SoloArena ladder = seed == null ? null : UnityEngine.Object.FindObjectOfType<SoloArena>();
            if (ladder != null && ladder.isActiveAndEnabled && OfflineNativeMatch.CurrentSimulation == null && Time.realtimeSinceStartup - lastArenaPlay > 30f)
            {
                ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
                Arena.ArenaClientDataStruct data = services == null || services.Arena == null ? null : services.Arena.ArenaClientData;
                Arena.ArenaClientInstance run = data == null || data.instances == null ? null : data.instances.Find(delegate(Arena.ArenaClientInstance x) { return x.arenaType == "solo"; });
                Arena.ArenaClientMatch next = run == null || run.phase != Arena.ArenaPhase.MatchSelection || run.matches == null ? null : run.matches.Find(delegate(Arena.ArenaClientMatch m) { return !m.completed; });
                if (next != null)
                {
                    lastArenaPlay = Time.realtimeSinceStartup;
                    texts.Append(step).Append("\t-\t(press)\tARENA PLAY ").AppendLine(next.orderIndex.ToString());
                    AccessTools.Field(typeof(SoloArena), "_selectedMatchOrderIndex").SetValue(ladder, next.orderIndex);
                    AccessTools.Method(typeof(SoloArena), "HandlePlayButtonPressed").Invoke(ladder, new object[] { Arena.ArenaType.Solo });
                    return;
                }
            }
            // Deck picker with no deck selected: choose the first saved deck (PlayWin's own handler).
            SPTESL.PlayWin playWin = seed == null ? null : UnityEngine.Object.FindObjectOfType<SPTESL.PlayWin>();
            if (playWin != null && playWin.isActiveAndEnabled && (long)AccessTools.Field(typeof(SPTESL.PlayWin), "_selectedDeckId").GetValue(playWin) == 0)
            {
                OfflinePlayerStateData snapshot = OfflineNativeMatch.State.Snapshot();
                if (snapshot.decks.Count > 0)
                {
                    texts.Append(step).Append("\t-\t(press)\tSELECT DECK ").AppendLine(snapshot.decks[0].id.ToString());
                    AccessTools.Method(typeof(SPTESL.PlayWin), "HandleOnDeckSelected").Invoke(playWin, new object[] { snapshot.decks[0].id });
                    return;
                }
            }
            // Coin-map stories (DBH/CWC/IOM): press the chapter's next uncompleted battle event
            // through StoryState's own coin handler, at most every 30 s and never mid-match.
            global::StoryState story = coinMaps ? UnityEngine.Object.FindObjectOfType<global::StoryState>() : null;
            bool mapIdle = (playWin == null || !playWin.isActiveAndEnabled) && GameObject.Find("Section_ChoiceButtons") == null;
            if (story != null && story.isActiveAndEnabled && mapIdle && OfflineNativeMatch.CurrentSimulation == null && Time.realtimeSinceStartup - lastCoin > 30f)
            {
                object info = AccessTools.Field(typeof(global::StoryState), "_currentChapterInfo").GetValue(story);
                Type infoType = info.GetType();
                int campaignId = (int)infoType.GetField("CampaignId").GetValue(info), actId = (int)infoType.GetField("ActId").GetValue(info), chapterId = (int)infoType.GetField("ChapterId").GetValue(info);
                SPTESL.Chapter chapter = null;
                foreach (SPTESL.Campaign c in SPTESL.GameManager.Instance.Config.Campaigns) foreach (SPTESL.Act a in c.Acts) foreach (SPTESL.Chapter ch in a.Chapters) if (ch.ResourceId.Id == chapterId) chapter = ch;
                SparkleServices.Campaign.IUserChapterData user = chapter == null ? null : OfflineCampaignService.FindChapter(OfflineNativeMatch.State.Snapshot(), campaignId, actId, chapterId, false);
                if (chapter != null && user != null)
                    foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                    {
                        SparkleServices.Campaign.IUserChapterEventData done = Array.Find(user.chapterEventData, delegate(SparkleServices.Campaign.IUserChapterEventData x) { return x.chapterEventId == e.ResourceId.Id; });
                        // Mod stories: every revealed, unfinished event (dialogue too); originals: battle events.
                        bool mod = OfflineStories.IsModChapter(chapter.ResourceId.Id);
                        if (mod ? done == null || done.isComplete : e.GatedScenarios.Count == 0 || (done != null && done.isComplete)) continue;
                        lastCoin = Time.realtimeSinceStartup;
                        texts.Append(step).Append("\t-\t(press)\tEVENT COIN ").AppendLine(e.ResourceId.UniqueName);
                        AccessTools.Method(typeof(global::StoryState), "OnEventCoinPressed").Invoke(story, new object[] { e, null });
                        return;
                    }
            }
            // Then flip each dealt card through its own click callback, one per tick.
            if (seed != null && packDropped)
                foreach (PackOpenCardButton card in UnityEngine.Object.FindObjectsOfType<PackOpenCardButton>())
                {
                    if (!card.isActiveAndEnabled || card.onClick == null || !openedRewards.Add(card.GetInstanceID())) continue;
                    texts.Append(step).Append("\t-\t(press)\tFLIP CARD\n");
                    card.onClick();
                    return;
                }
            SPTESL.RewardsWin rewardsWin = UnityEngine.Object.FindObjectOfType<SPTESL.RewardsWin>();
            UnityEngine.UI.Button rewardsContinue = rewardsWin == null ? null : rewardsWin.GetContinueButton();
            if (rewardsContinue != null && rewardsContinue.isActiveAndEnabled && rewardsContinue.interactable)
            {
                texts.Append(step).Append("\t-\t(press)\tREWARDS CONTINUE\n");
                rewardsContinue.onClick.Invoke();
                return;
            }
            SPTESL.EndGameWin end = UnityEngine.Object.FindObjectOfType<SPTESL.EndGameWin>();
            UnityEngine.UI.Button anywhere = end == null ? null : AccessTools.Field(typeof(SPTESL.EndGameWin), "_clickAnywhereBtn").GetValue(end) as UnityEngine.UI.Button;
            if (anywhere != null && anywhere.isActiveAndEnabled && anywhere.interactable)
            {
                texts.Append(step).Append("\t-\t(press)\tCLICK ANYWHERE\n");
                anywhere.onClick.Invoke();
                return;
            }
            // Priority: dialog OK/CONTINUE and story Play, then (seeded) named buttons, then menu
            // labels with later entries first, so a flow advances instead of re-pressing its entry.
            UnityEngine.UI.Button best = null; string bestLabel = null; int bestRank = int.MaxValue;
            foreach (UnityEngine.UI.Button button in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>())
            {
                if (!button.isActiveAndEnabled || !button.interactable) continue;
                string label = OfflineStoryDiagnostic.Label(button);
                // The story map's Play starts the next chapter (diagnostic chain, bounded).
                bool play = label == "PLAY" && OfflineNativeMatch.ResultsRecorded < MaxStoryMatches && CurrentState().StartsWith("StoryState");
                int rank = int.MaxValue;
                if (play && button.name == "Btn_Play" && button.transform.parent != null && button.transform.parent.name == "Layout_Bottom_Buttons") rank = -1;
                else if (label == "OK" || label == "CONTINUE" || label == "KEEP HAND" || label == "SELECT" || play) rank = 0;
                // The post-match quest progress popup (labelled with the quest text) is dismissed by a click.
                else if (button.name == "QuestWin(Clone)" && !string.IsNullOrEmpty(label) && label != "DAILY QUESTS") rank = 0;
                else if (seed != null && Array.IndexOf(MenuButtonNames, button.name) >= 0) rank = 1;
                else if (seed != null && Array.IndexOf(MenuLabels, label) >= 0) rank = 2 + MenuLabels.Length - Array.IndexOf(MenuLabels, label);
                if (rank < bestRank) { best = button; bestLabel = label; bestRank = rank; }
            }
            if (best == null) return;
            texts.Append(step).Append("\t-\t(press)\t").AppendLine(bestLabel.Length == 0 ? best.name : bestLabel);
            best.onClick.Invoke();
        }

        // Diagnostic input only: invoke the scroll's own click event once it is idle.
        private static bool scrollClicked;
        private static void AdvanceOnboarding()
        {
            if (scrollClicked) return;
            SPTESL.IntroOnboarding3d scroll = UnityEngine.Object.FindObjectOfType<SPTESL.IntroOnboarding3d>();
            if (scroll == null || scroll.OnScrollClick == null) return;
            scrollClicked = true;
            scroll.OnScrollClick();
        }

        private static string CurrentState()
        {
            ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
            string auth = services == null ? "no-services" : services.AuthData == null ? "unauthenticated" : "auth:" + services.AuthData.nickname;
            Pixelplacement.State current = null;
            foreach (Pixelplacement.StateMachine machine in UnityEngine.Object.FindObjectsOfType<Pixelplacement.StateMachine>())
                if (machine.currentState != null) { current = machine.currentState.GetComponent<Pixelplacement.State>(); break; }
            return (current == null ? "no-state" : current.GetType().Name) + "/" + auth;
        }
    }
}
