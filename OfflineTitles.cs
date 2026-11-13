using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SparkleServices;
using SPTESL;

namespace OfflineEngine
{
    // Title achievements. The shipped Title quests carry no goal (Quest.Goal is NonSerialized), but each installed
    // TitleData.Description states its condition ("Win 500 games.", "Summon 300 Orcs.", "Complete 10 Mages Guild Quests.").
    // Reconstruction:
    //  - lifetime counters run as native ActiveQuests every match (synthetic Quest objects with parsed goals);
    //  - "Complete N <guild> Quests" counts completed daily quests by QuestGiverType;
    //  - "Complete A, B, and C" titles follow ownership of the named titles;
    //  - "Starting title." titles are owned from the start.
    //  - per-game feats and story wins: OfflineTitleFeats; arena undefeated runs, set collection and supporter titles here
    //    (sources and the titles still locked: data/TITLE-STORY-SOURCE.md).
    public static class OfflineTitles
    {
        private class Tracker { public string Key; public Quest Quest; }
        private class Guild { public string Key; public QuestGiverType Giver; public int Count; }
        private class Meta { public string Key; public List<string> Required; }
        private class SetGoal { public string Key; public List<int> Cards = new List<int>(); public List<int> Need = new List<int>(); }

        private static volatile List<Tracker> trackers = new List<Tracker>();
        private static volatile List<Guild> guilds = new List<Guild>();
        private static volatile List<Meta> metas = new List<Meta>();
        private static volatile List<string> starting = new List<string>();
        // "... in one game" goals: judged on one match's added value, never accumulated.
        private static volatile List<Tracker> singleGame = new List<Tracker>();
        private static volatile List<SetGoal> setGoals = new List<SetGoal>();
        private static volatile Dictionary<int, List<SetGoal>> setGoalsByCard = new Dictionary<int, List<SetGoal>>();
        // Arena titles: key, mode, undefeated runs needed. Chaos Arena has no offline mode yet.
        private static readonly string[][] ArenaTitles = {
            new[] { "title_the_champion_of_the_arena", "versus", "1" },
            new[] { "title_the_master_of_the_arena", "versus", "5" },
            new[] { "title_the_master_of_prowess", "solo", "5" } };
        // ActiveQuest -> title key for the running match (main thread).
        private static readonly Dictionary<ActiveQuest, string> live = new Dictionary<ActiveQuest, string>();

        // Starting titles, plus supporter/bundle titles whose original grant was a purchase or an early-access flag
        // (offline access must not depend on purchases: BACKLOG 2026-10-13). Reborn tester/developer titles stay locked.
        public static bool IsStarting(string key) { return starting.Contains(key); }

        // Reported kind of a title after Publish (counter, guild, meta, starting, supporter, feat, story, arena, collection,
        // single-game, unsupported).
        private static volatile Dictionary<string, string> kinds = new Dictionary<string, string>();
        public static string Kind(string key) { string v; return kinds.TryGetValue(key, out v) ? v : null; }

        public static void Publish(GameConfiguration config, string reportPath)
        {
            PublishCards(config);
            List<Tracker> t = new List<Tracker>(); List<Guild> g = new List<Guild>(); List<Meta> m = new List<Meta>(); List<string> s = new List<string>();
            List<Tracker> one = new List<Tracker>(); List<SetGoal> sets = new List<SetGoal>(); Dictionary<string, string> k = new Dictionary<string, string>();
            StringBuilder report = new StringBuilder("key\tdescription\tkind\tgoal\n");
            foreach (TitleData title in config.Titles)
            {
                if (title == null || string.IsNullOrEmpty(title.ResourceId.UniqueName)) continue;
                string key = title.ResourceId.UniqueName, desc = Text(title.Description), kind = "unsupported", goalText = "";
                string l = Normalize(desc);
                Match x; QuestGiverType giver;
                SetGoal sg;
                if (l == "starting title") { s.Add(key); kind = "starting"; }
                else if (IsSupporterTitle(key, l)) { s.Add(key); kind = "supporter"; }
                else if (OfflineTitleFeats.Describe(key) != null) { goalText = OfflineTitleFeats.Describe(key); kind = goalText.StartsWith("won ", StringComparison.Ordinal) ? "story" : "feat"; }
                else if (ArenaGoal(key) != null) { kind = "arena"; goalText = ArenaGoal(key)[1] + " undefeated " + ArenaGoal(key)[2]; }
                else if ((x = Regex.Match(l, @"^collect 100% of the (.+) set$")).Success && (sg = SetFor(config, key, x.Groups[1].Value)) != null)
                { sets.Add(sg); kind = "collection"; goalText = sg.Cards.Count + " cards"; }
                else if (l.EndsWith(" in one game", StringComparison.Ordinal) && ParseGoal(l.Substring(0, l.Length - 12)) != null)
                {
                    QuestGoal goal = ParseGoal(l.Substring(0, l.Length - 12));
                    one.Add(new Tracker { Key = key, Quest = new Quest { Goal = goal, QuestType = QuestType.Title } }); kind = "single-game"; goalText = goal.Type + " " + goal.Value;
                }
                else if ((x = Regex.Match(l, @"^complete (\d+) (.+) quests$")).Success && TryGiver(x.Groups[2].Value, out giver))
                { g.Add(new Guild { Key = key, Giver = giver, Count = int.Parse(x.Groups[1].Value) }); kind = "guild"; goalText = giver + " " + x.Groups[1].Value; }
                else if (l.StartsWith("complete ", StringComparison.Ordinal) && MetaRequired(l.Substring(9)) != null)
                { m.Add(new Meta { Key = key, Required = MetaRequired(l.Substring(9)) }); kind = "meta"; goalText = string.Join("+", m[m.Count - 1].Required.ToArray()); }
                else if (CardEventGoal(key) != null)
                {
                    QuestGoal goal = CardEventGoal(key);
                    t.Add(new Tracker { Key = key, Quest = new Quest { Goal = goal, QuestType = QuestType.Title } }); kind = "counter"; goalText = goal.Type + " " + goal.Value;
                }
                else
                {
                    QuestGoal goal = ParseGoal(l);
                    if (goal != null)
                    {
                        Quest quest = new Quest { Goal = goal, QuestType = QuestType.Title };
                        t.Add(new Tracker { Key = key, Quest = quest }); kind = "counter"; goalText = goal.Type + " " + goal.Value;
                    }
                }
                k[key] = kind;
                report.Append(key).Append('\t').Append(desc).Append('\t').Append(kind).Append('\t').Append(goalText).Append('\n');
            }
            trackers = t; guilds = g; metas = m; starting = s; singleGame = one; setGoals = sets; kinds = k;
            Dictionary<int, List<SetGoal>> byCard = new Dictionary<int, List<SetGoal>>();
            foreach (SetGoal goal in sets)
                foreach (int id in goal.Cards) { List<SetGoal> list; if (!byCard.TryGetValue(id, out list)) byCard[id] = list = new List<SetGoal>(); list.Add(goal); }
            setGoalsByCard = byCard;
            if (reportPath != null) System.IO.File.WriteAllText(reportPath, report.ToString());
            UnityEngine.Debug.Log("OFFLINE TITLES: " + t.Count + " counters, " + g.Count + " guild, " + m.Count + " meta, " + s.Count + " starting/supporter, "
                + one.Count + " single-game, " + sets.Count + " collection.");
        }

        // Titles whose card raises its own quest event (CardActions.IncrementQuestProgress in the original card graphs). The card
        // checks the condition itself (5-cost, 10 items at once, 100 damage at once, a named target...), so the goal is the
        // description's count, or 1 for "once" titles. The installed title quests ship without goals (2026-11-13).
        private static readonly Dictionary<string, KeyValuePair<QuestGoalType, int>> CardEvents = new Dictionary<string, KeyValuePair<QuestGoalType, int>> {
            { "title_the_archmage", new KeyValuePair<QuestGoalType, int>(QuestGoalType.FiveCostWinterhold, 1) },
            { "title_the_dawnbreaker", new KeyValuePair<QuestGoalType, int>(QuestGoalType.Dawnbreaker30Undead, 30) },
            { "title_the_director_of_despair", new KeyValuePair<QuestGoalType, int>(QuestGoalType.DirectorOfDespairCount, 50) },
            { "title_the_engineer", new KeyValuePair<QuestGoalType, int>(QuestGoalType.EngineerFactotum, 1) },
            { "title_the_forger", new KeyValuePair<QuestGoalType, int>(QuestGoalType.WrothgarForgerCount, 30) },
            { "title_the_genius_gardener", new KeyValuePair<QuestGoalType, int>(QuestGoalType.GeniusGardenerCount, 50) },
            { "title_the_golden", new KeyValuePair<QuestGoalType, int>(QuestGoalType.GoldenCount, 1) },
            { "title_the_hortator", new KeyValuePair<QuestGoalType, int>(QuestGoalType.UniteTheHousesCount, 1) },
            { "title_the_king_of_orsinium", new KeyValuePair<QuestGoalType, int>(QuestGoalType.KingOfOrsiniumCount, 30) },
            { "title_the_mastermind", new KeyValuePair<QuestGoalType, int>(QuestGoalType.MastermindCount, 50) },
            { "title_the_red_mountain", new KeyValuePair<QuestGoalType, int>(QuestGoalType.RedYearDamageCount, 1) },
            { "title_the_skirmisher", new KeyValuePair<QuestGoalType, int>(QuestGoalType.SkirmisherDamageCount, 30) },
            { "title_the_thief_of_dreams", new KeyValuePair<QuestGoalType, int>(QuestGoalType.ThiefOfDreamsCount, 10) },
            { "title_the_true_high_king", new KeyValuePair<QuestGoalType, int>(QuestGoalType.TrueHighKingCount, 50) },
            { "title_thieves_guild_recruiter", new KeyValuePair<QuestGoalType, int>(QuestGoalType.ThievesGuildRecruiterCount, 30) },
            { "title_IOM_the_chamberlain", new KeyValuePair<QuestGoalType, int>(QuestGoalType.HaskillActivationCount, 20) },
            { "title_IOM_the_transmogrifier", new KeyValuePair<QuestGoalType, int>(QuestGoalType.TransmogrifierCount, 10) },
            { "title_IOM_the_sadist", new KeyValuePair<QuestGoalType, int>(QuestGoalType.TorturerActivationCount, 30) },
            { "title_IOM_the_heretic", new KeyValuePair<QuestGoalType, int>(QuestGoalType.HereticConjurerTransformCount, 50) },
            { "title_AW_nirns_doom", new KeyValuePair<QuestGoalType, int>(QuestGoalType.NirnsDoomCount, 1) },
            { "title_AW_the_telekinetic", new KeyValuePair<QuestGoalType, int>(QuestGoalType.TelekineticCount, 1) },
            { "title_MoE_MoonCatcher", new KeyValuePair<QuestGoalType, int>(QuestGoalType.MoonCatcherCount, 100) },
            { "title_MoE_TheDragonSeeker", new KeyValuePair<QuestGoalType, int>(QuestGoalType.DragonSeekerCount, 20) },
            { "title_MoE_TheUsurper", new KeyValuePair<QuestGoalType, int>(QuestGoalType.UsurperCount, 1) },
            { "title_MoE_TheShriven", new KeyValuePair<QuestGoalType, int>(QuestGoalType.ShrivenCount, 1) },
            { "title_Jaws_ChosenOfAkatosh", new KeyValuePair<QuestGoalType, int>(QuestGoalType.ChosenOfAkatoshCount, 1) },
            { "title_Jaws_TheLearned", new KeyValuePair<QuestGoalType, int>(QuestGoalType.TheLearnedCount, 50) },
            // Raised by the installed shared Avatar / Creature definitions ("prodigy title check", "Pocket Emptier check", "Perfect check").
            { "title_AW_the_arcane_prodigy", new KeyValuePair<QuestGoalType, int>(QuestGoalType.ProdigyCount, 1) },
            { "title_MoE_ThePocketEmptier", new KeyValuePair<QuestGoalType, int>(QuestGoalType.PocketEmptierCount, 1) },
            { "title_Jaws_Perfect", new KeyValuePair<QuestGoalType, int>(QuestGoalType.PerfectCount, 1) },
            // Dedicated events of the Isle of Madness lanes and scenarios (raised by their lane / scenario rules, not by cards).
            { "title_IOM_duke_of_mania", new KeyValuePair<QuestGoalType, int>(QuestGoalType.ManiaActivationCount, 40) },
            { "title_IOM_duchess_of_dementia", new KeyValuePair<QuestGoalType, int>(QuestGoalType.DementiaActivationCount, 40) },
            { "title_the_risen", new KeyValuePair<QuestGoalType, int>(QuestGoalType.TheRisenCount, 1) },
            { "title_IOM_the_strategist", new KeyValuePair<QuestGoalType, int>(QuestGoalType.TheStrategistCount, 1) },
        };

        public static QuestGoal CardEventGoal(string key)
        {
            // Mankar Camoran's own Invades: the native InvadeCount event names the invading card.
            if (key == "title_Jaws_ParadiseChosen") return WithCard(new QuestGoal { Type = QuestGoalType.InvadeCount, Value = 50 }, CardRole.Self, "Mankar Camoran");
            KeyValuePair<QuestGoalType, int> e;
            return CardEvents.TryGetValue(key, out e) ? new QuestGoal { Type = e.Key, Value = e.Value } : null;
        }

        public static IEnumerable<string> CardEventTitles() { List<string> k = new List<string>(CardEvents.Keys); k.Add("title_Jaws_ParadiseChosen"); return k; }

        // ---- goals --------------------------------------------------------------------------------
        public static string Normalize(string text)
        {
            string l = Regex.Replace(text ?? "", @"<[^>]+>", "").Trim().TrimEnd('.').Trim().ToLowerInvariant();
            return Regex.Replace(l, @"(\d),(\d)", "$1$2");
        }

        private static readonly string[] Colors = { "strength", "intelligence", "willpower", "agility", "endurance" };

        public static QuestGoal ParseGoal(string l)
        {
            Match x;
            if ((x = Regex.Match(l, @"^win (\d+) games with an? (\w+) deck$")).Success && Array.IndexOf(Colors, x.Groups[2].Value) >= 0)
            { QuestGoal g = Goal(QuestGoalType.WinGame, x); g.DeckAttributes.Add(Subtype(x.Groups[2].Value)); return g; }
            if ((x = Regex.Match(l, @"^win (\d+) games with an? (\w+), (\w+),? and (\w+) deck?$")).Success)
            {
                QuestGoal g = Goal(QuestGoalType.WinGame, x);
                for (int i = 2; i <= 4; i++) { if (Array.IndexOf(Colors, x.Groups[i].Value) < 0) return null; g.DeckAttributes.Add(Subtype(x.Groups[i].Value)); }
                return g;
            }
            if (l == "win a game with an all-premium deck") return new QuestGoal { Type = QuestGoalType.WinGame, Value = 1, WithAllPremiums = true };
            if ((x = Regex.Match(l, @"^play (\d+) blood magic spells$" )).Success)
            { QuestGoal g = Goal(QuestGoalType.PlayCount, x); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Spell)); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Blood_Magic)); return g; }
            if ((x = Regex.Match(l, @"^draw (\d+) blood magic spells with aundae clan sorcerer$" )).Success)
                return WithCard(Goal(QuestGoalType.AundaeDrawCount, x), CardRole.Self, "Aundae Clan Sorcerer");
            if (l == "give 50 wards with elixir of deflection") return WithCard(new QuestGoal { Type = QuestGoalType.DeflectorCount, Value = 50 }, CardRole.Self, "Elixir of Deflection");
            if (l == "give 50 creatures guard with elixir of the defender") return WithCard(new QuestGoal { Type = QuestGoalType.DefenderCount, Value = 50 }, CardRole.Self, "Elixir of the Defender");
            if (l == "give 50 creatures cover with elixir of light feet") return WithCard(new QuestGoal { Type = QuestGoalType.LightfootedCount, Value = 50 }, CardRole.Self, "Elixir of Light Feet");
            if (l == "give 100 points of health with elixir of vigor") return WithCard(new QuestGoal { Type = QuestGoalType.VigorousCount, Value = 100 }, CardRole.Self, "Elixir of Vigor");
            if (l == "give 50 keywords with mundus stone") return WithCard(new QuestGoal { Type = QuestGoalType.WalkerOfMundusCount, Value = 50 }, CardRole.Self, "Mundus Stone");
            // Card-specific titles on counters the client (or the restored sequences) already raise.
            if ((x = Regex.Match(l, @"^complete (\d+) treasure hunts$")).Success) return Goal(QuestGoalType.TreasureHuntCount, x);
            if (l == "complete 3 or more treasure hunts with one treasure map") return new QuestGoal { Type = QuestGoalType.ThreeHuntsOneMapCount, Value = 1 };
            if (l == "use mecinar to create an abomination with 13 or more power") return new QuestGoal { Type = QuestGoalType.Mecinar13PowerCount, Value = 1 };
            if ((x = Regex.Match(l, @"^summon (\d+) daughters of fyr$")).Success) { QuestGoal g = Goal(QuestGoalType.SummonCount, x); g.CardRequirements.Add(Is(CardRole.Self, CardSubtype.Daughter_Of_Fyr)); return g; }
            if ((x = Regex.Match(l, @"^play the (.+) card$")).Success) return WithCard(new QuestGoal { Type = QuestGoalType.PlayCount, Value = 1 }, CardRole.Self, x.Groups[1].Value);
            if ((x = Regex.Match(l, @"^destroy (\d+) creatures with (.+)$")).Success) { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Enemy()); return WithCard(g, CardRole.Self, x.Groups[2].Value); }
            if ((x = Regex.Match(l, @"^deal (\d+) damage with (.+?)(?:'s ability)?$")).Success && !x.Groups[2].Value.StartsWith("a ")) return WithCard(Goal(QuestGoalType.DamageValue, x), CardRole.Self, x.Groups[2].Value);
            if ((x = Regex.Match(l, @"^drain (\d+) health with (.+)$")).Success) return WithCard(Goal(QuestGoalType.HealFromDrainValue, x), CardRole.Self, x.Groups[2].Value);
            if ((x = Regex.Match(l, @"^move (\d+) creatures with (.+)$")).Success) return WithCard(Goal(QuestGoalType.ChangeLaneCount, x), CardRole.Self, x.Groups[2].Value);
            if ((x = Regex.Match(l, @"^draw (\d+) cards with (.+)$")).Success) return WithCard(Goal(QuestGoalType.DrawExtraCard, x), CardRole.Self, x.Groups[2].Value);
            if ((x = Regex.Match(l, @"^kill (\d+) creatures$")).Success) { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Enemy()); return g; }
            if ((x = Regex.Match(l, @"^kill (\d+) undead and daedra$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Enemy()); g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.MonsterHunterTarget)); return g; }
            if ((x = Regex.Match(l, @"^kill (\d+) dragons$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Enemy()); g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.Dragon)); return g; }
            // "Kill 10 Slaughterfish" falls through to the named-card templates at the end.
            if ((x = Regex.Match(l, @"^destroy (\d+) daedra$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Enemy()); g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.Daedra)); return g; }
            if ((x = Regex.Match(l, @"^use lethal creatures to kill (\d+) creatures$")).Success)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Enemy()); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.Deathtouch, 1)); return g; }
            if ((x = Regex.Match(l, @"^deal (\d+) damage$")).Success) return Goal(QuestGoalType.DamageValue, x);
            if ((x = Regex.Match(l, @"^summon (\d+) (dark|high|wood) elves$")).Success)
            {
                QuestGoal g = Goal(QuestGoalType.SummonCount, x);
                g.CardRequirements.Add(Is(CardRole.Self, x.Groups[2].Value == "dark" ? CardSubtype.Dark_Elf : x.Groups[2].Value == "high" ? CardSubtype.High_Elf : CardSubtype.Wood_Elf));
                return g;
            }
            if ((x = Regex.Match(l, @"^summon (\d+) creatures waxed$")).Success) return Goal(QuestGoalType.WaxTriggerCount, x);
            if ((x = Regex.Match(l, @"^summon (\d+) creatures waned$")).Success) return Goal(QuestGoalType.WaneTriggerCount, x);
            if ((x = Regex.Match(l, @"^summon (\d+) creatures with (\d+) power or more$")).Success)
            { QuestGoal g = Goal(QuestGoalType.SummonCount, x); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.Attack, int.Parse(x.Groups[2].Value))); return g; }
            if ((x = Regex.Match(l, @"^pay the exalt cost on (\d+) cards$")).Success) return Goal(QuestGoalType.ExaltCount, x);
            if ((x = Regex.Match(l, @"^trigger plot effects on (\d+) cards$")).Success) return Goal(QuestGoalType.PlotCount, x);
            if ((x = Regex.Match(l, @"^sacrifice (\d+) creatures with betray$")).Success) return Goal(QuestGoalType.BetrayCount, x);
            if ((x = Regex.Match(l, @"^activate expertise effects (\d+) times$")).Success) return Goal(QuestGoalType.ExpertiseCount, x);
            if ((x = Regex.Match(l, @"^bring (\d+) creatures back from the dead$")).Success) return Goal(QuestGoalType.ResurrectionCount, x);
            if ((x = Regex.Match(l, @"^change (\d+) beast form creatures to werewolves$")).Success) return Goal(QuestGoalType.BeastFormChangeCount, x);
            if ((x = Regex.Match(l, @"^play (\d+) guards$")).Success)
            { QuestGoal g = Goal(QuestGoalType.PlayCount, x); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.Taunt, 1)); return g; }
            if ((x = Regex.Match(l, @"^play (\d+) prophecies$")).Success)
            { QuestGoal g = Goal(QuestGoalType.PlayCount, x); g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.Prophecy, 1)); return g; }
            QuestGoal daily = OfflineQuestService.Parse(l);
            if (daily != null) return daily;
            // Last: a named card as the target ("Kill 10 Slaughterfish", "Destroy 10 Oblivion Gates") or the played card.
            if ((x = Regex.Match(l, @"^(?:kill|destroy) (\d+) (.+?)s?$")).Success && CardId(x.Groups[2].Value) != 0)
            { QuestGoal g = Goal(QuestGoalType.DestroyCount, x); g.CardRequirements.Add(Enemy()); return WithCard(g, CardRole.EventTarget, x.Groups[2].Value); }
            if ((x = Regex.Match(l, @"^play (\d+) (.+?)s$")).Success && CardId(x.Groups[2].Value) != 0) return WithCard(Goal(QuestGoalType.PlayCount, x), CardRole.Self, x.Groups[2].Value);
            return null;
        }

        // English card name (lower case) -> collectible card id, built in Publish.
        private static volatile Dictionary<string, int> cardsByName = new Dictionary<string, int>();

        public static void PublishCards(GameConfiguration config)
        {
            Dictionary<string, int> names = new Dictionary<string, int>();
            Dictionary<int, string> byId = new Dictionary<int, string>(); Dictionary<int, bool> uniques = new Dictionary<int, bool>();
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    if (card == null || string.IsNullOrEmpty(card.Name)) continue;
                    string key = card.Name.Trim().ToLowerInvariant();
                    bool collectible = card.Production && !string.IsNullOrEmpty(card.ExportCode);
                    if (!names.ContainsKey(key) || collectible) names[key] = card.ResourceId.Id;
                    byId[card.ResourceId.Id] = card.Name.Trim();
                    if (card.Unique) uniques[card.ResourceId.Id] = true;
                }
            cardsByName = names; namesById = byId; uniqueIds = uniques;
        }

        private static volatile Dictionary<int, string> namesById = new Dictionary<int, string>();
        private static volatile Dictionary<int, bool> uniqueIds = new Dictionary<int, bool>();
        public static string CardName(int id) { string n; return namesById.TryGetValue(id, out n) ? n : null; }
        public static bool IsUniqueCard(int id) { return uniqueIds.ContainsKey(id); }

        public static int CardId(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string key = name.Trim().ToLowerInvariant();
            int id;
            if (cardsByName.TryGetValue(key, out id)) return id;
            if (key.StartsWith("the ") && cardsByName.TryGetValue(key.Substring(4), out id)) return id;
            if (!key.StartsWith("the ") && cardsByName.TryGetValue("the " + key, out id)) return id;
            return 0;
        }

        private static QuestGoal WithCard(QuestGoal goal, CardRole role, string name)
        {
            int id = CardId(name);
            if (id == 0) return null;
            CardRequirements.MatchesCardRequirement r = new CardRequirements.MatchesCardRequirement();
            r.LhsCard = role;
            CardConditionals.MatchesCardResource match = new CardConditionals.MatchesCardResource { Check = CardConditionals.MatchesCardResource.Requirement.Is };
            match.SpecificMatch.SetToSpecificCard(id);
            r.Conditional = match;
            goal.CardRequirements.Add(r);
            return goal;
        }

        // Event goals for OfflineTitleFeats (native ActiveQuest counting at the event, not frame sampling).
        // Damage dealt to an avatar by a card of the given relationship to the quest owner's acting card.
        public static QuestGoal AvatarDamageGoal(bool byEnemy)
        {
            QuestGoal g = new QuestGoal { Type = QuestGoalType.DamageValue, Value = 1 };
            g.CardRequirements.Add(Is(CardRole.EventTarget, CardSubtype.Avatar));
            CardRequirements.RelationshipRequirement r = new CardRequirements.RelationshipRequirement();
            r.LhsCard = CardRole.EventTarget;
            r.Conditional = new CardConditionals.Relationship { OtherCard = CardRole.Self,
                Are = byEnemy ? CardConditionals.Relationship.RelationshipType.Enemies : CardConditionals.Relationship.RelationshipType.Friends };
            g.CardRequirements.Add(r);
            return g;
        }

        // Summoning the named creature with at least this power (requirements judged at the summon).
        public static QuestGoal NamedSummonGoal(string name, int minPower)
        {
            QuestGoal g = WithCard(new QuestGoal { Type = QuestGoalType.SummonCount, Value = 1 }, CardRole.Self, name);
            if (g != null) g.CardRequirements.Add(Attr(CardRole.Self, AttributeType.Attack, minPower));
            return g;
        }

        private static bool TryGiver(string name, out QuestGiverType giver)
        {
            switch (name)
            {
                case "fighters guild": giver = QuestGiverType.Fighters; return true;
                case "mages guild": giver = QuestGiverType.Mages; return true;
                case "thieves guild": giver = QuestGiverType.Thieves; return true;
                case "college of winterhold": giver = QuestGiverType.Winterhold; return true;
                case "companions": giver = QuestGiverType.Companions; return true;
            }
            giver = QuestGiverType.None; return false;
        }

        // "Cunning, Mighty, Nimble, Resolute, and Stalwart" -> title keys; null unless every name resolves.
        private static List<string> MetaRequired(string list)
        {
            string[] parts = Regex.Split(list, @",\s*(?:and\s+)?|\s+and\s+");
            List<string> keys = new List<string>();
            foreach (string raw in parts)
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                string key = OfflineCosmetics.TitleKey(name) ?? OfflineCosmetics.TitleKey("the " + name) ?? OfflineCosmetics.TitleKey("the " + name.Replace(" ", ""));
                if (key == null) return null;
                keys.Add(key);
            }
            return keys.Count > 1 ? keys : null;
        }

        // ---- match hooks --------------------------------------------------------------------------
        public static void AddActiveQuests(Simulation sim, OfflinePlayerStateData d, PlayerState human)
        {
            live.Clear(); liveSingle.Clear();
            foreach (Tracker t in trackers)
            {
                if (d.ownedTitles.Contains(t.Key)) continue;
                ActiveQuest active = new ActiveQuest { Owner = human.NetId, Quest = t.Quest };
                sim.ActiveQuests.Add(active); live[active] = t.Key;
            }
            foreach (Tracker t in singleGame)
            {
                if (d.ownedTitles.Contains(t.Key)) continue;
                ActiveQuest active = new ActiveQuest { Owner = human.NetId, Quest = t.Quest };
                sim.ActiveQuests.Add(active); liveSingle[active] = t.Key;
            }
            OfflineTitleFeats.Begin(sim.ActiveQuests, sim.Players, human);
        }

        private static readonly Dictionary<ActiveQuest, string> liveSingle = new Dictionary<ActiveQuest, string>();

        public static void RecordProgress(Simulation sim, OfflinePlayerStateData d)
        {
            foreach (ActiveQuest active in sim.ActiveQuests)
            {
                string key;
                if (!live.TryGetValue(active, out key)) continue;
                int added = active.GetValueAddedDuringMatch();
                if (added <= 0) continue;
                int total = Progress(d, key) + added;
                SetProgress(d, key, total);
                if (total >= active.Quest.Goal.Value) Grant(d, key);
            }
            live.Clear();
            foreach (ActiveQuest active in sim.ActiveQuests)
            {
                string key;
                if (liveSingle.TryGetValue(active, out key) && active.GetValueAddedDuringMatch() >= active.Quest.Goal.Value) Grant(d, key);
            }
            liveSingle.Clear();
            OfflineTitleFeats.End(sim, d, OfflineNativeMatch.CurrentChapterEventId, OfflineNativeMatch.CurrentScenarioName, OfflineNativeMatch.CurrentMastery);
            CheckCollections(d);
        }

        // ---- supporter, arena and collection titles ---------------------------------------------------
        public static bool IsSupporterTitle(string key, string l)
        {
            if (key.StartsWith("title_reborn_", StringComparison.Ordinal)) return false;
            return l.StartsWith("early supporter of ", StringComparison.Ordinal) || l.StartsWith("supporter of ", StringComparison.Ordinal)
                || key.StartsWith("title_FOM_", StringComparison.Ordinal);
        }

        private static string[] ArenaGoal(string key)
        {
            foreach (string[] a in ArenaTitles) if (a[0] == key) return a;
            return null;
        }

        // A finished arena run (OfflineArenaService.Finish): a completed run (all 7 Versus / 9 Solo wins) with no loss counts
        // toward the arena titles. A run retired early (OfflineArenaService retire/quit also calls Finish) never counts.
        public static void ArenaFinished(OfflinePlayerStateData d, bool versus, int wins, int losses)
        {
            if (losses != 0 || wins < (versus ? OfflineArenaService.VersusMaxWins : OfflineArenaService.Matches)) return;
            string mode = versus ? "versus" : "solo";
            int total = AddProgress(d, "arena-undefeated:" + mode, 1);
            foreach (string[] a in ArenaTitles) if (a[1] == mode && total >= int.Parse(a[2])) Grant(d, a[0]);
        }

        // Collectible cards of the named set (production with a deck code), one copy each: the native profile collection
        // screen (PlayerProfileCollectionWin) shows a set's percentage as numTypesOwned / numTypes, i.e. card types, not
        // playsets. Which printings the server counted as types is not shipped; production cards with a deck code is the
        // reconstruction.
        private static SetGoal SetFor(GameConfiguration config, string key, string setName)
        {
            foreach (CardSet set in config.CardCollection)
            {
                string shown = Normalize(Text(set.DisplayName));
                if (shown != setName && shown != "the " + setName && Normalize(set.Name) != setName) continue;
                SetGoal goal = new SetGoal { Key = key };
                foreach (Card card in set.Cards)
                    if (card != null && card.Production && !string.IsNullOrEmpty(card.ExportCode) && !goal.Cards.Contains(card.ResourceId.Id))
                    { goal.Cards.Add(card.ResourceId.Id); goal.Need.Add(1); }
                return goal.Cards.Count > 0 ? goal : null;
            }
            return null;
        }

        public static void CheckCollections(OfflinePlayerStateData d)
        {
            foreach (SetGoal goal in setGoals) CheckCollection(d, goal);
        }

        // OfflinePlayerStateData.AddCards: only the sets containing the added card are checked.
        public static void CardsAdded(OfflinePlayerStateData d, int cardId)
        {
            List<SetGoal> list;
            if (!setGoalsByCard.TryGetValue(cardId, out list)) return;
            foreach (SetGoal goal in list) CheckCollection(d, goal);
        }

        private static void CheckCollection(OfflinePlayerStateData d, SetGoal goal)
        {
            if (d.ownedTitles.Contains(goal.Key) || d.pendingTitleRewards.Contains(goal.Key)) return;
            for (int i = 0; i < goal.Cards.Count; i++) if (d.TotalCopies(goal.Cards[i]) < goal.Need[i]) return;
            Grant(d, goal.Key);
        }

        // A completed daily quest of a guild counts toward that guild's titles.
        public static void DailyCompleted(OfflinePlayerStateData d, QuestGiverType giver)
        {
            string counter = "guild:" + giver;
            int total = Progress(d, counter) + 1;
            SetProgress(d, counter, total);
            foreach (Guild g in guilds) if (g.Giver == giver && total >= g.Count) Grant(d, g.Key);
        }

        // Grants the title through the reward flow so the client's reward window announces it.
        public static void Grant(OfflinePlayerStateData d, string key)
        {
            if (d.ownedTitles.Contains(key) || d.pendingTitleRewards.Contains(key)) return;
            d.pendingTitleRewards.Add(key);
            Rewards.RewardDescription reward = new Rewards.RewardDescription();
            reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.Quest;
            reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                rewardPassIndex = 0, bundleKey = TitleBundlePrefix + key, bundleCount = 1, rarity = Rewards.RewardRarity.Common,
                rewardContainerType = Rewards.RewardContainer.None } };
            d.rewards.Add(reward);
            UnityEngine.Debug.Log("OFFLINE TITLES: earned " + key);
        }

        public const string TitleBundlePrefix = "title_grant_";

        // Called when a title becomes owned (reward accepted): meta titles may follow.
        public static void AfterOwned(OfflinePlayerStateData d, string key)
        {
            d.pendingTitleRewards.Remove(key);
            OfflineCosmetics.AfterTitleOwned(d, key);
            foreach (Meta m in metas)
            {
                if (d.ownedTitles.Contains(m.Key)) continue;
                bool all = true;
                foreach (string r in m.Required) if (!d.ownedTitles.Contains(r)) { all = false; break; }
                if (all) Grant(d, m.Key);
            }
        }

        // ---- helpers --------------------------------------------------------------------------------
        public static int Progress(OfflinePlayerStateData d, string key)
        { int i = d.titleProgressKeys.IndexOf(key); return i < 0 ? 0 : d.titleProgressValues[i]; }

        public static int AddProgress(OfflinePlayerStateData d, string key, int added)
        { int total = Progress(d, key) + added; SetProgress(d, key, total); return total; }

        private static void SetProgress(OfflinePlayerStateData d, string key, int value)
        {
            int i = d.titleProgressKeys.IndexOf(key);
            if (i < 0) { d.titleProgressKeys.Add(key); d.titleProgressValues.Add(value); } else d.titleProgressValues[i] = value;
        }

        private static string Text(LocalizedStringKey key)
        {
            string raw = key.RawKey;
            string text = null;   // TryGetText: two Sage titles have no description text (GetText would log "Key not found")
            if (!string.IsNullOrEmpty(raw) && LocalizationManager.Instance != null && !LocalizationManager.Instance.TryGetText(raw, out text)) text = raw;
            return (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Trim();
        }

        private static CardSubtype Subtype(string color) { return (CardSubtype)Enum.Parse(typeof(CardSubtype), char.ToUpperInvariant(color[0]) + color.Substring(1)); }

        private static QuestGoal Goal(QuestGoalType type, Match x) { return new QuestGoal { Type = type, Value = int.Parse(x.Groups[1].Value) }; }

        private static ICardRequirement Is(CardRole role, CardSubtype subtype)
        {
            CardRequirements.SubtypeRequirement r = new CardRequirements.SubtypeRequirement();
            r.LhsCard = role; r.Conditional = new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = subtype };
            return r;
        }

        private static ICardRequirement Attr(CardRole role, AttributeType attribute, int atLeast)
        {
            CardRequirements.AttributeValue r = new CardRequirements.AttributeValue();
            r.LhsCard = role;
            r.Conditional = new CardConditionals.AttributeValue { AttributeName = attribute, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(atLeast) };
            return r;
        }

        private static ICardRequirement Enemy()
        {
            CardRequirements.RelationshipRequirement r = new CardRequirements.RelationshipRequirement();
            r.LhsCard = CardRole.EventTarget; r.Conditional = new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Enemies };
            return r;
        }
    }
}
