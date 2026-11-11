using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Reconstruction of missing server choice orchestration using the native TargetCemetery protocol.
    // Native CustomTargetCemeteryController uses REAL cemetery UIDs, then sends
    // the index in GatherValidTargetCemeteryCards' list. DisplayCards alone keeps
    // that list exactly aligned with TriggerGameState.CustomUiCards.
    public static class OfflineConsumeChoices
    {
        // Snapshot before native Consume (including its reactions). TriggerValue belongs to
        // this resolution; the reusable graph never holds mutable per-play state.
        public sealed class ConsumeValueReward : ICardAction
        {
            public AttributeType Attribute;
            public TriggeredEffect Reward;
            public TriggeredEffect HealthReward;
            public CardFxAction GetFxActionType() { return CardFxAction.Consume; }
            public void Execute(CardActionArguments arguments)
            {
                var state = arguments.TriggerGameState;
                ServerCard source = arguments.CardArguments.SelfCard;
                ServerCard food = arguments.CardArguments.GetEventRoleCard(CardRole.CustomUITarget);
                if (food == null || food.CurrentGameZoneType != GameZoneType.Cemetery) return;
                int value = food.QueryAttribute(Attribute).Value;
                int health = food.QueryAttribute(AttributeType.MaxHealth).Value;
                new CardActions.Consume().Execute(arguments);
                if (food.CurrentGameZoneType != GameZoneType.Purgatory) return;
                var context = new ServerEffectUtilities.TriggerContext {
                    EffectOwner = source, TriggeredEffect = Reward, EffectIndex = -1,
                    ActingLaneIndex = source.CurrentLaneIndex, ActingLaneIndexType = (int)source.CurrentLaneType,
                    TriggerEvent = new ServerEffectUtilities.TriggerEvent(Reward.TriggerType, source, food, state.Players, value)
                };
                if (Reward.ClientTargetingType == PlayTargetingType.NoTarget)
                    ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(state, context, null);
                else state.TriggerQueues.Active.Enqueue(context);
                if (HealthReward != null) {
                    context.TriggeredEffect = HealthReward;
                    context.TriggerEvent = new ServerEffectUtilities.TriggerEvent(HealthReward.TriggerType, source, food, state.Players, health);
                    ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(state, context, null);
                }
            }
        }
        // Pilfer accounting belongs to the outer native trigger once, not each custom-UI phase.
        public sealed class BeginChoice : ICardAction
        {
            public TriggeredEffect Choice;
            public CardFxAction GetFxActionType() { return CardFxAction.BuildCustomUiCards; }
            public void Execute(CardActionArguments arguments)
            {
                var source = arguments.CardArguments.SelfCard;
                arguments.TriggerGameState.TriggerQueues.Active.Enqueue(new ServerEffectUtilities.TriggerContext {
                    EffectOwner = source, TriggeredEffect = Choice, EffectIndex = -1,
                    ActingLaneIndex = source.CurrentLaneIndex, ActingLaneIndexType = (int)source.CurrentLaneType,
                    TriggerEvent = new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, source, arguments.TriggerGameState.Players)
                });
            }
        }
        public static bool Supported(ServerEffectUtilities.TriggerContext context)
        { return context.EffectOwner != null && context.TriggeredEffect.CustomUiType == CustomCardUiType.TargetCemetery; }

        // Reconstructed two-step ability: consume first, then ask for the board target.
        // Keep the consumed instance in EventTarget; native custom-stage DecisionTarget is null.
        public sealed class QueueReward : ICardAction
        {
            public TriggeredEffect Reward;
            public CardFxAction GetFxActionType() { return CardFxAction.BuildCustomUiCards; }
            public void Execute(CardActionArguments arguments)
            {
                var state = arguments.TriggerGameState;
                ServerCard source = arguments.CardArguments.SelfCard;
                ServerCard consumed = arguments.CardArguments.GetEventRoleCard(CardRole.CustomUITarget);
                if (consumed == null || consumed.CurrentGameZoneType != GameZoneType.Purgatory) return;
                state.TriggerQueues.Active.Enqueue(new ServerEffectUtilities.TriggerContext {
                    EffectOwner = source, TriggeredEffect = Reward, EffectIndex = -1,
                    ActingLaneIndex = source.CurrentLaneIndex, ActingLaneIndexType = (int)source.CurrentLaneType,
                    TriggerEvent = new ServerEffectUtilities.TriggerEvent(Reward.TriggerType, source, consumed, state.Players)
                });
            }
        }

        public sealed class BuildCemeteryChoices : ICardAction
        {
            public CardFxAction GetFxActionType() { return CardFxAction.BuildCustomUiCards; }
            public void Execute(CardActionArguments arguments)
            {
                var state = arguments.TriggerGameState;
                if (state.CustomUiCards == null) throw new InvalidOperationException("No custom display storage.");
                state.CustomUiCards.Clear();
                foreach (ServerCard card in arguments.ValidTargetList)
                {
                    if (card.CurrentGameZoneType != GameZoneType.Cemetery) continue;
                    state.CustomUiCards.Add(new CustomDisplayCard {
                        HistoryCard = card.HistoryCreate_FullStateCard(state.CardCatalog),
                        CardInstanceId = card.InstanceId, CardOrder = state.CustomUiCards.Count
                    });
                }
            }
        }

        // Any custom-card UI (original graphs) after its main group ran: one EffectGroupChoice command carrying the UI type,
        // the built display cards and the number of choices (display cards or choice groups). Decline is offered only
        // where native DeterminePlayerCustomUIChoice accepts it (TargetCemetery, TargetOpponentCemetery, TargetDeck).
        public static CardCommandItem GeneralCommands(ServerCardCatalog catalog, ServerEffectUtilities.TriggerContext context, List<CustomDisplayCard> display)
        {
            TriggeredEffect e = context.TriggeredEffect;
            CustomCardUiType type = e.CustomUiType;
            bool optional = type == CustomCardUiType.TargetCemetery || type == CustomCardUiType.TargetOpponentCemetery || type == CustomCardUiType.TargetDeck;
            CardCommandItem item = new CardCommandItem {
                CommandList = new CardCommandList { ListUid = ++catalog._commandListUuid, MustSelectCommand = !optional },
                OpponentCommandList = new OpponentCardCommandList()
            };
            int choices = Math.Max(display == null ? 0 : display.Count, e.CustomUiEffectGroups == null ? 0 : e.CustomUiEffectGroups.Count);
            if (choices == 0) { item.CommandList.MustSelectCommand = false; return item; }
            CardCommandState command = new CardCommandState {
                CardNetUid = context.EffectOwner.NetUid.Id,
                ValidCardCommandMask = (int)CardCommand.EffectGroupChoice | (optional ? (int)CardCommand.DeclineToPlay : 0),
                CustomCardUiEnum = (int)type,
                NumActionChoices = choices
            };
            if (display != null) foreach (CustomDisplayCard choice in display) command.DisplayCards.Add(choice.HistoryCard);
            item.CommandList.ValidCommands.Add(command);
            item.OpponentCommandList.ListUid = item.CommandList.ListUid;
            return item;
        }

        public static CardCommandItem Commands(ServerCardCatalog catalog, ServerEffectUtilities.TriggerContext context, List<CustomDisplayCard> display)
        {
            if (!Supported(context) || !context.DidMainEffectsRun) throw new InvalidOperationException("Not a cemetery selection phase.");
            CardCommandItem item = new CardCommandItem {
                CommandList = new CardCommandList { ListUid = ++catalog._commandListUuid, MustSelectCommand = false },
                OpponentCommandList = new OpponentCardCommandList()
            };
            if (display == null || display.Count == 0) return item;
            CardCommandState command = new CardCommandState {
                CardNetUid = context.EffectOwner.NetUid.Id,
                ValidCardCommandMask = (int)CardCommand.EffectGroupChoice | (int)CardCommand.DeclineToPlay,
                CustomCardUiEnum = (int)CustomCardUiType.TargetCemetery,
                NumActionChoices = display.Count
            };
            foreach (CustomDisplayCard choice in display) command.DisplayCards.Add(choice.HistoryCard);
            item.CommandList.ValidCommands.Add(command);
            return item;
        }

        // Invoke BEFORE ordinary validation rejects a nonzero CustomChoice.
        // Does not broaden other custom UI kinds. Ordinary NoCommand validation
        // remains applicable to the native decline packet (all IDs zero).
        public static bool Validate(Decision d, CardCommandList list)
        {
            if (d == null || list == null || d.Type != CcgRpc.DecisionType.CustomChoice || d.CommandUid != list.ListUid) return false;
            if (d.ActorCardNetUid != 0 || d.TargetCardNetUid != 0 || d.EquipCardNetUid != 0 || d.LaneTarget != BoardLaneTarget.None ||
                d.PositionIndex != 0 || d.LaneIndex != 0 || d.DiscardCardNetUids.Count != 0) return false;
            // Every custom-card UI (original graphs): the choice indexes the offered display cards or choice groups.
            foreach (CardCommandState c in list.ValidCommands)
                if (list.ValidCommands.Count == 1 && c.CustomCardUiEnum != (int)CustomCardUiType.None &&
                    (c.ValidCardCommandMask & (int)CardCommand.EffectGroupChoice) != 0)
                {
                    if (c.CustomCardUiEnum == (int)CustomCardUiType.TargetCemetery)
                        return d.CustomChoice >= 0 && d.CustomChoice < c.NumActionChoices && d.CustomChoice < c.DisplayCards.Count;
                    int limit = Math.Max(c.NumActionChoices, c.DisplayCards.Count);
                    return d.CustomChoice >= 0 && (d.CustomChoice < limit || (limit == 0 && d.CustomChoice == 0));
                }
            return false;
        }

        // Also invoke in catalog/player overload after structural validation.
        public static bool ValidateLive(Decision d, CardCommandList list, ServerCardCatalog catalog, PlayerState player)
        {
            if (!Validate(d, list) || catalog == null || player == null) return false;
            foreach (CardCommandState c in list.ValidCommands)
                if (c.CustomCardUiEnum != (int)CustomCardUiType.TargetCemetery) return true;
                else
                {
                    ServerCard chosen = catalog.FindDynamicCard(new CardNetUid(c.DisplayCards[d.CustomChoice].CardNetUid));
                    return chosen != null && chosen.OwnerNetId == player.NetId && chosen.CurrentGameZoneType == GameZoneType.Cemetery;
                }
            return false;
        }

        public static Decision Choose(CardCommandList list)
        {
            if (list == null) return null;
            foreach (CardCommandState c in list.ValidCommands)
                if (c.CustomCardUiEnum != (int)CustomCardUiType.None && (c.ValidCardCommandMask & (int)CardCommand.EffectGroupChoice) != 0
                    && (c.CustomCardUiEnum != (int)CustomCardUiType.TargetCemetery || c.NumActionChoices > 0))
                    return new Decision { Type = CcgRpc.DecisionType.CustomChoice, CommandUid = list.ListUid,
                        CustomChoice = 0 };
            return null;
        }
    }
}
