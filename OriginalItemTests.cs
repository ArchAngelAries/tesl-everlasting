using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalItemTests
    {
        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-items-exact-fourteen-metadata-and-atomic-preflight", delegate {
                List<Card> cards=OriginalItemRules.Validate(config);
                Require(cards.Count==14,"Item inventory differs.");
                object before=cards[0].TriggeredEffects; string text=cards[13].Description;
                try {
                    cards[13].Description+=" Unexpected ability."; bool rejected=false;
                    try {OriginalItemRules.Restore(config);} catch(NotSupportedException){rejected=true;}
                    Require(rejected && Object.ReferenceEquals(before,cards[0].TriggeredEffects),"Item preflight mutated or admitted changed metadata.");
                } finally {cards[13].Description=text;}
                OriginalItemRules.Restore(config);
                Require(Object.ReferenceEquals(before,cards[0].TriggeredEffects),"Item restore not idempotent.");
            });
            test("original-items-all-fourteen-native-paid-equip-stats-targets-and-journal", delegate {
                foreach(Card original in OriginalItemRules.Validate(config))
                using(OriginalDamageSpellTests.LifecycleFixture f=new OriginalDamageSpellTests.LifecycleFixture(config,original.ResourceId.Id))
                {
                    Simulation s=f.Sim; int p=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard item=s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==original.ResourceId.Id;},s.Players[p].NetId,GameZoneName.Hand);
                    ServerCard ally=f.Create(1427457192,p,GameZoneName.CreatureBoard);
                    ServerCard enemy=f.Create(1427457192,1-p,GameZoneName.CreatureBoard);
                    int power=ally.QueryAttribute(AttributeType.Attack).Value, health=ally.CurrentHealth;
                    int bonusPower=item.QueryAttribute(AttributeType.Attack).Value, bonusHealth=item.QueryAttribute(AttributeType.MaxHealth).Value;
                    int cost=item.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Event(delegate {PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),s.Players[p],12);});
                    CardCommandItem commands=s.CardCatalog.GenerateCardCommandItem(s.Players[p],s.Players[1-p],s.Players);
                    s._turnStateStack.Peek().CurrentCommandItem=commands;
                    Decision play=new Decision { Type=CcgRpc.DecisionType.Play,CommandUid=commands.CommandList.ListUid,ActorCardNetUid=item.NetUid.Id,EquipCardNetUid=enemy.NetUid.Id };
                    Require(!CommandRestoration.ValidateDecision(play,commands.CommandList,s.CardCatalog,s.Players[p]),"Enemy equip admitted.");
                    play.EquipCardNetUid=ally.NetUid.Id;
                    Require(CommandRestoration.ValidateDecision(play,commands.CommandList,s.CardCatalog,s.Players[p]),"Original equip command missing: "+original.Name);
                    s.PlayerServiceClients[p]._decisionMessages.Enqueue(play);
                    f.Until(delegate {return Ready(s) && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid!=commands.CommandList.ListUid;});
                    Require(item.CurrentGameZoneName==GameZoneName.ItemBoard && item.ItemTarget==ally.InstanceId
                        && ally.QueryAttribute(AttributeType.Attack).Value==power+bonusPower && ally.CurrentHealth==health+bonusHealth
                        && s.Players[p].QueryAttribute(AttributeType.Mana).Value==12-cost && item.QueryGameJournalCount(CardJournal.Played)==1,
                        "Native equipped item stats/cost/link/journal differ: "+original.Name+", itemZone="+item.CurrentGameZoneName+", attack="+ally.QueryAttribute(AttributeType.Attack).Value+", health="+ally.CurrentHealth);
                }
            });
            test("original-items-native-wielder-death-cleans-equipped-item", delegate {
                using(OriginalDamageSpellTests.LifecycleFixture f=new OriginalDamageSpellTests.LifecycleFixture(config,844499977))
                {
                    Simulation s=f.Sim; int p=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard ally=f.Create(1427457192,p,GameZoneName.CreatureBoard);
                    ServerCard item=f.Create(844499977,p,GameZoneName.ItemBoard);
                    f.Event(delegate {CardActions.Equip.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),item,ally,-1);});
                    f.Event(delegate {CardActions.Destroy.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),new List<ServerCard>{ally},null,-1,false,null);});
                    f.Until(delegate {return s.CardCatalog.FindCardCount(GameZoneName.CreatureBoard,s.Players[p].NetId)==0
                        && s.CardCatalog.FindCardCount(GameZoneName.ItemBoard,s.Players[p].NetId)==0;});
                    Require(s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c){return c.CardId==844499977;},s.Players[p].NetId,GameZoneName.Cemetery)!=null,
                        "Equipped item absent from native cemetery after wielder death.");
                }
            });
            test("original-items-strategy-equips-owned-creature-without-mutating", delegate {
                using(OriginalDamageSpellTests.LifecycleFixture f=new OriginalDamageSpellTests.LifecycleFixture(config,844499977))
                {
                    Simulation s=f.Sim; int p=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard ally=f.Create(1427457192,p,GameZoneName.CreatureBoard);
                    f.Create(1427457192,1-p,GameZoneName.CreatureBoard);
                    f.Event(delegate {PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),s.Players[p],12);});
                    CardCommandItem commands=s.CardCatalog.GenerateCardCommandItem(s.Players[p],s.Players[1-p],s.Players);
                    s._turnStateStack.Peek().CurrentCommandItem=commands;
                    Decision choice=StrategicDecisionRestoration.ChooseDecision(s,commands.CommandList);
                    Require(choice.Type==CcgRpc.DecisionType.Play && choice.EquipCardNetUid==ally.NetUid.Id
                        && CommandRestoration.ValidateDecision(choice,commands.CommandList,s.CardCatalog,s.Players[p]),"Item AI did not select a legal own wielder.");
                    Require(ally.CurrentHealth==8 && s.Players[p].QueryAttribute(AttributeType.Mana).Value==12,"Item ranking mutated state.");
                }
            });
        }
        private static bool Ready(Simulation s)
        {return s._turnStateStack.Count==1 && s._turnStateStack.Peek().CurrentSequence==null && s._turnStateStack.Peek().CurrentCommandItem!=null
            && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek());}
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
