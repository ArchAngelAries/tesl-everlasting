using System;
using System.Collections.Generic;
using System.IO;
using SparkleServices;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // The extension interface of TESL: Everlasting (owner 2026-11-07: expansions such as TESL: Aetherius are separate
    // products that integrate as if they were always in the game). Expansions are their own BepInEx plugins that depend on
    // local.tesl.offline and use only this class. The restoration contains no expansion code.
    //
    // Versioned: an extension declares the API version it was built for (RequiresApi). Unsupported versions are refused
    // with a clear message, never a silent break. New members are added in a way that keeps older callers working; a
    // breaking change gets a new ApiVersion and Supported keeps the old one while it can.
    //
    // Hooks (X numbers follow the progression plan, PROPOSALS.md section 1, kept with the TESL: Aetherius research):
    //   registry and capabilities, X1 match lifecycle, X2 reward delivery, X3 content listing (resolved mod state), X4
    //   extension data files, X8 assist and launch state, X9 wallet and inventory writes (through X2 entries), X10 client
    //   refresh, X13 clock, X6 F10 pages, local service routes, and engine-test registration.
    public static class EverlastingHost
    {
        public const int ApiVersion = 1;
        private static readonly int[] Supported = { 1 };
        public static bool Supports(int version) { return Array.IndexOf(Supported, version) >= 0; }

        // ---- registry -------------------------------------------------------------------------------------------
        public sealed class Manifest
        {
            public string Id, Name, Version;
            public int RequiresApi = 1;
            public string[] Requires = new string[0];      // hard dependencies: "<extension id>" or "<id>>=<version>"
            public string[] Provides = new string[0];      // capabilities offered, "<name>@<version>"
            public string[] Consumes = new string[0];      // capabilities used when present (soft)
        }

        public sealed class Registered
        {
            public Manifest Manifest;
            public bool Active;
            public string Problem;     // why it is not active (unsupported API, missing dependency), else null
        }

        private static readonly List<Registered> extensions = new List<Registered>();
        public static List<Registered> Extensions { get { lock (extensions) return new List<Registered>(extensions); } }

        // Host features added inside API 1, listed as always-active entries so an extension can require them like any
        // dependency ("everlasting.ai-decks>=1"); an older engine lacks the entry and MissingDependencies says so.
        public static readonly string[] HostFeatures = { "everlasting.ai-decks", "everlasting.catalog-decks", "everlasting.store-decks" };
        private static readonly bool hostFeaturesListed = ListHostFeatures();
        private static bool ListHostFeatures()
        {
            foreach (string f in HostFeatures)
                extensions.Add(new Registered { Active = true, Manifest = new Manifest { Id = f, Name = "TESL: Everlasting " + f, Version = "1" } });
            return true;
        }

        // Returns null when accepted, else the reason (also logged and kept in Extensions).
        public static string Register(Manifest m)
        {
            if (m == null || string.IsNullOrEmpty(m.Id)) return "extension manifest has no id";
            Registered r = new Registered { Manifest = m, Active = true };
            if (!Supports(m.RequiresApi))
            {
                r.Active = false;
                r.Problem = m.Name + " needs TESL: Everlasting extension API " + m.RequiresApi + "; this version offers " + string.Join(", ", Array.ConvertAll(Supported, delegate(int v) { return v.ToString(); }))
                    + ". Update " + (m.RequiresApi > ApiVersion ? "TESL: Everlasting" : m.Name) + ".";
            }
            lock (extensions) { extensions.RemoveAll(delegate(Registered x) { return x.Manifest.Id == m.Id; }); extensions.Add(r); }
            Debug.Log("EVERLASTING HOST: extension " + m.Id + " " + m.Version + (r.Active ? " registered (API " + m.RequiresApi + ")" : " refused: " + r.Problem));
            return r.Problem;
        }

        public static bool IsActive(string id)
        {
            lock (extensions) return extensions.Exists(delegate(Registered x) { return x.Manifest.Id == id && x.Active; });
        }

        // Hard dependencies that are missing or too old (call once every plugin has registered, e.g. at the first menu).
        public static List<string> MissingDependencies(string id)
        {
            List<string> missing = new List<string>();
            Registered self = null;
            lock (extensions) self = extensions.Find(delegate(Registered x) { return x.Manifest.Id == id; });
            if (self == null) { missing.Add(id + " (not registered)"); return missing; }
            foreach (string req in self.Manifest.Requires)
            {
                string need = req, min = null;
                int at = req.IndexOf(">=", StringComparison.Ordinal);
                if (at > 0) { need = req.Substring(0, at); min = req.Substring(at + 2); }
                Registered dep = null;
                lock (extensions) dep = extensions.Find(delegate(Registered x) { return x.Manifest.Id == need; });
                if (dep == null || !dep.Active) missing.Add(req);
                else if (min != null && CompareVersions(dep.Manifest.Version, min) < 0) missing.Add(req + " (installed " + dep.Manifest.Version + ")");
            }
            return missing;
        }

        public static int CompareVersions(string a, string b)
        {
            string[] x = (a ?? "0").Split('.'), y = (b ?? "0").Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = 0, q = 0;
                if (i < x.Length) int.TryParse(x[i], out p);
                if (i < y.Length) int.TryParse(y[i], out q);
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        // Capabilities: a provider object offered under a versioned name (e.g. "quest-objectives@1"); consumers ask for the
        // name and the lowest version they understand. Lets community mods plug in with no code written for them.
        private static readonly Dictionary<string, List<KeyValuePair<int, object>>> capabilities = new Dictionary<string, List<KeyValuePair<int, object>>>(StringComparer.OrdinalIgnoreCase);

        public static void Provide(string capability, int version, object provider)
        {
            lock (capabilities)
            {
                List<KeyValuePair<int, object>> list;
                if (!capabilities.TryGetValue(capability, out list)) capabilities[capability] = list = new List<KeyValuePair<int, object>>();
                list.Add(new KeyValuePair<int, object>(version, provider));
            }
        }

        public static List<object> Providers(string capability, int minVersion)
        {
            List<object> found = new List<object>();
            lock (capabilities)
            {
                List<KeyValuePair<int, object>> list;
                if (capabilities.TryGetValue(capability, out list))
                    foreach (KeyValuePair<int, object> p in list) if (p.Key >= minVersion) found.Add(p.Value);
            }
            return found;
        }

        // ---- player state ---------------------------------------------------------------------------------------
        private static OfflinePlayerState state;
        internal static void SetState(OfflinePlayerState s) { state = s; }

        // Tests only: a throwaway profile in a temporary folder (returns a handle that restores the real one).
        public static IDisposable UseTestProfile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "everlasting-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            TestProfile handle = new TestProfile { Previous = state, Folder = dir, PreviousRoot = DataRootOverride };
            state = new OfflinePlayerState(Path.Combine(dir, "profile.json"));
            DataRootOverride = dir;
            return handle;
        }

        private sealed class TestProfile : IDisposable
        {
            public OfflinePlayerState Previous; public string Folder, PreviousRoot;
            public void Dispose()
            {
                state = Previous; DataRootOverride = PreviousRoot;
                try { Directory.Delete(Folder, true); } catch (Exception) { }
            }
        }

        // Where extension data folders live (tests point it at their temporary folder; diagnostics and card previews at
        // their throwaway folder, so a hidden run never touches the player's real extension data).
        private static string DataRootOverride;
        internal static void SetDataRoot(string root) { DataRootOverride = root; }
        public static bool HasProfile { get { return state != null; } }
        // A stable id for this local player (seeds per-player random rolls; never sent anywhere).
        public static string PlayerId { get { return OfflineNativeFlow.Profile != null ? OfflineNativeFlow.Profile.Identity : "offline-player"; } }

        // Hidden diagnostic runs and the menu script they run (--tesl-offline-native-menu=<name>), so an extension can keep its
        // pop-ups (a daily-rewards window ...) out of runs that test something else.
        public static bool DiagnosticRun { get { return Array.IndexOf(Environment.GetCommandLineArgs(), OfflineNativeFlow.DiagnosticFlag) >= 0; } }
        public static string DiagnosticMenu
        {
            get
            {
                const string prefix = "--tesl-offline-native-menu=";
                foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length);
                return null;
            }
        }
        public static OfflinePlayerStateData Snapshot() { return state == null ? null : state.Snapshot(); }
        public static void Mutate(Action<OfflinePlayerStateData> change)
        {
            if (state == null) throw new InvalidOperationException("No player profile is loaded yet.");
            state.Mutate(change);
        }

        // ---- X1 match lifecycle ---------------------------------------------------------------------------------
        public sealed class MatchResult
        {
            public string Mode;          // XP mode: Story, Puzzle, Practice, SoloArena, VersusArena, Ranked, Casual, ...
            public string RatingMode;    // ranked/casual rating mode or null
            public bool Won;
            public string Scenario;
            public int ChapterEventId;
            public int ArenaIndex = -1;
            public string MatchId;
            public long EventId;         // an event battle (EventMatchProvider), else 0
        }

        // ---- X11 event battles ------------------------------------------------------------------------------------
        // The client's event matches (matching/add with matchMode EventBattle, eventId, deckId). An extension that runs
        // events supplies the plan; the engine plays it like a practice match (ordinary rules, the player's chosen deck)
        // with the plan's game mode (TESL Forge modes.json mode object), opponent deck and difficulty, and reports the
        // result through OnMatchEnded with EventId set. Return null to refuse the request.
        public sealed class EventMatchPlan
        {
            public string ModeJson;               // a modes.json mode object, or null for ordinary rules
            public string Difficulty = "Expert";  // Novice | Adept | Expert
            public List<int> OpponentCards;       // the AI's deck (card ids), or null for a practice AI deck
            public string OpponentName;
            public string[] OpponentTags;         // the event's opponent theme (opponents.tagsAny), passed to AI deck providers
        }
        public static Func<long, long, EventMatchPlan> EventMatchProvider;   // (eventId, deckId) -> plan

        // ---- X11 AI decks (host feature "everlasting.ai-decks" 1) -------------------------------------------------
        // When the engine would give an AI opponent an installed practice deck (Practice, Versus, Casual, event battles
        // without their own deck, story battles that play "a practice deck"), it first asks the registered providers, in
        // registration order; the first non-null choice is played. The request names the opponent already chosen the
        // game's own way (an original AiOpponent from the AI deck's ValidAvatars) and its race, so a provider can keep
        // the opponent believable. A provider may name another original AiOpponent to show instead (its own name and
        // portrait are then shown). Mod opponents (OfflineOpponents), game modes that name a deck and mod decks chosen for
        // the AI are never replaced.
        public sealed class AiDeckRequest
        {
            public string Kind;            // practice | versus | casual | event | story
            public string Difficulty;      // Novice | Adept | Expert
            public string Opponent;        // AiOpponent unique name shown (may be null)
            public string OpponentRace;    // OfflineEmotes.OpponentRace of it, or null
            public string AiDeck;          // the original AI deck identity (CustomDeck unique name)
            public List<string> Colors = new List<string>();   // that AI deck's attributes (Strength ... Endurance)
            public string[] Tags = new string[0];              // event opponent theme, if any
            public long EventId;
            public int Seed;               // stable per match request
        }
        public sealed class AiDeckChoice
        {
            public List<int> Cards;        // card ids, one entry per copy
            public string DeckName;        // for logs and match results
            public string Opponent;        // another original AiOpponent unique name to show, or null to keep the chosen one
            public string Provider;        // extension id (filled by the engine)
        }
        private static readonly List<KeyValuePair<string, Func<AiDeckRequest, AiDeckChoice>>> aiDeckProviders = new List<KeyValuePair<string, Func<AiDeckRequest, AiDeckChoice>>>();
        public static void RegisterAiDeckProvider(string extensionId, Func<AiDeckRequest, AiDeckChoice> provider)
        { lock (aiDeckProviders) { aiDeckProviders.RemoveAll(delegate(KeyValuePair<string, Func<AiDeckRequest, AiDeckChoice>> p) { return p.Key == extensionId; }); aiDeckProviders.Add(new KeyValuePair<string, Func<AiDeckRequest, AiDeckChoice>>(extensionId, provider)); } }
        public static AiDeckChoice LastAiDeckChoice;   // the choice of the latest match start (logs and tests)

        public static AiDeckChoice ChooseAiDeck(AiDeckRequest request)
        {
            List<KeyValuePair<string, Func<AiDeckRequest, AiDeckChoice>>> list;
            lock (aiDeckProviders) list = new List<KeyValuePair<string, Func<AiDeckRequest, AiDeckChoice>>>(aiDeckProviders);
            foreach (KeyValuePair<string, Func<AiDeckRequest, AiDeckChoice>> p in list)
            {
                if (!IsActive(p.Key)) continue;
                try
                {
                    AiDeckChoice c = p.Value(request);
                    if (c != null && c.Cards != null && c.Cards.Count > 0) { c.Provider = p.Key; LastAiDeckChoice = c; return c; }
                }
                catch (Exception e) { Debug.LogError("EVERLASTING HOST: AI deck provider " + p.Key + " failed: " + e); }
            }
            LastAiDeckChoice = null;
            return null;
        }

        // The game's AI opponents (portrait + name), with the race their portrait or name shows (null = not a known race).
        public sealed class OpponentInfo { public string UniqueName, DisplayName, Image, Race; public int Id; }
        public static List<OpponentInfo> AiOpponents()
        {
            List<OpponentInfo> list = new List<OpponentInfo>();
            GameConfiguration config = GameManager.Instance == null ? null : GameManager.Instance.Config;
            if (config == null || config.AiOpponents == null) return list;
            foreach (AiOpponent ai in config.AiOpponents)
            {
                if (ai == null || (ai.ResourceId.UniqueName ?? "").StartsWith("MOD:", StringComparison.Ordinal)) continue;   // mod opponents are their mods' own
                list.Add(new OpponentInfo { UniqueName = ai.ResourceId.UniqueName, Id = ai.ResourceId.Id, Image = ai.Image,
                    DisplayName = OfflineEmotes.OpponentDisplayName(ai), Race = OfflineEmotes.OpponentRace(ai, null) });
            }
            return list;
        }

        // ---- catalog decks (host feature "everlasting.catalog-decks" 1) -------------------------------------------
        // Premade decks an extension owns: known to the client (deck lists, store detail, rewards deck:/deckonce:<TypeName>)
        // but never granted on their own, never listed by the "unlock all premade decks" cheat and never sold unless a
        // store-deck provider offers them. Register before the game configuration loads (plugin Awake).
        public sealed class CatalogDeck
        {
            public string TypeName, Name;  // TypeName: unique, prefix with the extension id (e.g. "aetherius.decks.dl-1234")
            public int ArtCardId;          // the card whose art is the deck's tile
            public List<KeyValuePair<int, int>> Cards = new List<KeyValuePair<int, int>>();   // card id, copies
        }
        private static readonly List<KeyValuePair<string, CatalogDeck>> catalogDecks = new List<KeyValuePair<string, CatalogDeck>>();
        public static void RegisterCatalogDecks(string extensionId, List<CatalogDeck> decks)
        { lock (catalogDecks) foreach (CatalogDeck d in decks) catalogDecks.Add(new KeyValuePair<string, CatalogDeck>(extensionId, d)); }
        internal static List<KeyValuePair<string, CatalogDeck>> CatalogDecks() { lock (catalogDecks) return new List<KeyValuePair<string, CatalogDeck>>(catalogDecks); }
        public static int DeckTypeHash(string typeName) { return OfflineMods.StableId("deck|" + typeName); }

        // ---- store decks (host feature "everlasting.store-decks" 1) -----------------------------------------------
        // Catalog decks an extension sells for gold in the store's Special Offers now (e.g. a weekly selection). The store
        // leaves out decks the player already owns. Called on the services thread: keep it quick and lock-free.
        public sealed class StoreDeck { public string TypeName, Description; public int Gold; }
        private static readonly List<KeyValuePair<string, Func<OfflinePlayerStateData, List<StoreDeck>>>> storeDecks = new List<KeyValuePair<string, Func<OfflinePlayerStateData, List<StoreDeck>>>>();
        public static void RegisterStoreDecks(string extensionId, Func<OfflinePlayerStateData, List<StoreDeck>> provider)
        { lock (storeDecks) storeDecks.Add(new KeyValuePair<string, Func<OfflinePlayerStateData, List<StoreDeck>>>(extensionId, provider)); }
        internal static List<StoreDeck> StoreDecks(OfflinePlayerStateData d)
        {
            List<KeyValuePair<string, Func<OfflinePlayerStateData, List<StoreDeck>>>> list;
            lock (storeDecks) list = new List<KeyValuePair<string, Func<OfflinePlayerStateData, List<StoreDeck>>>>(storeDecks);
            List<StoreDeck> all = new List<StoreDeck>();
            foreach (KeyValuePair<string, Func<OfflinePlayerStateData, List<StoreDeck>>> p in list)
            {
                if (!IsActive(p.Key)) continue;
                try { List<StoreDeck> s = p.Value(d); if (s != null) all.AddRange(s); }
                catch (Exception e) { Debug.LogError("EVERLASTING HOST: store-deck provider " + p.Key + " failed: " + e); }
            }
            return all;
        }

        // A handler returns true when it queued a reward (so the client's match-rewards screen fetches rewards/list).
        private static readonly List<Func<MatchResult, bool>> matchEnded = new List<Func<MatchResult, bool>>();
        public static void OnMatchEnded(Func<MatchResult, bool> handler) { lock (matchEnded) matchEnded.Add(handler); }

        internal static bool RaiseMatchEnded(MatchResult result)
        {
            List<Func<MatchResult, bool>> handlers;
            lock (matchEnded) handlers = new List<Func<MatchResult, bool>>(matchEnded);
            bool rewarded = false;
            foreach (Func<MatchResult, bool> h in handlers)
                try { rewarded |= h(result); }
                catch (Exception e) { Debug.LogError("EVERLASTING HOST: match-ended handler failed: " + e); }
            return rewarded;
        }

        // ---- X2 reward delivery ---------------------------------------------------------------------------------
        // An extension registers bundle contents under its own key (prefix it with the extension id) and queues it: the
        // client shows its own reward window (rewards/list -> accept), and accept applies the entries. Entries use the
        // engine's bundle language: card:<Name>*n, cardid:<id>*n, premiumcardid:<id>*n, cardback:<key>, title:<key>, gold:n,
        // gems:n, ticket:n, xp:n, deck:<name>, deckonce:<name>, pack:<set>*n, pack:Core Set*n, corecard:<Rarity>:race|random.
        private static readonly Dictionary<string, string[]> bundles = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public static void RegisterBundle(string key, string[] entries) { lock (bundles) bundles[key] = entries; }
        internal static string[] Bundle(string key) { string[] e; lock (bundles) return bundles.TryGetValue(key, out e) ? e : null; }

        public static Rewards.RewardDescription QueueReward(OfflinePlayerStateData d, string bundleKey, Rewards.RewardSource source)
        {
            Rewards.RewardDescription reward = new Rewards.RewardDescription();
            reward.rewardId = ++d.nextRewardId; reward.source = source;
            reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                rewardPassIndex = 0, bundleKey = bundleKey, bundleCount = 1, rarity = Rewards.RewardRarity.Common,
                rewardContainerType = Rewards.RewardContainer.Bag } };
            d.rewards.Add(reward);
            return reward;
        }

        // Applies entries now, with no window (e.g. a daily reward accepted through its own service call).
        public static Rewards.AcceptRewardResponse.AcceptRewardResult ApplyEntries(OfflinePlayerStateData d, string[] entries)
        {
            return OfflineRewardsService.ApplyEntries(d, entries);
        }

        // ---- X3 content (resolved: what the game loaded after mod load order and conflicts) ---------------------
        public sealed class CardInfo
        {
            public int Id;
            public string Name, Set, Rarity, Mod;   // Mod is null for official cards
            public bool Collectible, Unique;
        }

        public static List<CardInfo> Cards()
        {
            List<CardInfo> list = new List<CardInfo>();
            GameConfiguration config = GameManager.Instance == null ? null : GameManager.Instance.Config;
            if (config == null) return list;
            foreach (CardSet set in config.CardCollection)
                foreach (Card c in set.Cards)
                {
                    if (c == null) continue;
                    list.Add(new CardInfo { Id = c.ResourceId.Id, Name = c.Name, Set = set.Name, Rarity = c.Rarity.ToString(),
                        Mod = OfflineMods.ModOfCard(c.ResourceId.Id), Collectible = Collectible(c), Unique = c.Unique });
                }
            return list;
        }

        // The collection's rule (OfflineInventoryService.PublishCatalog): production cards with an export code; alternate
        // art versions of another card are left out so a reward never duplicates a base card.
        public static bool Collectible(Card c)
        {
            if (c == null || !c.Production || string.IsNullOrEmpty(c.ExportCode)) return false;
            int alternate = c.AlternateOfCard == null ? 0 : c.AlternateOfCard.resourceId;
            return alternate == 0 || alternate == UniqueResourceId.GetInvalid().Id;
        }

        public static List<string> CardBacks() { return OfflineCosmetics.AllCardBacks(); }
        public static string ModOfCardBack(string key) { return OfflineMods.ModOfCardBack(key); }

        public sealed class ModInfo { public string Id, Name, Folder; public int LoadIndex; }

        // Mods the game loaded this session, in load order (later wins where two change the same thing).
        public static List<ModInfo> Mods()
        {
            List<ModInfo> list = new List<ModInfo>();
            for (int i = 0; i < OfflineMods.LoadOrder.Count; i++)
            {
                string id = OfflineMods.LoadOrder[i];
                if (!OfflineMods.Loaded.Contains(id)) continue;
                list.Add(new ModInfo { Id = id, Name = OfflineMods.ModName(id), Folder = OfflineMods.ModFolder(id), LoadIndex = i });
            }
            return list;
        }

        // ---- X4 extension data ----------------------------------------------------------------------------------
        // One folder per extension and per profile ("offline" now; "online" later), never read by the restoration.
        public static string DataFolder(string extensionId, string profile)
        {
            string root = DataRootOverride ?? Path.Combine(OfflineLaunchMode.GameRoot(), ".offline-user-data");
            string dir = Path.Combine(Path.Combine(root, "extensions"), Path.Combine(extensionId, profile ?? "offline"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Atomic write with a .bak of the previous version.
        public static void WriteAtomic(string file, string text)
        {
            string temp = file + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(file)) File.Replace(temp, file, file + ".bak");
            else File.Move(temp, file);
        }

        // ---- X8 assist and launch state -------------------------------------------------------------------------
        public static string LaunchMode { get { return OfflineLaunchMode.Current; } }   // "offline" | "online"
        // Cheats, free deck building or changed rates have been used on this profile (the "hard way" mark goes away).
        public static bool AssistEverUsed { get { OfflinePlayerStateData d = Snapshot(); return d != null && (d.assistUsed || d.freeDeckBuilding); } }
        public static bool AssistActiveNow { get { OfflinePlayerStateData d = Snapshot(); return d != null && d.freeDeckBuilding; } }
        public static event Action AssistUsed;
        internal static void RaiseAssistUsed() { Action a = AssistUsed; if (a != null) try { a(); } catch (Exception e) { Debug.LogError("EVERLASTING HOST: " + e); } }

        // ---- X10 client refresh ---------------------------------------------------------------------------------
        // Extensions call this from the local services thread (a route); the client's Profile/Inventory refresh starts Unity
        // requests and must run on the main thread, or it fails half-started and the client skips its own next refresh
        // (owner 2026-11-09: daily login gold saved but not shown).
        public static void RefreshClient() { OfflineGoldStore.OnMainThread(OfflineCheats.RefreshClient); }

        // ---- X13 clock ------------------------------------------------------------------------------------------
        // Tests replace it; the trusted (rollback-proof) day logic lives in the extension, which persists its own guard.
        public static Func<DateTime> UtcNow = delegate { return DateTime.UtcNow; };
        public static Func<TimeSpan> LocalOffset = delegate { return TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now); };

        // ---- X6 F10 menu pages ----------------------------------------------------------------------------------
        public sealed class MenuPage { public string Title; public Action Draw; }
        private static readonly List<MenuPage> pages = new List<MenuPage>();
        public static void RegisterMenuPage(string title, Action draw) { lock (pages) pages.Add(new MenuPage { Title = title, Draw = draw }); }
        internal static List<MenuPage> MenuPages { get { lock (pages) return new List<MenuPage>(pages); } }

        // ---- local service routes -------------------------------------------------------------------------------
        // An extension answers a client service call (e.g. dailyrewards/list). Registered before the services start
        // they are applied when they start; after, at once. They replace the restoration's empty default for that path.
        private static readonly List<KeyValuePair<string, OfflineLocalServices.Handler>> routes = new List<KeyValuePair<string, OfflineLocalServices.Handler>>();
        public static void RegisterRoute(string path, OfflineLocalServices.Handler handler)
        {
            lock (routes) routes.Add(new KeyValuePair<string, OfflineLocalServices.Handler>(path, handler));
            if (OfflineLocalServices.Instance != null) OfflineLocalServices.Instance.Register(path, handler);
        }
        internal static void ApplyRoutes(OfflineLocalServices services)
        {
            lock (routes) foreach (KeyValuePair<string, OfflineLocalServices.Handler> r in routes) services.Register(r.Key, r.Value);
        }

        // ---- engine tests ---------------------------------------------------------------------------------------
        // An extension's tests run in the engine test audit (Run-Audit -EngineTests) beside the restoration's own.
        private static readonly List<Action<Action<string, Action>>> tests = new List<Action<Action<string, Action>>>();
        public static void RegisterTests(Action<Action<string, Action>> run) { lock (tests) tests.Add(run); }
        internal static void RunTests(Action<string, Action> test)
        {
            List<Action<Action<string, Action>>> list;
            lock (tests) list = new List<Action<Action<string, Action>>>(tests);
            foreach (Action<Action<string, Action>> run in list) run(test);
        }
    }
}
