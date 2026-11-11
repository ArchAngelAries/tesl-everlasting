using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card Summon wrappers from exact installed text, not recovered server graphs.
    // Targeted Summons use the play-time ActionPlayInformation route: the original client's
    // BoardInputController.Targeting_Start records the creature's drop lane/slot and then aims at
    // the command's PlayTargets, and native PlayCardHandler only enforces RequiredTarget for Spells.
    // OptionalTarget keeps the creature playable when no legal target exists (native AreTargetRequirementsMet).
    // All effects execute in intact original CardActions.Damage/Buff/Debuff/Freeze and PlayerActions.DamageAvatarOfPlayer.
    public static class OriginalTargetedSummonRules
    {
        public enum Kind { DamageAny, DamageCreature, BuffCreature, ShackleEnemyCreature, DamageOpponent }
        private sealed class Entry
        {
            public int Id, Attack, Health; public Kind Kind; public string Hash;
            public Entry(int id, Kind kind, int attack, int health, string hash) { Id=id; Kind=kind; Attack=attack; Health=health; Hash=hash; }
        }
        // For Damage kinds, Attack carries the damage amount. For BuffCreature, Attack/Health are the exact +X/+Y text values.
        private static readonly Entry[] entries = new Entry[] {
            new Entry(1735315658, Kind.DamageAny, 1, 0, "AD23997905BBC581BC7DE6D8A7011D2629B7C72E073CB69474872AE8FFA83F86"), // Allena Benoch (Lethal)
            new Entry(1884147519, Kind.DamageAny, 1, 0, "7719E163162B1A504251DEFBA327291318BE637BC243E350BADA5E5F2C909F3A"), // Sharpshooter Scout (Prophecy)
            new Entry(-1871307781, Kind.DamageAny, 1, 0, "B52A0497298365A2428D1AD3FE14BEE4FAF199103DB422040AD44313466C3733"), // Sharpshooter Scout (Prophecy)
            new Entry(-1767873507, Kind.DamageAny, 1, 0, "F24B234186DA24B1686619839388814FF4B590574C4D1717F5C59F5B3ABEDEED"), // Valenwood Huntsman
            new Entry(1863406899, Kind.DamageCreature, 2, 0, "64BFC8D963A743DF721D43043B1649857E025C3B6716AD87C2DA5B02CEEB58B7"), // Ash Servant
            new Entry(-1249810974, Kind.DamageCreature, 2, 0, "B2DDC7CBB2754B06CBCBA9B57D6A4ABB337D78B875D1B2CACB8C2F5B5DEF1D63"), // Ash Servant
            new Entry(1750084664, Kind.DamageCreature, 4, 0, "F90C1453EEEBF2F82B5B51D6A1F6FDE6D0B66479F4DD026C61ABC9E8643B5DD0"), // Shocking Wamasu
            new Entry(-781594103, Kind.BuffCreature, 2, 1, "2A8217EB84BA9F04B80A4B12E6C248B128067B7877B1B44DEA4B07E45E0A6AF7"), // Frenzied Witchman
            new Entry(1106955994, Kind.BuffCreature, 2, 0, "00D941F6513FFE6467E2579FA1C0EB12077EAE8C1B84720545758AD37F00F853"), // Morkul Gatekeeper (Guard, Prophecy)
            new Entry(-967450522, Kind.BuffCreature, -1, -1, "AC530EB1FDD0F9320373F0846C0D77806538B05BD3E1A3250663203AE71BDF11"), // Murkwater Witch (Guard)
            new Entry(-463613369, Kind.BuffCreature, -1, -1, "E6F9F9FC109BAECB0BE901EFE779991E629FC9FA763BFFC00C1E7FA1B7BFC432"), // Murkwater Witch (Guard)
            new Entry(-1621791439, Kind.BuffCreature, 1, 0, "8D14FA6AC4A1FC3E1DB8D9B209BF23349CDB3EBF7AE0FC3BC81A1C1BAB03EEE6"), // Orc Clansman
            new Entry(1826150536, Kind.BuffCreature, 1, 1, "05F032428C02D781AC58A52827CEBFBABB8DDE1259C61755351DA66D25DE8E80"), // Prowling Hunger
            new Entry(-452943738, Kind.BuffCreature, 0, 2, "0931680C9978F773C45029F2B8047032B6E290011BE5F58C61DD7D9B2CE8CC87"), // Sunhold Medic
            new Entry(-1935845219, Kind.BuffCreature, 1, 1, "9A42C57F92E4B9CF95E90BCF24830DDD15FC9435B7451F81D5C29854825450E9"), // Wrothgar Artisan
            new Entry(2052829044, Kind.BuffCreature, 0, 2, "08098A178FC1D2320A365D8953F824C62CF784F7FDD7DCDFAE0256509BC100DC"), // Cleric of Kyne (Prophecy)
            new Entry(-12036977, Kind.ShackleEnemyCreature, 0, 0, "3A08BE71CFCA615750EF6B3B3B51770D013B44E4457F668032C7E5E8FD033CA7"), // Dwarven Sphere
            new Entry(-710854512, Kind.ShackleEnemyCreature, 0, 0, "ED286ADCA60AC798AC56927D0B403828A18E0422521632C98A6993441DA71CA7"), // Shrieking Harpy (Prophecy)
            new Entry(-1766587430, Kind.ShackleEnemyCreature, 0, 0, "3B463B04DB0749A4C0BC757A5292DB3E333BB0A0D397B09708164B8C77E700D9"), // Shrieking Harpy (Prophecy)
            new Entry(575791787, Kind.ShackleEnemyCreature, 0, 0, "FDAF37336967618EF074C7BB8AA12AD7856F22946B19A7348A88044FF7B4E702"), // Mace of Encumbrance (Animated_Item creature)
            new Entry(-456767346, Kind.ShackleEnemyCreature, 0, 0, "D0F7727B8DA1C7841004E73636345319891A15CF40E945624757252F31989D52"), // Icewing Dragon
            new Entry(-554006585, Kind.DamageOpponent, 2, 0, "7108BBE35F07E9D76905FC02A72C8B1374EDBF4EE8BC75CE39B058486FF23EF1"), // Camlorn Hero
            new Entry(380978308, Kind.DamageOpponent, 2, 0, "F364873EA8B9C76D934F90B1BA0C47951E6A9DD723F16E0264A719A1C3D85C54"), // Crystal Tower Acolyte
            new Entry(-602835722, Kind.DamageOpponent, 1, 0, "DB1D4AC7CD1AD43A60A808F7CB09AACEFDD8D9238BE8AEA679F9FA824E40CB1C"), // Mudcrab Anklesnapper
            new Entry(667662641, Kind.DamageOpponent, 1, 0, "269CEB3FD826A484ECC1B11E2FA65F16B22CA7CF808E239EF0444BB258481A0C"), // Mudcrab Anklesnapper
        };
        private static readonly Dictionary<Card, object> owned = new Dictionary<Card, object>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited targeted Summon ID: " + id); return e.Kind; }
        public static int Amount(int id) { Entry e = Find(id); if (e == null || e.Kind == Kind.BuffCreature || e.Kind == Kind.ShackleEnemyCreature) throw new NotSupportedException("No damage amount: " + id); return e.Attack; }
        public static int BuffAttack(int id) { Entry e = Find(id); if (e == null || e.Kind != Kind.BuffCreature) throw new NotSupportedException("No stat change: " + id); return e.Attack; }
        public static int BuffHealth(int id) { Entry e = Find(id); if (e == null || e.Kind != Kind.BuffCreature) throw new NotSupportedException("No stat change: " + id); return e.Health; }
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
                    throw new NotSupportedException("Original targeted Summon metadata differs: " + e.Id);
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
                    if (e.Kind == Kind.DamageOpponent)
                    {
                        TriggeredEffect trigger = CreateOpponentTrigger(e);
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

        private static TriggeredEffect CreateOpponentTrigger(Entry e)
        {
            PlayerEffectAction action = new PlayerEffectAction { Action = new PlayerActions.DamageAvatarOfPlayer { Amount = new ActionValueData(e.Attack), AssetHash = -1 } };
            action.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Opponent });
            Effect effect = new Effect(); effect.PlayerActions.Add(action);
            EffectGroup group = new EffectGroup(); group.Effects.Add(effect);
            return new TriggeredEffect { TriggerType = EffectTriggerType.Summon, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = group };
        }

        private static PlayCardAbility CreateAbility(Entry e)
        {
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = NativeAction(e) };
            action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
            Effect effect = new Effect(); effect.CardActions.Add(action);
            EffectGroup group = new EffectGroup(); group.Effects.Add(effect);
            PlayCardAbility ability = new PlayCardAbility { EffectGroup = group, PlayTargetingType = PlayTargetingType.OptionalTarget };
            DecisionConditionalExpression selector = new DecisionConditionalExpression { DecisionType = DecisionType.Card };
            if (e.Kind == Kind.DamageAny)
            {
                CardConditionals.SubtypeCount types = new CardConditionals.SubtypeCount { Is = CardConditionals.SubtypeCount.ComparisonType.GreaterThanOrEqualTo, Value = 1 };
                types.Subtypes.Add(CardSubtype.Creature); types.Subtypes.Add(CardSubtype.Avatar);
                selector.CardConditionalExpressions.Add(types);
                selector.CardConditionalExpressions.Add(BoardZone());
            }
            else
            {
                selector.CardConditionalExpressions.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
                selector.CardConditionalExpressions.Add(BoardZone());
                if (e.Kind == Kind.ShackleEnemyCreature)
                    selector.CardConditionalExpressions.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Enemies });
            }
            ability.TargetSelectors.Add(selector);
            return ability;
        }

        private static ICardAction NativeAction(Entry e)
        {
            switch (e.Kind)
            {
                case Kind.DamageAny:
                case Kind.DamageCreature:
                    return new CardActions.Damage { HealthLoss = new ActionValueData(e.Attack), DamageSource = CardRole.Self };
                case Kind.ShackleEnemyCreature:
                    // FreezeType selects only the visual variant; the original per-card choice is absent, so Normal is used.
                    return new CardActions.Freeze { PermanentFreeze = false, Type = EnchantmentUtilities.FreezeType.Normal };
                case Kind.BuffCreature:
                    Enchantment enchantment = new Enchantment();
                    if (e.Attack != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, e.Attack));
                    if (e.Health != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, e.Health));
                    // Negative changes use native Debuff, which owns curse amplification; positive ones native Buff.
                    if (e.Attack < 0 || e.Health < 0) return new CardActions.Debuff { Enchantment = enchantment };
                    return new CardActions.Buff { Enchantment = enchantment };
            }
            throw new NotSupportedException("Unknown targeted Summon kind.");
        }

        private static CardConditionals.Zone BoardZone()
        { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }

        private static bool OtherWrappersEmpty(Card card, bool allowTrigger, bool allowAbility)
        {
            return (allowTrigger || card.TriggeredEffects == null || card.TriggeredEffects.Count == 0)
                && (allowAbility || card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                && (card.Auras == null || card.Auras.Count == 0)
                && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0));
        }

        private static void ValidateOwned(Card card, object graph, Entry e)
        {
            if (e.Kind == Kind.DamageOpponent) { ValidateOpponent(card, graph as TriggeredEffect, e); return; }
            PlayCardAbility ability = graph as PlayCardAbility;
            if (ability == null || !OtherWrappersEmpty(card, false, true) || card.ActionPlayInformation == null || card.ActionPlayInformation.Count != 1
                || card.ActionPlayInformation[0] != ability || ability.PlayTargetingType != PlayTargetingType.OptionalTarget
                || ability.CustomUiType != CustomCardUiType.None || ability.IsDamageOnly || ability.CustomUiEffectGroups.Count != 0
                || ability.EffectGroup == null || ability.EffectGroup.Effects.Count != 1 || ability.TargetSelectors == null || ability.TargetSelectors.Count != 1) Reject();
            Effect effect = ability.EffectGroup.Effects[0];
            if (effect.CardActions.Count != 1 || effect.PlayerActions.Count != 0 || effect.ExecuteValueRequirements.Count != 0 || effect.ExecuteCardRequirements.Count != 0
                || effect.ExecutePlayerRequirements.Count != 0 || effect.ExecuteLaneRequirements.Count != 0) Reject();
            CardEffectAction action = effect.CardActions[0];
            CardConditionals.CardRoleType role = action.Selectors.Count == 1 ? action.Selectors[0] as CardConditionals.CardRoleType : null;
            if (action.HashId != -1 || role == null || role.Check != CardConditionals.CardRoleType.Requirement.Is || role.Role != CardRole.DecisionTarget) Reject();
            ValidateAction(action.Action, e);
            DecisionConditionalExpression selector = ability.TargetSelectors[0];
            List<ICardConditionalExpression> c = selector.CardConditionalExpressions;
            if (selector.DecisionType != DecisionType.Card || selector.LaneConditionalExpressions.Count != 0) Reject();
            if (e.Kind == Kind.DamageAny)
            {
                CardConditionals.SubtypeCount count = c.Count == 2 ? c[0] as CardConditionals.SubtypeCount : null;
                if (count == null || count.Is != CardConditionals.SubtypeCount.ComparisonType.GreaterThanOrEqualTo || count.Value != 1
                    || count.Subtypes.Count != 2 || count.Subtypes[0] != CardSubtype.Creature || count.Subtypes[1] != CardSubtype.Avatar) Reject();
                ValidateZone(c[1]);
                return;
            }
            if (c.Count != (e.Kind == Kind.ShackleEnemyCreature ? 3 : 2)) Reject();
            CardConditionals.Subtype type = c[0] as CardConditionals.Subtype;
            if (type == null || type.Check != CardConditionals.Subtype.Requirement.Is || type.SubtypeName != CardSubtype.Creature || type.TestSubtypesOfDouble) Reject();
            ValidateZone(c[1]);
            if (e.Kind == Kind.ShackleEnemyCreature)
            {
                CardConditionals.Relationship enemy = c[2] as CardConditionals.Relationship;
                if (enemy == null || enemy.OtherCard != CardRole.Self || enemy.Are != CardConditionals.Relationship.RelationshipType.Enemies) Reject();
            }
        }

        private static void ValidateAction(ICardAction native, Entry e)
        {
            if (e.Kind == Kind.DamageAny || e.Kind == Kind.DamageCreature)
            {
                CardActions.Damage damage = native as CardActions.Damage;
                if (damage == null || damage.DamageSource != CardRole.Self || damage.UseItemWielderAsDamageSource || damage.SeparateLogMessages
                    || !Constant(damage.HealthLoss, e.Attack)) Reject();
                return;
            }
            if (e.Kind == Kind.ShackleEnemyCreature)
            {
                CardActions.Freeze freeze = native as CardActions.Freeze;
                if (freeze == null || freeze.PermanentFreeze || freeze.Type != EnchantmentUtilities.FreezeType.Normal) Reject();
                return;
            }
            Enchantment enchantment;
            bool negative = e.Attack < 0 || e.Health < 0;
            if (negative)
            {
                CardActions.Debuff debuff = native as CardActions.Debuff;
                if (debuff == null || debuff.SkipLogging || debuff.SanitizeRule != CcgRpc.SanitizeRule.Default) Reject();
                enchantment = debuff.Enchantment;
            }
            else
            {
                CardActions.Buff buff = native as CardActions.Buff;
                if (buff == null || buff.IgnoreInClientLog || buff.SimultaneousFx || buff.SanitizeRule != CcgRpc.SanitizeRule.Default) Reject();
                enchantment = buff.Enchantment;
            }
            int expected = (e.Attack != 0 ? 1 : 0) + (e.Health != 0 ? 1 : 0);
            if (enchantment == null || enchantment.AttributeModifications.Count != expected || enchantment.GrantedEffects.Count != 0 || enchantment.GrantedAuras.Count != 0
                || enchantment.AddedCreatureSubtypes.Count != 0 || enchantment.MaxTurns != 0 || enchantment.ExpiresAtEndOfTurn || enchantment.ExpiresAtStartOfTurn
                || enchantment.ExpiresAtStartOfSourcesTurn || enchantment.KeepWhenMovesOffBoard) Reject();
            int index = 0;
            if (e.Attack != 0) ValidateModification(enchantment.AttributeModifications[index++], AttributeType.Attack, e.Attack);
            if (e.Health != 0) ValidateModification(enchantment.AttributeModifications[index], AttributeType.MaxHealth, e.Health);
        }

        private static void ValidateModification(EnchantmentAttributeModification m, AttributeType name, int value)
        {
            if (m == null || m.AttributeName != name || m.ModificationType != EnchantmentAttributeModificationType.Add || m.Value != value
                || m.DynamicValue != 0 || m.LockedDynamicValue) Reject();
        }

        private static void ValidateOpponent(Card card, TriggeredEffect trigger, Entry e)
        {
            if (trigger == null || !OtherWrappersEmpty(card, true, false) || card.TriggeredEffects == null || card.TriggeredEffects.Count != 1
                || card.TriggeredEffects[0] != trigger || trigger.TriggerType != EffectTriggerType.Summon || trigger.ClientTargetingType != PlayTargetingType.NoTarget
                || trigger.CustomUiType != CustomCardUiType.None || trigger.TargetSelectors.Count != 0 || trigger.TriggerValueRequirements.Count != 0
                || trigger.TriggerCardRequirements.Count != 0 || trigger.TriggerPlayerRequirements.Count != 0 || trigger.BeforeTargetingCardRequirements.Count != 0
                || trigger.BeforeTargetingPlayerRequirements.Count != 0 || trigger.AfterRuneBreaks || trigger.AreaEffect || trigger.AllowReentrancy
                || trigger.OpponentChooses || trigger.SecretTarget || trigger.CustomUiEffectGroups.Count != 0
                || trigger.EffectGroup == null || trigger.EffectGroup.Effects.Count != 1) Reject();
            Effect effect = trigger.EffectGroup.Effects[0];
            if (effect.CardActions.Count != 0 || effect.PlayerActions.Count != 1 || effect.ExecuteValueRequirements.Count != 0 || effect.ExecuteCardRequirements.Count != 0
                || effect.ExecutePlayerRequirements.Count != 0 || effect.ExecuteLaneRequirements.Count != 0) Reject();
            PlayerEffectAction action = effect.PlayerActions[0];
            PlayerActions.DamageAvatarOfPlayer damage = action.Action as PlayerActions.DamageAvatarOfPlayer;
            PlayerConditionals.PlayerRoleType role = action.Selectors.Count == 1 ? action.Selectors[0] as PlayerConditionals.PlayerRoleType : null;
            if (damage == null || damage.AssetHash != -1 || !Constant(damage.Amount, e.Attack) || role == null
                || role.Check != PlayerConditionals.PlayerRoleType.Requirement.Is || role.Role != PlayerRole.Opponent) Reject();
        }

        private static bool Constant(ActionValueData value, int amount)
        {
            return value != null && value.ValueMethod == ActionValueMethod.Constant && value.ConstantValue == amount && value.Selectors.Count == 0
                && value.ConstantMultiplier == 0 && value.MinValue == 0 && value.MaxValue == 0;
        }
        private static void ValidateZone(ICardConditionalExpression expression)
        {
            CardConditionals.Zone zone = expression as CardConditionals.Zone;
            if (zone == null || zone.Check != CardConditionals.Zone.Requirement.IsIn || zone.ZoneTypeMask.Bits != GameZoneType.Board) Reject();
        }
        private static void Reject() { throw new NotSupportedException("Owned original targeted Summon graph changed or gained extra behavior."); }
    }
}
