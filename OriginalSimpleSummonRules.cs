using System;
using System.Collections.Generic;
using SPTESL;
namespace OfflineEngine
{
    // Reconstructed absent per-card Summon wrappers using exact installed text and intact native DrawCards.
    public static class OriginalSimpleSummonRules
    {
        private static readonly Dictionary<int,string> fingerprints=new Dictionary<int,string> {
            { -2120013104, "5F53907105ED4D06C87441AD5FE3FB68AFCBD23CA32A767D894043419587CD4B" }, // Brotherhood Assassin
            { 1405118346, "591A275304146433378980183E8E2A63D6CCE35CA2C1E8A8F769390ABB0D5104" }, // Fate's Witness
            { -1728909427, "CE32CC1252754EA123C265F3C99B4752C3065D944061E8AD747433841709B3B3" }, // Spider Worker
            { -595080579, "9423826A637EBD41F5C70FD32FDA2147A775DDABF8F57BB9EAE388BD07D5A547" }, // Vigilant Giant
            { -1762371288, "4330F87953FB2BF1C2E18BFB6DB2CC6493FADD9B171A87C2922BD563785C983D" }, // Wicked Spiderling
            { -434140922, "FC6FCBD091E7BF60D2135A47509DCC42659BBD7D31D729787626F4F859E8F1B6" }, // Blue Grummite
            { 663927501, "896C944DAA4EF9BB9CC268EC28C2388DAA083F06DB05795DFA52F82CEFD27C98" }, // Red Grummite
            { 1819434587, "7485F8484499C4508F8DDBACF7DBE42D1294EBEDF8834810FF8073D2549FFC88" }, // Tome of Alteration
            { -284862378, "1718C3F09487B34380BFB4E1621A09617B5905172A88BF2526627459F8E5007F" }, // Indoril Mastermind
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
                    PlayerEffectAction action=new PlayerEffectAction { HashId=-1,
                        Action=new PlayerActions.DrawCards { NumCards=new ActionValueData(1), DrawFrom=PlayerActions.DrawCards.DrawDeck.Self,
                            OperationType=ServerCardCatalog.MoveOperationType.DefaultDraw, StopAtHandLimit=false, IgnoreDeckingRule=false } };
                    action.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check=PlayerConditionals.PlayerRoleType.Requirement.Is, Role=PlayerRole.Self });
                    Effect effect=new Effect(); effect.PlayerActions.Add(action);
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
            if(effect.CardActions.Count!=0 || effect.PlayerActions.Count!=1 || effect.ExecuteValueRequirements.Count!=0 || effect.ExecuteCardRequirements.Count!=0
                || effect.ExecutePlayerRequirements.Count!=0 || effect.ExecuteLaneRequirements.Count!=0)Reject();
            PlayerEffectAction action=effect.PlayerActions[0]; PlayerActions.DrawCards draw=action.Action as PlayerActions.DrawCards;
            PlayerConditionals.PlayerRoleType selector=action.Selectors.Count==1?action.Selectors[0] as PlayerConditionals.PlayerRoleType:null;
            if(action.HashId!=-1 || draw==null || draw.DrawFrom!=PlayerActions.DrawCards.DrawDeck.Self || draw.OperationType!=ServerCardCatalog.MoveOperationType.DefaultDraw
                || draw.StopAtHandLimit || draw.IgnoreDeckingRule || draw.NumCards==null || draw.NumCards.ValueMethod!=ActionValueMethod.Constant
                || draw.NumCards.ConstantValue!=1 || draw.NumCards.ConstantMultiplier!=0 || draw.NumCards.MinValue!=0 || draw.NumCards.MaxValue!=0
                || draw.NumCards.Selectors.Count!=0 || selector==null || selector.Check!=PlayerConditionals.PlayerRoleType.Requirement.Is || selector.Role!=PlayerRole.Self)Reject();
        }
        private static void Reject(){throw new NotSupportedException("Owned original Summon DrawCards graph changed or gained additional behavior.");}
    }
}


