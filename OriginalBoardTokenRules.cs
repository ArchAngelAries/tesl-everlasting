using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card "Summon a <token>" wrappers from exact installed text, not recovered server graphs.
    // Tokens are created on the board by intact native PlayerActions.CreateToken; placement uses native InsertPosition:
    // Relative to Self's BoardLaneIndex (same/other lane; retained after death for Last Gasp), Constant Left/Right for
    // "in each lane", Selected + FillLane for "Fill a lane". A full lane yields no token (native FindInsertionPosition).
    // Token identity: the card's surviving original CardReferences when present; otherwise the unique installed
    // creature with the exact text name, checked against the text's power/health. Never invented.
    public static class OriginalBoardTokenRules
    {
        public enum Place { SameLane, OtherLane, EachLane, FillChosenLane }
        private sealed class Entry
        {
            public int Id, Attack, Health; public EffectTriggerType Trigger; public Place Place; public string Token; public bool Referenced; public string Hash;
            public Entry(int id, EffectTriggerType trigger, Place place, string token, int attack, int health, bool referenced, string hash)
            { Id = id; Trigger = trigger; Place = place; Token = token; Attack = attack; Health = health; Referenced = referenced; Hash = hash; }
        }
        private const EffectTriggerType Summon = EffectTriggerType.Summon, LastGasp = EffectTriggerType.LastGaspTrigger, Play = EffectTriggerType.ActionEffect;
        private static readonly Entry[] entries = new Entry[] {
            new Entry(1322155544, Summon, Place.SameLane, "Northpoint Herald", 0, 4, false, "E9A49A11D54BE7EE42FC40BBD529C639D979B72DF3D17BFA3DF2360521D05BF3"), // Northpoint Captain
            new Entry(-498166336, Summon, Place.SameLane, "Snow Fox", 2, 3, false, "14662E357CE21C296E87F3238AE07D86E2E799922ABE537187CED7BB9BDE9808"), // Wild Spriggan
            new Entry(1413311580, Summon, Place.SameLane, "Imperial Grunt", 1, 1, false, "FB372B672029A47F23E58122B7A6CBA7CF603727C77BE5F795693F812A3B11FA"), // Rising Legate
            new Entry(-1077442028, Summon, Place.SameLane, "Frost Atronach", 5, 5, true, "02DCBCC916177A58A3EECB0015C2CF606FB1EEE89FF5B677631F44526C61DE0C"), // Conjuration Scholar
            new Entry(775516546, Summon, Place.OtherLane, "Young Wolf", 1, 1, false, "507AF18E88F96E922E3A9D99C939B88CDA6BD998FCC083176D0535F6BE630214"), // Pack Wolf
            new Entry(677816908, Summon, Place.EachLane, "Flame Atronach", 5, 3, true, "A78FDEF97F2EB9356DC078C9673B782881C4192331EC5ED0EB5A23AE82951EF2"), // Expert Atromancer
            new Entry(-481674007, LastGasp, Place.SameLane, "Skeleton", 1, 1, false, "5C4B92E21806A754F843F2B20C9938057A3B7299F5EAC2FDFF775672F32B35D9"), // Deathless Draugr
            new Entry(543164871, LastGasp, Place.SameLane, "Deshaan Sneak", 3, 3, false, "35FF74DA2A2F24A63CD6AFAE1E99836A9F88924C532E91ABFCA1E12ECE657C4F"), // Deshaan Avenger
            new Entry(184208638, LastGasp, Place.SameLane, "Mournhold Guardian", 2, 1, false, "1B179B25EBFE2DED8E13274246717BBD8B0CF9B0BFC7D898188C7DFF75AB1755"), // Sadras Agent
            new Entry(2115746825, LastGasp, Place.SameLane, "Durzog", 4, 4, false, "FC1D2E5D333BE5AE2A563207B082D42F418DDF92F5FD8A55ABA8FD7254A948C0"), // Tazkad the Packmaster
            new Entry(97694117, LastGasp, Place.EachLane, "Rallying Stormcloak", 2, 2, false, "2BB92474A1424175A5EA39BA537E7B8C722F4348BCC3563B929F00841DC8F8CD"), // Inspiring Stormcloak
            new Entry(406806594, EffectTriggerType.Veteran, Place.SameLane, "Recruit", 1, 1, true, "EA7F17BD4669AED7E8CEEF4CD9F7CAB2762323257931DCEC256C9337AF64584D"), // Inspiring Soldier
            new Entry(423704935, EffectTriggerType.Slay, Place.SameLane, "Risen Dead", 1, 1, true, "D5D804ED0893B7F2F0ED784BA587760F04C034CB7A3651669AA3CBBD678954CE"), // Ancestral Dead
            new Entry(398739133, Play, Place.FillChosenLane, "Imperial Grunt", 1, 1, false, "3C87AD840BED7B582491347016474795382E39620E37FE4B89B0158BB6299D13"), // Imperial Reinforcements
            new Entry(-1603644698, Play, Place.FillChosenLane, "Imperial Grunt", 1, 1, false, "C2DD06F4FF32D51643633FE74CB81E584D1BBA4270C1B5EDA0551041A23FBC7D"), // Imperial Reinforcements
        };
        private static readonly Dictionary<Card, object> owned = new Dictionary<Card, object>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Place PlaceOf(int id) { return Get(id).Place; }
        public static EffectTriggerType TriggerOf(int id) { return Get(id).Trigger; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited board-token ID: " + id); return e; }
        private static Entry Find(int id) { foreach (Entry e in entries) if (e.Id == id) return e; return null; }

        public static int TokenId(GameConfiguration config, int id)
        {
            Entry e = Get(id); Card card = config.FindCardById(id);
            List<int> matches = new List<int>();
            if (e.Referenced)
            {
                if (card.CardReferences == null || card.CardReferences.Count != 1) throw new NotSupportedException("Original token reference count differs: " + id);
                matches.Add(card.CardReferences[0].resourceId);
            }
            else
                foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                    if (c.Name == e.Token && !matches.Contains(c.ResourceId.Id)) matches.Add(c.ResourceId.Id);
            if (matches.Count != 1) throw new NotSupportedException("Board token identity is not unique: " + id + " / " + e.Token);
            Card token = config.FindCardById(matches[0]);
            CardDefinition definition = GameManager.Instance.LookUpCardDefinition(matches[0]);
            if (token == null || token.Name != e.Token || definition == null || !definition.Subtypes.Contains(CardSubtype.Creature)
                || Stat(token, AttributeType.Attack) != e.Attack || Stat(token, AttributeType.MaxHealth) != e.Health)
                throw new NotSupportedException("Board token metadata differs from text: " + id + " / " + e.Token);
            return matches[0];
        }
        private static int Stat(Card card, AttributeType name)
        {
            IntAttribute a = card.EntityAttributes.Find(delegate(SPTESL.Attribute x) { return x.Name == name; }) as IntAttribute;
            return a == null ? 0 : a.Value;
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
                    throw new NotSupportedException("Original board-token creator metadata differs: " + e.Id);
                int token = TokenId(config, e.Id);
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
                            && OriginalGraphDescriber.Ability(ability) == OriginalGraphDescriber.Ability(CreateAbility(e, token));
                    }
                    else
                    {
                        TriggeredEffect trigger = graph as TriggeredEffect;
                        valid = trigger != null && shared && (card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                            && card.TriggeredEffects != null && card.TriggeredEffects.Count == 1 && card.TriggeredEffects[0] == trigger
                            && OriginalGraphDescriber.Trigger(trigger) == OriginalGraphDescriber.Trigger(CreateTrigger(e, token));
                    }
                    if (!valid) throw new NotSupportedException("Owned original board-token graph changed or gained extra behavior: " + e.Id);
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
                    int token = TokenId(config, e.Id);
                    if (e.Trigger == Play)
                    {
                        PlayCardAbility ability = CreateAbility(e, token);
                        card.ActionPlayInformation = new List<PlayCardAbility> { ability };
                        card.TriggeredEffects = new List<TriggeredEffect>();
                        owned.Add(card, ability);
                    }
                    else
                    {
                        TriggeredEffect trigger = CreateTrigger(e, token);
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

        private static PlayCardAbility CreateAbility(Entry e, int token)
        {
            EffectGroup group = new EffectGroup(); group.Effects.Add(Body(e, token));
            PlayCardAbility ability = new PlayCardAbility { EffectGroup = group, PlayTargetingType = PlayTargetingType.RequiredTarget };
            DecisionConditionalExpression lane = new DecisionConditionalExpression { DecisionType = DecisionType.Lane };
            lane.LaneConditionalExpressions.Add(new LaneConditionals.LaneOwner { OwnedBy = LaneConditionals.LaneOwner.RelationshipType.Friends, PlayerRole = PlayerRole.Self });
            ability.TargetSelectors.Add(lane);
            return ability;
        }
        private static TriggeredEffect CreateTrigger(Entry e, int token)
        {
            TriggeredEffect trigger = new TriggeredEffect { TriggerType = e.Trigger, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            trigger.EffectGroup.Effects.Add(Body(e, token));
            return trigger;
        }
        private static Effect Body(Entry e, int token)
        {
            Effect effect = new Effect();
            switch (e.Place)
            {
                case Place.SameLane: effect.PlayerActions.Add(Create(token, new InsertPosition { Type = InsertPosition.LaneChoiceType.Relative, Comparison = InsertPosition.PositionComparison.SameLane, Lhs = CardRole.Self, InsertionType = CardInsertionType.Bottom }, false)); break;
                case Place.OtherLane: effect.PlayerActions.Add(Create(token, new InsertPosition { Type = InsertPosition.LaneChoiceType.Relative, Comparison = InsertPosition.PositionComparison.OtherLane, Lhs = CardRole.Self, InsertionType = CardInsertionType.Bottom }, false)); break;
                case Place.EachLane:
                    effect.PlayerActions.Add(Create(token, new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Bottom }, false));
                    effect.PlayerActions.Add(Create(token, new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Right, InsertionType = CardInsertionType.Bottom }, false)); break;
                case Place.FillChosenLane: effect.PlayerActions.Add(Create(token, new InsertPosition { Type = InsertPosition.LaneChoiceType.Selected, InsertionType = CardInsertionType.Bottom }, true)); break;
            }
            return effect;
        }
        private static PlayerEffectAction Create(int token, InsertPosition position, bool fill)
        {
            PlayerEffectAction create = new PlayerEffectAction { Action = new PlayerActions.CreateToken { CardReference = new EditorCardReference(token), CreateInZoneType = GameZoneType.Board,
                NumTokens = new ActionValueData(fill ? 0 : 1), FillLane = fill, InsertPosition = position } };
            create.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Self });
            return create;
        }
    }
}
