using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalSimpleSummonTests
    {
        public static void Run(GameConfiguration config,Action<string,Action> test)
        {
            test("original-summon-nine-exact-atomic-metadata-graph-and-idempotence",delegate {
                List<Card> cards=OriginalSimpleSummonRules.Validate(config); Require(cards.Count==9,"Original draw-summon inventory differs.");
                object before=cards[0].TriggeredEffects; string text=cards[8].Description;
                try {
                    cards[8].Description+=" Extra effect."; bool rejected=false;
                    try {OriginalSimpleSummonRules.Restore(config);}catch(NotSupportedException){rejected=true;}
                    Require(rejected && Object.ReferenceEquals(before,cards[0].TriggeredEffects),"Original summon preflight mutated before complete validation.");
                } finally {cards[8].Description=text;}
                OriginalSimpleSummonRules.Restore(config); before=cards[0].TriggeredEffects;
                OriginalSimpleSummonRules.Restore(config); Require(Object.ReferenceEquals(before,cards[0].TriggeredEffects),"Original summon graph identity changed.");
                PlayerActions.DrawCards draw=(PlayerActions.DrawCards)cards[8].TriggeredEffects[0].EffectGroup.Effects[0].PlayerActions[0].Action;
                try {
                    draw.IgnoreDeckingRule=true; bool rejected=false;
                    try {OriginalSimpleSummonRules.Restore(config);}catch(NotSupportedException){rejected=true;}
                    Require(rejected,"Changed original summon native draw flags admitted.");
                } finally {draw.IgnoreDeckingRule=false;}
            });
            test("original-summon-nine-native-self-draw-hidden-uid-and-opponent-isolation",delegate {
                foreach(Card card in OriginalSimpleSummonRules.Validate(config)) {
                    EngineTests.Fixture f=new EngineTests.Fixture(config);
                    ServerCard source=f.CreateId(card.ResourceId.Id,GameZoneName.CreatureBoard,0,f.One);
                    ServerCard top=f.CreateId(620729069,GameZoneName.Deck,0,f.One);
                    ServerCard other=f.CreateId(620729069,GameZoneName.Deck,0,f.Two); int uid=top.NetUid.Id;
                    Summon(f,source);
                    Require(top.CurrentGameZoneName==GameZoneName.Hand && top.NetUid.Id!=uid
                        && f.Catalog.FindCardCountInDeck(f.One.NetId)==0 && f.Catalog.FindCardCount(GameZoneName.Hand,f.One.NetId)==1
                        && other.CurrentGameZoneName==GameZoneName.Deck && f.Catalog.FindCardCount(GameZoneName.Hand,f.Two.NetId)==0
                        && f.AvatarOne.CurrentHealth==30 && f.AvatarTwo.CurrentHealth==30,"Original summon native draw/source-owner/UID differs: "+card.ResourceId.Id);
                }
            });
            test("original-summon-native-full-hand-burn-preserves-draw-dependency",delegate {
                EngineTests.Fixture f=new EngineTests.Fixture(config);
                ServerCard source=f.CreateId(-1728909427,GameZoneName.CreatureBoard,0,f.One);
                for(int n=0;n<10;n++)f.CreateId(620729069,GameZoneName.Hand,0,f.One);
                ServerCard top=f.CreateId(1427457192,GameZoneName.Deck,0,f.One); Summon(f,source);
                Require(f.Catalog.FindCardCount(GameZoneName.Hand,f.One.NetId)==10 && f.Catalog.FindCardCountInDeck(f.One.NetId)==0
                    && f.Catalog.FindCardCount(GameZoneName.Cemetery,f.One.NetId)==1 && top.CurrentGameZoneName==GameZoneName.Cemetery,
                    "Original summon did not burn drawn excess card through native full-hand discard.");
            });
            test("original-summon-native-empty-deck-rune-and-health-rule",delegate {
                EngineTests.Fixture f=new EngineTests.Fixture(config);
                f.One.SetAttribute(AttributeType.NumRunes,5); f.One.SetAttribute(AttributeType.RuneHealth,5); f.One.SetAttribute(AttributeType.NumRunesLost,0);
                ServerCard source=f.CreateId(-1728909427,GameZoneName.CreatureBoard,0,f.One); Summon(f,source);
                Require(f.AvatarOne.CurrentHealth==25 && f.One.QueryAttribute(AttributeType.NumRunes).Value==4
                    && f.One.QueryAttribute(AttributeType.NumRunesLost).Value==1 && f.AvatarTwo.CurrentHealth==30
                    && f.Catalog.FindCardCount(GameZoneName.Hand,f.One.NetId)==0,
                    "Original summon empty-deck native rule differs: health="+f.AvatarOne.CurrentHealth+", runes="+f.One.QueryAttribute(AttributeType.NumRunes).Value);
            });
            test("original-summon-nine-paid-native-creature-play-single-draw",delegate {
                foreach(Card card in OriginalSimpleSummonRules.Validate(config))
                using(LifecycleFixture f=new LifecycleFixture(config,-1728909427)) {
                    Simulation s=f.Sim; int player=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source=f.Create(card.ResourceId.Id,player,GameZoneName.Hand);
                    int hand=s.CardCatalog.FindCardCount(GameZoneName.Hand,s.Players[player].NetId);
                    int deck=s.CardCatalog.FindCardCountInDeck(s.Players[player].NetId);
                    int enemyDeck=s.CardCatalog.FindCardCountInDeck(s.Players[1-player].NetId);
                    int cost=source.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Event(delegate{PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),s.Players[player],12);});
                    s._turnStateStack.Peek().CurrentCommandItem=s.CardCatalog.GenerateCardCommandItem(s.Players[player],s.Players[1-player],s.Players);
                    f.Play(source,null,player);
                    Require(source.CurrentGameZoneName==GameZoneName.CreatureBoard && s.Players[player].QueryAttribute(AttributeType.Mana).Value==12-cost
                        && source.QueryGameJournalCount(CardJournal.Played)==1 && s.CardCatalog.FindCardCountInDeck(s.Players[player].NetId)==deck-1
                        && s.CardCatalog.FindCardCount(GameZoneName.Hand,s.Players[player].NetId)==hand
                        && s.CardCatalog.FindCardCountInDeck(s.Players[1-player].NetId)==enemyDeck,
                        "Original paid summon cost/board/single draw/journal differs: "+card.ResourceId.Id);
                }
            });
        }
        private static void Summon(EngineTests.Fixture f,ServerCard source)
        {
            Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State,new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon,source,f.Players)),"Original summon trigger not gathered.");
            f.DrainTriggers();
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private static bool Ready(Simulation s)
        { return s._gameState.GamePhase == GamePhase.MainLoop && s._turnStateStack.Count == 1 && s._turnStateStack.Peek().Phase == TurnPhase.PlayCards
            && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem != null && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek()); }


        // Bounded server fixture; twelve duplicate originals isolate targeted action
        // and rune orchestration. This is not a legal user deck or physical UI test.
        private sealed class LifecycleFixture : IDisposable
        {
            public readonly Simulation Sim = new Simulation();
            private readonly IDisposable scope;
            public LifecycleFixture(GameConfiguration config, int spellId)
            {
                scope = OrdinaryMatchRules.Prepare(config);
                try
                {
                    MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo { ScenarioId = OrdinaryMatchRules.DefaultScenarioId, RandomNumberGenerator = new RandomIntegerGenerator(12345) };
                    for (int slot = 0; slot < 2; slot++)
                    {
                        PlayerDescription d = new PlayerDescription { PlayerName = "Original draw summon test " + slot, AvatarCardId = OrdinaryMatchRules.BaseAvatarId, AiOpponentId = 0 };
                        for (int n = 0; n < 12; n++) { d.Deck.Add(spellId); d.Premium.Add(false); }
                        info.Users[slot] = new MatchConfiguration.User { CustomDeck = d, Type = PlayerType.AiOpponent, EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "summon-draw-test-" + slot, SpectatorKey = "summon-draw-observer-" + slot };
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
                    CardInsertionType.Bottom, Sim.Players[owner].NetId, null, null); }); return result;
            }
            public void Event(Action action)
            { Sim.HistoryLog.StartEvent(HistoryType.EventTrigger, Sim.CurrentPlayer.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1); try { action(); } finally { Sim.HistoryLog.EndEvent(); } }
            public void Play(ServerCard spell, ServerCard target, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = spell.NetUid.Id, TargetCardNetUid = target == null ? CardNetUid.GetInvalid().Id : target.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Original damage command is invalid.");
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public void Until(Func<bool> done)
            { for (int tick = 0; tick < 500; tick++) { if (done()) return; LifecycleRestoration.FixedUpdate(Sim); } throw new InvalidOperationException("Damage native sequence did not settle: " + Sim._gameState.GamePhase); }
            public void Dispose()
            { RuneProphecyRestoration.Clear(Sim); if (Sim.PlayerServiceClients != null) foreach (PlayerServiceClient client in Sim.PlayerServiceClients) if (client != null) client.Stop(); scope.Dispose(); }
        }


    }
}

