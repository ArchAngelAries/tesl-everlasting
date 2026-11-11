using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card wrappers from exact installed text, not recovered server graphs.
    // Max magicka: the native turn-start routine and ServerCard.AddMaxManaBuff represent max magicka as an Add
    // MaxMana enchantment on the player's board avatar, and current magicka is only set from MaxMana at turn start.
    // Native CardActions.Buff on the avatar therefore gains max magicka without refilling current magicka, and
    // ServerCard.AddEnchantment natively applies DoubleMaxManaGained. Conditional draws use native
    // PlayerRequirements (CountOfCards / AttributeValue) on the Summon effect.
    public static class OriginalMagickaConditionRules
    {
        public enum Kind { MaxMagickaSummon, EachPlayerMaxMagicka, AttributePairDraw, OpponentRuneDraw }
        private sealed class Entry
        {
            public int Id, A; public Kind Kind; public CardSubtype Attribute; public string Hash;
            public Entry(int id, Kind kind, int a, CardSubtype attribute, string hash) { Id = id; Kind = kind; A = a; Attribute = attribute; Hash = hash; }
        }
        private static readonly Entry[] entries = new Entry[] {
            new Entry(1262508063, Kind.MaxMagickaSummon, 1, CardSubtype.None, "B98FF9C50DCC4A87E9CDBF460045A7458B343FD622EB254F850758BFB98264FB"), // Tree Minder (Guard)
            new Entry(1818589334, Kind.MaxMagickaSummon, 1, CardSubtype.None, "C26037E2970D9A95A0AC7B7570A1F915388C302C91193E369AD8B04F98E021DD"), // Tree Minder (Guard)
            new Entry(1702639680, Kind.MaxMagickaSummon, 1, CardSubtype.None, "0A7AACADFEBA41C61C9F6C62B2271816E59DF266622C4440C7D985FF850BD374"), // Spine of Eldersblood
            new Entry(-1641639169, Kind.EachPlayerMaxMagicka, 2, CardSubtype.None, "14F6B136A196B5CE313F8D63ACAA16D02E61F5C4DB2B524CA73F59E601CDC4A9"), // Jyggalag's Incursion (Prophecy)
            // Installed text carries <img=TextIcons/icon{Attribute}/> between "other" and "creatures".
            new Entry(458738242, Kind.AttributePairDraw, 2, CardSubtype.Intelligence, "186EEF26CBE6D2CBD865C39C6006AAEAFF19EA9C78824EC51BD51E0157CE8618"), // Court Wizard
            new Entry(1113127163, Kind.AttributePairDraw, 2, CardSubtype.Willpower, "03F1B423334A3BFFA8C45C634370E74F00245EF1FF92071F9D9D841ED746EA66"), // Priest of the Eight
            new Entry(-655836816, Kind.AttributePairDraw, 2, CardSubtype.Agility, "BACFF38A80B761A1D731CBF9940A505BCA5AAB2A568283BA57D03ABFE173DE29"), // Sightless Skulk
            new Entry(-229481561, Kind.AttributePairDraw, 2, CardSubtype.Endurance, "C80677A5A973C5FB0798C9A33910D5CC561359883B2C8CBF8298A4CFBD8AB393"), // Stonehill Mammoth
            new Entry(-1299948782, Kind.AttributePairDraw, 2, CardSubtype.Strength, "5B89CBACE1E37D8959BB7920C8F818EC2401EB943F398AF766C200314B72C729"), // Stronghold Patrol
            new Entry(-212193127, Kind.OpponentRuneDraw, 0, CardSubtype.None, "5E364EE0F9F94916443CB0342122793BBF8784C950B61FE5CE7AA2C1E02252A1"), // Eastmarch Crusader
            new Entry(2132131160, Kind.OpponentRuneDraw, 0, CardSubtype.None, "B1093D8A2C266C093AE896DA78CFDE938887C68B5DCF726BA92D7809E750174B"), // Eastmarch Crusader
        };
        private static readonly Dictionary<Card, object> owned = new Dictionary<Card, object>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static int Value(int id) { return Get(id).A; }
        public static CardSubtype AttributeOf(int id) { return Get(id).Attribute; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited original magicka/condition ID: " + id); return e; }
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
                    throw new NotSupportedException("Original magicka/condition metadata differs: " + e.Id);
                object graph;
                if (owned.TryGetValue(card, out graph)) ValidateOwned(card, graph, e);
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
                Card card = cards[i]; Entry e = entries[i];
                if (!owned.ContainsKey(card))
                {
                    if (e.Kind == Kind.EachPlayerMaxMagicka)
                    {
                        PlayCardAbility ability = CreateAbility(e);
                        card.ActionPlayInformation = new List<PlayCardAbility> { ability };
                        card.TriggeredEffects = new List<TriggeredEffect>();
                        owned.Add(card, ability);
                    }
                    else
                    {
                        TriggeredEffect trigger = CreateTrigger(e);
                        card.TriggeredEffects = new List<TriggeredEffect> { trigger };
                        card.ActionPlayInformation = new List<PlayCardAbility>();
                        owned.Add(card, trigger);
                    }
                }
                if (card.Auras == null) card.Auras = new List<Aura>();
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        private static void ValidateOwned(Card card, object graph, Entry e)
        {
            bool shared = (card.Auras == null || card.Auras.Count == 0)
                && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0));
            bool valid;
            if (e.Kind == Kind.EachPlayerMaxMagicka)
            {
                PlayCardAbility ability = graph as PlayCardAbility;
                valid = ability != null && shared && (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0)
                    && card.ActionPlayInformation != null && card.ActionPlayInformation.Count == 1 && card.ActionPlayInformation[0] == ability
                    && OriginalGraphDescriber.Ability(ability) == OriginalGraphDescriber.Ability(CreateAbility(e));
            }
            else
            {
                TriggeredEffect trigger = graph as TriggeredEffect;
                valid = trigger != null && shared && (card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                    && card.TriggeredEffects != null && card.TriggeredEffects.Count == 1 && card.TriggeredEffects[0] == trigger
                    && OriginalGraphDescriber.Trigger(trigger) == OriginalGraphDescriber.Trigger(CreateTrigger(e));
            }
            if (!valid) throw new NotSupportedException("Owned original magicka/condition graph changed or gained extra behavior: " + e.Id);
        }

        private static PlayCardAbility CreateAbility(Entry e)
        {
            EffectGroup group = new EffectGroup();
            group.Effects.Add(MaxMagicka(e.A, false));
            return new PlayCardAbility { EffectGroup = group, PlayTargetingType = PlayTargetingType.NoTarget };
        }

        private static TriggeredEffect CreateTrigger(Entry e)
        {
            TriggeredEffect trigger = new TriggeredEffect { TriggerType = EffectTriggerType.Summon, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            Effect effect;
            if (e.Kind == Kind.MaxMagickaSummon) effect = MaxMagicka(e.A, true);
            else
            {
                effect = new Effect();
                effect.PlayerActions.Add(Draw());
                if (e.Kind == Kind.AttributePairDraw)
                {
                    PlayerRequirements.PlayerCountOfCardsRequirement count = new PlayerRequirements.PlayerCountOfCardsRequirement();
                    count.LhsPlayer = PlayerRole.Self;
                    count.Conditional.Count_Is = PlayerConditionals.CountOfCards.ComparisonType.GreaterThanOrEqualTo;
                    count.Conditional.Value = e.A;
                    count.Conditional.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
                    count.Conditional.Selectors.Add(Board());
                    count.Conditional.Selectors.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends });
                    count.Conditional.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = e.Attribute });
                    count.Conditional.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = CardRole.Self });
                    effect.ExecutePlayerRequirements.Add(count);
                }
                else
                {
                    // Native NumRunesLost rises on rune break and falls on native rune restoration (HealRunes),
                    // matching installed "has a destroyed rune". UESP words it "destroyed this game".
                    PlayerRequirements.PlayerAttributeValueRequirement runes = new PlayerRequirements.PlayerAttributeValueRequirement();
                    runes.LhsPlayer = PlayerRole.Opponent;
                    runes.Conditional.AttributeName = AttributeType.NumRunesLost;
                    runes.Conditional.Is = PlayerConditionals.AttributeValue.ComparisonType.GreaterThan;
                    runes.Conditional.Value = 0;
                    effect.ExecutePlayerRequirements.Add(runes);
                }
            }
            trigger.EffectGroup.Effects.Add(effect);
            return trigger;
        }

        private static Effect MaxMagicka(int amount, bool ownOnly)
        {
            SPTESL.Enchantment enchantment = new SPTESL.Enchantment();
            enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxMana, amount));
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = new CardActions.Buff { Enchantment = enchantment } };
            action.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Avatar });
            action.Selectors.Add(Board());
            if (ownOnly) action.Selectors.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends });
            Effect effect = new Effect(); effect.CardActions.Add(action);
            return effect;
        }
        private static PlayerEffectAction Draw()
        {
            PlayerEffectAction draw = new PlayerEffectAction { Action = new PlayerActions.DrawCards { NumCards = new ActionValueData(1), DrawFrom = PlayerActions.DrawCards.DrawDeck.Self,
                OperationType = ServerCardCatalog.MoveOperationType.DefaultDraw, StopAtHandLimit = false, IgnoreDeckingRule = false } };
            draw.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Self });
            return draw;
        }
        private static CardConditionals.Zone Board()
        { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }
    }
}
