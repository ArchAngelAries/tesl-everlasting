using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalHealingSummonTests
    {
        public static void Run(GameConfiguration config,Action<string,Action> test)
        {
            test("original-healing-six-exact-atomic-metadata-owned-graph",delegate {
                List<Card> cards=OriginalHealingSummonRules.Validate(config); Require(cards.Count==6,"Original healing inventory differs.");
                object before=cards[0].TriggeredEffects; string text=cards[5].Description;
                try {
                    cards[5].Description+=" Extra effect."; bool rejected=false;
                    try{OriginalHealingSummonRules.Restore(config);}catch(NotSupportedException){rejected=true;}
                    Require(rejected && Object.ReferenceEquals(before,cards[0].TriggeredEffects),"Original healing atomic preflight failed.");
                }finally{cards[5].Description=text;}
                OriginalHealingSummonRules.Restore(config); before=cards[0].TriggeredEffects;
                OriginalHealingSummonRules.Restore(config);Require(Object.ReferenceEquals(before,cards[0].TriggeredEffects),"Healing graph identity changed.");
                CardActions.Heal heal=(CardActions.Heal)cards[5].TriggeredEffects[0].EffectGroup.Effects[0].CardActions.Find(delegate(CardEffectAction x) { return x.Action is CardActions.Heal; }).Action;   // an original PlayCustomFx may precede it (OriginalFxOverlay)
                try{
                    heal.FullHeal=true;bool rejected=false;
                    try{OriginalHealingSummonRules.Restore(config);}catch(NotSupportedException){rejected=true;}
                    Require(rejected,"Changed original native Heal mode admitted.");
                }finally{heal.FullHeal=false;}
            });
            test("original-healing-six-native-owner-avatar-only-and-above-starting-health",delegate {
                foreach(Card card in OriginalHealingSummonRules.Validate(config)) {
                    EngineTests.Fixture f=new EngineTests.Fixture(config);
                    ServerCard source=f.CreateId(card.ResourceId.Id,GameZoneName.CreatureBoard,0,f.One);
                    ServerCard ally=f.CreateId(1427457192,GameZoneName.CreatureBoard,0,f.One);
                    ServerCard enemy=f.CreateId(1427457192,GameZoneName.CreatureBoard,0,f.Two);
                    int amount=OriginalHealingSummonRules.Amount(card.ResourceId.Id);
                    f.AvatarOne.SetAvatarHealthPreGame(29);f.AvatarTwo.SetAvatarHealthPreGame(20);
                    f.One.SetAttribute(AttributeType.NumRunes,3);
                    Summon(f,source);
                    Require(f.AvatarOne.CurrentHealth==29+amount && f.AvatarTwo.CurrentHealth==20 && ally.CurrentHealth==8 && enemy.CurrentHealth==8
                        && f.One.QueryAttribute(AttributeType.NumRunes).Value==3,"Original heal owner/above-starting-health/creature isolation/runes differs: "+card.ResourceId.Id);
                }
            });
            test("original-healing-native-NoHealing-DoubleHealing-and-999-ceiling",delegate {
                EngineTests.Fixture f=new EngineTests.Fixture(config);
                ServerCard source=f.CreateId(1078595393,GameZoneName.CreatureBoard,0,f.One);
                f.AvatarOne.SetAvatarHealthPreGame(25);f.AvatarOne.SetAttribute(AttributeType.NoHealing,1);Summon(f,source);
                Require(f.AvatarOne.CurrentHealth==25,"Original native NoHealing bypassed.");
                f.AvatarOne.SetAttribute(AttributeType.NoHealing,0);f.AvatarOne.SetAttribute(AttributeType.DoubleHealing,1);Summon(f,source);
                Require(f.AvatarOne.CurrentHealth==29,"Original native DoubleHealing multiplier differs.");
                f.AvatarOne.SetAttribute(AttributeType.DoubleHealing,0);f.AvatarOne.SetAvatarHealthPreGame(998);Summon(f,source);
                Require(f.AvatarOne.CurrentHealth==999 && f.AvatarTwo.CurrentHealth==30,"Original native health ceiling/opponent isolation differs.");
            });
            test("original-healing-six-paid-native-summon-cost-single-heal",delegate {
                foreach(Card card in OriginalHealingSummonRules.Validate(config))
                using(LifecycleFixture f=new LifecycleFixture(config,620729069)) {
                    Simulation s=f.Sim;int player=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source=f.Create(card.ResourceId.Id,player,GameZoneName.Hand);
                    ServerCard avatar=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.HasSubtype(CardSubtype.Avatar);},s.Players[player].NetId,GameZoneName.AvatarBoard);
                    ServerCard enemyAvatar=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.HasSubtype(CardSubtype.Avatar);},s.Players[1-player].NetId,GameZoneName.AvatarBoard);
                    int cost=source.QueryAttribute(AttributeType.ManaCost).Value;int amount=OriginalHealingSummonRules.Amount(card.ResourceId.Id);
                    int deck=s.CardCatalog.FindCardCountInDeck(s.Players[player].NetId);
                    f.Event(delegate{avatar.SetAvatarHealthPreGame(29);PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),s.Players[player],12);});
                    s._turnStateStack.Peek().CurrentCommandItem=s.CardCatalog.GenerateCardCommandItem(s.Players[player],s.Players[1-player],s.Players);
                    f.Play(source,null,player);
                    Require(source.CurrentGameZoneName==GameZoneName.CreatureBoard && avatar.CurrentHealth==29+amount && enemyAvatar.CurrentHealth==30
                        && s.Players[player].QueryAttribute(AttributeType.Mana).Value==12-cost && source.QueryGameJournalCount(CardJournal.Played)==1
                        && s.CardCatalog.FindCardCountInDeck(s.Players[player].NetId)==deck,
                        "Original paid healing summon cost/single-heal/owner/unchanged-deck differs: "+card.ResourceId.Id);
                }
            });
            test("original-healing-prophecy-free-summon-single-heal-and-parent-resumption",delegate {
                int exercised=0;
                foreach(Card card in OriginalHealingSummonRules.Validate(config)) {
                    bool prophecy=false;
                    foreach(SPTESL.Attribute attribute in card.EntityAttributes) {
                        BoolAttribute flag=attribute as BoolAttribute;
                        if(attribute.Name==AttributeType.Prophecy && flag!=null && flag.Value)prophecy=true;
                    }
                    if(!prophecy)continue; exercised++;
                    using(LifecycleFixture f=new LifecycleFixture(config,card.ResourceId.Id)) {
                        Simulation s=f.Sim;int parent=s._turnStateStack.Peek().ActivePlayerIndex,player=1-parent;
                        int mana=s.Players[player].QueryAttribute(AttributeType.Mana).Value;
                        ServerCard avatar=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.HasSubtype(CardSubtype.Avatar);},s.Players[player].NetId,GameZoneName.AvatarBoard);
                        ServerCard enemyAvatar=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.HasSubtype(CardSubtype.Avatar);},s.Players[parent].NetId,GameZoneName.AvatarBoard);
                        f.Event(delegate {PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),null,s.Players[player],avatar.CurrentHealth-25,-1);});
                        f.Until(delegate {return s._turnStateStack.Count==2 && s._turnStateStack.Peek().Phase==TurnPhase.Interrupt
                            && s._turnStateStack.Peek().CurrentSequence==null && s._turnStateStack.Peek().CurrentCommandItem!=null
                            && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek());});
                        ServerCard source=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==card.ResourceId.Id;},s.Players[player].NetId,GameZoneName.Prophecy);
                        Require(source!=null && source.QueryAttribute(AttributeType.Prophecy).Value==1,"Original healing rune draw did not offer Prophecy: "+card.ResourceId.Id);
                        f.Play(source,null,player);
                        Require(source.CurrentGameZoneName==GameZoneName.CreatureBoard && source.QueryGameJournalCount(CardJournal.PlayedAsProphecy)==1
                            && avatar.CurrentHealth==25+OriginalHealingSummonRules.Amount(card.ResourceId.Id) && enemyAvatar.CurrentHealth==30
                            && s.Players[player].QueryAttribute(AttributeType.Mana).Value==mana
                            && s.Players[player].QueryAttribute(AttributeType.NumRunes).Value==4 && s.Players[parent].QueryAttribute(AttributeType.NumRunes).Value==5
                            && s._turnStateStack.Count==1 && s._turnStateStack.Peek().ActivePlayerIndex==parent,
                            "Original free healing Summon single-heal/mana/runes/parent resumption differs: "+card.ResourceId.Id);
                    }
                }
                Require(exercised==5,"Original healing Prophecy test inventory differs.");
            });
        }
        private static void Summon(EngineTests.Fixture f,ServerCard source)
        {
            Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State,new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon,source,f.Players)),"Original healing Summon trigger not gathered.");
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




