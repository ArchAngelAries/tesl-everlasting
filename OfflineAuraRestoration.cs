using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstruction of the server-side card aura recompute. The shipped client's EnchantmentUtilities.RecalculateAuras
    // has an empty body; its surviving private TestLaneAuras shows the original pattern, which is followed here:
    // evaluate each AuraEnchantment's native selectors, keep an existing matching aura instance, otherwise add a new
    // EnchantmentInstance (native StackCount) to ServerCard.AuraEnchantments through native AddEnchantment, and remove
    // instances that no longer apply. Card auras come from the native ServerCard.Auras list (definition + card, which
    // native RebuildAurasFromCard empties of card auras when silenced). Instances are keyed by (Enchantment, source
    // instance) so two copies of one aura card stack. Lane-trigger auras are NOT applied here (unchanged behaviour).
    public static class OfflineAuraRestoration
    {
        private static readonly HashSet<EnchantmentInstance> owned = new HashSet<EnchantmentInstance>();
        public static int OwnedCount { get { return owned.Count; } }

        public static void Apply(ServerEffectUtilities.TriggerGameState gs)
        {
            if (gs.CardCatalog == null || gs.Players == null || gs.Players.Count < 2) return;
            ServerCardCatalog catalog = gs.CardCatalog;
            List<ServerCard> all = catalog.GatherCardList(delegate(ServerCard c) { return true; });
            Dictionary<ServerCard, List<EnchantmentInstance>> stale = new Dictionary<ServerCard, List<EnchantmentInstance>>();
            foreach (ServerCard card in all)
                foreach (EnchantmentInstance instance in card.AuraEnchantments)
                    if (owned.Contains(instance))
                    {
                        List<EnchantmentInstance> list;
                        if (!stale.TryGetValue(card, out list)) stale[card] = list = new List<EnchantmentInstance>();
                        list.Add(instance);
                    }
            List<ServerCard> affected = new List<ServerCard>();
            foreach (ServerCard source in all)
            {
                if (source.CurrentGameZoneType != GameZoneType.Board || source.IsDying() || source.Auras == null || source.Auras.Count == 0) continue;
                PlayerState self = gs.Players.Find(delegate(PlayerState p) { return p.NetId == source.OwnerNetId; });
                PlayerState opponent = gs.Players.Find(delegate(PlayerState p) { return p.NetId != source.OwnerNetId; });
                if (self == null) continue;
                CardConditionalArguments ca = new CardConditionalArguments(catalog, source, source, null, null, null, LaneIdentifier.GetInvalid());
                PlayerConditionalArguments pa = new PlayerConditionalArguments(catalog, self, self, null, null, opponent);
                foreach (Aura aura in source.Auras)
                {
                    if (!Met(aura, ca, pa)) continue;
                    foreach (AuraEnchantment ae in aura.EnchantmentsToApply)
                    {
                        int stacks = ActionValueData.EvaluateValue(ae.StackCount, ca, pa, default(TriggerConditionalArguments), gs.RandomGenerator);
                        if (stacks <= 0) continue;
                        foreach (ServerCard target in CardConditionals.EvaluateSelectorsOnServerCardList(ae.Selectors, all, ca, pa, default(TriggerConditionalArguments)))
                        {
                            if (target.CurrentGameZoneType == GameZoneType.Deathrattle) continue;
                            List<EnchantmentInstance> existing;
                            int index = -1;
                            if (stale.TryGetValue(target, out existing))
                                index = existing.FindIndex(delegate(EnchantmentInstance x) { return Object.ReferenceEquals(x.Enchantment, ae.Enchantment) && x.SourceId == source.InstanceId && x.Stacks == stacks; });
                            if (index >= 0) { existing.RemoveAt(index); continue; }
                            EnchantmentInstance instance = new EnchantmentInstance(ae.Enchantment, stacks, source.CardId, source.InstanceId, target);
                            target.AuraEnchantments.Add(instance);
                            target.AddEnchantment(gs, instance, source);
                            owned.Add(instance);
                            if (!affected.Contains(target)) affected.Add(target);
                        }
                    }
                }
            }
            ApplyLaneAuras(gs, all, stale, affected);
            foreach (KeyValuePair<ServerCard, List<EnchantmentInstance>> pair in stale)
                foreach (EnchantmentInstance instance in pair.Value)
                {
                    pair.Key.Enchantments.Remove(instance);
                    pair.Key.AuraEnchantments.Remove(instance);
                    owned.Remove(instance);
                    if (!affected.Contains(pair.Key)) affected.Add(pair.Key);
                }
            owned.RemoveWhere(delegate(EnchantmentInstance x) { return x == null; });
            foreach (ServerCard card in affected) card.RecalculateEnchantmentsWithTriggers(gs, null);
            if (affected.Count > 0 && gs.Log != null) HistoryLoggingUtilities.LogFullCardRefresh(gs.Log, catalog, affected, false);
        }

        // Lane auras (LaneTrigger.AuraEnchantments over each lane's creatures, as the surviving private
        // TestLaneAuras does) and the story Library hand discount. Instances are keyed to the target itself.
        private static void ApplyLaneAuras(ServerEffectUtilities.TriggerGameState gs, List<ServerCard> all,
            Dictionary<ServerCard, List<EnchantmentInstance>> stale, List<ServerCard> affected)
        {
            LaneTrigger[] lanes = GameManager.Instance == null || GameManager.Instance.Config == null ? null : GameManager.Instance.Config.LaneTriggers;
            if (lanes == null) return;
            ServerCardCatalog catalog = gs.CardCatalog;
            foreach (PlayerState player in gs.Players)
            {
                PlayerState opponent = gs.Players.Find(delegate(PlayerState p) { return p.NetId != player.NetId; });
                PlayerConditionalArguments pa = new PlayerConditionalArguments(catalog, player, null, null, null, opponent);
                CardConditionalArguments ca = new CardConditionalArguments(catalog, null, null, null, null, null, LaneIdentifier.GetInvalid());
                int count = catalog.FindLaneCount(GameZoneName.CreatureBoard, player.NetId);
                for (int lane = 0; lane < count; lane++)
                {
                    CardLaneType type = catalog.QueryCreatureBoardLaneType(player.NetId, lane);
                    if ((int)type <= 0 || (int)type >= lanes.Length || lanes[(int)type] == null || lanes[(int)type].AuraEnchantments.Count == 0) continue;
                    int index = lane;
                    List<ServerCard> here = catalog.GatherCardsInZone(delegate(ServerCard x) { return x.CurrentLaneIndex == index; }, player.NetId, GameZoneName.CreatureBoard);
                    foreach (AuraEnchantment ae in lanes[(int)type].AuraEnchantments)
                        foreach (ServerCard target in CardConditionals.EvaluateSelectorsOnServerCardList(ae.Selectors, here, ca, pa, default(TriggerConditionalArguments)))
                            Keep(gs, target, ae.Enchantment, stale, affected);
                }
                if (StoryScenarioRules.Active && StoryLaneRules.LibraryActive(catalog, player))
                    foreach (ServerCard card in catalog.GatherCardsInZone(delegate(ServerCard x) { return x.HasSubtype(CardSubtype.Spell); }, player.NetId, GameZoneName.Hand))
                        Keep(gs, card, StoryLaneRules.LibraryDiscount, stale, affected);
            }
        }

        private static void Keep(ServerEffectUtilities.TriggerGameState gs, ServerCard target, Enchantment enchantment,
            Dictionary<ServerCard, List<EnchantmentInstance>> stale, List<ServerCard> affected)
        {
            if (target.CurrentGameZoneType == GameZoneType.Deathrattle) return;
            List<EnchantmentInstance> existing;
            int index = -1;
            if (stale.TryGetValue(target, out existing))
                index = existing.FindIndex(delegate(EnchantmentInstance x) { return Object.ReferenceEquals(x.Enchantment, enchantment) && x.SourceId == target.InstanceId; });
            if (index >= 0) { existing.RemoveAt(index); return; }
            EnchantmentInstance instance = new EnchantmentInstance(enchantment, 1, target.CardId, target.InstanceId, target);
            target.AuraEnchantments.Add(instance);
            target.AddEnchantment(gs, instance, target);
            owned.Add(instance);
            if (!affected.Contains(target)) affected.Add(target);
        }

        private static bool Met(Aura aura, CardConditionalArguments ca, PlayerConditionalArguments pa)
        {
            foreach (IPlayerRequirement r in aura.PlayerRequirements)
                if (!r.IsRequirementMet(default(PlayerConditionalLog), pa, ca, default(TriggerConditionalArguments))) return false;
            foreach (ICardRequirement r in aura.CardRequirements)
                if (!r.IsRequirementMet(ca, pa, default(TriggerConditionalArguments))) return false;
            return true;
        }

        public static bool RecalculateAurasPrefix(ServerEffectUtilities.TriggerGameState gameState, PlayerState currentPlayer) { Apply(gameState); return false; }
    }
}
