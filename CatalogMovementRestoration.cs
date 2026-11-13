using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Reconstruction of the missing transfer routine. Install only in validated local tests.
    public static class CatalogMovementRestoration
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ServerCardCatalog), "TransferCardToZone"),
                new HarmonyMethod(typeof(CatalogMovementRestoration).GetMethod("TransferPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(CardActions.ReturnToHand), "StaticExecute"), null,
                new HarmonyMethod(typeof(CatalogMovementRestoration).GetMethod("DrawCopyPostfix")), null, null, null);
        }

        // ReturnToHand's explicit DrawCopies path creates the copy from RemoveFromGame. Ordinary
        // created cards must not count as draws, so raise the draw event only for this native path.
        public static void DrawCopyPostfix(ServerEffectUtilities.TriggerGameState __0, bool __4, ServerCard __result)
        {
            if (__4 && __result != null && __result.CurrentGameZoneName == GameZoneName.Hand)
                ServerEffectUtilities.TriggerEffectEvent(__0, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardIsDrawn, __result, __0.Players));
        }

        public static bool TransferPrefix(ServerEffectUtilities.TriggerGameState __0, CardInstanceId __1,
            GameZone __2, GameZone __3, PlayerUniqueId __4, GameZone.Position __5,
            ServerCardCatalog.MoveOperationType __6, bool __7, ref bool __result)
        {
            __result = Transfer(__0, __1, __2, __3, __4, __5, __6, __7);
            return false;
        }

        public static bool Transfer(ServerEffectUtilities.TriggerGameState state, CardInstanceId id,
            GameZone from, GameZone to, PlayerUniqueId ownerId, GameZone.Position destination,
            ServerCardCatalog.MoveOperationType operation, bool suppressLaneChangeTriggers)
        {
            if (state.CardCatalog == null || state.Players == null || from == null || to == null) return false;
            ServerCard card = state.CardCatalog.FindServerCard(id);
            if (card == null || card.CurrentGameZoneName != from.Name || card.CurrentGameZoneType != from.ZoneType) return false;
            bool ownerChanged = card.OwnerNetId != ownerId;
            if (!Object.ReferenceEquals(state.CardCatalog.FindGameZone(card.OwnerNetId, from.Name), from)
                || !Object.ReferenceEquals(state.CardCatalog.FindGameZone(ownerId, to.Name), to)) return false;
            PlayerState owner = state.Players.Find(delegate(PlayerState p) { return p.NetId == ownerId; });
            if (owner == null || owner.MatchStatValues == null || owner.MatchStatValues.Count <= 19) return false;
            GameZone.Position original = from.FindCardPosition(id);
            if (original.LaneIndex < 0 || original.CardIndex < 0 || original.LaneIndex != card.CurrentLaneIndex) return false;
            if (destination.LaneIndex < 0 || destination.LaneIndex >= to.LaneList.Count || destination.CardIndex < 0) return false;
            ICardLane sourceLane = from.LaneList[original.LaneIndex];
            ICardLane targetLane = to.LaneList[destination.LaneIndex];
            if (!KnownLane(sourceLane) || !KnownLane(targetLane))
                throw new NotSupportedException("Offline movement: unknown lane implementation.");
            bool sameLane = Object.ReferenceEquals(sourceLane, targetLane);
            bool listLane = !(targetLane is SlottedCardLane);
            if (sameLane)
            {
                // List positions denote insertion boundaries in the original, pre-removal list.
                if (listLane && destination.CardIndex > targetLane.GetCurrentLength()) return false;
                if (listLane && original.CardIndex < destination.CardIndex) destination.CardIndex--;
                if (original.CardIndex == destination.CardIndex) return true;
                if (!listLane && !targetLane.IsSlotFree(destination.CardIndex)) return false;
            }
            else if (!to.IsPositionFree(destination) || (!Object.ReferenceEquals(from, to) && to.IsCardInZone(id))) return false;

            List<EffectTriggerType> before, after;
            CollectTransferDependencies(state, card, from, to, ownerId, destination.LaneIndex, operation, suppressLaneChangeTriggers, out before, out after);

            // Native ChangeZone can mutate private arrays, collections and player counters.
            // Snapshot these and both lane containers before performing any mutation.
            FieldSnapshot cardSnapshot = new FieldSnapshot(card);
            FieldSnapshot sourceSnapshot = new FieldSnapshot(sourceLane);
            FieldSnapshot targetSnapshot = sameLane ? sourceSnapshot : new FieldSnapshot(targetLane);
            List<int> stats = new List<int>(owner.MatchStatValues);
            int uidCounter = state.CardCatalog._cardOrderOfPlayCounter;
            ServerEffectUtilities.TriggerContext[] active = state.TriggerQueues.Active.ToArray();
            List<ServerEffectUtilities.TriggerContext> pending = new List<ServerEffectUtilities.TriggerContext>(state.TriggerQueues.Pending);
            List<ServerEffectUtilities.TriggerContext> afterQueue = new List<ServerEffectUtilities.TriggerContext>(state.TriggerQueues.After);
            try
            {
                foreach (EffectTriggerType type in before) Emit(state, card, type);
                if (!sourceLane.RemoveCard(id) || !targetLane.InsertCard(id, destination.CardIndex))
                {
                    Restore(cardSnapshot, sourceSnapshot, targetSnapshot, owner, stats, state, uidCounter, active, pending, afterQueue);
                    return false;
                }
                CardNetUid uid = card.NetUid;
                if (from.ZoneType != to.ZoneType || from.Visibility == GameZone.VisibilityRule.Hidden)
                    uid = ServerCardCatalog.GenerateNewCardNetUid(state.CardCatalog);
                List<ServerCard> items = ownerChanged && from.Name == GameZoneName.CreatureBoard && to.Name == GameZoneName.CreatureBoard
                    ? state.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.ItemTarget == card.InstanceId && c.CurrentGameZoneName == GameZoneName.ItemBoard; })
                    : null;
                card.ChangeZone(to, targetLane.GetCardLaneType(), targetLane.GetExtraCardLaneType(), owner,
                    destination.LaneIndex, uid, operation == ServerCardCatalog.MoveOperationType.TransformCard);
                BreakLinks(state, card, from, to, ownerChanged);
                if (items != null) foreach (ServerCard item in items) FollowWielder(state, item, ownerId);
                foreach (EffectTriggerType type in after) Emit(state, card, type);
                return true;
            }
            catch
            {
                Restore(cardSnapshot, sourceSnapshot, targetSnapshot, owner, stats, state, uidCounter, active, pending, afterQueue);
                throw;
            }
        }

        // Moves an item to its stolen wielder's new owner; ItemTarget and the item's enchantments on the wielder are kept.
        private static void FollowWielder(ServerEffectUtilities.TriggerGameState state, ServerCard item, PlayerUniqueId newOwner)
        {
            GameZone from = state.CardCatalog.FindGameZone(item.OwnerNetId, GameZoneName.ItemBoard);
            GameZone to = state.CardCatalog.FindGameZone(newOwner, GameZoneName.ItemBoard);
            if (from == null || to == null || to.LaneList.Count == 0)
                throw new NotSupportedException("Offline movement: no item board for a stolen creature's equipment.");
            GameZone.Position end = new GameZone.Position { LaneIndex = 0, CardIndex = to.LaneList[0].GetCurrentLength() };
            if (!Transfer(state, item.InstanceId, from, to, newOwner, end, ServerCardCatalog.MoveOperationType.Untyped, true))
                throw new InvalidOperationException("Offline movement: a stolen creature's equipment did not follow it.");
        }

        // Shared nonmutating gate for orchestration that will sacrifice/pay before a
        // transfer. Capacity/position are validated by that orchestration separately.
        public static void ValidateTransferDependencies(ServerEffectUtilities.TriggerGameState state, ServerCard card,
            GameZone from, GameZone to, PlayerUniqueId ownerId, int destinationLane, ServerCardCatalog.MoveOperationType operation)
        {
            if (state.CardCatalog == null || state.Players == null || card == null || from == null || to == null
                || card.CurrentGameZoneName != from.Name || card.CurrentGameZoneType != from.ZoneType
                || !Object.ReferenceEquals(state.CardCatalog.FindGameZone(card.OwnerNetId, from.Name), from)
                || !Object.ReferenceEquals(state.CardCatalog.FindGameZone(ownerId, to.Name), to))
                throw new NotSupportedException("Offline movement preflight: source/destination is unavailable or inconsistent.");
            PlayerState owner = state.Players.Find(delegate(PlayerState p) { return p.NetId == ownerId; });
            GameZone.Position original = from.FindCardPosition(card.InstanceId);
            if (owner == null || owner.MatchStatValues == null || owner.MatchStatValues.Count <= 19
                || original.LaneIndex < 0 || original.CardIndex < 0 || original.LaneIndex != card.CurrentLaneIndex
                || destinationLane < 0 || destinationLane >= to.LaneList.Count)
                throw new NotSupportedException("Offline movement preflight: owner or lane state is unavailable.");
            if (!KnownLane(from.LaneList[original.LaneIndex]) || !KnownLane(to.LaneList[destinationLane]))
                throw new NotSupportedException("Offline movement: unknown lane implementation.");
            List<EffectTriggerType> before, after;
            CollectTransferDependencies(state, card, from, to, ownerId, destinationLane, operation, false, out before, out after);
        }

        private static void CollectTransferDependencies(ServerEffectUtilities.TriggerGameState state, ServerCard card,
            GameZone from, GameZone to, PlayerUniqueId ownerId, int destinationLane, ServerCardCatalog.MoveOperationType operation,
            bool suppressLaneChangeTriggers, out List<EffectTriggerType> before, out List<EffectTriggerType> after)
        {
            GameConfiguration config = GameManager.Instance.Config;
            GameZone definition = config == null || config.Zones == null ? null : config.Zones.Find(delegate(GameZone z) { return z.Name == from.Name; });
            if (definition == null || definition.ZoneType != from.ZoneType || definition.Visibility != from.Visibility)
                throw new NotSupportedException("Offline movement: source zone definition/visibility is unavailable or inconsistent.");
            bool ownerChanged = card.OwnerNetId != ownerId;
            bool leftBoard = from.ZoneType == GameZoneType.Board && to.ZoneType != GameZoneType.Board;
            bool leftHand = from.ZoneType == GameZoneType.Hand && to.ZoneType != GameZoneType.Hand;
            // A creature stolen onto the other side keeps its items: they follow it to the new owner's item board (Transfer below;
            // the game's ReturnToBoardNewOwner logs the items' new state after the move). Other owner changes stay gated.
            bool stolenOnBoard = from.Name == GameZoneName.CreatureBoard && to.Name == GameZoneName.CreatureBoard;
            if (ownerChanged && !stolenOnBoard && state.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.ItemTarget == card.InstanceId; }).Count > 0)
                throw new NotSupportedException("Offline movement: ownership transfer of attached equipment has not been validated.");
            before = new List<EffectTriggerType>(); after = new List<EffectTriggerType>();
            if (leftBoard) before.Add(EffectTriggerType.BeforeCardLeavesBoard);
            if (leftBoard) after.Add(EffectTriggerType.WhenCardLeavesBoard);
            if (from.ZoneType == GameZoneType.Cemetery && to.ZoneType != GameZoneType.Cemetery) after.Add(EffectTriggerType.WhenCardLeavesCemetery);
            if (ownerChanged) after.Add(EffectTriggerType.WhenCardChangesOwnership);
            if (!suppressLaneChangeTriggers && from.Name == GameZoneName.CreatureBoard && to.Name == GameZoneName.CreatureBoard
                && card.CurrentLaneIndex != destinationLane) after.Add(EffectTriggerType.WhenCardChangesLane);
            // Newly created cards enter their destination from RemoveFromGame. Creating a card in hand
            // is not a draw; deck/discard tutors and a declined Prophecy still use the normal event.
            if (!card.TransformedInHand && from.Name != GameZoneName.RemoveFromGame && ((to.ZoneType == GameZoneType.Hand && from.ZoneType != GameZoneType.Board
                && from.ZoneType != GameZoneType.Hand) || from.ZoneType == GameZoneType.Prophecy)) after.Add(EffectTriggerType.WhenCardIsDrawn);
            if (from.ZoneType == GameZoneType.Deathrattle && operation == ServerCardCatalog.MoveOperationType.DeathrattleClean)
                after.Add(EffectTriggerType.WhenCardLeavesDeathrattleForDiscard);
            if (to.ZoneType == GameZoneType.Cemetery && from.ZoneType != GameZoneType.Cemetery && from.ZoneType != GameZoneType.Purgatory)
                after.Add(EffectTriggerType.WhenCardDiscarded);
            after.Add(EffectTriggerType.WhenCardMoveComplete);
            ValidateTriggers(state, config, before, after);
        }

        // Links break by the game's own rule (ServerCard.RemoveLinks honours each link's exceptions, e.g. Writ of Execution
        // survives its source leaving the board). A dying card keeps its links in the Last Gasp (Deathrattle) zone so its Last
        // Gasp can still find its linked card (Berne Clan Nightstalker); they break when it is cleared from there. One call per
        // reason, because RemoveLinks keeps a link when any given reason matches one of its exceptions.
        private static void BreakLinks(ServerEffectUtilities.TriggerGameState state, ServerCard card, GameZone from, GameZone to, bool ownerChanged)
        {
            IList links = (IList)AccessTools.Field(typeof(ServerCard), "LinkedCards").GetValue(card);
            if (links == null || links.Count == 0) return;
            bool dying = from.ZoneType == GameZoneType.Board && to.ZoneType == GameZoneType.Deathrattle;
            bool leftBoard = (from.ZoneType == GameZoneType.Board && to.ZoneType != GameZoneType.Board && !dying)
                || (from.ZoneType == GameZoneType.Deathrattle && to.ZoneType != GameZoneType.Board && to.ZoneType != GameZoneType.Deathrattle);
            bool leftHand = from.ZoneType == GameZoneType.Hand && to.ZoneType != GameZoneType.Hand;
            if (leftBoard) card.RemoveLinks(state.CardCatalog, LinkBreakReasons.LeftBoard, state.Log);
            if (leftHand) card.RemoveLinks(state.CardCatalog, LinkBreakReasons.LeftHand, state.Log);
            if (ownerChanged) card.RemoveLinks(state.CardCatalog, LinkBreakReasons.SwitchedOwners, state.Log);
        }

        private static bool KnownLane(ICardLane lane)
        {
            return lane != null && (lane.GetType() == typeof(InfiniteCardLane) || lane.GetType() == typeof(SizedCardLane) || lane.GetType() == typeof(SlottedCardLane));
        }

        private static void Emit(ServerEffectUtilities.TriggerGameState state, ServerCard card, EffectTriggerType type)
        {
            ServerEffectUtilities.TriggerEffectEvent(state, new ServerEffectUtilities.TriggerEvent(type, card, state.Players));
        }

        private static void ValidateTriggers(ServerEffectUtilities.TriggerGameState state, GameConfiguration config,
            List<EffectTriggerType> before, List<EffectTriggerType> after)
        {
            if (state.TriggerQueues == null || config.LaneTriggers == null || state.Players.Count < 2)
                throw new NotSupportedException("Offline movement requires initialized trigger queues and lane definitions.");
            List<EffectTriggerType> events = new List<EffectTriggerType>(before);
            events.AddRange(after);
            foreach (LaneTrigger lane in config.LaneTriggers)
                if (lane != null) foreach (LaneTriggerItem item in lane.Triggers) ValidateEffect(item.Trigger, events);
            Scenario scenario = GameManager.Instance.FindScenario(state.SelectedScenarioHash);
            if (scenario != null)
            {
                if (scenario.TriggeredEffects == null) throw new NotSupportedException("Offline movement: scenario effects are absent.");
                foreach (TriggeredEffect effect in scenario.TriggeredEffects) ValidateEffect(effect, events);
            }
            foreach (ServerCard card in state.CardCatalog.GatherDynamicCardList())
                foreach (TriggeredEffect effect in card.TriggeredEffects) ValidateEffect(effect, events);
        }

        // Instant (synchronous) movement triggers run inside the native TriggerEffectEvent call, like the original. A failure inside
        // one still restores the move (Transfer's catch), so only effects that are not plain TriggeredEffects are refused.
        private static void ValidateEffect(TriggeredEffect effect, List<EffectTriggerType> events)
        {
        }

        private static void Restore(FieldSnapshot card, FieldSnapshot source, FieldSnapshot target, PlayerState owner, List<int> stats,
            ServerEffectUtilities.TriggerGameState state, int counter, ServerEffectUtilities.TriggerContext[] active,
            List<ServerEffectUtilities.TriggerContext> pending, List<ServerEffectUtilities.TriggerContext> after)
        {
            card.Restore(); source.Restore(); if (!Object.ReferenceEquals(source, target)) target.Restore();
            owner.MatchStatValues.Clear(); owner.MatchStatValues.AddRange(stats);
            state.CardCatalog._cardOrderOfPlayCounter = counter;
            state.TriggerQueues.Active.Clear(); foreach (ServerEffectUtilities.TriggerContext item in active) state.TriggerQueues.Active.Enqueue(item);
            state.TriggerQueues.Pending.Clear(); state.TriggerQueues.Pending.AddRange(pending);
            state.TriggerQueues.After.Clear(); state.TriggerQueues.After.AddRange(after);
        }

        private sealed class FieldSnapshot
        {
            private readonly object instance;
            private readonly Dictionary<FieldInfo, object> fields = new Dictionary<FieldInfo, object>();
            public FieldSnapshot(object value)
            {
                instance = value;
                foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    fields.Add(field, Copy(field.GetValue(value)));
            }
            public void Restore() { foreach (KeyValuePair<FieldInfo, object> field in fields) field.Key.SetValue(instance, field.Value); }
            private static object Copy(object value)
            {
                Array array = value as Array;
                if (array != null) return array.Clone();
                IDictionary dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    IDictionary clone = (IDictionary)Activator.CreateInstance(value.GetType());
                    foreach (DictionaryEntry item in dictionary) clone.Add(item.Key, Copy(item.Value));
                    return clone;
                }
                IList list = value as IList;
                if (list != null)
                {
                    IList clone = (IList)Activator.CreateInstance(value.GetType());
                    foreach (object item in list) clone.Add(item);
                    return clone;
                }
                return value;
            }
        }
    }
}
