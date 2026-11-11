using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card "Put a random <kind> into your hand" wrappers from exact installed text, not recovered
    // server graphs. Creation runs in native PlayerActions.CreateToken with EditorCardReference.RandomFromSelector: native
    // GameManager.GetRandomCardFromSelectorPool draws uniformly with the game RNG from installed cards whose serialized
    // IneligibleForRandomQuery flag matches (native special pools for certain subtypes) and that satisfy the native
    // CollectionConditionals selectors. "Two random" uses two independent actions (one action copies one resolved card).
    // A result that is not yet restored stays in hand but cannot be played (honest gating via RuleImporter.IsSupported).
    public static class OriginalRandomCardRules
    {
        private sealed class Entry
        {
            public int Id, Count; public EffectTriggerType Trigger; public CardSubtype Pool; public string Hash;
            public Entry(int id, EffectTriggerType trigger, CardSubtype pool, int count, string hash) { Id = id; Trigger = trigger; Pool = pool; Count = count; Hash = hash; }
        }
        private const EffectTriggerType Play = EffectTriggerType.ActionEffect, LastGasp = EffectTriggerType.LastGaspTrigger;
        private static readonly Entry[] entries = new Entry[] {
            new Entry(-1196141924, EffectTriggerType.Summon, CardSubtype.Atronach, 1, "7A46F5C98E8B8D028AD1B986B3981BACCB11DEE9B0DC90E11F7EC60E890CD78C"), // High Rock Summoner
            new Entry(-512863404, EffectTriggerType.Summon, CardSubtype.Animal, 1, "C476180908033790655D3F610983FE12820A31640B9F16CC5683F7E7E6BC274D"), // Eldergleam Matron
            new Entry(-1756786374, LastGasp, CardSubtype.Dwemer, 1, "83D0BB6C32060C190B949DA8E718E5BCDF556BF52C5537D172EE9CC56FE74734"), // Stronghold Prototype
            new Entry(-1232844635, LastGasp, CardSubtype.Dwemer, 2, "77BB99E3A6C81459C6954154988CBAF087EDD3C707F9717ED310BB9A6DC87E49"), // Stronghold Incubator
            new Entry(1874902165, LastGasp, CardSubtype.Spell, 1, "5C8195EBFCB64D86433EE77A9F3340E8D703D22D6AF3366CBAD5E9745A7272F0"), // Telvanni Arcanist (random action)
            new Entry(830964466, LastGasp, CardSubtype.Shout, 1, "2646E4117315283F53D67FFC864F88B1E1D8B68F32AFF41ADFC2FD0DF92ADDDE"), // Dovah of the Voice
            new Entry(280505946, EffectTriggerType.Slay, CardSubtype.Item, 1, "F00C2F2D440797D6FA0CFB27A2773F620D2B0939B2A728DB199DE544E73DCB21"), // Battlefield Scrounger
            new Entry(-1470562179, Play, CardSubtype.Item, 2, "8AE932F94F20BD567E21F08805A4B1D2568C321E1F35B514450CA6F408F6F966"), // Plunder
            new Entry(1268231575, Play, CardSubtype.Spell, 2, "21CB36E6C566FC463B3F7C1CC26F9435A1D953BD7DC50F2180789F976286197A"), // Revealing the Unseen
            new Entry(-587697859, Play, CardSubtype.Spell, 2, "EC65D3F71EA87540BFB05133DB8EBC23FC72004DDBB327400B44DB702B0288E2"), // Revealing the Unseen
        };
        private static readonly Dictionary<Card, object> owned = new Dictionary<Card, object>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static EffectTriggerType TriggerOf(int id) { return Get(id).Trigger; }
        public static CardSubtype PoolOf(int id) { return Get(id).Pool; }
        public static int CountOf(int id) { return Get(id).Count; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited random-card ID: " + id); return e; }
        private static Entry Find(int id) { foreach (Entry e in entries) if (e.Id == id) return e; return null; }

        // The native pool for a creator, evaluated exactly as GameManager.GetRandomCardFromSelectorPool does.
        public static List<Card> Pool(int id)
        {
            List<ICollectionConditionalExpression> selectors = new List<ICollectionConditionalExpression> { Selector(Get(id).Pool) };
            return CollectionConditionals.EvaluateSelectorsOnCollection(selectors, default(CardConditionalArguments), default(PlayerConditionalArguments), default(TriggerConditionalArguments));
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
                    throw new NotSupportedException("Original random-card creator metadata differs: " + e.Id);
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
                    if (!valid) throw new NotSupportedException("Owned original random-card graph changed or gained extra behavior: " + e.Id);
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
            Effect effect = new Effect();
            for (int i = 0; i < e.Count; i++)
            {
                EditorCardReference reference = new EditorCardReference { ReferenceStyle = EditorCardReferenceStyle.RandomFromSelector };
                reference.RandomCreationSelectors.Add(Selector(e.Pool));
                PlayerEffectAction create = new PlayerEffectAction { Action = new PlayerActions.CreateToken { CardReference = reference, CreateInZoneType = GameZoneType.Hand,
                    NumTokens = new ActionValueData(1), InsertPosition = new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Bottom } } };
                create.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Self });
                effect.PlayerActions.Add(create);
            }
            return effect;
        }
        private static ICollectionConditionalExpression Selector(CardSubtype pool)
        { return new CollectionConditionals.Subtype { Check = CollectionConditionals.Subtype.Requirement.Is, SubtypeName = pool }; }
    }
}
