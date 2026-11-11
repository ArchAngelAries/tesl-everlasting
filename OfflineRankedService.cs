using System;
using System.Collections.Generic;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Ranked ladder offline (opponents are AI). Wire format is the client's Profile.ProfileRating / SeasonRollOverStruct:
    // rank 12..1, 0 = Legend; rankStars < 0 = the Serpent (client shows SERPENT_STAR_COUNT + stars); *Seen fields are what
    // RankedResultWin animates from until profile/setSeen. Rules follow UESP Legends:Ranked:
    //   stars per rank 12..1 = 4,4,4,7,5,5,5,7,6,6,6,7; a win gains a star, a loss loses one from The Lord (10) on;
    //   ranks 12 and 11 have loss protection; losing at 0 stars drops into the Serpent (no rank loss within a season);
    //   Legend has no stars (offline legend position is a reconstruction from the Legend record).
    // Reconstructions: a bonus star for a 3+ win streak at ranks 12..5 (the client shows "bonus" when more than one star is
    // gained); Serpent: entering sets -1, a win returns to the constellation at 0 stars, further losses keep -1.
    // Seasons are calendar months (UTC). At a new month every ranked rating gets a Ready rollover with UESP's
    // season-end gold/soul gems and rank reset, claimed through the client's own profile/doSeasonRollover flow.
    public static class OfflineRankedService
    {
        public const string RankedPvP = "rankedPvP", UnrankedPvP = "unrankedPvP", Legend = "legend", VersusArena = "versusArenaV2", ArenaLegend = "arenaLegend";
        private static readonly int[] StarsPerRank = { 0, 7, 6, 6, 6, 7, 5, 5, 5, 7, 4, 4, 4 }; // index = rank
        private static readonly int[] SeasonGold = { 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 10 };
        private static readonly int[] SeasonGems = { 250, 200, 180, 170, 160, 150, 130, 120, 110, 100, 80, 70, 0 };
        private static readonly int[] SeasonReset = { 5, 5, 5, 5, 5, 9, 9, 9, 9, 12, 12, 12, 12 };

        public static void Register(OfflineLocalServices services, OfflinePlayerState state)
        {
            services.Register("profile/setSeen", delegate(string m, string p, string body) {
                state.Mutate(delegate(OfflinePlayerStateData d) { foreach (Profile.ProfileRating r in d.ratings) { r.rankSeen = r.rank; r.rankStarsSeen = r.rankStars; } });
                return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.Ok }); });
            services.Register("profile/doSeasonRollover", delegate(string m, string p, string body) {
                state.Mutate(ClaimRollovers);
                return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.Ok }); });
        }

        // profile/fetch: ratings, best ratings and season data (season start is checked here).
        public static void Fill(OfflinePlayerState state, Profile.ProfileStruct profile)
        {
            state.Mutate(CheckSeason);
            OfflinePlayerStateData d = state.Snapshot();
            profile.ratings = new List<Profile.ProfileRating>(d.ratings);
            if (!profile.ratings.Exists(delegate(Profile.ProfileRating r) { return r.ratingMode == RankedPvP; })) profile.ratings.Add(Default(RankedPvP));
            if (!profile.ratings.Exists(delegate(Profile.ProfileRating r) { return r.ratingMode == VersusArena; })) profile.ratings.Add(Default(VersusArena));
            profile.maxRatings = new List<Profile.ProfileRating>(d.maxRatings);
            profile.dailyGold = d.battleDay == DateTime.UtcNow.ToString("yyyy-MM-dd") ? d.dailyGold : 0;
            profile.seasonRollovers = new List<Profile.SeasonRollOverStruct>();
            DateTime now = DateTime.UtcNow, epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int seasonEnd = (int)(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1) - epoch).TotalSeconds;
            int left = Math.Max(0, seasonEnd - (int)(now - epoch).TotalSeconds);
            foreach (string mode in new string[] { RankedPvP, VersusArena })
            {
                // The client counts down from secondsUntilEnd/secondsUntilPreview (relative to the fetch), not the timestamps.
                Profile.SeasonRollOverStruct season = d.rollovers.Find(delegate(Profile.SeasonRollOverStruct x) { return x.ratingMode == mode; });
                if (season == null) { season = new Profile.SeasonRollOverStruct(); season.ratingMode = mode; season.status = Profile.SeasonRollOverStruct.RolloverStatus.None; }
                season.seasonEndUnixTimeUTC = seasonEnd; season.monthlyPreviewUnixTimeUTC = seasonEnd;
                season.secondsUntilEnd = left; season.secondsUntilPreview = left;
                profile.seasonRollovers.Add(season);
            }
        }

        // Match end (main thread): Ranked (VersusBattle), Versus Arena, casual Versus (unranked record only).
        public static void RecordMatch(OfflinePlayerStateData d, string mode, bool won)
        {
            if (mode == UnrankedPvP)
            {
                Profile.ProfileRating casual = Rating(d, UnrankedPvP); if (won) casual.wins++; else casual.losses++;
                if (won) BattleVictory(d);
                return;
            }
            Profile.ProfileRating r = Rating(d, mode);
            int streak = Streak(d, mode, won);
            if (r.rank == 0)
            {
                // Legend games count on the legend record only: the client sums rankedPvP + unrankedPvP + legend wins.
                Profile.ProfileRating legend = Rating(d, mode == VersusArena ? ArenaLegend : Legend);
                if (won) legend.wins++; else legend.losses++;
                legend.rank = Math.Max(1, 1000 - 10 * (legend.wins - legend.losses));
                if (won && mode == RankedPvP) BattleVictory(d);
                return;
            }
            if (won) r.wins++; else r.losses++;
            if (won && mode == RankedPvP) BattleVictory(d);
            if (won)
            {
                int gain = streak >= 3 && r.rank >= 5 && r.rankStars >= 0 ? 2 : 1;
                for (int i = 0; i < gain && r.rank > 0; i++)
                {
                    if (r.rankStars < 0) { r.rankStars = 0; continue; } // out of the Serpent
                    r.rankStars++;
                    if (r.rankStars >= StarsPerRank[r.rank])
                    {
                        r.rank--; r.rankStars = 0;
                        if (r.rank == 0) { Profile.ProfileRating legend = Rating(d, mode == VersusArena ? ArenaLegend : Legend); if (legend.rank == 12) legend.rank = 1000; }
                    }
                }
            }
            else if (r.rank <= 10)
            {
                // Serpent (client SERPENT_STAR_COUNT = 2): 0 -> -1 -> -2; rank is never lost within a season.
                r.rankStars = r.rankStars > 0 ? r.rankStars - 1 : Math.Max(-2, r.rankStars - 1);
            }
            Profile.ProfileRating best = d.maxRatings.Find(delegate(Profile.ProfileRating x) { return x.ratingMode == mode; });
            if (best == null) { best = Default(mode); d.maxRatings.Add(best); }
            if (r.rank < best.rank || (r.rank == best.rank && r.rankStars > best.rankStars)) { best.rank = r.rank; best.rankStars = r.rankStars; best.rankSeen = r.rank; best.rankStarsSeen = r.rankStars; }
        }

        public static void CheckSeason(OfflinePlayerStateData d)
        {
            string season = DateTime.UtcNow.ToString("yyyy-MM");
            if (string.IsNullOrEmpty(d.rankedSeason)) { d.rankedSeason = season; return; }
            if (d.rankedSeason == season) return;
            string ended = d.rankedSeason;
            d.rankedSeason = season;
            foreach (string mode in new string[] { RankedPvP, VersusArena })
            {
                Profile.ProfileRating r = d.ratings.Find(delegate(Profile.ProfileRating x) { return x.ratingMode == mode; });
                if (r == null || d.rollovers.Exists(delegate(Profile.SeasonRollOverStruct x) { return x.ratingMode == mode; })) continue;
                int rank = Math.Max(0, Math.Min(12, r.rank));
                Profile.SeasonRollOverStruct ready = new Profile.SeasonRollOverStruct();
                ready.ratingMode = mode; ready.status = Profile.SeasonRollOverStruct.RolloverStatus.Ready;
                ready.pendingGold = SeasonGold[rank]; ready.pendingGems = SeasonGems[rank];
                ready.pendingRank = SeasonReset[rank]; ready.pendingRankStars = 0;
                // Monthly card: one copy per milestone reached (The Mage 9, The Warrior 5, The Thief 1; Legend counts all three).
                int milestones = (rank <= 9 ? 1 : 0) + (rank <= 5 ? 1 : 0) + (rank <= 1 ? 1 : 0);
                int monthly = MonthlyCard(ended);
                if (mode == RankedPvP && milestones > 0 && monthly != 0) { ready.pendingCardTypeHash = monthly; ready.pendingCardCount = milestones; }
                d.rollovers.Add(ready);
            }
        }

        public static void ClaimRollovers(OfflinePlayerStateData d)
        {
            foreach (Profile.SeasonRollOverStruct ready in d.rollovers)
            {
                if (ready.status != Profile.SeasonRollOverStruct.RolloverStatus.Ready) continue;
                d.gold += ready.pendingGold; d.gems += ready.pendingGems;
                if (ready.pendingCardTypeHash != 0 && ready.pendingCardCount > 0) d.AddCards(ready.pendingCardTypeHash, ready.pendingCardCount);
                Profile.ProfileRating r = Rating(d, ready.ratingMode);
                r.rank = ready.pendingRank; r.rankStars = ready.pendingRankStars; r.rankSeen = r.rank; r.rankStarsSeen = r.rankStars;
            }
            d.rollovers.Clear();
        }

        // UESP Legends:Gold: "15 to 50 gold for every three ranked or casual battles, plus a random card", at most ten times a
        // day; reaching the limit earns The Persistent ("Reach the gold limit for a day for versus battle").
        public const int BattleWinsPerReward = 3, BattleRewardsPerDay = 10, DailyGoldLimit = 180;
        private static readonly System.Random random = new System.Random();
        public static void BattleVictory(OfflinePlayerStateData d)
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (d.battleDay != today) { d.battleDay = today; d.battleWins = 0; d.battleBags = 0; d.dailyGold = 0; }
            d.battleWins++;
            // BattleVictoriesWin draws its checkmarks from lifetime rankedPvP + unrankedPvP + legend wins (% 3) and hides the
            // bag at dailyGold >= 180 (client constant); UESP: at most ten rewards a day.
            int total = 0;
            foreach (Profile.ProfileRating x in d.ratings) if (x.ratingMode == RankedPvP || x.ratingMode == UnrankedPvP || x.ratingMode == Legend) total += x.wins;
            if (total % BattleWinsPerReward != 0 || d.battleBags >= BattleRewardsPerDay || d.dailyGold >= DailyGoldLimit) return;
            d.battleBags++;
            int gold;
            lock (random) gold = random.Next(15, 51);
            d.dailyGold += gold;
            Rewards.RewardDescription reward = new Rewards.RewardDescription();
            reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.Game;
            reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                rewardPassIndex = 0, bundleKey = "arena_gold_" + gold, bundleCount = 1, gold = gold, rarity = Rewards.RewardRarity.Common,
                rewardContainerType = Rewards.RewardContainer.Bag } };
            int card = OfflinePackService.RandomCard(d);
            if (card != 0) reward.rewardPasses.Add(new Rewards.RewardPassDescription { rewardPassIndex = 1, bundleKey = BattleCardPrefix + card, bundleCount = 1,
                rarity = Rewards.RewardRarity.Common, rewardContainerType = Rewards.RewardContainer.Bag });
            d.rewards.Add(reward);
            if (d.battleBags >= BattleRewardsPerDay || d.dailyGold >= DailyGoldLimit) OfflineTitles.Grant(d, "title_the_persistent");
        }
        public const string BattleCardPrefix = "battle_card_";

        // Monthly cards: after December 2020 ranked rewards cycled through the released monthly cards (UESP Legends:Monthly_Card).
        // Offline: the installed Monthlies set (production cards, ordered by id), one per season month.
        private static volatile List<int> monthlyCards = new List<int>();
        public static void PublishMonthlies(SPTESL.GameConfiguration config)
        {
            List<int> ids = new List<int>();
            foreach (SPTESL.CardSet set in config.CardCollection)
            {
                string key = (set.Name ?? "").ToLowerInvariant();
                if (!key.Contains("monthl")) continue;
                foreach (SPTESL.Card c in set.Cards) if (c != null && c.Production && !string.IsNullOrEmpty(c.ExportCode) && !ids.Contains(c.ResourceId.Id)) ids.Add(c.ResourceId.Id);
            }
            ids.Sort();
            monthlyCards = ids;
            UnityEngine.Debug.Log("OFFLINE RANKED: " + ids.Count + " monthly cards.");
        }

        public static int MonthlyCard(string season)
        {
            List<int> cards = monthlyCards;
            DateTime month;
            if (cards.Count == 0 || !DateTime.TryParseExact(season ?? "", "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out month)) return 0;
            int index = (month.Year * 12 + month.Month) % cards.Count;
            return cards[index];
        }

        private static Profile.ProfileRating Rating(OfflinePlayerStateData d, string mode)
        {
            Profile.ProfileRating r = d.ratings.Find(delegate(Profile.ProfileRating x) { return x.ratingMode == mode; });
            if (r == null) { r = Default(mode); d.ratings.Add(r); }
            return r;
        }

        private static Profile.ProfileRating Default(string mode)
        {
            Profile.ProfileRating r = new Profile.ProfileRating();
            r.ratingMode = mode; r.rank = 12; r.rankSeen = 12;
            return r;
        }

        private static int Streak(OfflinePlayerStateData d, string mode, bool won)
        {
            int i = d.rankedStreakModes.IndexOf(mode);
            if (i < 0) { d.rankedStreakModes.Add(mode); d.rankedStreaks.Add(0); i = d.rankedStreaks.Count - 1; }
            d.rankedStreaks[i] = won ? d.rankedStreaks[i] + 1 : 0;
            return d.rankedStreaks[i];
        }
    }
}
