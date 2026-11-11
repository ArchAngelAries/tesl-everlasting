using System;
using System.Collections.Generic;
using System.IO;
using FullSerializer;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mod card packs (<mod>/packs/packs.json, art beside it), sold for gold in the offline store and opened with the
    // game's own pack screens (bundle "cardpack_mod_<mod>_<slug>"; the client shows unknown bundles with the Core pack model).
    // Each pack: name, description, price (gold), art, cards (cards per pack, 1-12), pool and drop table:
    //   "pool": "mod" (this mod's cards), a list of set names ("Core Set", "Heroes of Skyrim", ...), or "all";
    //   "cards": [{"card": "Name", "weight": 3}] - a hand-picked weighted list (used instead of rarity slots when given);
    //   "odds": {"common": 79, "rare": 15, "epic": 4.5, "legendary": 1.5} for every slot but the last,
    //   "last_slot": {...} the guaranteed slot (default rare 80 / epic 15 / legendary 5), "premium": 5 (percent).
    // Duplicate protection like the game's packs: cards below their max copies are preferred.
    public static class OfflineModPacks
    {
        public sealed class Pack
        {
            public string Key, Mod, Slug, Name, Description, Image, ItemKey, Bundle;
            public int Gold = 100, Cards = 6;
            public double[] Odds = { 0.79, 0.15, 0.045, 0.015 }, LastOdds = { 0, 0.80, 0.15, 0.05 };
            public double Premium = 0.05;
            public bool Rewards;   // "rewards": true - also handed out where the game rewards card packs (OfflinePackService.RewardBundle)
            public readonly List<int>[] Pool = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            public readonly List<KeyValuePair<int, double>> Weighted = new List<KeyValuePair<int, double>>();
        }
        public static readonly List<Pack> All = new List<Pack>();
        public static bool ListFirst;   // diagnostics: mod packs before the game's packs in the store
        private static readonly System.Random random = new System.Random();
        private static readonly string[] Rarities = { "common", "rare", "epic", "legendary" };

        private static string S(Dictionary<string, fsData> d, string k, string def) { return d != null && d.ContainsKey(k) && d[k].IsString ? d[k].AsString : def; }
        private static double N(Dictionary<string, fsData> d, string k, double def)
        {
            if (d == null || !d.ContainsKey(k)) return def;
            fsData v = d[k];
            return v.IsDouble ? v.AsDouble : v.IsInt64 ? v.AsInt64 : def;
        }
        private static double[] Odds(Dictionary<string, fsData> d, string k, double[] def)
        {
            if (d == null || !d.ContainsKey(k) || !d[k].IsDictionary) return def;
            Dictionary<string, fsData> o = d[k].AsDictionary;
            double[] r = new double[4]; double sum = 0;
            for (int i = 0; i < 4; i++) { r[i] = Math.Max(0, N(o, Rarities[i], 0)); sum += r[i]; }
            if (sum <= 0) return def;
            for (int i = 0; i < 4; i++) r[i] /= sum;
            return r;
        }

        public static void Load(GameConfiguration config, string modId, string dir, List<string> problems)
        {
            string folder = Path.Combine(dir, "packs"), file = Path.Combine(folder, "packs.json");
            if (!File.Exists(file)) return;
            foreach (fsData e in fsJsonParser.Parse(File.ReadAllText(file)).AsDictionary["packs"].AsList)
            {
                Dictionary<string, fsData> d = e.AsDictionary;
                Pack p = new Pack { Mod = modId, Slug = S(d, "slug", "pack"), Name = S(d, "name", "Pack"), Description = S(d, "description", "") };
                p.Key = modId + "/" + p.Slug;
                p.ItemKey = "modpack:" + p.Key;
                p.Bundle = "cardpack_mod_" + modId + "_" + p.Slug;
                p.Gold = Math.Max(0, (int)N(d, "price", 100));
                p.Cards = Math.Max(1, Math.Min(12, (int)N(d, "cards", 6)));
                p.Odds = Odds(d, "odds", p.Odds); p.LastOdds = Odds(d, "last_slot", p.LastOdds);
                p.Premium = Math.Max(0, Math.Min(100, N(d, "premium", 5))) / 100.0;
                p.Rewards = d.ContainsKey("rewards") && ((d["rewards"].IsBool && d["rewards"].AsBool) || N(d, "rewards", 0) > 0);
                string art = S(d, "art", null);
                if (!string.IsNullOrEmpty(art))
                {
                    string png = Path.Combine(folder, art);
                    if (File.Exists(png)) { p.Image = OfflineMods.ImagePrefix + modId + "/packs/" + p.Slug; OfflineMods.AddArt(p.Image, png); }
                    else problems.Add(p.Key + ": pack art missing: " + art);
                }
                // Hand-picked cards with weights.
                if (d.ContainsKey("cards_list") && d["cards_list"].IsList)
                    foreach (fsData c in d["cards_list"].AsList)
                    {
                        Dictionary<string, fsData> cd = c.AsDictionary;
                        Card card = OfflineModes.FindCard(config, S(cd, "card", ""));
                        if (card == null) { problems.Add(p.Key + ": no card named " + S(cd, "card", "")); continue; }
                        p.Weighted.Add(new KeyValuePair<int, double>(card.ResourceId.Id, Math.Max(0.0001, N(cd, "weight", 1))));
                    }
                // Rarity pools.
                List<string> sets = new List<string>();
                string poolKind = "mod";
                if (d.ContainsKey("pool") && d["pool"].IsString) poolKind = d["pool"].AsString;
                else if (d.ContainsKey("pool") && d["pool"].IsList) { poolKind = "sets"; foreach (fsData s in d["pool"].AsList) if (s.IsString) sets.Add(s.AsString); }
                foreach (CardSet set in config.CardCollection)
                {
                    bool modSet = set.Name != null && set.Name.StartsWith("Mod_", StringComparison.Ordinal), mine = set.Name == "Mod_" + modId;
                    bool take = poolKind == "all" || (poolKind == "mod" && mine)
                        || (poolKind == "sets" && sets.Exists(delegate(string n) { return string.Equals(n, set.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(n, LocalizedSetName(set), StringComparison.OrdinalIgnoreCase); }));
                    if (!take) continue;
                    foreach (Card card in set.Cards)
                    {
                        if (card == null) continue;
                        if (!mine && (!card.Production || string.IsNullOrEmpty(card.ExportCode) || card.Source != CardSource.Normal)) continue;
                        if (!mine && modSet) continue;
                        if (p.Pool[Mathf.Clamp((int)card.Rarity, 0, 3)].Contains(card.ResourceId.Id)) continue;
                        p.Pool[Mathf.Clamp((int)card.Rarity, 0, 3)].Add(card.ResourceId.Id);
                    }
                }
                if (p.Weighted.Count == 0 && p.Pool[0].Count + p.Pool[1].Count + p.Pool[2].Count + p.Pool[3].Count == 0)
                { problems.Add(p.Key + ": the pack has no cards to give (check pool / cards_list)"); continue; }
                All.RemoveAll(delegate(Pack x) { return x.Key == p.Key; });
                All.Add(p);
            }
        }

        private static string LocalizedSetName(CardSet set)
        {
            if (string.IsNullOrEmpty(set.DisplayName.RawKey)) return null;
            string mod = OfflineMods.Text(set.DisplayName.RawKey);
            if (mod != null) return mod;
            try { return LocalizationManager.Instance == null ? null : LocalizationManager.Instance.GetText(set.DisplayName.RawKey); } catch (Exception) { return null; }
        }

        public static Pack ForItem(string itemKey) { return All.Find(delegate(Pack p) { return p.ItemKey == itemKey; }); }
        public static Pack ForBundle(string bundle) { return All.Find(delegate(Pack p) { return string.Equals(p.Bundle, bundle, StringComparison.OrdinalIgnoreCase); }); }

        public static List<KeyValuePair<int, bool>> Open(OfflinePlayerStateData d, Pack p)
        {
            List<KeyValuePair<int, bool>> result = new List<KeyValuePair<int, bool>>();
            lock (random)
                for (int slot = 0; slot < p.Cards; slot++)
                {
                    int card;
                    if (p.Weighted.Count > 0) card = PickWeighted(d, p.Weighted);
                    else
                    {
                        int rarity = Roll(slot == p.Cards - 1 && p.Cards > 1 ? p.LastOdds : p.Odds);
                        int r = rarity;
                        while (r > 0 && p.Pool[r].Count == 0) r--;
                        if (p.Pool[r].Count == 0) { r = rarity; while (r < 3 && p.Pool[r].Count == 0) r++; }
                        card = Pick(d, p.Pool[r]);
                    }
                    if (card == 0) continue;
                    bool premium = random.NextDouble() < p.Premium;
                    d.AddCards(card, premium ? 0 : 1, premium ? 1 : 0);
                    result.Add(new KeyValuePair<int, bool>(card, premium));
                }
            return result;
        }

        private static int Roll(double[] odds)
        {
            double roll = random.NextDouble(), sum = 0;
            for (int r = 0; r < odds.Length; r++) { sum += odds[r]; if (roll < sum) return r; }
            return 0;
        }

        private static bool BelowMax(OfflinePlayerStateData d, int id)
        {
            Card c = GameManager.Instance == null ? null : GameManager.Instance.GetCard(id);
            int max = c == null ? 3 : c.Unique ? 1 : c.MaxCopies;
            return d.TotalCopies(id) < max;
        }

        private static int Pick(OfflinePlayerStateData d, List<int> cards)
        {
            if (cards.Count == 0) return 0;
            List<int> open = cards.FindAll(delegate(int id) { return BelowMax(d, id); });
            List<int> from = open.Count > 0 ? open : cards;
            return from[random.Next(from.Count)];
        }

        private static int PickWeighted(OfflinePlayerStateData d, List<KeyValuePair<int, double>> cards)
        {
            List<KeyValuePair<int, double>> open = cards.FindAll(delegate(KeyValuePair<int, double> c) { return BelowMax(d, c.Key); });
            List<KeyValuePair<int, double>> from = open.Count > 0 ? open : cards;
            double total = 0;
            foreach (KeyValuePair<int, double> c in from) total += c.Value;
            double roll = random.NextDouble() * total;
            foreach (KeyValuePair<int, double> c in from) { roll -= c.Value; if (roll <= 0) return c.Key; }
            return from[from.Count - 1].Key;
        }
    }
}
