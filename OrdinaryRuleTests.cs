using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    public static class OrdinaryRuleTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("ordinary-default-native-start-runes-ring-and-reversible-scope", delegate
            {
                Scenario scenario = config.Scenarios.Find(delegate(Scenario x) { return x.ResourceId.Id == OrdinaryMatchRules.DefaultScenarioId; });
                Card avatar = config.FindCardById(OrdinaryMatchRules.BaseAvatarId);
                LaneTrigger[] lanes = config.LaneTriggers;
                List<AvatarOverride> avatars = scenario.AvatarOverrides;
                List<PlayerOverride> players = scenario.PlayerOverrides;
                List<TriggeredEffect> effects = avatar.TriggeredEffects;
                List<Aura> auras = avatar.Auras;
                ActivatedAbility ability = avatar.ActivatedAbility;
                using (Fixture f = new Fixture(config))
                {
                    for (int i = 0; i < 2; i++)
                    {
                        PlayerState p = f.Sim.Players[i];
                        Require(f.Avatar(i).CurrentHealth == 30, "Ordinary native avatar does not start at 30 health.");
                        Require(p.QueryAttribute(AttributeType.NumRunes).Value == 5 && p.QueryAttribute(AttributeType.RuneHealth).Value == 5,
                            "Native ordinary rune template differs from five runes/5 health.");
                        Require(p.QueryAttribute(AttributeType.RingUsesLeft).Value == (i == 0 ? 0 : 3), "Second-player ring initialization differs.");
                    }
                    MatchConfiguration.StartInfo unsupported = f.StartInfo;
                    int saved = unsupported.Users[0].CustomDeck.AvatarCardId;
                    unsupported.Users[0].CustomDeck.AvatarCardId = -1495991085; // Native special one-health avatar.
                    bool rejected = false;
                    try { OrdinaryMatchRules.ValidateStart(unsupported); } catch (NotSupportedException) { rejected = true; }
                    finally { unsupported.Users[0].CustomDeck.AvatarCardId = saved; }
                    Require(rejected, "Ordinary gate accepted a special avatar.");
                }
                Require(Object.ReferenceEquals(lanes, config.LaneTriggers) && Object.ReferenceEquals(avatars, scenario.AvatarOverrides)
                    && Object.ReferenceEquals(players, scenario.PlayerOverrides) && Object.ReferenceEquals(effects, avatar.TriggeredEffects)
                    && Object.ReferenceEquals(auras, avatar.Auras) && Object.ReferenceEquals(ability, avatar.ActivatedAbility),
                    "Ordinary scope did not restore all original configuration references.");
            });
            test("ordinary-native-ring-once-per-turn-three-charges", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    f.EndTurnTo(1);
                    PlayerState p = f.Sim.Players[1];
                    for (int charge = 3; charge > 0; charge--)
                    {
                        TurnState turn = f.Sim._turnStateStack.Peek();
                        ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(f.Sim, turn);
                        f.Sim.HistoryLog.StartEvent(HistoryType.EventUseRing, p.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
                        try
                        {
                            OrdinaryMatchRules.ResetRingForTurn(f.Sim, turn);
                            int mana = p.QueryAttribute(AttributeType.Mana).Value;
                            Require(CardCommandUtilities.ManaRingRequirementsMet(p), "Charged ordinary ring command is unavailable.");
                            Require(PlayerActions.UseRing.StaticExecute(f.Sim.HistoryLog, gs, p), "Native ring rejected valid use.");
                            Require(p.QueryAttribute(AttributeType.Mana).Value == mana + 1 && p.QueryAttribute(AttributeType.RingUsesLeft).Value == charge - 1,
                                "Native ring mana/charge mutation differs.");
                            Require(!CardCommandUtilities.ManaRingRequirementsMet(p)
                                && !PlayerActions.UseRing.StaticExecute(f.Sim.HistoryLog, gs, p), "Native ring allowed twice in one turn.");
                        }
                        finally { f.Sim.HistoryLog.EndEvent(); }
                        f.EndTurnTo(0); f.EndTurnTo(1);
                    }
                    TurnState exhausted = f.Sim._turnStateStack.Peek();
                    f.Sim.HistoryLog.StartEvent(HistoryType.EventTurnStart, p.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
                    try { OrdinaryMatchRules.ResetRingForTurn(f.Sim, exhausted); }
                    finally { f.Sim.HistoryLog.EndEvent(); }
                    Require(p.QueryAttribute(AttributeType.RingUsesLeft).Value == 0 && !CardCommandUtilities.ManaRingRequirementsMet(p),
                        "Exhausted ring recharged on next turn.");
                }
            });
            test("ordinary-shadow-native-play-cover-and-owner-turn-expiry", delegate
            {
                using (Fixture f = new Fixture(config))
                {
                    f.EndTurnTo(1); f.EndTurnTo(0);
                    Decision play = DecisionRestoration.ChooseDecision(f.Sim, f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList);
                    Require(play.Type == CcgRpc.DecisionType.Play, "Ordinary second-turn native creature play unavailable.");
                    play.LaneIndex = 1; play.PositionIndex = 0;
                    ServerCard card = f.Sim.CardCatalog.FindDynamicCard(new CardNetUid(play.ActorCardNetUid));
                    f.Sim.PlayerServiceClients[0]._decisionMessages.Enqueue(play);
                    f.Until(delegate { return Ready(f.Sim) && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != play.CommandUid; });
                    Require(card.CurrentGameZoneName == GameZoneName.CreatureBoard && card.CurrentLaneType == CardLaneType.Stealth
                        && card.ProtectedByCover(), "Native shadow lane play did not grant Cover.");
                    f.EndTurnTo(1);
                    Require(card.ProtectedByCover(), "Shadow Cover expired at opponent turn start.");
                    f.EndTurnTo(0);
                    Require(!card.ProtectedByCover(), "Shadow Cover survived owner's next turn start.");
                    // Native lane movement (field then shadow) must reapply the
                    // same temporary Cover through WhenCardChangesLane.
                    Move(f, card, 0);
                    Require(!card.ProtectedByCover(), "Ordinary field lane added Cover.");
                    Move(f, card, 1);
                    Require(card.ProtectedByCover(), "Native movement into shadow did not grant Cover.");
                    card.SetAttribute(AttributeType.Taunt, 1);
                    Require(!card.ProtectedByCover(), "Guard bypassed native Cover exception.");
                }
            });
        }

        private static void Move(Fixture f, ServerCard card, int lane)
        {
            TurnState t = f.Sim._turnStateStack.Peek();
            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(f.Sim, t);
            f.Sim.HistoryLog.StartEvent(HistoryType.EventPlayCard, card.OwnerNetId, card.NetUid, CardNetUid.GetInvalid(), -1);
            try
            {
                Require(ServerCardCatalog.MoveCard(gs, new GameZoneId(card.OwnerNetId, GameZoneType.Board),
                    new GameZoneId(card.OwnerNetId, GameZoneType.Board), card.InstanceId,
                    new GameZone.Position { LaneIndex = lane, CardIndex = 0 }, ServerCardCatalog.MoveOperationType.Untyped)
                    == GameZoneOperationResult.Succeeded, "Native ordinary lane move failed.");
            }
            finally { f.Sim.HistoryLog.EndEvent(); }
            // Trigger orchestration will settle queued cover in the restored loop.
            for (int i = 0; i < 30 && (t.EffectTriggerQueues.Pending.Count != 0 || t.EffectTriggerQueues.Active.Count != 0); i++)
                LifecycleRestoration.FixedUpdate(f.Sim);
            Require(t.EffectTriggerQueues.Pending.Count == 0 && t.EffectTriggerQueues.Active.Count == 0, "Native cover movement trigger did not settle.");
        }

        private static bool Ready(Simulation sim)
        {
            if (sim._gameState.GamePhase != GamePhase.MainLoop || sim._turnStateStack.Count != 1) return false;
            TurnState t = sim._turnStateStack.Peek();
            return t.CurrentSequence == null && t.CurrentCommandItem != null && t.CurrentCommandItem.CommandList != null
                && t.EffectTriggerQueues.Active.Count == 0 && t.EffectTriggerQueues.Pending.Count == 0 && t.EffectTriggerQueues.After.Count == 0;
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

        private sealed class Fixture : IDisposable
        {
            public readonly Simulation Sim = new Simulation();
            public readonly MatchConfiguration.StartInfo StartInfo;
            private readonly IDisposable scope;
            public Fixture(GameConfiguration config)
            {
                scope = OrdinaryMatchRules.Prepare(config);
                try
                {
                    Card alit = null;
                    foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                        if (c.Name == "Afflicted Alit" && RuleImporter.IsSupported(c.ResourceId.Id)) alit = c;
                    Require(alit != null, "Reviewed native Alit is unavailable for ordinary tests.");
                    StartInfo = new MatchConfiguration.StartInfo { ScenarioId = OrdinaryMatchRules.DefaultScenarioId,
                        RandomNumberGenerator = new RandomIntegerGenerator(12345) };
                    for (int i = 0; i < 2; i++)
                    {
                        PlayerDescription d = new PlayerDescription { PlayerName = "Ordinary rules test " + i, AvatarCardId = OrdinaryMatchRules.BaseAvatarId,
                            AiOpponentId = UniqueResourceId.GetInvalid().Id };
                        for (int n = 0; n < 12; n++) { d.Deck.Add(alit.ResourceId.Id); d.Premium.Add(false); }
                        StartInfo.Users[i] = new MatchConfiguration.User { CustomDeck = d, Type = PlayerType.AiOpponent,
                            EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "ordinary-test-" + i,
                            SpectatorKey = "ordinary-observer-test-" + i };
                    }
                    OrdinaryMatchRules.ValidateStart(StartInfo);
                    LifecycleRestoration.Start(Sim, null, StartInfo);
                    Sim._disableAiDecisions = true;
                    Until(delegate { return Sim._gameState.GamePhase == GamePhase.Mulligan; });
                    foreach (PlayerState p in Sim.Players)
                        Sim.ReportMulliganDecision(p, new MulliganResponse { Action = MulliganAction.Commit, MulliganCardNetworkUids = new List<int>() });
                    Until(delegate { return Ready(Sim); });
                }
                catch { Dispose(); throw; }
            }
            public ServerCard Avatar(int i) { return Sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, Sim.Players[i].NetId, GameZoneName.AvatarBoard); }
            public void Until(Func<bool> done)
            {
                for (int i = 0; i < 250; i++) { if (done()) return; LifecycleRestoration.FixedUpdate(Sim); }
                throw new InvalidOperationException("Ordinary native sequence did not settle: phase=" + Sim._gameState.GamePhase);
            }
            public void EndTurnTo(int next)
            {
                TurnState t = Sim._turnStateStack.Peek();
                Sim.PlayerServiceClients[t.ActivePlayerIndex]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.NoCommand, CommandUid = t.CurrentCommandItem.CommandList.ListUid });
                Until(delegate { return Ready(Sim) && Sim._turnStateStack.Peek().ActivePlayerIndex == next; });
            }
            public void Dispose()
            { RuneProphecyRestoration.Clear(Sim); if (Sim.PlayerServiceClients != null) foreach (PlayerServiceClient c in Sim.PlayerServiceClients) if (c != null) c.Stop(); scope.Dispose(); }
        }
    }
}
