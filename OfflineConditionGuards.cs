using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Card conditions in the game's shared code that dereference a card which can be missing when a queued reaction resolves:
    //  - LinkedTo: the "linked to" role card is gone (Preying Elytra's chosen creature): a missing card is not linked.
    //  - HasTreasureHunt with UseWielderAsHunter: the wielder is not found (Treasure Map): a missing wielder is not a hunter.
    // Each guard only answers when the card is missing; otherwise the original runs unchanged.
    public static class OfflineConditionGuards
    {
        private static bool installed;

        public static void Install(Harmony harmony)
        {
            if (installed) return;
            installed = true;
            harmony.Patch(AccessTools.Method(typeof(CardConditionals.LinkedTo), "Evaluate"),
                new HarmonyMethod(typeof(OfflineConditionGuards).GetMethod("LinkedToPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(CardConditionals.HasTreasureHunt), "Evaluate"),
                new HarmonyMethod(typeof(OfflineConditionGuards).GetMethod("TreasureHuntPrefix")), null, null, null, null);
        }

        public static bool LinkedToPrefix(CardConditionals.LinkedTo __instance, ServerCard lhs, CardConditionalArguments arguments, ref bool __result)
        {
            if (lhs == null) return true;
            if (arguments.GetEventRoleCard(__instance.LinkedToRole) != null) return true;
            __result = __instance.Check == CardConditionals.LinkedTo.Requirement.IsNot;
            return false;
        }

        private static readonly System.Reflection.FieldInfo baseCard = AccessTools.Field(typeof(ServerCard), "_card");
        private static bool logged;

        public static bool TreasureHuntPrefix(CardConditionals.HasTreasureHunt __instance, ServerCard lhs, CardConditionalArguments arguments, ref bool __result)
        {
            if (lhs == null) return true;
            ServerCard hunter = lhs;
            if (__instance.UseWielderAsHunter && lhs.ItemTarget.IsValid())
                hunter = arguments.CardCatalog == null ? null : arguments.CardCatalog.FindDynamicCard(lhs.ItemTarget);
            Card data = hunter == null ? null : baseCard.GetValue(hunter) as Card;
            if (hunter != null && data != null && data.HuntTypes != null) return true;
            if (!logged)
            {
                logged = true;
                UnityEngine.Debug.LogWarning("OFFLINE CONDITIONS: treasure-hunt check on " + (hunter == null ? "a missing wielder" : "card " + hunter.CardId
                    + (data == null ? " without card data" : " without hunt types")) + " (asked by " + lhs.CardId + "); treated as not hunting.");
            }
            __result = __instance.Check == CardConditionals.HasTreasureHunt.Requirement.IsNotHunter;
            return false;
        }
    }
}
