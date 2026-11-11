using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent play wrappers for exact installed Betray actions, not recovered server graphs. The Betray
    // keyword itself is the original metadata attribute; the offer/sacrifice/free replay is OfflineBetrayRestoration.
    public static class OriginalBetrayRules
    {
        public enum Kind { Drain, Debuff, LaneEnemyDamage, SummonChosenLane }
        private sealed class Entry
        {
            public int Id, A, B; public Kind Kind; public string Hash;
            public Entry(int id, Kind kind, int a, int b, string hash) { Id = id; Kind = kind; A = a; B = b; Hash = hash; }
        }
        private static readonly Entry[] entries = new Entry[] {
            new Entry(-987206472, Kind.Drain, 2, 2, "5601896D7FE98653218AA2BF1CDCF2CF0F57964B300A12C7355B433393B74AB4"), // Blood Sacrament
            new Entry(6794648, Kind.Debuff, -2, -2, "474C38BF44E054B1E0D3F9700578C99B3F2AD515722272EEB041E2AC4DD7F889"), // Drain Blood
            new Entry(834379060, Kind.LaneEnemyDamage, 2, 0, "417B44E7D7CA6861B528CF6E911759DFB79BA197159AB4057F5349F41807763A"), // Traitor's Flames
            new Entry(-1156862687, Kind.SummonChosenLane, 3, 3, "9E351408EA749521FB0060357616A3820895ED283B6A8597A7BAD8D1DD18184B"), // Rising of Bones (Risen Horror 3/3 Guard)
        };
        private static readonly Dictionary<Card, PlayCardAbility> owned = new Dictionary<Card, PlayCardAbility>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static int ValueA(int id) { return Get(id).A; }
        public static int ValueB(int id) { return Get(id).B; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited Betray ID: " + id); return e; }
        private static Entry Find(int id) { foreach (Entry e in entries) if (e.Id == id) return e; return null; }

        // Rising of Bones creates the unique installed "Risen Horror" creature, checked against the text's 3/3.
        public static int RisenHorror(GameConfiguration config)
        {
            List<int> matches = new List<int>();
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards) if (c.Name == "Risen Horror" && !matches.Contains(c.ResourceId.Id)) matches.Add(c.ResourceId.Id);
            if (matches.Count != 1) throw new NotSupportedException("Risen Horror identity is not unique.");
            Card token = config.FindCardById(matches[0]);
            if (Stat(token, AttributeType.Attack) != 3 || Stat(token, AttributeType.MaxHealth) != 3) throw new NotSupportedException("Risen Horror stats differ from text.");
            return matches[0];
        }
        private static int Stat(Card card, AttributeType name)
        { IntAttribute a = card.EntityAttributes.Find(delegate(SPTESL.Attribute x) { return x.Name == name; }) as IntAttribute; return a == null ? 0 : a.Value; }

        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).

        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }


        private static List<Card> ValidateRules(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            List<Card> result = new List<Card>();
            int horror = RisenHorror(config);
            foreach (Entry e in entries)
            {
                Card card = config.FindCardById(e.Id);
                if (card == null || OriginalCardRules.Fingerprint(card) != e.Hash)
                    throw new NotSupportedException("Original Betray metadata differs: " + e.Id);
                PlayCardAbility ability;
                if (owned.TryGetValue(card, out ability))
                {
                    bool valid = (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0) && (card.Auras == null || card.Auras.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                        && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0))
                        && card.ActionPlayInformation != null && card.ActionPlayInformation.Count == 1 && card.ActionPlayInformation[0] == ability
                        && OriginalGraphDescriber.Ability(ability) == OriginalGraphDescriber.Ability(Create(e, horror));
                    if (!valid) throw new NotSupportedException("Owned original Betray graph changed or gained extra behavior: " + e.Id);
                }
                else OriginalCardRules.ValidateNoExtraRules(card);
                result.Add(card);
            }
            return result;
        }

        public static int[] Restore(GameConfiguration config)
        {
            List<Card> cards = Validate(config); // Atomic complete inventory preflight before mutations.
            int horror = RisenHorror(config);
            List<int> ids = new List<int>();
            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                if (!owned.ContainsKey(card))
                {
                    PlayCardAbility ability = Create(entries[i], horror);
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

        private static PlayCardAbility Create(Entry e, int horror)
        {
            EffectGroup group = new EffectGroup();
            PlayCardAbility ability = new PlayCardAbility { EffectGroup = group };
            switch (e.Kind)
            {
                case Kind.Drain:
                    PlayerEffectAction damage = new PlayerEffectAction { Action = new PlayerActions.DamageAvatarOfPlayer { Amount = new ActionValueData(e.A), AssetHash = -1 } };
                    damage.Selectors.Add(Role(PlayerRole.Opponent));
                    Effect first = new Effect(); first.PlayerActions.Add(damage); group.Effects.Add(first);
                    CardEffectAction heal = new CardEffectAction { HashId = -1, Action = new CardActions.Heal { HealthGain = new ActionValueData(e.B), FullHeal = false, HealToAVDValue = false } };
                    heal.Selectors.Add(Subtype(CardSubtype.Avatar)); heal.Selectors.Add(Board()); heal.Selectors.Add(Rel(true));
                    Effect second = new Effect(); second.CardActions.Add(heal); group.Effects.Add(second);
                    ability.PlayTargetingType = PlayTargetingType.NoTarget; break;
                case Kind.Debuff:
                    SPTESL.Enchantment minus = new SPTESL.Enchantment();
                    minus.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, e.A));
                    minus.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, e.B));
                    CardEffectAction debuff = new CardEffectAction { HashId = -1, Action = new CardActions.Debuff { Enchantment = minus } };
                    debuff.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
                    Effect effect = new Effect(); effect.CardActions.Add(debuff); group.Effects.Add(effect);
                    ability.PlayTargetingType = PlayTargetingType.RequiredTarget;
                    DecisionConditionalExpression creature = new DecisionConditionalExpression { DecisionType = DecisionType.Card };
                    creature.CardConditionalExpressions.Add(Subtype(CardSubtype.Creature)); creature.CardConditionalExpressions.Add(Board());
                    ability.TargetSelectors.Add(creature); break;
                case Kind.LaneEnemyDamage:
                    CardEffectAction burn = new CardEffectAction { HashId = -1, Action = new CardActions.Damage { HealthLoss = new ActionValueData(e.A), DamageSource = CardRole.Self } };
                    burn.Selectors.Add(Subtype(CardSubtype.Creature)); burn.Selectors.Add(Board());
                    burn.Selectors.Add(new CardConditionals.InTargetedLane { Lane = CardConditionals.InTargetedLane.LaneRequirement.SameLane, Owner = CardConditionals.InTargetedLane.OwnerRequirement.SamePlayer });
                    Effect lane = new Effect(); lane.CardActions.Add(burn); group.Effects.Add(lane);
                    ability.PlayTargetingType = PlayTargetingType.RequiredTarget;
                    ability.TargetSelectors.Add(LaneChoice(false)); break;
                case Kind.SummonChosenLane:
                    PlayerEffectAction create = new PlayerEffectAction { Action = new PlayerActions.CreateToken { CardReference = new EditorCardReference(horror), CreateInZoneType = GameZoneType.Board,
                        NumTokens = new ActionValueData(1), InsertPosition = new InsertPosition { Type = InsertPosition.LaneChoiceType.Selected, InsertionType = CardInsertionType.Bottom } } };
                    create.Selectors.Add(Role(PlayerRole.Self));
                    Effect summon = new Effect(); summon.PlayerActions.Add(create); group.Effects.Add(summon);
                    ability.PlayTargetingType = PlayTargetingType.RequiredTarget;
                    ability.TargetSelectors.Add(LaneChoice(true)); break;
            }
            return ability;
        }
        private static DecisionConditionalExpression LaneChoice(bool friendly)
        {
            DecisionConditionalExpression lane = new DecisionConditionalExpression { DecisionType = DecisionType.Lane };
            lane.LaneConditionalExpressions.Add(new LaneConditionals.LaneOwner { OwnedBy = friendly ? LaneConditionals.LaneOwner.RelationshipType.Friends : LaneConditionals.LaneOwner.RelationshipType.Enemies, PlayerRole = PlayerRole.Self });
            return lane;
        }
        private static PlayerConditionals.PlayerRoleType Role(PlayerRole role) { return new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = role }; }
        private static CardConditionals.Subtype Subtype(CardSubtype s) { return new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = s }; }
        private static CardConditionals.Zone Board() { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }
        private static CardConditionals.Relationship Rel(bool friends) { return new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = friends ? CardConditionals.Relationship.RelationshipType.Friends : CardConditionals.Relationship.RelationshipType.Enemies }; }
    }
}
