using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Local Inventory service: the collection (every collectible card type, with owned
    // counts from the player state) and the player's decks, in the client's own
    // inventory/list_v2 wire format, plus createDeck / saveDeck_v2 / deleteDeck / setSeens.
    // Card metadata (rarity, colours, copies) comes from the installed configuration.
    // Crafting prices are reconstructed from the published TESL economy (soul gems:
    // craft 50/100/400/1200, soul trap 5/20/100/400; premium craft 400/800/1600/3600,
    // premium soul trap 50/100/400/1200) and are not server-proven.
    public static class OfflineInventoryService
    {
        private static readonly int[] CraftPrice = { 50, 100, 400, 1200 }, Sell = { 5, 20, 100, 400 };
        private static readonly int[] PremiumCraft = { 400, 800, 1600, 3600 }, PremiumSell = { 50, 100, 400, 1200 };
        private static volatile List<Inventory.CardTypeLoadable> catalog = new List<Inventory.CardTypeLoadable>();
        private static volatile Dictionary<string, DeckInfo> deckInfos = new Dictionary<string, DeckInfo>(StringComparer.OrdinalIgnoreCase);

        private static volatile Dictionary<int, Inventory.CardTypeLoadable> types = new Dictionary<int, Inventory.CardTypeLoadable>();
        public static bool HasCatalog { get { return catalog.Count > 0; } }
        public const int FreeDeckCopies = 100;

        // Main thread, once the configuration has loaded.
        // Extension catalog decks (EverlastingHost.RegisterCatalogDecks): resolvable by name, never in AllDecks.
        private static readonly HashSet<int> extensionDecks = new HashSet<int>();
        public static bool IsExtensionDeck(int typeHash) { lock (extensionDecks) return extensionDecks.Contains(typeHash); }

        private static void AddCatalogDecks(SPTESL.GameConfiguration config)
        {
            if (config.DeckInfo == null) config.DeckInfo = new List<DeckInfo>();
            HashSet<int> present = new HashSet<int>();
            foreach (DeckInfo d in config.DeckInfo) if (d != null) present.Add(d.TypeNameHash);
            foreach (KeyValuePair<string, EverlastingHost.CatalogDeck> e in EverlastingHost.CatalogDecks())
            {
                EverlastingHost.CatalogDeck c = e.Value;
                int hash = EverlastingHost.DeckTypeHash(c.TypeName);
                lock (extensionDecks) extensionDecks.Add(hash);
                if (present.Contains(hash)) continue;
                DeckInfo info = new DeckInfo { TypeName = c.TypeName, TypeNameHash = hash, DisplayName = c.TypeName + ".name", IsProduction = true, ArtTypeHash = c.ArtCardId };
                info.DisplayNameLocKey = info.DisplayName;
                OfflineMods.SetText(info.DisplayName, c.Name);
                info.Cards = new List<DeckInfo.CardInfo>();
                foreach (KeyValuePair<int, int> card in c.Cards)
                    if (config.FindCardById(card.Key) != null) info.Cards.Add(new DeckInfo.CardInfo { Id = card.Key, Count = card.Value, NumToGive = card.Value });
                config.DeckInfo.Add(info); present.Add(hash);
            }
        }

        public static void PublishCatalog(SPTESL.GameConfiguration config, string reportPath)
        {
            AddCatalogDecks(config);
            List<Inventory.CardTypeLoadable> bases = new List<Inventory.CardTypeLoadable>(), alternates = new List<Inventory.CardTypeLoadable>();
            HashSet<int> ids = new HashSet<int>();
            foreach (SPTESL.CardSet set in config.CardCollection)
                foreach (SPTESL.Card card in set.Cards)
                {
                    if (card == null || !card.Production || string.IsNullOrEmpty(card.ExportCode) || !ids.Add(card.ResourceId.Id)) continue;
                    Inventory.CardTypeLoadable type = new Inventory.CardTypeLoadable();
                    int rarity = Mathf.Clamp((int)card.Rarity, 0, 3);
                    type.typeHash = card.ResourceId.Id; type.typeName = card.ResourceId.UniqueName; type.exportCode = card.ExportCode;
                    type.rarity = ((Inventory.CardType.Rarity)rarity).ToString();
                    type.maxCopies = card.Unique ? 1 : card.MaxCopies;
                    type.canCraft = true; type.regularSeen = true; type.premiumSeen = true;
                    type.purchasePriceGems = CraftPrice[rarity]; type.sellPriceGems = Sell[rarity];
                    type.premiumPurchasePriceGems = PremiumCraft[rarity]; type.premiumSellPriceGems = PremiumSell[rarity];
                    type.colorSubTypes = new List<string>();
                    foreach (string color in new string[] { "Strength", "Intelligence", "Willpower", "Agility", "Endurance", "Neutral" })
                        if (card.HasSubtype((SPTESL.CardSubtype)Enum.Parse(typeof(SPTESL.CardSubtype), color))) type.colorSubTypes.Add(color);
                    int alternate = card.AlternateOfCard == null ? 0 : card.AlternateOfCard.resourceId;
                    type.alternateOfCardTypeHash = alternate == SPTESL.UniqueResourceId.GetInvalid().Id ? 0 : alternate;
                    (type.alternateOfCardTypeHash == 0 ? bases : alternates).Add(type);
                }
            // The client requires each alternate's base type to precede it.
            HashSet<int> baseIds = new HashSet<int>();
            foreach (Inventory.CardTypeLoadable type in bases) baseIds.Add(type.typeHash);
            int dropped = alternates.RemoveAll(delegate(Inventory.CardTypeLoadable a) { return !baseIds.Contains(a.alternateOfCardTypeHash); });
            bases.AddRange(alternates);
            Dictionary<string, DeckInfo> decks = new Dictionary<string, DeckInfo>(StringComparer.OrdinalIgnoreCase);
            StringBuilder report = new StringBuilder("kind\tname\ttype_hash\tcards\n");
            if (config.DeckInfo != null)
                foreach (DeckInfo info in config.DeckInfo)
                {
                    if (info == null) continue;
                    // Display names are localization keys; index every readable form.
                    List<string> names = new List<string> { info.DisplayName, info.TypeName, Localized(info.DisplayName), Localized(info.DisplayNameLocKey) };
                    names.RemoveAll(delegate(string n) { return string.IsNullOrEmpty(n); });
                    foreach (string name in names) decks[name] = info;
                    report.Append("deck\t").Append(string.Join(" | ", names.ToArray())).Append('\t').Append(info.TypeNameHash).Append('\t').Append(info.Cards == null ? 0 : info.Cards.Count).Append('\n');
                }
            report.Append("summary\tcard types ").Append(bases.Count).Append(", alternates without base dropped ").Append(dropped).Append("\t\t\n");
            Dictionary<int, Inventory.CardTypeLoadable> byId = new Dictionary<int, Inventory.CardTypeLoadable>();
            foreach (Inventory.CardTypeLoadable type in bases) byId[type.typeHash] = type;
            catalog = bases; deckInfos = decks; types = byId;
            File.WriteAllText(reportPath, report.ToString());
        }

        private static string Localized(string key)
        {
            if (string.IsNullOrEmpty(key) || LocalizationManager.Instance == null) return null;
            string text;   // TryGetText: some deck names are plain text, and GetText logs "Key not found" for them
            return LocalizationManager.Instance.TryGetText(key, out text) && text != key ? text : null;
        }

        // Collectible card types (with maxCopies) and the distinct installed premade decks, for unlock-all.
        public static IEnumerable<Inventory.CardTypeLoadable> CatalogTypes { get { return types.Values; } }
        public static List<DeckInfo> AllDecks
        {
            get { List<DeckInfo> list = new List<DeckInfo>(); foreach (DeckInfo d in deckInfos.Values) if (!list.Contains(d) && !IsExtensionDeck(d.TypeNameHash)) list.Add(d); return list; }
        }

        public static DeckInfo FindDeck(string name)
        {
            DeckInfo info;
            return deckInfos.TryGetValue(name, out info) ? info : null;
        }

        // The collection and decks as the client's inventory list reads them.
        public static Inventory.InventoryListResponse List(OfflinePlayerStateData d)
        {
            Inventory.InventoryListResponse response = new Inventory.InventoryListResponse();
            response.resultCode = ResultCode.Ok;
            response.inventory = new Inventory.InventoryDescLoadable();
            response.inventory.deckLimit = 50;
            response.inventory.decks = d.decks;
            response.inventory.cardTypes = new List<Inventory.CardTypeLoadable>();
            foreach (Inventory.CardTypeLoadable template in catalog)
            {
                Inventory.CardTypeLoadable type = JsonUtility.FromJson<Inventory.CardTypeLoadable>(JsonUtility.ToJson(template));
                int index = d.cardIds.IndexOf(type.typeHash);
                type.numRegular = index < 0 ? 0 : d.cardCounts[index];
                type.numPremium = index < 0 ? 0 : d.cardPremiumCounts[index];
                if (d.freeDeckBuilding)
                {   // free deck building: no copy limit and enough copies of every card (a deck holds at most 100 cards)
                    type.maxCopies = FreeDeckCopies;
                    type.numRegular = Math.Max(type.numRegular, FreeDeckCopies);
                }
                response.inventory.cardTypes.Add(type);
            }
            return response;
        }

        public static void Register(OfflineLocalServices services, OfflinePlayerState state)
        {
            services.Register("inventory/list_v2", delegate { return JsonUtility.ToJson(List(state.Snapshot())); });
            services.Register("inventory/createDeck", delegate(string m, string p, string body) {
                Inventory.InventoryCreateDeckRequest request = JsonUtility.FromJson<Inventory.InventoryCreateDeckRequest>(body);
                long id = 0;
                state.Mutate(delegate(OfflinePlayerStateData d) {
                    id = ++d.nextDeckId;
                    Inventory.Deck deck = new Inventory.Deck();
                    deck.id = id; deck.UnlocalizedName = request == null ? "" : request.displayName;
                    deck.hasBeenSeen = true; deck.cards = new List<Inventory.Deck.DeckCard>();
                    d.decks.Add(deck); });
                Inventory.InventoryCreateDeckResponse response = new Inventory.InventoryCreateDeckResponse();
                response.resultCode = ResultCode.Ok; response.deckId = id;
                return JsonUtility.ToJson(response); });
            services.Register("inventory/saveDeck_v2", delegate(string m, string p, string body) {
                Inventory.Deck deck = JsonUtility.FromJson<Inventory.Deck>(body);
                bool saved = false;
                if (deck != null)
                    state.Mutate(delegate(OfflinePlayerStateData d) {
                        int index = d.decks.FindIndex(delegate(Inventory.Deck x) { return x.id == deck.id; });
                        if (index < 0) return;
                        d.decks.RemoveAt(index); d.decks.Insert(0, deck); saved = true; });
                Inventory.SaveResult result = new Inventory.SaveResult();
                result.resultCode = saved ? ResultCode.Ok : ResultCode.ClientHttpErrorBadRequest; result.success = saved;
                return JsonUtility.ToJson(result); });
            // Crafting (soul summon / soul trap) at the catalog's reconstructed gem prices.
            services.Register("crafting/purchase", delegate(string m, string p, string body) { return Craft(state, body, true); });
            services.Register("crafting/sell", delegate(string m, string p, string body) { return Craft(state, body, false); });
            services.Register("crafting/soulTrapExtras", delegate(string m, string p, string body) {
                Crafting.SoulTrapExtrasRequest request = JsonUtility.FromJson<Crafting.SoulTrapExtrasRequest>(body);
                string priority = request == null ? "allExtras" : request.priority;
                uint gained = 0;
                state.Mutate(delegate(OfflinePlayerStateData d) {
                    for (int i = 0; i < d.cardIds.Count; i++)
                    {
                        Inventory.CardTypeLoadable type;
                        if (!types.TryGetValue(d.cardIds[i], out type)) continue;
                        int extra = d.cardCounts[i] + d.cardPremiumCounts[i] - type.maxCopies;
                        if (extra <= 0) continue;
                        // Extras go regular-first unless premiums are asked for.
                        int premium = priority == "nonPremiums" ? 0 : priority == "premiums" ? Math.Min(extra, d.cardPremiumCounts[i]) : Math.Max(0, extra - d.cardCounts[i]);
                        int regular = priority == "premiums" ? 0 : Math.Min(extra - premium, d.cardCounts[i]);
                        d.cardCounts[i] -= regular; d.cardPremiumCounts[i] -= premium;
                        int value = regular * type.sellPriceGems + premium * type.premiumSellPriceGems;
                        d.gems += value; gained += (uint)value;
                    } });
                Crafting.SoultTrapExtrasResponse response = new Crafting.SoultTrapExtrasResponse();
                response.resultCode = ResultCode.Ok; response.numGems = gained;
                return JsonUtility.ToJson(response); });
            services.Register("inventory/deleteDeck", delegate(string m, string p, string body) {
                Inventory.InventoryDeleteDeckRequest request = JsonUtility.FromJson<Inventory.InventoryDeleteDeckRequest>(body);
                if (request != null) state.Mutate(delegate(OfflinePlayerStateData d) { d.decks.RemoveAll(delegate(Inventory.Deck x) { return x.id == request.deckId; }); });
                return JsonUtility.ToJson(new HTTPUtils.BaseResult { resultCode = ResultCode.Ok }); });
        }

        private static string Craft(OfflinePlayerState state, string body, bool purchase)
        {
            Crafting.CraftingRequest request = JsonUtility.FromJson<Crafting.CraftingRequest>(body);
            Crafting.CraftingResponse response = new Crafting.CraftingResponse();
            response.resultCode = ResultCode.ClientHttpErrorBadRequest;
            Inventory.CardTypeLoadable type;
            if (request == null || request.amount <= 0 || !types.TryGetValue(request.typeHash, out type) || (purchase && !type.canCraft)) return JsonUtility.ToJson(response);
            state.Mutate(delegate(OfflinePlayerStateData d) {
                int index = d.cardIds.IndexOf(request.typeHash);
                int regular = index < 0 ? 0 : d.cardCounts[index], premium = index < 0 ? 0 : d.cardPremiumCounts[index];
                int price = request.isPremium ? (purchase ? type.premiumPurchasePriceGems : type.premiumSellPriceGems) : (purchase ? type.purchasePriceGems : type.sellPriceGems);
                if (purchase)
                {
                    if (d.gems < price * request.amount) return;
                    d.gems -= price * request.amount;
                    d.AddCards(request.typeHash, request.isPremium ? 0 : request.amount, request.isPremium ? request.amount : 0);
                }
                else
                {
                    int have = request.isPremium ? premium : regular;
                    if (have < request.amount) return;
                    d.AddCards(request.typeHash, request.isPremium ? 0 : -request.amount, request.isPremium ? -request.amount : 0);
                    d.gems += price * request.amount;
                }
                index = d.cardIds.IndexOf(request.typeHash);
                response.resultCode = ResultCode.Ok; response.cardTypeHash = request.typeHash; response.gems = d.gems;
                response.numRegular = d.cardCounts[index]; response.numPremium = d.cardPremiumCounts[index]; response.ghostedChange = false; });
            return JsonUtility.ToJson(response);
        }

        // Grants an installed deck: its cards join the collection and it becomes a saved deck.
        public static bool GrantDeck(OfflinePlayerStateData d, DeckInfo info)
        {
            if (info == null || info.Cards == null) return false;
            Inventory.Deck deck = new Inventory.Deck();
            deck.id = ++d.nextDeckId; deck.UnlocalizedName = info.DisplayName; deck.hasBeenSeen = false; deck.isValid = true;
            deck.artTypeHash = info.ArtTypeHash; deck.deckTypeHash = info.TypeNameHash;
            deck.cards = new List<Inventory.Deck.DeckCard>();
            foreach (DeckInfo.CardInfo card in info.Cards)
            {
                int give = card.NumToGive >= 0 ? card.NumToGive : card.Count;
                if (give > 0) d.AddCards(card.Id, give);
                deck.cards.Add(new Inventory.Deck.DeckCard { typeHash = card.Id, numRegular = card.Count, numPremium = 0 });
            }
            d.decks.Add(deck);
            return true;
        }
    }
}
