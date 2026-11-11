using System;
using System.Collections.Generic;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Local Campaigns service. User progress uses the client's own IUser* classes (stored
    // in OfflinePlayerState); content (acts/chapters/events/scenarios) comes from the
    // installed configuration, published once from the main thread. Rules mirror the
    // client's CampaignService: unlocked = present in user data; a chapter is complete
    // when every ChapterEventsToComplete event isComplete; completion unlocks the next
    // chapter, or the next act's first chapter.
    public static class OfflineCampaignService
    {
        public const int ForgottenHeroCampaignId = 838862610;
        private static volatile List<SPTESL.Campaign> content;

        public static void PublishContent(List<SPTESL.Campaign> campaigns) { content = campaigns; }
        public static List<SPTESL.Campaign> Content { get { return content; } }

        // Shape of every campaign scenario (lanes, decks, overrides), to scope story rules.
        public static void DumpScenarios(SPTESL.GameConfiguration config, string path)
        {
            System.Text.StringBuilder rows = new System.Text.StringBuilder("campaign\tchapter\tscenario\tscenario_id\tfirst\tuser_deck\tdeck_overrides\tzones\teffects\tai\tavatar_data\n");
            if (content != null)
                foreach (SPTESL.Campaign campaign in content)
                    foreach (SPTESL.Act act in campaign.Acts)
                        foreach (SPTESL.Chapter chapter in act.Chapters)
                            foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                                foreach (SPTESL.GatedScenario gated in e.GatedScenarios)
                                {
                                    SPTESL.Scenario s = config.Scenarios.Find(delegate(SPTESL.Scenario x) { return x.ResourceId.Id == gated.Scenario.resourceId; });
                                    if (s == null) { rows.Append(campaign.ResourceId.UniqueName).Append('\t').Append(chapter.ResourceId.UniqueName).Append("\tMISSING\t").Append(gated.Scenario.resourceId).Append('\n'); continue; }
                                    List<string> decks = new List<string>(), zones = new List<string>();
                                    foreach (SPTESL.DeckOverride d in s.DeckOverrides) decks.Add(d.SlotMask + ":" + d.Override.resourceId);
                                    foreach (SPTESL.GameZoneOverrideList list in s.GameZoneOverrides)
                                        foreach (SPTESL.GameZoneOverride zone in list.ZoneList)
                                        {
                                            List<string> lanes = new List<string>();
                                            foreach (SPTESL.LaneOverride lane in zone.LaneOverrides)
                                            {
                                                SPTESL.SizedCardLane sized = lane.Lane as SPTESL.SizedCardLane;
                                                lanes.Add(sized == null ? (lane.Lane == null ? "null" : lane.Lane.GetType().Name) : sized.LaneType + "/" + sized.ExtraLaneType + "/" + sized.Capacity);
                                                if (lane.StartingCards.Count > 0) lanes[lanes.Count - 1] += "+" + lane.StartingCards.Count + "cards";
                                            }
                                            zones.Add(list.SlotMask + ":" + zone.ZoneToOverride + (lanes.Count > 0 ? "[" + string.Join(" ", lanes.ToArray()) + "]" : ""));
                                        }
                                    rows.Append(campaign.ResourceId.UniqueName).Append('\t').Append(chapter.ResourceId.UniqueName).Append('\t').Append(s.ResourceId.UniqueName)
                                        .Append('\t').Append(s.ResourceId.Id).Append('\t').Append(s.StartingPlayer).Append('\t').Append(s.ScenarioAllowsUserDeckSelection())
                                        .Append('\t').Append(string.Join(",", decks.ToArray())).Append('\t').Append(string.Join(" ; ", zones.ToArray()))
                                        .Append('\t').Append(s.TriggeredEffects == null ? -1 : s.TriggeredEffects.Count).Append('\t').Append(s.AiOpponent.resourceId)
                                        .Append('\t').Append(s.PlayerAvatarDataOverride.resourceId).Append('\n');
                                }
            System.IO.File.WriteAllText(path, rows.ToString());
        }

        // Installed quests with their localized texts (Quest.Goal is NonSerialized), for goal reconstruction.
        public static void DumpQuests(SPTESL.GameConfiguration config, string path)
        {
            System.Text.StringBuilder rows = new System.Text.StringBuilder("id\tname\ttype\tgiver\trank\tproduction\treward\ttitle\tgame_text\tdescription\n");
            foreach (SPTESL.Quest q in config.Quests)
            {
                if (q == null) continue;
                rows.Append(q.ResourceId.Id).Append('\t').Append(q.ResourceId.UniqueName).Append('\t').Append(q.QuestType).Append('\t').Append(q.QuestGiverType)
                    .Append('\t').Append(q.QuestRank).Append('\t').Append(q.Production).Append('\t').Append(q.RewardBundle)
                    .Append('\t').Append(Text(q.Ui.TitleText)).Append('\t').Append(Text(q.Ui.GameText)).Append('\t').Append(Text(q.Ui.Description)).Append('\n');
            }
            System.IO.File.WriteAllText(path, rows.ToString());
        }

        private static string Text(SPTESL.LocalizedStringKey key)
        {
            string raw = key.RawKey;
            string text = string.IsNullOrEmpty(raw) || LocalizationManager.Instance == null ? null : LocalizationManager.Instance.GetText(raw);
            return (text ?? raw ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }

        // Installed campaign graph, for reference (main thread, once).
        public static void DumpContent(string path)
        {
            System.Text.StringBuilder rows = new System.Text.StringBuilder("campaign\tact\tact_index\tchapter\tchapter_name\tchapter_index\tevents_to_complete\tevent\tscenarios\thidden\trequired_outcomes\trewards\tintro_cinematics\n");
            if (content != null)
                foreach (SPTESL.Campaign campaign in content)
                    for (int a = 0; a < campaign.Acts.Count; a++)
                        for (int c = 0; c < campaign.Acts[a].Chapters.Count; c++)
                        {
                            SPTESL.Chapter chapter = campaign.Acts[a].Chapters[c];
                            List<string> required = new List<string>();
                            foreach (SPTESL.UniqueResourceReference r in chapter.ChapterEventsToComplete) required.Add(r.resourceId.ToString());
                            string head = campaign.ResourceId.UniqueName + "\t" + campaign.Acts[a].ResourceId.Id + "\t" + a + "\t" + chapter.ResourceId.Id + "\t" + chapter.ResourceId.UniqueName
                                + "\t" + c + "\t" + string.Join(",", required.ToArray()) + "\t";
                            if (chapter.ChapterEvents.Count == 0) rows.Append(head).Append("-\t\t\t\t\t" + chapter.GatedIntroCinematics.Count + "\n");
                            foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                            {
                                List<string> scenarios = new List<string>();
                                foreach (SPTESL.GatedScenario g in e.GatedScenarios) scenarios.Add(g.Scenario.resourceId.ToString());
                                rows.Append(head).Append(e.ResourceId.Id).Append('\t').Append(string.Join(",", scenarios.ToArray())).Append('\t').Append(e.IsHiddenFromMap)
                                    .Append('\t').Append(e.RequiredOutcomes.Count).Append('\t').Append(e.RewardsBundleKey).Append('\t').Append(chapter.GatedIntroCinematics.Count)
                                    .Append(" choice1=").Append(e.IntroFirstChoiceRewardsBundleKey).Append(" choice2=").Append(e.IntroSecondChoiceRewardsBundleKey)
                                    .Append(" introDialogues=").Append(e.IntroModal == null ? 0 : e.IntroModal.DialogueDataEvents.Count)
                                    .Append(" endingDialogues=").Append(e.EndingModal == null ? 0 : e.EndingModal.DialogueDataEvents.Count).Append('\n');
                            }
                        }
            System.IO.File.WriteAllText(path, rows.ToString());
        }
        public static bool HasContent { get { return content != null; } }

        public static void Register(OfflineLocalServices services, OfflinePlayerState state)
        {
            services.Register("campaigns/list", delegate { return List(state); });
            services.Register("campaigns/checkCampaignProgress", delegate(string m, string p, string body) {
                Campaign.CheckCampaignProgressRequest request = JsonUtility.FromJson<Campaign.CheckCampaignProgressRequest>(body);
                state.Mutate(delegate(OfflinePlayerStateData d) { EnsureStarted(d); if (request != null) Advance(d, request.campaignId, request.isMastery); });
                return Ok(); });
            services.Register("campaigns/setChapterDialogStatus", delegate(string m, string p, string body) {
                Campaign.SetChapterDialogStatusRequest request = JsonUtility.FromJson<Campaign.SetChapterDialogStatusRequest>(body);
                if (request != null) state.Mutate(delegate(OfflinePlayerStateData d) {
                    Campaign.IUserChapterData chapter = FindChapter(d, request.campaignId, request.actId, request.chapterId, request.isMastery);
                    if (chapter == null) return;
                    if (request.modalType == Campaign.IDialogModalType.Intro) chapter.hasIntroBeenShown = request.seen;
                    if (request.modalType == Campaign.IDialogModalType.Ending) chapter.hasEndingBeenShown = request.seen;
                    if (request.cinematicId != 0 && Array.IndexOf(chapter.chapterSeenCinematicTypeHashes, request.cinematicId) < 0)
                    {
                        List<int> seen = new List<int>(chapter.chapterSeenCinematicTypeHashes); seen.Add(request.cinematicId);
                        chapter.chapterSeenCinematicTypeHashes = seen.ToArray();
                    }
                    Advance(d, request.campaignId, request.isMastery); });
                Campaign.SetChapterDialogResult result = new Campaign.SetChapterDialogResult();
                result.resultCode = ResultCode.Ok; result.rewards = new List<Rewards.RewardDescription>();
                return JsonUtility.ToJson(result); });
            services.Register("campaigns/setChapterEventChoice", delegate(string m, string p, string body) {
                Campaign.SetChapterEventChoiceRequest request = JsonUtility.FromJson<Campaign.SetChapterEventChoiceRequest>(body);
                List<Rewards.RewardDescription> granted = new List<Rewards.RewardDescription>();
                if (request != null) state.Mutate(delegate(OfflinePlayerStateData d) {
                    Campaign.IUserChapterData chapter = FindChapter(d, request.campaignId, request.actId, request.chapterId, request.isMastery);
                    if (chapter == null) return;
                    foreach (Campaign.IUserChapterEventData e in chapter.chapterEventData)
                        if (e.chapterEventId == request.chapterEventId)
                        {
                            if (request.modalType == Campaign.IDialogModalType.Intro) e.introChoice = request.choiceType;
                            if (request.modalType == Campaign.IDialogModalType.Ending) e.endingChoice = request.choiceType;
                            // Reconstruction: a dialogue-only event (no scenario) completes once its choice is made.
                            SPTESL.ChapterEvent source = FindContentEvent(request.campaignId, e.chapterEventId);
                            if (source != null && source.GatedScenarios.Count == 0) e.isComplete = true;
                            // The chosen option's own card or gold (GatedStoryDialogueOption), once.
                            Rewards.RewardDescription reward = OfflineRewardsService.GrantChoice(d, source, request.modalType, request.choiceType, request.isMastery);
                            if (reward != null) granted.Add(reward);
                            reward = OfflineTitleFeats.StoryChoice(d, e.chapterEventId, e.isComplete, request.modalType, request.choiceType);
                            if (reward != null) granted.Add(reward);
                        }
                    Advance(d, request.campaignId, request.isMastery); });
                Campaign.SetChapterEventResult result = new Campaign.SetChapterEventResult();
                result.resultCode = ResultCode.Ok; result.rewards = granted;
                return JsonUtility.ToJson(result); });
        }

        private static string Ok() { return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.Ok }); }

        private static string List(OfflinePlayerState state)
        {
            OfflinePlayerStateData snapshot = state.Snapshot();
            if (content != null && content.Exists(delegate(SPTESL.Campaign c) {
                    return c != null && c.Production && c.Acts.Count > 0 && !snapshot.campaigns.Exists(delegate(Campaign.IUserCampaignData u) { return u.campaignId == c.ResourceId.Id; }); }))
                state.Mutate(EnsureStarted);
            snapshot = state.Snapshot();
            if (NeedsMasterReconciliation(snapshot)) state.Mutate(ReconcileMasterUnlocks);
            Campaign.ListCampaignsResponse response = new Campaign.ListCampaignsResponse();
            response.resultCode = ResultCode.Ok;
            response.campaignDatas = state.Snapshot().campaigns;
            return JsonUtility.ToJson(response);
        }

        // A new player owns The Forgotten Hero with its first act's first chapter unlocked.
        public static void EnsureStarted(OfflinePlayerStateData d)
        {
            if (content == null) return;
            // Offline, every installed campaign is owned (the original sold the later stories and
            // their acts). The Forgotten Hero stays linear from its first act; other stories and
            // puzzle sets own every act, each starting at its first chapter.
            foreach (SPTESL.Campaign source in content)
            {
                if (source == null || !source.Production || source.Acts.Count == 0) continue;
                if (d.campaigns.Exists(delegate(Campaign.IUserCampaignData c) { return c.campaignId == source.ResourceId.Id; })) continue;
                bool tfh = source.ResourceId.Id == ForgottenHeroCampaignId;
                if (!tfh && d.storiesForGold && !OfflineStories.CampaignIds.Contains(source.ResourceId.Id)) continue;   // "stories cost gold": bought act by act in the store (OfflineGoldStore)
                List<int> owned = new List<int>();
                List<Campaign.IUserActData> acts = new List<Campaign.IUserActData>();
                foreach (SPTESL.Act act in source.Acts)
                {
                    if (act.Chapters.Count == 0) continue;
                    owned.Add(act.ResourceId.Id);
                    if (!tfh || acts.Count == 0) acts.Add(NewAct(act, false));
                    if (tfh) break;
                }
                if (acts.Count == 0) continue;
                Campaign.IUserCampaignData user = new Campaign.IUserCampaignData();
                user.campaignId = source.ResourceId.Id; user.isActive = tfh;
                user.ownedActs = owned.ToArray(); user.actData = acts.ToArray();
                d.campaigns.Add(user);
            }
        }

        // Owns one act (store purchase or "stories free" again): the campaign entry is created if needed and the act starts
        // at its first chapter. Returns false if it was already owned.
        public static bool GrantAct(OfflinePlayerStateData d, SPTESL.Campaign source, SPTESL.Act act)
        {
            if (source == null || act == null || act.Chapters.Count == 0) return false;
            Campaign.IUserCampaignData user = d.campaigns.Find(delegate(Campaign.IUserCampaignData c) { return c.campaignId == source.ResourceId.Id; });
            if (user == null)
            {
                user = new Campaign.IUserCampaignData();
                user.campaignId = source.ResourceId.Id; user.isActive = false;
                user.ownedActs = new int[0]; user.actData = new Campaign.IUserActData[0];
                d.campaigns.Add(user);
            }
            List<int> owned = new List<int>(user.ownedActs ?? new int[0]);
            if (owned.Contains(act.ResourceId.Id)) return false;
            owned.Add(act.ResourceId.Id);
            user.ownedActs = owned.ToArray();
            List<Campaign.IUserActData> acts = new List<Campaign.IUserActData>(user.actData ?? new Campaign.IUserActData[0]);
            if (!acts.Exists(delegate(Campaign.IUserActData a) { return !a.isMastery && a.actId == act.ResourceId.Id; })) acts.Add(NewAct(act, false));
            user.actData = acts.ToArray();
            return true;
        }

        // Un-owns acts with no completed chapter; a campaign left with no act is removed. Returns acts re-locked.
        public static int RelockUnplayed(OfflinePlayerStateData d, SPTESL.Campaign source)
        {
            Campaign.IUserCampaignData user = d.campaigns.Find(delegate(Campaign.IUserCampaignData c) { return c.campaignId == source.ResourceId.Id; });
            if (user == null) return 0;
            List<int> owned = new List<int>(user.ownedActs ?? new int[0]);
            List<Campaign.IUserActData> acts = new List<Campaign.IUserActData>(user.actData ?? new Campaign.IUserActData[0]);
            int relocked = 0;
            foreach (int actId in owned.ToArray())
            {
                bool played = acts.Exists(delegate(Campaign.IUserActData a) {
                    return a.actId == actId && a.chapterData != null && Array.Exists(a.chapterData, delegate(Campaign.IUserChapterData ch) { return ch != null && ch.isComplete; }); });
                if (played) continue;
                owned.Remove(actId);
                acts.RemoveAll(delegate(Campaign.IUserActData a) { return a.actId == actId; });
                relocked++;
            }
            if (owned.Count == 0) d.campaigns.Remove(user);
            else { user.ownedActs = owned.ToArray(); user.actData = acts.ToArray(); }
            return relocked;
        }

        private static Campaign.IUserActData NewAct(SPTESL.Act act, bool mastery)
        {
            Campaign.IUserActData user = new Campaign.IUserActData();
            user.actId = act.ResourceId.Id; user.isMastery = mastery;
            user.chapterData = new Campaign.IUserChapterData[] { NewChapter(act.Chapters[0]) };
            return user;
        }

        private static Campaign.IUserChapterData NewChapter(SPTESL.Chapter chapter)
        {
            Campaign.IUserChapterData user = new Campaign.IUserChapterData();
            user.chapterId = chapter.ResourceId.Id;
            user.chapterSeenCinematicTypeHashes = new int[0];
            List<Campaign.IUserChapterEventData> events = new List<Campaign.IUserChapterEventData>();
            foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                if (!OfflineStories.IsModChapter(chapter.ResourceId.Id) || e.RequiredOutcomes.Count == 0)   // mod events with requirements are revealed later
                    events.Add(new Campaign.IUserChapterEventData { chapterEventId = e.ResourceId.Id, chapterId = chapter.ResourceId.Id });
            user.chapterEventData = events.ToArray();
            return user;
        }

        public static Campaign.IUserChapterData FindChapter(OfflinePlayerStateData d, int campaignId, int actId, int chapterId, bool mastery)
        {
            Campaign.IUserCampaignData campaign = d.campaigns.Find(delegate(Campaign.IUserCampaignData c) { return c.campaignId == campaignId; });
            if (campaign == null) return null;
            foreach (Campaign.IUserActData act in campaign.actData)
                if (act.actId == actId && act.isMastery == mastery)
                    foreach (Campaign.IUserChapterData chapter in act.chapterData)
                        if (chapter.chapterId == chapterId) return chapter;
            return null;
        }

        // Mark a won chapter event complete (called by the match-result path).
        public static bool CompleteEvent(OfflinePlayerStateData d, int chapterEventId, bool mastery)
        {
            foreach (Campaign.IUserCampaignData campaign in d.campaigns)
                foreach (Campaign.IUserActData act in campaign.actData)
                {
                    if (act.isMastery != mastery) continue;
                    foreach (Campaign.IUserChapterData chapter in act.chapterData)
                        foreach (Campaign.IUserChapterEventData e in chapter.chapterEventData)
                            if (e.chapterEventId == chapterEventId) { e.isComplete = true; Advance(d, campaign.campaignId, act.isMastery); return true; }
                }
            return false;
        }

        // Completion and unlocking, in content order, until stable.
        public static void Advance(OfflinePlayerStateData d, int campaignId, bool mastery)
        {
            if (content == null) return;
            SPTESL.Campaign source = content.Find(delegate(SPTESL.Campaign c) { return c.ResourceId.Id == campaignId; });
            Campaign.IUserCampaignData user = d.campaigns.Find(delegate(Campaign.IUserCampaignData c) { return c.campaignId == campaignId; });
            // Mod stories: reveal events whose requirements are met, then complete/unlock, until stable.
            for (int pass = 0; pass < 8; pass++)
            {
                OfflineStories.RevealEvents(source, user, mastery);
                AdvanceChapters(d, campaignId, mastery);
                if (OfflineStories.RevealEvents(source, user, mastery) == 0) break;
            }
            if (source != null && user != null) OfflineCosmetics.AfterCampaignProgress(d, source, user);
        }

        private static void AdvanceChapters(OfflinePlayerStateData d, int campaignId, bool mastery)
        {
            if (content == null) return;
            SPTESL.Campaign source = content.Find(delegate(SPTESL.Campaign c) { return c.ResourceId.Id == campaignId; });
            Campaign.IUserCampaignData user = d.campaigns.Find(delegate(Campaign.IUserCampaignData c) { return c.campaignId == campaignId; });
            if (source == null || user == null) return;
            for (int a = 0; a < source.Acts.Count; a++)
            {
                Campaign.IUserActData userAct = Array.Find(user.actData, delegate(Campaign.IUserActData x) { return x.actId == source.Acts[a].ResourceId.Id && x.isMastery == mastery; });
                if (userAct == null) return;
                for (int c = 0; c < source.Acts[a].Chapters.Count; c++)
                {
                    SPTESL.Chapter chapter = source.Acts[a].Chapters[c];
                    Campaign.IUserChapterData userChapter = Array.Find(userAct.chapterData, delegate(Campaign.IUserChapterData x) { return x.chapterId == chapter.ResourceId.Id; });
                    if (userChapter == null) return;
                    if (!IsComplete(chapter, userChapter)) return;
                    userChapter.isComplete = true;
                    if (c + 1 < source.Acts[a].Chapters.Count)
                    {
                        SPTESL.Chapter next = source.Acts[a].Chapters[c + 1];
                        if (!Array.Exists(userAct.chapterData, delegate(Campaign.IUserChapterData x) { return x.chapterId == next.ResourceId.Id; }))
                        {
                            List<Campaign.IUserChapterData> chapters = new List<Campaign.IUserChapterData>(userAct.chapterData);
                            chapters.Add(NewChapter(next));
                            userAct.chapterData = chapters.ToArray();
                        }
                    }
                    else if (a + 1 < source.Acts.Count && source.Acts[a + 1].Chapters.Count > 0
                        && !Array.Exists(user.actData, delegate(Campaign.IUserActData x) { return x.actId == source.Acts[a + 1].ResourceId.Id && x.isMastery == mastery; }))
                    {
                        List<Campaign.IUserActData> acts = new List<Campaign.IUserActData>(user.actData);
                        acts.Add(NewAct(source.Acts[a + 1], mastery));
                        user.actData = acts.ToArray();
                        List<int> owned = new List<int>(user.ownedActs);
                        if (!owned.Contains(source.Acts[a + 1].ResourceId.Id)) owned.Add(source.Acts[a + 1].ResourceId.Id);
                        user.ownedActs = owned.ToArray();
                    }
                }
            }
            // Master stories have their own progress tree and installed MasterScenario references.
            // Start that tree only after every Normal chapter in this campaign is complete.
            if (!mastery && source.Acts.Count > 0 && source.Acts[0].Chapters.Count > 0 && HasMasterScenarios(source)
                && !Array.Exists(user.actData, delegate(Campaign.IUserActData x) { return x.actId == source.Acts[0].ResourceId.Id && x.isMastery; }))
            {
                List<Campaign.IUserActData> acts = new List<Campaign.IUserActData>(user.actData);
                acts.Add(NewAct(source.Acts[0], true)); user.actData = acts.ToArray();
            }
        }

        private static bool HasMasterScenarios(SPTESL.Campaign campaign)
        {
            foreach (SPTESL.Act act in campaign.Acts) foreach (SPTESL.Chapter chapter in act.Chapters)
                foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents) foreach (SPTESL.GatedScenario g in e.GatedScenarios)
                    if (g.MasterScenario != null && g.MasterScenario.resourceId != 0 && g.MasterScenario.resourceId != SPTESL.UniqueResourceId.GetInvalid().Id)
                        return true;
            return false;
        }

        // Existing profiles may have completed Normal before Master progress was implemented.
        private static bool NeedsMasterReconciliation(OfflinePlayerStateData d)
        {
            if (content == null) return false;
            foreach (SPTESL.Campaign source in content)
            {
                if (source == null) continue;
                Campaign.IUserCampaignData user = d.campaigns.Find(delegate(Campaign.IUserCampaignData x) { return x.campaignId == source.ResourceId.Id; });
                if (ShouldStartMaster(source, user)) return true;
            }
            return false;
        }
        private static bool ShouldStartMaster(SPTESL.Campaign source, Campaign.IUserCampaignData user)
        {
            if (user == null || source.Acts.Count == 0 || source.Acts[0].Chapters.Count == 0 || !HasMasterScenarios(source)
                || Array.Exists(user.actData, delegate(Campaign.IUserActData x) { return x.isMastery && x.actId == source.Acts[0].ResourceId.Id; })) return false;
            foreach (SPTESL.Act act in source.Acts)
            {
                Campaign.IUserActData owned = Array.Find(user.actData, delegate(Campaign.IUserActData x) { return !x.isMastery && x.actId == act.ResourceId.Id; });
                if (owned == null) return false;
                foreach (SPTESL.Chapter chapter in act.Chapters)
                {
                    Campaign.IUserChapterData saved = Array.Find(owned.chapterData, delegate(Campaign.IUserChapterData x) { return x.chapterId == chapter.ResourceId.Id; });
                    if (saved == null || !IsComplete(chapter, saved)) return false;
                }
            }
            return true;
        }
        public static void ReconcileMasterUnlocks(OfflinePlayerStateData d)
        {
            if (content == null) return;
            foreach (SPTESL.Campaign campaign in content) if (campaign != null)
            {
                Campaign.IUserCampaignData user = d.campaigns.Find(delegate(Campaign.IUserCampaignData x) { return x.campaignId == campaign.ResourceId.Id; });
                if (ShouldStartMaster(campaign, user)) Advance(d, campaign.ResourceId.Id, false);
            }
        }

        public static SPTESL.ChapterEvent FindContentEvent(int eventId)
        {
            List<SPTESL.Campaign> campaigns = content;
            if (campaigns == null) return null;
            foreach (SPTESL.Campaign campaign in campaigns)
            {
                SPTESL.ChapterEvent found = FindContentEvent(campaign.ResourceId.Id, eventId);
                if (found != null) return found;
            }
            return null;
        }

        public static SPTESL.ChapterEvent FindContentEvent(int campaignId, int eventId)
        {
            List<SPTESL.Campaign> campaigns = content;
            if (campaigns == null) return null;
            foreach (SPTESL.Campaign campaign in campaigns)
                if (campaign.ResourceId.Id == campaignId)
                    foreach (SPTESL.Act act in campaign.Acts)
                        foreach (SPTESL.Chapter chapter in act.Chapters)
                            foreach (SPTESL.ChapterEvent e in chapter.ChapterEvents)
                                if (e.ResourceId.Id == eventId) return e;
            return null;
        }

        private static bool IsComplete(SPTESL.Chapter chapter, Campaign.IUserChapterData user)
        {
            // Reconstruction: event-less chapters (prologue, epilogue) are story cinematics,
            // complete once their intro has been shown.
            if (chapter.ChapterEvents.Count == 0) return user.hasIntroBeenShown;
            if (chapter.ChapterEventsToComplete.Count == 0) return false;
            foreach (SPTESL.UniqueResourceReference required in chapter.ChapterEventsToComplete)
            {
                Campaign.IUserChapterEventData e = Array.Find(user.chapterEventData, delegate(Campaign.IUserChapterEventData x) { return x.chapterEventId == required.resourceId; });
                if (e == null || !e.isComplete) return false;
            }
            return true;
        }
    }
}
