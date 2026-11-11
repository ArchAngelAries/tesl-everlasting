using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card wrappers for "Put a <card> into your hand" from exact installed text, not recovered
    // server graphs. The created card identity is NOT invented: it is taken from each card's surviving original
    // CardReferences and checked by name. Creation runs in intact native PlayerActions.CreateToken (Specific
    // EditorCardReference, Hand zone, constant-lane Bottom InsertPosition); a full hand yields no card, as natively.
    public static class OriginalTokenRules
    {
        private sealed class Entry
        {
            public int Id; public EffectTriggerType Trigger; public string[] Tokens; public string Hash;
            public Entry(int id, EffectTriggerType trigger, string hash, params string[] tokens) { Id = id; Trigger = trigger; Hash = hash; Tokens = tokens; }
        }
        private static readonly Entry[] entries = new Entry[] {
            new Entry(-474818882, EffectTriggerType.Summon, "19E7AFDE1E266DB80A2A1E844D030993B82785D51C08FAD6FC362F6EA1332A1A", "Steel Dagger"), // Crown Quartermaster
            new Entry(-846195381, EffectTriggerType.Summon, "F8F756C5DFB8F128D787FE93D191989F4C768AD19247A8DD306D25FF95814CA1", "Steel Dagger"), // Crown Quartermaster
            new Entry(-146382907, EffectTriggerType.Summon, "504F988266B728B27BDF1090BF2707D12172556695352300EBA391BE232942F1", "Steel Dagger", "Steel Sword"), // Sentinel Reclaimer
            new Entry(752254333, EffectTriggerType.LastGaspTrigger, "B0D964F7337E1CBD46E16DED0C7C20BF2185E06401C80C90C2A5C0621BF4C573", "Iron Sword"), // Dunmer Nightblade
            new Entry(-679792433, EffectTriggerType.LastGaspTrigger, "DCA3BE9A28227AEE8045A7DCAF02BD1765A59D86FB112AC5560AA8C7E217AC07", "Heavy Battleaxe"), // Ulfric's Zealot
            new Entry(1038408842, EffectTriggerType.LastGaspTrigger, "D27A16D5D8BE9D95D5331C73E83B552CCE5E37980859C9D5411DCB64607C0718", "Stormcloak Battalion"), // Stormcloak Vanguard
        };
        private static readonly Dictionary<Card, TriggeredEffect> owned = new Dictionary<Card, TriggeredEffect>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static EffectTriggerType TriggerOf(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited token ID: " + id); return e.Trigger; }
        private static Entry Find(int id) { foreach (Entry e in entries) if (e.Id == id) return e; return null; }

        // Resolve each named token through the card's own original CardReferences, in installed-text order.
        public static int[] TokenIds(GameConfiguration config, int id)
        {
            Entry e = Find(id); Card card = config.FindCardById(id);
            if (e == null || card == null || card.CardReferences == null || card.CardReferences.Count != e.Tokens.Length)
                throw new NotSupportedException("Original token references differ: " + id);
            int[] result = new int[e.Tokens.Length];
            for (int i = 0; i < e.Tokens.Length; i++)
            {
                result[i] = 0;
                foreach (UniqueResourceReference reference in card.CardReferences)
                {
                    Card token = config.FindCardById(reference.resourceId);
                    if (token != null && token.Name == e.Tokens[i]) { result[i] = reference.resourceId; break; }
                }
                if (result[i] == 0)
                    throw new NotSupportedException("Original token reference missing: " + id + " / " + e.Tokens[i]);
            }
            return result;
        }

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
                    throw new NotSupportedException("Original token-creator metadata differs: " + e.Id);
                TokenIds(config, e.Id);
                TriggeredEffect trigger;
                if (owned.TryGetValue(card, out trigger))
                {
                    bool valid = (card.Auras == null || card.Auras.Count == 0) && (card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                        && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0))
                        && card.TriggeredEffects != null && card.TriggeredEffects.Count == 1 && card.TriggeredEffects[0] == trigger
                        && OriginalGraphDescriber.Trigger(trigger) == OriginalGraphDescriber.Trigger(Create(e, TokenIds(config, e.Id)));
                    if (!valid) throw new NotSupportedException("Owned original token graph changed or gained extra behavior: " + e.Id);
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
                    TriggeredEffect trigger = Create(entries[i], TokenIds(config, entries[i].Id));
                    card.TriggeredEffects = new List<TriggeredEffect> { trigger };
                    owned.Add(card, trigger);
                }
                if (card.ActionPlayInformation == null) card.ActionPlayInformation = new List<PlayCardAbility>();
                if (card.Auras == null) card.Auras = new List<Aura>();
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        private static TriggeredEffect Create(Entry e, int[] tokens)
        {
            TriggeredEffect trigger = new TriggeredEffect { TriggerType = e.Trigger, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            Effect effect = new Effect();
            foreach (int token in tokens)
            {
                PlayerEffectAction create = new PlayerEffectAction { Action = new PlayerActions.CreateToken {
                    CardReference = new EditorCardReference(token), CreateInZoneType = GameZoneType.Hand, NumTokens = new ActionValueData(1),
                    InsertPosition = new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Bottom } } };
                create.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Self });
                effect.PlayerActions.Add(create);
            }
            trigger.EffectGroup.Effects.Add(effect);
            return trigger;
        }
    }
}
