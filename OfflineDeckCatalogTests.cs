using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    public static class OfflineDeckCatalogTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        { Run(config, test, null); }
        public static void Run(GameConfiguration config, Action<string, Action> test, string directory)
        {
            test("offline-original-deck-catalog-and-native-legal-practice", delegate {
                int expected = config.DeckInfo.Count;
                foreach (CustomDeck original in config.CustomDecks) expected += original.Decks == null || original.Decks.Count == 0 ? 1 : original.Decks.Count;
                List<OfflineDeckCatalog.Entry> entries = OfflineDeckCatalog.Create(config);
                Require(entries.Count == expected, "Original deck/preset identities disappeared from the catalog.");
                HashSet<string> keys = new HashSet<string>();
                foreach (OfflineDeckCatalog.Entry e in entries)
                {
                    Require(e.IsOriginal && keys.Add(e.Key), "Catalog replaced or duplicated an original identity.");
                    if (e.CanPlay) OfflineDeckCatalog.ValidateForPlay(config, e.Deck);
                    else Require(!string.IsNullOrEmpty(e.Reason), "Unavailable original deck lacks a readable technical reason.");
                }
                Deck practice = OfflineDeckCatalog.CreatePracticeDeck(config);
                Require(practice.Size == 50 && practice.AvatarCardId == OrdinaryMatchRules.BaseAvatarId, "Generated ordinary practice recipe differs.");
                Require(config.CustomDecks.Find(delegate(CustomDeck c) { return c.ResourceId.UniqueName == practice.Name; }) == null,
                    "Generated practice name collides with an original avatar-selection identity.");
                foreach (DeckCardInfo c in practice.Cards)
                    Require(RuleImporter.IsSupported(c.Id) && config.FindCardById(c.Id).Definition == "Creature", "Practice recipe contains an unsupported/non-creature card.");
                Deck copy = OfflineDeckCatalog.Clone(practice);
                copy.Cards[0].Count = config.FindCardById(copy.Cards[0].Id).MaxCopies + 1;
                Reject(config, copy);
                Require(practice.Size == 50 && !Object.ReferenceEquals(copy.Cards[0], practice.Cards[0]), "Changing clone mutated selected/original deck recipe.");
                copy = OfflineDeckCatalog.Clone(practice); copy.Cards.Add(new DeckCardInfo(copy.Cards[0].Id, 1)); Reject(config, copy);
                copy = OfflineDeckCatalog.Clone(practice); copy.AvatarCardId = 0; Reject(config, copy);
                // User direction (2026-10-15): a deck containing an original card whose text effects are still being
                // reconstructed is accepted (the card plays with native metadata); it is no longer refused.
                // Stay within the recipe's attributes so native deck legality (two attributes) is unaffected.
                List<CardSubtype> colors = new List<CardSubtype>();
                foreach (DeckCardInfo d in practice.Cards)
                    foreach (CardSubtype st in config.FindCardById(d.Id).Subtypes)
                        if (st >= CardSubtype.Strength && st <= CardSubtype.Endurance && !colors.Contains(st)) colors.Add(st);
                Card metadataOnly = null;
                foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                {
                    if (metadataOnly != null || c.Definition != "Creature" || c.MaxCopies < 3 || RuleImporter.IsSupported(c.ResourceId.Id) || !c.Production
                        || practice.Cards.Exists(delegate(DeckCardInfo d) { return d.Id == c.ResourceId.Id; })) continue;
                    bool fits = c.Subtypes.Exists(delegate(CardSubtype st) { return st >= CardSubtype.Strength && st <= CardSubtype.Endurance; });
                    foreach (CardSubtype st in c.Subtypes) if (st >= CardSubtype.Strength && st <= CardSubtype.Endurance && !colors.Contains(st)) fits = false;
                    if (fits) metadataOnly = c;
                }
                if (metadataOnly != null)
                {
                    copy = OfflineDeckCatalog.Clone(practice); copy.Cards[0] = new DeckCardInfo(metadataOnly.ResourceId.Id, Math.Min(practice.Cards[0].Count, metadataOnly.MaxCopies));
                    if (copy.Cards[0].Count < practice.Cards[0].Count) copy.Cards.Add(new DeckCardInfo(practice.Cards[0].Id, practice.Cards[0].Count - copy.Cards[0].Count));
                    OfflineDeckCatalog.Validate(config, copy);
                }
                if (directory != null) AssertRecoveredDefault(config, directory);
            });
        }
        public static void AssertRecoveredDefault(GameConfiguration config, string directory)
        {
            OfflineDeckCatalog.Load(config, directory);
            OriginalResourceDeckRecovery.Recipe original = OriginalResourceDeckRecovery.Read(directory).Find(
                delegate(OriginalResourceDeckRecovery.Recipe r) { return r.AssetName == "DefaultDeck"; });
            Require(original != null && original.Offset == 2781684 && original.Deck.Name == "Default"
                && original.Deck.Size == 30 && original.Deck.Cards.Count == 19, "Exact original Resources DefaultDeck recipe was changed or mistaken for a fifty-card deck.");
            foreach (DeckCardInfo c in original.Deck.Cards) Require(RuleImporter.IsSupported(c.Id), "Original Resources DefaultDeck still lacks rule support for " + c.Id + ".");
            Deck copy = OfflineDeckCatalog.Clone(original.Deck);
            Require(copy.Size == 30 && copy.Cards.Count == 19, "Original Resources clone expanded/removed source cards.");
            for (int i = 0; i < copy.Cards.Count; i++) Require(copy.Cards[i].Id == original.Deck.Cards[i].Id
                && copy.Cards[i].Count == original.Deck.Cards[i].Count && !Object.ReferenceEquals(copy.Cards[i], original.Deck.Cards[i]), "Original Resources cloned recipe differs.");
            OfflineDeckCatalog.Entry catalog = OfflineDeckCatalog.Entries.Find(delegate(OfflineDeckCatalog.Entry e) { return e.Key == "resource/DefaultDeck/2781684"; });
            Require(catalog != null && catalog.CanPlay && catalog.Deck.Size == 30, "Exact restored original Default recipe is not playable or was padded.");
            OfflineDeckCatalog.ValidateForPlay(config, copy);
            bool ordinaryRejected = false;
            try { OfflineDeckCatalog.Validate(config, copy); } catch (NotSupportedException e) { ordinaryRejected = !string.IsNullOrEmpty(e.Message); }
            Require(ordinaryRejected, "The original historical/debug recipe unexpectedly passed ordinary constructed-deck rules or lacked a readable rejection reason.");
            copy.Cards[0].Count++;
            bool changedRejected = false;
            try { OfflineDeckCatalog.ValidateForPlay(config, copy); } catch (NotSupportedException) { changedRejected = true; }
            Require(changedRejected && original.Deck.Size == 30 && catalog.Deck.Size == 30,
                "A changed thirty-card recipe borrowed the exact original playback exemption or mutated its source.");
        }
        private static void Reject(GameConfiguration config, Deck deck)
        {
            bool rejected = false; try { OfflineDeckCatalog.Validate(config, deck); } catch (NotSupportedException) { rejected = true; }
            Require(rejected, "Deck validation admitted a malformed or unrestored recipe.");
        }
        private static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    }
}
