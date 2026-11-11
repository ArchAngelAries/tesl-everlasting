using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalTargetedSummonTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069; // Swamp Leviathan 8/8, Enraged Mudcrab 2/1.
        private const int Sphere = -12036977, Scout = -1871307781, Witchman = -781594103, Witch = -967450522, Camlorn = -554006585, Allena = 1735315658;

        // The 21 targeted Summons now run their original graphs (prefer-original.tsv, owner report 2026-11-06): the card is
        // played with no target, lands, and its Summon asks for the target (Reborn order; the client can show it). The old
        // reconstruction targeted on drop: the client lifted the card out of the hand, PLAY_TO_BOARD found no card and the
        // creature never appeared on screen.
        private static readonly int[] Targeted = { 1735315658, 1884147519, -1871307781, -1767873507, 1863406899, -1249810974, 1750084664,
            -781594103, 1106955994, -967450522, -463613369, -1621791439, 1826150536, -452943738, -1935845219, 2052829044,
            -12036977, -710854512, -1766587430, 575791787, -456767346 };
        private static readonly int[] OpponentDamage = { -554006585, 380978308, -602835722, 667662641 };
        private const int Morkul = 1106955994;

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            foreach (int each in Targeted)
            {
                int id = each;
                test("original-targeted-summon-lands-then-prompts-" + id, delegate {
                    using (LifecycleFixture f = new LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex; f.AutoAnswerTargets = false;
                        ServerCard card = f.Create(id, me, GameZoneName.Hand);
                        ServerCard ally = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 0);
                        ServerCard enemy = f.CreateIn(Leviathan, 1 - me, GameZoneName.CreatureBoard, 0);
                        f.Mana(me, 20); f.Refresh(me);
                        CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                        CardCommandState command = Command(list, card);
                        Require(command != null && command.PlayTargets.Count == 0, id + ": the hand command still targets on drop.");
                        bool item = (command.ValidCardCommandMask & (int)CardCommand.PlayToEquip) != 0;
                        Decision play = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = card.NetUid.Id,
                            LaneIndex = item ? 0 : 1, PositionIndex = 0, EquipCardNetUid = item ? ally.NetUid.Id : 0 };
                        Require(CommandRestoration.ValidateDecision(play, list, s.CardCatalog, s.Players[me]), id + ": the client's play (no target) is invalid.");
                        s.PlayerServiceClients[me]._decisionMessages.Enqueue(play);
                        f.Until(delegate { return Prompt(s) != null || OriginalTargetedSummonTests.Ready(s) && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
                        CardCommandState prompt = Prompt(s);
                        Require(prompt != null, id + ": no target prompt after the card landed (zone " + card.CurrentGameZoneName + ").");
                        Require(card.CurrentGameZoneName != GameZoneName.Hand, id + ": the card was not played before its Summon asked.");
                        Require((prompt.ValidCardCommandMask & (int)CardCommand.DeclineToPlay) != 0 && !string.IsNullOrEmpty(prompt.TargetingPromptLocKey),
                            id + ": the optional prompt lacks Cancel or its instruction (mask " + prompt.ValidCardCommandMask + ", '" + prompt.TargetingPromptLocKey + "').");
                        ServerCard target = s.CardCatalog.FindServerCard(new CardNetUid(prompt.PlayTargets[0]));
                        string before = Snapshot(target);
                        s.PlayerServiceClients[s._turnStateStack.Peek().ActivePlayerIndex]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play,
                            CommandUid = s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid, ActorCardNetUid = prompt.CardNetUid, TargetCardNetUid = target.NetUid.Id });
                        f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                        Require(Snapshot(target) != before, id + ": the chosen target did not change (" + before + ").");
                        Require(s._turnStateStack.Peek().ActivePlayerIndex == me, id + ": the turn did not continue.");
                        foreach (HistoryItem h in s.HistoryLog.GenerateFullReplayForPlayer(s.PlayerServiceClients[me], HistoryLog.FullGameStateSendRule.Exclude).Queue)
                            if (h.Type == HistoryType.ActionPlayToBoard && h.AffectedCardsArray.Count > 0 && h.AffectedCardsArray[0].CardNetUid == card.NetUid.Id)
                                Require(h.TargetCardNetUid == 0 || h.TargetCardNetUid == CardNetUid.GetInvalid().Id, id + ": PLAY_TO_BOARD still carries a target.");
                    }
                });
            }
            test("original-targeted-summon-optional-target-cancel", delegate {
                using (LifecycleFixture f = new LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex; f.AutoAnswerTargets = false;
                    ServerCard morkul = f.Create(Morkul, me, GameZoneName.Hand), ally = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 0);
                    f.Mana(me, 20); f.Refresh(me);
                    CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = morkul.NetUid.Id, LaneIndex = 1 });
                    f.Until(delegate { return Prompt(s) != null; });
                    s.PlayerServiceClients[s._turnStateStack.Peek().ActivePlayerIndex]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.NoCommand,
                        CommandUid = s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    Require(morkul.CurrentGameZoneName == GameZoneName.CreatureBoard && ally.QueryAttribute(AttributeType.Attack).Value == 8
                        && s._turnStateStack.Peek().ActivePlayerIndex == me, "Cancel must land Morkul, skip the buff and keep the turn (zone "
                        + morkul.CurrentGameZoneName + ", ally attack " + ally.QueryAttribute(AttributeType.Attack).Value + ").");
                }
            });
            test("strategic-original-targeted-summon-prompt-targets", delegate {
                // the AI answers the Summon prompt: buff its own creature, debuff the killable enemy, shackle the strongest enemy
                int[][] cases = { new int[] { -781594103, 0 }, new int[] { -967450522, 1 }, new int[] { -12036977, 2 } };
                foreach (int[] c in cases)
                    using (LifecycleFixture f = new LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex; f.AutoAnswerTargets = false;
                        ServerCard card = f.Create(c[0], me, GameZoneName.Hand);
                        ServerCard ally = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 0);
                        ServerCard small = f.CreateIn(Mudcrab, 1 - me, GameZoneName.CreatureBoard, 0), big = f.CreateIn(Leviathan, 1 - me, GameZoneName.CreatureBoard, 1);
                        f.Mana(me, 20); f.Refresh(me);
                        CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                        s.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = card.NetUid.Id, LaneIndex = 1 });
                        f.Until(delegate { return Prompt(s) != null; });
                        Decision choice = StrategicDecisionRestoration.ChooseDecision(s, s._turnStateStack.Peek().CurrentCommandItem.CommandList);
                        ServerCard want = c[1] == 0 ? ally : c[1] == 1 ? small : big;
                        ServerCard chosen = choice == null ? null : s.CardCatalog.FindServerCard(new CardNetUid(choice.TargetCardNetUid));
                        bool ok = c[1] == 0 ? chosen != null && chosen.OwnerNetId == s.Players[me].NetId && chosen.HasSubtype(CardSubtype.Creature)   // a friendly creature (itself counts)
                            : chosen == want;
                        Require(ok, c[0] + ": the AI chose " + (chosen == null ? "nothing" : chosen.GetCardName() + " of " + (chosen.OwnerNetId == s.Players[me].NetId ? "its own" : "the enemy"))
                            + " instead of " + want.GetCardName());
                    }
            });
            foreach (int each in OpponentDamage)
            {
                int id = each;
                test("original-summon-damage-opponent-" + id, delegate {
                    using (LifecycleFixture f = new LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard card = f.Create(id, me, GameZoneName.Hand);
                        f.Mana(me, 20); f.Refresh(me);
                        int health = f.Avatar(1 - me).CurrentHealth;
                        f.Play(card, null, me);
                        Require(card.CurrentGameZoneName == GameZoneName.CreatureBoard && f.Avatar(1 - me).CurrentHealth < health, id + ": Summon did not damage the opponent.");
                    }
                });
            }
        }

        private static CardCommandState Prompt(Simulation s)
        {
            if (s._turnStateStack.Count < 2) return null;
            TurnState top = s._turnStateStack.Peek();
            if (top.CurrentCommandItem == null || top.CurrentSequence != null) return null;
            foreach (CardCommandState c in top.CurrentCommandItem.CommandList.ValidCommands)
                if ((c.ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0) return c;
            return null;
        }

        private static string Snapshot(ServerCard c)
        {
            return c.CurrentGameZoneName + " " + c.QueryAttribute(AttributeType.Attack).Value + "/" + c.CurrentHealth + "/" + c.QueryAttribute(AttributeType.MaxHealth).Value
                + " freeze " + c.QueryAttribute(AttributeType.Freeze).Value + " ench " + c.Enchantments.Count;
        }

        internal static ServerCard Hand(OriginalDamageStrategyTests.Fixture f, int id)
        {
            ServerCard card = ServerCardCatalog.CreateCard(f.State, id, false, GameZoneName.Hand, 0, CardInsertionType.Bottom, f.One.NetId, null, null);
            Require(card != null, "Targeted Summon hand fixture creation failed: " + id); return card;
        }
        internal static CardCommandState Command(CardCommandList list, ServerCard card)
        { foreach (CardCommandState state in list.ValidCommands) if (state.CardNetUid == card.NetUid.Id) return state; return null; }
        internal static bool Prophecy(Card card)
        {
            foreach (SPTESL.Attribute attribute in card.EntityAttributes)
            { BoolAttribute flag = attribute as BoolAttribute; if (attribute.Name == AttributeType.Prophecy && flag != null && flag.Value) return true; }
            return false;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        internal static bool Ready(Simulation s)
        { return s._gameState.GamePhase == GamePhase.MainLoop && s._turnStateStack.Count == 1 && s._turnStateStack.Peek().Phase == TurnPhase.PlayCards
            && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem != null && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek())
            && s._turnStateStack.Peek().EffectTriggerQueues.Pending.Count == 0 && s._turnStateStack.Peek().EffectTriggerQueues.Active.Count == 0
            && s._turnStateStack.Peek().EffectTriggerQueues.After.Count == 0; } // queued (non-Instant) triggers must have resolved

        // Bounded server fixture; twelve duplicate originals per deck isolate targeted Summon and
        // rune orchestration. This is not a legal user deck, scenario setting or physical UI test.
        internal sealed class LifecycleFixture : IDisposable
        {
            public readonly Simulation Sim = new Simulation();
            private readonly IDisposable scope;
            public LifecycleFixture(GameConfiguration config, int deckId)
            {
                scope = OrdinaryMatchRules.Prepare(config);
                try
                {
                    MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo { ScenarioId = OrdinaryMatchRules.DefaultScenarioId, RandomNumberGenerator = new RandomIntegerGenerator(12345) };
                    for (int slot = 0; slot < 2; slot++)
                    {
                        PlayerDescription d = new PlayerDescription { PlayerName = "Original targeted summon test " + slot, AvatarCardId = OrdinaryMatchRules.BaseAvatarId, AiOpponentId = 0 };
                        for (int n = 0; n < 12; n++) { d.Deck.Add(deckId); d.Premium.Add(false); }
                        info.Users[slot] = new MatchConfiguration.User { CustomDeck = d, Type = PlayerType.AiOpponent, EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "targeted-summon-test-" + slot, SpectatorKey = "targeted-summon-observer-" + slot };
                    }
                    LifecycleRestoration.Start(Sim, null, info); Sim._disableAiDecisions = true;
                    Until(delegate { return Sim._gameState.GamePhase == GamePhase.Mulligan; });
                    foreach (PlayerState player in Sim.Players) Sim.ReportMulliganDecision(player, new MulliganResponse { Action = MulliganAction.Commit, MulliganCardNetworkUids = new List<int>() });
                    Until(delegate { return Ready(Sim); });
                }
                catch { Dispose(); throw; }
            }
            public ServerCard Create(int id, int owner, GameZoneName zone)
            {
                ServerCard result = null;
                Event(delegate { result = ServerCardCatalog.CreateCard(DecisionRestoration.State(Sim, Sim._turnStateStack.Peek()), id, false, zone, 0,
                    CardInsertionType.Bottom, Sim.Players[owner].NetId, null, null); });
                Require(result != null, "Targeted Summon lifecycle card creation failed: " + id); return result;
            }
            public ServerCard CreateIn(int id, int owner, GameZoneName zone, int lane)
            {
                ServerCard result = null;
                Event(delegate { result = ServerCardCatalog.CreateCard(DecisionRestoration.State(Sim, Sim._turnStateStack.Peek()), id, false, zone, lane,
                    CardInsertionType.Bottom, Sim.Players[owner].NetId, null, null); });
                Require(result != null, "Lifecycle lane card creation failed: " + id); return result;
            }
            public int HandCount(int owner) { return Sim.CardCatalog.FindCardCount(GameZoneName.Hand, Sim.Players[owner].NetId); }
            public int DeckCount(int owner) { return Sim.CardCatalog.FindCardCountInDeck(Sim.Players[owner].NetId); }
            public ServerCard Avatar(int owner)
            { return Sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, Sim.Players[owner].NetId, GameZoneName.AvatarBoard); }
            public bool CemeteryHas(int owner, int id)
            { return Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == id; }, Sim.Players[owner].NetId, GameZoneName.Cemetery).Count > 0; }
            public int BoardCount(int owner)
            { return Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, Sim.Players[owner].NetId, GameZoneName.CreatureBoard).Count; }
            public void Event(Action action)
            { Sim.HistoryLog.StartEvent(HistoryType.EventTrigger, Sim.CurrentPlayer.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1); try { action(); } finally { Sim.HistoryLog.EndEvent(); } }
            public void Play(ServerCard card, ServerCard target, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = card.NetUid.Id, TargetCardNetUid = target == null ? CardNetUid.GetInvalid().Id : target.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Original targeted Summon command is invalid: " + card.CardId);
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { AnswerTargetPrompt(); return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public void PlayEquip(ServerCard item, ServerCard wielder, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = item.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id, EquipCardNetUid = wielder.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Native equip command is invalid: " + item.CardId);
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { AnswerTargetPrompt(); return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public void PlayLane(ServerCard card, BoardLaneTarget lane, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = card.NetUid.Id, LaneTarget = lane };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Native lane play command is invalid: " + card.CardId + " " + lane);
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { AnswerTargetPrompt(); return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            // Creature insertion uses LaneIndex; LaneTarget above selects a lane for an effect.
            public void PlayCreatureLane(ServerCard card, int lane, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid,
                    ActorCardNetUid = card.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id, LaneIndex = lane };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Native creature placement is invalid: " + card.CardId + " " + lane);
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { AnswerTargetPrompt(); return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public void Activate(ServerCard card, ServerCard target, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Activate, CommandUid = list.ListUid, ActorCardNetUid = card.NetUid.Id, TargetCardNetUid = target == null ? CardNetUid.GetInvalid().Id : target.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Native activate command is invalid: " + card.CardId);
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { AnswerTargetPrompt(); return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public int LaneCount(int owner, int lane)
            { return Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CurrentLaneIndex == lane; }, Sim.Players[owner].NetId, GameZoneName.CreatureBoard).Count; }
            public void Betray(ServerCard action, ServerCard sacrifice, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Betray, CommandUid = list.ListUid, ActorCardNetUid = action.NetUid.Id };
                if (sacrifice != null) decision.DiscardCardNetUids.Add(sacrifice.NetUid.Id);
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Native Betray command is invalid: " + action.CardId);
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { AnswerTargetPrompt(); return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public void Attack(ServerCard attacker, ServerCard target, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Attack, CommandUid = list.ListUid, ActorCardNetUid = attacker.NetUid.Id, TargetCardNetUid = target.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Native attack command is invalid: " + attacker.CardId);
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { AnswerTargetPrompt(); return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public void EndTurn()
            {
                Require(Ready(Sim), "End turn requires a settled command list.");
                int current = Sim._turnStateStack.Peek().ActivePlayerIndex;
                Sim.PlayerServiceClients[current]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.NoCommand,
                    CommandUid = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid, ActorCardNetUid = CardNetUid.GetInvalid().Id,
                    TargetCardNetUid = CardNetUid.GetInvalid().Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                Until(delegate { return Ready(Sim) && Sim._turnStateStack.Peek().ActivePlayerIndex != current; });
            }
            // Settles triggers queued by a test-injected event (outside a decision) with the native queue executor.
            public void DrainEffects()
            {
                for (int guard = 0; ; guard++)
                {
                    TurnState turn = Sim._turnStateStack.Peek(); ServerEffectUtilities.TriggerQueues q = turn.EffectTriggerQueues;
                    if (q.Pending.Count == 0 && q.Active.Count == 0) return;
                    Require(guard < 64, "Injected trigger queue did not settle.");
                    q.Pending.Sort(ServerEffectUtilities.TriggerContext.SortComparison);
                    foreach (ServerEffectUtilities.TriggerContext pending in q.Pending) q.Active.Enqueue(pending);
                    q.Pending.Clear();
                    ServerEffectUtilities.TriggerContext context = q.Active.Dequeue();
                    Event(delegate { ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(DecisionRestoration.State(Sim, turn), context, null); });
                }
            }
            // A triggered ChooseTarget prompt (child decision state) is answered with its first offered target, or no target.
            // Only target prompts are answered here; Prophecy/Betray/interrupt prompts are left to the test.
            public bool AutoAnswerTargets = true;
            public void AnswerTargetPrompt()
            {
                if (!AutoAnswerTargets || Sim._turnStateStack.Count < 2) return;
                TurnState top = Sim._turnStateStack.Peek();
                if (top.CurrentCommandItem == null || top.CurrentSequence != null) return;
                CardCommandList prompt = top.CurrentCommandItem.CommandList;
                Decision custom = OfflineConsumeChoices.Choose(prompt);
                if (custom != null)
                {
                    PlayerServiceClient chooser = Sim.PlayerServiceClients[top.ActivePlayerIndex];
                    if (chooser._decisionMessages.Count == 0) chooser._decisionMessages.Enqueue(custom);
                    return;
                }
                if (prompt.ValidCommands.Count != 1 || (prompt.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) == 0) return;
                PlayerServiceClient client = Sim.PlayerServiceClients[top.ActivePlayerIndex];
                if (client._decisionMessages.Count > 0) return;
                CardCommandState c = prompt.ValidCommands[0];
                Decision d = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid, ActorCardNetUid = c.CardNetUid,
                    TargetCardNetUid = c.PlayTargets.Count > 0 ? c.PlayTargets[0] : CardNetUid.GetInvalid().Id, EquipCardNetUid = CardNetUid.GetInvalid().Id };
                if (c.PlayTargets.Count == 0 && c.LaneTargets.Count > 0) d.LaneTarget = c.LaneTargets[0];
                client._decisionMessages.Enqueue(d);
            }
            public void Mana(int player, int amount)
            { Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(Sim, Sim._turnStateStack.Peek()), Sim.Players[player], amount); }); }
            public void Refresh(int player)
            { Sim._turnStateStack.Peek().CurrentCommandItem = Sim.CardCatalog.GenerateCardCommandItem(Sim.Players[player], Sim.Players[1 - player], Sim.Players); }
            public void Until(Func<bool> done)
            { for (int tick = 0; tick < 500; tick++) { if (done()) return; LifecycleRestoration.FixedUpdate(Sim); } throw new InvalidOperationException("Targeted Summon native sequence did not settle: " + Sim._gameState.GamePhase); }
            public void Dispose()
            { RuneProphecyRestoration.Clear(Sim); if (Sim.PlayerServiceClients != null) foreach (PlayerServiceClient client in Sim.PlayerServiceClients) if (client != null) client.Stop(); scope.Dispose(); }
        }
    }
}
