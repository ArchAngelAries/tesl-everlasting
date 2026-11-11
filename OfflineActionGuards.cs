using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Native CardActions.TransformCard.StaticExecute writes the event inciter into its history item and dereferences it. Turn
    // events (start/end of turn) carry no inciter card, so a self-transform such as "At the start of your turn, this changes into
    // ..." (Little Girl, Corpselight Farmer) threw. The original server evidently supplied an inciter; reconstructed: when none
    // is present, the transformed card itself is the acting card.
    public static class OfflineActionGuards
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(CardActions.TransformCard), "StaticExecute"),
                new HarmonyMethod(typeof(OfflineActionGuards).GetMethod("TransformPrefix")), null, null, null, null);
        }

        public static void TransformPrefix(ref ServerCard inciterCard, ServerCard fromServerCard)
        {
            if (inciterCard == null) inciterCard = fromServerCard;
        }
    }
}
