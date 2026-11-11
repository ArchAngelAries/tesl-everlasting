using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent ActivatedAbility wrappers for exact installed "Activate:" support text, not recovered server
    // graphs. Activation runs through the restored DecisionRestoration.ProcessActivate (native ExecuteOnActivateCardEffects,
    // native use counters). Original UsesPerTurn/TotalUsesAllowed/UnlimitedUses/Indestructible attributes are untouched;
    // native ServerCard.ShouldBeDiscarded removes a support once its TotalUsesAllowed is consumed.
    public static class OriginalActivateSupportRules
    {
        public enum Kind { Buff, Ward, Cover, GuardFriendly, BuffBreakthrough, OpponentDamage, Heal }
        private sealed class Entry
        {
            public int Id, A, B; public Kind Kind; public string Hash;
            public Entry(int id, Kind kind, int a, int b, string hash) { Id = id; Kind = kind; A = a; B = b; Hash = hash; }
        }
        private static readonly Entry[] entries = new Entry[] {
            new Entry(-1574884301, Kind.Buff, 1, 1, "F361C2E20EB017AE73A6AE0709F159BB2050F8F1D3754C218F48E566228779C2"), // Elixir of Conflict
            new Entry(1705848025, Kind.Buff, 0, 1, "80AF6A293D944A6DFEBA4DE8FF0D8189D72D0D37F2B4753D604A3F9C8A04CB2B"), // Elixir of Vigor
            new Entry(539510398, Kind.Ward, 0, 0, "53FED005B50306E147E3E704DA78258B5B8F5F60E568522BB7308BAE21557E78"), // Elixir of Deflection
            new Entry(1839123663, Kind.Ward, 0, 0, "46E7DCDFB735ABDA9116EEF98A0333E4AAAE4607BBA904F04987313118B3F6E2"), // Wraithguard II (UnlimitedUses, Indestructible)
            new Entry(-387279246, Kind.Cover, 0, 0, "1AC5B45C357ED0D9DC7854B643407CA876D9337392DC6FE6A2CC15DF313ADEBC"), // Elixir of Light Feet
            new Entry(-646559647, Kind.GuardFriendly, 0, 0, "6D930204DAFE222451DA8E6E5DAD88451F695796980FAD8C69BBF61019064524"), // Elixir of the Defender
            new Entry(-475716370, Kind.BuffBreakthrough, 2, 0, "1669A8A6DAC9039B678DCFEC0373AA7AFC8C158EC45BE9D0C41FC7274D2573C2"), // Skirmisher's Elixir
            new Entry(-1914450579, Kind.BuffBreakthrough, 4, 4, "68BE6EB1320397115E079A02EEB4AE36595EA1CF669E35C890C40C20182C5F3A"), // Volendrung
            new Entry(472460151, Kind.OpponentDamage, 1, 0, "713B69D0AB2688D1C0376104185A6A327615277B7F178DAB35E32ABFBE125748"), // Ravaging Elixir
            new Entry(1469820870, Kind.Heal, 2, 0, "5AF52DE808C0755EE1D8E16D204E817A697158513CB85A452973EC18BBAA12CC"), // Elixir of Vitality
        };
        private static readonly Dictionary<Card, ActivatedAbility> owned = new Dictionary<Card, ActivatedAbility>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static int ValueA(int id) { return Get(id).A; }
        public static int ValueB(int id) { return Get(id).B; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited Activate support ID: " + id); return e; }
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
                    throw new NotSupportedException("Original Activate support metadata differs: " + e.Id);
                ActivatedAbility ability;
                if (owned.TryGetValue(card, out ability))
                {
                    bool valid = (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0) && (card.Auras == null || card.Auras.Count == 0)
                        && (card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && card.ActivatedAbility == ability && OriginalGraphDescriber.Activated(ability) == OriginalGraphDescriber.Activated(Create(e));
                    if (!valid) throw new NotSupportedException("Owned original Activate graph changed or gained extra behavior: " + e.Id);
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
                    ActivatedAbility ability = Create(entries[i]);
                    card.ActivatedAbility = ability;
                    owned.Add(card, ability);
                }
                if (card.TriggeredEffects == null) card.TriggeredEffects = new List<TriggeredEffect>();
                if (card.Auras == null) card.Auras = new List<Aura>();
                if (card.ActionPlayInformation == null) card.ActionPlayInformation = new List<PlayCardAbility>();
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        private static ActivatedAbility Create(Entry e)
        {
            ActivatedAbility ability = new ActivatedAbility();
            AbilityGroup group = new AbilityGroup();
            Effect effect = new Effect();
            bool targeted = e.Kind != Kind.OpponentDamage && e.Kind != Kind.Heal;
            switch (e.Kind)
            {
                case Kind.Buff: effect.CardActions.Add(Target(new CardActions.Buff { Enchantment = Stats(e.A, e.B) })); break;
                case Kind.Ward: effect.CardActions.Add(Target(new CardActions.Ward())); break;
                case Kind.Cover: effect.CardActions.Add(Target(new CardActions.Cover { MultipleTurns = false })); break;
                case Kind.GuardFriendly: effect.CardActions.Add(Target(new CardActions.GiveKeyword { KeywordAttrib = AttributeType.Taunt, RandomKeyword = false })); break;
                case Kind.BuffBreakthrough:
                    effect.CardActions.Add(Target(new CardActions.Buff { Enchantment = Stats(e.A, e.B) }));
                    effect.CardActions.Add(Target(new CardActions.GiveKeyword { KeywordAttrib = AttributeType.Breakthrough, RandomKeyword = false })); break;
                case Kind.OpponentDamage:
                    PlayerEffectAction damage = new PlayerEffectAction { Action = new PlayerActions.DamageAvatarOfPlayer { Amount = new ActionValueData(e.A), AssetHash = -1 } };
                    damage.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Opponent });
                    effect.PlayerActions.Add(damage); break;
                case Kind.Heal:
                    CardEffectAction heal = new CardEffectAction { HashId = -1, Action = new CardActions.Heal { HealthGain = new ActionValueData(e.A), FullHeal = false, HealToAVDValue = false } };
                    heal.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Avatar });
                    heal.Selectors.Add(Board()); heal.Selectors.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends });
                    effect.CardActions.Add(heal); break;
            }
            group.EffectGroup.Effects.Add(effect);
            ability.AbilityGroups.Add(group);
            ability.TargetingType = targeted ? PlayTargetingType.RequiredTarget : PlayTargetingType.NoTarget;
            if (targeted)
            {
                DecisionConditionalExpression selector = new DecisionConditionalExpression { DecisionType = DecisionType.Card };
                selector.CardConditionalExpressions.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
                selector.CardConditionalExpressions.Add(Board());
                if (e.Kind == Kind.GuardFriendly)
                    selector.CardConditionalExpressions.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends });
                ability.TargetSelectors.Add(selector);
            }
            return ability;
        }
        private static CardEffectAction Target(ICardAction native)
        {
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = native };
            action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
            return action;
        }
        private static SPTESL.Enchantment Stats(int attack, int health)
        {
            SPTESL.Enchantment enchantment = new SPTESL.Enchantment();
            if (attack != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, attack));
            if (health != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, health));
            return enchantment;
        }
        private static CardConditionals.Zone Board()
        { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }
    }
}
