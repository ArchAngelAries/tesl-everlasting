using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed Oblivion Gate (-712591309), the token native PlayerActions.Invade creates or levels. Native Invade itself is
    // intact (creates the Gate in the right lane, fires the Gate's Invade trigger, counts InvadeCounter); the Gate's own graph
    // was server data. Rebuilt from its installed level text (attribute OblivionGateLevel, starts at 1) and UESP:
    //   Invade: the Gate gains a level and +0/+2.
    //   L1: when you summon a Daedra, give it +0/+1.     L2+: give it +1/+1.
    //   L3+: your Daedra cost 1 less (aura on your hand).  L4: also a random keyword.  L5+: also (level - 3) random keywords
    //   (installed text shows a live count; UESP gives two at level 5).
    // Immune to Silence and Permanently Shackled are installed native attributes.
    public static class OriginalOblivionGateRules
    {
        public const int Gate = -712591309;
        private const string Hash = "F95703BD1ED19639BA4D25514DBCF87D304B565F8D0AF5E6C1158E2EDC5C2069";
        private static Card owned;
        private static string ownedDescription;

        public static bool IsOriginalId(int id) { return id == Gate; }

        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).

        public static Card Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }


        private static Card ValidateRules(GameConfiguration config)
        {
            Card card = config.FindCardById(Gate);
            if (card == null || OriginalCardRules.Fingerprint(card) != Hash) throw new NotSupportedException("Original Oblivion Gate metadata differs.");
            if (owned == card)
            {
                if (Describe(card) != ownedDescription) throw new NotSupportedException("Owned Oblivion Gate graph changed or gained extra behavior.");
            }
            else OriginalCardRules.ValidateNoExtraRules(card);
            return card;
        }

        public static int[] Restore(GameConfiguration config)
        {
            Card card = Validate(config);
            if (owned != card)
            {
                card.TriggeredEffects = new List<TriggeredEffect> { InvadeTrigger(), DaedraTrigger() };
                card.Auras = new List<Aura> { CostAura() };
                card.ActionPlayInformation = new List<PlayCardAbility>();
                if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
                owned = card; ownedDescription = Describe(card);
            }
            if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
            if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
            return new int[] { Gate };
        }

        private static string Describe(Card card)
        {
            string s = "";
            foreach (TriggeredEffect t in card.TriggeredEffects) s += "T{" + OriginalGraphDescriber.Trigger(t) + "}";
            return s + "U{" + OriginalGraphDescriber.Auras(card.Auras) + "}";
        }

        private static TriggeredEffect InvadeTrigger()
        {
            SPTESL.Enchantment level = new SPTESL.Enchantment();
            level.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.OblivionGateLevel, 1));
            level.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, 2));
            CardEffectAction buff = new CardEffectAction { HashId = -1, Action = new CardActions.Buff { Enchantment = level } };
            buff.Selectors.Add(Role(CardRole.Self, true));
            Effect effect = new Effect(); effect.CardActions.Add(buff);
            TriggeredEffect trigger = new TriggeredEffect { TriggerType = EffectTriggerType.Invade, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            trigger.EffectGroup.Effects.Add(effect);
            return trigger;
        }

        private static TriggeredEffect DaedraTrigger()
        {
            TriggeredEffect trigger = new TriggeredEffect { TriggerType = EffectTriggerType.WhenCardEntersBoard, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            trigger.TriggerCardRequirements.Add(new CardRequirements.ZoneRequirement { LhsCard = CardRole.Self, Conditional = new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } } });
            trigger.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.Inciter, Conditional = new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Daedra } });
            trigger.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.Inciter, Conditional = new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature } });
            trigger.TriggerCardRequirements.Add(new CardRequirements.RelationshipRequirement { LhsCard = CardRole.Inciter, Conditional = new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends } });
            trigger.EffectGroup.Effects.Add(AtLevel(CardConditionals.AttributeValue.ComparisonType.EqualTo, 1, new CardActions.Buff { Enchantment = Stats(0, 1) }));
            trigger.EffectGroup.Effects.Add(AtLevel(CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, 2, new CardActions.Buff { Enchantment = Stats(1, 1) }));
            trigger.EffectGroup.Effects.Add(AtLevel(CardConditionals.AttributeValue.ComparisonType.EqualTo, 4, new CardActions.GiveKeyword { RandomKeyword = true }));
            ActionValueData many = new ActionValueData(ActionValueMethod.ValueOfCardAttribute) { CardRole = CardRole.Self, AttributeName = AttributeType.OblivionGateLevel, ConstantMultiplier = 1, ConstantValue = -3 };
            trigger.EffectGroup.Effects.Add(AtLevel(CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, 5, new CardActions.GiveRandomKeywords { NumKeywords = many }));
            return trigger;
        }

        private static Aura CostAura()
        {
            AuraEnchantment cheaper = new AuraEnchantment { Name = "Oblivion Gate Daedra cost", StackCount = new ActionValueData(1) };
            cheaper.Enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.ManaCost, -1));
            cheaper.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Daedra });
            cheaper.Selectors.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Hand } });
            cheaper.Selectors.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends });
            Aura aura = new Aura { NameForEditor = "Oblivion Gate level 3+" };
            aura.CardRequirements.Add(Level(CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, 3));
            aura.EnchantmentsToApply.Add(cheaper);
            return aura;
        }

        private static Effect AtLevel(CardConditionals.AttributeValue.ComparisonType cmp, int level, ICardAction action)
        {
            Effect effect = new Effect();
            effect.ExecuteCardRequirements.Add(Level(cmp, level));
            CardEffectAction a = new CardEffectAction { HashId = -1, Action = action };
            a.Selectors.Add(Role(CardRole.Inciter, true));
            effect.CardActions.Add(a);
            return effect;
        }
        private static CardRequirements.AttributeValue Level(CardConditionals.AttributeValue.ComparisonType cmp, int level)
        {
            return new CardRequirements.AttributeValue { LhsCard = CardRole.Self, Conditional = new CardConditionals.AttributeValue { AttributeName = AttributeType.OblivionGateLevel, Is = cmp, Value = new ConditionalValueData(level) } };
        }
        private static CardConditionals.CardRoleType Role(CardRole role, bool @is)
        { return new CardConditionals.CardRoleType { Check = @is ? CardConditionals.CardRoleType.Requirement.Is : CardConditionals.CardRoleType.Requirement.IsNot, Role = role }; }
        private static SPTESL.Enchantment Stats(int attack, int health)
        {
            SPTESL.Enchantment e = new SPTESL.Enchantment();
            if (attack != 0) e.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, attack));
            if (health != 0) e.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, health));
            return e;
        }
    }
}
