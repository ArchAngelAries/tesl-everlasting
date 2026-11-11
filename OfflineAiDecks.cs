using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SPTESL;

namespace OfflineEngine
{
    // AI preset decks (CustomDeck.Decks[].Cards) are NonSerialized: the shipped configuration
    // keeps only each deck's identity, colours and Elo, and the recipes lived on the server.
    // When a scenario needs one, this builds a deterministic stand-in from installed data:
    // the CustomDeck's own colours (plus Neutral), collectible Normal-source cards, preferring
    // cards with restored rules, a fixed mana curve, rarity weighted by Elo/difficulty, and
    // native copy limits. Seeded by deck id and difficulty, so a deck is the same every match.
    // Provenance: reconstruction, NOT the original recipe. Each generated deck is logged to
    // exports/offline-ai-deck-reconstructions.tsv. Replace with captured recipes when available.
    public static class OfflineAiDecks
    {
        private static readonly int[] Curve = { 0, 3, 5, 5, 5, 4, 3, 2, 3 }; // cost 0..7, 8+ (30 cards)
        private static readonly Dictionary<long, List<DeckCardInfo>> cache = new Dictionary<long, List<DeckCardInfo>>();
        public static string ReportPath;

        public static List<DeckCardInfo> Reconstruct(GameConfiguration config, CustomDeck deck, PresetDeck preset)
        {
            long key = ((long)deck.ResourceId.Id << 8) | (long)(int)preset.Difficulty;
            List<DeckCardInfo> cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            List<CardSubtype> colors = new List<CardSubtype>(deck.Colors ?? new List<CardSubtype>());
            if (!colors.Contains(CardSubtype.Neutral)) colors.Add(CardSubtype.Neutral);
            CardSubtype[] all = { CardSubtype.Strength, CardSubtype.Intelligence, CardSubtype.Willpower, CardSubtype.Agility, CardSubtype.Endurance, CardSubtype.Neutral };
            System.Random random = new System.Random((int)(key ^ (key >> 32)));
            // Pools by cost bucket: restored-rule cards first, then the rest.
            List<int>[] preferred = new List<int>[Curve.Length], fallback = new List<int>[Curve.Length];
            for (int i = 0; i < Curve.Length; i++) { preferred[i] = new List<int>(); fallback[i] = new List<int>(); }
            Dictionary<int, int> copies = new Dictionary<int, int>();
            int maxRarity = preset.Difficulty == DeckDifficulty.Normal && deck.DeckElo < 1200 ? 2 : 3;
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    if (card == null || !card.Production || card.Source != CardSource.Normal || string.IsNullOrEmpty(card.ExportCode)) continue;
                    if (card.Definition == "Avatar" || (int)card.Rarity > maxRarity) continue;
                    bool fits = true, any = false;
                    foreach (CardSubtype color in all)
                        if (card.HasSubtype(color)) { any = true; if (!colors.Contains(color)) fits = false; }
                    if (!fits || !any) continue;
                    int cost = Math.Max(0, Math.Min(Curve.Length - 1, card.GetIntegerAttribute(AttributeType.ManaCost)));
                    (RuleImporter.IsSupported(card.ResourceId.Id) ? preferred : fallback)[cost].Add(card.ResourceId.Id);
                    copies[card.ResourceId.Id] = card.Unique ? 1 : Math.Max(1, card.MaxCopies);
                }
            Dictionary<int, int> chosen = new Dictionary<int, int>();
            int total = 0;
            for (int cost = 0; cost < Curve.Length; cost++)
            {
                int need = Curve[cost];
                foreach (List<int> pool in new List<int>[] { preferred[cost], fallback[cost] })
                {
                    List<int> open = new List<int>(pool);
                    while (need > 0 && open.Count > 0)
                    {
                        int id = open[random.Next(open.Count)];
                        int have; chosen.TryGetValue(id, out have);
                        int take = Math.Min(need, Math.Min(copies[id] - have, 1 + random.Next(3)));
                        if (take <= 0) { open.Remove(id); continue; }
                        chosen[id] = have + take; need -= take; total += take;
                        open.Remove(id);
                    }
                }
            }
            // Fill any shortfall (thin colour pools) from every bucket.
            for (int pass = 0; total < 30 && pass < 3; pass++)
                foreach (KeyValuePair<int, int> copy in copies)
                {
                    if (total >= 30) break;
                    int have; chosen.TryGetValue(copy.Key, out have);
                    if (have > 0 && have < copy.Value) { chosen[copy.Key] = have + 1; total++; }
                }
            List<DeckCardInfo> recipe = new List<DeckCardInfo>();
            foreach (KeyValuePair<int, int> entry in chosen) recipe.Add(new DeckCardInfo(entry.Key, entry.Value));
            if (total == 0) throw new InvalidOperationException("No installed cards fit AI deck colours for " + deck.ResourceId.UniqueName);
            cache[key] = recipe;
            if (ReportPath != null)
            {
                StringBuilder row = new StringBuilder();
                if (!File.Exists(ReportPath)) row.Append("deck\tdifficulty\telo\tcolors\tcards\trecipe\n");
                List<string> parts = new List<string>();
                foreach (DeckCardInfo info in recipe) parts.Add(info.Count + "x" + info.Id);
                row.Append(deck.ResourceId.UniqueName).Append('\t').Append(preset.Difficulty).Append('\t').Append(deck.DeckElo).Append('\t')
                    .Append(string.Join(",", colors.ConvertAll(delegate(CardSubtype c) { return c.ToString(); }).ToArray())).Append('\t').Append(total)
                    .Append('\t').Append(string.Join(",", parts.ToArray())).Append('\n');
                try { File.AppendAllText(ReportPath, row.ToString()); } catch (Exception) { }
            }
            return recipe;
        }
    }
}
