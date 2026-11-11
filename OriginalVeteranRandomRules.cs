using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card wrappers from exact installed text, not recovered server graphs.
    // Veteran: native EffectTriggerType.Veteran is raised by DecisionRestoration.ProcessAttack after a creature's first
    // resolved attack while it survives (reconstructed bookkeeping; UESP Legends:Veteran wording).
    // Random targets: native CardEffectAction.SelectorSort { Method = Random, NumToReturn = N } shuffles the native
    // selector result with the game RNG. UESP: "enemies" include the opponent's creatures and the opponent.
    public static class OriginalVeteranRandomRules
    {
        public enum Kind { VeteranSelfBuff, VeteranSelfGuard, RandomEnemyDamage, RandomFriendlyBuff, RandomEnemyShackle, RandomEnemyDestroy }
        private sealed class Entry
        {
            public int Id, A, B; public EffectTriggerType Trigger; public Kind Kind; public string Hash;
            public Entry(int id, EffectTriggerType trigger, Kind kind, int a, int b, string hash) { Id = id; Trigger = trigger; Kind = kind; A = a; B = b; Hash = hash; }
        }
        // Trigger None = action card (play ability). For random kinds B is the number of random targets.
        private const EffectTriggerType Veteran = EffectTriggerType.Veteran, LastGasp = EffectTriggerType.LastGaspTrigger, Summon = EffectTriggerType.Summon, Play = EffectTriggerType.ActionEffect;
        private static readonly Entry[] entries = new Entry[] {
            new Entry(670172603, Veteran, Kind.VeteranSelfBuff, 4, 4, "98D81A6EA652AFB430483015922C431269E9E99511F3BDE7C81880DF5A3D6F52"), // Black Marsh Prodigy
            new Entry(-532872243, Veteran, Kind.VeteranSelfBuff, 2, 0, "C36AF0FC69B868C46AC6EDC67F428AECCECCAA58E458EDF6D279A62833AC3538"), // Dunmer Tyro (Charge)
            new Entry(1223410755, Veteran, Kind.VeteranSelfBuff, 1, 1, "8E88C5215932FB1B594E6203BDA517D21FF21C0FF7D73361F09D23FF59543A4C"), // Great Moot Squire
            new Entry(754975007, Veteran, Kind.VeteranSelfBuff, 3, 0, "342019EB3592302C42E4CBAC15ECFA29900489B04F7E6CBE93F9232B897A59DC"), // Windhelm Gatekeeper (Guard)
            new Entry(910643677, Veteran, Kind.VeteranSelfGuard, 0, 0, "CC94F45B9F2E1D5D71D49791FF30BB0C83AA923AB03DE3BD10E7A7763956BE71"), // Black Marsh Centurion
            new Entry(-938950495, LastGasp, Kind.RandomEnemyDamage, 3, 1, "FE2194739A254D4AD26A997498E82D7C5CA2F50AABF9D5D0B10D338939C9A539"), // Brutal Ashlander
            new Entry(1081115101, LastGasp, Kind.RandomEnemyDamage, 3, 1, "9310A6FAF7D2016E2615C11041C655C03DA3B7C4609794F373C44357D1855F0F"), // Brutal Ashlander
            new Entry(-822105628, LastGasp, Kind.RandomFriendlyBuff, 3, 1, "526EFE34B98AC82835DD3B533562C57E4BF4DAB5D50C7D3E339C9AB935F359B3"), // Haunting Spirit
            new Entry(-1614622271, LastGasp, Kind.RandomFriendlyBuff, 3, 1, "5AD7113D9D18ED729F3B51A322A9AB86B924CEC85FEC24B39CF33FB71434B6F7"), // Haunting Spirit
            new Entry(16891283, Summon, Kind.RandomEnemyDamage, 6, 1, "5A1A6F6207867B445AFB393C139CDF788836C55FDE40C3D0AC940AB1810B2E20"), // Alfe Fyr
            new Entry(1783048624, Summon, Kind.RandomEnemyDamage, 6, 1, "561072EFD45551E6725FC8E2BD433EDE353F272E7170115AA155FBF4CC43C2B6"), // Beyte Fyr
            new Entry(1307441161, Summon, Kind.RandomEnemyDamage, 6, 1, "1C0AB941A3CDA8E59D996EF8DC2376D0902D0C6A87817684F38BDABDE8A84AB8"), // Delte Fyr
            new Entry(-811885009, Summon, Kind.RandomEnemyDamage, 6, 1, "7A1940C9415AB586CB32FD361C2C1550B4F3637D322345D7FC8EBB3844A15D3B"), // Uupse Fyr
            new Entry(-1592446350, Summon, Kind.RandomEnemyShackle, 0, 1, "DC5FD033204CDFC46063C1F34FA11D37DBD7A35FE391E3CC296572C44375ADB5"), // Cavern Spinner
            new Entry(148278048, Play, Kind.RandomEnemyDestroy, 0, 3, "59239E7532AB6A6D993513DA83D4F590CCECEB17E2DA7DEB2E2DA473E91EC6D5"), // Power of Naarifin
        };
        private static readonly Dictionary<Card, object> owned = new Dictionary<Card, object>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static EffectTriggerType TriggerOf(int id) { return Get(id).Trigger; }
        public static int ValueA(int id) { return Get(id).A; }
        public static int ValueB(int id) { return Get(id).B; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited Veteran/random ID: " + id); return e; }
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
                    throw new NotSupportedException("Original Veteran/random metadata differs: " + e.Id);
                object graph;
                if (owned.TryGetValue(card, out graph))
                {
                    bool shared = (card.Auras == null || card.Auras.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                        && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0));
                    bool valid;
                    if (e.Trigger == Play)
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
                    if (!valid) throw new NotSupportedException("Owned original Veteran/random graph changed or gained extra behavior: " + e.Id);
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
                Card card = cards[i]; Entry e = entries[i];
                if (!owned.ContainsKey(card))
                {
                    if (e.Trigger == Play)
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

        private static PlayCardAbility CreateAbility(Entry e)
        {
            EffectGroup group = new EffectGroup(); group.Effects.Add(Body(e));
            return new PlayCardAbility { EffectGroup = group, PlayTargetingType = PlayTargetingType.NoTarget };
        }
        private static TriggeredEffect CreateTrigger(Entry e)
        {
            TriggeredEffect trigger = new TriggeredEffect { TriggerType = e.Trigger, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            trigger.EffectGroup.Effects.Add(Body(e));
            return trigger;
        }

        private static Effect Body(Entry e)
        {
            CardEffectAction action = new CardEffectAction { HashId = -1 };
            switch (e.Kind)
            {
                case Kind.VeteranSelfBuff:
                    action.Action = new CardActions.Buff { Enchantment = Stats(e.A, e.B) };
                    action.Selectors.Add(SelfRole()); break;
                case Kind.VeteranSelfGuard:
                    action.Action = new CardActions.GiveKeyword { KeywordAttrib = AttributeType.Taunt, RandomKeyword = false };
                    action.Selectors.Add(SelfRole()); break;
                case Kind.RandomEnemyDamage:
                    action.Action = new CardActions.Damage { HealthLoss = new ActionValueData(e.A), DamageSource = CardRole.Self };
                    CardConditionals.SubtypeCount types = new CardConditionals.SubtypeCount { Is = CardConditionals.SubtypeCount.ComparisonType.GreaterThanOrEqualTo, Value = 1 };
                    types.Subtypes.Add(CardSubtype.Creature); types.Subtypes.Add(CardSubtype.Avatar);
                    action.Selectors.Add(types); action.Selectors.Add(Board()); action.Selectors.Add(Relationship(false));
                    Random(action, e.B); break;
                case Kind.RandomFriendlyBuff:
                    action.Action = new CardActions.Buff { Enchantment = Stats(e.A, e.A) };
                    action.Selectors.Add(Creature()); action.Selectors.Add(Board()); action.Selectors.Add(Relationship(true));
                    Random(action, e.B); break;
                case Kind.RandomEnemyShackle:
                    action.Action = new CardActions.Freeze { PermanentFreeze = false, Type = EnchantmentUtilities.FreezeType.Normal };
                    action.Selectors.Add(Creature()); action.Selectors.Add(Board()); action.Selectors.Add(Relationship(false));
                    Random(action, e.B); break;
                case Kind.RandomEnemyDestroy:
                    action.Action = new CardActions.Destroy { Sacrifice = false };
                    action.Selectors.Add(Creature()); action.Selectors.Add(Board()); action.Selectors.Add(Relationship(false));
                    Random(action, e.B); break;
            }
            Effect effect = new Effect(); effect.CardActions.Add(action);
            return effect;
        }
        private static void Random(CardEffectAction action, int count)
        { action.SelectorSort = new CardListSort { Method = CardListSort.SortMethod.Random, NumToReturn = new ActionValueData(count) }; }
        private static SPTESL.Enchantment Stats(int attack, int health)
        {
            SPTESL.Enchantment enchantment = new SPTESL.Enchantment();
            if (attack != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, attack));
            if (health != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, health));
            return enchantment;
        }
        private static CardConditionals.CardRoleType SelfRole()
        { return new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self }; }
        private static CardConditionals.Subtype Creature()
        { return new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature }; }
        private static CardConditionals.Zone Board()
        { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }
        private static CardConditionals.Relationship Relationship(bool friends)
        { return new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = friends ? CardConditionals.Relationship.RelationshipType.Friends : CardConditionals.Relationship.RelationshipType.Enemies }; }
    }
}
