using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SPTESL;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Daily quests. Quest.Goal is NonSerialized in the shipped configuration, but the native
    // counting survives (ActiveQuestUtilities.ExecuteCardQuestEvent from native actions,
    // WinGame/PlayGame from Simulation.EndGame, plus the play/summon/attack/destroy calls restored
    // in DecisionRestoration). Goals are reconstructed from each quest's localized game text
    // ("Summon 15 Endurance creatures") into native QuestGoal objects; quests whose text is not
    // understood are not offered (listed in exports/native-quest-goals.tsv).
    // Rotation (reconstruction): one new daily quest per day while fewer than 3 are active, one
    // skip per day. Rewards: DQ_<n>_Gold → gold, cardpack_<set>_quest_reward → one pack.
    public static class OfflineQuestService
    {
        public const int MaxActive = 3;
        private static volatile List<Quest> offerable = new List<Quest>();
        private static volatile Dictionary<int, Quest> byId = new Dictionary<int, Quest>();
        private static readonly System.Random random = new System.Random();

        // Main thread, once the configuration and localization have loaded.
        public static void Publish(GameConfiguration config, string reportPath)
        {
            List<Quest> ok = new List<Quest>();
            Dictionary<int, Quest> ids = new Dictionary<int, Quest>();
            StringBuilder report = new StringBuilder("quest\ttext\treward\tsupported\tgoal\n");
            foreach (Quest quest in config.Quests)
            {
                if (quest == null || !quest.Production || quest.QuestType != QuestType.Daily) continue;
                string text = Text(quest.Ui.GameText);
                QuestGoal goal = Parse(text);
                bool rewardKnown = RewardOf(quest.RewardBundle) != null;
                report.Append(quest.ResourceId.UniqueName).Append('\t').Append(text).Append('\t').Append(quest.RewardBundle).Append('\t')
                    .Append(goal != null && rewardKnown).Append('\t').Append(goal == null ? "" : goal.Type + " " + goal.Value).Append('\n');
                if (goal == null || !rewardKnown) continue;
                quest.Goal = goal;
                ok.Add(quest); ids[quest.ResourceId.Id] = quest;
            }
            offerable = ok; byId = ids;
            File.WriteAllText(reportPath, report.ToString());
            Debug.Log("OFFLINE QUESTS: " + ok.Count + " daily quests offerable.");
        }

        private static string Text(LocalizedStringKey key)
        {
            string raw = key.RawKey;
            string text = string.IsNullOrEmpty(raw) || LocalizationManager.Instance == null ? null : LocalizationManager.Instance.GetText(raw);
            return (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Trim();
        }

        // ---- goal reconstruction ------------------------------------------------------------
        private static readonly string[] Colors = { "Strength", "Intelligence", "Willpower", "Agility", "Endurance" };

        public static QuestGoal Parse(string text)
        {
            string t = text.Trim().TrimEnd('.');
            List<CardSubtype> colors = new List<CardSubtype>();
            foreach (Match m in Regex.Matches(t, @"<sprite name=icon(\w+)>"))
            { CardSubtype c; if (TrySubtype(m.Groups[1].Value, out c)) colors.Add(c); }
            t = Regex.Replace(t, @"<[^>]+>", "").Trim().TrimEnd(':').Trim();
            string l = t.ToLowerInvariant();
            Match x;
            if ((x = Regex.Match(l, @"^win (\d+) games? with three-attribute decks$")).Success) { QuestGoal g = Goal(QuestGoalType.WinGame, x); g.WithThreeAttributes = true; return g; }
            if ((x = Regex.Match(l, @"^win (\d+) games?(?: with)?:?$")).Success && colors.Count > 0) { QuestGoal g = Goal(QuestGoalType.WinGame, x); g.DeckAttributes.AddRange(colors); return g; }
            if ((x = Regex.Match(l, @"^win (\d+) games?$")).Success) return Goal(QuestGoalType.WinGame, x);
            if ((x = Regex.Match(l, @"^play (\d+) games?$")).Success) return Goal(QuestGoalType.PlayGame, x);
            if ((x = Regex.Match(l, @"^summon (\d+) creatures to the shadow lane$")).Success) return Goal(QuestGoalType.CoverCount, x); // Shadow lane grants Cover (native AddCover counts)
            if ((x = Regex.Match(l, @"^summon (\d+) creatures waxed or waned$")).Success) return Goal(QuestGoalType.WaxWaneTriggerCount, x);
            if ((x = Regex.Match(l, @"^play (\d+) cards as prophecies$")).Success) return Goal(QuestGoalType.ProphecySummonCount, x);
            if ((x = Regex.Match(l, @"^exalt (\d+) cards$")).Success) return Goal(QuestGoalType.ExaltCount, x);
            if ((x = Regex.Match(l, @"^betray (\d+) creatures$")).Success) return Goal(QuestGoalType.BetrayCount, x);
            if ((x = Regex.Match(l, @"^use (\d+) plot abilities$")).Success) return Goal(QuestGoalType.PlotCount, x);
            if ((x = Regex.Match(l, @"^activate expertise (\d+) times$")).Success) return Goal(QuestGoalType.ExpertiseCount, x);
            if ((x = Regex.Match(l, @"^draw (\d+) extra cards$")).Success) return Goal(QuestGoalType.DrawExtraCard, x);
            if (l == "summon a god") { QuestGoal g = new QuestGoal { Type = QuestGoalType.SummonCount, Value = 1 }; g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.God)); return g; }
            if ((x = Regex.Match(l, @"^play (\d+) items with mobilize$")).Success)
            { QuestGoal g = Goal(QuestGoalType.PlayCount, x); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Item)); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.Mobilize, CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, 1)); return g; }
            if ((x = Regex.Match(l, @"^play (\d+) actions with empower$")).Success)
            { QuestGoal g = Goal(QuestGoalType.PlayCount, x); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Spell)); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.HasEmpower, CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, 1)); return g; }
            if ((x = Regex.Match(l, @"^destroy (\d+) enemy supports$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.Support)); g.CardRequirements.Add(Enemy()); return g; }
            if ((x = Regex.Match(l, @"^destroy (\d+) enemy creatures with cover$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Attr(CardRole.EventTarget, AttributeType.Cover, CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, 1)); g.CardRequirements.Add(Enemy()); return g; }
            if ((x = Regex.Match(l, @"^summon (\d+) (.+)$")).Success) return Summon(x);
            if ((x = Regex.Match(l, @"^use (\d+) (summon|slay|last gasp|pilfer) abilities$")).Success)
                return Goal(x.Groups[2].Value == "summon" ? QuestGoalType.SummonTriggerCount : x.Groups[2].Value == "slay" ? QuestGoalType.SlayCount
                    : x.Groups[2].Value == "pilfer" ? QuestGoalType.PilferCount : QuestGoalType.LastGaspCount, x);
            if ((x = Regex.Match(l, @"^play (\d+) (actions|shouts|support cards|items)$")).Success)
            {
                QuestGoal g = Goal(QuestGoalType.PlayCount, x);
                string kind = x.Groups[2].Value;
                g.CardRequirements.Add(Is(CardRole.Self, kind == "actions" ? CardSubtype.Spell : kind == "shouts" ? CardSubtype.Shout : kind == "items" ? CardSubtype.Item : CardSubtype.Support));
                return g;
            }
            if ((x = Regex.Match(l, @"^play (\d+) cards that cost (\d+) or more$")).Success)
            { QuestGoal g = Goal(QuestGoalType.PlayCount, x); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.ManaCost, CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, int.Parse(x.Groups[2].Value))); return g; }
            if ((x = Regex.Match(l, @"^play (\d+) (\d+)-cost cards$")).Success)
            { QuestGoal g = Goal(QuestGoalType.PlayCount, x); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.ManaCost, CardConditionals.AttributeValue.ComparisonType.EqualTo, int.Parse(x.Groups[2].Value))); return g; }
            if ((x = Regex.Match(l, @"^attack your opponent (\d+) times$")).Success)
            { QuestGoal g = Goal(QuestGoalType.AttackCount, x); g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.Avatar)); return g; }
            if ((x = Regex.Match(l, @"^attack and destroy (\d+) creatures(?: with (\w+))?$")).Success)
            {
                QuestGoal g = Goal(QuestGoalType.AttackAndKilledCount, x);
                if (x.Groups[2].Success) { CardSubtype s; if (!TrySubtype(Singular(x.Groups[2].Value), out s)) return null; g.CardRequirements.Add(Is(CardRole.Self, s)); }
                return g;
            }
            if ((x = Regex.Match(l, @"^silence (\d+) creatures$")).Success) return Goal(QuestGoalType.SilenceCount, x);
            if ((x = Regex.Match(l, @"^shackle (\d+) enemy creatures$")).Success) { QuestGoal g = Goal(QuestGoalType.FreezeCount, x); g.CardRequirements.Add(Enemy()); return g; }
            if ((x = Regex.Match(l, @"^heal (\d+) creatures$")).Success) { QuestGoal g = Goal(QuestGoalType.HealCount, x); g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.Creature)); return g; }
            if ((x = Regex.Match(l, @"^gain (\d+) health using drain creatures$")).Success) return Goal(QuestGoalType.HealFromDrainValue, x);
            if ((x = Regex.Match(l, @"^gain (\d+) health$")).Success) { QuestGoal g = Goal(QuestGoalType.HealValue, x); g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.Avatar)); return g; }
            if ((x = Regex.Match(l, @"^equip (\d+) items$")).Success) return Goal(QuestGoalType.EquipCount, x);
            if ((x = Regex.Match(l, @"^boost your max magicka (\d+) times$")).Success) return Goal(QuestGoalType.MaxMagickaBuffCount, x);
            if ((x = Regex.Match(l, @"^reduce the cost of (\d+) cards$")).Success) return Goal(QuestGoalType.ReduceMagickaCostCount, x);
            if ((x = Regex.Match(l, @"^rally (\d+) creatures$")).Success) return Goal(QuestGoalType.RallyCount, x);
            if ((x = Regex.Match(l, @"^prevent damage with ward (\d+) times$")).Success) return Goal(QuestGoalType.WardDamagePreventCount, x);
            if ((x = Regex.Match(l, @"^pilfer (\d+) times$")).Success) return Goal(QuestGoalType.PilferCount, x);
            if ((x = Regex.Match(l, @"^pilfer with khajiit (\d+) times$")).Success) { QuestGoal g = Goal(QuestGoalType.PilferCount, x); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Khajiit)); return g; }
            if ((x = Regex.Match(l, @"^unsummon (\d+) creatures$")).Success) return Goal(QuestGoalType.UnsummonCount, x);
            if ((x = Regex.Match(l, @"^move (\d+) creatures$")).Success) return Goal(QuestGoalType.ChangeLaneCount, x);
            if ((x = Regex.Match(l, @"^invade (\d+) times$")).Success) return Goal(QuestGoalType.InvadeCount, x);
            if ((x = Regex.Match(l, @"^give creatures (\d+) random keywords$")).Success) return Goal(QuestGoalType.RandomKeywordCount, x);
            if ((x = Regex.Match(l, @"^give (\d+) creatures cover$")).Success) return Goal(QuestGoalType.CoverCount, x);
            if ((x = Regex.Match(l, @"^consume (\d+) creatures$")).Success) return Goal(QuestGoalType.ConsumeCount, x);
            if ((x = Regex.Match(l, @"^change (\d+) creatures with beast form$")).Success) return Goal(QuestGoalType.BeastFormChangeCount, x);
            if ((x = Regex.Match(l, @"^destroy (\d+) enemy creatures with actions$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Spell)); g.CardRequirements.Add(Enemy()); return g; }
            if ((x = Regex.Match(l, @"^deal (\d+) damage with actions$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DamageValue, x); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Spell)); return g; }
            return null;
        }

        private static QuestGoal Summon(Match x)
        {
            QuestGoal g = Goal(QuestGoalType.SummonCount, x);
            string what = x.Groups[2].Value.Trim();
            Match m;
            if (what == "creatures") return g;
            if ((m = Regex.Match(what, @"^creatures that cost (\d+) or (more|less)$")).Success)
            {
                g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.ManaCost, m.Groups[2].Value == "more" ? CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo
                    : CardConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo, int.Parse(m.Groups[1].Value)));
                return g;
            }
            if ((m = Regex.Match(what, @"^creatures with (\d+) health or more$")).Success)
            { g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.MaxHealth, CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, int.Parse(m.Groups[1].Value))); return g; }
            if ((m = Regex.Match(what, @"^creatures with (guard|charge|breakthrough|lethal|ward|drain|regenerate)$")).Success || (m = Regex.Match(what, @"^(guard|charge|breakthrough|lethal|drain|regenerate) creatures$")).Success)
            { g.CardRequirements.Add(Attr(CardRole.Self, Keyword(m.Groups[1].Value), CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, 1)); return g; }
            if ((m = Regex.Match(what, @"^(strength|intelligence|willpower|agility|endurance) creatures$")).Success)
            { CardSubtype c; TrySubtype(m.Groups[1].Value, out c); g.CardRequirements.Add(Is(CardRole.Self, c)); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Creature)); return g; }
            if (what.Contains(" ")) return null; // lanes, waxed/waned, "while ..." are not reconstructed yet
            CardSubtype tribe;
            if (!TrySubtype(Singular(what), out tribe)) return null;
            g.CardRequirements.Add(Is(CardRole.Self, tribe));
            return g;
        }

        private static AttributeType Keyword(string word)
        {
            switch (word)
            {
                case "guard": return AttributeType.Taunt;
                case "lethal": return AttributeType.Deathtouch;
                default: return (AttributeType)Enum.Parse(typeof(AttributeType), char.ToUpperInvariant(word[0]) + word.Substring(1));
            }
        }

        private static string Singular(string word)
        {
            if (word == "dwemer" || word == "daedra" || word == "undead" || word == "khajiit") return word;
            if (word.EndsWith("s")) return word.Substring(0, word.Length - 1);
            return word;
        }

        private static bool TrySubtype(string name, out CardSubtype subtype)
        {
            subtype = CardSubtype.None;
            foreach (string n in Enum.GetNames(typeof(CardSubtype)))
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { subtype = (CardSubtype)Enum.Parse(typeof(CardSubtype), n); return true; }
            return false;
        }

        private static QuestGoal Goal(QuestGoalType type, Match x)
        { return new QuestGoal { Type = type, Value = int.Parse(x.Groups[1].Value) }; }

        private static ICardRequirement Is(CardRole role, CardSubtype subtype)
        {
            CardRequirements.SubtypeRequirement r = new CardRequirements.SubtypeRequirement();
            r.LhsCard = role; r.Conditional = new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = subtype };
            return r;
        }

        private static ICardRequirement Attr(CardRole role, AttributeType attribute, CardConditionals.AttributeValue.ComparisonType compare, int value)
        {
            CardRequirements.AttributeValue r = new CardRequirements.AttributeValue();
            r.LhsCard = role; r.Conditional = new CardConditionals.AttributeValue { AttributeName = attribute, Is = compare, Value = new ConditionalValueData(value) };
            return r;
        }

        private static ICardRequirement Enemy()
        {
            CardRequirements.RelationshipRequirement r = new CardRequirements.RelationshipRequirement();
            r.LhsCard = CardRole.EventTarget; r.Conditional = new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Enemies };
            return r;
        }

        // ---- rewards ---------------------------------------------------------------------------
        public static string RewardOf(string bundle)
        {
            Match m;
            if (bundle == null) return null;
            if ((m = Regex.Match(bundle, @"^DQ_(\d+)_Gold$")).Success) return "gold:" + m.Groups[1].Value;
            if ((m = Regex.Match(bundle, @"^cardpack_(\w+?)_quest_reward$", RegexOptions.IgnoreCase)).Success) return "pack:" + m.Groups[1].Value.ToLowerInvariant();
            return null;
        }

        // ---- service -----------------------------------------------------------------------------
        public static void Register(OfflineLocalServices services, OfflinePlayerState state)
        {
            services.Register("quests/fetch", delegate {
                state.Mutate(Rotate);
                Quests.QuestFetchResponse response = new Quests.QuestFetchResponse();
                response.resultCode = ResultCode.Ok; response.quests = state.Snapshot().quests; response.choice = new List<Quests.QuestChoiceStruct>();
                return JsonUtility.ToJson(response); });
            services.Register("quests/setSeen", delegate {
                // Completed quests stay listed until seen, so the client's quest window shows their completion.
                state.Mutate(delegate(OfflinePlayerStateData d) {
                    foreach (Quests.QuestDescription q in d.quests) q.numAchievedSeen = q.numAchieved;
                    d.quests.RemoveAll(delegate(Quests.QuestDescription q) { return q.numRequired > 0 && q.numAchieved >= q.numRequired; }); });
                return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.Ok }); });
            services.Register("quests/skip", delegate(string m, string p, string body) {
                Quests.SkipRequest request = JsonUtility.FromJson<Quests.SkipRequest>(body);
                bool ok = false;
                if (request != null) state.Mutate(delegate(OfflinePlayerStateData d) {
                    string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    if (d.questSkipDay == today) return;
                    int index = d.quests.FindIndex(delegate(Quests.QuestDescription q) { return q.typeNameHash == request.questTypeHash; });
                    if (index < 0) return;
                    Quests.QuestDescription replacement = NewQuest(d);
                    if (replacement == null) return;
                    d.quests[index] = replacement; d.questSkipDay = today; ok = true; });
                return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ok ? ResultCode.Ok : ResultCode.ClientHttpErrorBadRequest }); });
        }

        // One new daily quest per UTC day while fewer than MaxActive are active.
        private static void Rotate(OfflinePlayerStateData d)
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            // A quest saved by name only (hand-made fixtures) gets its installed id.
            foreach (Quests.QuestDescription q in d.quests)
                if (q.typeNameHash == 0)
                {
                    Quest named = offerable.Find(delegate(Quest x) { return x.ResourceId.UniqueName == q.typeName; });
                    if (named != null) q.typeNameHash = named.ResourceId.Id;
                }
            if (d.questDay == today || d.quests.Count >= MaxActive || offerable.Count == 0) return;
            Quests.QuestDescription quest = NewQuest(d);
            if (quest != null) d.quests.Add(quest);
            d.questDay = today;
        }

        private static Quests.QuestDescription NewQuest(OfflinePlayerStateData d)
        {
            List<Quest> pool = offerable.FindAll(delegate(Quest q) { return !d.quests.Exists(delegate(Quests.QuestDescription a) { return a.typeNameHash == q.ResourceId.Id; }); });
            if (pool.Count == 0) return null;
            Quest quest = pool[random.Next(pool.Count)];
            string reward = RewardOf(quest.RewardBundle);
            Quests.QuestDescription desc = new Quests.QuestDescription();
            desc.typeName = quest.ResourceId.UniqueName; desc.typeNameHash = quest.ResourceId.Id; desc.questType = "Daily";
            desc.mechanicType = quest.Goal.Type.ToString(); desc.numRequired = quest.Goal.Value; desc.isSkippable = true;
            if (reward.StartsWith("gold:")) desc.numGold = int.Parse(reward.Substring(5));
            else { desc.numCardPacks = 1; desc.cardPackType = "cardpack_" + reward.Substring(5); }
            return desc;
        }

        // Match start (main thread): the human's active quests as native ActiveQuests.
        public static void AddActiveQuests(Simulation sim, OfflinePlayerStateData d)
        {
            PlayerState human = sim.Players.Find(delegate(PlayerState p) { return p.PlayerType == (int)PlayerType.Normal; });
            if (human == null) return;
            foreach (Quests.QuestDescription q in d.quests)
            {
                Quest quest;
                if (byId.TryGetValue(q.typeNameHash, out quest)) sim.ActiveQuests.Add(new ActiveQuest { Owner = human.NetId, Quest = quest });
            }
            OfflineTitles.AddActiveQuests(sim, d, human);
        }

        // Match end: add native progress; completed quests grant their reward (Quest source).
        public static void RecordProgress(Simulation sim, OfflinePlayerStateData d)
        {
            foreach (ActiveQuest active in sim.ActiveQuests)
            {
                if (active.Quest.QuestType == QuestType.Title) continue; // title trackers (OfflineTitles)
                Quests.QuestDescription q = d.quests.Find(delegate(Quests.QuestDescription x) { return x.typeNameHash == active.Quest.ResourceId.Id; });
                // Rewarded exactly once per quest instance: a completed quest stays listed until setSeen, so one that was
                // already complete before this match is skipped (a day-keyed marker double-granted or never granted).
                if (q == null || q.numAchieved >= q.numRequired) continue;
                q.numAchieved = Math.Min(q.numRequired, q.numAchieved + active.GetValueAddedDuringMatch());
                if (q.numAchieved < q.numRequired) continue;
                Rewards.RewardDescription reward = new Rewards.RewardDescription();
                reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.Quest;
                string key = q.numGold > 0 ? "arena_gold_" + q.numGold : "quest_pack_" + OfflinePackService.RewardBundle(q.cardPackType);
                reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                    rewardPassIndex = 0, bundleKey = key, bundleCount = 1, gold = q.numGold, rarity = Rewards.RewardRarity.Common,
                    rewardContainerType = Rewards.RewardContainer.None } };
                d.rewards.Add(reward);
                d.completedQuests.Add(q.typeName + "@" + d.questDay);
                OfflineTitles.DailyCompleted(d, active.Quest.QuestGiverType);
            }
            OfflineTitles.RecordProgress(sim, d);
        }
    }
}
