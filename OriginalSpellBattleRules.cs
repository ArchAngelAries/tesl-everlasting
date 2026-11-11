using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card wrappers from exact installed text, not recovered server graphs.
    // Effects execute in intact original CardActions.Debuff/Ward/Silence/Heal/Buff/GiveKeyword/Fight
    // and PlayerActions.DamageAvatarOfPlayer. Creature Summon targets use the play-time
    // ActionPlayInformation route documented in ORIGINAL-TARGETED-SUMMON-RECOVERY-NOTES.md.
    public static class OriginalSpellBattleRules
    {
        public enum Kind { DebuffSpell, WardSpell, SilenceSpell, HealSpell, DrainSpell, BuffGuardSummon, BattleSummon }
        private sealed class Entry
        {
            public int Id, A, B; public Kind Kind; public string Hash;
            public Entry(int id, Kind kind, int a, int b, string hash) { Id = id; Kind = kind; A = a; B = b; Hash = hash; }
        }
        // DebuffSpell A/B = attack/health change; HealSpell A = health; DrainSpell A = damage, B = health; BuffGuardSummon A/B = +attack/+health.
        private static readonly Entry[] entries = new Entry[] {
            new Entry(627783150, Kind.DebuffSpell, -1, -1, "ED123510BA7309901A4A13AD1A038C3564977D60F7CD56F68A9E5D245A5304A5"), // Curse
            new Entry(-632269135, Kind.DebuffSpell, -2, -2, "9F2350242CA3CE3D9B673A59F767EC542102816B8599CFADA3137F47D45EC80B"), // Malefic Wreath
            new Entry(171706845, Kind.WardSpell, 0, 0, "258990FA7AA2EBDAAD489B39363BE47A0A34D588C2D19000C57AED7F0E44EED6"), // Lesser Ward
            new Entry(-520695966, Kind.WardSpell, 0, 0, "CEF98FEBBCBB5D6318F8DC0200542C007D33423ECA8CBB0DDF21D6A1DC34F4DE"), // Lesser Ward
            new Entry(1009056840, Kind.SilenceSpell, 0, 0, "C93E41BBAD84B08B91473D6D606A0A0EDC6843ECAD9AC0033E7689AF2C32D4E2"), // Suppress
            new Entry(-1897475450, Kind.SilenceSpell, 0, 0, "AD69A4A9ED54F71B1DC1DC7DAF678258AAF8B3B90890D9FD1A8649E7ED070EC6"), // Suppress
            new Entry(599351067, Kind.SilenceSpell, 0, 0, "14E73F5F3614109378F0061F53A4F7CBB7B54EB30E7A41FBD7CBACE0A2254294"), // Hush (Prophecy)
            new Entry(1898861704, Kind.HealSpell, 5, 0, "6A61EBF4E73980CF69DDBAF7D1D8E246F00524222F3FD6269AEBA20CB53EF37B"), // Apprentice's Potion
            new Entry(-30507580, Kind.HealSpell, 5, 0, "43460CAE47360E36955120D611F401C69FCB24D7EF40E72D84F2A2527E2B9D1C"), // Healing Potion (Prophecy)
            new Entry(-1879015996, Kind.DrainSpell, 5, 5, "4D712CA6A04D4E2C095A9DB8B9ED8BB4EC978BD1E3A431F5D501E20F55AC29F6"), // Drain Life
            new Entry(1468435563, Kind.DrainSpell, 3, 3, "A880C3BA304BF94308DF8C9AB9CA477F6B0DD5C3DB0768251300A791942407AD"), // Thievery
            new Entry(207833797, Kind.BuffGuardSummon, 2, 2, "BEA8979A73E5589C8731E8BD558B1DAB0F550F86C5602B4B4EEBB94285B9483E"), // Loyal Housecarl (Prophecy)
            new Entry(-1769639680, Kind.BuffGuardSummon, 2, 2, "C5347F7559E2E526042D7555113A1C7F0905277F7F2D527D1C6B070AB6142770"), // Loyal Housecarl (Prophecy)
            new Entry(-1800065169, Kind.BuffGuardSummon, 0, 2, "4ED622C10516095CC900B5480151E3D9DFC5ECE33728A11C0C811EA273CA61D6"), // Skinned Hound
            new Entry(-1314585700, Kind.BattleSummon, 0, 0, "28713E337CAC108B8599F0EBDDAA9571239E45CF2CB695F4E0E71C0EFA945CE1"), // Ashlander Zealot
            new Entry(-117580191, Kind.BattleSummon, 0, 0, "C1A9E177086006E86B34845901AAD370BEA66394A89CD51045BCD54ADB86EA62"), // Cliff Hunter (Prophecy)
            new Entry(824194903, Kind.BattleSummon, 0, 0, "2CC84C747C7821D32BFDD8D65B306083405D675CCCFF41C9DBB91F861DC0131B"), // Skyborn Dragon
        };
        private static readonly Dictionary<Card, PlayCardAbility> owned = new Dictionary<Card, PlayCardAbility>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static int ValueA(int id) { return Get(id).A; }
        public static int ValueB(int id) { return Get(id).B; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited original spell/battle ID: " + id); return e; }
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
                    throw new NotSupportedException("Original spell/battle metadata differs: " + e.Id);
                PlayCardAbility ability;
                if (owned.TryGetValue(card, out ability))
                {
                    bool empty = (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0) && (card.Auras == null || card.Auras.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                        && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0));
                    if (!empty || card.ActionPlayInformation == null || card.ActionPlayInformation.Count != 1 || card.ActionPlayInformation[0] != ability
                        || OriginalGraphDescriber.Ability(ability) != OriginalGraphDescriber.Ability(Create(e)))
                        throw new NotSupportedException("Owned original spell/battle graph changed or gained extra behavior: " + e.Id);
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
            EffectGroup group = new EffectGroup();
            PlayCardAbility ability = new PlayCardAbility { EffectGroup = group };
            switch (e.Kind)
            {
                case Kind.DebuffSpell:
                    group.Effects.Add(Single(Target(new CardActions.Debuff { Enchantment = Stats(e.A, e.B) })));
                    Targeted(ability, PlayTargetingType.RequiredTarget, false); break;
                case Kind.WardSpell:
                    group.Effects.Add(Single(Target(new CardActions.Ward())));
                    Targeted(ability, PlayTargetingType.RequiredTarget, false); break;
                case Kind.SilenceSpell:
                    group.Effects.Add(Single(Target(new CardActions.Silence())));
                    Targeted(ability, PlayTargetingType.RequiredTarget, false); break;
                case Kind.HealSpell:
                    group.Effects.Add(Single(OwnAvatarHeal(e.A)));
                    ability.PlayTargetingType = PlayTargetingType.NoTarget; break;
                case Kind.DrainSpell:
                    PlayerEffectAction damage = new PlayerEffectAction { Action = new PlayerActions.DamageAvatarOfPlayer { Amount = new ActionValueData(e.A), AssetHash = -1 } };
                    damage.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Opponent });
                    Effect first = new Effect(); first.PlayerActions.Add(damage); group.Effects.Add(first);
                    group.Effects.Add(Single(OwnAvatarHeal(e.B)));
                    ability.PlayTargetingType = PlayTargetingType.NoTarget; break;
                case Kind.BuffGuardSummon:
                    Effect buff = new Effect();
                    buff.CardActions.Add(Target(new CardActions.Buff { Enchantment = Stats(e.A, e.B) }));
                    buff.CardActions.Add(Target(new CardActions.GiveKeyword { KeywordAttrib = AttributeType.Taunt, RandomKeyword = false }));
                    group.Effects.Add(buff);
                    Targeted(ability, PlayTargetingType.OptionalTarget, false); break;
                case Kind.BattleSummon:
                    CardActions.Fight fight = new CardActions.Fight();
                    fight.InciterSelectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self });
                    group.Effects.Add(Single(Target(fight)));
                    Targeted(ability, PlayTargetingType.OptionalTarget, true); break;
            }
            return ability;
        }
        private static Effect Single(CardEffectAction action) { Effect effect = new Effect(); effect.CardActions.Add(action); return effect; }
        private static CardEffectAction Target(ICardAction native)
        {
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = native };
            action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
            return action;
        }
        private static CardEffectAction OwnAvatarHeal(int amount)
        {
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = new CardActions.Heal { HealthGain = new ActionValueData(amount), FullHeal = false, HealToAVDValue = false } };
            action.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Avatar });
            action.Selectors.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } });
            action.Selectors.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends });
            return action;
        }
        private static void Targeted(PlayCardAbility ability, PlayTargetingType type, bool enemyOnly)
        {
            ability.PlayTargetingType = type;
            DecisionConditionalExpression selector = new DecisionConditionalExpression { DecisionType = DecisionType.Card };
            selector.CardConditionalExpressions.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
            selector.CardConditionalExpressions.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } });
            if (enemyOnly) selector.CardConditionalExpressions.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Enemies });
            ability.TargetSelectors.Add(selector);
        }
        private static SPTESL.Enchantment Stats(int attack, int health)
        {
            SPTESL.Enchantment enchantment = new SPTESL.Enchantment();
            if (attack != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, attack));
            if (health != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, health));
            return enchantment;
        }
    }
}
