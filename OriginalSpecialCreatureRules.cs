using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Authored original description fully expressed by surviving native attributes.
    // Snowy's two attacks use original ServerCard methods, with no added effects.
    public static class OriginalSpecialCreatureRules
    {
        public const int SnowySabreCatId = 151390294;
        private const string SnowyMetadata = "4B2381B412F4995E50BCC755A97FC38864E4FFF80AE87CF1612DC582CC39BCB4";

        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).

        public static Card Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }


        private static Card ValidateRules(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            Card card = config.FindCardById(SnowySabreCatId);
            if (card == null || OriginalCardRules.Fingerprint(card) != SnowyMetadata)
                throw new NotSupportedException("Original Snowy Sabre Cat metadata differs from exact installed audit.");
            OriginalCardRules.ValidateNoExtraRules(card);
            CardDefinition definition = config.CardDefinitions.Find(delegate(CardDefinition d) { return d.Name == card.Definition; });
            if (definition == null || definition.TriggeredEffects == null || definition.TriggeredEffects.Count != 4
                || definition.Auras == null || definition.Auras.Count != 0 || definition.Subtypes.Count != 1
                || !definition.Subtypes.Contains(CardSubtype.Creature) || definition.ZoneLookupBoard != GameZoneName.CreatureBoard
                || definition.ZoneLookupDeck != GameZoneName.Deck || definition.ZoneLookupHand != GameZoneName.Hand
                || definition.ZoneLookupCemetery != GameZoneName.Cemetery)
                throw new NotSupportedException("Installed shared Creature definition differs from original Snowy audit.");
            return card;
        }

        public static int[] Restore(GameConfiguration config)
        {
            Card card = Validate(config);
            if (card.TriggeredEffects == null) card.TriggeredEffects = new List<TriggeredEffect>();
            if (card.Auras == null) card.Auras = new List<Aura>();
            if (card.ActionPlayInformation == null) card.ActionPlayInformation = new List<PlayCardAbility>();
            if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
            if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
            if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
            return new int[] { SnowySabreCatId };
        }
    }
}
