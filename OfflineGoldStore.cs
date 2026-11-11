using System;
using System.Collections;
using System.Collections.Generic;
using SparkleServices;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Gold store for what the original sold for real money or kept out of reach offline (offline only; prices in gold):
    //  - premade decks (Special Offers; key "deck_<TypeName>" so the client's DeckPurchaseWin shows the deck list);
    //  - card backs the player does not own yet (Special Offers);
    //  - story acts (Stories tab, filters DarkBrotherhood/ClockworkCity/IsleOfMadness/Puzzle): owned acts show as Owned. Stories
    //    are free offline unless the player turns on "stories cost gold" (OfflinePlayerStateData.storiesForGold), which
    //    re-locks unplayed acts and stops granting new stories for free.
    // Purchases arrive on shop/purchaseWithGold (OfflinePackService); grants land in the collection at once and the client's
    // inventory, cosmetics, campaign and shop views are refreshed on the main thread.
    public static class OfflineGoldStore
    {
        public const int CardBackGold = 300, PremiumCardBackGold = 500, StoryActGold = 1000, PuzzleActGold = 500;
        public static int DeckGold(DeckInfo info)
        {
            int cards = 0;
            foreach (DeckInfo.CardInfo c in info.Cards) cards += Math.Max(c.Count, c.NumToGive);
            return Math.Max(250, (cards * 15 + 49) / 50 * 50);   // 15 gold per card, rounded up to 50 (50 cards: 750)
        }

        public sealed class Offer
        {
            public string Key, Title, Description, Image, Kind; public int Gold; public bool Owned; public List<string> Tags = new List<string>();
            public DeckInfo Deck; public string CardBack; public SPTESL.Campaign Campaign; public Act Act;
        }

        private static readonly Queue<Action> mainThread = new Queue<Action>();
        private static readonly Dictionary<string, string> texts = new Dictionary<string, string>();
        public static void OnMainThread(Action a) { lock (mainThread) mainThread.Enqueue(a); }

        public static void Install(MonoBehaviour host) { host.StartCoroutine(Pump()); }

        private static IEnumerator Pump()
        {
            while (true)
            {
                yield return null;
                Action next = null;
                lock (mainThread) if (mainThread.Count > 0) next = mainThread.Dequeue();
                if (next != null) try { next(); } catch (Exception e) { Debug.LogWarning("OFFLINE GOLD STORE: " + e.Message); }
            }
        }

        // Localized names are read on the main thread once and cached for the services thread.
        public static void PublishTexts(GameConfiguration config)
        {
            LocalizationManager loc = LocalizationManager.Instance;
            if (config == null || loc == null) return;
            lock (texts)
            {
                foreach (DeckInfo d in OfflineInventoryService.AllDecks)
                    if (d != null && !string.IsNullOrEmpty(d.TypeName)) texts["deck:" + d.TypeName] = Safe(loc, string.IsNullOrEmpty(d.DisplayNameLocKey) ? d.DisplayName : d.DisplayNameLocKey);
                // Card backs: the game's own name, and the diffuse texture beside the card back material as store art.
                if (config.CardSharedGraphics != null)
                    foreach (string key in OfflineCosmetics.CardBacks)
                    {
                        CardBackData back = config.CardSharedGraphics.FindCardBackData(key);
                        if (back == null || (back.Type == CardBackType.Default && key != OfflineCosmetics.DefaultCardBack)) continue;
                        texts["cardback:" + key] = Safe(loc, back.NameTextKey.RawKey);
                        if (!string.IsNullOrEmpty(back.VisualAsset) && back.VisualAsset.IndexOf("/Materials/", StringComparison.Ordinal) >= 0)
                            texts["cardback-art:" + key] = back.VisualAsset.Replace("/Materials/", "/Textures/") + "_D";
                    }
                if (config.Campaigns != null)
                    foreach (SPTESL.Campaign c in config.Campaigns)
                    {
                        if (c == null) continue;
                        texts[c.DisplayNameKey.RawKey ?? ""] = Safe(loc, c.DisplayNameKey.RawKey);
                        foreach (Act a in c.Acts) if (a != null) texts[a.DisplayNameKey.RawKey ?? ""] = Safe(loc, a.DisplayNameKey.RawKey);
                    }
            }
        }

        public static int TextCount { get { lock (texts) return texts.Count; } }
        // Direct lookup when the main-thread cache isn't filled yet (the client may fetch the store first).
        private static string Loc(string key)
        {
            try
            {
                LocalizationManager loc = LocalizationManager.Instance;
                if (string.IsNullOrEmpty(key) || loc == null || loc.IsLoading()) return null;
                string t = loc.GetText(key);
                return string.IsNullOrEmpty(t) || t == key ? null : t;
            }
            catch (Exception) { return null; }
        }

        private static string Safe(LocalizationManager loc, string key) { return string.IsNullOrEmpty(key) ? "" : loc.GetText(key); }

        private static string Text(string key, string fallback)
        {
            string t;
            lock (texts) if (key != null && texts.TryGetValue(key, out t) && !string.IsNullOrEmpty(t) && t != key) return t;
            string mod = OfflineMods.Text(key);
            return !string.IsNullOrEmpty(mod) ? mod : fallback;
        }

        public static string StoryFilter(SPTESL.Campaign c)
        {
            switch (c.ResourceId.UniqueName)
            {
                case "DBH": return "DarkBrotherhood";
                case "CWC": return "ClockworkCity";
                case "IOM": return "IsleOfMadness";
                case "Puzzles": return "Puzzle";
                default: return null;
            }
        }

        public static List<Offer> Offers(OfflinePlayerStateData d)
        {
            List<Offer> offers = new List<Offer>();
            GameConfiguration config = GameManager.Instance == null ? null : GameManager.Instance.Config;
            foreach (DeckInfo info in OfflineInventoryService.AllDecks)
            {
                if (info == null || info.Cards == null || info.Cards.Count == 0 || string.IsNullOrEmpty(info.TypeName)) continue;
                if (info.TypeName.StartsWith("mod.", StringComparison.Ordinal) && !OfflineMods.IsEarnedDeck(info.TypeNameHash)) continue;   // granted free unless the mod is earned
                if (string.Equals(info.TypeName, "Default", StringComparison.OrdinalIgnoreCase) || string.Equals(Text("deck:" + info.TypeName, ""), "Default", StringComparison.OrdinalIgnoreCase)) continue; // template deck
                if (d.decks.Exists(delegate(Inventory.Deck x) { return x.deckTypeHash == info.TypeNameHash; })) continue;
                Offer o = new Offer { Kind = "deck", Deck = info, Key = "deck_" + info.TypeName, Gold = DeckGold(info) };
                o.Title = Text("deck:" + info.TypeName, Loc(string.IsNullOrEmpty(info.DisplayNameLocKey) ? info.DisplayName : info.DisplayNameLocKey) ?? Pretty(info.TypeName));
                int cards = 0; foreach (DeckInfo.CardInfo c in info.Cards) cards += Math.Max(c.Count, c.NumToGive);
                o.Description = "A ready-to-play " + cards + "-card deck. Its cards join your collection.";
                o.Image = DeckImage(config, info);
                o.Tags.Add("Special"); o.Tags.Add("Deck");
                offers.Add(o);
            }
            // Extension catalog decks offered now (EverlastingHost.RegisterStoreDecks), at the extension's price.
            foreach (EverlastingHost.StoreDeck s in EverlastingHost.StoreDecks(d))
            {
                DeckInfo info = s == null ? null : OfflineInventoryService.FindDeck(s.TypeName);
                if (info == null || info.Cards == null || info.Cards.Count == 0 || s.Gold <= 0) continue;
                if (d.decks.Exists(delegate(Inventory.Deck x) { return x.deckTypeHash == info.TypeNameHash; })) continue;
                if (offers.Exists(delegate(Offer x) { return x.Key == "deck_" + info.TypeName; })) continue;
                Offer o = new Offer { Kind = "deck", Deck = info, Key = "deck_" + info.TypeName, Gold = s.Gold };
                o.Title = Text(info.DisplayName, info.TypeName);
                int cards = 0; foreach (DeckInfo.CardInfo c in info.Cards) cards += Math.Max(c.Count, c.NumToGive);
                o.Description = !string.IsNullOrEmpty(s.Description) ? s.Description : "A ready-to-play " + cards + "-card deck. Its cards join your collection.";
                o.Image = DeckImage(config, info);
                o.Tags.Add("Special"); o.Tags.Add("Deck");
                offers.Add(o);
            }
            // The client keeps the first copy of a catalog item (image included), and its first fetch comes before the card
            // back graphics are loaded: card backs join the catalog only once their art path can be resolved.
            bool backsReady = config != null && config.CardSharedGraphics != null && CardBackArt(config, "cardback_scale") != null;
            foreach (string back in backsReady ? OfflineCosmetics.AllCardBacks() : new List<string>())
            {
                if (back == OfflineCosmetics.DefaultCardBack || d.ownedCardBacks.Contains(back) || OfflineMods.IsFreeCardBack(back)) continue;
                bool premium = back.IndexOf("premium", StringComparison.OrdinalIgnoreCase) >= 0;
                Offer o = new Offer { Kind = "cardback", CardBack = back, Key = "offline_" + back, Gold = OfflineMods.IsModCardBack(back) ? OfflineMods.CardBackPrice(back) : premium ? PremiumCardBackGold : CardBackGold };
                string name = OfflineMods.CardBackName(back) ?? Text("cardback:" + back, null); o.Title = (string.IsNullOrEmpty(name) ? Pretty(back) : name) + " Card Back";
                o.Image = CardBackArt(config, back); o.Description = (premium ? "Animated premium card back" : "Card back") + " for your decks.";
                o.Tags.Add("Special");
                offers.Add(o);
            }
            List<SPTESL.Campaign> campaigns = OfflineCampaignService.Content;
            if (campaigns != null)
                foreach (SPTESL.Campaign c in campaigns)
                {
                    string filter = c == null || !c.Production ? null : StoryFilter(c);
                    if (filter == null) continue;
                    SparkleServices.Campaign.IUserCampaignData user = d.campaigns.Find(delegate(SparkleServices.Campaign.IUserCampaignData x) { return x.campaignId == c.ResourceId.Id; });
                    for (int i = 0; i < c.Acts.Count; i++)
                    {
                        Act act = c.Acts[i];
                        if (act == null || act.Chapters.Count == 0) continue;
                        Offer o = new Offer { Kind = "story", Campaign = c, Act = act, Key = "offline_story_" + c.ResourceId.Id + "_" + act.ResourceId.Id };
                        o.Gold = filter == "Puzzle" ? PuzzleActGold : StoryActGold;
                        o.Owned = user != null && user.ownedActs != null && Array.IndexOf(user.ownedActs, act.ResourceId.Id) >= 0;
                        o.Title = Text(c.DisplayNameKey.RawKey, Loc(c.DisplayNameKey.RawKey) ?? c.ResourceId.UniqueName) + ": " + Text(act.DisplayNameKey.RawKey, Loc(act.DisplayNameKey.RawKey) ?? "Act " + (i + 1));
                        o.Description = act.Chapters.Count + " chapters" + (filter == "Puzzle" ? " of puzzles." : " of story, with their card rewards.");
                        o.Image = string.IsNullOrEmpty(c.DisplayBanner) ? null : c.DisplayBanner;
                        o.Tags.Add(filter == "Puzzle" ? "Puzzle" : "Story"); o.Tags.Add(filter);
                        offers.Add(o);
                    }
                }
            return offers;
        }

        // The diffuse texture beside a card back's material (AppBase/Cards/Materials/Backs/X -> .../Textures/Backs/X_D).
        public static string CardBackArt(GameConfiguration config, string key)
        {
            string mod = OfflineMods.CardBackStoreArt(key);
            if (mod != null) return mod;
            if (config == null || config.CardSharedGraphics == null) return null;
            CardBackData back = config.CardSharedGraphics.FindCardBackData(key);
            // Newer official backs (Alliance War on) are prefabs or keep their texture beside them under another name:
            // data/cardback-store-art.tsv maps them to the game's own texture (owner 2026-11-07: 52 backs had no store image).
            string mapped = back == null || string.IsNullOrEmpty(back.VisualAsset) ? null : MappedCardBackArt(back.VisualAsset);
            if (mapped != null) return mapped;
            if (back == null || string.IsNullOrEmpty(back.VisualAsset) || back.VisualAsset.IndexOf("/Materials/", StringComparison.Ordinal) < 0) return null;
            return back.VisualAsset.Replace("/Materials/", "/Textures/") + "_D";
        }

        private static Dictionary<string, string> cardBackArt;

        private static string MappedCardBackArt(string visualAsset)
        {
            if (cardBackArt == null)
            {
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string file = OfflinePaths.Data("cardback-store-art.tsv");
                    if (System.IO.File.Exists(file))
                        foreach (string raw in System.IO.File.ReadAllLines(file))
                        {
                            string line = raw.TrimEnd(new char[] { (char)13 });
                            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                            string[] f = line.Split((char)9);
                            if (f.Length < 2) continue;
                            string path = f[1];
                            // file:<path under data/store-art> = a rendered image for a layered back with no single texture
                            if (path.StartsWith("file:", StringComparison.Ordinal))
                                path = new Uri(System.IO.Path.GetFullPath(System.IO.Path.Combine(OfflinePaths.Data("store-art"), path.Substring(5)))).AbsoluteUri;
                            map[f[0]] = path;
                        }
                }
                catch (Exception e) { Debug.LogWarning("OFFLINE STORE ART: card back map: " + e.Message); }
                cardBackArt = map;
            }
            string name = visualAsset.Substring(visualAsset.LastIndexOf('/') + 1);
            string art;
            return cardBackArt.TryGetValue(name, out art) ? art : null;
        }

        private static string DeckImage(GameConfiguration config, DeckInfo info)
        {
            if (config == null) return null;
            if (config.DeckArt != null)
            {
                DeckArtData art = config.DeckArt.Find(delegate(DeckArtData a) { return a != null && a.ResourceId.Id == info.ArtTypeHash; });
                if (art != null && !string.IsNullOrEmpty(art.LargeImagePath)) return art.LargeImagePath;
            }
            Card card = config.FindCardById(info.ArtTypeHash);
            return card == null ? null : card.Image;
        }

        private static string Pretty(string key)
        {
            string s = key.StartsWith("cardback_", StringComparison.Ordinal) ? key.Substring(9) : key;
            while (s.Contains("__")) s = s.Replace("__", "_");
            s = s.Replace("_premium", "").Replace("AW_", "Alliance War ");
            string[] words = s.Split('_');
            for (int i = 0; i < words.Length; i++) if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words).Trim();
        }

        public static List<Shop.CatalogItem> CatalogItems(OfflinePlayerStateData d, int firstIndex)
        {
            List<Shop.CatalogItem> items = new List<Shop.CatalogItem>();
            int index = firstIndex;
            foreach (Offer o in Offers(d))
            {
                Shop.CatalogItem item = new Shop.CatalogItem();
                item.itemKey = o.Key; item.platformKey = o.Key; item.index = index++;
                item.itemState = o.Owned ? Shop.CatalogItem.IChannelCatalogItemState.Owned : Shop.CatalogItem.IChannelCatalogItemState.Normal;
                item.priceGold = o.Gold;
                item.summaryTitle = o.Title; item.detailsTitle = o.Title; item.summaryDescription = o.Description; item.detailsDescription = o.Description;
                item.imageUrl = o.Image; item.frontPageImageUrl = o.Image;
                item.tags = new List<string>(o.Tags);
                items.Add(item);
            }
            return items;
        }

        // Applies a gold purchase inside the state mutation. Returns false (and spends nothing) when it can't.
        public static bool Buy(OfflinePlayerStateData d, string key)
        {
            Offer offer = Offers(d).Find(delegate(Offer o) { return o.Key == key; });
            if (offer == null || offer.Owned || d.gold < offer.Gold) return false;
            bool done;
            switch (offer.Kind)
            {
                case "deck": done = OfflineInventoryService.GrantDeck(d, offer.Deck); break;
                case "cardback": OfflineCosmetics.GrantCardBack(d, offer.CardBack); done = d.ownedCardBacks.Contains(offer.CardBack); break;
                case "story": done = OfflineCampaignService.GrantAct(d, offer.Campaign, offer.Act); break;
                default: done = false; break;
            }
            if (!done) return false;
            d.gold -= offer.Gold;
            OnMainThread(RefreshClient);
            return true;
        }

        public static bool IsGoldStoreKey(string key)
        { return key != null && (key.StartsWith("deck_", StringComparison.Ordinal) || key.StartsWith("offline_", StringComparison.Ordinal)); }

        public static void RefreshClient()
        {
            ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
            if (services == null) return;
            if (services.Profile != null) services.Profile.Refresh();
            if (services.Inventory != null) services.Inventory.Refresh();
            if (services.GenericInventory != null) services.GenericInventory.Refresh();
            if (services.Campaign != null) services.Campaign.Refresh();
            if (services.Shop != null) services.Shop.Refresh();
        }

        // "Stories cost gold": on re-locks every act with no completed chapter (except The Forgotten Hero), off owns all again.
        public static int SetStoriesForGold(OfflinePlayerStateData d, bool on)
        {
            d.storiesForGold = on;
            int changed = 0;
            List<SPTESL.Campaign> campaigns = OfflineCampaignService.Content;
            if (campaigns == null) return 0;
            foreach (SPTESL.Campaign c in campaigns)
            {
                if (c == null || !c.Production || c.ResourceId.Id == OfflineCampaignService.ForgottenHeroCampaignId || OfflineStories.CampaignIds.Contains(c.ResourceId.Id)) continue;   // mod stories stay free
                if (on) changed += OfflineCampaignService.RelockUnplayed(d, c);
                else foreach (Act a in c.Acts) if (a != null && a.Chapters.Count > 0 && OfflineCampaignService.GrantAct(d, c, a)) changed++;
            }
            return changed;
        }
    }
}
