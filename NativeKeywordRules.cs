using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // One audited original keyword-only card. Preserve native keyword/stat/type
    // metadata and shared Creature rules; reconstruct only missing empty wrappers.
    // This deliberately does not admit arbitrary cards with an empty description.
    public static class NativeKeywordRules
    {
        public const int LurkingCrocodileId = 794235553;

        public static int[] Restore(GameConfiguration config)
        {
            Card card = Validate(config);
            List<TriggeredEffect> triggers = card.TriggeredEffects ?? new List<TriggeredEffect>();
            List<Aura> auras = card.Auras ?? new List<Aura>();
            List<PlayCardAbility> abilities = card.ActionPlayInformation ?? new List<PlayCardAbility>();
            PlayCardRequirements play = card.PlayRequirements ?? new PlayCardRequirements();
            PlayCardRequirements betray = card.BetrayRequirements ?? new PlayCardRequirements();
            ActivatedAbility active = card.ActivatedAbility ?? new ActivatedAbility();
            card.TriggeredEffects = triggers;
            card.Auras = auras;
            card.ActionPlayInformation = abilities;
            card.PlayRequirements = play;
            card.BetrayRequirements = betray;
            card.ActivatedAbility = active;
            return new int[] { LurkingCrocodileId };
        }

        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).

        public static Card Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }


        private static Card ValidateRules(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            Card card = config.FindCardById(LurkingCrocodileId);
            if (card == null || card.Name != "Lurking Crocodile" || card.ResourceId.UniqueName != "Lurking Crocodile"
                || card.Definition != "Creature" || !card.Production || card.ContentPackIndex != 0
                || card.ExportCode != "jS" || card.Unique || card.MaxCopies != 3
                || card.ResourceId.CompletionType.ToString() != "Complete" || card.Rarity != CardRarity.Common
                || card.Image != "ContentPack000/Cards/Visuals/marsh_crocodile/marsh_crocodile"
                || card.NameKey.RawKey != "62a40f7a-5be2-4ee6-b8fe-47678157e81b-title")
                Reject("original identity/type/art metadata differs");
            if (!String.IsNullOrEmpty(card.Description) || !String.IsNullOrEmpty(card.DescriptionKey.RawKey)
                || card.MechanicsEntries == null || card.MechanicsEntries.Count != 1
                || card.MechanicsEntries[0].MechanicType != UIGameMechanicTypes.Prophecy
                || !card.MechanicsEntries[0].DisplayHanger || card.MechanicsEntries[0].DisplayIcon)
                Reject("original keyword-only description/mechanic metadata differs");
            if (card.Subtypes == null || card.Subtypes.Count != 3 || !card.Subtypes.Contains(CardSubtype.Neutral)
                || !card.Subtypes.Contains(CardSubtype.Reptile) || !card.Subtypes.Contains(CardSubtype.Animal)
                || card.PermanentHangers == null || card.PermanentHangers.Count != 0
                || card.CardReferences == null || card.CardReferences.Count != 0
                || card.DoubleCardReferences == null || card.DoubleCardReferences.Count != 0
                || card.DynamicTextAssignments == null || card.DynamicTextAssignments.Count != 0
                || card.DefaultRareAttributes == null || card.DefaultRareAttributes.Count != 0
                || (card.HuntTypes != null && card.HuntTypes.Count != 0)
                || (card.UpgradesInto != null && card.UpgradesInto.Count != 0)
                || card.MobilizeRecruitId != null
                || card.AttackRequirements == null || card.AttackRequirements.PlayerRequirements.Count != 0
                || card.AttackRequirements.CardRequirements.Count != 0 || card.AttackRequirements.LaneRequirements.Count != 0)
                Reject("additional card references, text assignments or requirements exist");
            CardDefinition definition = config.CardDefinitions.Find(delegate(CardDefinition d) { return d.Name == card.Definition; });
            if (definition == null || definition.Subtypes.Count != 1 || !definition.Subtypes.Contains(CardSubtype.Creature)
                || definition.ZoneLookupBoard != GameZoneName.CreatureBoard || definition.ZoneLookupDeck != GameZoneName.Deck
                || definition.ZoneLookupHand != GameZoneName.Hand || definition.ZoneLookupCemetery != GameZoneName.Cemetery
                || definition.TriggeredEffects == null || definition.TriggeredEffects.Count != 4
                || definition.Auras == null || definition.Auras.Count != 0)
                Reject("original shared Creature definition differs");
            ValidateAttributes(card);
            if ((card.TriggeredEffects != null && card.TriggeredEffects.Count != 0)
                || (card.Auras != null && card.Auras.Count != 0)
                || (card.ActionPlayInformation != null && card.ActionPlayInformation.Count != 0)
                || (card.PlayRequirements != null && card.PlayRequirements.PlayerRequirements.Count != 0)
                || (card.BetrayRequirements != null && card.BetrayRequirements.PlayerRequirements.Count != 0)
                || (card.ActivatedAbility != null && (card.ActivatedAbility.AbilityGroups.Count != 0
                    || card.ActivatedAbility.TargetSelectors.Count != 0
                    || card.ActivatedAbility.ActivationCardRequirements.Count != 0
                    || card.ActivatedAbility.ActivationPlayerRequirements.Count != 0)))
                Reject("existing card rules contain extra behavior; preserve them for separate review");
            return card;
        }

        private static void ValidateAttributes(Card card)
        {
            if (card.EntityAttributes == null || card.EntityAttributes.Count != 56) Reject("native attribute inventory differs");
            HashSet<AttributeType> seen = new HashSet<AttributeType>();
            foreach (SPTESL.Attribute attribute in card.EntityAttributes)
            {
                if (attribute == null || !seen.Add(attribute.Name)) Reject("duplicate/missing original attribute");
                IntAttribute integer = attribute as IntAttribute;
                BoolAttribute flag = attribute as BoolAttribute;
                if (integer != null)
                {
                    int expected = attribute.Name == AttributeType.ManaCost ? 2
                        : attribute.Name == AttributeType.Attack ? 3
                        : attribute.Name == AttributeType.MaxHealth ? 2
                        : attribute.Name == AttributeType.NumAttacksPerTurn ? 1 : 0;
                    if (integer.Value != expected) Reject("extra/differing integer attribute " + attribute.Name);
                }
                else if (flag != null)
                {
                    bool expected = attribute.Name == AttributeType.Prophecy || attribute.Name == AttributeType.BoardSplash;
                    if (flag.Value != expected) Reject("extra/differing boolean attribute " + attribute.Name);
                }
                else Reject("unknown attribute implementation");
            }
            foreach (AttributeType required in new AttributeType[] { AttributeType.ManaCost, AttributeType.Attack,
                AttributeType.MaxHealth, AttributeType.NumAttacksPerTurn, AttributeType.Prophecy, AttributeType.BoardSplash })
                if (!seen.Contains(required)) Reject("missing required native attribute " + required);
            if (!(card.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == AttributeType.Prophecy; }) is BoolAttribute))
                Reject("native Prophecy must be its original boolean attribute");
            foreach (AttributeType required in new AttributeType[] { AttributeType.ManaCost, AttributeType.Attack,
                AttributeType.MaxHealth, AttributeType.NumAttacksPerTurn })
                if (!(card.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == required; }) is IntAttribute))
                    Reject("native statistic must be its original integer attribute " + required);
        }

        private static void Reject(string detail)
        { throw new NotSupportedException("Audited Lurking Crocodile gate: " + detail + "."); }
    }
}
