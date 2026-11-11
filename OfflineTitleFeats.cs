using System;
using System.Collections.Generic;
using SparkleServices;
using SPTESL;

namespace OfflineEngine
{
    // Per-game title feats. The original server judged these from the match; the shipped Title quests carry no goal.
    // Offline, the running Simulation is sampled once per frame on the main thread (OfflineNativeMatch.Pump) and once
    // more at the result, then each feat is judged from what was seen (installed TitleData descriptions):
    //  - The Defiant "Win a game at 1 health", The Miraculous "Win a game at negative health" (avatar health at the result);
    //  - The Clever "Defeat your opponent in six turns or less" (the human's TurnCount at the win);
    //  - The Untouchable "Win a game of 5 turns or longer without taking damage": event-based, hidden native DamageValue
    //    ActiveQuests on the human's avatar (one owned by each player, so enemy and self damage both count);
    //  - The Immortal "Have 100 health at some point in a game", The Hist Mage "Reach 15 max magicka in a game";
    //  - The Gravedigger "Have 50 or more creatures in your discard pile";
    //  - The General "Have 8 Imperial Grunts in play at the same time";
    //  - The Master of Mirrors "Get three copies of a unique creature in play";
    //  - The Master of Flesh "Summon a 20 or more power Flesh Atronach": native SummonCount plus a post-Summon-buff check;
    //  - The Unconquerable "Have a Fighters Guild Elite with 100 power";
    //  - The Prophet "Hit a prophecy off all 5 runes in a game" (native PropheciesFromRuneBreak reaches 5);
    //  - The Temple's Chosen "Reach 40 health at the Temple of Akatosh" (DBH chapter event 560661651);
    //  - The Runebreaker "Destroy 500 runes" (lifetime): counted at each actual break of an opponent rune in the restored
    //    DecisionRestoration.CheckForRuneBreaks (exact event; NumRunesLost is a deficit that rune restores reset).
    // Limits of the state-worded titles: a state that rises and falls inside one frame is not seen; "in play" means the
    // human's creature lanes. Damage without an acting card raises no native DamageValue event, so an observed drop of the
    // avatar's health also counts as damage for The Untouchable (conservative: never a false grant). The Engineer (Assemble)
    // stays locked: there is no Assemble event hook yet.
    public static class OfflineTitleFeats
    {
        public const int TempleOfAkatoshEvent = 560661651;

        // What one match showed (main thread). Public for the engine test.
        public class Seen
        {
            public int StartHealth = int.MinValue, LastHealth = int.MinValue, MaxHealth = int.MinValue, EndHealth;
            public bool TookDamage, Won, NeutralStartingDeck;
            public int MaxMagicka, Turns, MaxDiscardCreatures, MaxGrunts, MaxUniqueCopies, MaxEliteGuildPower;
            public bool OpponentDead;
            public int DamageTaken, FleshSummons;
            public int RuneProphecies, OpponentRunesBroken, ChapterEventId;
            public bool HealthDropped, FleshSummonBuffReached;
            public string ScenarioName; public bool Mastery;
        }

        // Story titles won on a named scenario or chapter event (installed scenario names, exports/native-campaign-scenarios.tsv;
        // mapping evidence in data/TITLE-STORY-SOURCE.md). Rows: key, scenario name or "event:<id>", "master"|"any", extra.
        // Extra "foe0": the opponent's health reached 0; "pair:<other key>": both rows must have been won (profile markers).
        private static readonly string[][] StoryWins = {
            new[] { "title_the_animal_lover", "DBH_Act1_Ep2_MonstersOfWayrest", "master", "" },
            new[] { "title_the_giant_slayer", "DBH_Act2_Ep3_HilltopGiants", "master", "foe0" },
            new[] { "title_the_crypt_raider", "DBH_Act3_Ep6_Alisanne", "any", "" },
            new[] { "title_the_assassins_bane", "DBH_Act3_Ep6_Alisanne", "master", "" },
            new[] { "title_the_listener", "DBH_Act3_Ep6_UtherNere", "any", "" },
            new[] { "title_the_night_mothers_keeper", "DBH_Act3_Ep6_UtherNere", "master", "" },
            new[] { "title_child_of_hircine", "CWC_Act1_Ep2_PitFalmer_Werewolf", "any", "" },
            new[] { "title_friend_to_animals", "CWC_Act1_Ep2_FrostTroll_B", "any", "" },
            new[] { "title_the_citys_savior", "CWC_Act3_Ep6_Mecinar1", "any", "" },
            new[] { "title_the_god_of_the_machine", "CWC_Act3_Ep6_Mecinar1", "master", "" },
            new[] { "title_the_citys_destroyer", "CWC_Act3_Ep6_Mecinar2", "any", "" },
            new[] { "title_the_god_slayer", "CWC_Act3_Ep6_Mecinar2", "master", "" },
            new[] { "title_the_escape_artist", "CWC_Act1_Ep1_MistveilGuard", "any", "" },
            new[] { "title_IOM_the_truthbringer", "IOM_Act1_Ep1_TellTheKnight", "any", "" },
            new[] { "title_IOM_the_ghost_whisperer", "event:591184772", "any", "" },
            new[] { "title_IOM_the_betrayer", "event:-1909586255", "any", "" },
            new[] { "title_IOM_the_prince_of_order", "event:-938799815", "any", "" },
            new[] { "title_IOM_the_purveyor_of_prophecy", "IOM_Act2_Ep3_KwamaMine", "any", "all:IOM_Act2_Ep3_KwamaMine,IOM_Act2_Ep3_MushroomTower,IOM_Act2_Ep3_MelielsFarm" },
            new[] { "title_IOM_the_purveyor_of_prophecy", "IOM_Act2_Ep3_MushroomTower", "any", "all:IOM_Act2_Ep3_KwamaMine,IOM_Act2_Ep3_MushroomTower,IOM_Act2_Ep3_MelielsFarm" },
            new[] { "title_IOM_the_purveyor_of_prophecy", "IOM_Act2_Ep3_MelielsFarm", "any", "all:IOM_Act2_Ep3_KwamaMine,IOM_Act2_Ep3_MushroomTower,IOM_Act2_Ep3_MelielsFarm" },
            new[] { "title_the_ingenious", "event:-2037241312", "any", "" },
            new[] { "title_IOM_the_conqueror_of_madness", "event:-1463643075", "any", "" },
            new[] { "title_IOM_the_clearheaded", "event:-1463643075", "master", "" },
            // Multi-part titles: every listed part won (markers kept in titleProgress as "story:<part>").
            new[] { "title_the_master_of_stealth", "DBH_Act1_Ep2_CastleDefenders_NoDisguise", "any", "all:DBH_Act1_Ep2_CastleDefenders_NoDisguise,DBH_Act3_Ep5_EclipseSneak" },
            new[] { "title_the_master_of_stealth", "DBH_Act3_Ep5_EclipseSneak", "any", "all:DBH_Act1_Ep2_CastleDefenders_NoDisguise,DBH_Act3_Ep5_EclipseSneak" },
            new[] { "title_the_memory_master", "event:-783094043", "master", "all:m-783094043,m27525883,m1074531588,m1933962168,m1271260546" },
            new[] { "title_the_memory_master", "event:27525883", "master", "all:m-783094043,m27525883,m1074531588,m1933962168,m1271260546" },
            new[] { "title_the_memory_master", "event:1074531588", "master", "all:m-783094043,m27525883,m1074531588,m1933962168,m1271260546" },
            new[] { "title_the_memory_master", "event:1933962168", "master", "all:m-783094043,m27525883,m1074531588,m1933962168,m1271260546" },
            new[] { "title_the_memory_master", "event:1271260546", "master", "all:m-783094043,m27525883,m1074531588,m1933962168,m1271260546" },
            // "Complete Master Gates of Madness after completing Master Garden of Swords".
            new[] { "title_IOM_the_gateslayer", "event:-527834228", "master", "after:m1824998868" },
        };

        private static void JudgeStory(Seen s, OfflinePlayerStateData d, List<string> keys)
        {
            if (!s.Won) return;
            string scenario = StoryScenarioKey(s.ScenarioName, s.Mastery);
            // Profile markers first, so a row can see the part just won.
            if (scenario != null) OfflineTitles.AddProgress(d, "story:" + scenario, 1);
            if (s.ChapterEventId != 0 && s.Mastery) OfflineTitles.AddProgress(d, "story:m" + s.ChapterEventId, 1);
            foreach (string[] row in StoryWins)
            {
                bool match = row[1].StartsWith("event:", StringComparison.Ordinal) ? s.ChapterEventId != 0 && row[1] == "event:" + s.ChapterEventId : row[1] == scenario;
                if (!match || (row[2] == "master" && !s.Mastery)) continue;
                if (row[3] == "foe0" && !s.OpponentDead) continue;
                if (row[3].StartsWith("all:", StringComparison.Ordinal) && !AllWon(d, row[3].Substring(4).Split(','))) continue;
                if (row[3].StartsWith("after:", StringComparison.Ordinal) && !AllWon(d, new[] { row[3].Substring(6) })) continue;
                if (!keys.Contains(row[0])) keys.Add(row[0]);
            }
        }

        // Installed Master scenario names insert "_M" after the campaign prefix. Normalize
        // only the three known story campaigns, and only for an actual Master request.
        private static string StoryScenarioKey(string name, bool mastery)
        {
            if (mastery && name != null && (name.StartsWith("DBH_M_", StringComparison.Ordinal)
                || name.StartsWith("CWC_M_", StringComparison.Ordinal) || name.StartsWith("IOM_M_", StringComparison.Ordinal)))
                return name.Substring(0, 3) + "_" + name.Substring(6);
            return name;
        }

        private static bool AllWon(OfflinePlayerStateData d, string[] parts)
        {
            foreach (string part in parts)
            {
                if (OfflineTitles.Progress(d, "story:" + part) > 0) continue;
                // Preserve prior profile markers recorded with the full Master scenario name.
                if ((part.StartsWith("DBH_", StringComparison.Ordinal) || part.StartsWith("CWC_", StringComparison.Ordinal)
                    || part.StartsWith("IOM_", StringComparison.Ordinal))
                    && OfflineTitles.Progress(d, "story:" + part.Substring(0, 3) + "_M" + part.Substring(3)) > 0) continue;
                return false;
            }
            return true;
        }

        private static string StoryDescribe(string key)
        {
            if (key == "title_the_kingslayer") return "won Wayrest throne room and chose Kill";
            if (key == "title_the_kings_protector") return "won Wayrest throne room and chose Spare";
            if (key == "title_the_smuggler") return "won Forgotten Hero chapter 8 reward bundle";
            if (key == "title_the_savior_of_cyrodiil") return "won Forgotten Hero chapter 20 reward bundle";
            if (key == "title_2.15_TheFelonius") return "won the Skooma Cat puzzle set";
            foreach (string[] row in StoryWins) if (row[0] == key) return "won " + row[1] + (row[2] == "master" ? " (Master)" : "") + (row[3].Length > 0 ? " " + row[3] : "");
            return null;
        }

        // The installed King Barynia ending presents Kill then Spare after the battle.
        public static SparkleServices.Rewards.RewardDescription StoryChoice(OfflinePlayerStateData d, int eventId, bool completed,
            SparkleServices.Campaign.IDialogModalType modal, SparkleServices.Campaign.IUserCampaignChoice choice)
        {
            if (!completed || eventId != 1122728033 || modal != SparkleServices.Campaign.IDialogModalType.Ending) return null;
            string key = choice == SparkleServices.Campaign.IUserCampaignChoice.First ? "title_the_kingslayer"
                : choice == SparkleServices.Campaign.IUserCampaignChoice.Second ? "title_the_kings_protector" : null;
            if (key == null) return null;
            int before = d.rewards.Count;
            OfflineTitles.Grant(d, key);
            return d.rewards.Count > before ? d.rewards[before] : null;
        }

        private static Seen seen;
        private static int humanId = int.MinValue;

        public static Seen Current { get { return seen; } }

        // Flesh Atronach's own Summon buff resolves after the native summon-count quest event.
        public static void FleshSummonBuffReached(ServerCard card)
        {
            if (seen != null && card != null && card.OwnerNetId.Id == humanId) seen.FleshSummonBuffReached = true;
        }

        private static readonly List<ActiveQuest> damageQuests = new List<ActiveQuest>();
        private static ActiveQuest fleshQuest;

        // Match start: event quests are added to the simulation's ActiveQuests (QuestType.Title, so daily
        // bookkeeping skips them; OfflineTitles keeps no record of them).
        public static void Begin(List<ActiveQuest> quests, List<PlayerState> players, PlayerState human)
        {
            seen = new Seen();
            humanId = human == null ? int.MinValue : human.NetId.Id;
            damageQuests.Clear(); fleshQuest = null;
            if (human == null || quests == null || players == null) return;
            foreach (PlayerState p in players)
            {
                ActiveQuest q = new ActiveQuest { Owner = p.NetId, Quest = new Quest { Goal = OfflineTitles.AvatarDamageGoal(p.NetId.Id != humanId), QuestType = QuestType.Title } };
                quests.Add(q); damageQuests.Add(q);
            }
            QuestGoal flesh = OfflineTitles.NamedSummonGoal("Flesh Atronach", 20);
            if (flesh != null) { fleshQuest = new ActiveQuest { Owner = human.NetId, Quest = new Quest { Goal = flesh, QuestType = QuestType.Title } }; quests.Add(fleshQuest); }
        }

        // DecisionRestoration.CheckForRuneBreaks, at each break (main thread): the opponent's runes count for the human.
        public static void RuneBroken(int playerId)
        {
            if (seen != null && humanId != int.MinValue && playerId != humanId) seen.OpponentRunesBroken++;
        }

        // Damage/summon events counted so far this match (main thread).
        private static void ReadEvents()
        {
            int damage = 0;
            foreach (ActiveQuest q in damageQuests) damage += q.GetValueAddedDuringMatch();
            seen.DamageTaken = damage;
            seen.FleshSummons = fleshQuest == null ? 0 : fleshQuest.GetValueAddedDuringMatch();
        }

        // Once per frame while a match runs.
        public static void Sample(Simulation sim)
        {
            if (seen == null || sim == null || humanId == int.MinValue) return;
            try { Observe(sim.CardCatalog, sim.Players); }
            // One failed sample is skipped; the match's other observations, events and the story judgement are kept.
            catch (Exception e) { UnityEngine.Debug.LogWarning("OFFLINE TITLE FEATS: sample skipped: " + e.Message); }
        }

        // One observation of the board (public for the engine test).
        public static void Observe(ServerCardCatalog catalog, List<PlayerState> players)
        {
            if (seen == null || catalog == null || players == null) return;
            ReadEvents();
            PlayerState human = null, foe = null;
            foreach (PlayerState p in players) { if (p.NetId.Id == humanId) human = p; else foe = p; }
            if (human == null) return;
            int grunts = 0, discard = 0;
            Dictionary<int, int> unique = new Dictionary<int, int>();
            ServerCard avatar = null, foeAvatar = null;
            foreach (ServerCard c in catalog.GatherCardList(delegate(ServerCard x) { return true; }))
            {
                bool mine = c.OwnerNetId.Id == humanId;
                if (c.HasSubtype(CardSubtype.Avatar)) { if (mine) avatar = c; else foeAvatar = c; continue; }
                if (!mine || !c.HasSubtype(CardSubtype.Creature)) continue;
                if (c.CurrentGameZoneName == GameZoneName.Cemetery) { discard++; continue; }
                if (c.CurrentGameZoneName != GameZoneName.CreatureBoard) continue;
                string name = OfflineTitles.CardName(c.CardId);
                int power = c.QueryAttribute(AttributeType.Attack).Value;
                if (name == "Imperial Grunt") grunts++;
                if (name == "Fighters Guild Elite") seen.MaxEliteGuildPower = Math.Max(seen.MaxEliteGuildPower, power);
                if (OfflineTitles.IsUniqueCard(c.CardId)) { int n; unique.TryGetValue(c.CardId, out n); unique[c.CardId] = n + 1; }
            }
            seen.MaxGrunts = Math.Max(seen.MaxGrunts, grunts);
            seen.MaxDiscardCreatures = Math.Max(seen.MaxDiscardCreatures, discard);
            foreach (int n in unique.Values) seen.MaxUniqueCopies = Math.Max(seen.MaxUniqueCopies, n);
            if (avatar != null)
            {
                int health = avatar.CurrentHealth;
                if (seen.StartHealth == int.MinValue) seen.StartHealth = health;
                if (seen.LastHealth != int.MinValue && health < seen.LastHealth) seen.HealthDropped = true;
                seen.LastHealth = health; seen.EndHealth = health;
                seen.MaxHealth = Math.Max(seen.MaxHealth, health);
                seen.MaxMagicka = Math.Max(seen.MaxMagicka, avatar.QueryAttribute(AttributeType.MaxMana).Value);
            }
            seen.Turns = human.QueryAttribute(AttributeType.TurnCount).Value;
            seen.RuneProphecies = Math.Max(seen.RuneProphecies, human.QueryAttribute(AttributeType.PropheciesFromRuneBreak).Value);
            if (foeAvatar != null) seen.OpponentDead = foeAvatar.CurrentHealth <= 0;
        }

        // Match end (inside the state mutation): judge and grant. Returns the earned keys (test aid).
        public static List<string> End(Simulation sim, OfflinePlayerStateData d, int chapterEventId, string scenarioName, bool mastery)
        {
            List<string> earned = new List<string>();
            if (seen == null) return earned;
            Sample(sim);
            ReadEvents();
            seen.Won = sim != null && sim.WinnerId.Id == humanId;
            PlayerState human = sim == null ? null : sim.Players.Find(delegate(PlayerState p) { return p.NetId.Id == humanId; });
            seen.NeutralStartingDeck = IsAllNeutralStartingDeck(human);
            seen.TookDamage = seen.DamageTaken > 0 || seen.HealthDropped;
            seen.ChapterEventId = chapterEventId; seen.ScenarioName = scenarioName; seen.Mastery = mastery;
            earned = Judge(seen, d);
            seen = null; humanId = int.MinValue; damageQuests.Clear(); fleshQuest = null;
            return earned;
        }

        // Pure judgement of one match (engine test entry point).
        public static List<string> Judge(Seen s, OfflinePlayerStateData d)
        {
            List<string> keys = new List<string>();
            bool sampled = s.StartHealth != int.MinValue;
            if (s.Won && sampled && s.EndHealth == 1) keys.Add("title_the_defiant");
            if (s.Won && sampled && s.EndHealth < 0) keys.Add("title_the_miraculous");
            if (s.Won && s.Turns > 0 && s.Turns <= 6) keys.Add("title_the_clever");
            if (s.Won && sampled && s.Turns >= 5 && !s.TookDamage) keys.Add("title_the_untouchable");
            if (s.MaxHealth >= 100) keys.Add("title_the_immortal");
            if (s.MaxMagicka >= 15) keys.Add("title_the_hist_mage");
            if (s.MaxDiscardCreatures >= 50) keys.Add("title_the_gravedigger");
            if (s.MaxGrunts >= 8) keys.Add("title_the_general");
            if (s.MaxUniqueCopies >= 3) keys.Add("title_the_master_of_mirrors");
            if (s.FleshSummons > 0 || s.FleshSummonBuffReached) keys.Add("title_the_master_of_flesh");
            if (s.MaxEliteGuildPower >= 100) keys.Add("title_AW_the_unconquerable");
            if (s.RuneProphecies >= 5) keys.Add("title_the_prophet");
            if (s.ChapterEventId == TempleOfAkatoshEvent && s.MaxHealth >= 40) keys.Add("title_the_temples_chosen");
            if (s.OpponentRunesBroken > 0)
            {
                int total = OfflineTitles.AddProgress(d, "feat:runes", s.OpponentRunesBroken);
                if (total >= 500) keys.Add("title_the_runebreaker");
            }
            if (s.Won && s.NeutralStartingDeck && OfflineTitles.AddProgress(d, "title_the_centurion", 1) >= 100)
                keys.Add("title_the_centurion");
            JudgeStory(s, d, keys);
            foreach (string key in keys) OfflineTitles.Grant(d, key);
            return keys;
        }

        // Report text for native-title-goals.tsv (null: not a feat).
        public static string Describe(string key)
        {
            switch (key)
            {
                case "title_the_defiant": return "win at health 1";
                case "title_the_miraculous": return "win at health < 0";
                case "title_the_clever": return "win by turn 6";
                case "title_the_untouchable": return "win, >=5 turns, no DamageValue event or health drop on own avatar";
                case "title_the_immortal": return "health >= 100";
                case "title_the_hist_mage": return "max magicka >= 15";
                case "title_the_gravedigger": return "creatures in discard >= 50";
                case "title_the_general": return "8 Imperial Grunts in lanes";
                case "title_the_master_of_mirrors": return "3 copies of a unique creature in lanes";
                case "title_the_master_of_flesh": return "Flesh Atronach power >= 20 at summon or after its Summon buff";
                case "title_AW_the_unconquerable": return "Fighters Guild Elite power >= 100";
                case "title_the_prophet": return "PropheciesFromRuneBreak >= 5";
                case "title_the_temples_chosen": return "health >= 40 at event " + TempleOfAkatoshEvent;
                case "title_the_runebreaker": return "opponent rune breaks (CheckForRuneBreaks), lifetime >= 500";
                case "title_the_centurion": return "100 wins with an all-Neutral starting deck";
            }
            return StoryDescribe(key);
        }

        // StartingDeckColors comes from the installed Deck.GetDeckCardColors(false), which excludes Neutral.
        internal static bool IsAllNeutralStartingDeck(PlayerState player)
        {
            if (player == null || player.StartingDeck == null || player.StartingDeck.Count == 0 || player.StartingDeckColors == null) return false;
            foreach (CardSubtype color in player.StartingDeckColors) if (color != CardSubtype.Neutral) return false;
            return true;
        }
    }
}
