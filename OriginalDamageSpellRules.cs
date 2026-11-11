using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed wrappers from exact installed English text, not recovered per-card server graphs.
    // All damage and its interactions execute in the intact original CardActions.Damage.
    public static class OriginalDamageSpellRules
    {
        private sealed class Entry
        {
            public int Id, Amount, Kind;
            public string Hash;
            public Entry(int id, int amount, int kind, string hash) { Id=id; Amount=amount; Kind=kind; Hash=hash; }
        }
        // Kind 0: creature target. Kind 1: creature/avatar target. Kind 2: all board creatures.
        private static readonly Entry[] entries = new Entry[] {
            new Entry(-1226235166, 3, 1, "680A51465096713CF27F2139B043BBF4A0E61BDDC7318B290C1A1B41D4E45DFB"), // Crushing Blow
            new Entry(1744549196, 3, 1, "8AAE42C63B867929AE297C936E38604D54ABEBF339F257BB68CCFFF1463600D1"), // Crushing Blow
            new Entry(1185654238, 3, 1, "0C055C1F383E77514E3D6C41CFF6831A3448281CC6E55C7EE98A653ABA75E863"), // Crushing Blow
            new Entry(-1865755971, 2, 0, "FF47BBA04D23ADAB9453BBDB2A75FC24A137CF56B95EB753D16B48D9665EBD56"), // Firebolt
            new Entry(1293352672, 2, 0, "ACB75B5A0C88EC68B430B89EE77A1C42F0EC536DD8F5DD96BE66595DE9000BC7"), // Firebolt
            new Entry(-1571294504, 3, 2, "A65BE4018738546894AEEA2945AD8044837AD2AFFE1D3032712C13AC8F9E46EC"), // Ice Storm
            new Entry(-759393216, 4, 1, "6F079BDD6B66153503CD1F41E81DF1AC897C99976B4B562409CAF8B37A4F9388"), // Lightning Bolt
            new Entry(41830377, 4, 1, "43B2BA9118F9D9CED6C98029AA44EF7896C33BE0804B5AC8B4481F0B31D81839"), // Lightning Bolt
        };
        private static readonly Dictionary<Card, PlayCardAbility> owned = new Dictionary<Card, PlayCardAbility>();

        public static bool IsOriginalId(int id)
        {
            foreach(Entry e in entries) if(e.Id==id) return true;
            return false;
        }
        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).
        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }

        private static List<Card> ValidateRules(GameConfiguration config)
        {
            if(config==null) throw new ArgumentNullException("config");
            List<Card> result=new List<Card>();
            foreach(Entry e in entries)
            {
                Card card=config.FindCardById(e.Id);
                if(card==null || OriginalCardRules.Fingerprint(card)!=e.Hash)
                    throw new NotSupportedException("Original damage spell metadata differs: "+e.Id);
                PlayCardAbility ability;
                if(owned.TryGetValue(card,out ability)) ValidateOwned(card,ability,e);
                else OriginalCardRules.ValidateNoExtraRules(card);
                result.Add(card);
            }
            return result;
        }

        public static int[] Restore(GameConfiguration config)
        {
            List<Card> cards=Validate(config); // Atomic complete inventory preflight before mutations.
            List<int> ids=new List<int>();
            for(int i=0;i<cards.Count;i++)
            {
                Card card=cards[i]; Entry e=entries[i];
                if(!owned.ContainsKey(card))
                {
                    PlayCardAbility ability=Create(e);
                    card.ActionPlayInformation=new List<PlayCardAbility>{ability};
                    owned.Add(card,ability);
                }
                if(card.TriggeredEffects==null) card.TriggeredEffects=new List<TriggeredEffect>();
                if(card.Auras==null) card.Auras=new List<Aura>();
                if(card.PlayRequirements==null) card.PlayRequirements=new PlayCardRequirements();
                if(card.BetrayRequirements==null) card.BetrayRequirements=new PlayCardRequirements();
                if(card.ActivatedAbility==null) card.ActivatedAbility=new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        private static PlayCardAbility Create(Entry e)
        {
            CardEffectAction action=new CardEffectAction { HashId=-1,
                Action=new CardActions.Damage { HealthLoss=new ActionValueData(e.Amount), DamageSource=CardRole.Self } };
            if(e.Kind==2) action.Selectors=BoardCreatureSelectors();
            else action.Selectors.Add(new CardConditionals.CardRoleType {
                Check=CardConditionals.CardRoleType.Requirement.Is, Role=CardRole.DecisionTarget });
            Effect effect=new Effect(); effect.CardActions.Add(action);
            EffectGroup group=new EffectGroup(); group.Effects.Add(effect);
            PlayCardAbility ability=new PlayCardAbility { EffectGroup=group,
                PlayTargetingType=e.Kind==2 ? PlayTargetingType.NoTarget : PlayTargetingType.RequiredTarget };
            if(e.Kind!=2)
            {
                DecisionConditionalExpression selector=new DecisionConditionalExpression { DecisionType=DecisionType.Card };
                if(e.Kind==0) selector.CardConditionalExpressions=BoardCreatureSelectors();
                else
                {
                    CardConditionals.SubtypeCount types=new CardConditionals.SubtypeCount {
                        Is=CardConditionals.SubtypeCount.ComparisonType.GreaterThanOrEqualTo, Value=1 };
                    types.Subtypes.Add(CardSubtype.Creature); types.Subtypes.Add(CardSubtype.Avatar);
                    selector.CardConditionalExpressions.Add(types);
                    selector.CardConditionalExpressions.Add(BoardZone());
                }
                ability.TargetSelectors.Add(selector);
            }
            return ability;
        }
        private static CardConditionals.Zone BoardZone()
        { return new CardConditionals.Zone { Check=CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask=new GameZoneTypeMask { Bits=GameZoneType.Board } }; }
        private static List<ICardConditionalExpression> BoardCreatureSelectors()
        { return new List<ICardConditionalExpression> { new CardConditionals.Subtype { Check=CardConditionals.Subtype.Requirement.Is, SubtypeName=CardSubtype.Creature }, BoardZone() }; }

        private static void ValidateOwned(Card card, PlayCardAbility ability, Entry e)
        {
            // Require all other wrappers to remain empty.
            bool empty=(card.TriggeredEffects==null || card.TriggeredEffects.Count==0) && (card.Auras==null || card.Auras.Count==0)
                && (card.PlayRequirements==null || card.PlayRequirements.PlayerRequirements.Count==0)
                && (card.BetrayRequirements==null || card.BetrayRequirements.PlayerRequirements.Count==0)
                && (card.ActivatedAbility==null || (card.ActivatedAbility.AbilityGroups.Count==0 && card.ActivatedAbility.TargetSelectors.Count==0
                && card.ActivatedAbility.ActivationCardRequirements.Count==0 && card.ActivatedAbility.ActivationPlayerRequirements.Count==0));
            if(!empty || card.ActionPlayInformation==null || card.ActionPlayInformation.Count!=1 || card.ActionPlayInformation[0]!=ability
                || ability.PlayTargetingType!=(e.Kind==2?PlayTargetingType.NoTarget:PlayTargetingType.RequiredTarget)
                || ability.CustomUiType!=CustomCardUiType.None || ability.EffectGroup==null || ability.EffectGroup.Effects.Count!=1
                || ability.TargetSelectors==null || ability.TargetSelectors.Count!=(e.Kind==2?0:1)) Reject();
            Effect effect=ability.EffectGroup.Effects[0];
            if(effect.CardActions.Count!=1 || effect.PlayerActions.Count!=0) Reject();
            CardEffectAction action=effect.CardActions[0]; CardActions.Damage damage=action.Action as CardActions.Damage;
            if(action.HashId!=-1 || damage==null || damage.DamageSource!=CardRole.Self || damage.UseItemWielderAsDamageSource || damage.SeparateLogMessages
                || damage.HealthLoss==null || damage.HealthLoss.ValueMethod!=ActionValueMethod.Constant || damage.HealthLoss.ConstantValue!=e.Amount
                || damage.HealthLoss.Selectors.Count!=0 || damage.HealthLoss.ConstantMultiplier!=0 || damage.HealthLoss.MinValue!=0 || damage.HealthLoss.MaxValue!=0) Reject();
            if(e.Kind==2) ValidateCreatureSelectors(action.Selectors);
            else
            {
                CardConditionals.CardRoleType role=action.Selectors.Count==1?action.Selectors[0] as CardConditionals.CardRoleType:null;
                if(role==null || role.Check!=CardConditionals.CardRoleType.Requirement.Is || role.Role!=CardRole.DecisionTarget) Reject();
                DecisionConditionalExpression selector=ability.TargetSelectors[0];
                if(selector.DecisionType!=DecisionType.Card || selector.LaneConditionalExpressions.Count!=0) Reject();
                if(e.Kind==0) ValidateCreatureSelectors(selector.CardConditionalExpressions);
                else
                {
                    if(selector.CardConditionalExpressions.Count!=2) Reject();
                    CardConditionals.SubtypeCount count=selector.CardConditionalExpressions[0] as CardConditionals.SubtypeCount;
                    if(count==null || count.Is!=CardConditionals.SubtypeCount.ComparisonType.GreaterThanOrEqualTo || count.Value!=1
                        || count.Subtypes.Count!=2 || count.Subtypes[0]!=CardSubtype.Creature || count.Subtypes[1]!=CardSubtype.Avatar) Reject();
                    ValidateZone(selector.CardConditionalExpressions[1]);
                }
            }
        }
        private static void ValidateCreatureSelectors(List<ICardConditionalExpression> selectors)
        {
            if(selectors.Count!=2) Reject();
            CardConditionals.Subtype type=selectors[0] as CardConditionals.Subtype;
            if(type==null || type.Check!=CardConditionals.Subtype.Requirement.Is || type.SubtypeName!=CardSubtype.Creature || type.TestSubtypesOfDouble) Reject();
            ValidateZone(selectors[1]);
        }
        private static void ValidateZone(ICardConditionalExpression expression)
        {
            CardConditionals.Zone zone=expression as CardConditionals.Zone;
            if(zone==null || zone.Check!=CardConditionals.Zone.Requirement.IsIn || zone.ZoneTypeMask.Bits!=GameZoneType.Board) Reject();
        }
        private static void Reject() { throw new NotSupportedException("Owned original native damage graph changed or gained extra behavior."); }
    }
}


