using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Local shop gold purchases and card packages (Packages service): purchaseWithGold adds
    // a package; packages/list, packages/deliver (single pack opening) and
    // packages/massPackOpening open them through the client's own pack-opening screens.
    // Pools come from the installed card sets (source Normal, production, with an export
    // code). Pack rules are a reconstruction, not server data: 6 cards, slots 1-5 Common
    // 79% / Rare 15% / Epic 4.5% / Legendary 1.5%, slot 6 Rare 80% / Epic 15% / Legendary
    // 5%, premium 5% per card, and no card past its max copies while another exists.
    // Legendary packs ("cardpack_legendary_<set>") hold one legendary.
    public static class OfflinePackService
    {
        public const string CoreBundle = "cardpack_core";
        // Store: one 100-gold pack per installed set with a card pool (pool key from native-pack-pools.tsv).
        // Art: official pack renders from UESP (data/store-art/pack_<pool>.png, LG-pack-*.png), served as file URLs.
        public sealed class StoreOffer { public string ItemKey, Bundle, Title, Description, Pool; public int Gold; }
        public static readonly StoreOffer[] StoreOffers = {
            new StoreOffer { ItemKey = "packs_core_1", Bundle = CoreBundle, Pool = "core", Gold = 100, Title = "Core Set Pack", Description = "Six cards from the Core Set, with at least one Rare or better." },
            new StoreOffer { ItemKey = "packs_skyrim_1", Bundle = "cardpack_Skyrim", Pool = "skyrim", Gold = 100, Title = "Heroes of Skyrim Pack", Description = "Six cards from Heroes of Skyrim, with at least one Rare or better." },
            new StoreOffer { ItemKey = "packs_morrowind_1", Bundle = "cardpack_morrowind", Pool = "morrowind", Gold = 100, Title = "Houses of Morrowind Pack", Description = "Six cards from Houses of Morrowind, with at least one Rare or better." },
            new StoreOffer { ItemKey = "packs_alliancewar_1", Bundle = "cardpack_alliancewar", Pool = "alliancewar", Gold = 100, Title = "Alliance War Pack", Description = "Six cards from Alliance War, with at least one Rare or better." },
            new StoreOffer { ItemKey = "packs_sage_1", Bundle = "cardpack_elsweyr", Pool = "sage", Gold = 100, Title = "Moons of Elsweyr Pack", Description = "Six cards from Moons of Elsweyr, with at least one Rare or better." },
            new StoreOffer { ItemKey = "packs_crisis_1", Bundle = "cardpack_oblivion", Pool = "crisis", Gold = 100, Title = "Jaws of Oblivion Pack", Description = "Six cards from Jaws of Oblivion, with at least one Rare or better." },
        };
        private static readonly Dictionary<string, KeyValuePair<string, int>> Offers = BuildOffers();
        private static Dictionary<string, KeyValuePair<string, int>> BuildOffers()
        {
            Dictionary<string, KeyValuePair<string, int>> offers = new Dictionary<string, KeyValuePair<string, int>>();
            foreach (StoreOffer o in StoreOffers) offers[o.ItemKey] = new KeyValuePair<string, int>(o.Bundle, o.Gold);
            return offers;
        }
        public static string StoreArtDirectory;
        private static readonly double[] SlotOdds = { 0.79, 0.15, 0.045, 0.015 }, LastSlotOdds = { 0, 0.80, 0.15, 0.05 };
        private const double PremiumChance = 0.05;
        private static readonly System.Random random = new System.Random();
        // set key (letters only, lower case) -> rarity -> card ids, plus max copies per id.
        private static volatile Dictionary<string, List<int>[]> pools = new Dictionary<string, List<int>[]>();
        private static volatile Dictionary<int, int> maxCopies = new Dictionary<int, int>();

        public static void PublishPools(SPTESL.GameConfiguration config, string reportPath)
        {
            Dictionary<string, List<int>[]> sets = new Dictionary<string, List<int>[]>();
            Dictionary<int, int> copies = new Dictionary<int, int>();
            StringBuilder report = new StringBuilder("set\tkey\tcommon\trare\tepic\tlegendary\n");
            foreach (SPTESL.CardSet set in config.CardCollection)
            {
                List<int>[] byRarity = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                foreach (SPTESL.Card card in set.Cards)
                {
                    if (card == null || !card.Production || string.IsNullOrEmpty(card.ExportCode) || card.Source != SPTESL.CardSource.Normal) continue;
                    if (card.AlternateOfCard != null && card.AlternateOfCard.resourceId != SPTESL.UniqueResourceId.GetInvalid().Id && card.AlternateOfCard.resourceId != 0) continue;
                    byRarity[Mathf.Clamp((int)card.Rarity, 0, 3)].Add(card.ResourceId.Id);
                    copies[card.ResourceId.Id] = card.Unique ? 1 : card.MaxCopies;
                }
                string key = Key(set.Name);
                report.Append(set.Name).Append('\t').Append(key).Append('\t').Append(byRarity[0].Count).Append('\t').Append(byRarity[1].Count)
                    .Append('\t').Append(byRarity[2].Count).Append('\t').Append(byRarity[3].Count).Append('\n');
                if (byRarity[0].Count + byRarity[1].Count + byRarity[2].Count + byRarity[3].Count == 0) continue;
                List<int>[] existing;
                if (sets.TryGetValue(key, out existing)) for (int r = 0; r < 4; r++) existing[r].AddRange(byRarity[r]);
                else sets[key] = byRarity;
            }
            pools = sets; maxCopies = copies;
            File.WriteAllText(reportPath, report.ToString());
        }

        private static string Key(string name)
        {
            StringBuilder key = new StringBuilder();
            foreach (char c in name ?? "") if (char.IsLetter(c)) key.Append(char.ToLowerInvariant(c));
            return key.ToString();
        }

        // "cardpack_core" / "cardpack_legendary_skyrim" -> the set pool whose key contains the suffix.
        private static List<int>[] PoolFor(string bundle, out bool legendaryOnly)
        {
            string suffix = (bundle ?? "").ToLowerInvariant();
            if (suffix.StartsWith("cardpack_")) suffix = suffix.Substring(9);
            legendaryOnly = suffix.StartsWith("legendary_");
            if (legendaryOnly) suffix = suffix.Substring(10);
            suffix = Key(suffix);
            // Installed set codenames: Moons of Elsweyr = "Sage", Jaws of Oblivion = "Crisis".
            if (suffix == "elsweyr") suffix = "sage";
            if (suffix == "oblivion") suffix = "crisis";
            List<int>[] exact;
            if (pools.TryGetValue(suffix, out exact)) return exact;
            foreach (KeyValuePair<string, List<int>[]> pool in pools) if (pool.Key.Contains(suffix)) return pool.Value;
            return null;
        }

        public static void Register(OfflineLocalServices services, OfflinePlayerState state)
        {
            services.Register("shop/purchaseWithGold", delegate(string m, string p, string body) {
                Shop.PurchaseWithGoldRequest request = JsonUtility.FromJson<Shop.PurchaseWithGoldRequest>(body);
                KeyValuePair<string, int> offer;
                bool ok = false;
                if (request != null && request.count > 0 && request.count <= 100 && Offers.TryGetValue(request.offerId ?? "", out offer))
                    state.Mutate(delegate(OfflinePlayerStateData d) {
                        int cost = offer.Value * request.count;
                        if (d.gold < cost) return;
                        d.gold -= cost; AddPackages(d, offer.Key, request.count); ok = true; });
                else if (request != null && request.count > 0 && request.count <= 100 && OfflineModPacks.ForItem(request.offerId) != null)
                {
                    OfflineModPacks.Pack mp = OfflineModPacks.ForItem(request.offerId);
                    state.Mutate(delegate(OfflinePlayerStateData d) {
                        int cost = mp.Gold * request.count;
                        if (d.gold < cost) return;
                        d.gold -= cost; AddPackages(d, mp.Bundle, request.count); ok = true; });
                }
                else if (request != null && request.count == 1 && OfflineGoldStore.IsGoldStoreKey(request.offerId))
                    state.Mutate(delegate(OfflinePlayerStateData d) { ok = OfflineGoldStore.Buy(d, request.offerId); });
                Shop.BeginSteamTransactionResponse response = new Shop.BeginSteamTransactionResponse();
                response.resultCode = ok ? ResultCode.Ok : ResultCode.ClientHttpErrorBadRequest;
                response.transactionId = ok ? Guid.NewGuid().ToString("N") : "";
                return JsonUtility.ToJson(response); });
            services.Register("packages/list", delegate {
                state.Mutate(MigratePackageKeys);
                Packages.ListPackagesResponse response = new Packages.ListPackagesResponse();
                response.resultCode = ResultCode.Ok; response.massOpenMinCount = 10;
                response.packageInfos = state.Snapshot().packages;
                return JsonUtility.ToJson(response); });
            services.Register("packages/deliver", delegate(string m, string p, string body) {
                Packages.DeliverRequest request = JsonUtility.FromJson<Packages.DeliverRequest>(body);
                Packages.DeliverResponse response = new Packages.DeliverResponse();
                response.resultCode = ResultCode.ClientHttpErrorBadRequest;
                if (request != null)
                    state.Mutate(delegate(OfflinePlayerStateData d) {
                        Packages.PackageDescription package = d.packages.Find(delegate(Packages.PackageDescription x) { return x.packageId == request.packageId && x.numRemaining > 0; });
                        if (package == null) return;
                        List<KeyValuePair<int, bool>> cards = Open(d, package.bundleKey);
                        if (cards == null) return;
                        package.numRemaining--;
                        if (package.numRemaining == 0) d.packages.Remove(package);
                        response.delivered = new Packages.DeliverResponse.Delivered();
                        response.delivered.items = new List<Packages.DeliverResponse.Delivered.DeliveredItem>();
                        foreach (KeyValuePair<int, bool> card in cards)
                            response.delivered.items.Add(new Packages.DeliverResponse.Delivered.DeliveredItem { type = "card", typeHash = card.Key, isPremium = card.Value });
                        response.resultCode = ResultCode.Ok; });
                return JsonUtility.ToJson(response); });
            services.Register("packages/massPackOpening", delegate(string m, string p, string body) {
                Packages.MassPackOpeningRequest request = JsonUtility.FromJson<Packages.MassPackOpeningRequest>(body);
                Packages.MassPackOpeningResponse response = new Packages.MassPackOpeningResponse();
                response.resultCode = ResultCode.Ok;
                response.delivered = new List<Packages.MassPackOpeningResponse.ItemDesc>();
                if (request != null)
                    state.Mutate(delegate(OfflinePlayerStateData d) {
                        Dictionary<int, Packages.MassPackOpeningResponse.ItemDesc> totals = new Dictionary<int, Packages.MassPackOpeningResponse.ItemDesc>();
                        foreach (Packages.PackageDescription package in d.packages.FindAll(delegate(Packages.PackageDescription x) { return x.bundleKey == request.bundleKey; }))
                            while (package.numRemaining > 0)
                            {
                                List<KeyValuePair<int, bool>> cards = Open(d, package.bundleKey);
                                if (cards == null) break;
                                package.numRemaining--;
                                foreach (KeyValuePair<int, bool> card in cards)
                                {
                                    Packages.MassPackOpeningResponse.ItemDesc item;
                                    if (!totals.TryGetValue(card.Key, out item)) totals[card.Key] = item = new Packages.MassPackOpeningResponse.ItemDesc { typeHash = card.Key };
                                    if (card.Value) item.numPremium++; else item.numRegular++;
                                }
                            }
                        d.packages.RemoveAll(delegate(Packages.PackageDescription x) { return x.numRemaining <= 0; });
                        response.delivered.AddRange(totals.Values);
                        response.remaining = 0; });
                return JsonUtility.ToJson(response); });
        }

        // The client's own pack keys (PackOpenState._validKeyList): an unknown key is grouped as Core and opens the
        // Core model. Installed set codenames map to them: Sage = Elsweyr, Crisis = Oblivion; Skyrim is capitalised.
        public static string CanonicalBundle(string bundle)
        {
            if (string.IsNullOrEmpty(bundle) || !bundle.StartsWith("cardpack_", StringComparison.OrdinalIgnoreCase)) return bundle;
            if (bundle.StartsWith("cardpack_mod_", StringComparison.OrdinalIgnoreCase)) return bundle;   // mod packs keep their key
            string rest = bundle.Substring(9).ToLowerInvariant();
            string prefix = "cardpack_";
            if (rest.StartsWith("premium_legendary_")) { prefix += "premium_legendary_"; rest = rest.Substring(18); }
            else if (rest.StartsWith("legendary_")) { prefix += "legendary_"; rest = rest.Substring(10); }
            string set = rest.Replace(" ", "");
            if (set == "sage" || set == "moonsofelsweyr") set = "elsweyr";
            else if (set == "crisis" || set == "jawsofoblivion") set = "oblivion";
            else if (set == "heroesofskyrim") set = "skyrim";
            else if (set == "housesofmorrowind") set = "morrowind";
            if (set == "skyrim" && prefix == "cardpack_") set = "Skyrim";
            return prefix + set;
        }

        public static void AddPackages(OfflinePlayerStateData d, string bundle, int count)
        {
            bundle = CanonicalBundle(bundle);
            Packages.PackageDescription package = d.packages.Find(delegate(Packages.PackageDescription x) { return x.bundleKey == bundle && x.deliveryMode == "manual"; });
            if (package == null)
            {
                package = new Packages.PackageDescription { packageId = ++d.nextPackageId, bundleKey = bundle, numRemaining = 0, deliveryMode = "manual" };
                d.packages.Add(package);
            }
            package.numRemaining += count;
        }

        // Packages saved before CanonicalBundle: rename (merging into an existing canonical package).
        private static void MigratePackageKeys(OfflinePlayerStateData d)
        {
            foreach (Packages.PackageDescription package in d.packages.ToArray())
            {
                string canonical = CanonicalBundle(package.bundleKey);
                if (canonical == package.bundleKey) continue;
                Packages.PackageDescription target = d.packages.Find(delegate(Packages.PackageDescription x) { return x != package && x.bundleKey == canonical && x.deliveryMode == package.deliveryMode; });
                if (target != null) { target.numRemaining += package.numRemaining; d.packages.Remove(package); }
                else package.bundleKey = canonical;
            }
        }

        // Opens one pack into the collection; null when the bundle has no pool.
        private static List<KeyValuePair<int, bool>> Open(OfflinePlayerStateData d, string bundle)
        {
            OfflineModPacks.Pack modPack = OfflineModPacks.ForBundle(bundle);
            if (modPack != null) return OfflineModPacks.Open(d, modPack);
            bool legendaryOnly;
            List<int>[] pool = PoolFor(bundle, out legendaryOnly);
            if (pool == null) { Debug.LogWarning("OFFLINE PACKS: no card pool for bundle " + bundle); return null; }
            List<KeyValuePair<int, bool>> result = new List<KeyValuePair<int, bool>>();
            int slots = legendaryOnly ? 1 : 6;
            for (int slot = 0; slot < slots; slot++)
            {
                int rarity = legendaryOnly ? 3 : Roll(slot == slots - 1 ? LastSlotOdds : SlotOdds);
                while (rarity > 0 && pool[rarity].Count == 0) rarity--;
                int card = Pick(d, pool[rarity]);
                if (card == 0) continue;
                bool premium = random.NextDouble() < PremiumChance;
                d.AddCards(card, premium ? 0 : 1, premium ? 1 : 0);
                result.Add(new KeyValuePair<int, bool>(card, premium));
            }
            return result;
        }

        // A Core Set card of the given rarity (0 Common .. 3 Legendary), of the given race when one exists (level rewards).
        public static int CoreCard(OfflinePlayerStateData d, int rarity, SPTESL.CardSubtype race)
        {
            List<int>[] core;
            if (!pools.TryGetValue("core", out core)) return 0;
            List<int> cards = core[Math.Max(0, Math.Min(3, rarity))];
            if (race != SPTESL.CardSubtype.None)
            {
                List<int> raced = cards.FindAll(delegate(int id) { SPTESL.Card c = SPTESL.GameManager.Instance.GetCard(id); return c != null && c.HasSubtype(race); });
                if (raced.Count > 0) cards = raced;
            }
            return Pick(d, cards);
        }

        // A rewarded pack: the original bundle, or (with the share of one set among the six store sets each) a mod pack that
        // opted into rewards ("rewards": true in packs.json). Used by arena, daily quest and level-up pack rewards.
        public static string RewardBundle(string original)
        {
            List<OfflineModPacks.Pack> mod = OfflineModPacks.All.FindAll(delegate(OfflineModPacks.Pack p) { return p.Rewards; });
            if (mod.Count == 0) return original;
            int roll;
            lock (random) roll = random.Next(StoreOffers.Length + mod.Count);
            return roll < StoreOffers.Length ? original : mod[roll - StoreOffers.Length].Bundle;
        }

        // One random card from all set pools with pack-slot rarity odds (Battle Victories reward card); 0 if none.
        public static int RandomCard(OfflinePlayerStateData d)
        {
            List<List<int>[]> all = new List<List<int>[]>(pools.Values);
            if (all.Count == 0) return 0;
            List<int>[] pool;
            lock (random) pool = all[random.Next(all.Count)];
            int rarity = Roll(SlotOdds);
            while (rarity > 0 && pool[rarity].Count == 0) rarity--;
            return Pick(d, pool[rarity]);
        }

        private static int Roll(double[] odds)
        {
            double roll = random.NextDouble(), sum = 0;
            for (int r = 0; r < odds.Length; r++) { sum += odds[r]; if (roll < sum) return r; }
            return 0;
        }

        // Duplicate protection: prefer cards still below their max copies.
        private static int Pick(OfflinePlayerStateData d, List<int> cards)
        {
            if (cards.Count == 0) return 0;
            List<int> open = cards.FindAll(delegate(int id) { int max; return !maxCopies.TryGetValue(id, out max) || d.TotalCopies(id) < max; });
            List<int> from = open.Count > 0 ? open : cards;
            return from[random.Next(from.Count)];
        }
    }
}
