using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using SPTESL;
namespace OfflineEngine
{
    // Read-only export of installed story gates and dialogue labels, including native title references.
    public static class StoryBranchEvidence
    {
        public static void Run(GameConfiguration config, Action<string, Action> test, string output)
        {
            test("story-branch-installed-evidence-export", delegate {
                StringBuilder b = new StringBuilder("campaign\tact\tchapter\tevent\tkind\tid\tname\tnormal_title\tmaster_title\toutcomes\toption1\toption2\toption3\tmaster_scenario\n");
                StringBuilder rewards = new StringBuilder("campaign\tact\tchapter\tevent\tnormal\tmaster\tchoice1\tchoice1_master\tchoice2\tchoice2_master\n");
                int count = 0;
                Dictionary<int, string> expected = new Dictionary<int, string> { { 645408370, "title_friend_to_animals" },
                    { 591184772, "title_IOM_the_ghost_whisperer" }, { -1909586255, "title_IOM_the_betrayer" },
                    { -938799815, "title_IOM_the_prince_of_order" } };
                HashSet<int> verified = new HashSet<int>();
                foreach (Campaign campaign in config.Campaigns) foreach (Act act in campaign.Acts) foreach (Chapter chapter in act.Chapters)
                    foreach (ChapterEvent e in chapter.ChapterEvents)
                    {
                        rewards.Append(Clean(campaign.ResourceId.UniqueName)).Append('\t').Append(act.ResourceId.Id).Append('\t').Append(chapter.ResourceId.Id)
                            .Append('\t').Append(e.ResourceId.Id).Append('\t').Append(Clean(e.RewardsBundleKey)).Append('\t').Append(Clean(e.MasterRewardsBundleKey))
                            .Append('\t').Append(Clean(e.IntroFirstChoiceRewardsBundleKey)).Append('\t').Append(Clean(e.IntroFirstChoiceMasterRewardsBundleKey))
                            .Append('\t').Append(Clean(e.IntroSecondChoiceRewardsBundleKey)).Append('\t').Append(Clean(e.IntroSecondChoiceMasterRewardsBundleKey)).Append('\n');
                        string prefix = Clean(campaign.ResourceId.UniqueName) + "\t" + act.ResourceId.Id + "\t" + chapter.ResourceId.Id + "\t" + e.ResourceId.Id + "\t";
                        foreach (GatedScenario g in e.GatedScenarios)
                        {
                            string wanted;
                            if (expected.TryGetValue(Id(g.Scenario), out wanted) && Title(config, g.TitleAwarded) == wanted)
                            {
                                Scenario normal = config.Scenarios.Find(delegate(Scenario x) { return x.ResourceId.Id == Id(g.Scenario); });
                                Scenario master = config.Scenarios.Find(delegate(Scenario x) { return x.ResourceId.Id == Id(g.MasterScenario); });
                                if (normal == null || master == null || normal.ResourceId.UniqueName.Length < 4
                                    || master.ResourceId.UniqueName != normal.ResourceId.UniqueName.Substring(0, 3) + "_M" + normal.ResourceId.UniqueName.Substring(3))
                                    throw new InvalidOperationException("Unexpected Master scenario mapping for " + wanted);
                                verified.Add(Id(g.Scenario));
                            }
                            Scenario scenario = config.Scenarios.Find(delegate(Scenario x) { return x.ResourceId.Id == Id(g.Scenario); });
                            b.Append(prefix).Append("scenario\t").Append(Id(g.Scenario)).Append('\t').Append(scenario == null ? "" : Clean(scenario.ResourceId.UniqueName))
                                .Append('\t').Append(Title(config, g.TitleAwarded)).Append('\t').Append(Title(config, g.MasterTitleAwarded))
                                .Append('\t').Append(Outcomes(g.RequiredOutcomes)).Append("\t\t\t\t").Append(Id(g.MasterScenario)).Append('\n'); count++;
                        }
                        foreach (StoryDialogueModal modal in new StoryDialogueModal[] { e.IntroModal, e.EndingModal })
                        {
                            if (modal == null) continue;
                            string kind = Object.ReferenceEquals(modal, e.IntroModal) ? "intro" : "ending";
                            foreach (StoryDialogueData dialogue in modal.DialogueDataEvents)
                                b.Append(prefix).Append(kind).Append("\t0\t").Append(Clean(Text(dialogue.DialogueTextKey))).Append("\t\t\t\t")
                                    .Append(Option(dialogue.Option1)).Append('\t').Append(Option(dialogue.Option2)).Append('\t').Append(Option(dialogue.Option3)).Append('\n');
                        }
                    }
                if (count == 0) throw new InvalidOperationException("Installed campaign scenarios are absent.");
                if (verified.Count != expected.Count) throw new InvalidOperationException("One or more restored story-title references differ from installed campaign data.");
                File.WriteAllText(Path.Combine(output, "story-branch-evidence.tsv"), b.ToString());
                File.WriteAllText(Path.Combine(output, "story-master-reward-evidence.tsv"), rewards.ToString());
                OfflineTitles.Publish(config, Path.Combine(output, "story-title-goals-evidence.tsv"));
            });
        }
        private static int Id(UniqueResourceReference r) { return r == null ? 0 : r.resourceId; }
        private static string Title(GameConfiguration c, UniqueResourceReference r)
        { TitleData t = c.Titles.Find(delegate(TitleData x) { return x != null && x.ResourceId.Id == Id(r); }); return t == null ? "" : Clean(t.ResourceId.UniqueName); }
        private static string Outcomes(List<ChapterEventOutcome> outcomes)
        { List<string> a = new List<string>(); if (outcomes != null) foreach (ChapterEventOutcome x in outcomes) a.Add(Id(x.ChapterEvent) + ":" + x.ModalType + ":" + x.ModalChoiceIndex + ":" + Clean(x.DisplayName)); return string.Join(" | ", a.ToArray()); }
        private static string Option(GatedStoryDialogueOption o)
        { return o == null ? "" : Clean(Text(o.ButtonTextKey)) + " / " + Clean(Text(o.ButtonDescriptionKey)) + " [" + Outcomes(o.RequiredOutcomes) + "]"; }
        private static string Text(LocalizedStringKey k)
        { string raw = k.RawKey; return LocalizationManager.Instance == null || string.IsNullOrEmpty(raw) ? raw : LocalizationManager.Instance.GetText(raw); }
        private static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
