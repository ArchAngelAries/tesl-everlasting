using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Double cards (Subtype Double, two DoubleCardReferences) split into their two halves once they are in hand. The original
    // server did this after a draw; the client only animates it (HistoryType.ActionSplitDoubleCard -> SequenceSplitDoubleCard).
    // The split is the game's own CardActions.SplitDoubleCard.StaticExecute: both halves are created in hand, linked
    // (LinkType.SharedDoubleCard), and the double card moves to purgatory. Summoning a double card from the deck (ReturnToBoard)
    // already splits in the shared code.
    // When: SplitHands runs right before a player's move list is built (every decision point), so a double card drawn by any
    // means is never offered whole. (A Harmony patch on PlayerActions.DrawCards.StaticExecute hangs the Mono runtime while
    // detouring, so the draw itself is not patched.)
    public static class OfflineDoubleCards
    {
        public static int Splits;

        public static List<ServerCard> SplitHands(ServerEffectUtilities.TriggerGameState state)
        {
            List<ServerCard> made = new List<ServerCard>();
            if (state.CardCatalog == null || state.Players == null) return made;
            foreach (PlayerState player in state.Players)
            {
                List<ServerCard> doubles = state.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c != null && c.HasSubtype(CardSubtype.Double); },
                    player.NetId, GameZoneName.Hand);
                foreach (ServerCard card in doubles)
                {
                    List<ServerCard> halves = CardActions.SplitDoubleCard.StaticExecute(state, card, state.Log);
                    if (halves != null && halves.Count > 0) { made.AddRange(halves); Splits++; }
                }
            }
            return made;
        }

        public static List<ServerCard> SplitHands(Simulation s, TurnState t) { return SplitHands(DecisionRestoration.State(s, t)); }
    }
}
