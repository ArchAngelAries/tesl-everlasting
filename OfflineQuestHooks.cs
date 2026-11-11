using System.Collections.Generic;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Quest counting the stripped server performed around native actions that survive in the client.
    // Counting itself is the native ActiveQuestUtilities helper; the play/betray/plot/expertise counts are raised
    // in the restored sequences (DecisionRestoration, OfflineBetrayRestoration, OfflineTurnMechanics).
    public static class OfflineQuestHooks
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(CardActions.PayExaltCost), "Execute"), new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("ExaltPrefix")),
                new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("ExaltPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(PlayerActions.DrawCards), "Execute"), new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("DrawPrefix")),
                new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("DrawPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(PlayerActions.CreateToken), "Execute"), new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("AundaePrefix")),
                new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("AundaePostfix")), null, null, null);
            foreach (System.Type type in new[] { typeof(CardActions.Ward), typeof(CardActions.GiveKeyword), typeof(CardActions.Cover), typeof(CardActions.Buff) })
                harmony.Patch(AccessTools.Method(type, "Execute"), new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("ElixirPrefix")),
                    new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("ElixirPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(CardActions.GiveRandomKeywords), "Execute"), new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("MundusPrefix")),
                new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("MundusPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(CardActions.StackingBuff), "Execute"), null,
                new HarmonyMethod(typeof(OfflineQuestHooks).GetMethod("FleshSummonBuffPostfix")), null, null, null);
        }

        // Exalt is paid by the native PayExaltCost action: one exalted card per target that the native loop reaches.
        // With UseRing the native loop stops at the first target whose owner is missing or whose ring use fails
        // (UseRing.StaticExecute: CanUseRing or RingUsesLeft present and <= 0; a success sets CanUseRing to 0), so the
        // prefix mirrors that loop on the state before the action and the postfix counts only the reached targets
        // (Astra/Luna analysis, T-4; native CardActions.PayExaltCost.Execute, PlayerActions.UseRing.StaticExecute).
        public static void ExaltPrefix(CardActions.PayExaltCost __instance, CardActionArguments __0, out List<ServerCard> __state)
        {
            __state = null;
            ServerEffectUtilities.TriggerGameState gs = __0.TriggerGameState;
            if (gs.ActiveQuests == null || gs.ActiveQuests.Count == 0 || __0.ValidTargetList == null) return;
            __state = ExaltReached(__instance.UseRing, gs.Players, __0.ValidTargetList);
        }

        public static List<ServerCard> ExaltReached(bool useRing, List<PlayerState> players, List<ServerCard> targets)
        {
            List<ServerCard> reached = new List<ServerCard>();
            List<PlayerState> ringUsed = new List<PlayerState>();
            foreach (ServerCard target in targets)
            {
                if (useRing)
                {
                    PlayerUniqueId owner = target.OwnerNetId;
                    PlayerState player = players.Find(delegate(PlayerState x) { return x.NetId == owner; });
                    if (player == null || ringUsed.Contains(player) || !RingAvailable(player)) break;
                    ringUsed.Add(player);
                }
                reached.Add(target);
            }
            return reached;
        }

        private static bool RingAvailable(PlayerState player)
        {
            PlayerAttributeQueryResult can = player.QueryAttribute(AttributeType.CanUseRing);
            if (can.HasAttribute && can.Value <= 0) return false;
            PlayerAttributeQueryResult left = player.QueryAttribute(AttributeType.RingUsesLeft);
            return !(left.HasAttribute && left.Value <= 0);
        }

        public static void ExaltPostfix(CardActionArguments __0, List<ServerCard> __state)
        {
            if (__state == null) return;
            ServerEffectUtilities.TriggerGameState gs = __0.TriggerGameState;
            foreach (ServerCard card in __state)
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.ExaltCount, gs, card, card, 1);
        }

        // "Extra" cards are draws by card effects. Turn-start, mulligan and rune draws use DrawCards.StaticExecute directly.
        public static void DrawPrefix(PlayerActionArguments __0, out Dictionary<PlayerState, int> __state)
        {
            __state = null;
            ServerEffectUtilities.TriggerGameState gs = __0.TriggerGameState;
            if (gs.ActiveQuests == null || gs.ActiveQuests.Count == 0 || __0.ValidTargetList == null) return;
            __state = new Dictionary<PlayerState, int>();
            foreach (PlayerState player in __0.ValidTargetList) __state[player] = HandCount(gs, player);
        }

        public static void DrawPostfix(PlayerActionArguments __0, Dictionary<PlayerState, int> __state)
        {
            if (__state == null) return;
            ServerEffectUtilities.TriggerGameState gs = __0.TriggerGameState;
            ServerCard source = __0.CardArguments.GetEventRoleCard(CardRole.Self);
            foreach (KeyValuePair<PlayerState, int> before in __state)
            {
                int drawn = HandCount(gs, before.Key) - before.Value;
                if (drawn <= 0) continue;
                // The native helper credits the acting card's owner, so the source must belong to the drawing player.
                if (source == null || source.OwnerNetId != before.Key.NetId) continue;
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DrawExtraCard, gs, source, source, drawn);
            }
        }

        private static int HandCount(ServerEffectUtilities.TriggerGameState gs, PlayerState player)
        { return gs.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.Hand).Count; }

        // The title calls these "draws", while Aundae's installed text creates a spell in hand.
        public static void AundaePrefix(PlayerActions.CreateToken __instance, PlayerActionArguments __0, out HashSet<int> __state)
        {
            __state = null;
            ServerEffectUtilities.TriggerGameState gs = __0.TriggerGameState;
            ServerCard source = __0.CardArguments.GetEventRoleCard(CardRole.Self);
            if (__instance.CreateInZoneType != GameZoneType.Hand || source == null || source.CardId != 2019049161
                || gs.ActiveQuests == null || gs.ActiveQuests.Count == 0) return;
            __state = new HashSet<int>();
            foreach (ServerCard card in gs.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, source.OwnerNetId, GameZoneName.Hand))
                __state.Add(card.NetUid.Id);
        }

        public static void AundaePostfix(PlayerActionArguments __0, HashSet<int> __state)
        {
            if (__state == null) return;
            ServerEffectUtilities.TriggerGameState gs = __0.TriggerGameState;
            ServerCard source = __0.CardArguments.GetEventRoleCard(CardRole.Self);
            foreach (ServerCard card in gs.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, source.OwnerNetId, GameZoneName.Hand))
                if (!__state.Contains(card.NetUid.Id) && card.HasSubtype(CardSubtype.Blood_Magic) && card.HasSubtype(CardSubtype.Spell))
                    ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.AundaeDrawCount, gs, source, card, 1);
        }

        public sealed class ElixirObservation
        {
            public ServerCard Source;
            public AttributeType Attribute;
            public QuestGoalType Goal;
            public Dictionary<ServerCard, int> Before = new Dictionary<ServerCard, int>();
        }

        public static void ElixirPrefix(CardActionArguments __0, out ElixirObservation __state)
        {
            __state = null;
            ServerCard source = __0.CardArguments.GetEventRoleCard(CardRole.Self);
            if (source == null || __0.ValidTargetList == null || __0.TriggerGameState.ActiveQuests == null
                || __0.TriggerGameState.ActiveQuests.Count == 0) return;
            ElixirObservation observed = new ElixirObservation { Source = source };
            switch (source.CardId)
            {
                case 539510398: observed.Attribute = AttributeType.Ward; observed.Goal = QuestGoalType.DeflectorCount; break;
                case -646559647: observed.Attribute = AttributeType.Taunt; observed.Goal = QuestGoalType.DefenderCount; break;
                case -387279246: observed.Attribute = AttributeType.Cover; observed.Goal = QuestGoalType.LightfootedCount; break;
                case 1705848025: observed.Attribute = AttributeType.MaxHealth; observed.Goal = QuestGoalType.VigorousCount; break;
                default: return;
            }
            foreach (ServerCard target in __0.ValidTargetList)
                if (target != null && target.HasSubtype(CardSubtype.Creature)) observed.Before[target] = target.QueryAttribute(observed.Attribute).Value;
            __state = observed;
        }

        public static void ElixirPostfix(CardActionArguments __0, ElixirObservation __state)
        {
            if (__state == null) return;
            foreach (KeyValuePair<ServerCard, int> target in __state.Before)
            {
                int change = target.Key.QueryAttribute(__state.Attribute).Value - target.Value;
                if (change > 0) ActiveQuestUtilities.ExecuteCardQuestEvent(__state.Goal, __0.TriggerGameState, __state.Source, target.Key,
                    __state.Goal == QuestGoalType.VigorousCount ? change : 1);
            }
        }

        public sealed class MundusObservation
        {
            public ServerCard Source;
            public Dictionary<ServerCard, List<AttributeType>> Possible = new Dictionary<ServerCard, List<AttributeType>>();
        }

        public static void MundusPrefix(CardActionArguments __0, out MundusObservation __state)
        {
            __state = null;
            ServerCard source = __0.CardArguments.GetEventRoleCard(CardRole.Self);
            ServerEffectUtilities.TriggerGameState gs = __0.TriggerGameState;
            if (source == null || source.CardId != 1962253065 || __0.ValidTargetList == null || gs.ActiveQuests == null || gs.ActiveQuests.Count == 0) return;
            MundusObservation observed = new MundusObservation { Source = source };
            foreach (ServerCard target in __0.ValidTargetList)
                if (target != null) observed.Possible[target] = EnchantmentUtilities.GetPossibleNewKeywords(target, gs);
            __state = observed;
        }

        public static void MundusPostfix(CardActionArguments __0, MundusObservation __state)
        {
            if (__state == null) return;
            foreach (KeyValuePair<ServerCard, List<AttributeType>> target in __state.Possible)
            {
                int granted = 0;
                foreach (AttributeType keyword in target.Value) if (target.Key.QueryAttribute(keyword).Value > 0) granted++;
                if (granted > 0) ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.WalkerOfMundusCount,
                    __0.TriggerGameState, __state.Source, target.Key, granted);
            }
        }

        public static void FleshSummonBuffPostfix(CardActionArguments __0)
        {
            ServerCard source = __0.CardArguments.GetEventRoleCard(CardRole.Self);
            if (source == null || source.CardId != -355724207 || __0.ValidTargetList == null) return;
            foreach (ServerCard target in __0.ValidTargetList)
                if (target == source && target.CurrentGameZoneType == GameZoneType.Board && target.QueryAttribute(AttributeType.Attack).Value >= 20)
                    OfflineTitleFeats.FleshSummonBuffReached(target);
        }
    }
}
