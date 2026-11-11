using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Native diagnostic "deckgauntlet:<dir>" (Run-NativeDiagnostic.ps1 -Menu deckgauntlet:<dir>): plays scheduled AI-vs-AI
    // matches between constructed decks, server-side only (no board, no rendering), with ordinary rules and the base avatar,
    // exactly as OriginalRecipeMatchTests plays its full match. Both seats use the offline strategy.
    // <dir>/decks.tsv   : key \t id:count,id:count,...
    // <dir>/matches.tsv : match \t deckA \t deckB \t seed      (deck A takes slot 0)
    // <dir>/results.tsv : appended one row per match (resumable: matches already there are skipped)
    // <dir>/coverage.tsv: per deck, copies whose card rules are restored (RuleImporter) vs metadata-only
    // <dir>/done.txt    : written at the end; then the process exits.
    public static class OfflineDeckGauntlet
    {
        private const int MaxTicks = 60000;
        private const float MaxSeconds = 90f;

        public static IEnumerator Run(string dir)
        {
            yield return null;
            OfflineNativeMatch.PrepareEngine();
            GameConfiguration config = GameManager.Instance.Config;
            Debugging = File.Exists(Path.Combine(dir, "debug.txt"));
            // Opponent data for deck/opponent matching: every original AiOpponent with its inferred race, and every
            // practice AI deck identity with its colours and valid avatars.
            StringBuilder opp = new StringBuilder("unique_name\tid\tdisplay_name\trace\timage\n");
            foreach (EverlastingHost.OpponentInfo o in EverlastingHost.AiOpponents())
                opp.Append(o.UniqueName).Append('\t').Append(o.Id).Append('\t').Append(Clean(o.DisplayName)).Append('\t').Append(o.Race).Append('\t').Append(o.Image).Append('\n');
            File.WriteAllText(Path.Combine(dir, "opponents.tsv"), opp.ToString());
            StringBuilder aiTable = new StringBuilder("ai_deck\tpractice\tproduction\tcolors\topponent_name\tvalid_avatars\n");
            foreach (CustomDeck cd in config.CustomDecks)
            {
                if (cd == null) continue;
                List<string> av = new List<string>();
                foreach (UniqueResourceReference r in cd.ValidAvatars) { AiOpponent a = config.AiOpponents.Find(delegate(AiOpponent x) { return x != null && x.ResourceId.Id == r.resourceId; }); av.Add(a == null ? r.resourceId.ToString() : a.ResourceId.UniqueName); }
                aiTable.Append(cd.ResourceId.UniqueName).Append('\t').Append(cd.AvailableInPractice).Append('\t').Append(cd.Production).Append('\t')
                    .Append(string.Join(",", (cd.Colors ?? new List<CardSubtype>()).ConvertAll(delegate(CardSubtype c) { return c.ToString(); }).ToArray())).Append('\t')
                    .Append(Clean(cd.OpponentName.RawKey)).Append('\t').Append(string.Join(",", av.ToArray())).Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "ai-decks.tsv"), aiTable.ToString());
            Dictionary<string, List<int>> decks = new Dictionary<string, List<int>>();
            StringBuilder coverage = new StringBuilder("deck\tcards\trestored\tmetadata_only\tmissing\tmetadata_only_cards\n");
            foreach (string line in File.ReadAllLines(Path.Combine(dir, "decks.tsv")))
            {
                string[] f = line.Split('\t');
                if (f.Length < 2 || f[0] == "deck") continue;
                List<int> ids = new List<int>();
                int restored = 0, meta = 0, missing = 0; List<string> metaNames = new List<string>();
                foreach (string entry in f[1].Split(','))
                {
                    string[] p = entry.Split(':');
                    int id = int.Parse(p[0]), n = int.Parse(p[1]);
                    Card card = config.FindCardById(id);
                    if (card == null) { missing += n; continue; }
                    if (RuleImporter.IsSupported(id)) restored += n; else { meta += n; metaNames.Add(card.Name.Replace(',', ' ')); }
                    for (int i = 0; i < n; i++) ids.Add(id);
                }
                decks[f[0]] = ids;
                coverage.Append(f[0]).Append('\t').Append(ids.Count).Append('\t').Append(restored).Append('\t').Append(meta).Append('\t').Append(missing)
                    .Append('\t').Append(string.Join("|", metaNames.ToArray())).Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "coverage.tsv"), coverage.ToString());
            string resultsPath = Path.Combine(dir, "results.tsv");
            HashSet<string> done = new HashSet<string>();
            if (File.Exists(resultsPath)) foreach (string line in File.ReadAllLines(resultsPath)) { int tab = line.IndexOf('\t'); if (tab > 0) done.Add(line.Substring(0, tab)); }
            else File.WriteAllText(resultsPath, "match\tdeck_a\tdeck_b\tseed\tresult\tfirst\tturns_a\tturns_b\thealth_a\thealth_b\trunes_lost_a\trunes_lost_b\tticks\tms\terror\n");
            int played = 0, failed = 0;
            foreach (string line in File.ReadAllLines(Path.Combine(dir, "matches.tsv")))
            {
                string[] f = line.Split('\t');
                if (f.Length < 4 || f[0] == "match" || done.Contains(f[0])) continue;
                string row;
                if (!decks.ContainsKey(f[1]) || !decks.ContainsKey(f[2])) row = "error\t\t\t\t\t\t\t\t0\t0\tunknown deck";
                else
                {
                    try { row = Play(config, decks[f[1]], decks[f[2]], int.Parse(f[3])); played++; }
                    catch (Exception e) { row = "error\t\t\t\t\t\t\t\t0\t0\t" + Clean(e.GetType().Name + ": " + e.Message); failed++; }
                }
                File.AppendAllText(resultsPath, f[0] + "\t" + f[1] + "\t" + f[2] + "\t" + f[3] + "\t" + row + "\n");
                yield return null;
            }
            File.WriteAllText(Path.Combine(dir, "done.txt"), "played\t" + played + "\nfailed\t" + failed + "\n");
            UnityEngine.Debug.Log("OFFLINE DECK GAUNTLET: " + played + " played, " + failed + " failed -> " + resultsPath);
            yield return new WaitForSeconds(1f);
            Process.GetCurrentProcess().Kill();
        }

        // Diagnostics (<dir>/debug.txt present): seeded deck vs requested deck, and what each side actually played.
        private static bool Debugging;
        private static int Matching(PlayerState p, List<int> requested)
        {
            Dictionary<int, int> want = new Dictionary<int, int>();
            foreach (int id in requested) { int n; want.TryGetValue(id, out n); want[id] = n + 1; }
            int matching = 0;
            foreach (int id in p.StartingDeck) { int n; if (want.TryGetValue(id, out n) && n > 0) { want[id] = n - 1; matching++; } }
            return matching;
        }

        private static string Played(Simulation sim, PlayerState p, List<int> requested)
        {
            int played = 0, inDeck = 0, inHand = 0, board = 0, cemetery = 0, matching = 0;
            Dictionary<int, int> want = new Dictionary<int, int>();
            foreach (int id in requested) { int n; want.TryGetValue(id, out n); want[id] = n + 1; }
            foreach (int id in p.StartingDeck) { int n; if (want.TryGetValue(id, out n) && n > 0) { want[id] = n - 1; matching++; } }
            foreach (ServerCard c in sim.CardCatalog.GatherCardList(delegate(ServerCard x) { return x.OriginalOwnerNetId == p.NetId && x.StartedInDeck; }))
            {
                played += c.QueryGameJournalCount(CardJournal.Played);
                if (c.CurrentGameZoneName == GameZoneName.Deck) inDeck++;
                else if (c.CurrentGameZoneName == GameZoneName.Hand) inHand++;
                else if (c.CurrentGameZoneName == GameZoneName.CreatureBoard) board++;
                else if (c.CurrentGameZoneName == GameZoneName.Cemetery) cemetery++;
            }
            return "req=" + requested.Count + " seeded=" + p.StartingDeck.Count + " match=" + matching + " played=" + played + " deck=" + inDeck
                + " hand=" + inHand + " board=" + board + " grave=" + cemetery + " mana=" + p.QueryAttribute(AttributeType.MaxMana).Value;
        }

        private static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }

        private static int Health(Simulation sim, PlayerState p)
        {
            ServerCard avatar = sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, p.NetId, GameZoneName.AvatarBoard);
            return avatar == null ? -99 : avatar.CurrentHealth;
        }

        // One full match; returns "result first turns_a turns_b health_a health_b runes_a runes_b ticks ms error".
        private static string Play(GameConfiguration config, List<int> a, List<int> b, int seed)
        {
            Simulation sim = new Simulation();
            Stopwatch clock = Stopwatch.StartNew();
            using (IDisposable scope = OrdinaryMatchRules.Prepare(config))
            {
                try
                {
                    MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo { ScenarioId = OrdinaryMatchRules.DefaultScenarioId, RandomNumberGenerator = new RandomIntegerGenerator(seed) };
                    List<int>[] lists = { a, b };
                    for (int slot = 0; slot < 2; slot++)
                    {
                        PlayerDescription d = new PlayerDescription { PlayerName = "Gauntlet " + slot, AvatarCardId = OrdinaryMatchRules.BaseAvatarId, AiOpponentId = UniqueResourceId.GetInvalid().Id };
                        foreach (int id in lists[slot]) { d.Deck.Add(id); d.Premium.Add(false); }
                        info.Users[slot] = new MatchConfiguration.User { CustomDeck = d, Type = PlayerType.AiOpponent, EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32],
                            SecretKey = "deck-gauntlet-" + slot, SpectatorKey = "deck-gauntlet-observer-" + slot };
                    }
                    OrdinaryMatchRules.ValidateStart(info);
                    LifecycleRestoration.Start(sim, null, info);
                    int ticks = 0, first = -1;
                    string error = "";
                    while (sim._gameState.GamePhase != GamePhase.Results)
                    {
                        if (ticks >= MaxTicks) { error = "tick limit"; break; }
                        if ((ticks & 255) == 0 && clock.ElapsedMilliseconds > MaxSeconds * 1000) { error = "time limit"; break; }
                        LifecycleRestoration.FixedUpdate(sim); ticks++;
                        if (first < 0 && sim._gameState.GamePhase == GamePhase.MainLoop && sim._turnStateStack.Count > 0) first = sim._turnStateStack.Peek().TurnPlayerIndex;
                    }
                    // sim.Players is not in StartInfo slot order: find each deck's player by its seeded starting deck.
                    PlayerState p0 = sim.Players[0], p1 = sim.Players[1];
                    if (Matching(p0, b) + Matching(p1, a) > Matching(p0, a) + Matching(p1, b)) { p0 = sim.Players[1]; p1 = sim.Players[0]; }
                    string result;
                    if (sim._gameState.GamePhase != GamePhase.Results) result = "draw";
                    else if (sim.WinnerId == p0.NetId && sim.LoserId == p1.NetId) result = "a";
                    else if (sim.WinnerId == p1.NetId && sim.LoserId == p0.NetId) result = "b";
                    else result = "draw";
                    if (Debugging) error += "|" + Played(sim, p0, a) + "|" + Played(sim, p1, b);
                    PlayerState starter = first >= 0 && first < 2 ? sim.Players[first] : null;
                    return result + "\t" + (starter == null ? "" : starter == p0 ? "a" : "b") + "\t" + p0.QueryAttribute(AttributeType.TurnCount).Value + "\t" + p1.QueryAttribute(AttributeType.TurnCount).Value
                        + "\t" + Health(sim, p0) + "\t" + Health(sim, p1) + "\t" + p0.QueryAttribute(AttributeType.NumRunesLost).Value + "\t" + p1.QueryAttribute(AttributeType.NumRunesLost).Value
                        + "\t" + ticks + "\t" + clock.ElapsedMilliseconds + "\t" + error;
                }
                finally
                {
                    RuneProphecyRestoration.Clear(sim);
                    if (sim.PlayerServiceClients != null) foreach (PlayerServiceClient client in sim.PlayerServiceClients) if (client != null) client.Stop();
                }
            }
        }
    }
}
