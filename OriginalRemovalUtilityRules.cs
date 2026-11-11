using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card wrappers from exact installed text, not recovered server graphs.
    // Effects execute in intact original CardActions.Damage/Destroy/Freeze/Cover and PlayerActions
    // DamageAvatarOfPlayer/DrawCards; target legality uses native AttributeValue(Attack/Wounded)/InSameLane conditionals.
    public static class OriginalRemovalUtilityRules
    {
        public enum Kind { DamageCreatureSpell, ShackleSpell, OpponentDamageDraw, DestroyLowPowerSpell, DestroyWoundedEnemySpell, DestroyWoundedSummon, CoverSummon, LastGaspDraw, LaneDamageSummon }
        private sealed class Entry
        {
            public int Id, A; public Kind Kind; public string Hash;
            public Entry(int id, Kind kind, int a, string hash) { Id = id; Kind = kind; A = a; Hash = hash; }
        }
        private static readonly Entry[] entries = new Entry[] {
            new Entry(1055804545, Kind.DamageCreatureSpell, 10, "7FDD30C90EBC0BDDAD754774A129641DB74CD775890B5FB7567C795CCE5A2811"), // Rage of Naarifin
            new Entry(-1094245595, Kind.DamageCreatureSpell, 2, "AE104CBC91A374FD1509D2AF6669987FCC7E0B2A58BF2ECA73CE67DD5D0D9665"), // Burning Touch (Prophecy)
            new Entry(1640425267, Kind.ShackleSpell, 0, "5AE85CE5C72B43214A6DAB2F9F02C3DE4F34691A06A4242A57AAF5DB4DCED899"), // Web
            new Entry(1405826749, Kind.ShackleSpell, 0, "F08D7F48EBA36D9D9D058C85B3B10C38BFD3D195A999FF7B143B7D35A22ED84A"), // Paralyze
            new Entry(-1912323229, Kind.OpponentDamageDraw, 5, "60A33B68FA5B5F8C8CC04FF7363B34FB542495EE9EDA412D13F4BD04080B72F2"), // Crushing Grasp
            new Entry(-37005376, Kind.DestroyLowPowerSpell, 2, "B7835192A0D68000849A3769E7E1BE8FE0FC2207C346F2B53FD3BC127E3D4680"), // Execute
            new Entry(1256461915, Kind.DestroyLowPowerSpell, 2, "407A9ACD17137868C147B2883CCD8EFBA4F024E2014BC513F5DB432BA3225107"), // Execute
            new Entry(-1991073211, Kind.DestroyWoundedEnemySpell, 0, "EFECAE4FB995E9EF400CE8DBDBDC6967F0B84957E71D7A0D78DEED63E65E5A96"), // Finish Off
            new Entry(-474889299, Kind.DestroyWoundedEnemySpell, 0, "1268F521FD63976FBBAD389E2DD45DB54F88CAA6182DBC971545873AF19642EF"), // Finish Off
            new Entry(-663505315, Kind.DestroyWoundedSummon, 0, "63DCCB168EE5759CBB613859931E57B65B52ECDD25E76C62D30D67849DAB1809"), // Leaflurker
            new Entry(-427132516, Kind.DestroyWoundedSummon, 0, "A630AAF76E69F9E63F085AA5ACF163CBD5DB4D7634E245EC633317C6C5A109C6"), // Leaflurker
            new Entry(-1248991397, Kind.CoverSummon, 0, "BC1F31CE0AD7D926C731BCEA3A935634EE6CEDD4BA0690E65265F469E6EF6D1A"), // Back-Alley Footpad
            new Entry(496581115, Kind.CoverSummon, 0, "060BD2550EEF38B69F5FCC7000887E68A74247F8EA98E2DA987B957E15140171"), // Gloomlurker (Prophecy)
            new Entry(416064061, Kind.LastGaspDraw, 1, "ED669AAD891968396FEC8FD6E01C83A64EAA4C3CB0556955791F74DEB347A458"), // Varanis Courier (Guard)
            new Entry(1483054902, Kind.LastGaspDraw, 1, "7A84E2C0C7258FDD733D6726C3DE10C912D8B8834E2CDFACC50CDF8B177689FA"), // Varanis Courier (Guard)
            new Entry(-1152867558, Kind.LaneDamageSummon, 1, "E6F533C450A135999DC2257B91195E106E37F965E1D9F67E6474FC5F8A7E5E1F"), // Grahtwood Ambusher (Prophecy)
            new Entry(888500878, Kind.LaneDamageSummon, 1, "7836DE6B32B4359CC868E13AA5EDE251DC14721560DFB21879A9774601941C58"), // Tiny Dragon
        };
        private static readonly Dictionary<Card, object> owned = new Dictionary<Card, object>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static int Value(int id) { return Get(id).A; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited original removal/utility ID: " + id); return e; }
        private static Entry Find(int id) { foreach (Entry e in entries) if (e.Id == id) return e; return null; }
        private static bool IsTrigger(Kind k) { return k == Kind.LastGaspDraw || k == Kind.LaneDamageSummon; }

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
                    throw new NotSupportedException("Original removal/utility metadata differs: " + e.Id);
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
                    if (IsTrigger(e.Kind))
                    {
                        TriggeredEffect trigger = CreateTrigger(e);
                        card.TriggeredEffects = new List<TriggeredEffect> { trigger };
                        card.ActionPlayInformation = new List<PlayCardAbility>();
                        owned.Add(card, trigger);
                    }
                    else
                    {
                        PlayCardAbility ability = CreateAbility(e);
                        card.ActionPlayInformation = new List<PlayCardAbility> { ability };
                        card.TriggeredEffects = new List<TriggeredEffect>();
                        owned.Add(card, ability);
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
            if (IsTrigger(e.Kind))
            {
                TriggeredEffect trigger = graph as TriggeredEffect;
                valid = trigger != null && shared && (card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                    && card.TriggeredEffects != null && card.TriggeredEffects.Count == 1 && card.TriggeredEffects[0] == trigger
                    && OriginalGraphDescriber.Trigger(trigger) == OriginalGraphDescriber.Trigger(CreateTrigger(e));
            }
            else
            {
                PlayCardAbility ability = graph as PlayCardAbility;
                valid = ability != null && shared && (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0)
                    && card.ActionPlayInformation != null && card.ActionPlayInformation.Count == 1 && card.ActionPlayInformation[0] == ability
                    && OriginalGraphDescriber.Ability(ability) == OriginalGraphDescriber.Ability(CreateAbility(e));
            }
            if (!valid) throw new NotSupportedException("Owned original removal/utility graph changed or gained extra behavior: " + e.Id);
        }

        private static PlayCardAbility CreateAbility(Entry e)
        {
            EffectGroup group = new EffectGroup();
            PlayCardAbility ability = new PlayCardAbility { EffectGroup = group, PlayTargetingType = PlayTargetingType.RequiredTarget };
            DecisionConditionalExpression selector = new DecisionConditionalExpression { DecisionType = DecisionType.Card };
            selector.CardConditionalExpressions.Add(Creature()); selector.CardConditionalExpressions.Add(Board());
            ICardAction native;
            switch (e.Kind)
            {
                case Kind.DamageCreatureSpell:
                    native = new CardActions.Damage { HealthLoss = new ActionValueData(e.A), DamageSource = CardRole.Self }; break;
                case Kind.ShackleSpell:
                    selector.CardConditionalExpressions.Add(Enemies());
                    native = new CardActions.Freeze { PermanentFreeze = false, Type = EnchantmentUtilities.FreezeType.Normal }; break;
                case Kind.DestroyLowPowerSpell:
                    selector.CardConditionalExpressions.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.Attack,
                        Is = CardConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo, Value = new ConditionalValueData(e.A) });
                    native = new CardActions.Destroy { Sacrifice = false }; break;
                case Kind.DestroyWoundedEnemySpell:
                    selector.CardConditionalExpressions.Add(Enemies());
                    selector.CardConditionalExpressions.Add(Wounded());
                    native = new CardActions.Destroy { Sacrifice = false }; break;
                case Kind.DestroyWoundedSummon:
                    ability.PlayTargetingType = PlayTargetingType.OptionalTarget;
                    selector.CardConditionalExpressions.Add(Wounded());
                    native = new CardActions.Destroy { Sacrifice = false }; break;
                case Kind.CoverSummon:
                    ability.PlayTargetingType = PlayTargetingType.OptionalTarget;
                    // Native AddCover(expireAtStartOfTurn: !MultipleTurns) = ordinary Cover until the next turn.
                    native = new CardActions.Cover { MultipleTurns = false }; break;
                case Kind.OpponentDamageDraw:
                    ability.PlayTargetingType = PlayTargetingType.NoTarget;
                    PlayerEffectAction damage = new PlayerEffectAction { Action = new PlayerActions.DamageAvatarOfPlayer { Amount = new ActionValueData(e.A), AssetHash = -1 } };
                    damage.Selectors.Add(Role(PlayerRole.Opponent));
                    Effect first = new Effect(); first.PlayerActions.Add(damage); group.Effects.Add(first);
                    Effect second = new Effect(); second.PlayerActions.Add(Draw()); group.Effects.Add(second);
                    return ability;
                default: throw new NotSupportedException("Unknown play kind.");
            }
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = native };
            action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
            Effect effect = new Effect(); effect.CardActions.Add(action); group.Effects.Add(effect);
            ability.TargetSelectors.Add(selector);
            return ability;
        }

        private static TriggeredEffect CreateTrigger(Entry e)
        {
            Effect effect = new Effect();
            TriggeredEffect trigger = new TriggeredEffect { ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            if (e.Kind == Kind.LastGaspDraw)
            {
                trigger.TriggerType = EffectTriggerType.LastGaspTrigger;
                effect.PlayerActions.Add(Draw());
            }
            else
            {
                trigger.TriggerType = EffectTriggerType.Summon;
                CardEffectAction action = new CardEffectAction { HashId = -1, Action = new CardActions.Damage { HealthLoss = new ActionValueData(e.A), DamageSource = CardRole.Self } };
                action.Selectors.Add(Creature()); action.Selectors.Add(Board()); action.Selectors.Add(Enemies());
                action.Selectors.Add(new CardConditionals.InSameLane { Same = CardConditionals.InSameLane.Requirement.Is, OtherCard = CardRole.Self });
                effect.CardActions.Add(action);
            }
            trigger.EffectGroup.Effects.Add(effect);
            return trigger;
        }

        private static PlayerEffectAction Draw()
        {
            PlayerEffectAction draw = new PlayerEffectAction { Action = new PlayerActions.DrawCards { NumCards = new ActionValueData(1), DrawFrom = PlayerActions.DrawCards.DrawDeck.Self,
                OperationType = ServerCardCatalog.MoveOperationType.DefaultDraw, StopAtHandLimit = false, IgnoreDeckingRule = false } };
            draw.Selectors.Add(Role(PlayerRole.Self));
            return draw;
        }
        private static PlayerConditionals.PlayerRoleType Role(PlayerRole role)
        { return new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = role }; }
        // Native ServerCard maintains AttributeType.Wounded = (current health < max) || HasHealthDebuff, matching
        // UESP Legends:Wounded ("damaged or had its health lowered by a debuff"); the narrower Damaged conditional would miss debuffs.
        private static CardConditionals.AttributeValue Wounded()
        { return new CardConditionals.AttributeValue { AttributeName = AttributeType.Wounded, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThan, Value = new ConditionalValueData(0) }; }
        private static CardConditionals.Subtype Creature()
        { return new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature }; }
        private static CardConditionals.Zone Board()
        { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }
        private static CardConditionals.Relationship Enemies()
        { return new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Enemies }; }
    }
}
