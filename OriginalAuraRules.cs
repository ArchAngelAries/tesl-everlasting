using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card Aura wrappers from exact installed text, not recovered server graphs. Applied by the
    // reconstructed OfflineAuraRestoration (native RecalculateAuras is empty in the shipped client) through native
    // ServerCard.Auras / AuraEnchantments / AddEnchantment; Silence natively drops a silenced source's card auras.
    public static class OriginalAuraRules
    {
        public enum Kind { AllFriendlyStats, OtherLaneStats, OtherLaneLethal, GrantPilferBuff, GrantSlayBuffOthers }
        private sealed class Entry
        {
            public int Id, A, B; public Kind Kind; public string Hash;
            public Entry(int id, Kind kind, int a, int b, string hash) { Id = id; Kind = kind; A = a; B = b; Hash = hash; }
        }
        private static readonly Entry[] entries = new Entry[] {
            new Entry(-1264406243, Kind.AllFriendlyStats, 0, 1, "3E6E4FF8BC829C6528B3E6EC8D2061E6EFB1F91AF16F993CBF170331DD57C1DC"), // Divine Conviction (support)
            new Entry(-1653188426, Kind.AllFriendlyStats, 1, 1, "9DBE2CF070C4B1AD9DAC444B4D753E7E7C49D517BC918C26CA8CB373AD342BFE"), // Divine Fervor (support)
            new Entry(-1708131813, Kind.AllFriendlyStats, 1, 1, "5094258525713B6B634D39A5F7162DB54F940F744AC525ABF20DE625C7B84F48"), // Divine Fervor (support)
            new Entry(-1345791661, Kind.AllFriendlyStats, 1, 1, "A0D8EC7A5C73B3EB6026D2719C75D647B7ECDFAD7C5142123E680438CD4028CA"), // Shantytown Defenses (support, AI)
            new Entry(899518525, Kind.OtherLaneStats, 1, 0, "4DA35837BFBB1C7145E223B9D0D5C09F1B9EA201E2605C622386CB0E3684D3F2"), // Orc Clan Captain
            new Entry(1858282246, Kind.OtherLaneStats, 0, 1, "441140FA3F582D4E4425470DA29EA9EC491EB4F8E0C12EAD4120510F9C9903B0"), // Summerset Shieldmage
            new Entry(-1945422996, Kind.OtherLaneLethal, 0, 0, "ABD6E74E95A23EA1FAAB32CC178DABED7CA64BECC653A787AB9E216B4A7B5AE0"), // Doomcrag Vampire (Lethal)
            // Granted keyword triggers: native ApplyEnchantmentEffects adds Enchantment.GrantedEffects to the target's TriggeredEffects.
            new Entry(1659589269, Kind.GrantPilferBuff, 1, 1, "A8E64AC73745A7EACA0385F3D091BB5272A51F5A2AD6B2037E7ECA2629D0BCB1"), // Thieves' Den: friendly creatures have "Pilfer: +1/+1."
            new Entry(1787748980, Kind.GrantSlayBuffOthers, 2, 2, "5611625F7C5952CB01562AC66253D312A929C3F7E953276EC3ED1428C342F1C4"), // Lucien Lachance: other friendly creatures have "Slay: +2/+2."
        };
        private static readonly Dictionary<Card, List<Aura>> owned = new Dictionary<Card, List<Aura>>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static int ValueA(int id) { return Get(id).A; }
        public static int ValueB(int id) { return Get(id).B; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited aura ID: " + id); return e; }
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
                    throw new NotSupportedException("Original aura metadata differs: " + e.Id);
                List<Aura> auras;
                if (owned.TryGetValue(card, out auras))
                {
                    bool valid = (card.TriggeredEffects == null || card.TriggeredEffects.Count == 0) && (card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                        && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0))
                        && card.Auras == auras && OriginalGraphDescriber.Auras(auras) == OriginalGraphDescriber.Auras(Create(e));
                    if (!valid) throw new NotSupportedException("Owned original aura graph changed or gained extra behavior: " + e.Id);
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
                    List<Aura> auras = Create(entries[i]);
                    card.Auras = auras;
                    owned.Add(card, auras);
                }
                if (card.TriggeredEffects == null) card.TriggeredEffects = new List<TriggeredEffect>();
                if (card.ActionPlayInformation == null) card.ActionPlayInformation = new List<PlayCardAbility>();
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        private static List<Aura> Create(Entry e)
        {
            AuraEnchantment ae = new AuraEnchantment { Name = "Original aura " + e.Id, StackCount = new ActionValueData(1) };
            if (e.Kind == Kind.OtherLaneLethal)
                ae.Enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Deathtouch, 1));
            else if (e.Kind == Kind.GrantPilferBuff || e.Kind == Kind.GrantSlayBuffOthers)
            {
                SPTESL.Enchantment gain = new SPTESL.Enchantment();
                gain.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, e.A));
                gain.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, e.B));
                CardEffectAction buff = new CardEffectAction { HashId = -1, Action = new CardActions.Buff { Enchantment = gain } };
                buff.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self });
                Effect effect = new Effect(); effect.CardActions.Add(buff);
                TriggeredEffect granted = new TriggeredEffect { TriggerType = e.Kind == Kind.GrantPilferBuff ? EffectTriggerType.Pilfer : EffectTriggerType.Slay,
                    ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
                granted.EffectGroup.Effects.Add(effect);
                ae.Enchantment.GrantedEffects.Add(granted);
            }
            else
            {
                if (e.A != 0) ae.Enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, e.A));
                if (e.B != 0) ae.Enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, e.B));
            }
            ae.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
            ae.Selectors.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } });
            ae.Selectors.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends });
            if (e.Kind == Kind.OtherLaneStats || e.Kind == Kind.OtherLaneLethal)
                ae.Selectors.Add(new CardConditionals.InSameLane { Same = CardConditionals.InSameLane.Requirement.Is, OtherCard = CardRole.Self });
            if (e.Kind == Kind.OtherLaneStats || e.Kind == Kind.OtherLaneLethal || e.Kind == Kind.GrantSlayBuffOthers)
                ae.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = CardRole.Self });
            Aura aura = new Aura { NameForEditor = "Original aura " + e.Id };
            aura.EnchantmentsToApply.Add(ae);
            return new List<Aura> { aura };
        }
    }
}
