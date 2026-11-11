using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using SPTESL;

namespace OfflineEngine
{
    // Installed originals with no authored effects: retain all native metadata and
    // shared Creature rules (including Regenerate/Rally). Fill absent empty wrappers.
    // This manifest is an evidence inventory, never an ownership or purchase gate.
    public static partial class OriginalCardRules
    {
        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).
        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }

        private static List<Card> ValidateRules(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            List<Card> cards = new List<Card>();
            CardDefinition definition = config.CardDefinitions.Find(delegate(CardDefinition d) { return d.Name == "Creature"; });
            if (definition == null || definition.TriggeredEffects == null || definition.TriggeredEffects.Count != 4
                || definition.Auras == null || definition.Auras.Count != 0 || definition.Subtypes.Count != 1
                || !definition.Subtypes.Contains(CardSubtype.Creature) || definition.ZoneLookupBoard != GameZoneName.CreatureBoard
                || definition.ZoneLookupDeck != GameZoneName.Deck || definition.ZoneLookupHand != GameZoneName.Hand
                || definition.ZoneLookupCemetery != GameZoneName.Cemetery)
                throw new NotSupportedException("Installed shared Creature definition differs from original audit.");
            foreach (KeyValuePair<int, string> original in Manifest)
            {
                Card card = config.FindCardById(original.Key);
                if (card == null || Fingerprint(card) != original.Value) Reject(original.Key, "installed metadata differs from exact original audit");
                ValidateNoExtraRules(card);
                cards.Add(card);
            }
            return cards;
        }

        public static int[] Restore(GameConfiguration config)
        {
            List<Card> cards = Validate(config); // Finish complete preflight before the first mutation.
            List<int> ids = new List<int>();
            foreach (Card card in cards)
            {
                if (card.TriggeredEffects == null) card.TriggeredEffects = new List<TriggeredEffect>();
                if (card.Auras == null) card.Auras = new List<Aura>();
                if (card.ActionPlayInformation == null) card.ActionPlayInformation = new List<PlayCardAbility>();
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        public static bool Contains(int id) { return Manifest.ContainsKey(id); }
        public static int Count { get { return Manifest.Count; } }

        internal static void ValidateNoExtraRules(Card card)
        {
            if ((card.TriggeredEffects != null && card.TriggeredEffects.Count != 0)
                || (card.Auras != null && card.Auras.Count != 0)
                || (card.ActionPlayInformation != null && card.ActionPlayInformation.Count != 0)
                || (card.PlayRequirements != null && card.PlayRequirements.PlayerRequirements.Count != 0)
                || (card.BetrayRequirements != null && card.BetrayRequirements.PlayerRequirements.Count != 0)
                || (card.ActivatedAbility != null && (card.ActivatedAbility.AbilityGroups.Count != 0
                    || card.ActivatedAbility.TargetSelectors.Count != 0
                    || card.ActivatedAbility.ActivationCardRequirements.Count != 0
                    || card.ActivatedAbility.ActivationPlayerRequirements.Count != 0)))
                Reject(card.ResourceId.Id, "existing extra behavior requires its original rule recovery");
        }

        // Same field ordering and text as OriginalCardConfigurationHarness TSV.
        // Includes all 56 original attribute types/values; no statistics or keyword is rewritten.
        internal static string Fingerprint(Card card)
        {
            List<string> attributes = new List<string>();
            foreach (SPTESL.Attribute attribute in card.EntityAttributes)
            {
                IntAttribute number = attribute as IntAttribute;
                BoolAttribute flag = attribute as BoolAttribute;
                if (number == null && flag == null) Reject(card.ResourceId.Id, "unknown native attribute type");
                attributes.Add(attribute.Name + ":" + (number != null ? number.Value.ToString() : flag.Value.ToString()));
            }
            List<string> mechanics = new List<string>();
            foreach (UIGameMechanicEntry entry in card.MechanicsEntries)
                mechanics.Add(entry.MechanicType + ":" + entry.DisplayHanger + ":" + entry.DisplayIcon);
            List<string> subtypes = new List<string>();
            foreach (CardSubtype subtype in card.Subtypes) subtypes.Add(subtype.ToString());
            string row = String.Join("\t", new string[] {
                card.ResourceId.Id.ToString(), Text(card.Name), Text(card.Definition), card.Production.ToString(),
                card.ResourceId.CompletionType.ToString(), Text(card.Description), Text(card.DescriptionKey.RawKey),
                String.Join(",", attributes.ToArray()), String.Join(",", mechanics.ToArray()), String.Join(",", subtypes.ToArray()),
                CountOf(card.PermanentHangers), CountOf(card.CardReferences), CountOf(card.DoubleCardReferences),
                CountOf(card.DynamicTextAssignments), CountOf(card.DefaultRareAttributes), CountOf(card.HuntTypes), CountOf(card.UpgradesInto),
                Text(card.MobilizeRecruitId), card.AttackRequirements == null ? "NULL" : CountOf(card.AttackRequirements.PlayerRequirements)
                    + "," + CountOf(card.AttackRequirements.CardRequirements) + "," + CountOf(card.AttackRequirements.LaneRequirements),
                Text(card.ExportCode), Text(card.NameKey.RawKey), Text(card.Image), card.ContentPackIndex.ToString(),
                card.Rarity.ToString(), card.Unique.ToString(), card.MaxCopies.ToString(), card.Source.ToString(), Text(card.ResourceId.UniqueName)
            });
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(row))).Replace("-", "");
        }

        private static string Text(object value)
        { return value == null ? "" : value.ToString().Replace("\t", " ").Replace("\r", " ").Replace("\n", " "); }
        private static string CountOf(System.Collections.ICollection value) { return value == null ? "-1" : value.Count.ToString(); }
        private static void Reject(int id, string reason)
        { throw new NotSupportedException("Original card " + id + ": " + reason + "."); }
    }
}
