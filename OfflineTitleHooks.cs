using System;
using System.Collections.Generic;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Titles whose condition the original server tracked itself (no card raises an event for them). They are judged from the
    // native quest events (ActiveQuestUtilities.ExecuteCardQuestEvent names the acting and the target card), the Veteran
    // trigger and the board samples OfflineTitleFeats already takes. Per-match state; lifetime counts go to the profile.
    public static class OfflineTitleHooks
    {
        public const string LoneWarrior = "title_the_lone_warrior", PastryChef = "title_the_pastry_chef", Scrupulous = "title_IOM_the_scrupulous",
            Ironic = "title_IOM_the_ironic", Troublemaker = "title_IOM_the_troublemaker", PactWarleader = "title_AW_pact_warleader",
            ImperialTactician = "title_AW_imperial_tactician", CovenantArmsmaster = "title_AW_covenant_armsmaster",
            ServantOfDestruction = "title_Jaws_ServantOfDestruction", TheArtist = "title_Jaws_TheArtist";

        private static int humanId = int.MinValue;
        private static readonly HashSet<int> damagingCreatures = new HashSet<int>();
        private static readonly HashSet<int> veterans = new HashSet<int>();
        private static readonly HashSet<int> sweetRollsAtStart = new HashSet<int>();
        private static bool baseline, sweetRollMade, ironic, artist, trophyCaseLost;
        private static int selfDamage, tactician, armsmaster, servant;

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ActiveQuestUtilities), "ExecuteCardQuestEvent"),
                new HarmonyMethod(typeof(OfflineTitleHooks).GetMethod("QuestEventPrefix")), null, null, null, null);
        }

        public static string Describe(string key)
        {
            switch (key)
            {
                case LoneWarrior: return "win with damage dealt by only one of your creatures";
                case PastryChef: return "a Sweet Roll is made for you during a match";
                case Scrupulous: return "win Master IOM_Act1_Ep2_TrophyRoom with no Trophy Case destroyed";
                case Ironic: return "Lady Syl's Cruelty sacrifices Syl, Duchess of Dementia";
                case Troublemaker: return "100 damage to your own avatar from Trouble Seeker (lifetime)";
                case PactWarleader: return "200 of your creatures become Veteran (lifetime)";
                case ImperialTactician: return "200 summons with another of your creatures in each lane (lifetime)";
                case CovenantArmsmaster: return "200 items equipped to your Recruits (lifetime)";
                case ServantOfDestruction: return "100 Daedra summoned while you have an Oblivion Gate (lifetime)";
                case TheArtist: return "play The Painted World at 0 cost";
                case "title_the_persistent": return "reach the daily Versus gold limit (OfflineRankedService)";
            }
            return null;
        }

        public static void Begin(int human)
        {
            humanId = human;
            damagingCreatures.Clear(); veterans.Clear(); sweetRollsAtStart.Clear();
            baseline = sweetRollMade = ironic = artist = trophyCaseLost = false;
            selfDamage = tactician = armsmaster = servant = 0;
        }

        private static string Name(ServerCard c)
        {
            if (c == null) return null;
            Card def = GameManager.Instance == null ? null : GameManager.Instance.GetCard(c.CardId);
            return def == null ? null : def.Name;
        }

        private static bool Mine(ServerCard c) { return c != null && humanId != int.MinValue && c.OwnerNetId.Id == humanId; }

        // Prefix: observes, never changes the native quest bookkeeping.
        public static void QuestEventPrefix(QuestGoalType goalType, ServerEffectUtilities.TriggerGameState gameState, ServerCard actingCard, ServerCard targetCard, int increment)
        {
            try { Observe(goalType, gameState, actingCard, targetCard, increment); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("OFFLINE TITLE HOOKS: " + e.Message); }
        }

        public static void Observe(QuestGoalType goalType, ServerEffectUtilities.TriggerGameState gameState, ServerCard actingCard, ServerCard targetCard, int increment)
        {
            if (humanId == int.MinValue || actingCard == null) return;
            switch (goalType)
            {
                case QuestGoalType.DamageValue:
                    if (Mine(actingCard) && increment > 0 && actingCard.HasSubtype(CardSubtype.Creature)) damagingCreatures.Add(actingCard.InstanceId.Id);
                    if (Mine(actingCard) && increment > 0 && Name(actingCard) == "Trouble Seeker" && Mine(targetCard) && targetCard.HasSubtype(CardSubtype.Avatar))
                        selfDamage += increment;
                    break;
                case QuestGoalType.SummonCount:
                    if (!Mine(actingCard) || !actingCard.HasSubtype(CardSubtype.Creature) || gameState.CardCatalog == null) break;
                    if (EveryLaneHasAnother(gameState.CardCatalog, actingCard)) tactician++;
                    if (actingCard.HasSubtype(CardSubtype.Daedra) && HasCard(gameState.CardCatalog, "Oblivion Gate")) servant++;
                    break;
                case QuestGoalType.EquipCount:
                    if (Mine(actingCard) && Mine(targetCard) && targetCard.HasSubtype(CardSubtype.Recruit)) armsmaster++;
                    break;
                case QuestGoalType.PlayCount:
                    if (Mine(actingCard) && Name(actingCard) == "The Painted World" && actingCard.QueryAttribute(AttributeType.ManaCost).Value == 0) artist = true;
                    break;
                case QuestGoalType.DestroyCount:
                    if (Mine(actingCard) && Name(actingCard) == "Lady Syl's Cruelty" && Name(targetCard) == "Syl, Duchess of Dementia") ironic = true;
                    break;
            }
        }

        // DecisionRestoration raises the Veteran trigger once per creature, after its first resolved attack.
        public static void Veteran(ServerCard attacker)
        {
            if (Mine(attacker)) veterans.Add(attacker.InstanceId.Id);
        }

        private static bool EveryLaneHasAnother(ServerCardCatalog catalog, ServerCard summoned)
        {
            GameZone board = catalog.FindGameZone(summoned.OwnerNetId, GameZoneName.CreatureBoard);
            if (board == null || board.LaneList.Count == 0) return false;
            bool[] filled = new bool[board.LaneList.Count];
            foreach (ServerCard c in catalog.GatherCardsInZone(delegate(ServerCard x) { return x.InstanceId.Id != summoned.InstanceId.Id; }, summoned.OwnerNetId, GameZoneName.CreatureBoard))
                if (c.CurrentLaneIndex >= 0 && c.CurrentLaneIndex < filled.Length) filled[c.CurrentLaneIndex] = true;
            foreach (bool f in filled) if (!f) return false;
            return true;
        }

        private static bool HasCard(ServerCardCatalog catalog, string name)
        {
            foreach (ServerCard c in catalog.GatherCardList(delegate(ServerCard x) { return x.CurrentGameZoneName == GameZoneName.CreatureBoard; }))
                if (Mine(c) && Name(c) == name) return true;
            return false;
        }

        // Board sample (OfflineTitleFeats.Observe): Sweet Rolls that appear after the first sample were made this match;
        // a Trophy Case in any discard pile was destroyed.
        public static void Sample(ServerCardCatalog catalog)
        {
            if (humanId == int.MinValue || catalog == null) return;
            foreach (ServerCard c in catalog.GatherCardList(delegate(ServerCard x) { return true; }))
            {
                string n = Name(c);
                if (n == "Sweet Roll" && Mine(c))
                {
                    if (!baseline) sweetRollsAtStart.Add(c.InstanceId.Id);
                    else if (!sweetRollsAtStart.Contains(c.InstanceId.Id)) sweetRollMade = true;
                }
                if (n == "Trophy Case" && c.CurrentGameZoneName == GameZoneName.Cemetery) trophyCaseLost = true;
            }
            baseline = true;
        }

        // Match end: grants and lifetime progress. Returns the keys granted now.
        public static List<string> Judge(OfflinePlayerStateData d, bool won, string scenarioName, bool mastery)
        {
            List<string> keys = new List<string>();
            if (humanId == int.MinValue || d == null) return keys;
            if (won && damagingCreatures.Count == 1) keys.Add(LoneWarrior);
            if (sweetRollMade) keys.Add(PastryChef);
            if (won && mastery && scenarioName == "IOM_Act1_Ep2_TrophyRoom" && !trophyCaseLost) keys.Add(Scrupulous);
            if (ironic) keys.Add(Ironic);
            if (artist) keys.Add(TheArtist);
            Lifetime(d, Troublemaker, selfDamage, 100, keys);
            Lifetime(d, PactWarleader, veterans.Count, 200, keys);
            Lifetime(d, ImperialTactician, tactician, 200, keys);
            Lifetime(d, CovenantArmsmaster, armsmaster, 200, keys);
            Lifetime(d, ServantOfDestruction, servant, 100, keys);
            foreach (string k in keys) OfflineTitles.Grant(d, k);
            humanId = int.MinValue;
            return keys;
        }

        private static void Lifetime(OfflinePlayerStateData d, string key, int added, int goal, List<string> keys)
        {
            if (added <= 0) return;
            if (OfflineTitles.AddProgress(d, key, added) >= goal) keys.Add(key);
        }
    }
}
