using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AssetBundles;
using FullSerializer;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mod stories (<mod>/stories/<slug>/story.json, images and voice files beside it). Story content in this client is
    // configuration data (GameConfiguration.Campaigns: Campaign > Act > Chapter > ChapterEvent; dialogue in
    // StoryDialogueModal/StoryDialogueData with up to three GatedStoryDialogueOptions; battles as GatedScenario > Scenario);
    // the services only hold progress. So a mod story is built into the same objects at load (before the campaign content
    // is published) and the client's own story screens play it: the story picker (its hard-coded list is extended), the
    // episode select, the story map with event coins at map positions, dialogue with choices, PlayWin and the match.
    //   Battles reuse the game-mode setup (OfflineModes.Parse): lanes, per-side health/magicka/runes/decks/start cards,
    //   first player, turn rules, board, music, gold. Each battle gets its own Scenario (a copy of the Default scenario, so
    //   the server runs it under StoryScenarioRules exactly as modes), AiOpponent (name + portrait), intro briefing
    //   (MatchIntroModalData) and victory lines (MatchDialogueData).
    //   Branching: event and line requirements are ChapterEventOutcomes (an earlier event complete, or its intro/ending
    //   choice 1-3). Mod chapters reveal events as their requirements are met (OfflineCampaignService); one event can hold
    //   several battles, picked by the client's own GetScenarioIdToLaunch from the event's intro choice.
    //   Rewards per event use the existing reward bundle keys (gold, card, pack, xp, gems); a choice may give a card or gold.
    //   Voice: a line's "voice" file plays when the client asks LocalizationManager.GetDialogueAudio for it.
    // Images: "file.png" in the story folder, or "asset:<path>" to use an installed image (for example a story map).
    public static class OfflineStories
    {
        public sealed class Battle
        {
            public OfflineModes.Mode Mode; public string ScenarioName; public int ScenarioId; public bool FixedDeck; public int EventId;
        }
        public static readonly Dictionary<string, Battle> Battles = new Dictionary<string, Battle>();
        public static readonly HashSet<int> CampaignIds = new HashSet<int>();
        public static readonly HashSet<int> ChapterIds = new HashSet<int>();
        public static readonly List<string> NameKeys = new List<string>();
        public static readonly List<string> Loaded = new List<string>();
        private static readonly HashSet<int> fixedDeckScenarios = new HashSet<int>();
        private static readonly Dictionary<string, string> voices = new Dictionary<string, string>();
        public const string VoicePrefix = "modvoice:";
        public static int VoicesPlayed;
        public static string LastVoice;

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(StoryPickerWin), "Awake"), null, new HarmonyMethod(typeof(OfflineStories).GetMethod("PickerAwakePostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(StoryPickerWin), "ShowStoryPickerElements"), null, new HarmonyMethod(typeof(OfflineStories).GetMethod("PickerShowPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(Scenario), "ScenarioAllowsUserDeckSelection"), null, new HarmonyMethod(typeof(OfflineStories).GetMethod("DeckSelectionPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(CampaignService), "ChapterHasNewEndingCinematicToShow"), null, new HarmonyMethod(typeof(OfflineStories).GetMethod("NewEndingPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(global::StoryState), "OnChapterEndingModalComplete"), null, new HarmonyMethod(typeof(OfflineStories).GetMethod("EndingModalCompletePostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(StoryMapWin), "TryShowChapterEndingModal"), new HarmonyMethod(typeof(OfflineStories).GetMethod("ChapterEndingModalPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(EpisodeSelectPanel), "UpdateUi"), new HarmonyMethod(typeof(OfflineStories).GetMethod("EpisodePanelPrefix")), new HarmonyMethod(typeof(OfflineStories).GetMethod("EpisodePanelPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(LocalizationManager), "GetDialogueAudio"), new HarmonyMethod(typeof(OfflineStories).GetMethod("DialogueAudioPrefix")), null, null, null, null);
        }

        // ---- loading ---------------------------------------------------------------------------------------------
        private static string S(Dictionary<string, fsData> d, string k, string def) { return d != null && d.ContainsKey(k) && d[k].IsString ? d[k].AsString : def; }
        private static int I(Dictionary<string, fsData> d, string k, int def) { return d != null && d.ContainsKey(k) && !d[k].IsNull && (d[k].IsInt64 || d[k].IsDouble) ? (int)(d[k].IsInt64 ? d[k].AsInt64 : (long)d[k].AsDouble) : def; }
        private static float F(Dictionary<string, fsData> d, string k, float def) { return d != null && d.ContainsKey(k) && (d[k].IsInt64 || d[k].IsDouble) ? (d[k].IsInt64 ? d[k].AsInt64 : (float)d[k].AsDouble) : def; }
        private static bool B(Dictionary<string, fsData> d, string k) { return d != null && d.ContainsKey(k) && d[k].IsBool && d[k].AsBool; }
        private static List<Dictionary<string, fsData>> L(Dictionary<string, fsData> d, string k)
        {
            List<Dictionary<string, fsData>> list = new List<Dictionary<string, fsData>>();
            if (d != null && d.ContainsKey(k) && d[k].IsList) foreach (fsData e in d[k].AsList) if (e.IsDictionary) list.Add(e.AsDictionary);
            return list;
        }

        public static int Id(string path) { return OfflineMods.StableId("story:" + path); }
        private static UniqueResourceId Rid(string path) { return new UniqueResourceId { Id = Id(path), UniqueName = "MOD:" + path }; }
        private static UniqueResourceReference Ref(int id) { UniqueResourceReference r = new UniqueResourceReference(); r.resourceId = id; return r; }
        private static LocalizedStringKey T(string key, string text) { OfflineMods.SetText(key, text ?? ""); return new LocalizedStringKey { RawKey = key }; }
        private static LocalizedStringKey None() { return new LocalizedStringKey { RawKey = "" }; }

        private sealed class Ctx
        {
            public GameConfiguration Config; public string ModId, Key, Dir; public List<string> Problems; public Campaign Template;
            public string ActSlug, ChapterSlug; public Scenario DefaultScenario; public AiOpponent DefaultAi; public bool Master = true;
        }

        // An image value: a file in the story folder (registered as a mod asset), or "asset:<installed path>".
        private static string Img(Ctx c, string value, string fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            if (value.StartsWith("asset:", StringComparison.OrdinalIgnoreCase)) return value.Substring(6);
            string file = Path.Combine(c.Dir, value);
            if (!File.Exists(file)) { c.Problems.Add(c.Key + ": image missing: " + value); return fallback; }
            string asset = OfflineMods.ImagePrefix + c.ModId + "/stories/" + c.Key.Substring(c.ModId.Length + 1) + "/" + Path.GetFileNameWithoutExtension(value);
            OfflineMods.AddArt(asset, file);
            return asset;
        }

        public static void Load(GameConfiguration config, string modId, string modDir, List<string> problems)
        {
            string root = Path.Combine(modDir, "stories");
            if (!Directory.Exists(root)) return;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string file = Path.Combine(dir, "story.json");
                if (!File.Exists(file)) continue;
                try { LoadStory(config, modId, dir, fsJsonParser.Parse(File.ReadAllText(file)).AsDictionary, problems); }
                catch (Exception e) { problems.Add(modId + "/stories/" + Path.GetFileName(dir) + ": " + e.Message); Debug.LogError("OFFLINE STORIES: " + dir + " not loaded: " + e); }
            }
        }

        private static Campaign FindCampaign(GameConfiguration config, string name)
        {
            Campaign found = config.Campaigns.Find(delegate(Campaign x) { return x != null && string.Equals(x.ResourceId.UniqueName, name, StringComparison.OrdinalIgnoreCase); });
            return found ?? config.Campaigns.Find(delegate(Campaign x) { return x != null && x.ResourceId.UniqueName == "DBH"; });
        }

        private static void LoadStory(GameConfiguration config, string modId, string dir, Dictionary<string, fsData> d, List<string> problems)
        {
            string slug = S(d, "slug", Path.GetFileName(dir));
            Ctx c = new Ctx { Config = config, ModId = modId, Key = modId + "/" + slug, Dir = dir, Problems = problems };
            c.Template = FindCampaign(config, S(d, "template", "DBH"));
            if (c.Template == null || c.Template.Acts.Count == 0 || c.Template.Acts[0].Chapters.Count == 0) throw new InvalidOperationException("no installed template story");
            c.DefaultScenario = config.Scenarios.Find(delegate(Scenario x) { return x.ResourceId.UniqueName == "Default"; });
            if (c.DefaultScenario == null) throw new InvalidOperationException("the Default scenario is absent");
            c.DefaultAi = FirstTemplateAi(config, c.Template);
            c.Master = !d.ContainsKey("master") || !d["master"].IsBool || d["master"].AsBool;   // "master": false = Normal only
            Chapter tChapter = c.Template.Acts[0].Chapters[0];
            string tCoin = tChapter.ChapterEvents.Count > 0 ? tChapter.ChapterEvents[0].CoinImage : null;

            Campaign campaign = new Campaign();
            campaign.ResourceId = Rid(c.Key);
            campaign.Production = true;
            campaign.DisplayNameKey = T("mod.story." + c.Key + ".name", S(d, "name", slug));
            campaign.DisplayDescriptionKey = T("mod.story." + c.Key + ".description", S(d, "description", ""));
            campaign.ContentPackIndex = 0;
            campaign.DisplayIcon = Img(c, S(d, "icon", null), c.Template.DisplayIcon);
            campaign.DisplayBanner = Img(c, S(d, "banner", null), c.Template.DisplayBanner);
            campaign.IsLinear = false; campaign.IsPuzzle = false;
            string skin = S(d, "skin", "template");
            campaign.EpisodeSelectSkin = skin == "none" ? "" : skin == "template" ? c.Template.EpisodeSelectSkin : FindCampaign(config, skin).EpisodeSelectSkin;

            List<Dictionary<string, fsData>> acts = L(d, "acts");
            for (int a = 0; a < acts.Count; a++)
            {
                Dictionary<string, fsData> ad = acts[a];
                c.ActSlug = S(ad, "slug", "act" + (a + 1));
                string actPath = c.Key + "/" + c.ActSlug;
                Act tAct = c.Template.Acts[Math.Min(a, c.Template.Acts.Count - 1)];
                Act act = new Act { ResourceId = Rid(actPath), Production = true, DisplayNameKey = T("mod.story." + actPath + ".name", S(ad, "name", "Act " + (a + 1))) };
                string actDescription = S(ad, "description", "");
                act.DisplayDescriptionOwnedKey = T("mod.story." + actPath + ".description", actDescription);
                act.DisplayDescriptionUnownedKey = act.DisplayDescriptionOwnedKey;
                act.DisplayDescription = actDescription;
                act.DisplayIcon = Img(c, S(ad, "icon", null), tAct.DisplayIcon);
                List<Dictionary<string, fsData>> chapters = L(ad, "chapters");
                for (int ch = 0; ch < chapters.Count; ch++)
                    act.Chapters.Add(BuildChapter(c, chapters[ch], ch, tAct.Chapters[Math.Min(ch, tAct.Chapters.Count - 1)], tCoin));
                if (act.Chapters.Count == 0) { problems.Add(actPath + ": an act needs at least one chapter"); continue; }
                campaign.Acts.Add(act);
            }
            if (campaign.Acts.Count == 0) throw new InvalidOperationException("a story needs at least one act with a chapter");
            config.Campaigns.RemoveAll(delegate(Campaign x) { return x != null && x.ResourceId.Id == campaign.ResourceId.Id; });
            config.Campaigns.Add(campaign);
            CampaignIds.Add(campaign.ResourceId.Id);
            if (!NameKeys.Contains(campaign.DisplayNameKey.RawKey)) NameKeys.Add(campaign.DisplayNameKey.RawKey);
            Loaded.Add(c.Key);
            Debug.Log("OFFLINE STORIES: " + c.Key + " (" + campaign.Acts.Count + " acts) id " + campaign.ResourceId.Id);
        }

        private static AiOpponent FirstTemplateAi(GameConfiguration config, Campaign template)
        {
            foreach (Act act in template.Acts) foreach (Chapter ch in act.Chapters) foreach (ChapterEvent e in ch.ChapterEvents) foreach (GatedScenario g in e.GatedScenarios)
            {
                Scenario s = config.Scenarios.Find(delegate(Scenario x) { return x.ResourceId.Id == g.Scenario.resourceId; });
                if (s == null) continue;
                AiOpponent ai = config.AiOpponents.Find(delegate(AiOpponent x) { return x.ResourceId.Id == s.AiOpponent.resourceId; });
                if (ai != null) return ai;
            }
            return config.AiOpponents.Count > 0 ? config.AiOpponents[0] : null;
        }

        private static Chapter BuildChapter(Ctx c, Dictionary<string, fsData> cd, int index, Chapter template, string templateCoin)
        {
            c.ChapterSlug = S(cd, "slug", "chapter" + (index + 1));
            string path = c.Key + "/" + c.ActSlug + "/" + c.ChapterSlug;
            string name = S(cd, "name", "Chapter " + (index + 1));
            Chapter chapter = new Chapter { ResourceId = Rid(path), Production = true };
            chapter.DisplayNameLockedKey = T("mod.story." + path + ".name", name);
            chapter.DisplayNameUnlockedKey = chapter.DisplayNameLockedKey; chapter.DisplayNameCompletedKey = chapter.DisplayNameLockedKey;
            chapter.DisplaySubTitleKey = T("mod.story." + path + ".subtitle", S(cd, "subtitle", ""));
            chapter.DisplayChapterNumber = T("mod.story." + path + ".number", S(cd, "number", (index + 1).ToString()));
            chapter.DisplayRulesDescriptionKey = T("mod.story." + path + ".rules", S(cd, "rules", ""));
            chapter.DisplayIcon = Img(c, S(cd, "icon", null), template.DisplayIcon);
            chapter.MapImage = Img(c, S(cd, "map", null), template.MapImage);
            chapter.UseTreasureLockedIcon = template.UseTreasureLockedIcon;
            chapter.DisplayDescription = S(cd, "description", "");
            chapter.NewAllyImage = null; chapter.NewAllyLocKey = None();
            // "coin": the picture for chapter lines without a portrait (the narrator), else the template's chapter disc.
            string chapterCoin = Img(c, S(cd, "coin", null), null);
            chapter.IntroModal = Modal(c, path + "/intro", name, L(cd, "intro"), chapterCoin);
            chapter.EndingModal = Modal(c, path + "/ending", name, L(cd, "ending"), chapterCoin);
            List<Dictionary<string, fsData>> events = L(cd, "events");
            for (int i = 0; i < events.Count; i++)
            {
                Dictionary<string, fsData> ed = events[i];
                ChapterEvent e = BuildEvent(c, ed, i, templateCoin, chapter);
                chapter.ChapterEvents.Add(e);
                // Required for the chapter unless it is a branch (needs a particular choice) or the story says otherwise.
                bool branch = e.RequiredOutcomes.Exists(delegate(ChapterEventOutcome o) { return o.ModalType != DialogueModalType.None; });
                bool required = ed.ContainsKey("required") ? B(ed, "required") : !branch;
                if (required) chapter.ChapterEventsToComplete.Add(Ref(e.ResourceId.Id));
            }
            if (chapter.ChapterEvents.Count > 0 && chapter.ChapterEventsToComplete.Count == 0) chapter.ChapterEventsToComplete.Add(Ref(chapter.ChapterEvents[0].ResourceId.Id));
            ChapterIds.Add(chapter.ResourceId.Id);
            return chapter;
        }

        private static ChapterEvent BuildEvent(Ctx c, Dictionary<string, fsData> ed, int index, string templateCoin, Chapter chapter)
        {
            string slug = S(ed, "slug", "event" + (index + 1));
            string path = c.Key + "/" + c.ActSlug + "/" + c.ChapterSlug + "/" + slug;
            string name = S(ed, "name", slug);
            ChapterEvent e = new ChapterEvent { ResourceId = Rid(path), Production = true };
            // x/y: fractions of the map picture from its top-left corner; the client places coins at -1..1 from the map centre.
            e.Position = new MapPosition { x = F(ed, "x", 0.2f + 0.15f * (index % 5)) * 2f - 1f, y = 1f - F(ed, "y", 0.4f + 0.2f * (index / 5)) * 2f };
            List<Dictionary<string, fsData>> intro = L(ed, "intro"), ending = L(ed, "ending");
            e.IntroModal = Modal(c, path + "/intro", name, intro, null);
            e.EndingModal = Modal(c, path + "/ending", name, ending, null);
            string firstPortrait = intro.Count > 0 ? S(intro[0], "portrait", null) : null;
            e.CoinImage = Img(c, S(ed, "coin", firstPortrait), templateCoin);
            e.IsHiddenFromMap = B(ed, "hidden");
            e.RequiredOutcomes = Outcomes(c, ed, "requires");
            e.AltRequiredOutcomes = new List<ChapterEventOutcome>();
            int cards;
            e.RewardsBundleKey = Rewards(c, L(ed, "rewards"), path, chapter, out cards);
            int masterCards = 0;
            e.MasterRewardsBundleKey = c.Master ? Rewards(c, ed.ContainsKey("master_rewards") ? L(ed, "master_rewards") : L(ed, "rewards"), path, chapter, out masterCards) : null;
            e.NormalCardRewardUiCount = cards; e.MasterCardRewardUiCount = masterCards;
            e.IntroFirstChoiceRewardsBundleKey = null; e.IntroSecondChoiceRewardsBundleKey = null;
            e.IntroFirstChoiceMasterRewardsBundleKey = null; e.IntroSecondChoiceMasterRewardsBundleKey = null;
            List<Dictionary<string, fsData>> battles = L(ed, "battles");
            if (ed.ContainsKey("battle") && ed["battle"].IsDictionary) battles.Insert(0, ed["battle"].AsDictionary);
            for (int b = 0; b < battles.Count; b++)
            {
                Scenario s = BuildScenario(c, battles[b], path + (b == 0 ? "" : "#" + b), name, e.ResourceId.Id);
                // Master: the same battle with the battle's "master" changes (default: an Expert AI).
                Scenario ms = c.Master ? BuildScenario(c, MasterOf(battles[b]), path + (b == 0 ? "" : "#" + b) + "#master", name, e.ResourceId.Id) : null;
                GatedScenario g = new GatedScenario { Scenario = Ref(s.ResourceId.Id), MasterScenario = Ref(ms == null ? UniqueResourceId.GetInvalid().Id : ms.ResourceId.Id) };
                g.RequiredOutcomes = Outcomes(c, battles[b], "requires");
                g.TitleAwarded = Ref(UniqueResourceId.GetInvalid().Id); g.MasterTitleAwarded = Ref(UniqueResourceId.GetInvalid().Id);
                e.GatedScenarios.Add(g);
            }
            if (e.GatedScenarios.Count == 0 && e.IntroModal.DialogueDataEvents.Count == 0)
                c.Problems.Add(path + ": an event needs a battle or intro dialogue (it could never be completed)");
            return e;
        }

        // "requires": [{ "event": "slug" | "chapter/event" | "act/chapter/event", "choice": 1-3, "when": "intro"|"ending" }]
        // Without a choice the event must be complete.
        private static List<ChapterEventOutcome> Outcomes(Ctx c, Dictionary<string, fsData> d, string field)
        {
            List<ChapterEventOutcome> list = new List<ChapterEventOutcome>();
            foreach (Dictionary<string, fsData> r in L(d, field))
            {
                string[] parts = S(r, "event", "").Split('/');
                string act = parts.Length >= 3 ? parts[parts.Length - 3] : c.ActSlug;
                string chapter = parts.Length >= 2 ? parts[parts.Length - 2] : c.ChapterSlug;
                string ev = parts[parts.Length - 1];
                if (ev.Length == 0) { c.Problems.Add(c.Key + ": a requirement names no event"); continue; }
                string actPath = c.Key + "/" + act, chapterPath = actPath + "/" + chapter;
                ChapterEventOutcome o = new ChapterEventOutcome { DisplayName = S(r, "event", "") };
                o.Act = Ref(Id(actPath)); o.Chapter = Ref(Id(chapterPath)); o.ChapterEvent = Ref(Id(chapterPath + "/" + ev));
                int choice = I(r, "choice", 0);
                o.ModalType = choice <= 0 ? DialogueModalType.None : S(r, "when", "intro") == "ending" ? DialogueModalType.Ending : DialogueModalType.Intro;
                o.ModalChoiceIndex = choice <= 0 ? -1 : Math.Min(3, choice) - 1;
                list.Add(o);
            }
            return list;
        }

        // Dialogue: [{ "speaker", "text", "portrait", "voice", "end": bool, "requires": [...],
        //              "choices": [{ "text", "description", "card", "gold", "requires": [...] }] }]
        // A line without choices gets one "Continue" button (the client advances lines only through a choice button).
        private static StoryDialogueModal Modal(Ctx c, string path, string title, List<Dictionary<string, fsData>> lines, string fallbackPortrait)
        {
            StoryDialogueModal modal = new StoryDialogueModal();
            modal.PopupTitleKey = T("mod.story." + path + ".title", title);
            for (int i = 0; i < lines.Count; i++)
            {
                Dictionary<string, fsData> ld = lines[i];
                string speaker = S(ld, "speaker", null), text = S(ld, "text", "");
                StoryDialogueData data = new StoryDialogueData();
                data.DialogueTextKey = T("mod.story." + path + "." + i, string.IsNullOrEmpty(speaker) ? text : "<b>" + speaker + "</b>\n" + text);
                data.CoinImage = Img(c, S(ld, "portrait", null), fallbackPortrait);
                data.ContentPackIndex = 0;
                data.EndConversationBranch = B(ld, "end");
                string voice = S(ld, "voice", null);
                if (!string.IsNullOrEmpty(voice))
                {
                    string file = Path.Combine(c.Dir, voice);
                    if (File.Exists(file)) { data.DialogueAudio = VoicePrefix + path + "." + i; voices[data.DialogueAudio] = file; OfflineAudio.AddPreload(file); }
                    else c.Problems.Add(path + ": voice file missing: " + voice);
                }
                List<ChapterEventOutcome> lineRequires = Outcomes(c, ld, "requires");
                List<Dictionary<string, fsData>> choices = L(ld, "choices");
                if (choices.Count == 0)
                {
                    Dictionary<string, fsData> next = new Dictionary<string, fsData>();
                    next["text"] = new fsData(S(ld, "continue", "Continue"));
                    choices.Add(next);
                }
                if (choices.Count > 3) { c.Problems.Add(path + ": at most 3 choices per line"); choices.RemoveRange(3, choices.Count - 3); }
                for (int o = 0; o < choices.Count; o++)
                {
                    GatedStoryDialogueOption option = Option(c, path + "." + i + ".option" + (o + 1), choices[o]);
                    option.RequiredOutcomes.AddRange(lineRequires);
                    if (o == 0) data.Option1 = option; else if (o == 1) data.Option2 = option; else data.Option3 = option;
                }
                modal.DialogueDataEvents.Add(data);
            }
            return modal;
        }

        private static GatedStoryDialogueOption Option(Ctx c, string key, Dictionary<string, fsData> od)
        {
            GatedStoryDialogueOption option = new GatedStoryDialogueOption();
            option.ButtonTextKey = T("mod.story." + key, S(od, "text", "Continue"));
            string card = S(od, "card", null);
            int gold = I(od, "gold", 0);
            string description = S(od, "description", null);
            option.CardChoice = Ref(UniqueResourceId.GetInvalid().Id);
            if (!string.IsNullOrEmpty(card))
            {
                Card found = OfflineModes.FindCard(c.Config, card);
                if (found == null) c.Problems.Add(key + ": no card " + card);
                else { option.CardChoice = Ref(found.ResourceId.Id); if (description == null) description = found.Name; }
            }
            option.GoldChoice = Math.Max(0, gold);
            if (description == null && gold > 0) description = "+" + gold + " gold";
            option.ButtonDescriptionKey = string.IsNullOrEmpty(description) ? None() : T("mod.story." + key + ".description", description);
            option.ButtonDescriptionKeyMaster = None();
            option.RequiredOutcomes = Outcomes(c, od, "requires");
            return option;
        }

        // Event rewards as the existing bundle keys: [{ "gold": N } | { "card": "name" } | { "pack": "skyrim" } | { "xp": N } | { "gems": N }]
        private static string Rewards(Ctx c, List<Dictionary<string, fsData>> rewards, string path, Chapter chapter, out int cards)
        {
            cards = 0;
            List<string> keys = new List<string>();
            foreach (Dictionary<string, fsData> r in rewards)
            {
                if (I(r, "gold", 0) > 0) { keys.Add("story_choice_gold_" + I(r, "gold", 0)); chapter.HasRewardGold = true; }
                if (I(r, "gems", 0) > 0) { keys.Add("arena_gems_" + I(r, "gems", 0)); chapter.HasRewardGems = true; }
                if (I(r, "xp", 0) > 0) keys.Add(OfflineLevelService.XpPrefix + I(r, "xp", 0));
                string card = S(r, "card", null);
                if (!string.IsNullOrEmpty(card))
                {
                    Card found = OfflineModes.FindCard(c.Config, card);
                    if (found == null) c.Problems.Add(path + ": reward card not found: " + card);
                    else { int n = Math.Max(1, I(r, "count", 1)); for (int k = 0; k < n; k++) keys.Add("story_choice_card_" + found.ResourceId.Id); cards += n; chapter.HasRewardCards = true; }
                }
                string pack = S(r, "pack", null);
                if (!string.IsNullOrEmpty(pack)) { keys.Add("quest_pack_" + OfflinePackService.CanonicalBundle(pack.StartsWith("cardpack_") ? pack : "cardpack_" + pack)); chapter.HasRewardPacks = true; }
            }
            return keys.Count == 0 ? null : string.Join(",", keys.ToArray());
        }

        // A battle's Master version: its own settings with "master" applied on top (player/opponent/rules merged one level).
        private static Dictionary<string, fsData> MasterOf(Dictionary<string, fsData> battle)
        {
            Dictionary<string, fsData> m = new Dictionary<string, fsData>(battle);
            m.Remove("master");
            Dictionary<string, fsData> over = battle.ContainsKey("master") && battle["master"].IsDictionary ? battle["master"].AsDictionary : null;
            if (over == null) { m["difficulty"] = new fsData("Expert"); return m; }
            foreach (KeyValuePair<string, fsData> kv in over)
            {
                if (kv.Value.IsDictionary && m.ContainsKey(kv.Key) && m[kv.Key].IsDictionary)
                {
                    Dictionary<string, fsData> merged = new Dictionary<string, fsData>(m[kv.Key].AsDictionary);
                    foreach (KeyValuePair<string, fsData> inner in kv.Value.AsDictionary) merged[inner.Key] = inner.Value;
                    m[kv.Key] = new fsData(merged);
                }
                else m[kv.Key] = kv.Value;
            }
            if (!over.ContainsKey("difficulty")) m["difficulty"] = new fsData("Expert");
            return m;
        }

        private static T CopyFields<T>(T source) where T : class, new()
        {
            T copy = new T();
            foreach (FieldInfo f in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) f.SetValue(copy, f.GetValue(source));
            return copy;
        }

        // A battle's scenario: the Default scenario (as game modes play) with this battle's id, AI opponent, briefing and
        // victory lines. The setup itself is a game mode (OfflineModes) applied by OfflineNativeMatch for this scenario.
        private static Scenario BuildScenario(Ctx c, Dictionary<string, fsData> bd, string path, string eventName, int eventId)
        {
            OfflineModes.Mode mode = OfflineModes.Parse(bd, c.ModId, path, c.Problems);
            if (mode.Name == "Mode") mode.Name = eventName;
            Scenario s = CopyFields(c.DefaultScenario);
            s.ResourceId = Rid(path + "#scenario");
            s.DisplayName = eventName; s.DisplayDescription = mode.Description;
            s.GameProperties = CopyFields(c.DefaultScenario.GameProperties);
            if (mode.NoMulligan) s.GameProperties.MulliganDisabled = true;
            s.AvatarOverrides = new List<AvatarOverride>(); s.DeckOverrides = new List<DeckOverride>(); s.PlayerOverrides = new List<PlayerOverride>();
            s.GameZoneOverrides = new List<GameZoneOverrideList>(); s.TriggeredEffects = new List<TriggeredEffect>();
            s.MatchEndDialogues = new List<UniqueResourceReference>();
            s.DoinkerClientEvents = new List<DoinkerClientEventData>(); s.ClientRestrictions = new List<ClientRestrictionData>();
            s.IsSinglePlayer = true;
            s.StartingPlayer = mode.First == "player" ? SinglePlayerFirstToAct.HumanPlayer : mode.First == "opponent" ? SinglePlayerFirstToAct.AiPlayer : c.DefaultScenario.StartingPlayer;
            s.PlayerAvatarDataOverride = Ref(UniqueResourceId.GetInvalid().Id);
            // The opponent: its name and portrait (a file, an installed image, or "ai:<installed AI opponent>").
            string portrait = S(bd, "opponent_portrait", null);
            AiOpponent borrowed = null;
            OfflineOpponents.Opponent named = OfflineOpponents.Find(mode.AiRef);
            if (named != null)
            {
                borrowed = c.Config.AiOpponents.Find(delegate(AiOpponent x) { return x.ResourceId.Id == named.AiOpponentId; });
                if (mode.OpponentName == null) mode.OpponentName = named.Name;
                if (!bd.ContainsKey("difficulty") && named.Difficulty != "chosen") mode.Difficulty = named.Difficulty;
            }
            else if (!string.IsNullOrEmpty(mode.AiRef)) c.Problems.Add(path + ": no AI opponent " + mode.AiRef + " (opponents load in mod order; enable that mod first)");
            if (portrait != null && portrait.StartsWith("ai:"))
            {
                string wanted = portrait.Substring(3);
                borrowed = c.Config.AiOpponents.Find(delegate(AiOpponent x) { return x.ResourceId.UniqueName == wanted || string.Equals(OfflineMods.Text(x.DisplayNameKey.RawKey) ?? LocalizedOrNull(x.DisplayNameKey.RawKey), wanted, StringComparison.OrdinalIgnoreCase); });
                if (borrowed == null) c.Problems.Add(path + ": no installed AI opponent " + wanted);
                portrait = null;
            }
            AiOpponent template = borrowed ?? c.DefaultAi;
            AiOpponent ai = new AiOpponent { ResourceId = Rid(path + "#ai"), ContentPackIndex = 0, TitleRef = Ref(UniqueResourceId.GetInvalid().Id) };
            string opponentName = mode.OpponentName ?? (borrowed != null ? LocalizedOrNull(borrowed.DisplayNameKey.RawKey) : null) ?? eventName;
            ai.DisplayNameKey = T("mod.story." + path + ".opponent", opponentName);
            ai.Image = Img(c, portrait, template == null ? null : template.Image);
            c.Config.AiOpponents.RemoveAll(delegate(AiOpponent x) { return x.ResourceId.Id == ai.ResourceId.Id; });
            c.Config.AiOpponents.Add(ai);
            s.AiOpponent = Ref(ai.ResourceId.Id);
            // Briefing before the match (the client's MatchIntroModal): text and an optional image.
            s.IntroModalData = new MatchIntroModalData();
            string briefing = S(bd, "briefing", null);
            if (!string.IsNullOrEmpty(briefing))
            {
                s.IntroModalData.OverviewSection = new MatchIntroModalSectionData { TextKey = T("mod.story." + path + ".briefing", briefing), Image = Img(c, S(bd, "briefing_image", null), ai.Image), IsImageOnLeft = true };
            }
            else s.IntroModalData.OverviewSection = new MatchIntroModalSectionData { TextKey = None() };
            // Lines the opponent says when beaten (MatchDialogueData, shown by SequenceGameEnd on a win).
            List<Dictionary<string, fsData>> lines = L(bd, "victory_lines");
            for (int i = 0; i < lines.Count; i++)
            {
                MatchDialogueData line = new MatchDialogueData { ResourceId = Rid(path + "#victory" + i), Production = true, ContentPackIndex = 0 };
                line.SpeakerSourceType = default(ScenarioEmoteSourceType);
                line.SpeakerNameKey = T("mod.story." + path + ".victory" + i + ".speaker", S(lines[i], "speaker", opponentName));
                line.DialogueTextKey = T("mod.story." + path + ".victory" + i, S(lines[i], "text", ""));
                line.PortraitImage = Img(c, S(lines[i], "portrait", null), ai.Image);
                line.DialogueAudio = null;
                c.Config.MatchDialogues.RemoveAll(delegate(MatchDialogueData x) { return x.ResourceId.Id == line.ResourceId.Id; });
                c.Config.MatchDialogues.Add(line);
                s.MatchEndDialogues.Add(Ref(line.ResourceId.Id));
            }
            s.MatchEndDialogueAction = CardFxAction.CountOf;
            c.Config.Scenarios.RemoveAll(delegate(Scenario x) { return x.ResourceId.Id == s.ResourceId.Id; });
            c.Config.Scenarios.Add(s);
            Battle battle = new Battle { Mode = mode, ScenarioName = s.ResourceId.UniqueName, ScenarioId = s.ResourceId.Id, EventId = eventId };
            battle.FixedDeck = mode.Player.Deck != null && mode.Player.Deck != "own" && mode.Player.Deck != "practice";
            if (battle.FixedDeck) fixedDeckScenarios.Add(s.ResourceId.Id); else fixedDeckScenarios.Remove(s.ResourceId.Id);
            Battles[battle.ScenarioName] = battle;
            return s;
        }

        private static string LocalizedOrNull(string key)
        {
            if (string.IsNullOrEmpty(key) || LocalizationManager.Instance == null) return null;
            string text = LocalizationManager.Instance.GetText(key);
            return string.IsNullOrEmpty(text) || text == key ? null : text;
        }

        public static Battle FindBattle(string scenarioName)
        {
            Battle b;
            return scenarioName != null && Battles.TryGetValue(scenarioName, out b) ? b : null;
        }

        public static bool IsModChapter(int chapterId) { return ChapterIds.Contains(chapterId); }

        // ---- progress: mod chapters reveal events whose requirements are met (server side of the client's own checks) ----
        public static bool Fulfilled(List<ChapterEventOutcome> outcomes, bool mastery, SparkleServices.Campaign.IUserCampaignData user)
        {
            foreach (ChapterEventOutcome o in outcomes)
            {
                SparkleServices.Campaign.IUserActData act = Array.Find(user.actData, delegate(SparkleServices.Campaign.IUserActData x) { return x.actId == o.Act.resourceId && x.isMastery == mastery; });
                if (act == null) return false;
                SparkleServices.Campaign.IUserChapterData chapter = Array.Find(act.chapterData, delegate(SparkleServices.Campaign.IUserChapterData x) { return x.chapterId == o.Chapter.resourceId; });
                if (chapter == null) return false;
                SparkleServices.Campaign.IUserChapterEventData e = Array.Find(chapter.chapterEventData, delegate(SparkleServices.Campaign.IUserChapterEventData x) { return x.chapterEventId == o.ChapterEvent.resourceId; });
                if (e == null) return false;
                if (o.ModalType == DialogueModalType.None) { if (!e.isComplete) return false; continue; }
                SparkleServices.Campaign.IUserCampaignChoice made = o.ModalType == DialogueModalType.Intro ? e.introChoice : e.endingChoice;
                int index = made == SparkleServices.Campaign.IUserCampaignChoice.First ? 0 : made == SparkleServices.Campaign.IUserCampaignChoice.Second ? 1
                    : made == SparkleServices.Campaign.IUserCampaignChoice.Third ? 2 : -1;
                if (index < 0 || index != o.ModalChoiceIndex) return false;
            }
            return true;
        }

        // Adds the revealed events of every mod chapter the player has. Returns how many were added.
        public static int RevealEvents(Campaign source, SparkleServices.Campaign.IUserCampaignData user, bool mastery)
        {
            if (source == null || user == null || !CampaignIds.Contains(source.ResourceId.Id)) return 0;
            int added = 0;
            foreach (Act act in source.Acts)
            {
                SparkleServices.Campaign.IUserActData userAct = Array.Find(user.actData, delegate(SparkleServices.Campaign.IUserActData x) { return x.actId == act.ResourceId.Id && x.isMastery == mastery; });
                if (userAct == null) continue;
                foreach (Chapter chapter in act.Chapters)
                {
                    SparkleServices.Campaign.IUserChapterData userChapter = Array.Find(userAct.chapterData, delegate(SparkleServices.Campaign.IUserChapterData x) { return x.chapterId == chapter.ResourceId.Id; });
                    if (userChapter == null) continue;
                    List<SparkleServices.Campaign.IUserChapterEventData> events = new List<SparkleServices.Campaign.IUserChapterEventData>(userChapter.chapterEventData);
                    foreach (ChapterEvent e in chapter.ChapterEvents)
                    {
                        if (events.Exists(delegate(SparkleServices.Campaign.IUserChapterEventData x) { return x.chapterEventId == e.ResourceId.Id; })) continue;
                        if (!Fulfilled(e.RequiredOutcomes, mastery, user)) continue;
                        events.Add(new SparkleServices.Campaign.IUserChapterEventData { chapterEventId = e.ResourceId.Id, chapterId = chapter.ResourceId.Id });
                        added++;
                    }
                    userChapter.chapterEventData = events.ToArray();
                }
            }
            return added;
        }

        // ---- client hooks ----------------------------------------------------------------------------------------
        // The picker shows the campaigns named in its own campaignOrder list; mod stories are appended to it.
        public static void PickerAwakePostfix(StoryPickerWin __instance)
        {
            List<string> order = AccessTools.Field(typeof(StoryPickerWin), "campaignOrder").GetValue(__instance) as List<string>;
            if (order == null) return;
            foreach (string key in NameKeys) if (!order.Contains(key)) order.Add(key);
        }

        // The episode screen has a fixed set of act rows and episode slots (from the template's layout); rows and slots a
        // story does not use keep the template's placeholders, so they are hidden (shown again for other stories).
        public static int EpisodeRowCapacity, EpisodeSlotCapacity;
        private static bool layoutDumped;
        // One-off layout dump (names, layout components, rect positions/sizes) to extend the episode screen safely.
        private static string Describe(Transform t, int depth, int max)
        {
            if (t == null || depth > max) return "";
            RectTransform r = t as RectTransform;
            string line = new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " (off)");
            foreach (Component c in t.GetComponents<Component>()) if (c is UnityEngine.UI.LayoutGroup || c is UnityEngine.UI.ContentSizeFitter || c is UnityEngine.UI.LayoutElement) line += " [" + c.GetType().Name + "]";
            if (r != null) line += " pos=" + r.anchoredPosition + " size=" + r.sizeDelta + " anchor=" + r.anchorMin + "-" + r.anchorMax + " scale=" + r.localScale;
            string text = line + "\n";
            foreach (Transform child in t) text += Describe(child, depth + 1, max);
            return text;
        }
        public static void EpisodePanelPostfix(EpisodeSelectPanel __instance)
        {
            EpisodeSetup(__instance, false);
        }

        private static readonly Dictionary<int, int> originalRows = new Dictionary<int, int>(), originalSlots = new Dictionary<int, int>();
        private static readonly Dictionary<int, Vector3> originalScale = new Dictionary<int, Vector3>();

        // Before UpdateUi: a mod story with more acts or episodes than the template's layout gets extra rows/slots, cloned
        // from the template's own (UpdateUi otherwise fills only the existing ones and throws on a missing slot while it
        // links gamepad navigation, leaving the remaining rows unset).
        public static void EpisodePanelPrefix(EpisodeSelectPanel __instance)
        {
            EpisodeSetup(__instance, true);
        }

        private static void EpisodeSetup(EpisodeSelectPanel panel, bool before)
        {
            int campaignId = (int)AccessTools.Field(typeof(EpisodeSelectPanel), "_campaignId").GetValue(panel);
            List<EpisodeSelectMapElement> rows = AccessTools.Field(typeof(EpisodeSelectPanel), "_mapElements").GetValue(panel) as List<EpisodeSelectMapElement>;
            if (rows == null || rows.Count == 0) return;
            Campaign campaign = GameManager.Instance.Config.Campaigns.Find(delegate(Campaign x) { return x.ResourceId.Id == campaignId; });
            bool mod = campaign != null && CampaignIds.Contains(campaignId);
            FieldInfo buttonsField = AccessTools.Field(typeof(EpisodeSelectMapElement), "_episodeButtons");
            int key = panel.GetInstanceID();
            if (!originalRows.ContainsKey(key))
            {
                originalRows[key] = rows.Count;
                List<EpisodeSelectButton> first = buttonsField.GetValue(rows[0]) as List<EpisodeSelectButton>;
                originalSlots[key] = first == null ? 0 : first.Count;
                originalScale[key] = rows[0].transform.parent.localScale;
                Debug.Log("OFFLINE STORIES: episode layout " + rows.Count + " rows x " + originalSlots[key] + " slots\n" + Describe(rows[0].transform.parent, 0, 3));
            }
            int baseRows = originalRows[key], baseSlots = originalSlots[key];
            EpisodeRowCapacity = baseRows; EpisodeSlotCapacity = baseSlots;
            if (before)
            {
                if (!mod) return;
                MethodInfo pressed = AccessTools.Method(typeof(EpisodeSelectMapElement), "HandleEpisodeButtonPressed");
                while (rows.Count < campaign.Acts.Count)
                {
                    EpisodeSelectMapElement last = rows[rows.Count - 1];
                    GameObject go = UnityEngine.Object.Instantiate(last.gameObject, last.transform.parent);
                    EpisodeSelectMapElement row = go.GetComponent<EpisodeSelectMapElement>();
                    row.OnMapEpisodeSelected = rows[0].OnMapEpisodeSelected;
                    Place(go.transform, last.transform, rows.Count >= 2 ? rows[rows.Count - 2].transform : null, true);
                    rows.Add(row);
                }
                for (int r = 0; r < campaign.Acts.Count; r++)
                {
                    List<EpisodeSelectButton> slots = buttonsField.GetValue(rows[r]) as List<EpisodeSelectButton>;
                    if (slots == null || slots.Count == 0) continue;
                    while (slots.Count < campaign.Acts[r].Chapters.Count)
                    {
                        EpisodeSelectButton last = slots[slots.Count - 1];
                        GameObject go = UnityEngine.Object.Instantiate(last.gameObject, last.transform.parent);
                        EpisodeSelectButton slot = go.GetComponent<EpisodeSelectButton>();
                        slot.OnPressed = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), rows[r], pressed);
                        Place(go.transform, last.transform, slots.Count >= 2 ? slots[slots.Count - 2].transform : null, false);
                        slots.Add(slot);
                    }
                }
                return;
            }
            // After UpdateUi: rows and slots a story does not use are hidden; a bigger story is scaled to fit the screen.
            int maxSlots = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                bool used = mod ? r < campaign.Acts.Count : r < baseRows;
                rows[r].gameObject.SetActive(used);
                List<EpisodeSelectButton> slots = buttonsField.GetValue(rows[r]) as List<EpisodeSelectButton>;
                if (slots == null) continue;
                for (int s = 0; s < slots.Count; s++)
                {
                    bool show = used && (mod ? s < campaign.Acts[r].Chapters.Count : s < baseSlots);
                    slots[s].gameObject.SetActive(show);
                    if (show) maxSlots = Math.Max(maxSlots, s + 1);
                }
                int shown = 0;
                foreach (EpisodeSelectButton b in slots) if (b.gameObject.activeSelf) shown++;
                // a row with more episodes than the layout's slots shrinks its own episode strip to fit the row
                if (slots.Count > 0) FitStrip(slots, Math.Min(1f, baseSlots / (float)Math.Max(1, shown)));
            }
            int usedRows = mod ? campaign.Acts.Count : baseRows;
            float fit = Math.Min(1f, baseRows / (float)Math.Max(1, usedRows));
            rows[0].transform.parent.localScale = originalScale[key] * fit;
        }

        private static readonly Dictionary<int, Vector2> stripHome = new Dictionary<int, Vector2>();
        // Shrinks a row's episode strip and centres the shown episodes in it (a scaled layout overflows to the right).
        private static void FitStrip(List<EpisodeSelectButton> slots, float scale)
        {
            RectTransform strip = slots[0].transform.parent as RectTransform;
            if (strip == null) return;
            Vector2 home;
            if (!stripHome.TryGetValue(strip.GetInstanceID(), out home)) { home = strip.anchoredPosition; stripHome[strip.GetInstanceID()] = home; }
            strip.localScale = Vector3.one * scale;
            strip.anchoredPosition = home;
            if (scale >= 1f) return;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(strip);
            float min = float.MaxValue, max = float.MinValue;
            foreach (EpisodeSelectButton b in slots)
            {
                RectTransform r = b.transform as RectTransform;
                if (r == null || !b.gameObject.activeSelf) continue;
                float left = r.localPosition.x - r.rect.width * r.pivot.x;
                min = Math.Min(min, left); max = Math.Max(max, left + r.rect.width);
            }
            if (min > max) return;
            float offset = (min + max) / 2f - strip.rect.center.x;
            strip.anchoredPosition = home - new Vector2(offset * scale, 0f);
        }

        // A clone goes after the last item: layout groups place it themselves; otherwise it is offset by the spacing of the
        // last two items (or by the last item's own size: rows downwards, slots to the right).
        private static void Place(Transform clone, Transform last, Transform before, bool down)
        {
            clone.SetSiblingIndex(last.GetSiblingIndex() + 1);
            if (last.parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null) return;
            RectTransform c = clone as RectTransform, l = last as RectTransform, b = before as RectTransform;
            if (c == null || l == null) return;
            Vector2 step = b != null ? l.anchoredPosition - b.anchoredPosition : down ? new Vector2(0f, -l.rect.height) : new Vector2(l.rect.width, 0f);
            c.anchoredPosition = l.anchoredPosition + step;
        }

        // Chapter endings: the client counts an ending as seen only after its ending cinematic, so a mod chapter (no
        // cinematic) would show its ending dialogue again on every visit. For mod chapters the ending is new until
        // hasEndingBeenShown, which is recorded through the client's own call once the ending dialogue is finished.
        private static SparkleServices.Campaign.IUserChapterData UserChapter(int campaignId, int actId, int chapterId, bool mastery)
        {
            SparkleServices.ServicesManager services = Pixelplacement.Singleton<SparkleServices.ServicesManager>.Instance;
            SparkleServices.Campaign.IUserCampaignData user = services == null || services.Campaign == null || services.Campaign.CampaignDatas == null ? null
                : services.Campaign.CampaignDatas.Find(delegate(SparkleServices.Campaign.IUserCampaignData x) { return x.campaignId == campaignId; });
            if (user == null) return null;
            SparkleServices.Campaign.IUserActData act = Array.Find(user.actData, delegate(SparkleServices.Campaign.IUserActData x) { return x.actId == actId && x.isMastery == mastery; });
            return act == null ? null : Array.Find(act.chapterData, delegate(SparkleServices.Campaign.IUserChapterData x) { return x.chapterId == chapterId; });
        }

        public static void NewEndingPostfix(int campaignId, int actId, int chapterId, bool isMastery, ref bool __result)
        {
            if (!ChapterIds.Contains(chapterId)) return;
            SparkleServices.Campaign.IUserChapterData chapter = UserChapter(campaignId, actId, chapterId, isMastery);
            __result = chapter != null && !chapter.hasEndingBeenShown;
        }

        // StoryState opens the chapter ending with no hide callback; the popup's begin-hide delegate calls it, so closing
        // failed and the dialogue stayed open (originals hid it through their ending cinematic). A no-op keeps it closable.
        public static void ChapterEndingModalPrefix(ref UnityEngine.Events.UnityAction hideCallback)
        {
            if (hideCallback == null) hideCallback = delegate { };
            Debug.Log("OFFLINE STORIES: chapter ending opened at " + Time.realtimeSinceStartup.ToString("0.0") + " from " + new System.Diagnostics.StackTrace(2, false).GetFrame(0).GetMethod().Name);
        }

        public static int EndingsRecorded, EndingsClosed;
        public static void EndingModalCompletePostfix(global::StoryState __instance)
        {
            object info = AccessTools.Field(typeof(global::StoryState), "_currentChapterInfo").GetValue(__instance);
            Type t = info.GetType();
            int campaignId = (int)t.GetField("CampaignId").GetValue(info), actId = (int)t.GetField("ActId").GetValue(info), chapterId = (int)t.GetField("ChapterId").GetValue(info);
            bool mastery = (bool)AccessTools.Field(typeof(global::StoryState), "_isMastery").GetValue(__instance);
            if (!ChapterIds.Contains(chapterId)) return;
            SparkleServices.Campaign.IUserChapterData chapter = UserChapter(campaignId, actId, chapterId, mastery);
            // StoryMapWin.HideModal only hides its remembered popup; the previous (event ending) popup's hide callback clears
            // that reference after the chapter ending reuses the shared popup, so it is closed here directly.
            StoryMapWin map = AccessTools.Field(typeof(global::StoryState), "_storyMapWin").GetValue(__instance) as StoryMapWin;
            if (map != null && AccessTools.Field(typeof(StoryMapWin), "_visiblePopupInstance").GetValue(map) == null)
            {
                UIPopupWindow popup = Pixelplacement.Singleton<WinManager>.Instance.Popup;
                if (popup != null && popup.gameObject.activeInHierarchy) { popup.Hide(); EndingsClosed++; }
            }
            if (chapter == null || chapter.hasEndingBeenShown) return;
            EndingsRecorded++;
            CampaignService.OnChapterEndingShown(campaignId, actId, chapterId, UniqueResourceId.GetInvalid().Id, mastery, null, null);
        }

        // Many stories: the picker's grid shows two rows of tiles; with more, the grid moves into a vertical scroll area of
        // the same place and size (mouse wheel / drag), growing with its tiles. Two rows or fewer keep the original layout.
        public static int PickerTiles; public static bool PickerScrolls;
        public static void PickerShowPostfix(StoryPickerWin __instance)
        {
            GameObject container = AccessTools.Field(typeof(StoryPickerWin), "_storyPanelsContainer").GetValue(__instance) as GameObject;
            if (container == null) return;
            UnityEngine.UI.GridLayoutGroup grid = container.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            int tiles = 0;
            foreach (Transform t in container.transform) if (t.gameObject.activeSelf) tiles++;
            PickerTiles = tiles;
            int columns = grid != null && grid.constraint == UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount ? Math.Max(1, grid.constraintCount) : 4;
            if (tiles <= columns * 2 || container.transform.parent.GetComponent<UnityEngine.UI.ScrollRect>() != null) return;
            RectTransform content = container.transform as RectTransform;
            Debug.Log("OFFLINE STORIES: picker layout\n" + Describe(content.parent, 0, 1));
            Vector2 size = content.rect.size;
            GameObject view = new GameObject("OfflineStoryScroll", typeof(RectTransform));
            RectTransform v = view.GetComponent<RectTransform>();
            v.SetParent(content.parent, false);
            v.SetSiblingIndex(content.GetSiblingIndex());
            v.anchorMin = content.anchorMin; v.anchorMax = content.anchorMax; v.pivot = content.pivot;
            v.anchoredPosition = content.anchoredPosition; v.sizeDelta = content.sizeDelta; v.localScale = content.localScale;
            // the viewport takes the grid's place; tall enough for two rows plus a peek at the third
            if (grid != null) { float h = grid.padding.top + grid.cellSize.y * 2.35f + grid.spacing.y * 2f; if (v.rect.height < h) v.sizeDelta = new Vector2(v.sizeDelta.x, v.sizeDelta.y + (h - v.rect.height)); }
            // inside the picker's own layout group the viewport needs its own size (else it collapses to nothing)
            UnityEngine.UI.LayoutElement element = view.AddComponent<UnityEngine.UI.LayoutElement>();
            element.preferredWidth = size.x; element.minWidth = size.x;
            float viewHeight = grid != null ? grid.padding.top + grid.cellSize.y * 2.35f + grid.spacing.y * 2f : size.y;
            element.preferredHeight = viewHeight; element.minHeight = viewHeight;
            UnityEngine.UI.Image hit = view.AddComponent<UnityEngine.UI.Image>(); hit.color = new Color(0f, 0f, 0f, 0.001f);   // receives wheel/drag
            view.AddComponent<UnityEngine.UI.RectMask2D>();
            content.SetParent(v, true);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0f, content.sizeDelta.y); content.localScale = Vector3.one;
            UnityEngine.UI.ContentSizeFitter fit = container.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            UnityEngine.UI.ScrollRect scroll = view.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.content = content; scroll.viewport = v; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1f;
            PickerScrolls = true;
            Debug.Log("OFFLINE STORIES: story picker scrolls (" + tiles + " stories, " + columns + " per row)");
        }

        // A battle with a fixed player deck skips the deck picker (client) and the chosen deck (server).
        public static void DeckSelectionPostfix(Scenario __instance, ref bool __result)
        {
            if (__result && __instance != null && fixedDeckScenarios.Contains(__instance.ResourceId.Id)) __result = false;
        }

        private static GameObject voiceStub;
        private static AudioSource voiceSource;
        // A mod line's voice: played here on its own source (stopping the previous line); the client gets an empty audio
        // creator, so its own MasterAudio.PlaySound finds nothing and stays silent.
        public static bool DialogueAudioPrefix(string asset, uint contentPackIndex, ref AssetBundleLoadAssetOperation __result)
        {
            string file;
            if (asset == null || !asset.StartsWith(VoicePrefix) || !voices.TryGetValue(asset, out file)) return true;
            if (voiceStub == null) { voiceStub = new GameObject("OfflineStoryVoiceStub"); voiceStub.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(voiceStub); }
            AudioClip clip = OfflineAudio.ClipFor(file);
            if (clip != null && OfflineAudio.Host != null)
            {
                if (voiceSource == null) { voiceSource = OfflineAudio.Host.gameObject.AddComponent<AudioSource>(); voiceSource.playOnAwake = false; voiceSource.spatialBlend = 0f; }
                voiceSource.Stop(); voiceSource.clip = clip; voiceSource.volume = OfflineAudio.SfxVolume(); voiceSource.Play();
                VoicesPlayed++; LastVoice = asset;
                Debug.Log("OFFLINE STORIES: voice " + asset + " (" + clip.length.ToString("0.0") + " s)");
            }
            else Debug.LogWarning("OFFLINE STORIES: voice not ready " + asset);
            __result = new AssetBundleLoadAssetOperationSimulation(voiceStub);
            return false;
        }
    }
}
