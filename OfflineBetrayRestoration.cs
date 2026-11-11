using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Reconstruction of the stripped server Betray orchestration. Shipped native pieces that define the contract:
    //  - Simulation's send path treats a command with CardCommand.BetrayChoice as the Betray offer;
    //  - the client (BoardInputController.Betray_Start/Choice) highlights the offer's ActiveAbilityTargets, shows the
    //    action from DisplayCards[0], and sends DecisionType.Betray with the sacrifice in DiscardCardNetUids, or none to decline;
    //  - GameZoneName.Betray exists, PlayCardHandler accepts plays from it, CardCommand.BetrayPlay exists, and a declined
    //    Betray sequence is expected to complete without a history event.
    // Native GenerateBetrayCardCommandItem and Simulation.ProcessBetray are empty stubs; their behaviour here follows
    // UESP Legends:Betray: "After you play an action with Betray, you may sacrifice a creature to play that action again"
    // at no magicka cost. The replay is a normal native play from the Betray zone and does not offer Betray again.
    public static class OfflineBetrayRestoration
    {
        private sealed class Pending { public ServerCard Action; public bool Replay; }
        private static readonly Dictionary<Simulation, Pending> pending = new Dictionary<Simulation, Pending>();

        public static bool HasPending(Simulation s) { return pending.ContainsKey(s); }

        // Native ProcessSequence regenerates the command list through ServerCardCatalog.GenerateCardCommandItem when a
        // decision completes, so the original offer/replay must come from that generator: this postfix substitutes it.
        public static void GenerateCardCommandItemPostfix(ServerCardCatalog __instance, PlayerState player, ref CardCommandItem __result)
        {
            foreach (KeyValuePair<Simulation, Pending> entry in pending)
                if (Object.ReferenceEquals(entry.Key.CardCatalog, __instance) && entry.Value.Action.OwnerNetId == player.NetId && entry.Key._turnStateStack.Count > 0)
                {
                    CardCommandItem item = Command(entry.Key, entry.Key._turnStateStack.Peek());
                    if (item != null) __result = item;
                    return;
                }
        }
        public static void Clear(Simulation s) { pending.Remove(s); }

        public static List<ServerCard> Sacrifices(Simulation s, PlayerState player)
        {
            return s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return !c.IsDying() && c.HasSubtype(CardSubtype.Creature); }, player.NetId, GameZoneName.CreatureBoard);
        }

        // Called at the end of a resolved native play.
        public static void AfterPlay(Simulation s, TurnState t, ServerCard card, bool fromBetrayZone)
        {
            if (fromBetrayZone) { pending.Remove(s); return; }
            if (t.Phase != TurnPhase.PlayCards || !card.HasSubtype(CardSubtype.Spell) || card.QueryAttribute(AttributeType.Betray).Value <= 0) return;
            if (Sacrifices(s, s.Players[t.ActivePlayerIndex]).Count == 0) return;
            pending[s] = new Pending { Action = card };
        }

        // Command list for a pending offer or replay; null when nothing is pending.
        public static CardCommandItem Command(Simulation s, TurnState t)
        {
            Pending p;
            if (!pending.TryGetValue(s, out p)) return null;
            PlayerState player = s.Players[t.ActivePlayerIndex];
            PlayerState opponent = s.Players.Find(delegate(PlayerState x) { return x.NetId != player.NetId; });
            CardCommandItem item = new CardCommandItem { CommandList = new CardCommandList() };
            item.CommandList.ListUid = ++s.CardCatalog._commandListUuid;
            item.CommandList.MustSelectCommand = true;
            CardCommandState state = new CardCommandState { CardNetUid = p.Action.NetUid.Id };
            if (!p.Replay)
            {
                List<ServerCard> creatures = Sacrifices(s, player);
                if (creatures.Count == 0 || p.Action.CurrentGameZoneName != GameZoneName.Cemetery) { pending.Remove(s); return null; }
                state.ValidCardCommandMask = (int)CardCommand.BetrayChoice;
                foreach (ServerCard c in creatures) state.ActiveAbilityTargets.Add(c.NetUid.Id);
                state.DisplayCards.Add(p.Action.HistoryCreate_FullStateCard(s.CardCatalog));
            }
            else
            {
                Card card = GameManager.Instance.GetCard(p.Action.CardId);
                CardDefinition definition = GameManager.Instance.LookUpCardDefinition(card);
                BoardLaneMask legal = s.CardCatalog.GatherAllBoardLanesMask(player.NetId, definition.ZoneLookupBoard);
                BoardLaneMask filled = s.CardCatalog.GatherFilledBoardLanesMask(player.NetId, definition.ZoneLookupBoard);
                PlayerConditionalArguments pa = new PlayerConditionalArguments(s.CardCatalog, player, player, null, null, opponent);
                if (CommandRestoration.CanPlay(p.Action, card, ref legal, pa, false))
                    s.CardCatalog.FillPlayCardCommandStateNoPrerequisites(state, p.Action, card, player, opponent, s.Players, legal, filled);
                if ((state.ValidCardCommandMask & 15) == 0)
                {
                    // No legal replay (e.g. no target remains): the action returns to the cemetery.
                    DecisionRestoration.ActionToCemetery(DecisionRestoration.State(s, t), p.Action);
                    pending.Remove(s);
                    return null;
                }
                state.ValidCardCommandMask |= (int)CardCommand.BetrayPlay;
            }
            item.CommandList.ValidCommands.Add(state);
            return item;
        }

        public static Simulation.DecisionStatus ProcessBetray(Simulation s, TurnState t, Decision d)
        {
            if (t.CurrentSequence.Stage != 0) return Simulation.DecisionStatus.Complete;
            Pending p;
            if (!pending.TryGetValue(s, out p) || p.Replay || d == null || d.ActorCardNetUid != p.Action.NetUid.Id || d.DiscardCardNetUids.Count > 1)
                return Simulation.DecisionStatus.Error;
            if (d.DiscardCardNetUids.Count == 0)
            {
                pending.Remove(s); // Decline: native expects a Betray sequence to complete without an event.
                t.CurrentCommandItem = null;
                return Simulation.DecisionStatus.Complete;
            }
            ServerCard sacrifice = s.CardCatalog.FindDynamicCard(new CardNetUid(d.DiscardCardNetUids[0]));
            PlayerState player = s.Players[t.ActivePlayerIndex];
            if (sacrifice == null || !Sacrifices(s, player).Contains(sacrifice)) return Simulation.DecisionStatus.Error;
            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, t);
            t.CurrentSequence.EventStarted = true;
            s.HistoryLog.StartEvent(HistoryType.EventBetray, player.NetId, p.Action.NetUid, sacrifice.NetUid, -1);
            sacrifice.Sacrificed = true;
            DecisionRestoration.DiscardCards(gs, new List<ServerCard> { sacrifice }, -1, false);
            GameZoneOperationResult move = ServerCardCatalog.MoveCard(gs, new GameZoneId(player.NetId, p.Action.CurrentGameZoneType), new GameZoneId(player.NetId, GameZoneType.Betray),
                p.Action.InstanceId, 0, CardInsertionType.Bottom, ServerCardCatalog.MoveOperationType.Untyped);
            if (move != GameZoneOperationResult.Succeeded) throw new InvalidOperationException("Offline Betray move to the Betray zone failed: " + move);
            ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.BetrayCount, gs, p.Action, sacrifice, 1);
            p.Replay = true;
            t.CurrentCommandItem = null;
            return Simulation.DecisionStatus.Processing;
        }
    }
}
