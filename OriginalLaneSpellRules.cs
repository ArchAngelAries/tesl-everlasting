using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card "... in a lane" action wrappers from exact installed text, not recovered server graphs.
    // A lane choice is a native SPTESL.DecisionType.Lane selector; the chosen CcgRpc.BoardLaneTarget is converted natively
    // (ServerEffectUtilities) into CardConditionalArguments.LaneDecision, and creatures are selected with native
    // CardConditionals.InTargetedLane (SameLane; SamePlayer = only the chosen side, AnyPlayer = both sides of that lane).
    // Enemy-only spells restrict the choice with native LaneConditionals.LaneOwner(Enemies, Self). Effects are native
    // Freeze/Destroy/Damage. The shipped client's GatherSpellLaneTargets is a stub; CommandRestoration.LaneTargets restores it.
    public static class OriginalLaneSpellRules
    {
        public enum Kind { ShackleEnemyLane, DestroyEnemyLowPowerLane, DestroyLane, DamageLane }
        private sealed class Entry
        {
            public int Id, A; public Kind Kind; public string Hash;
            public Entry(int id, Kind kind, int a, string hash) { Id = id; Kind = kind; A = a; Hash = hash; }
        }
        private static readonly Entry[] entries = new Entry[] {
            new Entry(33791690, Kind.ShackleEnemyLane, 0, "ACDEBE8C640CFADAF49EF50EA692CF5704F91FD4D7173626719E4FA368192943"), // Corpse Curse
            new Entry(-1065777733, Kind.ShackleEnemyLane, 0, "9ECEA60BE0EA79B875209D8ECE1F37D3CCBD422AC440F00A5931A0F391DA94A5"), // Winter's Touch
            new Entry(105525150, Kind.DestroyEnemyLowPowerLane, 2, "11F5EF60CCA2A86B443DAFE79D1F63ADC602EF25BDAB25E2A559987E3EE0F61A"), // Arrow Storm
            new Entry(-1294830066, Kind.DestroyEnemyLowPowerLane, 2, "4258E7AAB2625B3179D7813E073DA5386F91A0CE846001627AB748F1BDB689A1"), // Arrow Storm
            new Entry(-916534147, Kind.DestroyLane, 0, "473CC1E3B83E97773125AACC69CD592D9757027D88794FFDEA6820C5654734F7"), // Dawn's Wrath
            new Entry(1095134115, Kind.DamageLane, 2, "4D1C6878B328461EAF7574177B39633702988A21E20DF765602561783CA32C83"), // Fire Storm (Prophecy)
        };
        private static readonly Dictionary<Card, PlayCardAbility> owned = new Dictionary<Card, PlayCardAbility>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited lane spell ID: " + id); return e.Kind; }
        public static int Value(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited lane spell ID: " + id); return e.A; }
        private static Entry Find(int id) { foreach (Entry e in entries) if (e.Id == id) return e; return null; }

        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).

        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }


        private static List<Card> ValidateRules(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            List<Card> result = new List<Card>();
            foreach (Entry e in entries)
            {
                Card card = config.FindCardById(e.Id);
                if (card == null || OriginalCardRules.Fingerprint(card) != e.Hash)
                    throw new NotSupportedException("Original lane spell metadata differs: " + e.Id);
                PlayCardAbility ability;
                if (owned.TryGetValue(card, out ability))
                {
                    bool valid = (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0) && (card.Auras == null || card.Auras.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                        && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0))
                        && card.ActionPlayInformation != null && card.ActionPlayInformation.Count == 1 && card.ActionPlayInformation[0] == ability
                        && OriginalGraphDescriber.Ability(ability) == OriginalGraphDescriber.Ability(Create(e));
                    if (!valid) throw new NotSupportedException("Owned original lane spell graph changed or gained extra behavior: " + e.Id);
                }
                else OriginalCardRules.ValidateNoExtraRules(card);
                result.Add(card);
            }
            return result;
        }

        public static int[] Restore(GameConfiguration config)
        {
            List<Card> cards = Validate(config); // Atomic complete inventory preflight before mutations.
            List<int> ids = new List<int>();
            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                if (!owned.ContainsKey(card))
                {
                    PlayCardAbility ability = Create(entries[i]);
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

        private static PlayCardAbility Create(Entry e)
        {
            bool enemyOnly = e.Kind == Kind.ShackleEnemyLane || e.Kind == Kind.DestroyEnemyLowPowerLane;
            DecisionConditionalExpression lane = new DecisionConditionalExpression { DecisionType = DecisionType.Lane };
            if (enemyOnly) lane.LaneConditionalExpressions.Add(new LaneConditionals.LaneOwner { OwnedBy = LaneConditionals.LaneOwner.RelationshipType.Enemies, PlayerRole = PlayerRole.Self });
            ICardAction native;
            switch (e.Kind)
            {
                case Kind.ShackleEnemyLane: native = new CardActions.Freeze { PermanentFreeze = false, Type = EnchantmentUtilities.FreezeType.Normal }; break;
                case Kind.DamageLane: native = new CardActions.Damage { HealthLoss = new ActionValueData(e.A), DamageSource = CardRole.Self }; break;
                default: native = new CardActions.Destroy { Sacrifice = false }; break;
            }
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = native };
            action.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
            action.Selectors.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } });
            action.Selectors.Add(new CardConditionals.InTargetedLane { Lane = CardConditionals.InTargetedLane.LaneRequirement.SameLane,
                Owner = enemyOnly ? CardConditionals.InTargetedLane.OwnerRequirement.SamePlayer : CardConditionals.InTargetedLane.OwnerRequirement.AnyPlayer });
            if (e.Kind == Kind.DestroyEnemyLowPowerLane)
                action.Selectors.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.Attack,
                    Is = CardConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo, Value = new ConditionalValueData(e.A) });
            Effect effect = new Effect(); effect.CardActions.Add(action);
            EffectGroup group = new EffectGroup(); group.Effects.Add(effect);
            PlayCardAbility ability = new PlayCardAbility { EffectGroup = group, PlayTargetingType = PlayTargetingType.RequiredTarget };
            ability.TargetSelectors.Add(lane);
            return ability;
        }
    }
}
