using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Bounded headless tests of native default startup/actions. AI descriptions avoid
    // sockets; the deterministic chooser is a testing opponent, not restored AI.
    public static class EngineLifecycleTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("native-original-crocodile-keyword-metadata-gate", delegate { NativeKeywordRuleTests.AssertMetadataGate(config); });
            test("native-original-crocodile-prophecy-decline", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(12, NativeKeywordRules.LurkingCrocodileId); f.Until(delegate { return Ready(f.Sim); }, "Original prophecy fixture startup failed."); EngineProphecyTests.AssertOriginalCrocodile(f.Sim, false); }
            });
            test("native-original-crocodile-prophecy-free-play", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(12, NativeKeywordRules.LurkingCrocodileId); f.Until(delegate { return Ready(f.Sim); }, "Original prophecy fixture startup failed."); EngineProphecyTests.AssertOriginalCrocodile(f.Sim, true); }
            });
            test("native-runes-multiple-draws-and-parent-resumption", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(); f.Until(delegate { return Ready(f.Sim); }, "Rune fixture startup failed."); EngineProphecyTests.AssertOrdinaryMultiRune(f.Sim); }
            });
            test("native-prophecy-decline-packet-control-and-history", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(); f.Until(delegate { return Ready(f.Sim); }, "Prophecy fixture startup failed."); EngineProphecyTests.AssertProphecyChoice(f.Sim, false, false); }
            });
            test("native-prophecy-free-play-with-suspended-second-rune", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(); f.Until(delegate { return Ready(f.Sim); }, "Prophecy fixture startup failed."); EngineProphecyTests.AssertProphecyChoice(f.Sim, true, true); }
            });
            test("native-rune-full-hand-burn", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(); f.Until(delegate { return Ready(f.Sim); }, "Rune burn fixture startup failed."); EngineProphecyTests.AssertFullHandBurn(f.Sim); }
            });
            test("native-prophecy-nested-rune-control-resumption", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(); f.Until(delegate { return Ready(f.Sim); }, "Nested prophecy fixture startup failed."); EngineProphecyTests.AssertNestedRuneDuringProphecy(f.Sim); }
            });
            test("native-rune-empty-deck-chain", delegate {
                using (Fixture f = new Fixture(config)) { f.Start(); f.Until(delegate { return Ready(f.Sim); }, "Empty deck fixture startup failed."); EngineProphecyTests.AssertEmptyDeck(f.Sim); }
            });
            test("native-reconstructed-strategy-opponents-complete-match", delegate {
                using (Fixture f = new Fixture(config))
                {
                    f.Start(50);
                    for (int tick = 0; tick < 5000 && f.Sim._gameState.GamePhase != GamePhase.Results; tick++) LifecycleRestoration.FixedUpdate(f.Sim);
                    Require(f.Sim._gameState.GamePhase == GamePhase.Results, "Reconstructed strategic opponents did not complete a match. " + f.Summary());
                    Require(f.Sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value > 1 && f.Sim.Players[1].QueryAttribute(AttributeType.TurnCount).Value > 1,
                        "Diagnostic match ended before actual alternating play.");
                    Require(f.Sim.Players[0].QueryAttribute(AttributeType.NumRunesLost).Value + f.Sim.Players[1].QueryAttribute(AttributeType.NumRunesLost).Value > 0,
                        "Diagnostic match did not exercise rune draws.");
                }
            });
            // Soak: complete AI-vs-AI matches with the game's real AI decks (many cards, Prophecies, rune breaks). A match that
            // throws, or whose state stops changing for 3000 ticks (a stall), fails with where it stopped.
            test("soak-real-deck-ai-matches-complete", delegate
            {
                List<OfflineDeckCatalog.Entry> decks = OfflineDeckCatalog.Create(config).FindAll(delegate(OfflineDeckCatalog.Entry e) { return e.CanPlay && e.Deck != null; });
                Require(decks.Count >= 4, "too few playable decks for the soak: " + decks.Count);
                List<string> problems = new List<string>();
                int matches = Math.Min(24, decks.Count), completed = 0;
                for (int m = 0; m < matches; m++)
                {
                    OfflineDeckCatalog.Entry a = decks[m % decks.Count], b = decks[(m * 7 + 3) % decks.Count];
                    List<int> da = new List<int>(), db = new List<int>();
                    foreach (DeckCardInfo c in a.Deck.Cards) if (c != null) for (int n = 0; n < Math.Max(1, c.Count); n++) da.Add(c.Id);
                    foreach (DeckCardInfo c in b.Deck.Cards) if (c != null) for (int n = 0; n < Math.Max(1, c.Count); n++) db.Add(c.Id);
                    string label = a.Name + " vs " + b.Name + " (seed " + (1000 + m) + ")";
                    try
                    {
                        using (Fixture f = new Fixture(config))
                        {
                            f.Start(da, db, 1000 + m);
                            string last = ""; int same = 0;
                            for (int tick = 0; tick < 30000 && f.Sim._gameState.GamePhase != GamePhase.Results; tick++)
                            {
                                LifecycleRestoration.FixedUpdate(f.Sim);
                                string now = f.Summary() + "depth=" + f.Sim._turnStateStack.Count + ";dyn=" + f.Sim.CardCatalog.GatherDynamicCardList().Count;
                                if (now == last) same++; else { same = 0; last = now; }
                                if (same >= 3000) break;
                            }
                            if (f.Sim._gameState.GamePhase == GamePhase.Results) completed++;
                            else
                            {
                                TurnState top = f.Sim._turnStateStack.Count > 0 ? f.Sim._turnStateStack.Peek() : null;
                                problems.Add(label + ": stalled; " + f.Summary() + " depth=" + f.Sim._turnStateStack.Count
                                    + " sequence=" + (top == null || top.CurrentSequence == null ? "none" : top.CurrentSequence.Type + "/" + top.CurrentSequence.Stage)
                                    + " commands=" + (top == null || top.CurrentCommandItem == null ? "none" : top.CurrentCommandItem.CommandList.ValidCommands.Count.ToString())
                                    + " interrupts=" + (top == null ? 0 : top.EffectTriggerQueues.CardDrawInterrupt.Count)
                                    + " prophecy=" + f.Sim.CardCatalog.FindCardCount(GameZoneName.Prophecy, f.Sim.Players[0].NetId) + "/" + f.Sim.CardCatalog.FindCardCount(GameZoneName.Prophecy, f.Sim.Players[1].NetId)
                                    + " lastRejection=" + DecisionRestoration.LastPlayRejection);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Exception inner = e; while (inner.InnerException != null) inner = inner.InnerException;
                        problems.Add(label + ": " + inner.GetType().Name + ": " + inner.Message + " @ " + (inner.StackTrace ?? "").Split('\n')[0].Trim());
                    }
                }
                UnityEngine.Debug.Log("SOAK: " + completed + " of " + matches + " matches completed; problems " + problems.Count + (problems.Count > 0 ? ": " + string.Join(" || ", problems.ToArray()) : ""));
                Require(problems.Count == 0, problems.Count + " of " + matches + " soak matches failed: " + string.Join(" || ", problems.ToArray()));
            });
            test("native-mulligan-producer-validation-and-redraw", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    f.Start();
                    f.Sim._disableAiDecisions = true;
                    f.Until(delegate { return f.Sim._gameState.GamePhase == GamePhase.Mulligan; }, "Native test mulligan did not arrive.");
                    EngineMulliganTests.AssertNativeProtocol(f.Sim);
                    f.Until(delegate { return Ready(f.Sim); }, "Validated native mulligan did not enter main turn.");
                }
            });
            test("native-default-lifecycle-scenario-fixture-startup", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    f.Start();
                    Require(f.Sim.Players.Count == 2 && !Object.ReferenceEquals(f.Sim.Players[0], f.Sim.Players[1]), "Player registration did not create independent states.");
                    Require(f.Sim._gameState.GamePhase == GamePhase.GatherPlayers, "Native lifecycle did not enter GatherPlayers.");
                    f.Until(delegate { return Ready(f.Sim); }, "Startup did not reach a settled native main turn.");
                    EngineIntroTests.AssertStartupHistory(f.Sim);
                    Require(f.Sim._gameState.GamePhase == GamePhase.MainLoop, "Default startup did not enter MainLoop.");
                    Require(f.Sim.Players[0].MulliganState == PlayerMulliganState.Mulligan_Complete
                        && f.Sim.Players[1].MulliganState == PlayerMulliganState.Mulligan_Complete, "AI keep-all mulligan did not settle.");
                    Require(f.Avatar(0) != null && f.Avatar(1) != null, "Default game-start actions did not place both avatars on board.");
                    Require(f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[0].NetId) == 4
                        && f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[1].NetId) == 3,
                        "Default opening draw plus first-turn draw differs from 4/3 cards. " + f.Summary());
                    Require(f.Sim.CardCatalog.FindCardCountInDeck(f.Sim.Players[0].NetId) == 8
                        && f.Sim.CardCatalog.FindCardCountInDeck(f.Sim.Players[1].NetId) == 9, "Native opening draw lost or duplicated deck cards. " + f.Summary());
                    Require(f.Sim.CurrentPlayer == f.Sim.Players[0] && f.Sim.Players[0].IsMyTurn && !f.Sim.Players[1].IsMyTurn,
                        "First-turn player control differs.");
                    Require(f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == 1
                        && f.Sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value == 1
                        && f.Sim.Players[1].QueryAttribute(AttributeType.TurnCount).Value == 0,
                        "Turn-start magicka or per-player turn counters differ. " + f.Summary());
                }
            });
            test("native-lifecycle-play-and-turn-alternation", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    f.Start();
                    f.Until(delegate { return Ready(f.Sim); }, "Startup did not reach first decision.");
                    f.Sim._disableAiDecisions = true; // Inject validated local decisions deterministically.
                    // Alit costs two magicka in the installed client. Pass each
                    // player's first turn and allow turn-start orchestration
                    // to produce player one's second-turn magicka.
                    Require(f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == 1,
                        "Native first-turn magicka differs from one. " + f.Summary());
                    f.EndTurnTo(1);
                    f.EndTurnTo(0);
                    TurnState turn = f.Sim._turnStateStack.Peek();
                    CardCommandList list = turn.CurrentCommandItem.CommandList;
                    Decision play = DecisionRestoration.ChooseDecision(f.Sim, list);
                    Require(play.Type == CcgRpc.DecisionType.Play, "Reviewed deck did not offer a playable card on player one's second turn. " + f.Summary());
                    ServerCard card = f.Sim.CardCatalog.FindDynamicCard(new CardNetUid(play.ActorCardNetUid));
                    Require(card != null && card.GetCardName() == "Afflicted Alit", "Test selected a card outside its reviewed deck.");
                    int healthOne = f.Avatar(0).CurrentHealth, healthTwo = f.Avatar(1).CurrentHealth;
                    Require(healthOne > 27 && healthTwo > 27, "Selected test avatar cannot safely test damage before the rune threshold.");
                    int mana = f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value;
                    int cost = card.QueryAttribute(AttributeType.ManaCost).Value;
                    Require(cost == 2 && mana == 2, "Alit/native second-turn magicka differs from two. " + f.Summary());
                    int handBefore = f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[0].NetId);
                    int secondHandBefore = f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[1].NetId);
                    int secondDeckBefore = f.Sim.CardCatalog.FindCardCountInDeck(f.Sim.Players[1].NetId);
                    int oldListUid = list.ListUid;
                    f.Sim.PlayerServiceClients[0]._decisionMessages.Enqueue(play);
                    // Require command replacement and settled sequence, not merely a zone mutation.
                    f.Until(delegate { return Ready(f.Sim) && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != oldListUid; },
                        "Native play sequence did not settle with refreshed commands.");
                    Require(card.CurrentGameZoneName == GameZoneName.CreatureBoard
                        && f.Sim.CardCatalog.FindCardCount(GameZoneName.CreatureBoard, f.Sim.Players[0].NetId) == 1,
                        "Play sequence did not place exactly one creature.");
                    Require(f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == mana - cost
                        && f.Sim.Players[0].NumCardsPlayedThisTurn == 1, "Casting cost or play count was not applied exactly once.");
                    Require(f.Avatar(0).CurrentHealth == healthOne - 2 && f.Avatar(1).CurrentHealth == healthTwo - 2,
                        "Native Alit summon did not deal exactly two damage to each player through the lifecycle.");
                    Require(f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[0].NetId) == handBefore - 1, "Play did not remove exactly one hand card. " + f.Summary());
                    f.EndTurnTo(1);
                    Require(!f.Sim.Players[0].IsMyTurn && f.Sim.Players[1].IsMyTurn, "Turn ownership did not alternate.");
                    Require(f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[1].NetId) == secondHandBefore + 1
                        && f.Sim.CardCatalog.FindCardCountInDeck(f.Sim.Players[1].NetId) == secondDeckBefore - 1, "Second player's native turn-start draw differs. " + f.Summary());
                    Require(f.Sim.Players[1].QueryAttribute(AttributeType.Mana).Value > 0, "Second native turn-start did not supply mana.");
                    Require(f.Avatar(0).CurrentHealth == healthOne - 2 && f.Avatar(1).CurrentHealth == healthTwo - 2,
                        "Turn transition repeated the prior play's summon effect.");
                }
            });
            test("native-selected-slot-front-middle-and-end", delegate
            {
                for (int slot = 0; slot <= 3; slot++)
                    using (Fixture f = new Fixture(config))
                    {
                        ServerCard actor = f.PrepareInsertionBoard(3);
                        GameZone board = f.Sim.CardCatalog.FindGameZone(f.Sim.Players[0].NetId, GameZoneName.CreatureBoard);
                        List<CardInstanceId> expected = new List<CardInstanceId>(board.LaneList[0]);
                        expected.Insert(slot, actor.InstanceId);
                        Decision play = f.InsertionDecision(actor, slot);
                        int mana = f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value;
                        f.PlayAndSettle(play);
                        f.RequireOrder(expected);
                        Require(board.FindCardPosition(actor.InstanceId).CardIndex == slot
                            && actor.HistoryCreate_FullStateCard(f.Sim.CardCatalog).CurrentPosition.PositionIndex == slot,
                            "Native model/history did not preserve selected slot " + slot + ".");
                        Require(f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == mana - 2
                            && f.Sim.Players[0].NumCardsPlayedThisTurn == 1, "Selected-slot play charged the wrong cost/count.");
                    }
            });
            test("native-full-lane-sacrifice-insertion-boundaries", delegate
            {
                for (int slot = 0; slot <= 4; slot++)
                    using (Fixture f = new Fixture(config))
                    {
                        ServerCard actor = f.PrepareInsertionBoard(4);
                        GameZone board = f.Sim.CardCatalog.FindGameZone(f.Sim.Players[0].NetId, GameZoneName.CreatureBoard);
                        List<CardInstanceId> expected = new List<CardInstanceId>(board.LaneList[0]);
                        ServerCard discard = f.Sim.CardCatalog.FindDynamicCard(expected[1]);
                        expected.RemoveAt(1);
                        int adjusted = slot > 1 ? slot - 1 : slot;
                        expected.Insert(adjusted, actor.InstanceId);
                        Decision play = f.InsertionDecision(actor, slot);
                        play.DiscardCardNetUids.Add(discard.NetUid.Id);
                        f.PlayAndSettle(play);
                        f.RequireOrder(expected);
                        Require(discard.CurrentGameZoneName != GameZoneName.CreatureBoard
                            && actor.HistoryCreate_FullStateCard(f.Sim.CardCatalog).CurrentPosition.PositionIndex == adjusted,
                            "Sacrifice did not adjust the original UI boundary " + slot + ".");
                        Require(f.Sim.Players[0].NumCardsPlayedThisTurn == 1,
                            "Sacrifice replacement counted the play more than once.");
                    }
            });
            test("native-invalid-slot-rejected-before-play-mutation", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard actor = f.PrepareInsertionBoard(3);
                    foreach (int slot in new int[] { -1, 4, Int32.MaxValue })
                        f.RejectInsertion(f.InsertionDecision(actor, slot), actor);
                    Decision stale = f.InsertionDecision(actor, 0); stale.CommandUid--;
                    f.RejectInsertion(stale, actor);
                    // Execution must recheck a live boundary even after the command
                    // list was consumed by native SelectDecision.
                    TurnState t = f.Sim._turnStateStack.Peek();
                    t.CurrentSequence = new SequenceData { Type = SequenceType.Decision };
                    try
                    {
                        Require(DecisionRestoration.ProcessPlayCard(f.Sim, t, f.InsertionDecision(actor, 4)) == Simulation.DecisionStatus.Error
                            && !t.CurrentSequence.EventStarted, "Bad execution-time slot opened a play event.");
                    }
                    finally { t.CurrentSequence = null; }
                }
                using (Fixture f = new Fixture(config))
                {
                    ServerCard actor = f.PrepareInsertionBoard(4);
                    GameZone board = f.Sim.CardCatalog.FindGameZone(f.Sim.Players[0].NetId, GameZoneName.CreatureBoard);
                    ServerCard discard = f.Sim.CardCatalog.FindDynamicCard(board.LaneList[0].GetCard(1));
                    Decision missing = f.InsertionDecision(actor, 4);
                    f.RejectInsertion(missing, actor);
                    Decision invalid = f.InsertionDecision(actor, 5); invalid.DiscardCardNetUids.Add(discard.NetUid.Id);
                    f.RejectInsertion(invalid, actor);
                    Require(!discard.Sacrificed && discard.CurrentGameZoneName == GameZoneName.CreatureBoard,
                        "Invalid boundary sacrificed a creature before rejection.");
                    Decision wrongLane = f.InsertionDecision(actor, 0); wrongLane.LaneIndex = 1; wrongLane.DiscardCardNetUids.Add(discard.NetUid.Id);
                    f.RejectInsertion(wrongLane, actor);
                    discard.SetAttribute(AttributeType.Indestructible, 1);
                    Decision indestructible = f.InsertionDecision(actor, 4); indestructible.DiscardCardNetUids.Add(discard.NetUid.Id);
                    f.RejectInsertion(indestructible, actor);
                }
            });
            test("native-unsupported-movement-gates-before-sacrifice-and-payment", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard actor = f.PrepareInsertionBoard(4);
                    GameZone board = f.Sim.CardCatalog.FindGameZone(f.Sim.Players[0].NetId, GameZoneName.CreatureBoard);
                    List<CardInstanceId> order = new List<CardInstanceId>(board.LaneList[0]);
                    ServerCard sacrifice = f.Sim.CardCatalog.FindDynamicCard(order[1]);
                    Decision play = f.InsertionDecision(actor, 4); play.DiscardCardNetUids.Add(sacrifice.NetUid.Id);
                    Require(CommandRestoration.ValidateDecision(play, f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList,
                        f.Sim.CardCatalog, f.Sim.Players[0]), "Unsupported-movement fixture has invalid placement.");
                    // Deliberately unsupported movement: the source zone's definition is missing (instant movement triggers are
                    // supported since 2026-11-05, so this preflight case stands in for "unsupported").
                    List<GameZone> zones = GameManager.Instance.Config.Zones;
                    GameZone handDefinition = zones.Find(delegate(GameZone z) { return z.Name == GameZoneName.Hand; });
                    zones.Remove(handDefinition);
                    int mana = f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value;
                    int hand = f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[0].NetId);
                    TurnState t = f.Sim._turnStateStack.Peek();
                    t.CurrentSequence = new SequenceData { Type = SequenceType.Decision };
                    bool gated = false;
                    try { DecisionRestoration.ProcessPlayCard(f.Sim, t, play); }
                    catch (NotSupportedException) { gated = true; }
                    finally { if (handDefinition != null) zones.Add(handDefinition); }
                    Require(gated && !t.CurrentSequence.EventStarted && !sacrifice.Sacrificed
                        && actor.CurrentGameZoneName == GameZoneName.Hand
                        && f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == mana
                        && f.Sim.Players[0].NumCardsPlayedThisTurn == 0
                        && f.Sim.CardCatalog.FindCardCount(GameZoneName.Hand, f.Sim.Players[0].NetId) == hand,
                        "Unsupported transfer mutated sacrifice, payment, hand, play count or history before failing.");
                    f.RequireOrder(order);
                    t.CurrentSequence = null;
                }
            });
            test("native-turn-magicka-growth-cap-bonus-and-refill", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    f.Start(32); // Enough native deck cards to avoid decking during 13 own turns.
                    f.Until(delegate { return Ready(f.Sim); }, "Startup did not reach magicka-growth test turn.");
                    f.Sim._disableAiDecisions = true;
                    for (int turn = 1; turn <= Simulation.MaxManaUnbuffed_; turn++)
                    {
                        Require(f.Sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value == turn
                            && f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == turn
                            && f.Avatar(0).QueryAttribute(AttributeType.MaxMana).Value == turn,
                            "Natural magicka growth differs on own turn " + turn + ". " + f.Summary());
                        if (turn < Simulation.MaxManaUnbuffed_)
                        { f.EndTurnTo(1); f.EndTurnTo(0); }
                    }
                    TurnState t = f.Sim._turnStateStack.Peek();
                    ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(f.Sim, t);
                    // A test-only native bonus isolates preservation above the
                    // natural cap; this does not claim any ramp card is restored.
                    f.InjectAction(delegate
                    {
                        f.Avatar(0).AddMaxManaBuff(gs, 3);
                        f.Avatar(0).RecalculateEnchantmentsWithTriggers(gs, null);
                        PlayerActions.ModifyMana.StaticExecute(gs, f.Sim.Players[0], -Simulation.MaxManaUnbuffed_);
                    });
                    Require(f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == 0, "Native mana spending fixture failed.");
                    f.EndTurnTo(1); f.EndTurnTo(0);
                    Require(f.Sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value == Simulation.MaxManaUnbuffed_ + 1
                        && f.Avatar(0).QueryAttribute(AttributeType.MaxMana).Value == Simulation.MaxManaUnbuffed_ + 3
                        && f.Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == Simulation.MaxManaUnbuffed_ + 3,
                        "Natural cap or native bonus refill differs. " + f.Summary());
                    int before = f.Sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value;
                    f.Avatar(0).SetAttribute(AttributeType.DoubleMaxManaGained, 1);
                    bool gated = false;
                    try { DecisionRestoration.RestoreStartTurnMagicka(f.Sim, f.Sim._turnStateStack.Peek(), f.Sim.Players[0]); }
                    catch (NotSupportedException) { gated = true; }
                    Require(gated && f.Sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value == before,
                        "Unvalidated doubling modifier must gate before incrementing the turn counter.");
                }
            });
            test("native-avatar-immortal-and-terminal-loss", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    f.Start();
                    f.Until(delegate { return Ready(f.Sim); }, "Startup did not reach lethal-damage test turn.");
                    f.Sim._disableAiDecisions = true;
                    ServerCard avatar = f.Avatar(1);
                    CardInstanceId instance = avatar.InstanceId;
                    int uid = avatar.NetUid.Id;
                    avatar.SetAttribute(AttributeType.Immortal, 1);
                    ServerEffectUtilities.TriggerGameState state = DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek());
                    f.InjectAction(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(state, null, f.Sim.Players[1], avatar.CurrentHealth + 1, -1); });
                    Require(avatar.CurrentHealth <= 0 && !avatar.IsDying(), "Native Immortal did not protect a lethally damaged avatar.");
                    DecisionRestoration.ProcessDiscards(f.Sim);
                    Require(avatar.CurrentGameZoneName == GameZoneName.AvatarBoard, "Immortal avatar was discarded.");
                    Require(!f.Sim.DidPlayerLose(f.Sim._gameProperties.LossConditions, f.Sim.Players[1], f.Sim.Players[0]),
                        "Native loss condition incorrectly matched immortal avatar on board.");
                    f.InjectAction(delegate
                    {
                        avatar.SetAttribute(AttributeType.Immortal, 0);
                        Require(avatar.IsDying(), "Expired Immortal did not expose lethal health.");
                        DecisionRestoration.ProcessDiscards(f.Sim);
                    });
                    f.Until(delegate { return f.Sim._gameState.GamePhase == GamePhase.Results; }, "Native avatar cemetery loss did not end the match.");
                    Require(avatar.CurrentGameZoneName == GameZoneName.AvatarCemetery
                        && avatar.CurrentGameZoneType == GameZoneType.Cemetery && avatar.InstanceId == instance && avatar.NetUid.Id == uid,
                        "Avatar loss did not preserve original instance in AvatarCemetery.");
                    Require(f.Sim.CardCatalog.FindCardCount(GameZoneName.Cemetery, f.Sim.Players[1].NetId) == 0
                        && f.Sim.CardCatalog.FindCardCount(GameZoneName.Deathrattle, f.Sim.Players[1].NetId) == 0,
                        "Avatar death incorrectly used creature clone/deathrattle zones.");
                    Require(f.Sim.WinnerId == f.Sim.Players[0].NetId && f.Sim.LoserId == f.Sim.Players[1].NetId,
                        "Native EndGame reported incorrect winner/loser.");
                }
            });
        }

        private static bool Ready(Simulation sim)
        {
            if (sim._gameState.GamePhase != GamePhase.MainLoop || sim._turnStateStack.Count != 1) return false;
            TurnState t = sim._turnStateStack.Peek();
            return t.Phase == TurnPhase.PlayCards && t.CurrentSequence == null && t.CurrentCommandItem != null
                && t.CurrentCommandItem.CommandList != null && t.EffectTriggerQueues.Active.Count == 0
                && t.EffectTriggerQueues.Pending.Count == 0 && t.EffectTriggerQueues.After.Count == 0;
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private sealed class Fixture : IDisposable
        {
            public readonly Simulation Sim = new Simulation();
            private readonly GameConfiguration config;
            private readonly Card avatar;
            private readonly List<TriggeredEffect> effects;
            private readonly List<Aura> auras;
            private readonly ActivatedAbility ability;
            private readonly int alitId;
            private readonly Scenario scenario;
            private readonly List<AvatarOverride> avatarOverrides;
            private readonly List<PlayerOverride> playerOverrides;
            public Fixture(GameConfiguration value)
            {
                config = value;
                foreach (CardSet set in config.CardCollection)
                    foreach (Card card in set.Cards)
                    {
                        if (avatar == null && Simulation.IsValidAvatarCard(card.ResourceId.Id)) avatar = card;
                        if (card.Name == "Afflicted Alit" && RuleImporter.IsSupported(card.ResourceId.Id)) alitId = card.ResourceId.Id;
                    }
                Require(avatar != null && alitId != UniqueResourceId.GetInvalid().Id && alitId != 0, "Lifecycle test avatar/reviewed Alit is unavailable.");
                // Only missing per-card avatar wrappers are fixtures. Native shared
                // CardDefinition triggers/auras and default game properties stay intact.
                effects = avatar.TriggeredEffects; auras = avatar.Auras; ability = avatar.ActivatedAbility;
                scenario = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.UniqueName == "Default"; });
                Require(scenario != null, "Native Default scenario is unavailable.");
                avatarOverrides = scenario.AvatarOverrides; playerOverrides = scenario.PlayerOverrides;
                // These two Scenario fields are NonSerialized and absent in this
                // client. Empty wrappers are TEST ASSUMPTIONS for Default only;
                // their original contents remain unverified and are restored below.
                if (scenario.AvatarOverrides == null) scenario.AvatarOverrides = new List<AvatarOverride>();
                if (scenario.PlayerOverrides == null) scenario.PlayerOverrides = new List<PlayerOverride>();
                if (avatar.TriggeredEffects == null) avatar.TriggeredEffects = new List<TriggeredEffect>();
                if (avatar.Auras == null) avatar.Auras = new List<Aura>();
                if (avatar.ActivatedAbility == null) avatar.ActivatedAbility = new ActivatedAbility();
            }
            public void Start() { Start(12); }
            public void Start(int deckSize)
            { Start(deckSize, alitId); }
            // Two real decklists (card ids, repeated per copy) and a seed: soak matches.
            public void Start(List<int> deck0, List<int> deck1, int seed)
            {
                MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo {
                    ScenarioId = scenario.ResourceId.Id, RandomNumberGenerator = new RandomIntegerGenerator(seed) };
                for (int slot = 0; slot < 2; slot++)
                {
                    PlayerDescription description = new PlayerDescription { PlayerName = "Soak " + slot,
                        AvatarCardId = avatar.ResourceId.Id, AiOpponentId = UniqueResourceId.GetInvalid().Id };
                    foreach (int id in slot == 0 ? deck0 : deck1) { description.Deck.Add(id); description.Premium.Add(false); }
                    info.Users[slot] = new MatchConfiguration.User { CustomDeck = description, Type = PlayerType.AiOpponent,
                        EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "soak-" + slot,
                        SpectatorKey = "soak-observer-" + slot };
                }
                LifecycleRestoration.Start(Sim, null, info);
            }
            public void Start(int deckSize, int deckCardId)
            {
                MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo {
                    ScenarioId = scenario.ResourceId.Id, RandomNumberGenerator = new RandomIntegerGenerator(12345) };
                for (int slot = 0; slot < 2; slot++)
                {
                    PlayerDescription description = new PlayerDescription { PlayerName = "Local lifecycle test " + slot,
                        AvatarCardId = avatar.ResourceId.Id, AiOpponentId = UniqueResourceId.GetInvalid().Id };
                    for (int n = 0; n < deckSize; n++) { description.Deck.Add(deckCardId); description.Premium.Add(false); }
                    info.Users[slot] = new MatchConfiguration.User { CustomDeck = description, Type = PlayerType.AiOpponent,
                        EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "local-lifecycle-test-" + slot,
                        SpectatorKey = "local-lifecycle-test-observer-" + slot };
                }
                LifecycleRestoration.Start(Sim, null, info);
            }
            public ServerCard Avatar(int slot)
            {
                return Sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard card) { return card.HasSubtype(CardSubtype.Avatar); },
                    Sim.Players[slot].NetId, GameZoneName.AvatarBoard);
            }
            public void InjectAction(Action action)
            {
                // Direct fixture actions do not pass through Begin/ProcessSequence.
                // Supply their test-only event so native history instant actions
                // satisfy the same boundary contract as normal game actions.
                Sim.HistoryLog.StartEvent(HistoryType.EventTrigger, Sim.CurrentPlayer.NetId,
                    CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
                try { action(); }
                finally { Sim.HistoryLog.EndEvent(); }
            }
            public ServerCard PrepareInsertionBoard(int count)
            {
                Start();
                Until(delegate { return Ready(Sim); }, "Startup did not reach insertion test turn.");
                Sim._disableAiDecisions = true;
                TurnState t = Sim._turnStateStack.Peek();
                ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(Sim, t);
                InjectAction(delegate
                {
                    // Test-only creatures populate exact lane positions without
                    // repeatedly summoning Alit and forcing rune interruptions.
                    for (int n = 0; n < count; n++)
                        Require(ServerCardCatalog.CreateCard(gs, alitId, false, GameZoneName.CreatureBoard, 0,
                            CardInsertionType.Bottom, Sim.Players[0].NetId, null, null) != null, "Fixture board creation failed.");
                    PlayerActions.SetMana.StaticExecute(gs, Sim.Players[0], 12);
                });
                t.CurrentCommandItem = Sim.CardCatalog.GenerateCardCommandItem(Sim.Players[0], Sim.Players[1], Sim.Players);
                ServerCard actor = Sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == alitId; },
                    Sim.Players[0].NetId, GameZoneName.Hand);
                Require(actor != null, "Insertion fixture has no reviewed hand card.");
                return actor;
            }
            public Decision InsertionDecision(ServerCard actor, int slot)
            {
                return new Decision { Type = CcgRpc.DecisionType.Play, ActorCardNetUid = actor.NetUid.Id,
                    CommandUid = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid, LaneIndex = 0, PositionIndex = slot };
            }
            public void PlayAndSettle(Decision play)
            {
                Require(CommandRestoration.ValidateDecision(play, Sim._turnStateStack.Peek().CurrentCommandItem.CommandList,
                    Sim.CardCatalog, Sim.Players[0]), "Valid native selected-slot command was rejected.");
                Sim.PlayerServiceClients[0]._decisionMessages.Enqueue(play);
                Until(delegate { return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != play.CommandUid; },
                    "Native selected-slot play did not settle.");
            }
            public void RequireOrder(List<CardInstanceId> expected)
            {
                ICardLane lane = Sim.CardCatalog.FindGameZone(Sim.Players[0].NetId, GameZoneName.CreatureBoard).LaneList[0];
                Require(lane.FindCardCount() == expected.Count, "Selected-slot play changed lane cardinality.");
                for (int n = 0; n < expected.Count; n++) Require(lane.GetCard(n) == expected[n], "Wrong creature order at native index " + n + ".");
            }
            public void RejectInsertion(Decision play, ServerCard actor)
            {
                TurnState t = Sim._turnStateStack.Peek();
                int uid = t.CurrentCommandItem.CommandList.ListUid;
                int mana = Sim.Players[0].QueryAttribute(AttributeType.Mana).Value;
                int played = Sim.Players[0].NumCardsPlayedThisTurn;
                int hand = Sim.CardCatalog.FindCardCount(GameZoneName.Hand, Sim.Players[0].NetId);
                List<CardInstanceId> order = new List<CardInstanceId>(Sim.CardCatalog.FindGameZone(Sim.Players[0].NetId, GameZoneName.CreatureBoard).LaneList[0]);
                Require(!CommandRestoration.ValidateDecision(play, t.CurrentCommandItem.CommandList, Sim.CardCatalog, Sim.Players[0]),
                    "Invalid native slot/sacrifice was accepted.");
                Sim.PlayerServiceClients[0]._decisionMessages.Enqueue(play);
                for (int tick = 0; tick < 4; tick++) LifecycleRestoration.FixedUpdate(Sim);
                Require(Ready(Sim) && t.CurrentCommandItem.CommandList.ListUid == uid
                    && actor.CurrentGameZoneName == GameZoneName.Hand && Sim.Players[0].QueryAttribute(AttributeType.Mana).Value == mana
                    && Sim.Players[0].NumCardsPlayedThisTurn == played && Sim.CardCatalog.FindCardCount(GameZoneName.Hand, Sim.Players[0].NetId) == hand,
                    "Rejected insertion mutated sequence, mana, play count or hand.");
                RequireOrder(order);
            }
            public void Until(Func<bool> done, string message)
            {
                for (int tick = 0; tick < 200; tick++)
                {
                    if (done()) return;
                    LifecycleRestoration.FixedUpdate(Sim);
                }
                throw new InvalidOperationException(message + " " + Summary());
            }
            public void EndTurnTo(int next)
            {
                Require(Ready(Sim), "End-turn injection requires a settled command list. " + Summary());
                int current = Sim._turnStateStack.Peek().ActivePlayerIndex;
                Sim.PlayerServiceClients[current]._decisionMessages.Enqueue(new Decision {
                    Type = CcgRpc.DecisionType.NoCommand, CommandUid = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid,
                    ActorCardNetUid = CardNetUid.GetInvalid().Id, TargetCardNetUid = CardNetUid.GetInvalid().Id,
                    EquipCardNetUid = CardNetUid.GetInvalid().Id });
                Until(delegate { return Ready(Sim) && Sim._turnStateStack.Peek().TurnPlayerIndex == next; },
                    "Native end-turn/start-turn did not transfer control to player " + next + ".");
            }
            public string Summary()
            {
                string detail = "phase=" + Sim._gameState.GamePhase + ";";
                if (Sim._turnStateStack.Count > 0) detail += "active=" + Sim._turnStateStack.Peek().ActivePlayerIndex + ";";
                foreach (PlayerState p in Sim.Players)
                    detail += "p" + p.NetId.Id + "(hand=" + Sim.CardCatalog.FindCardCount(GameZoneName.Hand, p.NetId)
                        + ",deck=" + Sim.CardCatalog.FindCardCountInDeck(p.NetId) + ",mana=" + p.QueryAttribute(AttributeType.Mana).Value
                        + ",turns=" + p.QueryAttribute(AttributeType.TurnCount).Value + ",maxMana="
                        + (Avatar(p.NetId.Id - 1) == null ? -1 : Avatar(p.NetId.Id - 1).QueryAttribute(AttributeType.MaxMana).Value) + ");";
                detail += "startActions=" + DescribeActions(Sim._gameProperties.GameStartActions)
                    + ";turnActions=" + DescribeActions(Sim._gameProperties.TurnStartActions);
                return detail;
            }
            private static string DescribeActions(List<PlayerEffectAction> actions)
            {
                string description = "";
                foreach (PlayerEffectAction effect in actions)
                {
                    if (effect.Action == null) { description += "null,"; continue; }
                    description += effect.Action.GetType().Name + "[selectors=" + effect.Selectors.Count;
                    PlayerActions.DrawCards draw = effect.Action as PlayerActions.DrawCards;
                    if (draw != null) description += ",method=" + draw.NumCards.ValueMethod + ",constant=" + draw.NumCards.ConstantValue;
                    description += "],";
                }
                return description;
            }
            public void Dispose()
            {
                RuneProphecyRestoration.Clear(Sim);
                if (Sim.PlayerServiceClients != null) foreach (PlayerServiceClient client in Sim.PlayerServiceClients) if (client != null) client.Stop();
                avatar.TriggeredEffects = effects; avatar.Auras = auras; avatar.ActivatedAbility = ability;
                scenario.AvatarOverrides = avatarOverrides; scenario.PlayerOverrides = playerOverrides;
            }
        }
    }
}
