using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalDamageSpellTests
    {
        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-damage-eight-exact-atomic-metadata-and-owned-graphs",delegate {
                List<Card> cards=OriginalDamageSpellRules.Validate(config);
                Require(cards.Count==8,"Original damage inventory differs.");
                object before=cards[0].ActionPlayInformation; string description=cards[7].Description;
                try {
                    cards[7].Description+=" Extra behavior."; bool rejected=false;
                    try { OriginalDamageSpellRules.Restore(config); } catch(NotSupportedException){rejected=true;}
                    Require(rejected && Object.ReferenceEquals(before,cards[0].ActionPlayInformation),"Original damage atomic preflight failed.");
                } finally {cards[7].Description=description;}
                OriginalDamageSpellRules.Restore(config);
                before=cards[0].ActionPlayInformation;
                OriginalDamageSpellRules.Restore(config);
                Require(Object.ReferenceEquals(before,cards[0].ActionPlayInformation),"Original damage graph identity changed.");
                CardActions.Damage damage=(CardActions.Damage)cards[7].ActionPlayInformation[0].EffectGroup.Effects[0].CardActions[0].Action;
                int amount=damage.HealthLoss.ConstantValue;
                try {
                    damage.HealthLoss.ConstantValue++; bool rejected=false;
                    try {OriginalDamageSpellRules.Restore(config);} catch(NotSupportedException){rejected=true;}
                    Require(rejected,"Changed native damage graph admitted.");
                } finally {damage.HealthLoss.ConstantValue=amount;}
            });
            test("original-firebolt-both-copies-native-creature-damage-ward-immunity",delegate {
                foreach(int id in new int[]{-1865755971,1293352672}) {
                    EngineTests.Fixture f=new EngineTests.Fixture(config);
                    ServerCard spell=f.CreateId(id,GameZoneName.Hand,0,f.One);
                    ServerCard target=f.CreateId(1427457192,GameZoneName.CreatureBoard,0,f.Two);
                    ServerCard ward=f.CreateId(1479393784,GameZoneName.CreatureBoard,0,f.One);
                    ServerCard hand=f.CreateId(1427457192,GameZoneName.Hand,0,f.One);
                    List<CardNetUid> targets=Targets(config,f,spell);
                    Require(targets.Contains(target.NetUid) && targets.Contains(ward.NetUid) && !targets.Contains(hand.NetUid)
                        && !targets.Contains(f.AvatarOne.NetUid) && !targets.Contains(f.AvatarTwo.NetUid),"Firebolt target scope differs.");
                    target.SetAttribute(AttributeType.ImmuneToActionDamage,1); f.PlayEffects(spell,target); f.DrainTriggers();
                    Require(target.CurrentHealth==8,"Native action immunity bypassed.");
                    target.SetAttribute(AttributeType.ImmuneToActionDamage,0); f.PlayEffects(spell,target); f.DrainTriggers();
                    Require(target.CurrentHealth==6,"Original Firebolt damage differs.");
                    f.PlayEffects(spell,ward); f.DrainTriggers();
                    Require(ward.CurrentHealth==2 && ward.QueryAttribute(AttributeType.Ward).Value==0,"Native spell Ward interaction differs.");
                }
            });
            test("original-crushing-lightning-all-copies-creature-avatar-targets",delegate {
                foreach(int id in new int[]{-1226235166,1744549196,1185654238,-759393216,41830377}) {
                    EngineTests.Fixture f=new EngineTests.Fixture(config);
                    ServerCard spell=f.CreateId(id,GameZoneName.Hand,0,f.One);
                    ServerCard ally=f.CreateId(1427457192,GameZoneName.CreatureBoard,0,f.One);
                    ServerCard enemy=f.CreateId(1427457192,GameZoneName.CreatureBoard,0,f.Two);
                    ServerCard hand=f.CreateId(1427457192,GameZoneName.Hand,0,f.One);
                    List<CardNetUid> targets=Targets(config,f,spell);
                    Require(targets.Contains(ally.NetUid) && targets.Contains(enemy.NetUid) && targets.Contains(f.AvatarOne.NetUid)
                        && targets.Contains(f.AvatarTwo.NetUid) && !targets.Contains(hand.NetUid),"Generic damage legal target scope differs.");
                    int amount=(id==-759393216 || id==41830377)?4:3;
                    f.PlayEffects(spell,enemy); f.DrainTriggers(); Require(enemy.CurrentHealth==8-amount,"Creature damage differs.");
                    f.PlayEffects(spell,f.AvatarTwo); f.DrainTriggers(); Require(f.AvatarTwo.CurrentHealth==30-amount,"Enemy avatar damage differs.");
                    f.PlayEffects(spell,f.AvatarOne); f.DrainTriggers(); Require(f.AvatarOne.CurrentHealth==30-amount,"Friendly avatar damage differs.");
                    Require(OriginalCardRules.Fingerprint(config.FindCardById(id))!=null,"Original metadata absent.");
                }
                OriginalDamageSpellRules.Validate(config);
            });
            test("original-ice-storm-native-all-creatures-both-lanes-ward",delegate {
                EngineTests.Fixture f=new EngineTests.Fixture(config);
                ServerCard spell=f.CreateId(-1571294504,GameZoneName.Hand,0,f.One);
                ServerCard ally=f.CreateId(1427457192,GameZoneName.CreatureBoard,0,f.One);
                ServerCard enemy=f.CreateId(1427457192,GameZoneName.CreatureBoard,1,f.Two);
                ServerCard ward=f.CreateId(1479393784,GameZoneName.CreatureBoard,0,f.Two);
                ServerCard hand=f.CreateId(1427457192,GameZoneName.Hand,0,f.Two);
                f.PlayEffects(spell,null); f.DrainTriggers();
                Require(ally.CurrentHealth==5 && enemy.CurrentHealth==5 && ward.CurrentHealth==2 && ward.QueryAttribute(AttributeType.Ward).Value==0
                    && hand.CurrentHealth==8 && f.AvatarOne.CurrentHealth==30 && f.AvatarTwo.CurrentHealth==30,"Ice Storm native board-wide damage scope differs.");
                OriginalDamageSpellRules.Validate(config);
            });
            test("original-damage-paid-targeted-play-cost-cemetery-journal",delegate {
                foreach(int id in new int[]{-1865755971,1293352672,-1226235166,1744549196,1185654238,-759393216,41830377})
                using(LifecycleFixture f=new LifecycleFixture(config,id)) {
                    Simulation s=f.Sim; int player=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard spell=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==id;},s.Players[player].NetId,GameZoneName.Hand);
                    ServerCard target=f.Create(1427457192,1-player,GameZoneName.CreatureBoard);
                    int cost=spell.QueryAttribute(AttributeType.ManaCost).Value;
                    int amount=(id==-1865755971 || id==1293352672)?2:(id==-759393216 || id==41830377)?4:3;
                    f.Event(delegate {PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),s.Players[player],12);});
                    s._turnStateStack.Peek().CurrentCommandItem=s.CardCatalog.GenerateCardCommandItem(s.Players[player],s.Players[1-player],s.Players);
                    f.Play(spell,target,player);
                    Require(target.CurrentHealth==8-amount && s.Players[player].QueryAttribute(AttributeType.Mana).Value==12-cost
                        && spell.CurrentGameZoneName==GameZoneName.Cemetery && spell.QueryGameJournalCount(CardJournal.Played)==1
                        && spell.QueryGameJournalCount(CardJournal.PlayedAsProphecy)==0,"Paid original damage target/cost/cemetery/journal differs.");
                }
            });
            test("original-lightning-both-copies-prophecy-free-play-and-parent-resumption",delegate {
                foreach(int id in new int[]{-759393216,41830377})
                using(LifecycleFixture f=new LifecycleFixture(config,id)) {
                    Simulation s=f.Sim; int parent=s._turnStateStack.Peek().ActivePlayerIndex, player=1-parent;
                    ServerCard target=f.Create(1427457192,parent,GameZoneName.CreatureBoard);
                    int mana=s.Players[player].QueryAttribute(AttributeType.Mana).Value;
                    ServerCard avatar=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.HasSubtype(CardSubtype.Avatar);},s.Players[player].NetId,GameZoneName.AvatarBoard);
                    f.Event(delegate {PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),null,s.Players[player],avatar.CurrentHealth-25,-1);});
                    f.Until(delegate {return s._turnStateStack.Count==2 && s._turnStateStack.Peek().Phase==TurnPhase.Interrupt
                        && s._turnStateStack.Peek().CurrentSequence==null && s._turnStateStack.Peek().CurrentCommandItem!=null
                        && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek());});
                    ServerCard spell=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==id;},s.Players[player].NetId,GameZoneName.Prophecy);
                    Require(spell!=null && spell.QueryAttribute(AttributeType.Prophecy).Value==1 && spell.QueryAttribute(AttributeType.ManaCost).Value==4,
                        "Original Lightning Bolt rune draw altered Prophecy/cost.");
                    f.Play(spell,target,player);
                    Require(target.CurrentHealth==4 && s.Players[player].QueryAttribute(AttributeType.Mana).Value==mana
                        && spell.CurrentGameZoneName==GameZoneName.Cemetery && spell.QueryGameJournalCount(CardJournal.PlayedAsProphecy)==1
                        && s._turnStateStack.Count==1 && s._turnStateStack.Peek().ActivePlayerIndex==parent
                        && s.Players[player].QueryAttribute(AttributeType.NumRunes).Value==4 && s.Players[parent].QueryAttribute(AttributeType.NumRunes).Value==5,
                        "Free Lightning damage/mana/rune ownership/parent resumption differs.");
                }
            });
            test("original-ice-storm-paid-no-target-native-play",delegate {
                using(LifecycleFixture f=new LifecycleFixture(config,-1571294504)) {
                    Simulation s=f.Sim; int player=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard spell=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==-1571294504;},s.Players[player].NetId,GameZoneName.Hand);
                    ServerCard ally=f.Create(1427457192,player,GameZoneName.CreatureBoard);
                    ServerCard enemy=f.Create(1427457192,1-player,GameZoneName.CreatureBoard);
                    ServerCard small=f.Create(794235553,1-player,GameZoneName.CreatureBoard);
                    int cost=spell.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Event(delegate {PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),s.Players[player],12);});
                    s._turnStateStack.Peek().CurrentCommandItem=s.CardCatalog.GenerateCardCommandItem(s.Players[player],s.Players[1-player],s.Players);
                    f.Play(spell,null,player);
                    // Intact native MoveToDeathrattle creates a fresh cemetery instance,
                    // then native FlushDeathrattleZone removes the former board instance.
                    ServerCard cemeteryClone=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==small.CardId;},
                        s.Players[1-player].NetId,GameZoneName.Cemetery);
                    ServerCard remainingSmall=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==small.CardId;},
                        s.Players[1-player].NetId,GameZoneName.CreatureBoard);
                    Require(ally.CurrentHealth==5 && enemy.CurrentHealth==5 && cemeteryClone!=null && !Object.ReferenceEquals(cemeteryClone,small)
                        && cemeteryClone.CardId==small.CardId && remainingSmall==null && small.CurrentGameZoneName==GameZoneName.RemoveFromGame
                        && s.Players[player].QueryAttribute(AttributeType.Mana).Value==12-cost && spell.CurrentGameZoneName==GameZoneName.Cemetery
                        && spell.QueryGameJournalCount(CardJournal.Played)==1 && spell.QueryGameJournalCount(CardJournal.PlayedAsProphecy)==0,
                        "Actual Ice Storm no-target play differs: ally="+ally.CurrentHealth+", enemy="+enemy.CurrentHealth+", smallHealth="+small.CurrentHealth+", smallZone="+small.CurrentGameZoneName+", cemeteryClone="+(cemeteryClone==null?"missing":cemeteryClone.CardId.ToString())+", mana="+s.Players[player].QueryAttribute(AttributeType.Mana).Value+", cost="+cost+", spellZone="+spell.CurrentGameZoneName+", played="+spell.QueryGameJournalCount(CardJournal.Played)+", prophecy="+spell.QueryGameJournalCount(CardJournal.PlayedAsProphecy));
                }
            });
        }
        private static bool Ready(Simulation s)
        { return s._gameState.GamePhase == GamePhase.MainLoop && s._turnStateStack.Count == 1 && s._turnStateStack.Peek().Phase == TurnPhase.PlayCards
            && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem != null && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek()); }


        // Bounded server fixture; twelve duplicate originals isolate targeted action
        // and rune orchestration. This is not a legal user deck or physical UI test.
        internal sealed class LifecycleFixture : IDisposable
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
                        PlayerDescription d = new PlayerDescription { PlayerName = "Original damage server test " + slot, AvatarCardId = OrdinaryMatchRules.BaseAvatarId, AiOpponentId = 0 };
                        for (int n = 0; n < 12; n++) { d.Deck.Add(spellId); d.Premium.Add(false); }
                        info.Users[slot] = new MatchConfiguration.User { CustomDeck = d, Type = PlayerType.AiOpponent, EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "damage-test-" + slot, SpectatorKey = "damage-observer-" + slot };
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

        private static List<CardNetUid> Targets(GameConfiguration config,EngineTests.Fixture f,ServerCard spell)
        {
            PlayCardAbility ability=config.FindCardById(spell.CardId).ActionPlayInformation[0];
            return f.Catalog.GatherSpellCardTargets(spell,ability.PlayTargetingType,ability.EffectGroup.Effects,ability.TargetSelectors,f.Players,false,false);
        }
        private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    }
}



