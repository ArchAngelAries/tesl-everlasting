using System;
using System.Collections.Generic;
using SparkleServices;
using SPTESL;

namespace OfflineEngine
{
    // Account XP and levels. The client derives the level from total XP (ProfileUtils: up to level 50) and animates the
    // level bar from profile.xp plus the xp of unclaimed container-less reward passes, so match XP is a Game-source reward
    // pass ("xp_<n>", container None) credited when the client claims it. Level-ups add a LevelUp-source reward with the
    // level's bundle (data/level-rewards.tsv, UESP Legends:Leveling).
    // XP per match, UESP Legends:Leveling (win/lose): Story 32/9, Puzzles 32/0, Practice 10/3, Solo Arena 24/7,
    // Versus Arena 35/10, Ranked 35/10, Casual 35/10. Loss XP "can be higher based on performance": the minimum is used.
    public static class OfflineLevelService
    {
        public const string XpPrefix = "xp_", LevelPrefix = "level_reward_";
        public const int MaxLevel = 50;

        public static int XpFor(string mode, bool won)
        {
            switch (mode)
            {
                case "story": return won ? 32 : 9;
                case "puzzle": return won ? 32 : 0;
                case "practice": return won ? 10 : 3;
                case "soloArena": return won ? 24 : 7;
                case "versusArena": case "ranked": case "casual": return won ? 35 : 10;
                default: return 0;
            }
        }

        // Match end: a container-less Game reward carrying the XP (the client shows and claims it on the end screen).
        public static void AwardMatch(OfflinePlayerStateData d, string mode, bool won)
        {
            int xp = XpFor(mode, won);
            if (xp <= 0 || Level(d.xp) >= MaxLevel) return;
            Rewards.RewardDescription reward = new Rewards.RewardDescription();
            reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.Game;
            reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                rewardPassIndex = 0, bundleKey = XpPrefix + xp, bundleCount = 1, xp = xp, rarity = Rewards.RewardRarity.Common,
                rewardContainerType = Rewards.RewardContainer.None } };
            d.rewards.Add(reward);
        }

        // Claiming XP: credit it and queue each new level's reward.
        public static void Credit(OfflinePlayerStateData d, int xp)
        {
            int before = Level(d.xp);
            d.xp += xp;
            int after = Level(d.xp);
            d.level = after;
            for (int level = before + 1; level <= after; level++)
            {
                Rewards.RewardDescription reward = new Rewards.RewardDescription();
                reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.LevelUp;
                reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                    rewardPassIndex = 0, bundleKey = LevelPrefix + level, bundleCount = 1, rarity = Rewards.RewardRarity.Rare,
                    rewardContainerType = Rewards.RewardContainer.Bag } };
                d.rewards.Add(reward);
            }
        }

        // ProfileUtils.GetXpNeededForLevelUp / CalculateXpData, as in the client.
        public static int XpNeeded(int level) { return 5 + level * 20 + ((int)Math.Ceiling((level + 1) / 10.0) - 1) * -25; }

        public static int Level(int xpTotal)
        {
            int level = 1, rest = xpTotal;
            while (level < MaxLevel && rest - XpNeeded(level) >= 0) { rest -= XpNeeded(level); level++; }
            return level;
        }

        // Race of the selected avatar ("AvatarBreton1" -> Breton) for race-based level cards.
        public static CardSubtype AvatarRace(OfflinePlayerStateData d)
        {
            string avatar = (d.GetSelected("avatar") ?? "").ToLowerInvariant();
            string[] names = { "darkelf", "highelf", "woodelf", "breton", "imperial", "nord", "orc", "redguard", "khajiit", "argonian" };
            CardSubtype[] races = { CardSubtype.Dark_Elf, CardSubtype.High_Elf, CardSubtype.Wood_Elf, CardSubtype.Breton, CardSubtype.Imperial,
                CardSubtype.Nord, CardSubtype.Orc, CardSubtype.Redguard, CardSubtype.Khajiit, CardSubtype.Argonian };
            for (int i = 0; i < names.Length; i++) if (avatar.Contains(names[i])) return races[i];
            return CardSubtype.None;
        }
    }
}
