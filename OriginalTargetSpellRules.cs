using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Missing on-play wrappers reconstructed from exact installed original text
    // and the surviving native Destroy algorithm. Not recovered server graphs.
    public static class OriginalTargetSpellRules
    {
        public const int PiercingJavelinId = -751645406;
        public const int AlternatePiercingJavelinId = -1839856508;
        private static readonly Dictionary<int, string> fingerprints = new Dictionary<int, string> {
            { PiercingJavelinId, "95F025BB2C859D854CA511FA7F6D1027E9572120B7FE0AE38B9E9DE3E7EBF48B" },
            { AlternatePiercingJavelinId, "14D683A96430740C60128AC5063CE0B46245391CD0147D6B4CDAC3EF3228D84C" }
        };
        private static readonly Dictionary<Card, PlayCardAbility> owned = new Dictionary<Card, PlayCardAbility>();

        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).

        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }


        private static List<Card> ValidateRules(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            List<Card> result = new List<Card>();
            foreach (KeyValuePair<int, string> expected in fingerprints)
            {
                Card card = config.FindCardById(expected.Key);
                if (card == null || OriginalCardRules.Fingerprint(card) != expected.Value)
                    throw new NotSupportedException("Original Piercing Javelin " + expected.Key + " metadata differs from exact installed audit.");
                PlayCardAbility previous;
                if (owned.TryGetValue(card, out previous)) ValidateOwned(card, previous);
                else OriginalCardRules.ValidateNoExtraRules(card);
                result.Add(card);
            }
            return result;
        }

        public static int[] Restore(GameConfiguration config)
        {
            List<Card> validated = Validate(config); // Validate both originals before mutation.
            List<int> ids = new List<int>();
            foreach (Card card in validated)
            {
                if (!owned.ContainsKey(card))
                {
                    CardEffectAction destroy = new CardEffectAction { HashId = -1, Action = new CardActions.Destroy { Sacrifice = false } };
                    destroy.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
                    Effect effect = new Effect(); effect.CardActions.Add(destroy);
                    EffectGroup group = new EffectGroup(); group.Effects.Add(effect);
                    PlayCardAbility ability = new PlayCardAbility { EffectGroup = group, PlayTargetingType = PlayTargetingType.RequiredTarget,
                        TargetSelectors = RuleImporter.CreatureSelectors() };
                    card.ActionPlayInformation = new List<PlayCardAbility> { ability };
                    owned.Add(card, ability);
                }
                if (card.TriggeredEffects == null) card.TriggeredEffects = new List<TriggeredEffect>();
                if (card.Auras == null) card.Auras = new List<Aura>();
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        private static void ValidateOwned(Card card, PlayCardAbility ability)
        {
            bool empty = (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0) && (card.Auras == null || card.Auras.Count == 0)
                && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                    && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0));
            if (!empty || card.ActionPlayInformation == null || card.ActionPlayInformation.Count != 1 || card.ActionPlayInformation[0] != ability
                || ability.PlayTargetingType != PlayTargetingType.RequiredTarget || ability.CustomUiType != CustomCardUiType.None
                || ability.EffectGroup == null || ability.EffectGroup.Effects.Count != 1 || ability.TargetSelectors == null || ability.TargetSelectors.Count != 1)
                throw new NotSupportedException("Owned Piercing Javelin wrapper was changed or contains additional behavior.");
            Effect effect = ability.EffectGroup.Effects[0];
            if (effect.CardActions.Count != 1 || effect.PlayerActions.Count != 0)
                throw new NotSupportedException("Owned Piercing Javelin effect graph differs.");
            CardEffectAction action = effect.CardActions[0];
            CardActions.Destroy destroy = action.Action as CardActions.Destroy;
            CardConditionals.CardRoleType role = action.Selectors.Count == 1 ? action.Selectors[0] as CardConditionals.CardRoleType : null;
            DecisionConditionalExpression selector = ability.TargetSelectors[0];
            CardConditionals.Subtype type = selector.CardConditionalExpressions.Count == 2 ? selector.CardConditionalExpressions[0] as CardConditionals.Subtype : null;
            CardConditionals.Zone zone = selector.CardConditionalExpressions.Count == 2 ? selector.CardConditionalExpressions[1] as CardConditionals.Zone : null;
            if (destroy == null || destroy.Sacrifice || role == null || role.Check != CardConditionals.CardRoleType.Requirement.Is || role.Role != CardRole.DecisionTarget
                || selector.DecisionType != SPTESL.DecisionType.Card || type == null || type.Check != CardConditionals.Subtype.Requirement.Is || type.SubtypeName != CardSubtype.Creature
                || zone == null || zone.Check != CardConditionals.Zone.Requirement.IsIn || zone.ZoneTypeMask.Bits != GameZoneType.Board)
                throw new NotSupportedException("Owned Piercing Javelin native action/target selectors differ.");
        }
    }
}
