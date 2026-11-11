using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Server lifecycle coverage; native card templates are unchanged.
    public static class AdditionalLifecycleTests
    {
        private const int Mudcrab = 620729069, Leviathan = 1427457192, Greaves = -1975997249, Marauder = 1297481327;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("glass-greaves-plot-first-vs-second-paid-play", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    EmptyHand(f, me);
                    ServerCard wielder = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                    ServerCard first = f.Create(Greaves, me, GameZoneName.Hand), second = f.Create(Greaves, me, GameZoneName.Hand);
                    ServerCard a = f.Create(Leviathan, me, GameZoneName.Hand), b = f.Create(Leviathan, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    Require(s.Players[me].NumCardsPlayedThisTurn == 0, "Plot fixture already played a card.");
                    int ac = Cost(a), bc = Cost(b), sc = Cost(second), fc = Cost(first);
                    f.PlayEquip(first, wielder, me);
                    Require(Cost(a) == ac && Cost(b) == bc && Cost(second) == sc, "Plot fired on first card played.");
                    Require(s.Players[me].QueryAttribute(AttributeType.Mana).Value == 30 - fc, "First Greaves payment differs.");
                    f.Refresh(me); f.PlayEquip(second, wielder, me);
                    int ad = ac - Cost(a), bd = bc - Cost(b);
                    Require((ad == 1 && bd == 0) || (ad == 0 && bd == 1), "Second Greaves must reduce exactly one remaining hand card by 1; deltas=" + ad + "," + bd);
                    Require(f.HandCount(me) == 2 && s.Players[me].NumCardsPlayedThisTurn == 2
                        && s.Players[me].QueryAttribute(AttributeType.Mana).Value == 30 - fc - sc, "Second Greaves play/payment differs.");
                }
            });
            test("covenant-marauder-empty-hand-aura-last-play-and-native-draw", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    EmptyHand(f, me);
                    ServerCard marauder = f.Create(Marauder, me, GameZoneName.Hand);
                    ServerCard last = f.Create(Leviathan, me, GameZoneName.Hand);
                    int baseAttack = marauder.QueryAttribute(AttributeType.Attack).Value;
                    f.Mana(me, 30); f.Refresh(me); f.Play(marauder, null, me);
                    Require(f.HandCount(me) == 1 && marauder.QueryAttribute(AttributeType.Attack).Value == baseAttack,
                        "Marauder aura active while a card remains in hand.");
                    int mana = s.Players[me].QueryAttribute(AttributeType.Mana).Value, cost = Cost(last);
                    f.Refresh(me); f.Play(last, null, me);
                    Require(f.HandCount(me) == 0 && marauder.QueryAttribute(AttributeType.Attack).Value == baseAttack + 2,
                        "Marauder did not gain exactly +2 attack after last paid play.");
                    Require(s.Players[me].QueryAttribute(AttributeType.Mana).Value == mana - cost, "Last hand card was not paid for.");
                    int deck = f.DeckCount(me);
                    f.Event(delegate { PlayerActions.DrawCards.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                        s.Players[me], 1, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, false, -1); });
                    for (int i = 0; i < 16; i++) LifecycleRestoration.FixedUpdate(s);
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    Require(f.HandCount(me) == 1 && f.DeckCount(me) == deck - 1 && marauder.QueryAttribute(AttributeType.Attack).Value == baseAttack,
                        "Native draw failed to remove Marauder's conditional +2 attack.");
                }
            });
        }
        private static int Cost(ServerCard card) { return card.QueryAttribute(AttributeType.ManaCost).Value; }
        private static void EmptyHand(OriginalTargetedSummonTests.LifecycleFixture f, int me)
        {
            Simulation s = f.Sim;
            List<ServerCard> hand = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[me].NetId, GameZoneName.Hand);
            f.Event(delegate {
                foreach (ServerCard c in hand) ServerCardCatalog.MoveToCemetery(DecisionRestoration.State(s, s._turnStateStack.Peek()), c);
            });
            Require(f.HandCount(me) == 0, "Failed to prepare empty hand.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
