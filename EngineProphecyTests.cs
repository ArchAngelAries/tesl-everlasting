using System;
using System.Collections.Generic;
using System.Reflection;
using CcgRpc;
using Google.Protobuf;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Parent supplies a fresh existing native Fixture at its first main-loop
    // decision. Instance-only Prophecy modifiers isolate orchestration from the
    // still-incomplete original per-card rule recovery; no config is changed.
    public static class EngineProphecyTests
    {
        private static bool captureRequested;
        private static Decision captured;

        public static void AssertOrdinaryMultiRune(Simulation sim)
        {
            Prepare(sim);
            PlayerState player = sim.Players[1];
            int deck = sim.CardCatalog.FindCardCountInDeck(player.NetId);
            int hand = sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
            int drawn = player.MatchStatValues[18];
            DamageTo(sim, player, 18);
            Until(sim, delegate { return Ready(sim); }, "Two ordinary rune draws did not restore the parent turn.");
            Require(player.QueryAttribute(AttributeType.NumRunes).Value == 3
                && player.QueryAttribute(AttributeType.NumRunesLost).Value == 2, "Two thresholds did not consume exactly two runes.");
            Require(sim.CardCatalog.FindCardCountInDeck(player.NetId) == deck - 2
                && sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == hand + 2
                && player.MatchStatValues[18] == drawn + 2, "Two rune draws lost cards or double-counted native draw stats.");
            // A second settled pump must not draw again or repeat rune events.
            for (int tick = 0; tick < 8; tick++) sim.FixedUpdate();
            Require(sim.CardCatalog.FindCardCountInDeck(player.NetId) == deck - 2, "Settled rune thresholds repeated a draw.");
            AssertReplay(sim, 2, 0, 0);
        }

        public static void AssertProphecyChoice(Simulation sim, bool play, bool twoRunes)
        { AssertProphecyChoiceCore(sim, play, twoRunes, false); }

        public static void AssertNestedRuneDuringProphecy(Simulation sim)
        { AssertProphecyChoiceCore(sim, true, false, true); }

        // Supply a fresh existing Fixture started with twelve original Crocodile
        // cards. No instance keyword/stat/effect is modified by this test.
        public static void AssertOriginalCrocodile(Simulation sim, bool play)
        {
            Prepare(sim);
            NativeKeywordRuleTests.AssertMetadataGate(GameManager.Instance.Config);
            PlayerState player = sim.Players[1];
            ServerCard card = Deck(sim, player)[0];
            Require(card.CardId == NativeKeywordRules.LurkingCrocodileId && card.GetCardName() == "Lurking Crocodile"
                && card.HasSubtype(CardSubtype.Creature) && card.QueryAttribute(AttributeType.Prophecy).Value == 1
                && card.QueryAttribute(AttributeType.ManaCost).Value == 2 && card.QueryAttribute(AttributeType.Attack).Value == 3
                && card.QueryAttribute(AttributeType.MaxHealth).Value == 2 && card.CurrentHealth == 2,
                "Original Crocodile instance metadata differs before rune draw.");
            int hand = sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
            int deck = sim.CardCatalog.FindCardCountInDeck(player.NetId), drawn = player.MatchStatValues[18];
            int mana = player.QueryAttribute(AttributeType.Mana).Value;
            DamageTo(sim, player, 25);
            Until(sim, delegate { return WindowReady(sim); }, "Original Crocodile did not enter native rune prophecy window.");
            TurnState window = sim._turnStateStack.Peek();
            CardCommandList list = window.CurrentCommandItem.CommandList;
            Require(card.CurrentGameZoneName == GameZoneName.Prophecy && player.MatchStatValues[18] == drawn
                && list.ValidCommands.Count == 1 && list.ValidCommands[0].CardNetUid == card.NetUid.Id
                && (list.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.PlayToPosition) != 0
                && (list.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.PlayToHand) != 0,
                "Original Crocodile prophecy does not offer native creature placement plus decline.");
            Decision decision = play ? DecisionRestoration.ChooseDecision(sim, list) : ProduceDecline(list.ListUid, card.NetUid);
            Require(CommandRestoration.ValidateDecision(decision, list, sim.CardCatalog, player), "Original Crocodile choice was rejected.");
            sim.PlayerServiceClients[1]._decisionMessages.Enqueue(decision);
            Until(sim, delegate { return Ready(sim); }, "Original Crocodile choice did not resume attacker turn.");
            Require(sim.CardCatalog.FindCardCountInDeck(player.NetId) == deck - 1 && player.MatchStatValues[18] == drawn + 1
                && sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == hand + (play ? 0 : 1)
                && card.CurrentGameZoneName == (play ? GameZoneName.CreatureBoard : GameZoneName.Hand)
                && player.QueryAttribute(AttributeType.Mana).Value == mana
                && player.QueryAttribute(AttributeType.PropheciesFromRuneBreak).Value == 1
                && player.QueryAttribute(AttributeType.NumRunes).Value == 4 && Avatar(sim, player).CurrentHealth == 25
                && Avatar(sim, sim.Players[0]).CurrentHealth == 30
                && card.QueryAttribute(AttributeType.Prophecy).Value == 1 && card.QueryAttribute(AttributeType.Attack).Value == 3
                && card.QueryAttribute(AttributeType.ManaCost).Value == 2 && card.CurrentHealth == 2,
                "Original Crocodile resolution changed original stats, mana, native draw counts or introduced absent summon damage.");
            if (play) Require(card.QueryGameJournalCount(CardJournal.PlayedAsProphecy) == 1 && card.IsSleeping(),
                "Original Crocodile free play did not record prophecy or preserve normal creature sleep.");
            AssertReplay(sim, 1, 1, play ? 0 : 1);
        }

        private static void AssertProphecyChoiceCore(Simulation sim, bool play, bool twoRunes, bool nestedRune)
        {
            Prepare(sim);
            TurnState parent = sim._turnStateStack.Peek();
            PlayerState player = sim.Players[1];
            ServerCard card = Deck(sim, player)[0];
            // Test-only modifier on one reviewed Alit instance, not a claim that
            // Alit's original card has Prophecy. It keeps native cost/summon rules.
            card.SetAttribute(AttributeType.Prophecy, 1);
            int oldUid = card.NetUid.Id;
            int hand = sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
            int deck = sim.CardCatalog.FindCardCountInDeck(player.NetId);
            int mana = player.QueryAttribute(AttributeType.Mana).Value;
            int drawn = player.MatchStatValues[18];
            DamageTo(sim, player, nestedRune ? 21 : (twoRunes ? 18 : 25));
            if (twoRunes)
            {
                // Synthetic native no-op contexts validate suspension of parent
                // Active/Pending queues while the first prophecy owns input.
                sim.CheckForRuneBreaks();
                TriggeredEffect sentinel = new TriggeredEffect { TriggerType = EffectTriggerType.ActionEffect };
                ServerEffectUtilities.TriggerContext context = new ServerEffectUtilities.TriggerContext {
                    EffectOwner = Avatar(sim, sim.Players[0]), TriggeredEffect = sentinel,
                    TriggerEvent = new ServerEffectUtilities.TriggerEvent(EffectTriggerType.ActionEffect, sim.Players[0]) };
                parent.EffectTriggerQueues.Active.Enqueue(context);
                parent.EffectTriggerQueues.Pending.Add(context);
            }
            Until(sim, delegate { return WindowReady(sim); }, "Rune did not expose a settled native prophecy command.");
            TurnState window = sim._turnStateStack.Peek();
            CardCommandList list = window.CurrentCommandItem.CommandList;
            Require(sim._turnStateStack.Count == 2 && window.ActivePlayerIndex == 1 && window.TurnPlayerIndex == 0
                && Object.ReferenceEquals(parent, sim._turnStateStack.ToArray()[1])
                && sim.CurrentPlayer == player && sim.Players[0].IsMyTurn && !player.IsMyTurn,
                "Prophecy changed actual turn ownership or failed to suspend original turn.");
            Require(card.CurrentGameZoneName == GameZoneName.Prophecy && card.NetUid.Id != oldUid
                && sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == hand
                && sim.CardCatalog.FindCardCountInDeck(player.NetId) == deck - 1
                && player.MatchStatValues[18] == drawn,
                "Entering native prophecy must preserve hand and defer native Drawn stat until resolution.");
            Require(list.MustSelectCommand && list.ValidCommands.Count == 1
                && (list.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.PlayToHand) != 0,
                "Native prophecy offer does not contain exactly the drawn card and return-to-hand alternative.");
            int suspendedDraws = parent.EffectTriggerQueues.CardDrawInterrupt.Count;
            Require(suspendedDraws == (twoRunes ? 1 : 0), "Parent rune draw queue was consumed while prophecy awaited input.");
            int suspendedActive = parent.EffectTriggerQueues.Active.Count;
            int suspendedPending = parent.EffectTriggerQueues.Pending.Count;
            if (twoRunes) Require(suspendedActive >= 1 && suspendedPending >= 1,
                "Synthetic parent Active/Pending contexts ran before suspended prophecy completed.");
            for (int tick = 0; tick < 6; tick++) sim.FixedUpdate();
            Require(WindowReady(sim) && parent.EffectTriggerQueues.CardDrawInterrupt.Count == suspendedDraws
                && parent.EffectTriggerQueues.Active.Count == suspendedActive
                && parent.EffectTriggerQueues.Pending.Count == suspendedPending,
                "Idle prophecy mutated the suspended parent's effects/queued draws.");
            Decision decision = play ? DecisionRestoration.ChooseDecision(sim, list) : ProduceDecline(list.ListUid, card.NetUid);
            Require(decision.Type == (play ? CcgRpc.DecisionType.Play : CcgRpc.DecisionType.ToHand)
                && CommandRestoration.ValidateDecision(decision, list, sim.CardCatalog, player), "Native prophecy choice was not validated.");
            Decision stale = decision.Clone(); stale.CommandUid--;
            Require(!CommandRestoration.ValidateDecision(stale, list, sim.CardCatalog, player), "Stale prophecy list UID was accepted.");
            Decision wrong = decision.Clone(); wrong.ActorCardNetUid = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; },
                sim.Players[0].NetId, GameZoneName.Hand)[0].NetUid.Id;
            Require(!CommandRestoration.ValidateDecision(wrong, list, sim.CardCatalog, player), "Opponent hand card was accepted as prophecy actor.");
            sim.PlayerServiceClients[1]._decisionMessages.Enqueue(decision);
            Until(sim, delegate { return Ready(sim); }, "Prophecy choice failed to settle and resume original turn.");
            Require(sim.CurrentPlayer == sim.Players[0] && sim.Players[0].IsMyTurn && !player.IsMyTurn
                && player.QueryAttribute(AttributeType.Mana).Value == mana, "Free prophecy changed mana or resumed wrong player.");
            int runeDraws = twoRunes || nestedRune ? 2 : 1;
            Require(sim.CardCatalog.FindCardCountInDeck(player.NetId) == deck - runeDraws
                && player.MatchStatValues[18] == drawn + runeDraws
                && player.QueryAttribute(AttributeType.PropheciesFromRuneBreak).Value == 1,
                "Resolved prophecy/native draw statistic or suspended second rune differs.");
            Require(card.CurrentGameZoneName == (play ? GameZoneName.CreatureBoard : GameZoneName.Hand), "Prophecy did not reach selected native destination.");
            Require(sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == hand + (play ? 0 : 1) + (runeDraws - 1),
                "Resolving prophecy duplicated or lost hand cards.");
            if (play) Require(card.QueryGameJournalCount(CardJournal.PlayedAsProphecy) == 1,
                "Native prophecy play journal was not recorded exactly once.");
            AssertReplay(sim, runeDraws, 1, play ? 0 : 1);
        }

        public static void AssertFullHandBurn(Simulation sim)
        {
            Prepare(sim);
            PlayerState player = sim.Players[1];
            int need = player.FindHandSize() - sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
            Event(sim, delegate { PlayerActions.DrawCards.StaticExecute(DecisionRestoration.State(sim, sim._turnStateStack.Peek()),
                player, need, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.Untyped, false, false, -1); });
            Until(sim, delegate { return Ready(sim); }, "Full-hand preparation did not settle.");
            List<ServerCard> deck = Deck(sim, player);
            Require(deck.Count > 0, "Burn fixture needs one remaining deck card.");
            ServerCard card = deck[0]; card.SetAttribute(AttributeType.Prophecy, 1);
            int count = deck.Count;
            DamageTo(sim, player, 25);
            Until(sim, delegate { return Ready(sim); }, "Full-hand rune draw did not settle.");
            Require(sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == player.FindHandSize()
                && sim.CardCatalog.FindCardCountInDeck(player.NetId) == count - 1
                && card.CurrentGameZoneName == GameZoneName.Cemetery
                && sim.CardCatalog.FindCardCount(GameZoneName.Prophecy, player.NetId) == 0,
                "Full-hand rune did not preserve native revealed burn/hand limit.");
            bool burned = false;
            HistoryQueue replay = sim.HistoryLog.GenerateFullReplayForPlayer(sim.PlayerServiceClients[1], HistoryLog.FullGameStateSendRule.Exclude);
            foreach (HistoryItem item in replay.Queue) if (item.Type == HistoryType.ActionDrawHandFull
                && item.SanitizeRule == SanitizeRule.ShowHiddenInfoToAll && item.AffectedCardsArray.Count == 1) burned = true;
            Require(burned, "Full-hand native reveal/burn action is absent from replay.");
            AssertReplay(sim, 1, 0, 0);
        }

        public static void AssertEmptyDeck(Simulation sim)
        {
            Prepare(sim);
            PlayerState player = sim.Players[1];
            Event(sim, delegate
            {
                ServerEffectUtilities.TriggerGameState state = DecisionRestoration.State(sim, sim._turnStateStack.Peek());
                foreach (ServerCard card in Deck(sim, player))
                    ServerCardCatalog.MoveToCemetery(state, card, ServerCardCatalog.MoveOperationType.Untyped);
            });
            Until(sim, delegate { return Ready(sim); }, "Empty-deck preparation did not settle.");
            int hand = sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
            DamageTo(sim, player, 25);
            Until(sim, delegate { return Ready(sim); }, "Empty-deck rune draw did not settle.");
            Require(sim.CardCatalog.FindCardCountInDeck(player.NetId) == 0
                && sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == hand
                && Avatar(sim, player).CurrentHealth == 20
                && player.QueryAttribute(AttributeType.NumRunes).Value == 3
                && player.QueryAttribute(AttributeType.NumRunesLost).Value == 2,
                "Empty rune draw did not reuse native next-rune loss/health behavior.");
            int empty = 0;
            HistoryQueue replay = sim.HistoryLog.GenerateFullReplayForPlayer(sim.PlayerServiceClients[1], HistoryLog.FullGameStateSendRule.Exclude);
            foreach (HistoryItem item in replay.Queue) if (item.Type == HistoryType.EventDeckEmptyDraw) empty++;
            Require(empty == 1, "Empty rune draw duplicated native deck-empty event.");
            AssertReplay(sim, 1, 0, 0);
        }

        private static Decision ProduceDecline(int listUid, CardNetUid cardUid)
        {
            Harmony probe = new Harmony("local.tesl.offline.tests.prophecyproducer");
            MethodInfo send = AccessTools.Method(typeof(GameNetworkManager), "SendDecision");
            probe.Patch(send, new HarmonyMethod(typeof(EngineProphecyTests).GetMethod("CapturePrefix")), null, null, null, null);
            captured = null; captureRequested = true;
            try
            {
                GameNetworkManager.Instance.PlayCardToHand(listUid, cardUid);
                Require(captured != null, "Native PlayCardToHand producer did not reach SendDecision.");
                return Decision.Parser.ParseFrom(captured.ToByteArray());
            }
            finally { captureRequested = false; captured = null; probe.Unpatch(send, HarmonyPatchType.Prefix, probe.Id); }
        }

        public static bool CapturePrefix(Decision __0)
        { if (!captureRequested) return true; captured = __0.Clone(); return false; }

        private static void Prepare(Simulation sim)
        {
            Require(Ready(sim) && sim.CurrentPlayer == sim.Players[0], "Rune test requires first settled native main turn.");
            sim._disableAiDecisions = true;
            Require(Avatar(sim, sim.Players[1]).CurrentHealth == 30
                && sim.Players[1].QueryAttribute(AttributeType.NumRunes).Value == 5, "Rune test requires native 30 health/five runes.");
        }

        private static List<ServerCard> Deck(Simulation sim, PlayerState player)
        { return sim.CardCatalog.GatherCardsInZone(delegate(ServerCard card) { return true; }, player.NetId, GameZoneName.Deck); }
        private static ServerCard Avatar(Simulation sim, PlayerState player)
        { return sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard card) { return card.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarBoard); }
        private static void DamageTo(Simulation sim, PlayerState player, int health)
        { Event(sim, delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(sim, sim._turnStateStack.Peek()),
            null, player, Avatar(sim, player).CurrentHealth - health, -1); }); }
        private static void Event(Simulation sim, Action action)
        {
            sim.HistoryLog.StartEvent(HistoryType.EventTrigger, sim.CurrentPlayer.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
            try { action(); } finally { sim.HistoryLog.EndEvent(); }
        }
        private static bool WindowReady(Simulation sim)
        {
            if (sim._turnStateStack.Count != 2) return false;
            TurnState turn = sim._turnStateStack.Peek();
            return turn.Phase == TurnPhase.Interrupt && turn.CurrentSequence == null && turn.CurrentCommandItem != null
                && !RuneProphecyRestoration.HasQueuedEffects(turn);
        }
        private static bool Ready(Simulation sim)
        {
            if (sim._gameState.GamePhase != GamePhase.MainLoop || sim._turnStateStack.Count != 1) return false;
            TurnState turn = sim._turnStateStack.Peek();
            return turn.Phase == TurnPhase.PlayCards && turn.CurrentSequence == null && turn.CurrentCommandItem != null
                && !RuneProphecyRestoration.HasQueuedEffects(turn);
        }
        private static void Until(Simulation sim, Func<bool> condition, string message)
        { for (int tick = 0; tick < 500; tick++) { sim.FixedUpdate(); if (condition()) return; } throw new InvalidOperationException(message); }
        private static void AssertReplay(Simulation sim, int runes, int prophecies, int declines)
        {
            foreach (PlayerServiceClient client in sim.PlayerServiceClients)
            {
                int depth = 0, runeCount = 0, prophecyCount = 0, declineCount = 0;
                HistoryQueue replay = sim.HistoryLog.GenerateFullReplayForPlayer(client, HistoryLog.FullGameStateSendRule.Exclude);
                foreach (HistoryItem item in replay.Queue)
                {
                    if (HistoryUtilities.IsHeader(item.Type)) { depth++; if (item.Type == HistoryType.EventRuneBreak) runeCount++;
                        if (item.Type == HistoryType.EventDeclineProphecy) declineCount++; }
                    if (item.Type == HistoryType.ActionMoveCards && item.UseProphecyBanner) prophecyCount++;
                    if (HistoryUtilities.IsFooter(item.Type)) { depth--; Require(depth >= 0, "Rune replay has unmatched footer."); }
                }
                Require(depth == 0 && runeCount == runes && prophecyCount == prophecies && declineCount == declines,
                    "Rune/prophecy recipient replay event counts or balanced boundaries differ.");
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
