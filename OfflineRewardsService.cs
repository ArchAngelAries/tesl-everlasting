using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Local Rewards service. Story rewards use the original presentation: a Campaign-source
    // RewardDescription whose passes are StoryScroll containers (one per bundle key in
    // ChapterEvent.RewardsBundleKey); opening one posts rewards/acceptReward, and the
    // response's items ("card" typeId=card id, count) and currencies are shown by the
    // client's RewardsWin and applied to the player state here.
    // Bundle contents were server-side only. They are reconstructed from UESP
    // Legends:Story/Rewards (research notes: forgotten-hero-rewards.md); the
    // split of a chapter's list across its bundle keys is an assumption.
    public static class OfflineRewardsService
    {
        // bundleKey -> entries: "card:Name*count", "gold:n", "gems:n", "ticket:n",
        // "deck:Name", "pack:Name*count", "title:Name" (deck/pack/title await their stores).
        private static readonly Dictionary<string, string[]> Bundles = new Dictionary<string, string[]> {
            { "onboarding_game_1_bundle", new[] { "card:Fiery Imp*2" } },
            { "onboarding_game_1_bundle_2", new[] { "card:Valenwood Huntsman*2" } },
            { "onboarding_game_1_bundle_3", new[] { "card:Crushing Blow*1" } },
            { "onboarding_game_2_bundle", new[] { "card:Kvatch Soldier*1" } },
            { "onboarding_game_2_bundle_2", new[] { "card:Grahtwood Ambusher*1" } },
            { "onboarding_game_2_bundle_3", new[] { "card:Divine Fervor*1" } },
            { "onboarding_game_3_bundle_ticket", new[] { "ticket:1" } },
            { "onboarding_game_3_bundle_gold", new[] { "gold:100" } },
            { "onboarding_game_4_steel_scimitar_bundle", new[] { "card:Steel Scimitar*1" } },
            { "onboarding_game_4_execute_bundle", new[] { "card:Execute*1" } },
            { "deck_random_skyrim", new[] { "deck:Band of Survivors", "deck:random Heroes of Skyrim" } },
            { "onboarding_game_5_bundle", new[] { "card:Legion Shield*1", "card:Senche-Tiger*1", "card:Bruma Profiteer*1" } },
            { "onboarding_game_6_bundle", new[] { "card:Piercing Javelin*1" } },
            { "onboarding_game_6_snow_wolf_bundle", new[] { "card:Snow Wolf*1" } },
            { "onboarding_game_6_cast_out_bundle", new[] { "card:Cast Out*1" } },
            { "onboarding_game_7_bundle", new[] { "card:Battlerage Orc*1", "card:Fireball*1", "card:Bog Lurcher*1" } },
            { "onboarding_game_8_bundle_A", new[] { "deck:Black Marsh Smugglers" } },
            { "onboarding_game_8_bundle_B", new[] { "gold:10" } },
            { "onboarding_game_8_bundle_title", new[] { "title:The Smuggler" } },
            { "onboarding_game_9_bundle_A", new[] { "card:Lurking Mummy*1", "card:Swamp Leviathan*1", "card:Suppress*1" } },
            { "onboarding_game_9_bundle_B", new[] { "gold:10" } },
            { "onboarding_game_10_bundle_A", new[] { "card:Voracious Spriggan*1", "card:Finish Off*1", "card:Daring Cutpurse*1" } },
            { "onboarding_game_10_bundle_B", new[] { "gold:5" } },
            { "onboarding_game_10_green_touched_spriggan_bundle", new[] { "card:Green-Touched Spriggan*1" } },
            { "onboarding_game_10_hackwing_feather_bundle", new[] { "card:Hackwing Feather*1" } },
            { "onboarding_game_11_bundle_A", new[] { "deck:Dunmer Avengers" } },
            { "onboarding_game_11_bundle_B", new[] { "gold:10" } },
            { "onboarding_game_12_bundle_A", new[] { "card:Breton Conjurer*1", "card:Silvenar Tracker*1", "card:Cave Grahl*1" } },
            { "onboarding_game_12_shimmerene_peddler_bundle", new[] { "card:Shimmerene Peddler*1" } },
            { "onboarding_game_12_gold_bundle", new[] { "gold:50" } },
            { "onboarding_game_13_bundle_A", new[] { "pack:Core Set*1" } },
            { "onboarding_game_13_bundle_B", new[] { "card:Lightning Bolt*1", "card:Shrieking Harpy*1", "card:Tome of Alteration*1" } },
            { "onboarding_game_14_bundle_A", new[] { "deck:Imperial Might" } },
            { "onboarding_game_14_bundle_B", new[] { "gold:50" } },
            { "onboarding_game_15_bundle_A", new[] { "gems:50" } },
            { "onboarding_game_16_bundle_A", new[] { "card:Shadowfen Priest*1" } },
            { "onboarding_game_16_bundle_B", new[] { "gems:25" } },
            { "onboarding_game_16_haunting_spirit_bundle", new[] { "card:Haunting Spirit*1" } },
            { "onboarding_game_16_plea_to_kynareth_bundle", new[] { "card:Plea to Kynareth*1" } },
            { "onboarding_interlude_tyr", new[] { "card:Tyr*1" } },
            { "onboarding_interlude_orb", new[] { "card:Orb of Vaermina*1" } },
            { "onboarding_game_17_bundle_A", new[] { "pack:Core Set*1" } },
            { "onboarding_game_17_bundle_B", new[] { "card:Goldbrand*1" } },
            { "onboarding_game_17_bundle_C", new[] { "gems:25" } },
            { "onboarding_game_18_bundle_A", new[] { "card:Rapid Shot*1", "card:Leaflurker*1" } },
            { "onboarding_game_18_bundle_B", new[] { "gems:100" } },
            { "onboarding_game_19_bundle_A", new[] { "card:Mantikora*1", "card:Snowy Sabre Cat*1" } },
            { "onboarding_game_20_bundle_A", new[] { "deck:Warriors of Hammerfell" } },
            { "onboarding_game_20_bundle_B", new[] { "gems:250" } },
            { "onboarding_game_20_bundle_C", new[] { "pack:Core Set*1" } },
            { "onboarding_game_20_bundle_title", new[] { "title:The Savior of Cyrodiil" } },
        };

        // The five Heroes of Skyrim theme decks installed in DeckInfo.
        private static readonly string[] HeroesOfSkyrim = { "Aela's Companions", "Alduin's Apocalypse", "Ancano's Cunning", "Brynjolf's Heist", "Paarthurnax's Roar" };
        private static readonly System.Random random = new System.Random();

        // Card names resolved to installed card ids on the main thread (config load).
        private static volatile Dictionary<string, int> cardIds = new Dictionary<string, int>();

        // Other stories and puzzles: data/story-reward-bundles.tsv (key<TAB>entry|entry, built by
        // tools/build_story_reward_bundles.py from installed bundle keys + UESP mission rewards).
        // Hand-written entries above take precedence.
        private static void LoadBundleData(string path)
        {
            if (!File.Exists(path)) { Debug.LogWarning("OFFLINE REWARDS: bundle data absent: " + path); return; }
            int loaded = 0;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 2 || parts[0] == "key" || Bundles.ContainsKey(parts[0])) continue;
                Bundles[parts[0]] = parts[1].Split('|'); loaded++;
            }
            Debug.Log("OFFLINE REWARDS: loaded " + loaded + " bundle definitions from " + path);
        }

        public static void PublishCards(SPTESL.GameConfiguration config, string reportPath)
        {
            LoadBundleData(OfflinePaths.DataNear(Path.GetDirectoryName(reportPath), "story-reward-bundles.tsv"));
            LoadBundleData(OfflinePaths.DataNear(Path.GetDirectoryName(reportPath), "level-rewards.tsv"));
            Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            StringBuilder report = new StringBuilder("bundle\tentry\tresolved\n");
            foreach (KeyValuePair<string, string[]> bundle in Bundles)
                foreach (string entry in bundle.Value)
                {
                    string resolved = "pending store";
                    if (entry.StartsWith("card:", StringComparison.Ordinal))
                    {
                        string name = CardName(entry);
                        // Prefer the collectible printing (production, has a deck export code).
                        SPTESL.Card card = null, fallback = null;
                        foreach (SPTESL.CardSet set in config.CardCollection)
                            foreach (SPTESL.Card c in set.Cards)
                            {
                                if (c == null || c.Name != name) continue;
                                if (fallback == null) fallback = c;
                                if (card == null && c.Production && !string.IsNullOrEmpty(c.ExportCode)) card = c;
                            }
                        if (card == null) card = fallback;
                        resolved = card == null ? "MISSING" : card.ResourceId.Id.ToString();
                        if (card != null) ids[name] = card.ResourceId.Id;
                    }
                    else if (!entry.StartsWith("deck:", StringComparison.Ordinal) && !entry.StartsWith("pack:", StringComparison.Ordinal)
                        && !entry.StartsWith("title:", StringComparison.Ordinal)) resolved = "currency";
                    report.Append(bundle.Key).Append('\t').Append(entry).Append('\t').AppendLine(resolved);
                }
            cardIds = ids;
            File.WriteAllText(reportPath, report.ToString());
        }

        private static string CardName(string entry)
        {
            string body = entry.Substring(entry.IndexOf(':') + 1);
            int star = body.LastIndexOf('*');
            return star < 0 ? body : body.Substring(0, star);
        }

        private static int Count(string entry)
        {
            int star = entry.LastIndexOf('*'); int value;
            return star >= 0 && int.TryParse(entry.Substring(star + 1), out value) ? value : 1;
        }

        public static void Register(OfflineLocalServices services, OfflinePlayerState state)
        {
            services.Register("rewards/list", delegate {
                Rewards.ListRewardsResponse response = new Rewards.ListRewardsResponse();
                response.resultCode = ResultCode.Ok;
                response.rewards = state.Snapshot().rewards;
                return JsonUtility.ToJson(response); });
            services.Register("rewards/acceptReward", delegate(string m, string p, string body) {
                Rewards.AcceptRewardRequest request = JsonUtility.FromJson<Rewards.AcceptRewardRequest>(body);
                List<Rewards.AcceptRewardResponse.AcceptRewardResult> results = new List<Rewards.AcceptRewardResponse.AcceptRewardResult>();
                if (request != null && request.rewards != null)
                    state.Mutate(delegate(OfflinePlayerStateData d) {
                        foreach (Rewards.RewardId id in request.rewards)
                        {
                            Rewards.RewardDescription reward = d.rewards.Find(delegate(Rewards.RewardDescription r) { return r.rewardId == id.rewardId; });
                            Rewards.RewardPassDescription pass = reward == null ? null : reward.rewardPasses.Find(delegate(Rewards.RewardPassDescription x) { return x.rewardPassIndex == id.passIndex; });
                            if (pass == null || pass.claimed) continue;
                            pass.claimed = true;
                            results.Add(Apply(d, pass));
                        }
                        d.rewards.RemoveAll(delegate(Rewards.RewardDescription r) { return r.rewardPasses.TrueForAll(delegate(Rewards.RewardPassDescription x) { return x.claimed; }); });
                        OfflineArenaService.AfterRewardsClaimed(d);
                    });
                Rewards.AcceptRewardResponse response = new Rewards.AcceptRewardResponse();
                response.resultCode = ResultCode.Ok;
                response.results = results.ToArray();
                return JsonUtility.ToJson(response); });
        }

        // A story dialogue choice's reward: the chosen option's CardChoice (one copy) or GoldChoice.
        public static Rewards.RewardDescription GrantChoice(OfflinePlayerStateData d, SPTESL.ChapterEvent source, Campaign.IDialogModalType modal, Campaign.IUserCampaignChoice choice, bool mastery)
        {
            if (source == null) return null;
            SPTESL.StoryDialogueModal dialogue = modal == Campaign.IDialogModalType.Intro ? source.IntroModal : modal == Campaign.IDialogModalType.Ending ? source.EndingModal : null;
            if (dialogue == null) return null;
            string marker = source.ResourceId.Id + ":" + modal + (mastery ? ":master" : "");
            if (d.grantedChoices.Contains(marker)) return null;
            string bundle = null;
            if (modal == Campaign.IDialogModalType.Intro)
            {
                if (choice == Campaign.IUserCampaignChoice.First) bundle = mastery ? source.IntroFirstChoiceMasterRewardsBundleKey : source.IntroFirstChoiceRewardsBundleKey;
                else if (choice == Campaign.IUserCampaignChoice.Second) bundle = mastery ? source.IntroSecondChoiceMasterRewardsBundleKey : source.IntroSecondChoiceRewardsBundleKey;
            }
            if (!string.IsNullOrEmpty(bundle))
            {
                Rewards.RewardDescription reward = new Rewards.RewardDescription();
                reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.Campaign;
                reward.rewardPasses = new List<Rewards.RewardPassDescription>();
                foreach (string raw in bundle.Split(','))
                {
                    string key = raw.Trim(); if (key.Length == 0) continue;
                    reward.rewardPasses.Add(new Rewards.RewardPassDescription { rewardPassIndex = reward.rewardPasses.Count,
                        bundleKey = key, bundleCount = 1, rarity = Rewards.RewardRarity.Common, rewardContainerType = Rewards.RewardContainer.None });
                }
                if (reward.rewardPasses.Count > 0) { d.rewards.Add(reward); d.grantedChoices.Add(marker); return reward; }
            }
            foreach (SPTESL.StoryDialogueData data in dialogue.DialogueDataEvents)
            {
                SPTESL.GatedStoryDialogueOption option = choice == Campaign.IUserCampaignChoice.First ? data.Option1
                    : choice == Campaign.IUserCampaignChoice.Second ? data.Option2 : choice == Campaign.IUserCampaignChoice.Third ? data.Option3 : null;
                if (option == null) continue;
                string key = null;
                if (option.CardChoice != null && option.CardChoice.resourceId != 0 && option.CardChoice.resourceId != SPTESL.UniqueResourceId.GetInvalid().Id)
                    key = ChoiceCardPrefix + option.CardChoice.resourceId;
                else if (option.GoldChoice > 0) key = ChoiceGoldPrefix + option.GoldChoice;
                if (key == null) continue;
                Rewards.RewardDescription reward = new Rewards.RewardDescription();
                reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.Campaign;
                reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                    rewardPassIndex = 0, bundleKey = key, bundleCount = 1, rarity = Rewards.RewardRarity.Common,
                    rewardContainerType = Rewards.RewardContainer.None } };
                d.rewards.Add(reward);
                d.grantedChoices.Add(marker);
                return reward;
            }
            return null;
        }

        private const string ChoiceCardPrefix = "story_choice_card_", ChoiceGoldPrefix = "story_choice_gold_";

        // Grant a chapter event's bundles once (story replays never re-grant).
        public static bool GrantEvent(OfflinePlayerStateData d, int chapterEventId, bool mastery)
        {
            List<int> granted = mastery ? d.grantedMasterEvents : d.grantedEvents;
            if (granted.Contains(chapterEventId)) return false;
            SPTESL.ChapterEvent source = OfflineCampaignService.FindContentEvent(chapterEventId);
            string bundleKeys = source == null ? null : mastery ? source.MasterRewardsBundleKey : source.RewardsBundleKey;
            if (string.IsNullOrEmpty(bundleKeys)) return false;
            Rewards.RewardDescription reward = new Rewards.RewardDescription();
            reward.rewardId = ++d.nextRewardId;
            reward.source = Rewards.RewardSource.Campaign;
            reward.rewardPasses = new List<Rewards.RewardPassDescription>();
            foreach (string raw in bundleKeys.Split(','))
            {
                string key = raw.Trim();
                if (key.Length == 0) continue;
                Rewards.RewardPassDescription pass = new Rewards.RewardPassDescription();
                pass.rewardPassIndex = reward.rewardPasses.Count;
                pass.bundleKey = key; pass.bundleCount = 1;
                pass.rarity = Rewards.RewardRarity.Common;
                pass.rewardContainerType = Rewards.RewardContainer.StoryScroll;
                reward.rewardPasses.Add(pass);
            }
            if (reward.rewardPasses.Count == 0) return false;
            d.rewards.Add(reward);
            granted.Add(chapterEventId);
            return true;
        }

        internal static Rewards.AcceptRewardResponse.AcceptRewardResult Apply(OfflinePlayerStateData d, Rewards.RewardPassDescription pass)
        {
            Rewards.AcceptRewardResponse.AcceptRewardResult result = new Rewards.AcceptRewardResponse.AcceptRewardResult();
            result.bundleCount = 1;
            result.items = new List<Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType>();
            string[] entries;
            string bundleKey = pass.bundleKey ?? "";
            int choiceValue;
            if (bundleKey.StartsWith(ChoiceCardPrefix, StringComparison.Ordinal) && int.TryParse(bundleKey.Substring(ChoiceCardPrefix.Length), out choiceValue))
            {
                d.AddCards(choiceValue, 1);
                Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                item.type = "card"; item.typeId = choiceValue; item.count = 1;
                result.items.Add(item);
                return result;
            }
            if (bundleKey.StartsWith(ChoiceGoldPrefix, StringComparison.Ordinal) && int.TryParse(bundleKey.Substring(ChoiceGoldPrefix.Length), out choiceValue))
            { d.gold += choiceValue; result.gold = choiceValue; return result; }
            // Solo Arena reward passes (OfflineArenaService): arena_gold_N, arena_gems_N, arena_pack.
            if (bundleKey.StartsWith("arena_gold_", StringComparison.Ordinal) && int.TryParse(bundleKey.Substring(11), out choiceValue)) { d.gold += choiceValue; result.gold = choiceValue; return result; }
            if (bundleKey.StartsWith("arena_gems_", StringComparison.Ordinal) && int.TryParse(bundleKey.Substring(11), out choiceValue)) { d.gems += choiceValue; result.gems = choiceValue; return result; }
            if (bundleKey.StartsWith(OfflineLevelService.XpPrefix, StringComparison.Ordinal) && int.TryParse(bundleKey.Substring(OfflineLevelService.XpPrefix.Length), out choiceValue))
            { OfflineLevelService.Credit(d, choiceValue); result.xp = choiceValue; return result; }
            if (bundleKey.StartsWith(OfflineRankedService.BattleCardPrefix, StringComparison.Ordinal) && int.TryParse(bundleKey.Substring(OfflineRankedService.BattleCardPrefix.Length), out choiceValue))
            {
                d.AddCards(choiceValue, 1);
                Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType card = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                card.type = "card"; card.typeId = choiceValue; card.count = 1; card.isPremium = false;
                result.items.Add(card); return result;
            }
            if (bundleKey.StartsWith(OfflineTitles.TitleBundlePrefix, StringComparison.Ordinal))
            {
                string titleKey = bundleKey.Substring(OfflineTitles.TitleBundlePrefix.Length);
                if (!d.ownedTitles.Contains(titleKey)) d.ownedTitles.Add(titleKey);
                OfflineTitles.AfterOwned(d, titleKey);
                Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType title = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                title.type = "inventory"; title.typeId = OfflineCosmetics.TitleId(titleKey); title.count = 1; title.description = titleKey;
                result.items.Add(title); return result;
            }
            if (bundleKey.StartsWith("quest_pack_", StringComparison.Ordinal))
            {
                string questPack = bundleKey.Substring(11);
                OfflinePackService.AddPackages(d, questPack, 1);
                Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType qp = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                qp.type = "bundle"; qp.typeId = MurmurHash3.Hash(questPack); qp.count = 1; qp.description = questPack;
                result.items.Add(qp); return result;
            }
            if (bundleKey == "arena_pack")
            {
                string arenaBundle = OfflinePackService.RewardBundle(OfflinePackService.CoreBundle);
                OfflinePackService.AddPackages(d, arenaBundle, 1);
                Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType pack = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                pack.type = "bundle"; pack.typeId = MurmurHash3.Hash(arenaBundle); pack.count = 1; pack.description = arenaBundle;
                result.items.Add(pack); return result;
            }
            if (!Bundles.TryGetValue(bundleKey, out entries) && (entries = EverlastingHost.Bundle(bundleKey)) == null)
            {
                Debug.LogWarning("OFFLINE REWARDS: no reconstructed contents for bundle " + pass.bundleKey);
                return result;
            }
            return ApplyEntries(d, entries, result);
        }

        // Bundle entries (the reward language extensions also use through EverlastingHost).
        internal static Rewards.AcceptRewardResponse.AcceptRewardResult ApplyEntries(OfflinePlayerStateData d, string[] entries)
        {
            Rewards.AcceptRewardResponse.AcceptRewardResult result = new Rewards.AcceptRewardResponse.AcceptRewardResult();
            result.bundleCount = 1;
            result.items = new List<Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType>();
            return ApplyEntries(d, entries, result);
        }

        private static Rewards.AcceptRewardResponse.AcceptRewardResult ApplyEntries(OfflinePlayerStateData d, string[] entries, Rewards.AcceptRewardResponse.AcceptRewardResult result)
        {
            foreach (string entry in entries)
            {
                int n; string value = entry.Substring(entry.IndexOf(':') + 1);
                if (value == "random Heroes of Skyrim") value = HeroesOfSkyrim[random.Next(HeroesOfSkyrim.Length)];
                if (entry.StartsWith("premiumcardid:", StringComparison.Ordinal) && int.TryParse(CardName(entry), out n))
                {
                    int count = Count(entry); d.AddCards(n, 0, count);
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "card"; item.typeId = n; item.count = count; item.isPremium = true;
                    result.items.Add(item);
                }
                else if (entry.StartsWith("cardback:", StringComparison.Ordinal) && OfflineCosmetics.AllCardBacks().Contains(value))
                {
                    OfflineCosmetics.GrantCardBack(d, value);
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "inventory"; item.typeId = MurmurHash3.Hash(value); item.count = 1; item.description = value;
                    result.items.Add(item);
                }
                else if (entry.StartsWith("cardid:", StringComparison.Ordinal) && int.TryParse(CardName(entry), out n))
                {
                    int count = Count(entry); d.AddCards(n, count);
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "card"; item.typeId = n; item.count = count; item.isPremium = false;
                    result.items.Add(item);
                }
                else if (entry.StartsWith("card:", StringComparison.Ordinal))
                {
                    int id;
                    if (!cardIds.TryGetValue(CardName(entry), out id)) { Debug.LogWarning("OFFLINE REWARDS: unresolved card " + entry); continue; }
                    int count = Count(entry);
                    d.AddCards(id, count);
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "card"; item.typeId = id; item.count = count; item.isPremium = false; item.description = CardName(entry);
                    result.items.Add(item);
                }
                else if (entry.StartsWith("gold:", StringComparison.Ordinal) && int.TryParse(value, out n)) { d.gold += n; result.gold += n; }
                else if (entry.StartsWith("gems:", StringComparison.Ordinal) && int.TryParse(value, out n)) { d.gems += n; result.gems += n; }
                else if (entry.StartsWith("ticket:", StringComparison.Ordinal) && int.TryParse(value, out n)) { d.tickets += n; result.tickets += n; }
                else if (entry.StartsWith("xp:", StringComparison.Ordinal) && int.TryParse(value, out n)) { OfflineLevelService.Credit(d, n); result.xp += n; }
                else if (entry.StartsWith("corecard:", StringComparison.Ordinal))
                {
                    // Level rewards: "corecard:<Rarity>:race|random" (race = the selected avatar's race).
                    string[] spec = value.Split(':');
                    int rarity = Array.IndexOf(new string[] { "Common", "Rare", "Epic", "Legendary" }, spec[0]);
                    int id = OfflinePackService.CoreCard(d, rarity, spec.Length > 1 && spec[1] == "race" ? OfflineLevelService.AvatarRace(d) : SPTESL.CardSubtype.None);
                    if (id == 0) { Debug.LogWarning("OFFLINE REWARDS: no Core card for " + entry); continue; }
                    d.AddCards(id, 1);
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "card"; item.typeId = id; item.count = 1; item.isPremium = false;
                    result.items.Add(item);
                }
                else if (entry.StartsWith("deckonce:", StringComparison.Ordinal))
                {
                    // "if not received from story": skip a deck the player already has.
                    DeckInfo info = OfflineInventoryService.FindDeck(value);
                    bool owned = info != null && d.decks.Exists(delegate(Inventory.Deck x) { return x.deckTypeHash == info.TypeNameHash; });
                    if (!owned && OfflineInventoryService.GrantDeck(d, info))
                    {
                        Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                        item.type = "deck"; item.typeId = info.TypeNameHash; item.count = 1; item.description = value;
                        result.items.Add(item);
                    }
                }
                else if (entry.StartsWith("deck:", StringComparison.Ordinal) && OfflineInventoryService.GrantDeck(d, OfflineInventoryService.FindDeck(value)))
                {
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "deck"; item.typeId = OfflineInventoryService.FindDeck(value).TypeNameHash; item.count = 1; item.description = value;
                    result.items.Add(item);
                }
                else if (entry.StartsWith("pack:Core Set", StringComparison.Ordinal))
                {
                    int packs = Count(entry);
                    string levelBundle = OfflinePackService.RewardBundle(OfflinePackService.CoreBundle);
                    OfflinePackService.AddPackages(d, levelBundle, packs);
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "bundle"; item.typeId = MurmurHash3.Hash(levelBundle); item.count = packs; item.description = levelBundle;
                    result.items.Add(item);
                }
                else if (entry.StartsWith("pack:", StringComparison.Ordinal))
                {
                    // Set packs (e.g. puzzle rewards "pack:morrowind"): native bundle key cardpack_<set>.
                    string set = CardName(entry).ToLowerInvariant().Replace(" ", "");
                    string bundle = "cardpack_" + set;
                    int packs = Count(entry);
                    OfflinePackService.AddPackages(d, bundle, packs);
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "bundle"; item.typeId = MurmurHash3.Hash(bundle); item.count = packs; item.description = bundle;
                    result.items.Add(item);
                }
                else if (entry.StartsWith("title:", StringComparison.Ordinal) && OfflineCosmetics.GrantTitle(d, entry.Substring(6)))
                {
                    // RewardsWin shows titles and card backs from "inventory" items (TitleData id or card-back hash).
                    Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType item = new Rewards.AcceptRewardResponse.AcceptRewardResult.ItemType();
                    item.type = "inventory"; item.typeId = OfflineCosmetics.TitleId(OfflineCosmetics.TitleKey(entry.Substring(6))); item.count = 1; item.description = entry.Substring(6);
                    result.items.Add(item);
                }
                else { d.pendingGrants.Add(entry); Debug.LogWarning("OFFLINE REWARDS: recorded, store not restored yet: " + entry); }
            }
            return result;
        }
    }
}
