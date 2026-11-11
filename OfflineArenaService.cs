using System;
using System.Collections.Generic;
using SPTESL;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Local Solo Arena (Arena service, "solo" instances): purchase (150 gold or 1 ticket) →
    // choose one of three installed ArenaClasses → draft 30 cards from three offers each →
    // a ladder of 9 matches; the run ends at 9 wins or 3 losses and grants SoloArena rewards.
    // Installed data used: SoloArenaData.ArenaClasses (colours), PossibleLanes, BossScenarios,
    // AiOpponents (portraits), CustomDecks AvailableInArena (opponents). The original's
    // scenario modifiers, featured matches, slot weights and reward tables were server data
    // (NonSerialized); everything not listed above is a reconstruction:
    //   offers: 3 cards of one rarity (Common 70% / Rare 20% / Epic 7% / Legendary 3%) in the
    //   class colours + Neutral; match 9 is a boss scenario; one lane per match is a random
    //   supported special lane, the other Field; rewards scale with wins (gold, gems, packs).
    public static class OfflineArenaService
    {
        public const int CostGold = 150, CostTickets = 1, Picks = 30, Matches = 9, MaxLosses = 3, VersusMaxWins = 7; // VersusArena.MaxVersusArenaWins_
        public const string Solo = "solo", Versus = "versus";
        private static volatile ArenaContent content;

        private sealed class ArenaContent
        {
            public List<int> Classes = new List<int>();
            public Dictionary<int, List<CardSubtype>> ClassColors = new Dictionary<int, List<CardSubtype>>();
            public List<int>[] CardsByRarity = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            public Dictionary<int, List<CardSubtype>> CardColors = new Dictionary<int, List<CardSubtype>>();
            public List<int> Opponents = new List<int>(), Avatars = new List<int>(), Bosses = new List<int>();
            public List<CardLaneType> Lanes = new List<CardLaneType>();
        }

        // Main thread, once the configuration has loaded.
        public static void Publish(GameConfiguration config)
        {
            ArenaContent c = new ArenaContent();
            foreach (ArenaClass arenaClass in config.SoloArenaData.ArenaClasses)
            {
                c.Classes.Add(arenaClass.ResourceId.Id);
                c.ClassColors[arenaClass.ResourceId.Id] = new List<CardSubtype>(arenaClass.Colors);
            }
            CardSubtype[] colors = { CardSubtype.Strength, CardSubtype.Intelligence, CardSubtype.Willpower, CardSubtype.Agility, CardSubtype.Endurance, CardSubtype.Neutral };
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    if (card == null || !card.Production || card.Source != CardSource.Normal || string.IsNullOrEmpty(card.ExportCode) || card.InvalidArenaPick) continue;
                    if (card.AlternateOfCard != null && card.AlternateOfCard.resourceId != 0 && card.AlternateOfCard.resourceId != UniqueResourceId.GetInvalid().Id) continue;
                    List<CardSubtype> own = new List<CardSubtype>();
                    foreach (CardSubtype color in colors) if (card.HasSubtype(color)) own.Add(color);
                    if (own.Count == 0) continue;
                    c.CardsByRarity[Mathf.Clamp((int)card.Rarity, 0, 3)].Add(card.ResourceId.Id);
                    c.CardColors[card.ResourceId.Id] = own;
                }
            foreach (CustomDeck deck in config.CustomDecks) if (deck != null && deck.Production && deck.AvailableInArena && deck.Decks.Count > 0) c.Opponents.Add(deck.ResourceId.Id);
            foreach (AiOpponent ai in config.AiOpponents) if (ai != null) c.Avatars.Add(ai.ResourceId.Id);
            foreach (UniqueResourceReference boss in config.SoloArenaData.BossScenarios) if (GameManager.Instance.FindScenario(boss.resourceId) != null) c.Bosses.Add(boss.resourceId);
            foreach (CardLaneType lane in config.SoloArenaData.PossibleLanes)
                if (lane == CardLaneType.Default || lane == CardLaneType.Stealth || Array.IndexOf(StoryLaneRules.Supported, lane) >= 0) c.Lanes.Add(lane);
            if (c.Lanes.Count == 0) c.Lanes.Add(CardLaneType.Stealth);
            content = c;
        }

        public static void Register(OfflineLocalServices services, OfflinePlayerState state)
        {
            services.Register("arena/fetchActiveArenaInstances", delegate {
                Arena.FetchArenaIntancesResult result = new Arena.FetchArenaIntancesResult();
                result.resultCode = ResultCode.Ok; result.clientData = Client(state.Snapshot());
                return JsonUtility.ToJson(result); });
            services.Register("arena/purchaseArena", delegate(string m, string p, string body) {
                Arena.PurchaseArenaRequest request = JsonUtility.FromJson<Arena.PurchaseArenaRequest>(body);
                bool ok = false;
                if (request != null && (request.arenaType == Solo || request.arenaType == Versus) && content != null)
                    state.Mutate(delegate(OfflinePlayerStateData d) {
                        Arena.ArenaClientInstance current = Run(d, request.arenaType);
                        if (current != null && !current.isCompleted) return;
                        if (request.currencyType == "tickets") { if (d.tickets < CostTickets) return; d.tickets -= CostTickets; }
                        else { if (d.gold < CostGold) return; d.gold -= CostGold; }
                        Arena.ArenaClientInstance fresh = NewRun(request.arenaType);
                        if (request.arenaType == Versus) d.versusArena = fresh; else d.arena = fresh;
                        ok = true; });
                Arena.PurchaseArenaResult result = new Arena.PurchaseArenaResult();
                result.resultCode = ok ? ResultCode.Ok : ResultCode.ClientHttpErrorBadRequest; result.clientData = Client(state.Snapshot());
                return JsonUtility.ToJson(result); });
            services.Register("arena/selectClassType", delegate(string m, string p, string body) {
                Arena.SelectClassRequest request = JsonUtility.FromJson<Arena.SelectClassRequest>(body);
                bool ok = false;
                if (request != null) state.Mutate(delegate(OfflinePlayerStateData d) {
                    Arena.ArenaClientInstance run = Run(d, request.arenaType);
                    if (run == null || run.phase != Arena.ArenaPhase.ClassSelection || !run.potentialClassTypeHashes.Contains(request.classTypeHash)) return;
                    run.selectedClassTypeHash = request.classTypeHash; run.phase = Arena.ArenaPhase.CardSelection;
                    run.cardSelectionTypeHashes = Offer(run); ok = true; });
                Arena.SelectClassResult result = new Arena.SelectClassResult();
                result.resultCode = ok ? ResultCode.Ok : ResultCode.ClientHttpErrorBadRequest; result.clientData = Client(state.Snapshot());
                return JsonUtility.ToJson(result); });
            services.Register("arena/selectCard", delegate(string m, string p, string body) {
                Arena.SelectCardRequest request = JsonUtility.FromJson<Arena.SelectCardRequest>(body);
                bool ok = false;
                if (request != null) state.Mutate(delegate(OfflinePlayerStateData d) {
                    Arena.ArenaClientInstance run = Run(d, request.arenaType);
                    if (run == null || run.phase != Arena.ArenaPhase.CardSelection || !run.cardSelectionTypeHashes.Contains(request.cardTypeHash)) return;
                    run.cardTypeHashes.Add(request.cardTypeHash); run.premiums.Add(false);
                    if (run.cardTypeHashes.Count >= Picks) { run.phase = Arena.ArenaPhase.MatchSelection; run.cardSelectionTypeHashes = new List<int>(); run.matches = run.arenaType == Versus ? VersusMatches() : Ladder(); }
                    else run.cardSelectionTypeHashes = Offer(run);
                    ok = true; });
                Arena.SelectCardResult result = new Arena.SelectCardResult();
                result.resultCode = ok ? ResultCode.Ok : ResultCode.ClientHttpErrorBadRequest; result.clientData = Client(state.Snapshot());
                return JsonUtility.ToJson(result); });
            services.Register("arena/resignArena", delegate(string m, string p, string body) {
                Arena.ResignArenaRequest request = JsonUtility.FromJson<Arena.ResignArenaRequest>(body);
                string type = request != null && request.arenaType == Versus ? Versus : Solo;
                state.Mutate(delegate(OfflinePlayerStateData d) { Arena.ArenaClientInstance run = Run(d, type); if (run != null && !run.isCompleted) Finish(d, type); });
                return JsonUtility.ToJson(new Arena.ResignArenaResult { resultCode = ResultCode.Ok }); });
        }

        // JsonUtility cannot store null for a serializable class: an absent run reloads as an empty instance.
        private static Arena.ArenaClientInstance Run(OfflinePlayerStateData d) { return Run(d, Solo); }

        private static Arena.ArenaClientInstance Run(OfflinePlayerStateData d, string type)
        {
            Arena.ArenaClientInstance run = type == Versus ? d.versusArena : d.arena;
            return run != null && run.arenaType == (type == Versus ? Versus : Solo) ? run : null;
        }

        private static Arena.ArenaClientDataStruct Client(OfflinePlayerStateData d)
        {
            Arena.ArenaClientDataStruct data = new Arena.ArenaClientDataStruct();
            data.costGold = CostGold; data.costTickets = CostTickets;
            data.instances = new List<Arena.ArenaClientInstance>();
            foreach (string type in new string[] { Solo, Versus })
            {
                Arena.ArenaClientInstance run = Run(d, type);
                if (run != null && (!run.isCompleted || run.hasRewards)) data.instances.Add(run);
            }
            return data;
        }

        // Used from the services listener thread and the main thread (RecordResult): System.Random is not thread-safe.
        private static readonly System.Random random = new System.Random();
        private static int Rnd(int max) { lock (random) return random.Next(max); }
        private static double RndDouble() { lock (random) return random.NextDouble(); }

        private static Arena.ArenaClientInstance NewRun(string type)
        {
            Arena.ArenaClientInstance run = new Arena.ArenaClientInstance();
            bool versus = type == Versus;
            run.arenaType = versus ? Versus : Solo; run.arenaTypeEnum = versus ? Arena.ArenaType.Versus : Arena.ArenaType.Solo; run.phase = Arena.ArenaPhase.ClassSelection;
            List<int> classes = new List<int>(content.Classes);
            run.potentialClassTypeHashes = new List<int>();
            while (run.potentialClassTypeHashes.Count < 3 && classes.Count > 0)
            { int i = Rnd(classes.Count); run.potentialClassTypeHashes.Add(classes[i]); classes.RemoveAt(i); }
            run.cardSelectionTypeHashes = new List<int>(); run.cardTypeHashes = new List<int>(); run.premiums = new List<bool>();
            run.matches = new List<Arena.ArenaClientMatch>();
            return run;
        }

        private static List<int> Offer(Arena.ArenaClientInstance run)
        {
            List<CardSubtype> colors;
            if (!content.ClassColors.TryGetValue(run.selectedClassTypeHash, out colors)) colors = new List<CardSubtype>();
            double roll = RndDouble();
            int rarity = roll < 0.70 ? 0 : roll < 0.90 ? 1 : roll < 0.97 ? 2 : 3;
            for (; rarity >= 0; rarity--)
            {
                List<int> fit = content.CardsByRarity[rarity].FindAll(delegate(int id) {
                    foreach (CardSubtype c in content.CardColors[id]) if (c != CardSubtype.Neutral && !colors.Contains(c)) return false;
                    return true; });
                if (fit.Count < 3) continue;
                List<int> offer = new List<int>();
                while (offer.Count < 3) { int id = fit[Rnd(fit.Count)]; if (!offer.Contains(id)) offer.Add(id); }
                return offer;
            }
            throw new InvalidOperationException("No arena draft cards for class " + run.selectedClassTypeHash);
        }

        private static List<Arena.ArenaClientMatch> Ladder()
        {
            List<Arena.ArenaClientMatch> matches = new List<Arena.ArenaClientMatch>();
            for (int i = 0; i < Matches; i++)
            {
                Arena.ArenaClientMatch match = new Arena.ArenaClientMatch();
                match.orderIndex = i;
                match.deckTypeHash = content.Opponents.Count == 0 ? 0 : content.Opponents[Rnd(content.Opponents.Count)];
                match.avatarTypeHash = content.Avatars.Count == 0 ? 0 : content.Avatars[Rnd(content.Avatars.Count)];
                match.scenarioTypeHash = i == Matches - 1 && content.Bosses.Count > 0 ? content.Bosses[Rnd(content.Bosses.Count)] : UniqueResourceId.GetInvalid().Id;
                match.scenarioModifierTypeHash = UniqueResourceId.GetInvalid().Id;
                match.featuredMatchTypeHash = UniqueResourceId.GetInvalid().Id;
                match.laneTypeHash1 = (int)CardLaneType.Default;
                match.laneTypeHash2 = (int)content.Lanes[Rnd(content.Lanes.Count)];
                match.canSelect = i == 0;
                matches.Add(match);
            }
            return matches;
        }

        // Versus Arena: one open entry (the client queues matches[0].orderIndex) that accumulates the run's record;
        // every game is against a fresh random AI opponent on Field + Shadow lanes.
        private static List<Arena.ArenaClientMatch> VersusMatches()
        {
            Arena.ArenaClientMatch match = new Arena.ArenaClientMatch();
            match.orderIndex = 0; match.canSelect = true;
            NextVersusOpponent(match);
            match.scenarioTypeHash = UniqueResourceId.GetInvalid().Id; match.scenarioModifierTypeHash = UniqueResourceId.GetInvalid().Id;
            match.featuredMatchTypeHash = UniqueResourceId.GetInvalid().Id;
            match.laneTypeHash1 = (int)CardLaneType.Default; match.laneTypeHash2 = (int)CardLaneType.Stealth;
            return new List<Arena.ArenaClientMatch> { match };
        }

        private static void NextVersusOpponent(Arena.ArenaClientMatch match)
        {
            match.deckTypeHash = content.Opponents.Count == 0 ? 0 : content.Opponents[Rnd(content.Opponents.Count)];
            match.avatarTypeHash = content.Avatars.Count == 0 ? 0 : content.Avatars[Rnd(content.Avatars.Count)];
        }

        // Arena ranks (profile ArenaRankItem: ratings "soloArena" / "versusArena", 9 Pit Dog .. 1 Grand Champion).
        // UESP Legends:Solo_Arena: rank up by defeating opponents: 7 wins per rank until Hero (3), then 9 per rank up to
        // Grand Champion (1); each promotion pays 50 gold; the first Grand Champion (either arena) grants a premium Adoring Fan.
        // Solo and Versus Arena rank independently with the same progression. Wins count as they happen (reconstruction:
        // UESP does not say whether promotion waits for the run's end).
        public const string SoloRank = "soloArena", VersusRank = "versusArena";
        public const int PromotionGold = 50;

        public static int RankForWins(int wins)
        {
            int rank = 9, needed = 0;
            while (rank > 1)
            {
                int step = rank > 3 ? 7 : 9;
                if (wins < needed + step) break;
                needed += step; rank--;
            }
            return rank;
        }

        public static void ArenaWin(OfflinePlayerStateData d, string type)
        {
            string mode = type == Versus ? VersusRank : SoloRank;
            Profile.ProfileRating r = d.ratings.Find(delegate(Profile.ProfileRating x) { return x.ratingMode == mode; });
            if (r == null) { r = new Profile.ProfileRating(); r.ratingMode = mode; r.rank = 9; r.rankSeen = 9; d.ratings.Add(r); }
            r.wins++;
            int target = RankForWins(r.wins);
            while (r.rank > target)
            {
                r.rank--;
                Rewards.RewardDescription reward = new Rewards.RewardDescription();
                reward.rewardId = ++d.nextRewardId; reward.source = Rewards.RewardSource.Game;
                reward.rewardPasses = new List<Rewards.RewardPassDescription> { new Rewards.RewardPassDescription {
                    rewardPassIndex = 0, bundleKey = "arena_gold_" + PromotionGold, bundleCount = 1, gold = PromotionGold,
                    rarity = Rewards.RewardRarity.Common, rewardContainerType = Rewards.RewardContainer.None } };
                d.rewards.Add(reward);
                if (r.rank == 1 && !d.adoringFanGranted)
                {
                    int fan = OfflineTitles.CardId("Adoring Fan");
                    if (fan != 0) { d.AddCards(fan, 0, 1); d.adoringFanGranted = true; }
                }
            }
            Profile.ProfileRating best = d.maxRatings.Find(delegate(Profile.ProfileRating x) { return x.ratingMode == mode; });
            if (best == null) { best = new Profile.ProfileRating(); best.ratingMode = mode; best.rank = 9; best.rankSeen = 9; d.maxRatings.Add(best); }
            if (r.rank < best.rank) { best.rank = r.rank; best.rankSeen = r.rank; }
            best.wins = Math.Max(best.wins, r.wins);
        }

        public static void RecordResult(OfflinePlayerStateData d, string type, int orderIndex, bool won)
        {
            if (type != Versus) { RecordResult(d, orderIndex, won); return; }
            Arena.ArenaClientInstance run = Run(d, Versus);
            if (run == null || run.isCompleted || run.matches == null || run.matches.Count == 0) return;
            Arena.ArenaClientMatch match = run.matches[0];
            if (won) { match.wins++; ArenaWin(d, Versus); } else match.losses++; // rank counts only validated results (Astra, T4-6)
            if (match.wins >= VersusMaxWins || match.losses >= MaxLosses) { match.completed = true; match.canSelect = false; Finish(d, Versus); }
            else NextVersusOpponent(match);
        }

        // Match result for the run (main thread, from OfflineNativeMatch).
        public static void RecordResult(OfflinePlayerStateData d, int orderIndex, bool won)
        {
            Arena.ArenaClientInstance run = Run(d);
            if (run == null || run.isCompleted || run.matches == null) return;
            Arena.ArenaClientMatch match = run.matches.Find(delegate(Arena.ArenaClientMatch x) { return x.orderIndex == orderIndex; });
            if (match == null || match.completed) return;
            if (won) { match.wins++; match.completed = true; match.canSelect = false; ArenaWin(d, Solo); } // validated result only (T4-6)
            else match.losses++;
            int wins = 0, losses = 0;
            foreach (Arena.ArenaClientMatch m in run.matches) { wins += m.wins; losses += m.losses; }
            Arena.ArenaClientMatch next = run.matches.Find(delegate(Arena.ArenaClientMatch x) { return !x.completed; });
            if (next != null) next.canSelect = true;
            if (wins >= Matches || losses >= MaxLosses) Finish(d);
        }

        // Once no unclaimed arena reward remains, a finished run stops reporting rewards (and leaves the instance list).
        public static void AfterRewardsClaimed(OfflinePlayerStateData d)
        {
            foreach (string type in new string[] { Solo, Versus })
            {
                Arena.ArenaClientInstance run = Run(d, type);
                if (run == null || !run.isCompleted || !run.hasRewards) continue;
                Rewards.RewardSource source = type == Versus ? Rewards.RewardSource.VersusArena : Rewards.RewardSource.SoloArena;
                if (!d.rewards.Exists(delegate(Rewards.RewardDescription r) { return r.source == source; })) run.hasRewards = false;
            }
        }

        private static void Finish(OfflinePlayerStateData d) { Finish(d, Solo); }

        // Rewards by wins (reconstruction): gold 20/win, soul gems 10/win, a Core pack from 3 wins, a second from 7.
        // Versus Arena (7 wins max) pays more per win: gold 30/win, gems 15/win, packs at 3, 5 and 7 wins.
        private static void Finish(OfflinePlayerStateData d, string type)
        {
            Arena.ArenaClientInstance run = Run(d, type);
            bool versus = type == Versus;
            int wins = 0;
            int losses = 0;
            foreach (Arena.ArenaClientMatch m in run.matches) { wins += m.wins; losses += m.losses; }
            OfflineTitles.ArenaFinished(d, versus, wins, losses); // undefeated-run titles
            // The client enters its arena RewardsState only while the finished run reports hasRewards.
            run.isCompleted = true; run.hasRewards = true;
            Rewards.RewardDescription reward = new Rewards.RewardDescription();
            reward.rewardId = ++d.nextRewardId; reward.source = versus ? Rewards.RewardSource.VersusArena : Rewards.RewardSource.SoloArena;
            int goldPerWin = versus ? 30 : 20, gemsPerWin = versus ? 15 : 10;
            reward.rewardPasses = new List<Rewards.RewardPassDescription>();
            reward.rewardPasses.Add(new Rewards.RewardPassDescription { rewardPassIndex = 0, bundleKey = "arena_gold_" + (goldPerWin * Math.Max(1, wins)), bundleCount = 1, rarity = Rewards.RewardRarity.Common, rewardContainerType = Rewards.RewardContainer.Bag, gold = goldPerWin * Math.Max(1, wins) });
            if (wins > 0) reward.rewardPasses.Add(new Rewards.RewardPassDescription { rewardPassIndex = 1, bundleKey = "arena_gems_" + (gemsPerWin * wins), bundleCount = 1, rarity = Rewards.RewardRarity.Rare, rewardContainerType = Rewards.RewardContainer.Bag, gems = gemsPerWin * wins });
            int packs = versus ? (wins >= 3 ? 1 : 0) + (wins >= 5 ? 1 : 0) + (wins >= 7 ? 1 : 0) : (wins >= 3 ? 1 : 0) + (wins >= 7 ? 1 : 0);
            for (int i = 0; i < packs; i++)
                reward.rewardPasses.Add(new Rewards.RewardPassDescription { rewardPassIndex = reward.rewardPasses.Count, bundleKey = "arena_pack", bundleCount = 1, rarity = Rewards.RewardRarity.Epic, rewardContainerType = Rewards.RewardContainer.Bag });
            d.rewards.Add(reward);
        }

        // The run's drafted deck and the match's opponent/scenario/lanes (main thread).
        public static bool MatchSetup(OfflinePlayerStateData d, int orderIndex, out List<int> deck, out Arena.ArenaClientMatch match)
        { return MatchSetup(d, Solo, orderIndex, out deck, out match); }

        public static bool MatchSetup(OfflinePlayerStateData d, string type, int orderIndex, out List<int> deck, out Arena.ArenaClientMatch match)
        {
            deck = null; match = null;
            Arena.ArenaClientInstance run = Run(d, type);
            if (run == null || run.isCompleted || run.phase != Arena.ArenaPhase.MatchSelection) return false;
            match = run.matches.Find(delegate(Arena.ArenaClientMatch x) { return x.orderIndex == orderIndex; });
            deck = new List<int>(run.cardTypeHashes);
            return match != null && !match.completed;
        }
    }
}
