using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // A creature or support's Summon ability triggers when it enters through an effect, not only when played from hand. Tokens, random
    // summons, Mobilize Recruits and creatures summoned from a deck or discard pile all count. The shipped client fires Summon
    // only from the (stubbed) play sequence and game-start summons; the server-side dispatch for effect summons is absent.
    // Every native effect summon (CreateToken, ReturnToBoard, ReturnToBoardNewOwner) reports through
    // ServerEffectUtilities.TriggerWhenCardEntersBoard, so a postfix there restores the rule. The play sequence fires Summon
    // itself; the card currently being played is excluded so its Summon runs exactly once. Lane moves do not use this path.
    public static class OfflineSummonRestoration
    {
        [System.ThreadStatic] private static ServerCard playing;

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ServerEffectUtilities), "TriggerWhenCardEntersBoard"), null,
                new HarmonyMethod(typeof(OfflineSummonRestoration).GetMethod("EntersBoardPostfix")), null, null, null);
        }

        public static ServerCard BeginPlay(ServerCard card) { ServerCard previous = playing; playing = card; return previous; }
        public static void EndPlay(ServerCard previous) { playing = previous; }

        public static void EntersBoardPostfix(ServerEffectUtilities.TriggerGameState gameState, ServerCard movedCard)
        {
            if (movedCard == null || movedCard == playing || movedCard.CurrentGameZoneType != GameZoneType.Board
                || (!movedCard.HasSubtype(CardSubtype.Creature) && !movedCard.HasSubtype(CardSubtype.Support)) || movedCard.TriggeredEffects.Count == 0) return;
            ServerEffectUtilities.TriggerDirectEffectEvent(gameState, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, movedCard, gameState.Players));
        }
    }
}
