using System.Collections.Generic;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed item ability routing. Installed item text uses creature keywords ("Summon: Give the wielder Cover",
    // "Slay: ...", "Pilfer: ...") that act through the wielder; the shipped client has no server routing for them. Native
    // ServerEffectUtilities.TriggerDirectEffectEventFromCard runs one card's triggers with another card as the acting card,
    // so an item's effects resolve as its wielder ("self"/"the wielder", "this lane").
    //  - Summon: when the item is played and equipped (OfflineItemTriggers.AfterEquipPlay, called from the play sequence).
    //  - Slay / Pilfer / Veteran / Last Gasp: whenever the wielder's own direct trigger of that type fires.
    public static class OfflineItemTriggers
    {
        private static readonly EffectTriggerType[] Forwarded = { EffectTriggerType.Slay, EffectTriggerType.Pilfer, EffectTriggerType.Veteran, EffectTriggerType.LastGaspTrigger };
        [System.ThreadStatic] private static bool forwarding;

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ServerEffectUtilities), "TriggerDirectEffectEvent"), null,
                new HarmonyMethod(typeof(OfflineItemTriggers).GetMethod("DirectPostfix")), null, null, null);
        }

        public static List<ServerCard> ItemsOf(ServerCardCatalog catalog, ServerCard wielder)
        {
            return catalog.GatherCardList(delegate(ServerCard x) { return x != wielder && x.HasSubtype(CardSubtype.Item) && x.ItemTarget == wielder.InstanceId; });
        }

        public static void DirectPostfix(ServerEffectUtilities.TriggerGameState gameState, ServerEffectUtilities.TriggerEvent triggerEvent, ref bool __result)
        {
            ServerCard wielder = triggerEvent.Inciter;
            if (forwarding || wielder == null || System.Array.IndexOf(Forwarded, triggerEvent.Type) < 0 || !wielder.HasSubtype(CardSubtype.Creature)) return;
            forwarding = true;
            try
            {
                foreach (ServerCard item in ItemsOf(gameState.CardCatalog, wielder))
                    if (item.TriggeredEffects.Count > 0 && ServerEffectUtilities.TriggerDirectEffectEventFromCard(gameState, triggerEvent, item)) __result = true;
            }
            finally { forwarding = false; }
        }

        // An item's own Summon ability when it is played onto a creature (equip or Mobilize).
        public static void AfterEquipPlay(ServerEffectUtilities.TriggerGameState gs, ServerCard item, ServerCard wielder, List<PlayerState> players)
        {
            if (item == null || wielder == null || item.TriggeredEffects.Count == 0) return;
            ServerEffectUtilities.TriggerDirectEffectEventFromCard(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, wielder, players), item);
        }
    }
}
