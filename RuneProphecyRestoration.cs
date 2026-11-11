using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed scheduling around surviving native movement, draw, commands,
    // turn-control histories and decline execution. This is not recovered server code.
    public static class RuneProphecyRestoration
    {
        private static readonly Dictionary<TurnState, ServerCard> windows = new Dictionary<TurnState, ServerCard>();

        public static void ValidateRunePlayer(PlayerState player)
        {
            if (player.QueryAttribute(AttributeType.GainHealthFromRuneBreak).Value != 0)
                throw new NotSupportedException("Rune-break health substitution is not restored.");
        }

        // Two kinds of card-draw interrupt are restored:
        //  - a rune break: the top card of the deck is drawn; a Prophecy is offered to play for free;
        //  - DrawAndPlayCard (Fate Weaver: the top card of your deck; Assassin Beetle: the hand card chosen as the Summon's
        //    target): the first card matching the selectors (in their sort order) is drawn, or offered in the Prophecy window
        //    when it is a Prophecy (from the deck or the hand).
        private static bool IsRune(ServerEffectUtilities.CardInterruptContext c)
        {
            return c.FromRuneBreak && c.RequireProphecyToPlay && c.IgnoreManaCostRestrictions && !c.PlaySpecificCard && c.SpecificCard == null
                && (c.Selectors == null || c.Selectors.Count == 0);
        }

        private static bool IsSelected(ServerEffectUtilities.CardInterruptContext c)
        {
            return !c.FromRuneBreak && c.RequireProphecyToPlay && !c.PlaySpecificCard && c.SpecificCard == null && c.Selectors != null && c.Selectors.Count > 0;
        }

        private static void ValidateContext(Simulation sim, ServerEffectUtilities.CardInterruptContext context)
        {
            if (context == null || (!IsRune(context) && !IsSelected(context)))
                throw new NotSupportedException("This card-draw interrupt kind is not restored."
                    + (context == null ? "" : " [rune " + context.FromRuneBreak + ", prophecy " + context.RequireProphecyToPlay + ", ignoreCost " + context.IgnoreManaCostRestrictions
                    + ", specific " + context.PlaySpecificCard + "/" + (context.SpecificCard == null ? "none" : context.SpecificCard.CardId + "@" + context.SpecificCard.CurrentGameZoneName)
                    + ", selectors " + (context.Selectors == null ? 0 : context.Selectors.Count) + (context.Selectors == null ? "" : ":" + string.Join(",", context.Selectors.ConvertAll(delegate(ICardConditionalExpression x) { return x.GetType().Name + FullSerializer.fsJsonPrinter.CompressedJson(Serialize(x)); }).ToArray()))
                    + ", sort " + (context.SelectorSort == null ? "none" : context.SelectorSort.Method + "/" + context.SelectorSort.Order + "/" + context.SelectorSort.SortAttribute) + "]"));
            PlayerState player = sim.Players.Find(delegate(PlayerState p) { return p.NetId == context.PlayerId; });
            if (player == null) throw new InvalidOperationException("Rune draw names an unregistered player.");
            if (context.FromRuneBreak) ValidateRunePlayer(player);
            if (sim.CardCatalog.FindCardCount(GameZoneName.Prophecy, player.NetId) != 0)
                throw new NotSupportedException("A rune interrupt cannot overwrite an occupied native prophecy zone.");
            List<ServerCard> deck = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.Deck);
            // Reject missing rules before pushing control, removing the pending
            // draw or touching the deck. Nonprophecy draws do not execute card rules.
            if (deck.Count > 0 && deck[0].QueryAttribute(AttributeType.Prophecy).Value > 0 && !RuleImporter.IsSupported(deck[0].CardId))
                RuleImporter.NoteMetadataOnlyUse(deck[0].CardId, "prophecy");
        }

        // DrawAndPlayCard's card: the first one matching its selectors, in its sort order (as the native DrawAndPlayMultipleCards does).
        private static ServerCard Pick(Simulation sim, ServerEffectUtilities.CardInterruptContext c)
        {
            List<ServerCard> list = CardConditionals.EvaluateSelectorsOnServerCardList(c.Selectors, sim.CardCatalog.GatherCardList(delegate(ServerCard x) { return true; }),
                c.CardArguments, c.PlayerArguments, c.TriggerArguments);
            if (c.SelectorSort != null) CardListSort.SortList(list, c.SelectorSort, sim.CardCatalog, c.CardArguments, c.PlayerArguments, c.TriggerArguments, sim.RandomGenerator);
            return list.Count > 0 ? list[0] : null;
        }

        // Puts a Prophecy in the Prophecy zone and opens its play-or-keep window.
        private static void Offer(Simulation sim, TurnState turn, ServerEffectUtilities.TriggerGameState state, PlayerState player, ServerCard card, int assetHash)
        {
            GameZoneOperationResult result = ServerCardCatalog.MoveCard(state,
                new GameZoneId(player.NetId, card.CurrentGameZoneType), new GameZoneId(player.NetId, GameZoneType.Prophecy),
                card.InstanceId, 0, CardInsertionType.Bottom, ServerCardCatalog.MoveOperationType.Untyped);
            if (result != GameZoneOperationResult.Succeeded)
                throw new InvalidOperationException("Native transfer to the prophecy zone failed: " + result);
            HistoryItem draw = new HistoryItem { Type = HistoryType.ActionMoveCards, AssetHash = assetHash, UseProphecyBanner = true };
            draw.AffectedCardsArray.Add(card.HistoryCreate_FullStateCard(sim.CardCatalog));
            sim.HistoryLog.PlayerDiffLogger.DiffDeckCountsAndAddToHistoryItem(sim.Players, sim.CardCatalog, draw);
            sim.HistoryLog.AddInstant(draw);
            windows.Add(turn, card);
        }

        private static FullSerializer.fsData Serialize(object x)
        {
            FullSerializer.fsData data;
            new FullSerializer.fsSerializer().TrySerialize(x.GetType(), x, out data);
            return data ?? new FullSerializer.fsData("?");
        }

        public static bool TryPushInterrupt(Simulation sim, TurnState parent)
        {
            if (parent.EffectTriggerQueues.CardDrawInterrupt.Count == 0) return false;
            ServerEffectUtilities.CardInterruptContext context = parent.EffectTriggerQueues.CardDrawInterrupt[0];
            ValidateContext(sim, context);
            int index = sim.Players.FindIndex(delegate(PlayerState p) { return p.NetId == context.PlayerId; });
            TurnState child = new TurnState {
                ActivePlayerIndex = index, TurnPlayerIndex = parent.TurnPlayerIndex,
                Phase = TurnPhase.Interrupt, CustomUICards = parent.CustomUICards,
                CurrentSequence = new SequenceData { Type = SequenceType.CardInterrupt, CardInterruptContext = context }
            };
            // Native Push/Pop provides balanced EventSetTurnControl and suspends
            // the attacker's clock. The original parent sequence remains suspended.
            sim.PushTurnState(child);
            parent.EffectTriggerQueues.CardDrawInterrupt.RemoveAt(0);
            parent.CurrentCommandItem = null;
            return true;
        }

        public static Simulation.SequenceStatus ProcessCardInterrupt(Simulation sim, TurnState turn)
        {
            SequenceData sequence = turn.CurrentSequence;
            if (sequence == null || sequence.Type != SequenceType.CardInterrupt)
                throw new InvalidOperationException("Rune draw requires its native card-interrupt sequence.");
            PlayerState player = sim.Players[turn.ActivePlayerIndex];
            ServerEffectUtilities.TriggerGameState state = DecisionRestoration.State(sim, turn);
            if (sequence.Stage == 0)
            {
                ValidateContext(sim, sequence.CardInterruptContext);
                // The rune counter/history was already emitted by CheckForRuneBreaks.
                // Draw movement has its own balanced native event and does not emit
                // a second rune break. The original protocol needs an event boundary.
                sequence.EventStarted = true;
                sim.HistoryLog.StartEvent(HistoryType.EventTrigger, player.NetId,
                    CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), sequence.CardInterruptContext.AssetHash);
                ServerEffectUtilities.CardInterruptContext context = sequence.CardInterruptContext;
                int handCount = sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
                if (!context.FromRuneBreak)
                {
                    ServerCard pick = Pick(sim, context);
                    if (pick != null && pick.OwnerNetId == player.NetId)
                    {
                        bool prophecy = pick.QueryAttribute(AttributeType.Prophecy).Value > 0;
                        if (pick.CurrentGameZoneType == GameZoneType.Deck && (!prophecy || handCount >= player.FindHandSize()))
                            PlayerActions.DrawCards.StaticExecute(state, player, 1, PlayerActions.DrawCards.DrawDeck.Self,
                                ServerCardCatalog.MoveOperationType.Untyped, false, false, context.AssetHash);
                        else if (prophecy && (pick.CurrentGameZoneType == GameZoneType.Deck || pick.CurrentGameZoneType == GameZoneType.Hand))
                            Offer(sim, turn, state, player, pick, context.AssetHash);
                    }
                    return Simulation.SequenceStatus.Processing;
                }
                List<ServerCard> deck = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.Deck);
                if (deck.Count == 0 || handCount >= player.FindHandSize() || deck[0].QueryAttribute(AttributeType.Prophecy).Value <= 0)
                {
                    // Preserves native hidden-card history, full-hand reveal/burn,
                    // empty-deck rune/health loss and original draw event triggers.
                    PlayerActions.DrawCards.StaticExecute(state, player, 1, PlayerActions.DrawCards.DrawDeck.Self,
                        ServerCardCatalog.MoveOperationType.Untyped, false, false, sequence.CardInterruptContext.AssetHash);
                }
                else
                {
                    ServerCard card = deck[0];
                    Offer(sim, turn, state, player, card, context.AssetHash);
                    PlayerActions.SetAttribute.StaticExecute(sim.HistoryLog, player, AttributeType.PropheciesFromRuneBreak,
                        player.QueryAttribute(AttributeType.PropheciesFromRuneBreak).Value + 1);
                    ServerEffectUtilities.TriggerEffectEvent(state,
                        new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenProphecyDrawnOffRuneBreak, card, sim.Players));
                }
                return Simulation.SequenceStatus.Processing;
            }
            ServerCard offered;
            if (windows.TryGetValue(turn, out offered) && offered.CurrentGameZoneName == GameZoneName.Prophecy)
                turn.CurrentCommandItem = sim.CardCatalog.GenerateProphecyCardCommandItem(player, sim.Players[1 - turn.ActivePlayerIndex],
                    sim.Players, offered, true);
            return Simulation.SequenceStatus.Complete;
        }

        // Called only after sequence/effect/discard queues have settled. The
        // parent loop must not pop a prophecy while the human command is pending.
        public static bool TryFinishInterrupt(Simulation sim, TurnState turn)
        {
            if (turn.CurrentCommandItem != null)
            {
                sim.GetPlayerDecision(turn);
                return false;
            }
            if (turn.CurrentSequence != null || turn.TriggerDecision != null
                || turn.EffectTriggerQueues.Active.Count != 0 || turn.EffectTriggerQueues.Pending.Count != 0
                || turn.EffectTriggerQueues.After.Count != 0 || turn.EffectTriggerQueues.CardDrawInterrupt.Count != 0) return false;
            ServerCard offered;
            if (windows.TryGetValue(turn, out offered))
            {
                if (offered.CurrentGameZoneName == GameZoneName.Prophecy)
                    throw new InvalidOperationException("Prophecy finished without playing or returning its offered card.");
                ServerEffectUtilities.TriggerGameState state = DecisionRestoration.State(sim, turn);
                sim.HistoryLog.StartEvent(HistoryType.EventTrigger, sim.Players[turn.ActivePlayerIndex].NetId,
                    offered.NetUid, CardNetUid.GetInvalid(), -1);
                try
                {
                    // Native enchantments explicitly support EndOfProphecy expiry.
                    foreach (ServerCard card in sim.CardCatalog.GatherDynamicCardList())
                        card.CullExpiredEnchantments(CullEnchantmentReason.EndOfProphecy, state);
                }
                finally { sim.HistoryLog.EndEvent(); }
                windows.Remove(turn);
                if (HasQueuedEffects(turn)) return false;
            }
            return true;
        }

        public static bool HasQueuedEffects(TurnState turn)
        {
            ServerEffectUtilities.TriggerQueues queues = turn.EffectTriggerQueues;
            return queues.Active.Count != 0 || queues.Pending.Count != 0
                || queues.After.Count != 0 || queues.CardDrawInterrupt.Count != 0;
        }

        public static void Clear(Simulation sim)
        {
            if (sim == null) return;
            foreach (TurnState turn in sim._turnStateStack) windows.Remove(turn);
        }
    }
}
