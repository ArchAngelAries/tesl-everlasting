using System;
using System.Collections.Generic;
using SparkleServices;
using SPTESL;

namespace OfflineEngine
{
    // GenericInventory categories "title" and "cardback" (PlayerProfileExtrasWin.TitleCategoryName_,
    // CollectionDeckBuilderWindow.CardBackCategoryName_). The client lists `available` then `locked` keys and
    // hides locked TitleData.Hidden titles itself.
    public static class OfflineCosmetics
    {
        public const string TitleCategory = "title", CardBackCategory = "cardback", DefaultCardBack = "cardback_default";

        // All 72 item keys from the client's own CardSharedGraphics._cardBackHashLookup (MurmurHash3 of these names).
        public static readonly string[] CardBacks = {
            "cardback_default", "cardback_houses_of_morrowind", "cardback_scale", "cardback_caius_training", "cardback_naryus_challenges",
            "cardback_divayth_fyrs_trials", "cardback_house_hlaalu", "cardback_house_redoran", "cardback_tribunal_temple", "cardback_house_telvanni",
            "cardback_house_dagoth", "cardback_nerevar_reborn", "cardback_legend", "cardback_clockwork_city", "cardback_dark_brotherhood",
            "cardback_elden_root", "cardback_flame_atronach", "cardback_storm_atronach", "cardback_e3_twitch_2018", "cardback_newclient",
            "cardback_madness_sale_premium_1", "cardback_madness_sale_premium_2", "cardback_prince_of_madness", "cardback_prince_of_madness_premium", "cardback_isle_of_madness_saint",
            "cardback_legend_premium", "cardback_legend_alt_premium", "cardback_elder_scrolls_25th_anniversary", "cardback_alliancewar_premium", "cardback_local_contender_premium",
            "cardback_AW_emperor", "cardback_AW_daggerfall", "cardback_AW_aldmeri", "cardback_AW_empire", "cardback_AW_guildsworn",
            "cardback_AW_ebonheart", "cardback_E3_2019", "cardback_elsweyr_preorder", "cardback_SageIntro", "cardback_SageCollector",
            "cardback_SageLich", "cardback_SageCrossPromo", "cardback_oblivion_preorder", "cardback_cadwell_normal", "cardback_cadwell_premium",
            "cardback_coldharbour_normal", "cardback_coldharbour_premium", "cardback_JawsIntro", "cardback_JawsCollector", "cardback_JawsSeptim",
            "cardback_EventsShiveringNormal", "cardback_EventsShiveringPremium", "cardback_EventsSteedNormal", "cardback_EventsSteedPremium", "cardback_EventsGrayFoxNormal",
            "cardback_EventsGrayFoxPremium", "cardback_EventsFightersNormal", "cardback_EventsFightersPremium", "cardback_EventsMagesNormal", "cardback_EventsMagesPremium",
            "cardback_EventsThievesNormal", "cardback_EventsThievesPremium", "cardback_FutureEvents1Regular", "cardback_FutureEvents1Premium", "cardback_FutureEvents2Regular",
            "cardback_FutureEvents2Premium", "cardback_FutureEvents3Regular", "cardback_FutureEvents3Premium", "cardback_FutureEvents4Regular", "cardback_FutureEvents4Premium",
            "cardback_FutureEvents5Regular", "cardback_FutureEvents5Premium" };

        private static volatile List<string> titleKeys = new List<string>();
        private static volatile Dictionary<string, string> titleByName = new Dictionary<string, string>();
        private static volatile Dictionary<string, int> titleIds = new Dictionary<string, int>();
        public static int TitleId(string key) { int id; return key != null && titleIds.TryGetValue(key, out id) ? id : 0; }

        // Main thread, after configuration and localization have loaded.
        public static void Publish(GameConfiguration config) { Publish(config, null); }

        public static void Publish(GameConfiguration config, string reportPath)
        {
            List<string> keys = new List<string>();
            Dictionary<string, string> names = new Dictionary<string, string>();
            Dictionary<string, int> ids = new Dictionary<string, int>();
            foreach (TitleData title in config.Titles)
            {
                if (title == null || string.IsNullOrEmpty(title.ResourceId.UniqueName)) continue;
                keys.Add(title.ResourceId.UniqueName); ids[title.ResourceId.UniqueName] = title.ResourceId.Id;
                string raw = title.Title.RawKey;
                string text = string.IsNullOrEmpty(raw) || LocalizationManager.Instance == null ? null : LocalizationManager.Instance.GetText(raw);
                if (!string.IsNullOrEmpty(text)) names[text.Trim().ToLowerInvariant()] = title.ResourceId.UniqueName;
                names[title.ResourceId.UniqueName.ToLowerInvariant()] = title.ResourceId.UniqueName;
            }
            titleKeys = keys; titleByName = names; titleIds = ids;
            if (reportPath != null)
            {
                System.Text.StringBuilder report = new System.Text.StringBuilder("key\tid\trarity\thidden\ttitle\tdescription\n");
                foreach (TitleData title in config.Titles)
                    if (title != null) report.Append(title.ResourceId.UniqueName).Append('\t').Append(title.ResourceId.Id).Append('\t').Append(title.Rarity).Append('\t').Append(title.Hidden)
                        .Append('\t').Append(Text(title.Title)).Append('\t').Append(Text(title.Description)).Append('\n');
                System.IO.File.WriteAllText(reportPath, report.ToString());
                System.Text.StringBuilder backs = new System.Text.StringBuilder("type\tasset\tname\tunlock\n");
                if (config.CardSharedGraphics != null)
                    foreach (CardBackData back in config.CardSharedGraphics.CardBackDataList)
                        if (back != null) backs.Append(back.Type).Append('\t').Append(back.VisualAsset).Append('\t').Append(Text(back.NameTextKey)).Append('\t').Append(Text(back.UnlockTextKey)).Append('\n');
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(reportPath), "native-cardbacks.tsv"), backs.ToString());
            }
            UnityEngine.Debug.Log("OFFLINE COSMETICS: " + keys.Count + " titles, " + CardBacks.Length + " card backs.");
        }

        private static string Text(LocalizedStringKey key)
        {
            string raw = key.RawKey;
            string text = string.IsNullOrEmpty(raw) || LocalizationManager.Instance == null ? null : LocalizationManager.Instance.GetText(raw);
            return (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Trim();
        }

        public static string TitleKey(string nameOrKey)
        {
            string key;
            return nameOrKey != null && titleByName.TryGetValue(nameOrKey.Trim().ToLowerInvariant(), out key) ? key : null;
        }

        // Grants waiting in pendingGrants from before the store existed ("title:Name").
        public static void ClaimPending(OfflinePlayerStateData d)
        {
            if (titleByName.Count == 0) return;
            List<string> claimed = new List<string>();
            d.pendingGrants.RemoveAll(delegate(string entry) {
                if (!entry.StartsWith("title:", StringComparison.Ordinal)) return false;
                string key = TitleKey(entry.Substring(6));
                if (key == null) return false;
                if (!d.ownedTitles.Contains(key)) d.ownedTitles.Add(key);
                claimed.Add(key);
                return true; });
            foreach (string key in claimed) OfflineTitles.AfterOwned(d, key);
        }

        public static bool GrantTitle(OfflinePlayerStateData d, string name)
        {
            string key = TitleKey(name);
            if (key == null) return false;
            if (!d.ownedTitles.Contains(key)) d.ownedTitles.Add(key);
            OfflineTitles.AfterOwned(d, key);
            return true;
        }

        // Completion card backs (installed CardBackData unlock texts): story campaigns and puzzle sets.
        public static void AfterCampaignProgress(OfflinePlayerStateData d, SPTESL.Campaign source, SparkleServices.Campaign.IUserCampaignData user)
        {
            bool all = source.Acts.Count > 0;
            foreach (SPTESL.Act act in source.Acts)
            {
                bool done = ActComplete(act, user);
                all &= done;
                if (!done || act.Chapters.Count == 0) continue;
                string first = act.Chapters[0].ResourceId.UniqueName;
                if (first.StartsWith("PUZ_Caius", StringComparison.Ordinal)) GrantCardBack(d, "cardback_caius_training");
                else if (first.StartsWith("PUZ_Naryu", StringComparison.Ordinal)) GrantCardBack(d, "cardback_naryus_challenges");
                else if (first.StartsWith("PUZ_Divayth", StringComparison.Ordinal)) GrantCardBack(d, "cardback_divayth_fyrs_trials");
                else if (first.StartsWith("PUZ_Bonus", StringComparison.Ordinal))
                {   // Skooma Cat's 20 puzzles: "Skooma Cat's Plaything" card back and "The Felonious" title.
                    GrantCardBack(d, "cardback_EventsShiveringNormal");
                    OfflineTitles.Grant(d, "title_2.15_TheFelonius");
                }
            }
            if (!all) return;
            if (source.ResourceId.UniqueName == "DBH") GrantCardBack(d, "cardback_dark_brotherhood");
            else if (source.ResourceId.UniqueName == "CWC") GrantCardBack(d, "cardback_clockwork_city");
        }

        private static bool ActComplete(SPTESL.Act act, SparkleServices.Campaign.IUserCampaignData user)
        {
            if (user.actData == null) return false;
            foreach (SparkleServices.Campaign.IUserActData userAct in user.actData)
            {
                if (userAct.actId != act.ResourceId.Id || userAct.isMastery || userAct.chapterData == null) continue;
                foreach (SPTESL.Chapter chapter in act.Chapters)
                {
                    SparkleServices.Campaign.IUserChapterData c = Array.Find(userAct.chapterData, delegate(SparkleServices.Campaign.IUserChapterData x) { return x.chapterId == chapter.ResourceId.Id; });
                    if (c == null || !c.isComplete) return false;
                }
                return true;
            }
            return false;
        }

        // Titles that also unlock a card back ("Win 30 games with this House/Alliance"; "Earned the ... title").
        private static readonly string[] TitleCardBacks = {
            "title_house_hlaalu_cousin", "cardback_house_hlaalu", "title_house_redoran_kinsman", "cardback_house_redoran",
            "title_house_telvanni_spellwright", "cardback_house_telvanni", "title_sixth_house_dagoth", "cardback_house_dagoth",
            "title_tribunal_temple_adept", "cardback_tribunal_temple", "title_AW_covenant_warmage", "cardback_AW_daggerfall",
            "title_AW_pact_guerilla", "cardback_AW_ebonheart", "title_AW_dominion_ambassador", "cardback_AW_aldmeri",
            "title_AW_guildsworn_steward", "cardback_AW_guildsworn", "title_AW_empire_bannerman", "cardback_AW_empire",
            "title_MoE_TheLich", "cardback_SageLich", "title_Jaws_AgentOfTheThrone", "cardback_JawsSeptim" };

        public static void AfterTitleOwned(OfflinePlayerStateData d, string titleKey)
        {
            for (int i = 0; i + 1 < TitleCardBacks.Length; i += 2)
                if (string.Equals(TitleCardBacks[i], titleKey, StringComparison.OrdinalIgnoreCase)) GrantCardBack(d, TitleCardBacks[i + 1]);
        }

        public static void GrantCardBack(OfflinePlayerStateData d, string key)
        {
            if ((Array.IndexOf(CardBacks, key) >= 0 || OfflineMods.IsModCardBack(key)) && !d.ownedCardBacks.Contains(key)) d.ownedCardBacks.Add(key);
        }

        // The 72 original keys plus every mod card back (OfflineMods), in that order.
        public static List<string> AllCardBacks()
        {
            List<string> all = new List<string>(CardBacks);
            all.AddRange(OfflineMods.CardBackKeys);
            return all;
        }

        public static List<GenericInventory.CategoryData> Categories(OfflinePlayerStateData d)
        {
            List<GenericInventory.CategoryData> list = new List<GenericInventory.CategoryData>();
            GenericInventory.CategoryData titles = Category(TitleCategory, d.GetSelected(TitleCategory) ?? "");
            foreach (string key in titleKeys) (d.ownedTitles.Contains(key) || OfflineTitles.IsStarting(key) ? titles.available : titles.locked).Add(key);
            list.Add(titles);
            GenericInventory.CategoryData backs = Category(CardBackCategory, d.GetSelected(CardBackCategory) ?? DefaultCardBack);
            foreach (string key in AllCardBacks()) (key == DefaultCardBack || d.ownedCardBacks.Contains(key) || OfflineMods.IsFreeCardBack(key) ? backs.available : backs.locked).Add(key);
            list.Add(backs);
            return list;
        }

        public static bool CanSelect(OfflinePlayerStateData d, string category, string key)
        {
            if (category == TitleCategory) return d.ownedTitles.Contains(key) || OfflineTitles.IsStarting(key);
            if (category == CardBackCategory) return key == DefaultCardBack || d.ownedCardBacks.Contains(key) || OfflineMods.IsFreeCardBack(key);
            return false;
        }

        private static GenericInventory.CategoryData Category(string name, string selected)
        {
            GenericInventory.CategoryData c = new GenericInventory.CategoryData();
            c.category = name; c.selected = selected;
            c.available = new List<string>(); c.locked = new List<string>(); c.descriptions = new List<string>();
            return c;
        }
    }
}
