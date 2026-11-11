using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AssetBundles;
using FullSerializer;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mods authored with the TESL Modding Suite (separate program next to the game folder; mod format in its
    // docs/DESIGN.md). Enabled mods are listed in <suite>/deploy/active-mods.json. At configuration load each mod adds:
    //  - its cards (original card JSON form, decoded like data/original-rules, rules included) as a new CardSet;
    //  - its decks as premade DeckInfo entries;
    //  - its strings (card name/text, set and deck names) through LocalizationManager.GetText/TryGetText;
    //  - its art: Card.Image "Mods/<mod>/<slug>" is served from <mod>/art/<slug>.png through
    //    AssetCatalog.LoadAssetFromPathAsync as a completed native AssetBundleLoadAssetOperationSimulation;
    //  - its image replacements (remastering, <mod>/replace/replace.json): an original asset path the game loads (card
    //    art, avatars, deck banners, ...) is served from the mod's PNG instead, as a sprite with the original's on-screen
    //    size (pixels-per-unit scaled by the new resolution), pivot and 9-slice border. A later mod in the list wins;
    //  - its avatars (<mod>/avatars/avatars.json): new pages in the avatar picker, selectable like the originals.
    // Nothing in the game folder is written. Collisions with existing card ids are refused per card.
    public static class OfflineMods
    {
        public const string ImagePrefix = "Mods/";
        private static bool applied;
        public static bool Ready;   // mods loaded (or none to load) for this process
        public const string ListFlag = "--tesl-mods-list=";   // load the mods of another list (TESL Forge previews)
        private static string CommandLineList()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(ListFlag, StringComparison.Ordinal)) return arg.Substring(ListFlag.Length).Trim('"');
            return null;
        }
        private static readonly Dictionary<string, string> texts = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> artFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        // Replacement sprite shape per asset path: pixels-per-unit, pivot x/y, border left/bottom/right/top (new pixels).
        private static readonly Dictionary<string, float[]> spriteShape = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<string> Replaced = new List<string>();
        public static readonly List<int> CardIds = new List<int>();
        public static readonly List<DeckInfo> Decks = new List<DeckInfo>();
        // Mods whose mod.json says "collection": "earn": their cards and decks are not handed out for free; players get them
        // like original cards (packs, store decks, rewards, crafting). Other mods keep the old behaviour (owned as a playset).
        public static readonly HashSet<string> EarnMods = new HashSet<string>();
        private static readonly Dictionary<int, string> cardMod = new Dictionary<int, string>(), deckMod = new Dictionary<int, string>();
        public static bool IsEarnedCard(int id) { string m; return cardMod.TryGetValue(id, out m) && EarnMods.Contains(m); }
        // Extension interface (EverlastingHost): which mod a card or card back came from, each loaded mod's name and folder.
        public static string ModOfCard(int id) { string m; return cardMod.TryGetValue(id, out m) ? m : null; }
        public static string ModOfCardBack(string key) { string m; return key != null && cardBackMod.TryGetValue(key, out m) ? m : null; }
        public static string ModName(string id) { string n; return id != null && modNames.TryGetValue(id, out n) ? n : id; }
        public static string ModFolder(string id) { string f; return id != null && modFolders.TryGetValue(id, out f) ? f : null; }
        private static readonly Dictionary<string, string> cardBackMod = new Dictionary<string, string>(), modNames = new Dictionary<string, string>(), modFolders = new Dictionary<string, string>();
        public static bool IsEarnedDeck(int typeHash) { string m; return deckMod.TryGetValue(typeHash, out m) && EarnMods.Contains(m); }
        public static readonly List<string> Loaded = new List<string>();
        public static readonly List<string> Problems = new List<string>();
        public static readonly List<string> LoadOrder = new List<string>();
        // Online-safe mode (OfflineOnlineSafe): only what the player alone sees or hears loads (images, sounds, music, boards).
        public static bool LocalOnly;

        public static string SuiteRoot() { return OfflinePaths.SuiteRoot(); }

        public static string DefaultActiveList() { return Path.Combine(Path.Combine(SuiteRoot(), "deploy"), "active-mods.json"); }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(AssetCatalog), "LoadAssetFromPathAsync", new Type[] { typeof(string), typeof(Type) }),
                new HarmonyMethod(typeof(OfflineMods).GetMethod("LoadAssetPrefix")), null, null, null, null);
            // Original avatar pages always fill all four slots, so the picker never clears unused ones; a mod page with fewer
            // avatars would show the previous page's portraits there. Hide slots past the visible page's avatar count.
            harmony.Patch(AccessTools.Method(typeof(AvatarPickerWin), "UpdateUi"), null, new HarmonyMethod(typeof(OfflineMods).GetMethod("AvatarPickerUpdateUiPostfix")), null, null, null);
            // Some callers (mechanic icons, shader masks) name the extension themselves.
            harmony.Patch(AccessTools.Method(typeof(AssetCatalog), "LoadAssetFromPathAsync", new Type[] { typeof(string), typeof(Type), typeof(string) }),
                new HarmonyMethod(typeof(OfflineMods).GetMethod("LoadAssetExtPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(LocalizationManager), "GetText", new Type[] { typeof(string) }),
                new HarmonyMethod(typeof(OfflineMods).GetMethod("GetTextPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(LocalizationManager), "TryGetText"),
                new HarmonyMethod(typeof(OfflineMods).GetMethod("TryGetTextPrefix")), null, null, null, null);
        }

        // Once per process, before the inventory catalog and rules are published. Returns cards added.
        public static int EnsureApplied(GameConfiguration config, string activeList)
        {
            if (applied || (config == null && !LocalOnly)) return CardIds.Count;   // local content needs no game data (online, before sign-in)
            applied = true;
            string list = activeList ?? CommandLineList() ?? DefaultActiveList();
            if (!File.Exists(list)) { Debug.Log("OFFLINE MODS: no active mod list at " + list); WriteReport("No mods are enabled (no active mod list)."); Ready = true; return 0; }
            fsData root = fsJsonParser.Parse(File.ReadAllText(list));
            if (!root.IsDictionary || !root.AsDictionary.ContainsKey("mods")) throw new InvalidOperationException("active-mods.json has no 'mods' list.");
            string baseDir = Path.GetDirectoryName(list);
            List<string> dirs = new List<string>();
            foreach (fsData entry in root.AsDictionary["mods"].AsList)
            {
                string dir = entry.AsString;
                if (!Path.IsPathRooted(dir)) dir = Path.GetFullPath(Path.Combine(baseDir, dir));
                dirs.Add(dir);
            }
            // OfflineCompat: duplicates, engine level, declared conflicts and dependencies decide what loads, and in which order.
            foreach (OfflineCompat.Manifest m in OfflineCompat.Plan(dirs, Problems))
            {
                try { ApplyMod(config, m.Dir); LoadOrder.Add(m.Id); }
                catch (Exception e) { Problems.Add(m.Id + ": " + e.Message); Debug.LogError("OFFLINE MODS: " + m.Dir + " not loaded: " + e); }
            }
            foreach (string p in Problems) Debug.LogWarning("OFFLINE MODS problem: " + p);
            if (!LocalOnly) try { ApplyGlobalExtras(config); } catch (Exception e) { Problems.Add("extra emotes: " + e.Message); }
            Debug.Log("OFFLINE MODS: " + Loaded.Count + " mods, " + CardIds.Count + " cards, " + Decks.Count + " decks, " + ExtraEmotes + " extra emotes; problems " + Problems.Count + ".");
            if (OfflineModLocale.Languages > 0) Debug.Log("OFFLINE MODS: translations " + OfflineModLocale.Strings + " strings in " + OfflineModLocale.Languages + " languages; game language names: " + OfflineModLocale.LanguageNames());
            if (CommandLineList() == null) WriteReport(null);   // previews with another list leave the Play test report alone
            Ready = true;
            return CardIds.Count;
        }

        // .offline-user-data/mod-report.json: what this game start loaded and every problem, for TESL Forge's Play test.
        private static void WriteReport(string note)
        {
            try
            {
                Dictionary<string, fsData> r = new Dictionary<string, fsData>();
                r["time"] = new fsData(DateTime.Now.ToString("s"));
                r["engine_level"] = new fsData((long)OfflineCompat.ApiLevel);
                r["online_safe"] = new fsData(LocalOnly);
                if (note != null) r["note"] = new fsData(note);
                List<fsData> loaded = new List<fsData>(), problems = new List<fsData>();
                foreach (string id in LoadOrder) loaded.Add(new fsData(id));
                foreach (string p in Problems) problems.Add(new fsData(p));
                r["loaded"] = new fsData(loaded); r["problems"] = new fsData(problems);
                Dictionary<string, fsData> counts = new Dictionary<string, fsData>();
                counts["cards"] = new fsData((long)CardIds.Count); counts["decks"] = new fsData((long)Decks.Count); counts["avatars"] = new fsData((long)Avatars.Count);
                counts["AI opponents"] = new fsData((long)OfflineOpponents.All.Count); counts["game modes"] = new fsData((long)OfflineModes.Modes.Count);
                counts["stories"] = new fsData((long)OfflineStories.Loaded.Count); counts["card packs"] = new fsData((long)OfflineModPacks.All.Count);
                r["counts"] = new fsData(counts);
                string dir = Path.Combine(OfflineLaunchMode.GameRoot(), ".offline-user-data");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "mod-report.json"), tmp = file + ".tmp";
                File.WriteAllText(tmp, fsJsonPrinter.PrettyJson(new fsData(r)));
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch (Exception e) { Debug.LogWarning("OFFLINE MODS: report not written: " + e.Message); }
        }

        private static void ApplyMod(GameConfiguration config, string dir)
        {
            fsData meta = fsJsonParser.Parse(File.ReadAllText(Path.Combine(dir, "mod.json")));
            string id = meta.AsDictionary["id"].AsString, name = meta.AsDictionary.ContainsKey("name") ? meta.AsDictionary["name"].AsString : id;
            HashSet<int> existing = new HashSet<int>();
            if (config != null) foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards) if (c != null) existing.Add(c.ResourceId.Id);
            CardSet set = new CardSet { Name = "Mod_" + id, DisplayName = new LocalizedStringKey { RawKey = "mod." + id + ".set" }, Cards = new List<Card>() };
            texts["mod." + id + ".set"] = name;
            if (meta.AsDictionary.ContainsKey("collection") && meta.AsDictionary["collection"].IsString
                && string.Equals(meta.AsDictionary["collection"].AsString, "earn", StringComparison.OrdinalIgnoreCase)) EarnMods.Add(id);
            string cardsDir = Path.Combine(dir, "cards");
            if (!LocalOnly && Directory.Exists(cardsDir))
                foreach (string file in Directory.GetFiles(cardsDir, "*.json"))
                try
                {
                    string cardJson = File.ReadAllText(file);
                    Card card = OriginalGraphRules.Decode<Card>(cardJson);
                    int cid = card.ResourceId.Id;
                    if (cid == 0 || existing.Contains(cid)) { Problems.Add(id + "/" + Path.GetFileName(file) + ": card id " + cid + " already exists"); continue; }
                    if (!config.CardDefinitions.Exists(delegate(CardDefinition d) { return d.Name == card.Definition; }))
                    { Problems.Add(id + "/" + Path.GetFileName(file) + ": unknown definition " + card.Definition); continue; }
                    string slug = card.ResourceId.UniqueName;
                    if (string.IsNullOrEmpty(card.NameKey.RawKey)) card.NameKey = new LocalizedStringKey { RawKey = "mod." + id + "." + slug + ".title" };
                    if (string.IsNullOrEmpty(card.DescriptionKey.RawKey)) card.DescriptionKey = new LocalizedStringKey { RawKey = "mod." + id + "." + slug + ".text" };
                    texts[card.NameKey.RawKey] = card.Name;
                    texts[card.DescriptionKey.RawKey] = card.Description ?? "";
                    string art = Path.Combine(Path.Combine(dir, "art"), slug + ".png");
                    if (string.IsNullOrEmpty(card.Image) || !card.Image.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase)) card.Image = ImagePrefix + id + "/" + slug;
                    if (File.Exists(art)) artFiles[card.Image] = art;
                    set.Cards.Add(card); existing.Add(cid); CardIds.Add(cid); cardMod[cid] = id;
                    OfflineAudio.RegisterCard(id, dir, card, fsJsonParser.Parse(cardJson).AsDictionary, config, Problems);
                }
                catch (Exception e) { Problems.Add(id + "/cards/" + Path.GetFileName(file) + ": skipped (" + e.Message + ")"); }
            if (set.Cards.Count > 0 && config != null) config.CardCollection.Add(set);
            string decksDir = Path.Combine(dir, "decks");
            if (!LocalOnly && Directory.Exists(decksDir))
                foreach (string file in Directory.GetFiles(decksDir, "*.json"))
                try
                {
                    Dictionary<string, fsData> d = fsJsonParser.Parse(File.ReadAllText(file)).AsDictionary;
                    DeckInfo deck = new DeckInfo();
                    deck.TypeName = d["type_name"].AsString;
                    deck.TypeNameHash = (int)d["type_hash"].AsInt64;
                    deck.DisplayName = deck.TypeName + ".name";
                    deck.DisplayNameLocKey = deck.DisplayName;
                    texts[deck.DisplayName] = d["name"].AsString;
                    deck.ArtTypeHash = d.ContainsKey("art_card") ? (int)d["art_card"].AsInt64 : 0;
                    // Deck art: the suite writes the two crops original decks use (512x128 tile, 256x512 portrait);
                    // DeckTile/DeckView look them up in config.DeckArt by ArtTypeHash and load them as card images.
                    if (d.ContainsKey("deck_art") && d["deck_art"].IsDictionary)
                    {
                        Dictionary<string, fsData> art = d["deck_art"].AsDictionary;
                        string slug = Path.GetFileNameWithoutExtension(file);
                        DeckArtData data = new DeckArtData { ResourceId = new UniqueResourceId { Id = deck.TypeNameHash, UniqueName = deck.TypeName + ".art" } };
                        foreach (KeyValuePair<string, string> crop in new Dictionary<string, string> { { "tile", "Cropped" }, { "large", "Large" } })
                        {
                            if (!art.ContainsKey(crop.Key)) continue;
                            string png = Path.Combine(Path.Combine(dir, "art"), art[crop.Key].AsString);
                            string asset = ImagePrefix + id + "/" + slug + "-deck-" + crop.Key;
                            if (File.Exists(png)) artFiles[asset] = png;
                            if (crop.Value == "Cropped") data.CroppedImagePath = asset; else data.LargeImagePath = asset;
                        }
                        if (config.DeckArt == null) config.DeckArt = new List<DeckArtData>();
                        config.DeckArt.Add(data);
                        deck.ArtTypeHash = deck.TypeNameHash;
                    }
                    deck.IsProduction = true;
                    deck.Cards = new List<DeckInfo.CardInfo>();
                    foreach (fsData e in d["cards"].AsList)
                    {
                        Dictionary<string, fsData> c = e.AsDictionary;
                        int cardId = (int)c["id"].AsInt64, count = (int)c["count"].AsInt64;
                        if (!existing.Contains(cardId)) { Problems.Add(id + "/" + Path.GetFileName(file) + ": missing card " + cardId); continue; }
                        deck.Cards.Add(new DeckInfo.CardInfo { Id = cardId, Count = count, NumToGive = count });
                    }
                    if (config.DeckInfo == null) config.DeckInfo = new List<DeckInfo>();
                    if (Decks.Exists(delegate(DeckInfo x) { return x.TypeName == deck.TypeName || x.TypeNameHash == deck.TypeNameHash; }))
                    { Problems.Add(id + "/decks/" + Path.GetFileName(file) + ": deck " + deck.TypeName + " already added by an earlier mod; skipped"); continue; }
                    config.DeckInfo.Add(deck); Decks.Add(deck); deckMod[deck.TypeNameHash] = id;
                }
                catch (Exception e) { Problems.Add(id + "/decks/" + Path.GetFileName(file) + ": skipped (" + e.Message + ")"); }
            int replacedHere = 0, avatarsHere = 0;
            Section(id, "image replacements", delegate { replacedHere = LoadReplacements(id, Path.Combine(dir, "replace")); });
            if (!LocalOnly) Section(id, "avatars", delegate { avatarsHere = LoadAvatars(config, id, name, Path.Combine(dir, "avatars")); });
            if (!LocalOnly) Section(id, "card backs", delegate { LoadCardBacks(config, id, Path.Combine(dir, "cardbacks")); });
            Section(id, "boards", delegate { OfflineBoards.Load(id, Path.Combine(dir, "boards"), Problems); });
            Section(id, "music", delegate { OfflineMusic.Load(id, Path.Combine(dir, "music"), Problems); });
            Section(id, "sounds", delegate { OfflineAudio.Load(id, dir, Problems); });
            if (!LocalOnly) Section(id, "emotes", delegate { LoadGlobalExtras(id, dir); });
            if (!LocalOnly) Section(id, "AI opponents", delegate { OfflineOpponents.Load(config, id, dir, Problems); });
            Section(id, "translations", delegate { OfflineModLocale.Load(id, dir, Problems); });
            if (!LocalOnly) Section(id, "card packs", delegate { OfflineModPacks.Load(config, id, dir, Problems); });
            if (!LocalOnly) Section(id, "game modes", delegate { OfflineModes.Load(id, dir, Problems); });
            if (!LocalOnly) Section(id, "stories", delegate { OfflineStories.Load(config, id, dir, Problems); });
            Loaded.Add(id); modNames[id] = name; modFolders[id] = dir;
            Debug.Log("OFFLINE MODS: loaded " + id + " (" + set.Cards.Count + " cards, " + replacedHere + " image replacements, " + avatarsHere + " avatars).");
        }

        // One kind of a mod's content: a failure is reported and the rest of the mod (and the game) carries on.
        private static void Section(string id, string what, Action load)
        {
            try { load(); }
            catch (Exception e) { Problems.Add(id + ": " + what + " not loaded (" + e.Message + ")"); Debug.LogError("OFFLINE MODS: " + id + " " + what + ": " + e); }
        }

        // The avatar picker (AvatarPickerWin) pages through AvatarCollection.Races and shows each race's avatars in its
        // four portrait slots; every configured avatar is selectable (OfflineLocalServices publishes them all). A mod's
        // avatars get their own pages, four per page, named after the mod (or avatars.json "page").
        public const int AvatarsPerPage = 4;
        public static readonly List<AvatarData> Avatars = new List<AvatarData>();
        public static readonly List<AvatarRaceData> AvatarPages = new List<AvatarRaceData>();
        // Original rectangle avatars: 286 px wide sprites at 94.702 px/unit.
        private const float AvatarWidth = 286f, AvatarPpu = 94.70199f;

        private static int LoadAvatars(GameConfiguration config, string id, string modName, string dir)
        {
            string manifest = Path.Combine(dir, "avatars.json");
            if (!File.Exists(manifest)) return 0;
            if (config.AvatarCollection == null) { Problems.Add(id + ": avatars skipped (configuration has no avatar collection yet)"); return 0; }
            Dictionary<string, fsData> root = fsJsonParser.Parse(File.ReadAllText(manifest)).AsDictionary;
            Dictionary<string, fsData> page = root.ContainsKey("page") && root["page"].IsDictionary ? root["page"].AsDictionary : new Dictionary<string, fsData>();
            string pageName = page.ContainsKey("name") ? page["name"].AsString : modName;
            AvatarCollection col = config.AvatarCollection;
            HashSet<int> ids = new HashSet<int>();
            foreach (AvatarData a in col.Avatars) if (a != null) ids.Add(a.ResourceId.Id);
            foreach (AvatarRaceData r in col.Races) if (r != null) ids.Add(r.ResourceId.Id);
            List<fsData> list = root["avatars"].AsList;
            int pages = (list.Count + AvatarsPerPage - 1) / AvatarsPerPage, count = 0;
            AvatarRaceData current = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (i % AvatarsPerPage == 0)
                {
                    int p = i / AvatarsPerPage;
                    string key = "mod." + id + ".avatars." + p;
                    current = new AvatarRaceData
                    {
                        ResourceId = new UniqueResourceId { Id = StableId(key), UniqueName = key },
                        Name = new LocalizedStringKey { RawKey = key + ".name" },
                        Description = new LocalizedStringKey { RawKey = key + ".description" },
                        Bonus = new LocalizedStringKey { RawKey = key + ".bonus" },
                        GuestPlayerName = new LocalizedStringKey { RawKey = key + ".guest" }
                    };
                    if (!ids.Add(current.ResourceId.Id)) { Problems.Add(id + ": avatar page id collision " + key); return count; }
                    texts[current.Name.RawKey] = pages > 1 ? pageName + " (" + (p + 1) + "/" + pages + ")" : pageName;
                    texts[current.Description.RawKey] = page.ContainsKey("description") ? page["description"].AsString : "Avatars from the mod " + modName + ".";
                    texts[current.Bonus.RawKey] = page.ContainsKey("bonus") ? page["bonus"].AsString : "";
                    texts[current.GuestPlayerName.RawKey] = pageName;
                    col.Races.Add(current); AvatarPages.Add(current);
                }
                Dictionary<string, fsData> e = list[i].AsDictionary;
                string slug = e["slug"].AsString, unique = "mod." + id + ".avatar." + slug;
                string png = Path.Combine(dir, e["image"].AsString);
                if (!File.Exists(png)) { Problems.Add(id + ": avatar " + slug + " has no image"); continue; }
                AvatarData avatar = new AvatarData { ResourceId = new UniqueResourceId { Id = StableId(unique), UniqueName = unique } };
                if (!ids.Add(avatar.ResourceId.Id)) { Problems.Add(id + ": avatar id collision " + unique); continue; }
                avatar.RaceId = new UniqueResourceReference { resourceId = current.ResourceId.Id };
                // Emote voice: borrowed from an original avatar ("voice": its UniqueName), else the fallback avatar's.
                string voice = e.ContainsKey("voice") ? e["voice"].AsString : null;
                AvatarData voiceOf = col.Avatars.Find(delegate(AvatarData a) { return a != null && voice != null && a.ResourceId.UniqueName == voice; })
                    ?? col.Avatars.Find(delegate(AvatarData a) { return a != null && a.ResourceId.Id == col.FallbackAvatar.resourceId; });
                if (voice != null && (voiceOf == null || voiceOf.ResourceId.UniqueName != voice)) Problems.Add(id + ": avatar " + slug + " voice " + voice + " not found; fallback voice used");
                avatar.EmotePackage = new UniqueResourceReference { resourceId = voiceOf == null ? 0 : voiceOf.EmotePackage.resourceId };
                bool extras = e.ContainsKey("extra_emotes") && e["extra_emotes"].IsList && e["extra_emotes"].AsList.Count > 0;
                if ((e.ContainsKey("emotes") && e["emotes"].IsDictionary) || extras)   // extras need a package of its own, never the borrowed original's
                    avatar.EmotePackage = new UniqueResourceReference { resourceId = BuildEmotePackage(col, id, slug, dir,
                        e.ContainsKey("emotes") && e["emotes"].IsDictionary ? e["emotes"].AsDictionary : new Dictionary<string, fsData>(), avatar.EmotePackage.resourceId) };
                if (extras)
                {
                    AvatarEmotePackage own = OfflineEmotes.Package(col, avatar.EmotePackage.resourceId);
                    foreach (fsData x in e["extra_emotes"].AsList) AddExtraEmote(own, id, "avatar-" + slug, dir, x.AsDictionary);
                }
                avatar.Image = ImagePrefix + id + "/avatar-" + slug;
                artFiles[avatar.Image] = png;
                float w = ImageWidth(png);
                spriteShape[avatar.Image] = new float[] { AvatarPpu * (w / AvatarWidth), 0.5f, 0.5f, 0, 0, 0, 0 };
                string circle = e.ContainsKey("circle") ? Path.Combine(dir, e["circle"].AsString) : null;
                avatar.CircularImage = ImagePrefix + id + "/avatar-" + slug + "-circle";
                artFiles[avatar.CircularImage] = circle != null && File.Exists(circle) ? circle : png;
                col.Avatars.Add(avatar); Avatars.Add(avatar);
                count++;
            }
            return count;
        }

        // A mod avatar's own emote lines: {"Hello": {"text": "...", "audio": "x.wav"}, ...}. Types it leaves out keep the
        // borrowed voice's lines. Text-only lines show for 5 s (EmotesWin.ShowBubble without audio).
        public static readonly List<AvatarEmotePackage> EmotePackages = new List<AvatarEmotePackage>();
        private static int BuildEmotePackage(AvatarCollection col, string id, string slug, string dir, Dictionary<string, fsData> emotes, int voicePackage)
        {
            string key = "mod." + id + ".emotes." + slug;
            AvatarEmotePackage package = new AvatarEmotePackage { ResourceId = new UniqueResourceId { Id = StableId(key), UniqueName = key } };
            AvatarEmotePackage voice = OfflineEmotes.Package(col, voicePackage);
            if (voice != null)
                foreach (AvatarEmoteData d in voice.EmoteData)
                    if (d != null) package.EmoteData.Add(new AvatarEmoteData { EmoteType = d.EmoteType, EmoteString = d.EmoteString, EmoteAudio = d.EmoteAudio, ContentPackIndex = d.ContentPackIndex });
            foreach (KeyValuePair<string, fsData> kv in emotes)
            {
                UserEmoteType type;
                try { type = (UserEmoteType)Enum.Parse(typeof(UserEmoteType), kv.Key, true); }
                catch (ArgumentException) { Problems.Add(id + ": avatar " + slug + " unknown emote " + kv.Key); continue; }
                Dictionary<string, fsData> line = kv.Value.AsDictionary;
                AvatarEmoteData data = new AvatarEmoteData { EmoteType = type, EmoteString = new LocalizedStringKey { RawKey = key + "." + type } };
                texts[data.EmoteString.RawKey] = line.ContainsKey("text") ? line["text"].AsString : "";
                data.EmoteAudio = "";
                if (line.ContainsKey("audio"))
                {
                    string wav = Path.Combine(dir, line["audio"].AsString);
                    if (File.Exists(wav)) { data.EmoteAudio = OfflineEmotes.AudioPrefix + id + "/emote-" + slug + "-" + type; OfflineEmotes.RegisterAudio(data.EmoteAudio, wav); }
                    else Problems.Add(id + ": avatar " + slug + " emote audio missing: " + line["audio"].AsString);
                }
                package.EmoteData.RemoveAll(delegate(AvatarEmoteData x) { return x.EmoteType == type; });
                package.EmoteData.Add(data);
            }
            col.EmotePackages.Add(package); EmotePackages.Add(package);
            return package.ResourceId.Id;
        }

        // An extra emote: {"slug", "label" (button), "text" (bubble), "audio" (WAV), "like" (one of the eight, optional)}.
        private static readonly List<KeyValuePair<string, Dictionary<string, fsData>>> globalExtras = new List<KeyValuePair<string, Dictionary<string, fsData>>>();
        private static readonly Dictionary<string, string> globalExtraDirs = new Dictionary<string, string>();
        public static int ExtraEmotes;
        public static void AddExtraEmote(AvatarEmotePackage package, string id, string owner, string dir, Dictionary<string, fsData> x)
        {
            if (package == null) return;
            string slug = x.ContainsKey("slug") ? x["slug"].AsString : "extra" + ExtraEmotes;
            string text = x.ContainsKey("text") ? x["text"].AsString : "";
            string label = x.ContainsKey("label") && x["label"].AsString.Length > 0 ? x["label"].AsString : (text.Length > 22 ? text.Substring(0, 20) + "..." : text);
            string key = "mod." + id + ".extra." + owner + "." + slug + "." + package.ResourceId.Id;
            AvatarEmoteData data = new AvatarEmoteData { EmoteString = new LocalizedStringKey { RawKey = key }, EmoteAudio = "" };
            texts[key] = text;
            if (x.ContainsKey("audio") && x["audio"].AsString.Length > 0)
            {
                string wav = Path.Combine(dir, x["audio"].AsString);
                if (File.Exists(wav)) { data.EmoteAudio = OfflineEmotes.AudioPrefix + id + "/extra-" + owner + "-" + slug; OfflineEmotes.RegisterAudio(data.EmoteAudio, wav); }
                else Problems.Add(id + ": extra emote " + slug + " audio missing: " + x["audio"].AsString);
            }
            UserEmoteType? like = OfflineExtraEmotes.ParseLike(x.ContainsKey("like") ? x["like"].AsString : null);
            if (x.ContainsKey("like") && x["like"].AsString.Length > 0 && like == null) Problems.Add(id + ": extra emote " + slug + " like '" + x["like"].AsString + "' is not one of the eight");
            OfflineExtraEmotes.Add(package, data, label, like);
            ExtraEmotes++;
        }

        // Mod-wide extras (<mod>/emotes/emotes.json {"emotes": [...]}) join every avatar's emotes, originals included,
        // once all mods' avatars exist.
        private static void LoadGlobalExtras(string id, string dir)
        {
            string file = Path.Combine(Path.Combine(dir, "emotes"), "emotes.json");
            if (!File.Exists(file)) return;
            foreach (fsData x in fsJsonParser.Parse(File.ReadAllText(file)).AsDictionary["emotes"].AsList)
                globalExtras.Add(new KeyValuePair<string, Dictionary<string, fsData>>(id, x.AsDictionary));
            globalExtraDirs[id] = Path.Combine(dir, "emotes");
        }

        private static void ApplyGlobalExtras(GameConfiguration config)
        {
            if (globalExtras.Count == 0) return;
            foreach (AvatarEmotePackage p in config.AvatarCollection.EmotePackages)
                if (p != null)
                    foreach (KeyValuePair<string, Dictionary<string, fsData>> g in globalExtras) AddExtraEmote(p, g.Key, "all", globalExtraDirs[g.Key], g.Value);
        }

        public static void AvatarPickerUpdateUiPostfix(AvatarPickerWin __instance)
        {
            try
            {
                AvatarCollection col = GameManager.Instance.Config.AvatarCollection;
                int index = (int)AccessTools.Field(typeof(AvatarPickerWin), "_visibleRaceIndex").GetValue(__instance);
                UnityEngine.UI.Toggle[] toggles = (UnityEngine.UI.Toggle[])AccessTools.Field(typeof(AvatarPickerWin), "_avatarToggles").GetValue(__instance);
                if (toggles == null || index < 0 || index >= col.Races.Count) return;
                int raceId = col.Races[index].ResourceId.Id, count = 0;
                foreach (AvatarData a in col.Avatars) if (a != null && a.RaceId.resourceId == raceId) count++;
                for (int i = 0; i < toggles.Length; i++) if (toggles[i] != null) toggles[i].gameObject.SetActive(i < count);
            }
            catch (Exception e) { Debug.LogWarning("OFFLINE MODS: avatar slot visibility: " + e.Message); }
        }

        // ---- card backs ------------------------------------------------------------------------------
        // The client maps a card back key to a CardBackType through CardSharedGraphics._cardBackHashLookup (MurmurHash3 of
        // the key) and draws CardBackDataList entries; the collection lists every entry the player has as available or
        // locked. A mod card back gets a new CardBackType value (100000+), its own CardBackData, and a material: a copy of
        // an original card back material (Houses of Morrowind: same 476x838 card area in a 1024 square) with the mod's
        // texture as _Diffuse. Unlimited: one entry per card back in <mod>/cardbacks/cardbacks.json.
        public const string CardBackBase = "cardback_houses_of_morrowind";
        public static readonly List<string> CardBackKeys = new List<string>();
        private static readonly Dictionary<string, int> cardBackPrices = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> cardBackNames = new Dictionary<string, string>(), cardBackStoreArt = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> cardBackTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string cardBackBaseMaterial;
        private static int nextCardBackType = 100000;

        public static bool IsModCardBack(string key) { return key != null && cardBackPrices.ContainsKey(key); }
        public static int CardBackPrice(string key) { int p; return key != null && cardBackPrices.TryGetValue(key, out p) ? p : -1; }
        public static bool IsFreeCardBack(string key) { return CardBackPrice(key) == 0; }
        public static string CardBackName(string key) { string n; return key != null && cardBackNames.TryGetValue(key, out n) ? n : null; }
        public static string CardBackStoreArt(string key) { string a; return key != null && cardBackStoreArt.TryGetValue(key, out a) ? a : null; }

        private static void LoadCardBacks(GameConfiguration config, string id, string dir)
        {
            string manifest = Path.Combine(dir, "cardbacks.json");
            if (!File.Exists(manifest)) return;
            if (config.CardSharedGraphics == null) { Problems.Add(id + ": card backs skipped (no card graphics yet)"); return; }
            Dictionary<int, CardBackType> lookup = (Dictionary<int, CardBackType>)AccessTools.Field(typeof(CardSharedGraphics), "_cardBackHashLookup").GetValue(null);
            CardBackData baseData = config.CardSharedGraphics.FindCardBackData(CardBackBase);
            if (baseData == null || string.IsNullOrEmpty(baseData.VisualAsset)) { Problems.Add(id + ": base card back material missing"); return; }
            cardBackBaseMaterial = baseData.VisualAsset;
            foreach (fsData e in fsJsonParser.Parse(File.ReadAllText(manifest)).AsDictionary["cardbacks"].AsList)
            {
                Dictionary<string, fsData> c = e.AsDictionary;
                string slug = c["slug"].AsString, key = "mod." + id + ".cardback." + slug;
                string png = Path.Combine(dir, c["image"].AsString);
                if (!File.Exists(png)) { Problems.Add(id + ": card back " + slug + " has no image"); continue; }
                int hash = MurmurHash3.Hash(key);
                if (lookup.ContainsKey(hash)) { Problems.Add(id + ": card back key collision " + key); continue; }
                CardBackType type = (CardBackType)(nextCardBackType++);
                lookup[hash] = type;
                CardBackData data = new CardBackData { Type = type, VisualAsset = ImagePrefix + id + "/cardback-" + slug,
                    NameTextKey = new LocalizedStringKey { RawKey = key + ".name" }, UnlockTextKey = new LocalizedStringKey { RawKey = key + ".unlock" } };
                string name = c.ContainsKey("name") ? c["name"].AsString : slug;
                int price = c.ContainsKey("price") ? (int)c["price"].AsInt64 : 0;
                texts[data.NameTextKey.RawKey] = name;
                texts[data.UnlockTextKey.RawKey] = c.ContainsKey("unlock") ? c["unlock"].AsString : price > 0 ? "Buy it in the Store (Special Offers)." : "From the mod " + id + ".";
                config.CardSharedGraphics.CardBackDataList.Add(data);
                cardBackTextures[data.VisualAsset] = png;
                string store = c.ContainsKey("store") ? Path.Combine(dir, c["store"].AsString) : null;
                if (store != null && File.Exists(store)) { artFiles[data.VisualAsset + "-store"] = store; cardBackStoreArt[key] = data.VisualAsset + "-store"; }
                CardBackKeys.Add(key); cardBackPrices[key] = Math.Max(0, price); cardBackNames[key] = name; cardBackMod[key] = id;
            }
        }

        // A mod card back material: the base material copied once it has loaded, with the mod texture as _Diffuse.
        public sealed class ModCardBackOperation : AssetBundleLoadAssetOperation
        {
            private readonly AssetBundleLoadAssetOperation baseOp;
            private readonly string path, file;
            private Material made;
            public ModCardBackOperation(string path, string file)
            {
                this.path = path; this.file = file;
                baseOp = AssetCatalog.Instance.LoadAssetFromPathAsync(cardBackBaseMaterial, typeof(Material), "mat");
            }
            public override bool Update() { return baseOp != null && !baseOp.IsDone(); }
            public override bool IsDone() { return baseOp == null || baseOp.IsDone(); }
            public override T GetAsset<T>()
            {
                if (made == null && baseOp != null && baseOp.IsDone())
                {
                    Material source = baseOp.GetAsset<Material>();
                    if (source != null)
                    {
                        made = new Material(source);
                        made.name = Path.GetFileNameWithoutExtension(file);
                        made.SetTexture("_Diffuse", TextureFor(path, file));
                    }
                }
                return made as T;
            }
        }

        // FNV-1a, as the suite derives mod ids; never 0 or -1 (invalid resource ids).
        public static int StableId(string key)
        {
            uint h = 2166136261;
            foreach (char c in key.ToLowerInvariant()) { h ^= c; h *= 16777619; }
            int v = (int)h;
            return v == 0 || v == -1 ? 1 : v;
        }

        // PNG width from the IHDR header (bytes 16-19, big-endian) without decoding the image.
        private static float ImageWidth(string png)
        {
            using (FileStream f = File.OpenRead(png))
            {
                byte[] b = new byte[24];
                if (f.Read(b, 0, 24) < 24 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return AvatarWidth;
                return (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            }
        }

        private static int LoadReplacements(string id, string dir)
        {
            string manifest = Path.Combine(dir, "replace.json");
            if (!File.Exists(manifest)) return 0;
            int count = 0;
            foreach (fsData e in fsJsonParser.Parse(File.ReadAllText(manifest)).AsDictionary["images"].AsList)
            {
                Dictionary<string, fsData> r = e.AsDictionary;
                string asset = r["asset"].AsString, png = Path.Combine(dir, r["file"].AsString);
                if (asset.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase)) { Problems.Add(id + ": replacement of mod asset " + asset + " ignored"); continue; }
                if (!File.Exists(png)) { Problems.Add(id + ": replacement file missing for " + asset); continue; }
                float width = Num(r["width"]), scale = 1f, ppu = 100f, px = 0.5f, py = 0.5f;
                float[] border = new float[4];
                if (r.ContainsKey("original"))
                {
                    Dictionary<string, fsData> o = r["original"].AsDictionary;
                    if (o.ContainsKey("width") && Num(o["width"]) > 0) scale = width / Num(o["width"]);
                    if (o.ContainsKey("ppu")) ppu = Num(o["ppu"]);
                    if (o.ContainsKey("pivot")) { List<fsData> p = o["pivot"].AsList; px = Num(p[0]); py = Num(p[1]); }
                    if (o.ContainsKey("border")) { List<fsData> b = o["border"].AsList; for (int i = 0; i < 4; i++) border[i] = Num(b[i]) * scale; }
                }
                artFiles[asset] = png;
                textures.Remove(asset); sprites.Remove(asset);
                spriteShape[asset] = new float[] { ppu * scale, px, py, border[0], border[1], border[2], border[3] };
                if (!Replaced.Contains(asset)) Replaced.Add(asset);
                count++;
            }
            return count;
        }

        private static float Num(fsData d) { return d.IsInt64 ? d.AsInt64 : (float)d.AsDouble;
        }

        // Each mod deck, then each mod card topped up to a playset, once per deck/card (markers in grantedChoices).
        // Decks first: a granted deck already adds its card copies.
        public static int GrantOwnership(OfflinePlayerStateData d)
        {
            int granted = 0;
            foreach (DeckInfo deck in Decks)
            {
                if (IsEarnedDeck(deck.TypeNameHash)) continue;   // bought in the store instead
                string marker = "mod-deck:" + deck.TypeNameHash;
                if (d.grantedChoices.Contains(marker)) continue;
                if (OfflineInventoryService.GrantDeck(d, deck)) granted++;
                d.grantedChoices.Add(marker);
            }
            foreach (int id in CardIds)
            {
                if (IsEarnedCard(id)) continue;                  // from packs, rewards and crafting instead
                string marker = "mod-card:" + id;
                if (d.grantedChoices.Contains(marker)) continue;
                Card card = GameManager.Instance.GetCard(id);
                int copies = card == null ? 3 : (card.Unique ? 1 : Math.Max(1, card.MaxCopies));
                if (d.TotalCopies(id) < copies) d.AddCards(id, copies - d.TotalCopies(id));
                d.grantedChoices.Add(marker); granted++;
            }
            return granted;
        }

        // ---- client hooks ------------------------------------------------------------------------
        public static bool LoadAssetPrefix(string path, Type type, ref AssetBundleLoadAssetOperation __result)
        {
            string file;
            if (!string.IsNullOrEmpty(path) && type != null && typeof(Material).IsAssignableFrom(type) && cardBackTextures.TryGetValue(path, out file))
            {
                __result = new ModCardBackOperation(path, file);
                return false;
            }
            if (string.IsNullOrEmpty(path) || type == null || !artFiles.TryGetValue(path, out file)) return true;
            if (!typeof(Texture).IsAssignableFrom(type) && !typeof(Sprite).IsAssignableFrom(type)) return true;
            UnityEngine.Object asset = typeof(Sprite).IsAssignableFrom(type) ? (UnityEngine.Object)SpriteFor(path, file) : TextureFor(path, file);
            __result = new AssetBundleLoadAssetOperationSimulation(asset);
            return false;
        }

        public static bool LoadAssetExtPrefix(string path, Type type, string extension, ref AssetBundleLoadAssetOperation __result)
        {
            return LoadAssetPrefix(path, type, ref __result);
        }

        public static Texture2D TextureFor(string path, string file)
        {
            Texture2D tex;
            if (textures.TryGetValue(path, out tex) && tex != null) return tex;
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(file), false)) throw new InvalidOperationException("Mod art is not a readable image: " + file);
            tex.name = Path.GetFileNameWithoutExtension(file);
            tex.wrapMode = TextureWrapMode.Clamp;   // as the original card art (no edge bleed from the opposite side)
            textures[path] = tex;
            return tex;
        }

        public static Sprite SpriteFor(string path, string file)
        {
            Sprite sprite;
            if (sprites.TryGetValue(path, out sprite) && sprite != null) return sprite;
            Texture2D tex = TextureFor(path, file);
            float[] shape;
            if (spriteShape.TryGetValue(path, out shape))
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(shape[1], shape[2]), shape[0], 0,
                    SpriteMeshType.FullRect, new Vector4(shape[3], shape[4], shape[5], shape[6]));
            else
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = tex.name;
            sprites[path] = sprite;
            return sprite;
        }

        public static bool GetTextPrefix(string key, ref string __result)
        {
            string text;
            if (key == null || !texts.TryGetValue(key, out text)) return true;
            __result = OfflineModLocale.Translate(key, text) ?? text; return false;
        }

        public static bool TryGetTextPrefix(string key, ref string text, ref bool __result)
        {
            string value;
            if (key == null || !texts.TryGetValue(key, out value)) return true;
            text = OfflineModLocale.Translate(key, value) ?? value; __result = true; return false;
        }

        public static void SetText(string key, string text) { if (key != null) texts[key] = text ?? ""; }
        public static void AddArt(string asset, string file) { artFiles[asset] = file; textures.Remove(asset); sprites.Remove(asset); }
        public static bool HasArt(string imagePath) { return imagePath != null && artFiles.ContainsKey(imagePath); }
        public static string Text(string key) { string t; return key != null && texts.TryGetValue(key, out t) ? t : null; }
    }
}
