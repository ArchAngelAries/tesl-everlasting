using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using SPTESL;

namespace OfflineEngine
{
    // Catalog identities and recipes are copied from installed data. Missing
    // NonSerialized preset recipes stay unavailable; never fill them by guessing.
    public static class OfflineDeckCatalog
    {
        public const string PracticeName = "__TESL_OFFLINE_DEVELOPMENT_PRACTICE__";
        public static List<Entry> Entries = new List<Entry>();
        private static GameConfiguration recipeConfiguration;
        private static readonly HashSet<string> originalRecipes = new HashSet<string>(StringComparer.Ordinal);
        public sealed class Entry
        {
            public string Key;
            public string Name;
            public Deck Deck;
            public bool CanPlay;
            public string Reason;
            public int OriginalId;
            public bool IsOriginal;
        }

        public static List<Entry> Create(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (!Object.ReferenceEquals(recipeConfiguration, config)) { originalRecipes.Clear(); recipeConfiguration = config; }
            List<Entry> result = new List<Entry>();
            int originalIndex = 0;
            if (config.CustomDecks != null)
                foreach (CustomDeck original in config.CustomDecks)
                {
                    int ordinal = originalIndex++;
                    if (original == null) continue;
                    if (original.Decks == null || original.Decks.Count == 0)
                        result.Add(Unavailable("preset/" + original.ResourceId.Id + "/" + ordinal, original.ResourceId.UniqueName,
                            original.ResourceId.Id, "Original preset definitions are missing from installed configuration."));
                    else for (int i = 0; i < original.Decks.Count; i++)
                    {
                        PresetDeck preset = original.Decks[i];
                        string name = original.ResourceId.UniqueName + " / " + (preset == null ? "missing" : preset.Difficulty.ToString());
                        string key = "preset/" + original.ResourceId.Id + "/" + ordinal + "/" + i;
                        if (preset == null || preset.Cards == null || preset.Cards.Count == 0)
                            result.Add(Unavailable(key, name, original.ResourceId.Id,
                                "Original card recipe is absent: PresetDeck.Cards is not serialized in this installed configuration."));
                        else
                        {
                            Deck deck = new Deck { Name = original.ResourceId.UniqueName, AvatarCardId = OrdinaryMatchRules.BaseAvatarId };
                            foreach (DeckCardInfo c in preset.Cards) deck.Cards.Add(c == null ? null : new DeckCardInfo(c.Id, c.Count));
                            result.Add(Inspect(config, key, name, original.ResourceId.Id, deck));
                        }
                    }
                }
            originalIndex = 0;
            if (config.DeckInfo != null)
                foreach (DeckInfo original in config.DeckInfo)
                {
                    int ordinal = originalIndex++;
                    if (original == null) continue;
                    Deck deck = new Deck { Name = original.TypeName, AvatarCardId = OrdinaryMatchRules.BaseAvatarId };
                    if (original.Cards != null)
                        foreach (DeckInfo.CardInfo c in original.Cards) deck.Cards.Add(c == null ? null : new DeckCardInfo(c.Id, c.Count));
                    Entry entry = Inspect(config, "deck-info/" + original.TypeNameHash + "/" + ordinal, original.TypeName.Replace('_', ' '), original.TypeNameHash, deck);
                    result.Add(entry);
                }
            return result;
        }

        public static void Load(GameConfiguration config, string directory)
        {
            originalRecipes.Clear(); recipeConfiguration = config;
            Entries = Create(config);
            foreach (OriginalResourceDeckRecovery.Recipe original in OriginalResourceDeckRecovery.Read(directory))
                // Resources Deck text has no CustomDeck ResourceId. Keep that ID
                // absent rather than inventing a game identity or difficulty map.
                Entries.Add(Inspect(config, "resource/" + original.AssetName + "/" + original.Offset,
                    original.Deck.Name + " (original Resources deck)", UniqueResourceId.GetInvalid().Id, original.Deck));
            StringBuilder report = new StringBuilder("key\toriginal_id\tname\tcan_play\treason\n");
            foreach (Entry entry in Entries) report.AppendLine(entry.Key + "\t" + entry.OriginalId + "\t" + Clean(entry.Name) + "\t" + entry.CanPlay + "\t" + Clean(entry.Reason));
            File.WriteAllText(Path.Combine(directory, "offline-original-decks.tsv"), report.ToString());
        }
        private static string Clean(string value) { return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        private static Entry Unavailable(string key, string name, int id, string reason)
        { return new Entry { Key = key, Name = name, OriginalId = id, IsOriginal = true, CanPlay = false, Reason = reason }; }
        private static Entry Inspect(GameConfiguration config, string key, string name, int id, Deck deck)
        {
            Entry entry = new Entry { Key = key, Name = name, OriginalId = id, IsOriginal = true, Deck = deck };
            string fingerprint = RecipeFingerprint(deck);
            if (fingerprint != null) originalRecipes.Add(fingerprint);
            try { ValidateOriginalRecipe(config, deck); entry.CanPlay = true; entry.Reason = "Exact original recipe ready with bounded ordinary field/shadow rules; original scenario fidelity remains unverified."; }
            catch (NotSupportedException e) { entry.Reason = e.Message; }
            return entry;
        }

        // Original installed debug/scenario recipes retain their exact size,
        // counts and attributes. User/generated construction uses native bounds.
        public static void ValidateForPlay(GameConfiguration config, Deck deck)
        {
            string fingerprint = RecipeFingerprint(deck);
            if (Object.ReferenceEquals(recipeConfiguration, config) && fingerprint != null && originalRecipes.Contains(fingerprint))
                ValidateOriginalRecipe(config, deck);
            else Validate(config, deck);
        }
        private static void ValidateOriginalRecipe(GameConfiguration config, Deck deck)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (deck == null || deck.Cards == null || deck.Cards.Count == 0) throw new NotSupportedException("Original deck/card recipe is missing.");
            if (deck.AvatarCardId != OrdinaryMatchRules.BaseAvatarId || config.FindCardById(deck.AvatarCardId) == null)
                throw new NotSupportedException("This offline rule scope requires the original base gameplay Avatar; other avatar rules remain unrestored.");
            List<string> unsupported = new List<string>();
            long total = 0;
            foreach (DeckCardInfo item in deck.Cards)
            {
                if (item == null || item.Count <= 0) throw new NotSupportedException("Original recipe contains a missing entry or nonpositive copy count.");
                total += item.Count;
                if (total > int.MaxValue) throw new NotSupportedException("Original recipe total overflows native Deck.Size.");
                Card card = config.FindCardById(item.Id);
                if (card == null) throw new NotSupportedException("Original card ID " + item.Id + " is missing from installed configuration.");
                if (!RuleImporter.IsSupported(item.Id)) unsupported.Add(card.Name + " [" + item.Id + "]");
            }
            RejectUnrestored(unsupported);
        }
        private static void RejectUnrestored(List<string> unsupported)
        {
            // User direction (2026-10-15): decks are never refused for cards whose text effects are still being
            // reconstructed; those cards play with native metadata and are reported by RuleImporter coverage.
            return;
            if (unsupported.Count == 0) return;
            int shown = Math.Min(4, unsupported.Count);
            throw new NotSupportedException(unsupported.Count + " card types require unrestored rules: " + string.Join(", ", unsupported.GetRange(0, shown).ToArray())
                + (shown < unsupported.Count ? ", and " + (unsupported.Count - shown) + " more." : "."));
        }
        private static string RecipeFingerprint(Deck deck)
        {
            if (deck == null || deck.Cards == null || deck.Cards.Count == 0) return null;
            StringBuilder recipe = new StringBuilder();
            string name = deck.Name ?? "";
            recipe.Append(name.Length).Append(':').Append(name).Append('|').Append((int)deck.Type).Append('|').Append(deck.AvatarCardId);
            foreach (DeckCardInfo item in deck.Cards)
            { if (item == null || item.Count <= 0) return null; recipe.Append('|').Append(item.Id).Append(':').Append(item.Count); }
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(recipe.ToString()))).Replace("-", "");
        }

        // Mirrors surviving native Deck color rule and Inventory edit bounds.
        // Story, arena and event size/copy overrides require their own scope.
        public static void Validate(GameConfiguration config, Deck deck)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (deck == null || deck.Cards == null) throw new NotSupportedException("Deck/card recipe is missing.");
            if (deck.AvatarCardId != OrdinaryMatchRules.BaseAvatarId || config.FindCardById(deck.AvatarCardId) == null)
                throw new NotSupportedException("Offline ordinary rules require the original base Avatar.");
            HashSet<int> seen = new HashSet<int>();
            HashSet<CardSubtype> colors = new HashSet<CardSubtype>();
            Dictionary<int, int> copies = new Dictionary<int, int>();
            List<string> unsupported = new List<string>();
            int size = 0; bool hasTriColorCard = false;
            foreach (DeckCardInfo item in deck.Cards)
            {
                if (item == null || item.Count <= 0 || item.Count > 100) throw new NotSupportedException("Deck contains a missing card entry or invalid copy count.");
                if (!seen.Add(item.Id)) throw new NotSupportedException("Deck contains duplicate recipe entries for card ID " + item.Id + ".");
                Card card = config.FindCardById(item.Id);
                if (card == null) throw new NotSupportedException("Original card ID " + item.Id + " is missing from installed configuration.");
                if (!RuleImporter.IsSupported(item.Id)) unsupported.Add(card.Name + " [" + item.Id + "]");
                if (card.MaxCopies <= 0 || item.Count > card.MaxCopies)
                    throw new NotSupportedException(card.Name + " allows at most " + card.MaxCopies + " copies; deck requests " + item.Count + ".");
                int canonical = card.AlternateOfCard != null && card.AlternateOfCard.resourceId != UniqueResourceId.GetInvalid().Id ? card.AlternateOfCard.resourceId : item.Id;
                int prior; copies.TryGetValue(canonical, out prior); copies[canonical] = prior + item.Count;
                Card baseCard = config.FindCardById(canonical) ?? card;
                if (copies[canonical] > baseCard.MaxCopies) throw new NotSupportedException("Combined original/alternate copies exceed " + baseCard.Name + " limit " + baseCard.MaxCopies + ".");
                int cardColors = 0;
                foreach (CardSubtype color in card.Subtypes)
                    if (color >= CardSubtype.Strength && color <= CardSubtype.Endurance) { colors.Add(color); cardColors++; }
                if (cardColors == 3) hasTriColorCard = true;
                size += item.Count;
                if (size > 100) throw new NotSupportedException("Native deck maximum is 100 cards.");
            }
            RejectUnrestored(unsupported);
            if (colors.Count > 3) throw new NotSupportedException("Native deck construction supports at most three attributes.");
            if (colors.Count == 3 && !hasTriColorCard) throw new NotSupportedException("Native three-attribute decks require an original card carrying all three attributes.");
            int minimum = colors.Count == 3 ? 75 : 50;
            if (size < minimum) throw new NotSupportedException("Native decks with " + colors.Count + " attributes require at least " + minimum + " cards; this recipe has " + size + ".");
        }

        public static Deck Clone(Deck source)
        {
            if (source == null) throw new ArgumentNullException("source");
            Deck copy = new Deck { Name = source.Name, Type = source.Type, AvatarCardId = source.AvatarCardId };
            if (source.Cards == null) { copy.Cards = null; return copy; }
            foreach (DeckCardInfo item in source.Cards) copy.Cards.Add(item == null ? null : new DeckCardInfo(item.Id, item.Count));
            return copy;
        }

        // Explicit generated development practice recipe, separate from every
        // original identity. Original metadata and copy limits remain unchanged.
        public static Deck CreatePracticeDeck(GameConfiguration config)
        {
            List<Card> best = null;
            int bestCopies = 0;
            for (CardSubtype first = CardSubtype.Strength; first <= CardSubtype.Endurance; first++)
                for (CardSubtype second = first; second <= CardSubtype.Endurance; second++)
                {
                    List<Card> candidates = new List<Card>(); int available = 0;
                    foreach (CardSet set in config.CardCollection) foreach (Card card in set.Cards)
                    {
                        if (!RuleImporter.IsSupported(card.ResourceId.Id) || !card.Production || card.Definition != "Creature"
                            || card.MaxCopies <= 0 || string.IsNullOrEmpty(card.ExportCode)
                            || (card.AlternateOfCard != null && card.AlternateOfCard.resourceId != UniqueResourceId.GetInvalid().Id)) continue;
                        bool fits = true;
                        foreach (CardSubtype color in card.Subtypes)
                            if (color >= CardSubtype.Strength && color <= CardSubtype.Endurance && color != first && color != second) fits = false;
                        if (fits) { candidates.Add(card); available += Math.Min(3, card.MaxCopies); }
                    }
                    if (available > bestCopies) { bestCopies = available; best = candidates; }
                }
            if (bestCopies < 50) throw new NotSupportedException("Reviewed original creatures cannot yet form a legal 50-card ordinary practice deck.");
            best.Sort(delegate(Card a, Card b) { int cost = ManaCost(a).CompareTo(ManaCost(b)); return cost != 0 ? cost : a.ResourceId.Id.CompareTo(b.ResourceId.Id); });
            Deck deck = new Deck { Name = PracticeName, AvatarCardId = OrdinaryMatchRules.BaseAvatarId };
            int remaining = 50;
            foreach (Card card in best)
            { int count = Math.Min(remaining, Math.Min(3, card.MaxCopies)); deck.SetCardCount(card.ResourceId.Id, count); remaining -= count; if (remaining == 0) break; }
            Validate(config, deck);
            return deck;
        }

        private static int ManaCost(Card card)
        { IntAttribute value = card.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == AttributeType.ManaCost; }) as IntAttribute; return value == null ? int.MaxValue : value.Value; }
    }
}
