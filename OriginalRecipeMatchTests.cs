using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Exact original historical/debug recipe under reconstructed ordinary rules.
    // Exercises native gameplay plus reconstructed visible-state strategy, not
    // the original scenario/AI, transport renderer or physical mouse workflow.
    public static class OriginalRecipeMatchTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test, string directory)
        {
            test("original-resources-default-exact-thirty-card-complete-match", delegate {
                OfflineDeckCatalogTests.AssertRecoveredDefault(config, directory);
                OfflineDeckCatalog.Entry entry = OfflineDeckCatalog.Entries.Find(delegate(OfflineDeckCatalog.Entry e) { return e.Key == "resource/DefaultDeck/2781684"; });
                Deck original = entry.Deck;
                List<int> ids = original.GetIDsAsList();
                Require(ids.Count == 30, "Exact installed original Default recipe must contain thirty cards.");
                Dictionary<int, string> metadata = new Dictionary<int, string>();
                foreach (DeckCardInfo card in original.Cards) metadata.Add(card.Id, OriginalCardRules.Fingerprint(config.FindCardById(card.Id)));
                Simulation sim = new Simulation();
                StringBuilder evidence = new StringBuilder("field\tvalue\nrecipe\tResources/DefaultDeck\nrecipe_size\t30\nrecipe_types\t19\nseed\t314159\nrule_scope\tbounded reconstructed Default/base Avatar/field/shadow/ring; historical recipe preserved\nstrategy\treconstructed visible-state strategy using native commands\n");
                using (IDisposable scope = OrdinaryMatchRules.Prepare(config))
                {
                    try
                    {
                        MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo { ScenarioId = OrdinaryMatchRules.DefaultScenarioId, RandomNumberGenerator = new RandomIntegerGenerator(314159) };
                        for (int slot = 0; slot < 2; slot++)
                        {
                            Deck selected = OfflineDeckCatalog.Clone(original); OfflineDeckCatalog.ValidateForPlay(config, selected);
                            PlayerDescription description = new PlayerDescription { PlayerName = "Original Default test " + slot,
                                AvatarCardId = selected.AvatarCardId, AiOpponentId = UniqueResourceId.GetInvalid().Id };
                            foreach (int id in selected.GetIDsAsList()) { description.Deck.Add(id); description.Premium.Add(false); }
                            Require(description.Deck.Count == 30, "Native original player description was expanded or truncated.");
                            info.Users[slot] = new MatchConfiguration.User { CustomDeck = description, Type = PlayerType.AiOpponent,
                                EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "original-default-test-" + slot,
                                SpectatorKey = "original-default-observer-" + slot };
                        }
                        OrdinaryMatchRules.ValidateStart(info);
                        LifecycleRestoration.Start(sim, null, info);
                        foreach (PlayerState player in sim.Players)
                        {
                            Require(player.StartingDeck.Count == 30 && player.StartingDeckPremiums.Count == 30, "Native registration lost original deck cards.");
                            for (int n = 0; n < ids.Count; n++) Require(player.StartingDeck[n] == ids[n] && !player.StartingDeckPremiums[n], "Native registration changed an exact original recipe entry/order.");
                        }
                        int ticks = 0;
                        while (ticks < 20000 && sim._gameState.GamePhase != GamePhase.Results) { LifecycleRestoration.FixedUpdate(sim); ticks++; }
                        evidence.AppendLine("ticks\t" + ticks).AppendLine("terminal_phase\t" + sim._gameState.GamePhase)
                            .AppendLine("winner\t" + sim.WinnerId.Id).AppendLine("loser\t" + sim.LoserId.Id);
                        int playedJavelins = 0, prophecyJavelins = 0; HashSet<CardInstanceId> seen = new HashSet<CardInstanceId>();
                        foreach (ServerCard card in sim.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.CardId == OriginalTargetSpellRules.PiercingJavelinId; }))
                            if (seen.Add(card.InstanceId)) { playedJavelins += card.QueryGameJournalCount(CardJournal.Played); prophecyJavelins += card.QueryGameJournalCount(CardJournal.PlayedAsProphecy); }
                        evidence.AppendLine("javelin_plays_observed\t" + playedJavelins).AppendLine("javelin_prophecy_plays_observed\t" + prophecyJavelins);
                        for (int i = 0; i < sim.Players.Count; i++) evidence.AppendLine("player_" + i + "_turns\t" + sim.Players[i].QueryAttribute(AttributeType.TurnCount).Value)
                            .AppendLine("player_" + i + "_runes_lost\t" + sim.Players[i].QueryAttribute(AttributeType.NumRunesLost).Value);
                        File.WriteAllText(Path.Combine(directory, "original-default-recipe-match.tsv"), evidence.ToString());
                        Require(sim._gameState.GamePhase == GamePhase.Results, "Exact original Default recipe match did not finish within 20,000 native lifecycle ticks.");
                        Require(sim.WinnerId != sim.LoserId && ((sim.WinnerId == sim.Players[0].NetId && sim.LoserId == sim.Players[1].NetId)
                            || (sim.WinnerId == sim.Players[1].NetId && sim.LoserId == sim.Players[0].NetId)), "Original recipe match produced an invalid native result.");
                        Require(sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value > 1 && sim.Players[1].QueryAttribute(AttributeType.TurnCount).Value > 1,
                            "Original recipe match ended before alternating actual gameplay.");
                        List<int> after = original.GetIDsAsList(); Require(after.Count == ids.Count, "Match altered original selected recipe cardinality.");
                        for (int n = 0; n < ids.Count; n++) Require(after[n] == ids[n], "Match altered original selected recipe IDs/order.");
                        foreach (KeyValuePair<int, string> card in metadata) Require(OriginalCardRules.Fingerprint(config.FindCardById(card.Key)) == card.Value,
                            "Match changed original card statistics/mechanics/art metadata " + card.Key + ".");
                    }
                    catch (Exception error)
                    {
                        evidence.AppendLine("status\tFAIL").AppendLine("terminal_phase\t" + sim._gameState.GamePhase)
                            .AppendLine("error\t" + error.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                        File.WriteAllText(Path.Combine(directory, "original-default-recipe-match.tsv"), evidence.ToString());
                        throw;
                    }
                    finally
                    {
                        RuneProphecyRestoration.Clear(sim);
                        if (sim.PlayerServiceClients != null) foreach (PlayerServiceClient client in sim.PlayerServiceClients) if (client != null) client.Stop();
                    }
                }
            });
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
