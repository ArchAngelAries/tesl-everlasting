using System;
using System.Collections.Generic;
using SPTESL;
namespace OfflineEngine
{
    // Reconstructed absent per-card Summon wrappers using exact installed text and intact native Heal.
    public static class OriginalHealingSummonRules
    {
        private static readonly Dictionary<int,string> fingerprints=new Dictionary<int,string> {
            { -439109688, "F26EB73192378C2B31E57F1ED71E23E13DD6B35A122B77BFEA311427C88A1153" }, // Dark Harvester; 4 health
            { -1117244397, "76AE126A56A20B57BB16926546AA5F1BB34D74D3BA78251453E9225BA17F1F95" }, // Dark Harvester; 4 health
            { -1395960197, "A752D022B85B892065B40EE923D70676F2DDAEA96D618366DDF8E0A026191F51" }, // Priest of the Moons; 2 health
            { 848305548, "4B131A4F35CCDC3B4C0A726B1D1271A59661B2D472DAFFF176F69F06E6AE5D14" }, // Riften Lawkeeper; 4 health
            { 368220686, "D67B3092B76C1214B20202EF14F70B5A7F10A98D1F9CD73DBFA0555A96C423B8" }, // Knight of the Hour; 3 health
            { 1078595393, "22036580BE7279DAB56F8890069E9B83AF8D34D1F984C5E7DD077702A92798EA" }, // Blackreach Rebuilder; 2 health
        };
        private static readonly Dictionary<Card,TriggeredEffect> owned=new Dictionary<Card,TriggeredEffect>();
        public static bool IsOriginalId(int id){return fingerprints.ContainsKey(id);}
        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).
        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }

        private static List<Card> ValidateRules(GameConfiguration config)
        {
            if(config==null)throw new ArgumentNullException("config");
            List<Card> result=new List<Card>();
            foreach(KeyValuePair<int,string> entry in fingerprints) {
                Card card=config.FindCardById(entry.Key);
                if(card==null || OriginalCardRules.Fingerprint(card)!=entry.Value)throw new NotSupportedException("Original Summon metadata differs: "+entry.Key);
                TriggeredEffect trigger;
                if(owned.TryGetValue(card,out trigger))ValidateOwned(card,trigger);
                else OriginalCardRules.ValidateNoExtraRules(card);
                result.Add(card);
            }
            return result;
        }
        public static int[] Restore(GameConfiguration config)
        {
            List<Card> cards=Validate(config); List<int> ids=new List<int>();
            foreach(Card card in cards) {
                if(!owned.ContainsKey(card)) {
                    CardEffectAction action=new CardEffectAction { HashId=-1,
                        Action=new CardActions.Heal { HealthGain=new ActionValueData(Amount(card.ResourceId.Id)), FullHeal=false, HealToAVDValue=false } };
                    action.Selectors.Add(new CardConditionals.Subtype { Check=CardConditionals.Subtype.Requirement.Is, SubtypeName=CardSubtype.Avatar });
                    action.Selectors.Add(new CardConditionals.Zone { Check=CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask=new GameZoneTypeMask { Bits=GameZoneType.Board } });
                    action.Selectors.Add(new CardConditionals.Relationship { OtherCard=CardRole.Self, Are=CardConditionals.Relationship.RelationshipType.Friends });
                    Effect effect=new Effect(); effect.CardActions.Add(action);
                    EffectGroup group=new EffectGroup(); group.Effects.Add(effect);
                    TriggeredEffect trigger=new TriggeredEffect { TriggerType=EffectTriggerType.Summon, ClientTargetingType=PlayTargetingType.NoTarget, EffectGroup=group };
                    card.TriggeredEffects=new List<TriggeredEffect>{trigger}; owned.Add(card,trigger);
                }
                if(card.Auras==null)card.Auras=new List<Aura>();
                if(card.ActionPlayInformation==null)card.ActionPlayInformation=new List<PlayCardAbility>();
                if(card.PlayRequirements==null)card.PlayRequirements=new PlayCardRequirements();
                if(card.BetrayRequirements==null)card.BetrayRequirements=new PlayCardRequirements();
                if(card.ActivatedAbility==null)card.ActivatedAbility=new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }
        private static void ValidateOwned(Card card,TriggeredEffect trigger)
        {
            bool empty=(card.Auras==null || card.Auras.Count==0) && (card.ActionPlayInformation==null || card.ActionPlayInformation.Count==0)
                && (card.PlayRequirements==null || card.PlayRequirements.PlayerRequirements.Count==0)
                && (card.BetrayRequirements==null || card.BetrayRequirements.PlayerRequirements.Count==0)
                && (card.ActivatedAbility==null || (card.ActivatedAbility.AbilityGroups.Count==0 && card.ActivatedAbility.TargetSelectors.Count==0
                && card.ActivatedAbility.ActivationCardRequirements.Count==0 && card.ActivatedAbility.ActivationPlayerRequirements.Count==0));
            if(!empty || card.TriggeredEffects==null || card.TriggeredEffects.Count!=1 || card.TriggeredEffects[0]!=trigger
                || trigger.TriggerType!=EffectTriggerType.Summon || trigger.ClientTargetingType!=PlayTargetingType.NoTarget || trigger.CustomUiType!=CustomCardUiType.None
                || trigger.TargetSelectors.Count!=0 || trigger.TriggerValueRequirements.Count!=0 || trigger.TriggerCardRequirements.Count!=0
                || trigger.TriggerPlayerRequirements.Count!=0 || trigger.BeforeTargetingCardRequirements.Count!=0 || trigger.BeforeTargetingPlayerRequirements.Count!=0
                || trigger.AfterRuneBreaks || trigger.AreaEffect || trigger.AllowReentrancy || trigger.IgnoredByClientLog
                || trigger.OpponentChooses || trigger.SecretTarget || trigger.ItemUsesWielderAsEffectSource || trigger.OptionalCustomUiType
                || trigger.UseGenericIcon || trigger.CanMergeEffectsWithSelf || trigger.ComplexMergeEffectsWithSelf
                || trigger.CustomUiEffectGroups.Count!=0 || trigger.PostCustomUiEffectGroup==null || trigger.PostCustomUiEffectGroup.Effects.Count!=0
                || trigger.EffectGroup==null || trigger.EffectGroup.Effects.Count!=1)Reject();
            Effect effect=trigger.EffectGroup.Effects[0];
            if(effect.CardActions.Count!=1 || effect.PlayerActions.Count!=0 || effect.ExecuteValueRequirements.Count!=0 || effect.ExecuteCardRequirements.Count!=0
                || effect.ExecutePlayerRequirements.Count!=0 || effect.ExecuteLaneRequirements.Count!=0)Reject();
            CardEffectAction action=effect.CardActions[0]; CardActions.Heal heal=action.Action as CardActions.Heal;
            if(action.HashId!=-1 || heal==null || heal.FullHeal || heal.HealToAVDValue || heal.DrainFxSelector.Count!=0
                || heal.HealthGain==null || heal.HealthGain.ValueMethod!=ActionValueMethod.Constant || heal.HealthGain.ConstantValue!=Amount(card.ResourceId.Id)
                || heal.HealthGain.ConstantMultiplier!=0 || heal.HealthGain.MinValue!=0 || heal.HealthGain.MaxValue!=0 || heal.HealthGain.Selectors.Count!=0
                || action.Selectors.Count!=3)Reject();
            CardConditionals.Subtype subtype=action.Selectors[0] as CardConditionals.Subtype;
            CardConditionals.Zone zone=action.Selectors[1] as CardConditionals.Zone;
            CardConditionals.Relationship owner=action.Selectors[2] as CardConditionals.Relationship;
            if(subtype==null || subtype.Check!=CardConditionals.Subtype.Requirement.Is || subtype.SubtypeName!=CardSubtype.Avatar || subtype.TestSubtypesOfDouble
                || zone==null || zone.Check!=CardConditionals.Zone.Requirement.IsIn || zone.ZoneTypeMask.Bits!=GameZoneType.Board
                || owner==null || owner.OtherCard!=CardRole.Self || owner.Are!=CardConditionals.Relationship.RelationshipType.Friends)Reject();
        }
        public static int Amount(int id)
        {
            switch(id) {
                case -439109688: case -1117244397: case 848305548:return 4;
                case -1395960197: case 1078595393:return 2;
                case 368220686:return 3;
                default:throw new NotSupportedException("Unaudited original healing summon ID: "+id);
            }
        }
        private static void Reject(){throw new NotSupportedException("Owned original Summon Heal graph changed or gained additional behavior.");}
    }
}



