using System;
using System.Collections.Generic;
using System.IO;
using Campaign = SparkleServices.Campaign;
using SPTESL;
namespace OfflineEngine
{
    public static class CampaignProgressTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test, string output)
        {
            test("story-rewards-arrive-in-each-storys-own-container", delegate {
                // GitHub issue 1: every story reward came in the story scroll. The client ships a book per later story.
                // Bundle contents load with the card index (at startup in the game); a bundle without contents gets no container.
                OfflineRewardsService.PublishCards(config, Path.Combine(output, "story-container-bundle-validation.tsv"));
                Dictionary<string, SparkleServices.Rewards.RewardContainer> want = new Dictionary<string, SparkleServices.Rewards.RewardContainer> {
                    { "TFH", SparkleServices.Rewards.RewardContainer.StoryScroll }, { "DBH", SparkleServices.Rewards.RewardContainer.DBBook },
                    { "CWC", SparkleServices.Rewards.RewardContainer.CWCBook }, { "IOM", SparkleServices.Rewards.RewardContainer.IOMBook } };
                foreach (KeyValuePair<string, SparkleServices.Rewards.RewardContainer> w in want)
                {
                    SPTESL.Campaign campaign = config.Campaigns.Find(delegate(SPTESL.Campaign c) { return c.ResourceId.UniqueName == w.Key; });
                    Require(campaign != null, "campaign " + w.Key + " missing");
                    ChapterEvent rewarded = null;
                    foreach (Act a in campaign.Acts) foreach (Chapter ch in a.Chapters) foreach (ChapterEvent e in ch.ChapterEvents)
                        if (rewarded == null && !string.IsNullOrEmpty(e.RewardsBundleKey)) rewarded = e;
                    Require(rewarded != null, w.Key + " has no rewarded event");
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    Require(OfflineRewardsService.GrantEvent(d, rewarded.ResourceId.Id, false), w.Key + " reward not granted");
                    SparkleServices.Rewards.RewardContainer got = d.rewards[d.rewards.Count - 1].rewardPasses[0].rewardContainerType;
                    Require(got == w.Value, w.Key + " rewards arrive in " + got + ", expected " + w.Value);
                }
            });
            test("story-scroll-screens-hold-only-scrolls-and-every-claim-is-answered", delegate {
                // GitHub issue 1: a reward screen holding a story scroll opens only scrolls (Rewards3d story mode), and a
                // claim with no answer left the scroll empty with Continue stuck.
                ChapterEvent rewarded = FirstRewarded(config, "TFH");
                Require(rewarded != null, "TFH has no rewarded event");
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                Require(OfflineRewardsService.GrantEvent(d, rewarded.ResourceId.Id, false), "TFH reward not granted");
                SparkleServices.Rewards.RewardDescription scroll = d.rewards[d.rewards.Count - 1];
                SparkleServices.Rewards.RewardDescription bag = new SparkleServices.Rewards.RewardDescription();
                bag.rewardId = ++d.nextRewardId; bag.source = SparkleServices.Rewards.RewardSource.Quest;
                bag.rewardPasses = new List<SparkleServices.Rewards.RewardPassDescription> { new SparkleServices.Rewards.RewardPassDescription {
                    rewardPassIndex = 0, bundleKey = "arena_gold_25", bundleCount = 1, rewardContainerType = SparkleServices.Rewards.RewardContainer.Bag } };
                d.rewards.Add(bag);
                List<SparkleServices.Rewards.RewardDescription> listed = OfflineRewardsService.Listed(d);
                Require(listed.Count == 1 && listed[0] == scroll, "a bag was listed beside a waiting story scroll (" + listed.Count + " listed)");
                List<SparkleServices.Rewards.RewardId> ids = new List<SparkleServices.Rewards.RewardId>();
                foreach (SparkleServices.Rewards.RewardPassDescription pass in scroll.rewardPasses)
                    ids.Add(new SparkleServices.Rewards.RewardId { rewardId = scroll.rewardId, passIndex = pass.rewardPassIndex });
                List<SparkleServices.Rewards.AcceptRewardResponse.AcceptRewardResult> first = OfflineRewardsService.Accept(d, ids.ToArray());
                Require(first.Count == ids.Count, "claims answered " + first.Count + " of " + ids.Count);
                Require(OfflineRewardsService.Listed(d).Exists(delegate(SparkleServices.Rewards.RewardDescription r) { return r.rewardId == bag.rewardId; }),
                    "the bag stayed hidden after the scrolls were opened");
                int gold = d.gold;
                List<SparkleServices.Rewards.AcceptRewardResponse.AcceptRewardResult> again = OfflineRewardsService.Accept(d, new SparkleServices.Rewards.RewardId[] {
                    ids[0], new SparkleServices.Rewards.RewardId { rewardId = 987654321, passIndex = 3 } });
                Require(again.Count == 2, "a repeated or unknown claim got no answer");
                Require(again[0] == first[0], "a repeated claim did not get its contents back");
                Require(d.gold == gold, "a repeated claim paid again");
            });
            test("story-rewards-keep-their-container-when-they-show-something", delegate {
                // Mod stories build prefixed keys (OfflineStories.Rewards); they must keep their scroll, not be claimed silently.
                OfflineRewardsService.PublishCards(config, Path.Combine(output, "shows-bundle-validation.tsv"));
                foreach (string key in new string[] { "story_choice_card_652289987", "story_choice_gold_10", "arena_gems_5", "quest_pack_cardpack_core",
                    "onboarding_game_1_bundle", "onboarding_game_3_bundle_gold", "deck_random_skyrim", "onboarding_game_8_bundle_title" })
                    Require(OfflineRewardsService.Shows(key), key + " would be claimed without being shown");
                foreach (string key in new string[] { "xp_50", "onboarding_game_8_bundle_quests", "onboarding_game_14_bundle_quests", "onboarding_game_20_bundle_quests", "", null })
                    Require(!OfflineRewardsService.Shows(key), (key ?? "null") + " would open an empty scroll");
            });
            test("rewards-saved-by-older-versions-never-open-an-empty-scroll", delegate {
                // A player upgrading from 1.0.4/1.0.5 with an unclaimed chapter reward saved as scrolls, including its quests-only pass.
                OfflineRewardsService.PublishCards(config, Path.Combine(output, "saved-scroll-bundle-validation.tsv"));
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                SparkleServices.Rewards.RewardDescription old = new SparkleServices.Rewards.RewardDescription();
                old.rewardId = ++d.nextRewardId; old.source = SparkleServices.Rewards.RewardSource.Campaign;
                old.rewardPasses = new List<SparkleServices.Rewards.RewardPassDescription>();
                string[] keys = { "onboarding_game_8_bundle_quests", "onboarding_game_8_bundle_title", "onboarding_game_3_bundle_gold" };
                for (int i = 0; i < keys.Length; i++)
                    old.rewardPasses.Add(new SparkleServices.Rewards.RewardPassDescription { rewardPassIndex = i, bundleKey = keys[i], bundleCount = 1,
                        rewardContainerType = SparkleServices.Rewards.RewardContainer.StoryScroll });
                d.rewards.Add(old);
                SparkleServices.Rewards.RewardDescription listed = OfflineRewardsService.Listed(d).Find(delegate(SparkleServices.Rewards.RewardDescription r) { return r.rewardId == old.rewardId; });
                Require(listed != null, "the saved chapter reward is no longer listed");
                Require(listed.rewardPasses[0].rewardContainerType == SparkleServices.Rewards.RewardContainer.None, "a saved quests-only pass would still open an empty scroll");
                Require(listed.rewardPasses[1].rewardContainerType == SparkleServices.Rewards.RewardContainer.StoryScroll
                    && listed.rewardPasses[2].rewardContainerType == SparkleServices.Rewards.RewardContainer.StoryScroll, "a saved pass with contents lost its scroll");
            });
            test("chapter-endings-announce-each-title-once", delegate {
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                SparkleServices.Rewards.RewardDescription title = new SparkleServices.Rewards.RewardDescription();
                title.rewardId = ++d.nextRewardId; title.source = SparkleServices.Rewards.RewardSource.Campaign;
                title.rewardPasses = new List<SparkleServices.Rewards.RewardPassDescription> { new SparkleServices.Rewards.RewardPassDescription {
                    rewardPassIndex = 0, bundleKey = OfflineTitles.TitleBundlePrefix + "test", bundleCount = 1, rewardContainerType = SparkleServices.Rewards.RewardContainer.Bag } };
                d.rewards.Add(title);
                Require(OfflineRewardsService.ChapterEndingRewards(d).Count == 1, "the title was not announced");
                Require(OfflineRewardsService.ChapterEndingRewards(d).Count == 0, "the same title was announced at the next chapter ending");
            });
            test("every-story-reward-container-shows-something", delegate {
                // GitHub issue 1: an empty scroll. Every original story and puzzle reward pass, Normal and Master, from a new
                // profile: a pass with a container must give something it can show; one that cannot is claimed silently.
                OfflineRewardsService.PublishCards(config, Path.Combine(output, "story-scroll-bundle-validation.tsv"));
                int checkedPasses = 0, tfh = 0;
                List<string> problems = new List<string>();
                foreach (SPTESL.Campaign campaign in config.Campaigns)
                    foreach (Act a in campaign.Acts) foreach (Chapter ch in a.Chapters) foreach (ChapterEvent e in ch.ChapterEvents)
                        foreach (bool master in new bool[] { false, true })
                        {
                            OfflinePlayerStateData d = new OfflinePlayerStateData();
                            if (!OfflineRewardsService.GrantEvent(d, e.ResourceId.Id, master)) continue;
                            SparkleServices.Rewards.RewardDescription r = d.rewards[d.rewards.Count - 1];
                            foreach (SparkleServices.Rewards.RewardPassDescription pass in r.rewardPasses)
                            {
                                if (pass.bundleKey.EndsWith("_quests", StringComparison.Ordinal) && pass.rewardContainerType != SparkleServices.Rewards.RewardContainer.None)
                                    problems.Add(pass.bundleKey + " would open an empty scroll");
                                if (pass.rewardContainerType == SparkleServices.Rewards.RewardContainer.None) continue;
                                SparkleServices.Rewards.AcceptRewardResponse.AcceptRewardResult got = OfflineRewardsService.Apply(d, pass);
                                if (!(got.gold > 0 || got.gems > 0 || got.tickets > 0 || (got.items != null && got.items.Count > 0)))
                                    problems.Add(campaign.ResourceId.UniqueName + (master ? " master " : " ") + pass.bundleKey + " opens empty");
                                checkedPasses++;
                                if (campaign.ResourceId.UniqueName == "TFH") tfh++;
                            }
                            if (d.pendingGrants.Count > 0) problems.Add(campaign.ResourceId.UniqueName + " reward not restored: " + string.Join(", ", d.pendingGrants.ToArray()));
                        }
                Require(problems.Count == 0, problems.Count + " problem(s): " + string.Join("; ", problems.ToArray()));
                Require(tfh > 20 && checkedPasses > 100, "only " + checkedPasses + " passes (" + tfh + " TFH) checked");
            });
            test("campaign-result-completes-only-matching-difficulty", delegate {
                Campaign.IUserChapterEventData normal = new Campaign.IUserChapterEventData { chapterEventId = 12345 };
                Campaign.IUserChapterEventData master = new Campaign.IUserChapterEventData { chapterEventId = 12345 };
                OfflinePlayerStateData state = new OfflinePlayerStateData();
                state.campaigns.Add(new Campaign.IUserCampaignData { campaignId = 987654321, actData = new Campaign.IUserActData[] {
                    Act(false, normal), Act(true, master) } });
                Require(OfflineCampaignService.CompleteEvent(state, 12345, true) && master.isComplete && !normal.isComplete,
                    "Master victory modified Normal progress or failed to mark Master complete.");
                normal.isComplete = false; master.isComplete = false;
                Require(OfflineCampaignService.CompleteEvent(state, 12345, false) && normal.isComplete && !master.isComplete,
                    "Normal victory modified Master progress or failed to mark Normal complete.");
                Require(!OfflineCampaignService.CompleteEvent(state, -12345, true), "An absent event was marked complete.");
            });
            test("campaign-completion-unlocks-separate-master-tree", delegate {
                List<SPTESL.Campaign> old = OfflineCampaignService.Content;
                try {
                    OfflineCampaignService.PublishContent(config.Campaigns);
                    SPTESL.Campaign source = config.Campaigns.Find(delegate(SPTESL.Campaign c) { return c.ResourceId.UniqueName == "DBH"; });
                    Require(source != null && source.Acts.Count > 1, "Installed Dark Brotherhood campaign missing.");
                    OfflinePlayerStateData state = new OfflinePlayerStateData();
                    List<Campaign.IUserActData> acts = new List<Campaign.IUserActData>(); List<int> owned = new List<int>();
                    foreach (SPTESL.Act act in source.Acts)
                    {
                        owned.Add(act.ResourceId.Id); List<Campaign.IUserChapterData> chapters = new List<Campaign.IUserChapterData>();
                        foreach (SPTESL.Chapter chapter in act.Chapters)
                        {
                            List<Campaign.IUserChapterEventData> events = new List<Campaign.IUserChapterEventData>();
                            foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents) events.Add(new Campaign.IUserChapterEventData { chapterEventId = e.ResourceId.Id, isComplete = true });
                            chapters.Add(new Campaign.IUserChapterData { chapterId = chapter.ResourceId.Id, hasIntroBeenShown = true, chapterEventData = events.ToArray(), chapterSeenCinematicTypeHashes = new int[0] });
                        }
                        acts.Add(new Campaign.IUserActData { actId = act.ResourceId.Id, isMastery = false, chapterData = chapters.ToArray() });
                    }
                    Campaign.IUserCampaignData user = new Campaign.IUserCampaignData { campaignId = source.ResourceId.Id, actData = acts.ToArray(), ownedActs = owned.ToArray() };
                    state.campaigns.Add(user);
                    // Keep one required event unfinished to verify no early Master unlock.
                    SPTESL.Chapter first = source.Acts[0].Chapters.Find(delegate(SPTESL.Chapter c) { return c.ChapterEventsToComplete.Count > 0; });
                    Campaign.IUserChapterData userChapter = Array.Find(user.actData[0].chapterData, delegate(Campaign.IUserChapterData c) { return c.chapterId == first.ResourceId.Id; });
                    Campaign.IUserChapterEventData unfinished = Array.Find(userChapter.chapterEventData, delegate(Campaign.IUserChapterEventData e) { return e.chapterEventId == first.ChapterEventsToComplete[0].resourceId; });
                    unfinished.isComplete = false; OfflineCampaignService.Advance(state, user.campaignId, false);
                    Require(!Array.Exists(user.actData, delegate(Campaign.IUserActData a) { return a.isMastery; }), "Master unlocked before Normal campaign completion.");
                    unfinished.isComplete = true; OfflineCampaignService.Advance(state, user.campaignId, false);
                    Campaign.IUserActData master = Array.Find(user.actData, delegate(Campaign.IUserActData a) { return a.isMastery && a.actId == source.Acts[0].ResourceId.Id; });
                    Require(master != null && master.chapterData.Length == 1 && !master.chapterData[0].isComplete, "Completing Normal did not start a separate Master tree.");
                    int count = user.actData.Length; OfflineCampaignService.Advance(state, user.campaignId, false);
                    Require(user.actData.Length == count, "Repeated progress check duplicated Master act.");
                    user.actData = acts.ToArray(); // Profile completed before Master progress was supported.
                    OfflineCampaignService.ReconcileMasterUnlocks(state);
                    Require(Array.Exists(user.actData, delegate(Campaign.IUserActData a) { return a.isMastery && a.actId == source.Acts[0].ResourceId.Id; }),
                        "Existing completed Normal progress did not recover its Master tree.");
                } finally { OfflineCampaignService.PublishContent(old); }
            });
            test("campaign-master-rewards-independent-and-persistent", delegate {
                List<SPTESL.Campaign> old = OfflineCampaignService.Content;
                try {
                    OfflineCampaignService.PublishContent(config.Campaigns);
                    OfflineCosmetics.Publish(config);
                    OfflineRewardsService.PublishCards(config, Path.Combine(output, "master-reward-bundle-validation.tsv"));
                    Dictionary<string, string[]> bundles = (Dictionary<string, string[]>)typeof(OfflineRewardsService)
                        .GetField("Bundles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                    int masterEvents = 0; HashSet<string> masterKeys = new HashSet<string>();
                    foreach (SPTESL.Campaign campaign in config.Campaigns) foreach (SPTESL.Act act in campaign.Acts)
                        foreach (SPTESL.Chapter chapter in act.Chapters) foreach (SPTESL.ChapterEvent chapterEvent in chapter.ChapterEvents)
                        {
                            if (string.IsNullOrEmpty(chapterEvent.MasterRewardsBundleKey) || OfflineStories.CampaignIds.Contains(campaign.ResourceId.Id)) continue;   // mod stories use reward keys, not installed bundles
                            masterEvents++;
                            foreach (string raw in chapterEvent.MasterRewardsBundleKey.Split(','))
                            {
                                string key = raw.Trim(); string[] entries;
                                Require(bundles.TryGetValue(key, out entries) && entries.Length > 0 && !string.IsNullOrEmpty(entries[0]),
                                    "Missing installed Master reward bundle: " + key);
                                masterKeys.Add(key);
                            }
                        }
                    Require(masterEvents == 94 && masterKeys.Count == 6, "Installed Master reward inventory changed; recheck its definitions.");
                    OfflinePlayerStateData d = new OfflinePlayerStateData(); int eventId = -1219628073;
                    d.grantedEvents.Add(eventId); // A Normal reward was claimed before this feature existed.
                    Require(OfflineRewardsService.GrantEvent(d, eventId, true), "Normal marker incorrectly suppressed Master rewards.");
                    Require(!OfflineRewardsService.GrantEvent(d, eventId, true) && !OfflineRewardsService.GrantEvent(d, eventId, false)
                        && d.grantedMasterEvents.Contains(eventId) && d.rewards.Count == 1, "Event rewards were duplicated or markers crossed difficulties.");
                    SparkleServices.Rewards.RewardDescription reward = d.rewards[0];
                    Require(reward.rewardPasses.Count == 2 && reward.rewardPasses[0].bundleKey == "rewards_20_soul_gems"
                        && reward.rewardPasses[1].bundleKey == "rewards_the_animal_lover_title", "Installed Master reward keys were not retained.");
                    foreach (SparkleServices.Rewards.RewardPassDescription pass in reward.rewardPasses) OfflineRewardsService.Apply(d, pass);
                    Require(d.gems == 20 && d.ownedTitles.Contains("title_the_animal_lover"), "Master gems or title were not credited.");
                    Apply(d, "rewards_30_soul_gems"); Apply(d, "rewards_40_soul_gems");
                    SparkleServices.Rewards.AcceptRewardResponse.AcceptRewardResult premium = Apply(d, "rewards_alt_swims_at_night");
                    SparkleServices.Rewards.AcceptRewardResponse.AcceptRewardResult cardback = Apply(d, "rewards_isle_of_madness_saint_card_back");
                    int i = d.cardIds.IndexOf(-79175348);
                    Require(config.FindCardById(-79175348) != null && i >= 0 && d.cardCounts[i] == 0 && d.cardPremiumCounts[i] == 1
                        && premium.items.Count == 1 && premium.items[0].isPremium && premium.items[0].typeId == -79175348
                        && d.gems == 90 && d.ownedCardBacks.Contains("cardback_isle_of_madness_saint") && cardback.items.Count == 1,
                        "Master gems, premium alternate card, or Saint card back were not credited correctly.");
                    string path = Path.Combine(Path.Combine(Path.Combine(output, "campaign-progress-tests"), Guid.NewGuid().ToString("N")), "state.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "{\"schema\":1,\"grantedEvents\":[12345]}");
                    OfflinePlayerState state = new OfflinePlayerState(path);
                    state.Mutate(delegate(OfflinePlayerStateData saved) { saved.grantedMasterEvents.Add(12345); });
                    OfflinePlayerStateData restored = new OfflinePlayerState(path).Snapshot();
                    Require(restored.grantedEvents.Contains(12345) && restored.grantedMasterEvents.Contains(12345),
                        "Legacy Normal marker or new Master marker failed to survive a save and reload.");
                } finally { OfflineCampaignService.PublishContent(old); }
            });
            test("campaign-king-ending-choice-titles", delegate {
                SPTESL.ChapterEvent king = null;
                foreach (SPTESL.Campaign campaign in config.Campaigns) foreach (SPTESL.Act act in campaign.Acts)
                    foreach (SPTESL.Chapter chapter in act.Chapters) foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                        if (e.ResourceId.Id == 1122728033) king = e;
                Require(king != null && king.GatedScenarios.Count > 0 && king.EndingModal != null, "Installed King event missing.");
                bool optionsFound = false;
                foreach (StoryDialogueData dialogue in king.EndingModal.DialogueDataEvents)
                    if (dialogue.Option1 != null && dialogue.Option2 != null
                        && LocalizationManager.Instance.GetText(dialogue.Option1.ButtonTextKey.RawKey) == "Kill"
                        && LocalizationManager.Instance.GetText(dialogue.Option2.ButtonTextKey.RawKey) == "Spare") optionsFound = true;
                Require(optionsFound, "Installed King ending choices changed.");
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                Require(OfflineTitleFeats.StoryChoice(d, king.ResourceId.Id, false, Campaign.IDialogModalType.Ending, Campaign.IUserCampaignChoice.First) == null,
                    "Unwon King event awarded a title.");
                Require(OfflineTitleFeats.StoryChoice(d, king.ResourceId.Id, true, Campaign.IDialogModalType.Intro, Campaign.IUserCampaignChoice.First) == null,
                    "Intro choice awarded an ending title.");
                SparkleServices.Rewards.RewardDescription kill = OfflineTitleFeats.StoryChoice(d, king.ResourceId.Id, true, Campaign.IDialogModalType.Ending, Campaign.IUserCampaignChoice.First);
                Require(kill != null && kill.rewardPasses[0].bundleKey == OfflineTitles.TitleBundlePrefix + "title_the_kingslayer", "Kill did not return its title reward.");
                Require(OfflineTitleFeats.StoryChoice(d, king.ResourceId.Id, true, Campaign.IDialogModalType.Ending, Campaign.IUserCampaignChoice.First) == null,
                    "Repeated choice duplicated its title reward.");
                SparkleServices.Rewards.RewardDescription spare = OfflineTitleFeats.StoryChoice(d, king.ResourceId.Id, true, Campaign.IDialogModalType.Ending, Campaign.IUserCampaignChoice.Second);
                Require(spare != null && spare.rewardPasses[0].bundleKey == OfflineTitles.TitleBundlePrefix + "title_the_kings_protector", "Spare did not return its title reward.");
                OfflineRewardsService.Apply(d, kill.rewardPasses[0]); OfflineRewardsService.Apply(d, spare.rewardPasses[0]);
                Require(d.ownedTitles.Contains("title_the_kingslayer") && d.ownedTitles.Contains("title_the_kings_protector"), "Accepted choice titles were not owned.");
            });
            test("match-notice-detects-new-quest-and-title-rewards", delegate {
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                OfflineTitles.Grant(d, "title_existing");
                Require(!OfflineNativeMatch.RecordRewardChanges(d, delegate { }), "A prior pending reward was mistaken for a new match reward.");
                Require(OfflineNativeMatch.RecordRewardChanges(d, delegate { OfflineTitles.Grant(d, "title_new"); }), "A new title reward was missed.");
                Require(!OfflineNativeMatch.RecordRewardChanges(d, delegate { OfflineTitles.Grant(d, "title_new"); }), "A duplicate title was mistaken for a new reward.");
                Require(OfflineNativeMatch.RecordRewardChanges(d, delegate { d.rewards.Add(new SparkleServices.Rewards.RewardDescription { rewardId = ++d.nextRewardId, source = SparkleServices.Rewards.RewardSource.Quest }); }),
                    "A new quest reward was missed.");
            });
            test("campaign-forgotten-hero-choice-bundle-rewards", delegate {
                OfflineRewardsService.PublishCards(config, Path.Combine(output, "choice-reward-bundle-validation.tsv"));
                int[] events = { -2076749286, 896653332, -383189891, 1556663847, -1503887833, -1935746928 };
                string[][] names = { new[] { "Steel Scimitar", "Execute" }, new[] { "Snow Wolf", "Cast Out" },
                    new[] { "Green-Touched Spriggan", "Hackwing Feather" }, new[] { "Shimmerene Peddler", "gold:50" },
                    new[] { "Haunting Spirit", "Plea to Kynareth" }, new[] { "Tyr", "Orb of Vaermina" } };
                for (int i = 0; i < events.Length; i++)
                {
                    SPTESL.ChapterEvent source = null;
                    foreach (SPTESL.Campaign campaign in config.Campaigns) foreach (SPTESL.Act act in campaign.Acts)
                        foreach (SPTESL.Chapter chapter in act.Chapters) foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                            if (e.ResourceId.Id == events[i]) source = e;
                    Require(source != null, "Installed choice event missing: " + events[i]);
                    for (int j = 0; j < 2; j++)
                    {
                        OfflinePlayerStateData d = new OfflinePlayerStateData();
                        Campaign.IUserCampaignChoice choice = j == 0 ? Campaign.IUserCampaignChoice.First : Campaign.IUserCampaignChoice.Second;
                        string expectedBundle = j == 0 ? source.IntroFirstChoiceRewardsBundleKey : source.IntroSecondChoiceRewardsBundleKey;
                        SparkleServices.Rewards.RewardDescription reward = OfflineRewardsService.GrantChoice(d, source, Campaign.IDialogModalType.Intro, choice, false);
                        Require(reward != null && reward.rewardPasses.Count == 1 && reward.rewardPasses[0].bundleKey == expectedBundle,
                            "Choice did not return installed bundle for event " + events[i]);
                        Require(OfflineRewardsService.GrantChoice(d, source, Campaign.IDialogModalType.Intro, choice, false) == null,
                            "Choice reward duplicated for event " + events[i]);
                        SparkleServices.Rewards.AcceptRewardResponse.AcceptRewardResult applied = OfflineRewardsService.Apply(d, reward.rewardPasses[0]);
                        if (names[i][j] == "gold:50") Require(d.gold == 50 && applied.gold == 50, "Rob choice did not grant 50 gold.");
                        else
                        {
                            Require(applied.items.Count == 1 && applied.items[0].type == "card" && applied.items[0].count == 1,
                                "Choice did not grant its card: " + names[i][j]);
                            SPTESL.Card card = config.FindCardById(applied.items[0].typeId);
                            Require(card != null && card.Name == names[i][j] && d.TotalCopies(card.ResourceId.Id) == 1,
                                "Wrong choice card for " + names[i][j]);
                        }
                    }
                }
            });
            test("campaign-forgotten-hero-title-bundles", delegate {
                OfflineCosmetics.Publish(config);
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                Apply(d, "onboarding_game_8_bundle_title"); Apply(d, "onboarding_game_20_bundle_title");
                Require(d.ownedTitles.Contains("title_the_smuggler") && d.ownedTitles.Contains("title_the_savior_of_cyrodiil"),
                    "Forgotten Hero title bundles did not add both installed title keys.");
            });
        }
        private static SparkleServices.Rewards.AcceptRewardResponse.AcceptRewardResult Apply(OfflinePlayerStateData d, string key)
        { return OfflineRewardsService.Apply(d, new SparkleServices.Rewards.RewardPassDescription { bundleKey = key, bundleCount = 1 }); }
        private static Campaign.IUserActData Act(bool mastery, Campaign.IUserChapterEventData e)
        {
            return new Campaign.IUserActData { actId = 123, isMastery = mastery,
                chapterData = new Campaign.IUserChapterData[] { new Campaign.IUserChapterData { chapterId = 456,
                    chapterEventData = new Campaign.IUserChapterEventData[] { e } } } };
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private static ChapterEvent FirstRewarded(GameConfiguration config, string campaignName)
        {
            SPTESL.Campaign campaign = config.Campaigns.Find(delegate(SPTESL.Campaign c) { return c.ResourceId.UniqueName == campaignName; });
            if (campaign == null) return null;
            foreach (Act a in campaign.Acts) foreach (Chapter ch in a.Chapters) foreach (ChapterEvent e in ch.ChapterEvents)
                if (!string.IsNullOrEmpty(e.RewardsBundleKey)) return e;
            return null;
        }
    }
}
