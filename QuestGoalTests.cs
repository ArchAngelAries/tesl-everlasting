using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Daily quest goal reconstruction (OfflineQuestService.Parse) on the native counting helper.
    public static class QuestGoalTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("quest-goal-parse-daily-templates", delegate {
                QuestGoal win = OfflineQuestService.Parse("Win 3 games:<sprite name=iconStrength><sprite name=iconAgility>");
                Require(win != null && win.Type == QuestGoalType.WinGame && win.Value == 3 && win.DeckAttributes.Count == 2, "Win with colours not parsed.");
                QuestGoal summon = OfflineQuestService.Parse("Summon 15 Endurance creatures");
                Require(summon != null && summon.Type == QuestGoalType.SummonCount && summon.Value == 15 && summon.CardRequirements.Count == 2, "Colour summon not parsed.");
                QuestGoal attack = OfflineQuestService.Parse("Attack your opponent 20 times.");
                Require(attack != null && attack.Type == QuestGoalType.AttackCount && attack.CardRequirements.Count == 1, "Attack opponent not parsed.");
                Require(OfflineQuestService.Parse("Summon 3 creatures while you have another creature in each lane 10 times.") == null, "Unsupported quest was accepted.");
                string[] texts = { "Summon 15 creatures to the Shadow lane.", "Summon 20 creatures Waxed or Waned.", "Play 6 cards as prophecies.", "Exalt 10 cards",
                    "Betray 10 creatures", "Use 10 Plot abilities", "Activate Expertise 10 times.", "Draw 15 extra cards.",
                    "Summon a God", "Play 10 items with Mobilize.", "Play 10 actions with Empower.", "Destroy 3 enemy supports.", "Destroy 10 enemy creatures with Cover." };
                QuestGoalType[] types = { QuestGoalType.CoverCount, QuestGoalType.WaxWaneTriggerCount, QuestGoalType.ProphecySummonCount, QuestGoalType.ExaltCount,
                    QuestGoalType.BetrayCount, QuestGoalType.PlotCount, QuestGoalType.ExpertiseCount, QuestGoalType.DrawExtraCard,
                    QuestGoalType.SummonCount, QuestGoalType.PlayCount, QuestGoalType.PlayCount, QuestGoalType.DestroyCount, QuestGoalType.DestroyCount };
                for (int i = 0; i < texts.Length; i++)
                {
                    QuestGoal g = OfflineQuestService.Parse(texts[i]);
                    Require(g != null && g.Type == types[i] && g.Value > 0, "Template not parsed: " + texts[i]);
                }
                Require(OfflineQuestService.RewardOf("DQ_40_Gold") == "gold:40" && OfflineQuestService.RewardOf("cardpack_Skyrim_quest_reward") == "pack:skyrim", "Quest rewards not parsed.");
            });
            test("cosmetics-categories-follow-ownership", delegate {
                OfflineCosmetics.Publish(config);
                Require(config.Titles.Count > 0, "No installed titles.");
                string key = config.Titles[0].ResourceId.UniqueName;
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                d.pendingGrants.Add("title:" + key);
                OfflineCosmetics.ClaimPending(d);
                Require(d.pendingGrants.Count == 0 && d.ownedTitles.Contains(key), "Pending title grant was not claimed.");
                OfflineCosmetics.GrantCardBack(d, "cardback_clockwork_city");
                OfflineCosmetics.GrantCardBack(d, "cardback_not_real");
                List<SparkleServices.GenericInventory.CategoryData> categories = OfflineCosmetics.Categories(d);
                SparkleServices.GenericInventory.CategoryData titles = categories[0], backs = categories[1];
                Require(titles.category == "title" && titles.available.Contains(key) && titles.available.Count + titles.locked.Count == config.Titles.Count, "Title category wrong.");
                foreach (TitleData title in config.Titles)
                    Require(titles.available.Contains(title.ResourceId.UniqueName) == (title.ResourceId.UniqueName == key || OfflineTitles.IsStarting(title.ResourceId.UniqueName)),
                        "Title availability wrong for " + title.ResourceId.UniqueName);
                Require(backs.category == "cardback" && backs.selected == "cardback_default" && backs.available.Count == 2 + OfflineMods.CardBackKeys.FindAll(OfflineMods.IsFreeCardBack).Count
                    && backs.locked.Count == OfflineCosmetics.AllCardBacks().FindAll(delegate(string k) { return !OfflineMods.IsFreeCardBack(k); }).Count - 2, "Card back category wrong.");
                Require(OfflineCosmetics.CanSelect(d, "cardback", "cardback_clockwork_city") && !OfflineCosmetics.CanSelect(d, "cardback", "cardback_legend")
                    && OfflineCosmetics.CanSelect(d, "title", key), "Selection rules wrong.");
                Require(OfflineCosmetics.TitleId(key) == config.Titles[0].ResourceId.Id, "Title id map wrong.");
                OfflinePlayerStateData house = new OfflinePlayerStateData();
                house.pendingGrants.Add("title:title_house_hlaalu_cousin");
                OfflineCosmetics.ClaimPending(house);
                Require(house.ownedTitles.Contains("title_house_hlaalu_cousin") && house.ownedCardBacks.Contains("cardback_house_hlaalu"), "Claimed pending title skipped its follow-ups.");
            });
            test("cardback-grants-from-completion-and-titles", delegate {
                SPTESL.Campaign puzzles = config.Campaigns.Find(delegate(SPTESL.Campaign c) { return c.ResourceId.UniqueName == "Puzzles"; });
                Require(puzzles != null, "Installed Puzzles campaign missing.");
                SPTESL.Act caius = puzzles.Acts.Find(delegate(SPTESL.Act a) { return a.Chapters.Count > 0 && a.Chapters[0].ResourceId.UniqueName.StartsWith("PUZ_Caius"); });
                Require(caius != null, "Caius act missing.");
                SparkleServices.Campaign.IUserCampaignData user = new SparkleServices.Campaign.IUserCampaignData();
                SparkleServices.Campaign.IUserActData act = new SparkleServices.Campaign.IUserActData { actId = caius.ResourceId.Id };
                List<SparkleServices.Campaign.IUserChapterData> chapters = new List<SparkleServices.Campaign.IUserChapterData>();
                foreach (SPTESL.Chapter chapter in caius.Chapters) chapters.Add(new SparkleServices.Campaign.IUserChapterData { chapterId = chapter.ResourceId.Id, isComplete = true });
                chapters[chapters.Count - 1].isComplete = false;
                act.chapterData = chapters.ToArray(); user.actData = new SparkleServices.Campaign.IUserActData[] { act };
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                OfflineCosmetics.AfterCampaignProgress(d, puzzles, user);
                Require(!d.ownedCardBacks.Contains("cardback_caius_training"), "Card back granted before the set was complete.");
                chapters[chapters.Count - 1].isComplete = true;
                OfflineCosmetics.AfterCampaignProgress(d, puzzles, user);
                Require(d.ownedCardBacks.Contains("cardback_caius_training") && d.ownedCardBacks.Count == 1, "Caius' Training card back not granted alone.");
                OfflineCosmetics.AfterTitleOwned(d, "title_house_hlaalu_cousin");
                Require(d.ownedCardBacks.Contains("cardback_house_hlaalu"), "House card back not granted with its title.");
                SPTESL.Act bonus = puzzles.Acts.Find(delegate(SPTESL.Act a) { return a.Chapters.Count > 0 && a.Chapters[0].ResourceId.UniqueName.StartsWith("PUZ_Bonus"); });
                Require(bonus != null && bonus.Chapters[0].ChapterEventsToComplete.Count == 20, "Installed Skooma Cat puzzle set changed.");
                List<SparkleServices.Campaign.IUserChapterData> bonusChapters = new List<SparkleServices.Campaign.IUserChapterData>();
                foreach (SPTESL.Chapter c in bonus.Chapters) bonusChapters.Add(new SparkleServices.Campaign.IUserChapterData { chapterId = c.ResourceId.Id, isComplete = false });
                user.actData = new[] { new SparkleServices.Campaign.IUserActData { actId = bonus.ResourceId.Id, chapterData = bonusChapters.ToArray() } };
                OfflinePlayerStateData bonusState = new OfflinePlayerStateData();
                OfflineCosmetics.AfterCampaignProgress(bonusState, puzzles, user);
                Require(!bonusState.pendingTitleRewards.Contains("title_2.15_TheFelonius"), "Puzzle title granted early.");
                foreach (SparkleServices.Campaign.IUserChapterData c in bonusChapters) c.isComplete = true;
                OfflineCosmetics.AfterCampaignProgress(bonusState, puzzles, user);
                Require(bonusState.pendingTitleRewards.Contains("title_2.15_TheFelonius") && bonusState.ownedCardBacks.Contains("cardback_EventsShiveringNormal"),
                    "Completing the Skooma Cat puzzles missed the title or card back.");
                Require(OfflineCosmetics.CardBacks.Length == 72, "Card back key list incomplete.");
            });
            test("ranked-ladder-stars-serpent-season", delegate {
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                string m = OfflineRankedService.RankedPvP;
                OfflineRankedService.RecordMatch(d, m, true); OfflineRankedService.RecordMatch(d, m, true);
                OfflineRankedService.RecordMatch(d, m, true); // third straight win at rank 12: bonus star, 4/4 -> rank 11
                SparkleServices.Profile.ProfileRating r = d.ratings.Find(delegate(SparkleServices.Profile.ProfileRating x) { return x.ratingMode == m; });
                Require(r.rank == 11 && r.rankStars == 0 && r.wins == 3, "Rank 12 win streak did not reach rank 11 (rank " + r.rank + ", stars " + r.rankStars + ").");
                OfflineRankedService.RecordMatch(d, m, false);
                Require(r.rank == 11 && r.rankStars == 0, "Loss protection at rank 11 failed.");
                r.rank = 10; r.rankStars = 0;
                OfflineRankedService.RecordMatch(d, m, false);
                Require(r.rank == 10 && r.rankStars == -1, "Loss at 0 stars from The Lord did not enter the Serpent.");
                OfflineRankedService.RecordMatch(d, m, false); OfflineRankedService.RecordMatch(d, m, false);
                Require(r.rank == 10 && r.rankStars == -2, "Serpent did not hold two stars (got " + r.rankStars + ").");
                OfflineRankedService.RecordMatch(d, m, true);
                Require(r.rank == 10 && r.rankStars == 0, "Win in the Serpent did not return to the constellation.");
                r.rank = 1; r.rankStars = 6;
                OfflineRankedService.RecordMatch(d, m, true);
                Require(r.rank == 0, "Completing The Thief did not reach Legend.");
                Require(d.ratings.Exists(delegate(SparkleServices.Profile.ProfileRating x) { return x.ratingMode == OfflineRankedService.Legend && x.rank == 1000; }), "Legend record not created at promotion.");
                OfflineRankedService.PublishMonthlies(config);
                r.rank = 8; r.rankStars = 2; d.rankedSeason = "2000-01"; d.gold = 0; d.gems = 0;
                OfflineRankedService.CheckSeason(d);
                Require(d.rollovers.Count == 1 && d.rollovers[0].pendingGold == 30 && d.rollovers[0].pendingGems == 110 && d.rollovers[0].pendingRank == 9, "Season-end rollover for rank 8 wrong.");
                Require(OfflineRankedService.MonthlyCard("2000-01") == 0 || (d.rollovers[0].pendingCardCount == 1 && d.rollovers[0].pendingCardTypeHash == OfflineRankedService.MonthlyCard("2000-01")), "Monthly card for one milestone wrong.");
                OfflineRankedService.ClaimRollovers(d);
                Require(d.gold == 30 && d.gems == 110 && r.rank == 9 && r.rankStars == 0 && d.rollovers.Count == 0, "Season rollover claim wrong.");
                OfflinePlayerStateData b = new OfflinePlayerStateData();
                for (int i = 0; i < 30; i++) OfflineRankedService.RecordMatch(b, OfflineRankedService.UnrankedPvP, true);
                int bags = b.rewards.FindAll(delegate(SparkleServices.Rewards.RewardDescription x) { return x.source == SparkleServices.Rewards.RewardSource.Game; }).Count;
                Require(bags >= 4 && bags <= 10 && b.battleBags == bags && (bags == 10 || b.dailyGold >= 180), "Battle Victories bags wrong: " + bags + " bags, " + b.dailyGold + " gold.");
                Require(b.rewards.Exists(delegate(SparkleServices.Rewards.RewardDescription x) { return x.rewardPasses[0].bundleKey == OfflineTitles.TitleBundlePrefix + "title_the_persistent"; }), "The Persistent not granted at the daily limit.");
            });
            test("account-xp-levels-follow-client-formula", delegate {
                Require(OfflineLevelService.XpNeeded(1) == ProfileUtils.GetXpNeededForLevelUp(1) && OfflineLevelService.XpNeeded(1) == 25, "Level 1 XP differs from the client.");
                for (int xp = 0; xp < 20000; xp += 37)
                    Require(OfflineLevelService.Level(xp) == ProfileUtils.CalculateXpData(xp).Lvl, "Level for " + xp + " XP differs from the client.");
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                OfflineLevelService.AwardMatch(d, "practice", true);
                Require(d.rewards.Count == 1 && d.rewards[0].rewardPasses[0].xp == 10 && d.rewards[0].rewardPasses[0].rewardContainerType == SparkleServices.Rewards.RewardContainer.None, "Practice win XP reward wrong.");
                d.rewards.Clear(); d.xp = 20;
                OfflineLevelService.Credit(d, 32);
                Require(d.xp == 52 && d.level == 2 && d.rewards.Count == 1 && d.rewards[0].rewardPasses[0].bundleKey == "level_reward_2" && d.rewards[0].source == SparkleServices.Rewards.RewardSource.LevelUp, "Level-up reward not queued.");
                Require(OfflineLevelService.XpFor("puzzle", false) == 0 && OfflineLevelService.XpFor("ranked", true) == 35 && OfflineLevelService.XpFor("story", false) == 9, "XP table wrong.");
            });
            test("arena-rank-thresholds-and-promotions", delegate {
                int[] wins = { 0, 6, 7, 13, 14, 41, 42, 50, 51, 59, 60, 500 };
                int[] ranks = { 9, 9, 8, 8, 7, 4, 3, 3, 2, 2, 1, 1 };
                for (int i = 0; i < wins.Length; i++)
                    Require(OfflineArenaService.RankForWins(wins[i]) == ranks[i], "Arena rank for " + wins[i] + " wins is " + OfflineArenaService.RankForWins(wins[i]) + ", expected " + ranks[i]);
                OfflineTitles.PublishCards(config);
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                for (int i = 0; i < 7; i++) OfflineArenaService.ArenaWin(d, OfflineArenaService.Solo);
                SparkleServices.Profile.ProfileRating solo = d.ratings.Find(delegate(SparkleServices.Profile.ProfileRating x) { return x.ratingMode == OfflineArenaService.SoloRank; });
                Require(solo != null && solo.rank == 8 && solo.wins == 7 && solo.rankSeen == 9, "Seventh solo win did not promote to Brawler with rank-seen kept for the client's animation.");
                Require(d.rewards.Count == 1 && d.rewards[0].rewardPasses[0].gold == 50, "Promotion gold reward missing.");
                Require(!d.ratings.Exists(delegate(SparkleServices.Profile.ProfileRating x) { return x.ratingMode == OfflineArenaService.VersusRank; }), "Solo wins touched the Versus Arena rank.");
                for (int i = 7; i < 60; i++) OfflineArenaService.ArenaWin(d, OfflineArenaService.Solo);
                Require(solo.rank == 1 && d.rewards.Count == 8 && d.adoringFanGranted, "Grand Champion: rank " + solo.rank + ", rewards " + d.rewards.Count + ", fan " + d.adoringFanGranted);
                int fan = OfflineTitles.CardId("Adoring Fan");
                int premium = 0; for (int i = 0; i < d.cardIds.Count; i++) if (d.cardIds[i] == fan) premium = d.cardPremiumCounts[i];
                Require(fan != 0 && premium == 1, "Premium Adoring Fan not granted once.");
                for (int i = 0; i < 60; i++) OfflineArenaService.ArenaWin(d, OfflineArenaService.Versus);
                premium = 0; for (int i = 0; i < d.cardIds.Count; i++) if (d.cardIds[i] == fan) premium = d.cardPremiumCounts[i];
                Require(premium == 1, "A second Grand Champion granted another Adoring Fan.");
                // Regression (Astra, T4-6): only validated results count toward rank.
                OfflinePlayerStateData r = new OfflinePlayerStateData();
                r.arena = new SparkleServices.Arena.ArenaClientInstance();
                r.arena.arenaType = OfflineArenaService.Solo; r.arena.phase = SparkleServices.Arena.ArenaPhase.MatchSelection;
                r.arena.matches = new List<SparkleServices.Arena.ArenaClientMatch> {
                    new SparkleServices.Arena.ArenaClientMatch { orderIndex = 0, canSelect = true }, new SparkleServices.Arena.ArenaClientMatch { orderIndex = 1 } };
                OfflineArenaService.RecordResult(r, OfflineArenaService.Solo, 0, true);
                OfflineArenaService.RecordResult(r, OfflineArenaService.Solo, 0, true);  // duplicate for a completed match
                OfflineArenaService.RecordResult(r, OfflineArenaService.Solo, 99, true); // unknown match index
                SparkleServices.Profile.ProfileRating counted = r.ratings.Find(delegate(SparkleServices.Profile.ProfileRating x) { return x.ratingMode == OfflineArenaService.SoloRank; });
                Require(counted != null && counted.wins == 1, "Rejected or duplicate arena results changed the rank record (wins " + (counted == null ? 0 : counted.wins) + ").");
            });
            test("title-goals-parse-and-grant", delegate {
                QuestGoal win = OfflineTitles.ParseGoal(OfflineTitles.Normalize("Win 1,000 games."));
                Require(win != null && win.Type == QuestGoalType.WinGame && win.Value == 1000, "Win 1,000 games not parsed.");
                QuestGoal colors = OfflineTitles.ParseGoal(OfflineTitles.Normalize("Win 30 games with an Endurance, Strength, and Willpower deck."));
                Require(colors != null && colors.DeckAttributes.Count == 3, "Three-colour title not parsed.");
                QuestGoal typo = OfflineTitles.ParseGoal(OfflineTitles.Normalize("Win 30 games with a Willpower, Agility, and Endurance dec."));
                Require(typo != null && typo.DeckAttributes.Count == 3, "Installed 'dec' typo not tolerated.");
                QuestGoal mono = OfflineTitles.ParseGoal(OfflineTitles.Normalize("Win 100 games with an Intelligence deck."));
                Require(mono != null && mono.DeckAttributes.Count == 1 && mono.DeckAttributes[0] == CardSubtype.Intelligence, "Mono-colour title not parsed.");
                QuestGoal elves = OfflineTitles.ParseGoal(OfflineTitles.Normalize("Summon 300 Dark Elves."));
                Require(elves != null && elves.Type == QuestGoalType.SummonCount && elves.CardRequirements.Count == 1, "Dark Elves not parsed.");
                QuestGoal orcs = OfflineTitles.ParseGoal(OfflineTitles.Normalize("Summon 300 Orcs."));
                Require(orcs != null && orcs.Type == QuestGoalType.SummonCount && orcs.Value == 300, "Orcs not parsed.");
                string[] counters = { "Pay the Exalt cost on 200 cards.", "Trigger Plot effects on 200 cards.", "Sacrifice 200 creatures with Betray.", "Kill 1,000 creatures.",
                    "Use Lethal creatures to kill 200 creatures.", "Bring 30 creatures back from the dead.", "Play 100 Guards.", "Play 100 Prophecies.", "Deal 10000 damage.",
                    "Summon 200 creatures with 5 power or more.", "Win a game with an all-premium deck." };
                foreach (string text in counters) Require(OfflineTitles.ParseGoal(OfflineTitles.Normalize(text)) != null, "Title not parsed: " + text);
                Require(OfflineTitles.ParseGoal(OfflineTitles.Normalize("Summon 20 Dragons in one game.")) == null, "Per-game title was accepted as a lifetime counter.");
                OfflineTitles.PublishCards(config);
                QuestGoal iron = OfflineTitles.ParseGoal(OfflineTitles.Normalize("Deal 120 damage with Iron Atronach."));
                Require(iron != null && iron.Type == QuestGoalType.DamageValue && iron.CardRequirements.Exists(delegate(ICardRequirement r) { return r is CardRequirements.MatchesCardRequirement; }), "Card-specific title not parsed.");
                Require(OfflineTitles.ParseGoal(OfflineTitles.Normalize("Deal 30 damage with Not A Real Card.")) == null, "Unknown card accepted.");
                Require(OfflineTitles.ParseGoal(OfflineTitles.Normalize("Complete 100 Treasure Hunts.")).Type == QuestGoalType.TreasureHuntCount, "Treasure hunt title not parsed.");
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                OfflineTitles.Grant(d, "title_x");
                OfflineTitles.Grant(d, "title_x");
                Require(d.rewards.Count == 1 && d.rewards[0].rewardPasses[0].bundleKey == OfflineTitles.TitleBundlePrefix + "title_x", "Title grant reward wrong or duplicated.");
            });
            test("title-feats-story-arena-collection", delegate {
                OfflineTitles.Publish(config, null);
                // Kinds follow the installed descriptions.
                string[][] kinds = { new[] { "title_the_defiant", "feat" }, new[] { "title_the_master_of_dragons", "single-game" }, new[] { "title_the_founder", "supporter" },
                    new[] { "title_reborn_keeper", "unsupported" }, new[] { "title_the_master_of_prowess", "arena" }, new[] { "title_nerevar_reborn", "collection" },
                    new[] { "title_the_listener", "story" }, new[] { "title_the_kingslayer", "story" }, new[] { "title_the_kings_protector", "story" }, new[] { "title_the_monster_hunter", "counter" }, new[] { "title_the_blood_magic_lord", "counter" }, new[] { "title_aundae_clan_bloodkin", "counter" }, new[] { "title_the_centurion", "feat" },
                    new[] { "title_the_smuggler", "story" }, new[] { "title_the_savior_of_cyrodiil", "story" }, new[] { "title_2.15_TheFelonius", "story" },
                    new[] { "title_the_deflector", "counter" }, new[] { "title_the_defender", "counter" }, new[] { "title_the_lightfooted", "counter" }, new[] { "title_the_vigorous", "counter" }, new[] { "title_the_walker_of_mundus", "counter" },
                    new[] { "title_friend_to_animals", "story" }, new[] { "title_IOM_the_ghost_whisperer", "story" },
                    new[] { "title_IOM_the_betrayer", "story" }, new[] { "title_IOM_the_prince_of_order", "story" } };
                foreach (string[] k in kinds)
                {
                    // Reborn's plugin adds its own titles to the game's list; without Reborn installed they do not exist
                    if (k[0] == "title_reborn_keeper" && OfflineTitles.Kind(k[0]) == null && !OfflineLaunchMode.RebornInstalled()) continue;
                    Require(OfflineTitles.Kind(k[0]) == k[1], k[0] + " kind is " + OfflineTitles.Kind(k[0]) + ", expected " + k[1]);
                }
                Require(OfflineTitles.IsStarting("title_the_founder") && !OfflineTitles.IsStarting("title_reborn_founder"), "Supporter availability wrong.");
                // Per-game judgement.
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                PlayerState neutral = new PlayerState();
                Require(!OfflineTitleFeats.IsAllNeutralStartingDeck(neutral), "An empty deck counted as all-Neutral.");
                neutral.StartingDeck.Add(620729069);
                Deck neutralDeck = new Deck(); neutralDeck.AddCard(620729069);
                neutral.StartingDeckColors.AddRange(neutralDeck.GetDeckCardColors(false));
                Require(OfflineTitleFeats.IsAllNeutralStartingDeck(neutral), "A nonempty colorless deck was rejected.");
                int strengthCard = FindCreature(config, CardSubtype.Strength); neutral.StartingDeck.Add(strengthCard); neutralDeck.AddCard(strengthCard);
                neutral.StartingDeckColors.Clear(); neutral.StartingDeckColors.AddRange(neutralDeck.GetDeckCardColors(false));
                Require(!OfflineTitleFeats.IsAllNeutralStartingDeck(neutral), "A colored deck counted as all-Neutral.");
                OfflinePlayerStateData neutralWins = new OfflinePlayerStateData();
                OfflineTitles.AddProgress(neutralWins, "title_the_centurion", 99);
                Require(!OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { NeutralStartingDeck = true }, neutralWins).Contains("title_the_centurion")
                    && !OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true }, neutralWins).Contains("title_the_centurion"), "Loss or colored deck counted toward Centurion.");
                Require(OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, NeutralStartingDeck = true }, neutralWins).Contains("title_the_centurion")
                    && neutralWins.pendingTitleRewards.Contains("title_the_centurion") && OfflineTitles.Progress(neutralWins, "title_the_centurion") == 100,
                    "The 100th Neutral win did not grant Centurion.");
                OfflineTitleFeats.Seen win = new OfflineTitleFeats.Seen { StartHealth = 30, EndHealth = 1, MaxHealth = 30, TookDamage = true, Won = true, Turns = 6 };
                List<string> got = OfflineTitleFeats.Judge(win, d);
                Require(got.Contains("title_the_defiant") && got.Contains("title_the_clever") && !got.Contains("title_the_untouchable"), "Win at 1 health: " + string.Join(",", got.ToArray()));
                OfflineTitleFeats.Seen lost = new OfflineTitleFeats.Seen { StartHealth = 30, EndHealth = 1, MaxHealth = 100, Turns = 4, MaxMagicka = 15 };
                got = OfflineTitleFeats.Judge(lost, d);
                Require(!got.Contains("title_the_defiant") && !got.Contains("title_the_clever") && got.Contains("title_the_immortal") && got.Contains("title_the_hist_mage"), "Lost game: " + string.Join(",", got.ToArray()));
                OfflineTitleFeats.Seen clean = new OfflineTitleFeats.Seen { StartHealth = 30, EndHealth = 30, MaxHealth = 41, Won = true, Turns = 7, ChapterEventId = OfflineTitleFeats.TempleOfAkatoshEvent };
                got = OfflineTitleFeats.Judge(clean, d);
                Require(got.Contains("title_the_untouchable") && got.Contains("title_the_temples_chosen") && !got.Contains("title_the_clever"), "Untouchable/temple: " + string.Join(",", got.ToArray()));
                OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { OpponentRunesBroken = 250 }, d);
                Require(!d.pendingTitleRewards.Contains("title_the_runebreaker"), "Runebreaker granted at 250.");
                OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { OpponentRunesBroken = 250 }, d);
                Require(d.pendingTitleRewards.Contains("title_the_runebreaker"), "Runebreaker not granted at 500.");
                // Story wins: Master only where stated; multi-part titles need every part.
                OfflinePlayerStateData st = new OfflinePlayerStateData();
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "DBH_Act3_Ep6_UtherNere" }, st);
                Require(got.Contains("title_the_listener") && !got.Contains("title_the_night_mothers_keeper"), "Uther (normal): " + string.Join(",", got.ToArray()));
                string[][] branchTitles = { new[] { "CWC_Act1_Ep2_FrostTroll_B", "0", "title_friend_to_animals" },
                    new[] { "IOM_Act1_Ep2_FortressPersonalChambers", "591184772", "title_IOM_the_ghost_whisperer" },
                    new[] { "IOM_Act2_Ep4_BthardzamSecretPassage", "-1909586255", "title_IOM_the_betrayer" },
                    new[] { "IOM_Act3_Ep6_ObeliskOfOrder", "-938799815", "title_IOM_the_prince_of_order" } };
                foreach (string[] branch in branchTitles)
                {
                    OfflinePlayerStateData branchState = new OfflinePlayerStateData();
                    OfflineTitleFeats.Seen outcome = new OfflineTitleFeats.Seen { ScenarioName = branch[0], ChapterEventId = int.Parse(branch[1]) };
                    Require(!OfflineTitleFeats.Judge(outcome, branchState).Contains(branch[2]) && !branchState.pendingTitleRewards.Contains(branch[2]), "Story loss granted " + branch[2]);
                    outcome.Won = true;
                    Require(OfflineTitleFeats.Judge(outcome, branchState).Contains(branch[2]) && branchState.pendingTitleRewards.Contains(branch[2]), "Story win failed to grant " + branch[2]);
                }
                Require(!OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "CWC_Act1_Ep2_FrostTroll" }, new OfflinePlayerStateData()).Contains("title_friend_to_animals"),
                    "Friend to Animals granted for the other Frost Troll branch.");
                foreach (string[] branch in new string[][] { new[] { "Mecinar1", "title_the_citys_savior", "title_the_god_of_the_machine" },
                    new[] { "Mecinar2", "title_the_citys_destroyer", "title_the_god_slayer" } })
                {
                    string scenario = "CWC_Act3_Ep6_" + branch[0];
                    Require(config.Scenarios.Exists(delegate(Scenario x) { return x.ResourceId.UniqueName == scenario; }), "Installed Heart scenario missing: " + scenario);
                    OfflinePlayerStateData branchState = new OfflinePlayerStateData();
                    Require(!OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { ScenarioName = scenario }, branchState).Contains(branch[1]), "A lost Heart battle granted its title.");
                    got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = scenario }, branchState);
                    Require(got.Contains(branch[1]) && !got.Contains(branch[2]), "Normal Heart branch granted wrong titles.");
                    got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, Mastery = true, ScenarioName = "CWC_M_Act3_Ep6_" + branch[0] }, branchState);
                    Require(got.Contains(branch[1]) && got.Contains(branch[2]), "Master Heart branch missed its titles.");
                }
                OfflinePlayerStateData miracles = new OfflinePlayerStateData();
                string[] miracleScenarios = { "IOM_Act2_Ep3_KwamaMine", "IOM_Act2_Ep3_MushroomTower", "IOM_Act2_Ep3_MelielsFarm" };
                foreach (string miracle in miracleScenarios)
                    Require(config.Scenarios.Exists(delegate(Scenario x) { return x.ResourceId.UniqueName == miracle; }), "Installed miracle scenario missing: " + miracle);
                for (int i = 0; i < miracleScenarios.Length; i++)
                {
                    got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = miracleScenarios[i] }, miracles);
                    Require(got.Contains("title_IOM_the_purveyor_of_prophecy") == (i == 2), "Miracle title threshold incorrect.");
                    if (i == 0) Require(!OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = miracleScenarios[i] }, miracles).Contains("title_IOM_the_purveyor_of_prophecy"), "Replaying a miracle counted twice.");
                }
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, Mastery = true, ScenarioName = "CWC_M_Act1_Ep2_FrostTroll_B" }, new OfflinePlayerStateData());
                Require(got.Contains("title_friend_to_animals"), "Master wolf branch did not grant Friend to Animals.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, Mastery = true, ScenarioName = "DBH_M_Act3_Ep6_UtherNere" }, new OfflinePlayerStateData());
                Require(got.Contains("title_the_listener") && got.Contains("title_the_night_mothers_keeper"), "Master Uther scenario name did not grant its story titles.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, Mastery = true, ScenarioName = "DBH_M_Act3_Ep6_Alisanne" }, new OfflinePlayerStateData());
                Require(got.Contains("title_the_crypt_raider") && got.Contains("title_the_assassins_bane"), "Master Alisanne scenario name did not grant its story titles.");
                Require(!OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "CWC_M_Act1_Ep2_FrostTroll_B" }, new OfflinePlayerStateData()).Contains("title_friend_to_animals"),
                    "A Master-only scenario name was treated as a normal scenario.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = false, ScenarioName = "DBH_Act3_Ep6_Alisanne", Mastery = true }, st);
                Require(got.Count == 0, "A lost story match granted titles.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "DBH_Act2_Ep3_HilltopGiants", Mastery = true }, st);
                Require(!got.Contains("title_the_giant_slayer"), "Giant Slayer without opponent health 0.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "DBH_Act2_Ep3_HilltopGiants", Mastery = true, OpponentDead = true }, st);
                Require(got.Contains("title_the_giant_slayer"), "Giant Slayer not granted.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "DBH_Act3_Ep5_EclipseSneak" }, st);
                Require(!got.Contains("title_the_master_of_stealth"), "Master of Stealth after one part.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "DBH_Act1_Ep2_CastleDefenders_NoDisguise" }, st);
                Require(got.Contains("title_the_master_of_stealth"), "Master of Stealth after both parts.");
                OfflinePlayerStateData legacy = new OfflinePlayerStateData();
                OfflineTitles.AddProgress(legacy, "story:DBH_M_Act3_Ep5_EclipseSneak", 1);
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ScenarioName = "DBH_Act1_Ep2_CastleDefenders_NoDisguise" }, legacy);
                Require(got.Contains("title_the_master_of_stealth"), "Existing Master-scenario progress was not preserved.");
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ChapterEventId = -527834228, Mastery = true }, st);
                Require(!got.Contains("title_IOM_the_gateslayer"), "Gateslayer before Master Garden of Swords.");
                OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ChapterEventId = 1824998868, Mastery = true }, st);
                got = OfflineTitleFeats.Judge(new OfflineTitleFeats.Seen { Won = true, ChapterEventId = -527834228, Mastery = true }, st);
                Require(got.Contains("title_IOM_the_gateslayer"), "Gateslayer not granted after Garden then Gates.");
                // Board observation on a real catalog.
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                OfflineTitleFeats.Begin(f.State.ActiveQuests, f.Players, f.One);
                int grunt = OfflineTitles.CardId("Imperial Grunt");
                Require(grunt != 0, "Imperial Grunt not installed.");
                for (int i = 0; i < 8; i++) f.CreateId(grunt, GameZoneName.CreatureBoard, i % 2, f.One);
                ServerCard foeGrunt = f.CreateId(grunt, GameZoneName.CreatureBoard, 0, f.Two);
                for (int i = 0; i < 3; i++) f.CreateId(grunt, GameZoneName.Cemetery, 0, f.One);
                OfflineTitleFeats.Observe(f.Catalog, f.Players);
                OfflineTitleFeats.Seen board = OfflineTitleFeats.Current;
                Require(board != null && board.MaxGrunts == 8 && board.MaxDiscardCreatures == 3, "Board observation: grunts " + (board == null ? -1 : board.MaxGrunts) + ", discard " + (board == null ? -1 : board.MaxDiscardCreatures));
                // Untouchable is event-based: native DamageValue on the human's avatar, from either side; damage dealt to the
                // opponent's avatar does not count.
                ServerCard myGrunt = f.CreateId(grunt, GameZoneName.CreatureBoard, 1, f.One);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DamageValue, f.State, myGrunt, f.AvatarTwo, 4);
                OfflineTitleFeats.Observe(f.Catalog, f.Players);
                Require(OfflineTitleFeats.Current.DamageTaken == 0, "Damage to the opponent counted as damage taken.");
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DamageValue, f.State, foeGrunt, f.AvatarOne, 3);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DamageValue, f.State, myGrunt, f.AvatarOne, 1);
                OfflineTitleFeats.Observe(f.Catalog, f.Players);
                Require(OfflineTitleFeats.Current.DamageTaken == 4, "Damage taken (enemy 3 + self 1) counted as " + OfflineTitleFeats.Current.DamageTaken);
                // Master of Flesh is judged at the summon: power below 20 does not count.
                int fleshId = OfflineTitles.CardId("Flesh Atronach");
                Require(fleshId != 0, "Flesh Atronach not installed.");
                ServerCard flesh = f.CreateId(fleshId, GameZoneName.CreatureBoard, 0, f.One);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.SummonCount, f.State, flesh, flesh, 1);
                OfflineTitleFeats.Observe(f.Catalog, f.Players);
                Require(OfflineTitleFeats.Current.FleshSummons == 0, "A Flesh Atronach under 20 power counted.");
                ServerCard big = f.CreateId(fleshId, GameZoneName.CreatureBoard, 1, f.One);
                big.SetAttribute(AttributeType.Attack, 20);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.SummonCount, f.State, big, big, 1);
                OfflineTitleFeats.Observe(f.Catalog, f.Players);
                Require(OfflineTitleFeats.Current.FleshSummons == 1, "A 20-power Flesh Atronach summon was not counted (power " + big.QueryAttribute(AttributeType.Attack).Value + ").");
                // Source-less damage (no acting card, so no DamageValue event) still disqualifies The Untouchable.
                EngineTests.Fixture g = new EngineTests.Fixture(config);
                OfflineTitleFeats.Begin(g.State.ActiveQuests, g.Players, g.One);
                OfflineTitleFeats.Observe(g.Catalog, g.Players);
                int before = g.AvatarOne.CurrentHealth;
                g.AvatarOne.ModifyCurrentHealth(-2, null, g.State);
                OfflineTitleFeats.Observe(g.Catalog, g.Players);
                Require(g.AvatarOne.CurrentHealth < before && OfflineTitleFeats.Current.DamageTaken == 0 && OfflineTitleFeats.Current.HealthDropped,
                    "Source-less damage: health " + before + "->" + g.AvatarOne.CurrentHealth + ", events " + OfflineTitleFeats.Current.DamageTaken + ", dropped " + OfflineTitleFeats.Current.HealthDropped);
                // Arena: only completed undefeated runs count; a 1-0 run retired early never does (Astra/Sol, T-16).
                OfflinePlayerStateData a = new OfflinePlayerStateData();
                OfflineTitles.ArenaFinished(a, true, 6, 1);
                Require(!a.pendingTitleRewards.Contains("title_the_champion_of_the_arena"), "Arena title with a loss.");
                for (int i = 0; i < 5; i++) { OfflineTitles.ArenaFinished(a, true, 1, 0); OfflineTitles.ArenaFinished(a, false, 1, 0); }
                Require(!a.pendingTitleRewards.Contains("title_the_champion_of_the_arena") && !a.pendingTitleRewards.Contains("title_the_master_of_prowess"), "Retired 1-0 runs granted arena titles.");
                OfflineTitles.ArenaFinished(a, true, 7, 0);
                Require(a.pendingTitleRewards.Contains("title_the_champion_of_the_arena") && !a.pendingTitleRewards.Contains("title_the_master_of_the_arena"), "Versus undefeated titles wrong.");
                for (int i = 0; i < 4; i++) OfflineTitles.ArenaFinished(a, false, 9, 0);
                Require(!a.pendingTitleRewards.Contains("title_the_master_of_prowess"), "Prowess at 4 runs.");
                OfflineTitles.ArenaFinished(a, false, 9, 0);
                Require(a.pendingTitleRewards.Contains("title_the_master_of_prowess"), "Prowess not granted at 5 runs.");
            });
            test("title-runebreaker-counts-native-breaks-across-restore", delegate {
                // Real Simulation: 10 damage breaks two enemy runes; a restored rune then breaks again on the next native
                // check. No board observation in between: every break is counted at CheckForRuneBreaks (T-19).
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, 620729069))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    PlayerState foe = f.Sim.Players[enemy];
                    OfflineTitleFeats.Begin(f.Sim.ActiveQuests, f.Sim.Players, f.Sim.Players[me]);
                    int runes = foe.QueryAttribute(AttributeType.NumRunes).Value;
                    f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), null, foe, 10, -1); });
                    f.Until(delegate { return foe.QueryAttribute(AttributeType.NumRunes).Value == runes - 2 && OriginalTargetedSummonTests.Ready(f.Sim); });
                    foe.SetAttribute(AttributeType.NumRunes, runes - 1); // a rune restore (e.g. Morokei) at 20 health
                    f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), null, foe, 1, -1); });
                    f.Until(delegate { return foe.QueryAttribute(AttributeType.NumRunes).Value == runes - 2 && OriginalTargetedSummonTests.Ready(f.Sim); });
                    OfflineTitleFeats.Seen seen = OfflineTitleFeats.Current;
                    Require(seen != null && seen.OpponentRunesBroken == 3, "Break, break, restore, break counted " + (seen == null ? -1 : seen.OpponentRunesBroken)
                        + " (NumRunesLost " + foe.QueryAttribute(AttributeType.NumRunesLost).Value + ")");
                    int mine = f.Sim.Players[me].QueryAttribute(AttributeType.NumRunes).Value;
                    f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), null, f.Sim.Players[me], 6, -1); });
                    f.Until(delegate { return f.Sim.Players[me].QueryAttribute(AttributeType.NumRunes).Value == mine - 1 && OriginalTargetedSummonTests.Ready(f.Sim); });
                    Require(OfflineTitleFeats.Current.OpponentRunesBroken == 3, "The human's own rune break counted for The Runebreaker.");
                }
            });
            test("exalt-quest-counts-only-native-reached-targets", delegate {
                // Real native PayExaltCost.Execute with the installed Harmony hooks (T-4 regression).
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                int grunt = OfflineTitles.CardId("Imperial Grunt");
                ServerCard c1 = f.CreateId(grunt, GameZoneName.CreatureBoard, 0, f.One), c2 = f.CreateId(grunt, GameZoneName.CreatureBoard, 1, f.One);
                Func<bool, int> run = delegate(bool useRing) {
                    ActiveQuest q = new ActiveQuest { Owner = f.One.NetId, Quest = new Quest { Goal = new QuestGoal { Type = QuestGoalType.ExaltCount, Value = 100 } } };
                    f.State.ActiveQuests.Add(q);
                    CardConditionalArguments cards = new CardConditionalArguments(f.Catalog, c1, null, null, null, null, LaneIdentifier.GetInvalid());
                    PlayerConditionalArguments players = new PlayerConditionalArguments(f.Catalog, f.One, null, null, null, f.Two);
                    new CardActions.PayExaltCost { UseRing = useRing }.Execute(new CardActionArguments(f.State, new List<ServerCard> { c1, c2 }, cards, players, new TriggerConditionalArguments(0, AttributeType.None), 0, true));
                    f.State.ActiveQuests.Remove(q);
                    return q.GetValueAddedDuringMatch();
                };
                f.One.SetAttribute(AttributeType.CanUseRing, 0); f.One.SetAttribute(AttributeType.RingUsesLeft, 3);
                int noRing = run(true);
                Require(noRing == 0, "Exalt counted " + noRing + " with the ring unavailable.");
                f.One.SetAttribute(AttributeType.CanUseRing, 1);
                int ring = run(true);
                Require(ring == 1 && f.One.QueryAttribute(AttributeType.CanUseRing).Value == 0 && f.One.QueryAttribute(AttributeType.RingUsesLeft).Value == 2,
                    "Ring exalt: counted " + ring + ", CanUseRing " + f.One.QueryAttribute(AttributeType.CanUseRing).Value + ", uses left " + f.One.QueryAttribute(AttributeType.RingUsesLeft).Value);
                int plain = run(false);
                Require(plain == 2, "Exalt without the ring counted " + plain + " of 2 targets.");
            });
            test("quest-goal-native-counting-respects-requirements", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                Quest quest = new Quest { Goal = OfflineQuestService.Parse("Summon 2 Strength creatures") };
                ActiveQuest active = new ActiveQuest { Owner = f.One.NetId, Quest = quest };
                f.State.ActiveQuests.Add(active);
                int strengthId = FindCreature(config, CardSubtype.Strength);
                ServerCard strength = f.CreateId(strengthId, GameZoneName.CreatureBoard, 0, f.One);
                Require(strength.HasSubtype(CardSubtype.Strength), "Test creature is not Strength.");
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.SummonCount, f.State, strength, strength, 1);
                Require(active.GetValueAddedDuringMatch() == 1, "Matching summon was not counted.");
                ServerCard opponents = f.CreateId(strengthId, GameZoneName.CreatureBoard, 0, f.Two);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.SummonCount, f.State, opponents, opponents, 1);
                Require(active.GetValueAddedDuringMatch() == 1, "Opponent's summon counted for the player's quest.");
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.AttackCount, f.State, strength, f.AvatarTwo, 1);
                Require(active.GetValueAddedDuringMatch() == 1, "Another goal type was counted.");
            });
            test("monster-hunter-native-kill-counter", delegate {
                int tagged = 0;
                foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                {
                    if (c == null || !c.HasSubtype(CardSubtype.Creature)) continue;
                    bool eligible = c.HasSubtype(CardSubtype.Undead) || c.HasSubtype(CardSubtype.Daedra);
                    Require(c.HasSubtype(CardSubtype.MonsterHunterTarget) == eligible, "Monster Hunter tag differs for " + c.Name);
                    if (eligible) tagged++;
                }
                Require(tagged > 0, "No installed Undead or Daedra were found.");
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                QuestGoal goal = OfflineTitles.ParseGoal("kill 150 undead and daedra");
                Require(goal != null && goal.Type == QuestGoalType.DestroyCount && goal.Value == 150, "Monster Hunter goal not parsed.");
                ActiveQuest active = new ActiveQuest { Owner = f.One.NetId, Quest = new Quest { Goal = goal } };
                f.State.ActiveQuests.Add(active);
                ServerCard actor = f.CreateId(FindCreature(config, CardSubtype.Strength), GameZoneName.CreatureBoard, 0, f.One);
                ServerCard undead = f.CreateId(FindCreature(config, CardSubtype.Undead), GameZoneName.CreatureBoard, 0, f.Two);
                ServerCard daedra = f.CreateId(FindCreature(config, CardSubtype.Daedra), GameZoneName.CreatureBoard, 0, f.Two);
                ServerCard friendly = f.CreateId(FindCreature(config, CardSubtype.Undead), GameZoneName.CreatureBoard, 0, f.One);
                ServerCard foe = f.CreateId(FindCreature(config, CardSubtype.Strength), GameZoneName.CreatureBoard, 0, f.Two);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DestroyCount, f.State, actor, undead, 1);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DestroyCount, f.State, actor, daedra, 1);
                Require(active.GetValueAddedDuringMatch() == 2, "Undead and Daedra kills were not counted.");
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DestroyCount, f.State, actor, friendly, 1);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DestroyCount, f.State, actor, foe, 1);
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DestroyCount, f.State, foe, friendly, 1);
                Require(active.GetValueAddedDuringMatch() == 2, "Friendly, ordinary, or enemy kills counted toward Monster Hunter.");
            });
            test("blood-magic-native-pool", delegate {
                List<ICollectionConditionalExpression> selectors = new List<ICollectionConditionalExpression> {
                    new CollectionConditionals.Subtype { Check = CollectionConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Blood_Magic },
                    new CollectionConditionals.Subtype { Check = CollectionConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Spell } };
                List<Card> pool = CollectionConditionals.EvaluateSelectorsOnCollection(selectors, default(CardConditionalArguments),
                    default(PlayerConditionalArguments), default(TriggerConditionalArguments));
                List<string> names = new List<string>(); foreach (Card c in pool) names.Add(c.Name);
                UnityEngine.Debug.Log("OFFLINE BLOOD MAGIC POOL: " + string.Join(",", names.ToArray()));
                Require(names.Count == 3 && names.Contains("Corpse Curse") && names.Contains("Drain Life") && names.Contains("Raise Dead"),
                    "Native Blood Magic pool differs: " + string.Join(",", names.ToArray()));
            });
            test("native-count-of-selector-discards", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard source = f.CreateId(620729069, GameZoneName.CreatureBoard, 0, f.One);
                ServerCard discarded = f.CreateId(620729069, GameZoneName.Cemetery, 0, f.One);
                ActionValueData count = new ActionValueData(ActionValueMethod.CountOfSelector);
                count.Selectors.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn,
                    ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Cemetery } });
                count.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
                CardConditionalArguments cards = new CardConditionalArguments(f.Catalog, source, source, null, null, null, LaneIdentifier.GetInvalid());
                PlayerConditionalArguments players = new PlayerConditionalArguments(f.Catalog, f.One, f.One, null, null, f.Two);
                int actual = ActionValueData.EvaluateValue(count, cards, players, default(TriggerConditionalArguments), f.State.RandomGenerator);
                Require(actual == 1, "Native discard count is " + actual + "; discarded card dynamic=" + discarded.IsInDynamicZone);
            });
        }

        private static int FindCreature(GameConfiguration config, CardSubtype color)
        {
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                    if (card != null && card.Production && card.HasSubtype(CardSubtype.Creature) && card.HasSubtype(color)) return card.ResourceId.Id;
            throw new InvalidOperationException("No installed creature of colour " + color);
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
