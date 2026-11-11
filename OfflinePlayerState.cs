using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OfflineEngine
{
    // Persistent server-side player state for OfflineLocalServices (what the original
    // account services stored). Identity and name come from OfflineLocalProfile. Saved
    // atomically as JSON; diagnostics pass a throwaway path so they never touch a save.
    [Serializable]
    public sealed class OfflinePlayerStateData
    {
        public int schema = 1;
        public int level = 1;
        public int xp;
        public int gold;
        public int gems;
        public int tickets;
        public long onboardingProgressFlags;
        // GenericInventory selections (avatar, title, cardback...): parallel lists because
        // JsonUtility cannot serialize dictionaries.
        public List<string> selectedCategories = new List<string>();
        public List<string> selectedValues = new List<string>();
        // Campaign progress in the client's own Campaigns wire format.
        public List<SparkleServices.Campaign.IUserCampaignData> campaigns = new List<SparkleServices.Campaign.IUserCampaignData>();
        // Rewards in the client's wire format (unclaimed passes stay listed until accepted).
        public List<SparkleServices.Rewards.RewardDescription> rewards = new List<SparkleServices.Rewards.RewardDescription>();
        public long nextRewardId;
        public List<int> grantedEvents = new List<int>();
        // Master event rewards are independent of the legacy Normal event markers.
        public List<int> grantedMasterEvents = new List<int>();
        public List<string> grantedChoices = new List<string>();
        // Card collection as parallel lists (card id, regular copies).
        public List<int> cardIds = new List<int>();
        public List<int> cardCounts = new List<int>();
        public List<int> cardPremiumCounts = new List<int>();
        // Unopened card packs in the client's Packages wire format.
        public List<SparkleServices.Packages.PackageDescription> packages = new List<SparkleServices.Packages.PackageDescription>();
        public long nextPackageId;
        // The current/last Solo Arena run in the client's Arena wire format (null: none).
        public SparkleServices.Arena.ArenaClientInstance arena;
        // Versus Arena run (arenaType "versus"), offline against AI opponents.
        public SparkleServices.Arena.ArenaClientInstance versusArena;
        // Daily quests in the client's Quests wire format, with the rotation days.
        public List<SparkleServices.Quests.QuestDescription> quests = new List<SparkleServices.Quests.QuestDescription>();
        public string questDay = "", questSkipDay = "";
        public List<string> completedQuests = new List<string>();
        // Owned cosmetics (GenericInventory item keys); cardback_default is always available.
        public List<string> ownedTitles = new List<string>(), ownedCardBacks = new List<string>();
        // Title achievement progress (lifetime counters, guild quest counts) and earned titles awaiting acceptance.
        public List<string> titleProgressKeys = new List<string>();
        public List<int> titleProgressValues = new List<int>();
        public List<string> pendingTitleRewards = new List<string>();
        // The player has seen the in-game name entry (OfflineNameEntry) at least once.
        public bool nameChosen;
        // First Grand Champion arena rank (either arena) has granted the premium Adoring Fan.
        public bool adoringFanGranted;
        // Offline option: story acts are bought with gold in the store instead of owned for free (OfflineGoldStore).
        public bool storiesForGold;
        // Offline cheat (owner idea 2026-11-05): any number of copies of any card in a deck (OfflineInventoryService reports
        // FreeDeckCopies owned with no copy limit). Offline decks live in this local profile only, never online.
        public bool freeDeckBuilding;
        public bool assistUsed;   // an F10 cheat was used on this profile (EverlastingHost.AssistEverUsed)
        // Ranked ladder (OfflineRankedService): client ProfileRating/SeasonRollOverStruct wire objects.
        public List<SparkleServices.Profile.ProfileRating> ratings = new List<SparkleServices.Profile.ProfileRating>();
        public List<SparkleServices.Profile.ProfileRating> maxRatings = new List<SparkleServices.Profile.ProfileRating>();
        public List<SparkleServices.Profile.SeasonRollOverStruct> rollovers = new List<SparkleServices.Profile.SeasonRollOverStruct>();
        public string rankedSeason = "";
        public List<string> rankedStreakModes = new List<string>();
        public List<int> rankedStreaks = new List<int>();
        // Battle Victories (Versus wins): per UTC day, a reward every three wins, at most ten a day.
        public string battleDay = "";
        public int battleWins, battleBags, dailyGold;
        // Granted entries whose stores (decks, packs, titles) are not restored yet.
        public List<string> pendingGrants = new List<string>();
        // Saved decks in the client's Inventory wire format.
        public List<SparkleServices.Inventory.Deck> decks = new List<SparkleServices.Inventory.Deck>();
        public long nextDeckId;

        public void AddCards(int cardId, int count) { AddCards(cardId, count, 0); }

        public void AddCards(int cardId, int regular, int premium)
        {
            int index = cardIds.IndexOf(cardId);
            if (index < 0) { cardIds.Add(cardId); cardCounts.Add(regular); cardPremiumCounts.Add(premium); }
            else { cardCounts[index] += regular; cardPremiumCounts[index] += premium; }
            OfflineTitles.CardsAdded(this, cardId); // set-collection titles
        }

        public int TotalCopies(int cardId)
        {
            int index = cardIds.IndexOf(cardId);
            return index < 0 ? 0 : cardCounts[index] + cardPremiumCounts[index];
        }

        public string GetSelected(string category)
        {
            int index = selectedCategories.IndexOf(category);
            return index < 0 ? null : selectedValues[index];
        }

        public void SetSelected(string category, string value)
        {
            int index = selectedCategories.IndexOf(category);
            if (index < 0) { selectedCategories.Add(category); selectedValues.Add(value); }
            else selectedValues[index] = value;
        }
    }

    public sealed class OfflinePlayerState
    {
        private readonly string path;
        private readonly object gate = new object();
        private OfflinePlayerStateData data;

        public OfflinePlayerState(string path)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            data = File.Exists(path) ? JsonUtility.FromJson<OfflinePlayerStateData>(File.ReadAllText(path)) : new OfflinePlayerStateData();
            if (data == null || data.schema != 1) throw new InvalidOperationException("Unsupported offline player state: " + path);
            if (data.campaigns == null) data.campaigns = new List<SparkleServices.Campaign.IUserCampaignData>();
            if (data.rewards == null) data.rewards = new List<SparkleServices.Rewards.RewardDescription>();
            if (data.grantedEvents == null) data.grantedEvents = new List<int>();
            if (data.grantedMasterEvents == null) data.grantedMasterEvents = new List<int>();
            if (data.grantedChoices == null) data.grantedChoices = new List<string>();
            if (data.pendingGrants == null) data.pendingGrants = new List<string>();
            if (data.ownedTitles == null) data.ownedTitles = new List<string>();
            if (data.ownedCardBacks == null) data.ownedCardBacks = new List<string>();
            if (data.titleProgressKeys == null || data.titleProgressValues == null || data.titleProgressKeys.Count != data.titleProgressValues.Count)
            { data.titleProgressKeys = new List<string>(); data.titleProgressValues = new List<int>(); }
            if (data.pendingTitleRewards == null) data.pendingTitleRewards = new List<string>();
            if (data.ratings == null) data.ratings = new List<SparkleServices.Profile.ProfileRating>();
            if (data.maxRatings == null) data.maxRatings = new List<SparkleServices.Profile.ProfileRating>();
            if (data.rollovers == null) data.rollovers = new List<SparkleServices.Profile.SeasonRollOverStruct>();
            if (data.rankedSeason == null) data.rankedSeason = "";
            if (data.battleDay == null) data.battleDay = "";
            if (data.rankedStreakModes == null || data.rankedStreaks == null || data.rankedStreakModes.Count != data.rankedStreaks.Count)
            { data.rankedStreakModes = new List<string>(); data.rankedStreaks = new List<int>(); }
            if (data.decks == null) data.decks = new List<SparkleServices.Inventory.Deck>();
            if (data.quests == null) data.quests = new List<SparkleServices.Quests.QuestDescription>();
            if (data.completedQuests == null) data.completedQuests = new List<string>();
            if (data.questDay == null) data.questDay = "";
            if (data.questSkipDay == null) data.questSkipDay = "";
            if (data.cardIds == null || data.cardCounts == null) { data.cardIds = new List<int>(); data.cardCounts = new List<int>(); }
            if (data.cardPremiumCounts == null) data.cardPremiumCounts = new List<int>();
            while (data.cardPremiumCounts.Count < data.cardIds.Count) data.cardPremiumCounts.Add(0);
            if (data.packages == null) data.packages = new List<SparkleServices.Packages.PackageDescription>();
            if (data.cardIds.Count != data.cardCounts.Count || data.cardIds.Count != data.cardPremiumCounts.Count) throw new InvalidOperationException("Corrupt offline card collection: " + path);
            if (data.selectedCategories == null || data.selectedValues == null || data.selectedCategories.Count != data.selectedValues.Count)
                throw new InvalidOperationException("Corrupt offline player selections: " + path);
        }

        public string StoragePath { get { return path; } }

        // Read under lock; returns a detached copy for serialization.
        public OfflinePlayerStateData Snapshot()
        { lock (gate) return Copy(data); }

        // Change a copy, persist it atomically, and only then publish it in memory, so a
        // failed write never leaves memory ahead of disk.
        public void Mutate(Action<OfflinePlayerStateData> change)
        {
            lock (gate)
            {
                OfflinePlayerStateData next = Copy(data);
                change(next);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(next, true));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                data = next;
            }
        }

        private static OfflinePlayerStateData Copy(OfflinePlayerStateData value)
        { return JsonUtility.FromJson<OfflinePlayerStateData>(JsonUtility.ToJson(value)); }
    }
}
