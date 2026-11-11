using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    // Consumed-card rewards through paid play; ordinary banish and silence/death controls.
    public static class ImbuedConsumeTests
    {
        const int Consumer = 1646149449, Mudcrab = 620729069, Khajiit = 1565955924, Crocodile = 1265061955;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            int[] ids = { Khajiit, -1545346644, -1394876136, 362390617, 1657353473 };
            AttributeType[] attributes = { AttributeType.Drain, AttributeType.Breakthrough, AttributeType.Taunt, AttributeType.Deathtouch, AttributeType.Ward };
            for (int i=0;i<ids.Length;i++) { int id=ids[i]; AttributeType attribute=attributes[i];
                test("consume-imbued-paid-"+id, delegate {
                    using(var f = new OriginalTargetedSummonTests.LifecycleFixture(config,Mudcrab)) {
                        int me=f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard selected=f.Create(id,me,GameZoneName.Cemetery);
                        ServerCard consumer=Consume(f,me,selected);
                        Require(consumer.QueryAttribute(AttributeType.Attack).Value==6 && consumer.QueryAttribute(AttributeType.MaxHealth).Value==5,"Imbued stats were absent or applied repeatedly.");
                        Require(consumer.QueryAttribute(attribute).Value>0,"Imbued keyword absent.");
                        if (id == Khajiit) Require(f.Avatar(me).CurrentHealth == 32 && f.Avatar(1-me).CurrentHealth == 28,
                            "Imbued Drain must resolve before both Dragonknight damage effects.");
                    }
                });
            }
            test("consume-crocodile-draw-paid", delegate {
                using(var f=new OriginalTargetedSummonTests.LifecycleFixture(config,Mudcrab)) {
                    int me=f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard selected=f.Create(Crocodile,me,GameZoneName.Cemetery);
                    int hand=f.HandCount(me), deck=f.DeckCount(me);
                    Consume(f,me,selected);
                    Require(f.HandCount(me)==hand+1 && f.DeckCount(me)==deck-1,"Crocodile did not draw exactly one card.");
                }
            });
            test("imbued-ordinary-banish-does-not-consume", delegate {
                using(var f=new OriginalTargetedSummonTests.LifecycleFixture(config,Mudcrab)) {
                    Simulation s=f.Sim; int me=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard candidate=f.Create(Khajiit,me,GameZoneName.Cemetery);
                    ServerCard actor=f.Create(Mudcrab,me,GameZoneName.CreatureBoard);
                    int power=actor.QueryAttribute(AttributeType.Attack).Value, health=actor.QueryAttribute(AttributeType.MaxHealth).Value;
                    f.Event(delegate {
                        var gs=DecisionRestoration.State(s,s._turnStateStack.Peek());
                        var ca=new CardConditionalArguments(s.CardCatalog,actor,actor,candidate,null,null,LaneIdentifier.GetInvalid());
                        var pa=new PlayerConditionalArguments(s.CardCatalog,s.Players[me],s.Players[me],s.Players[me],null,s.Players[1-me]);
                        CardActions.Banish.StaticExecute(new List<ServerCard>{candidate},new InsertPosition{Type=InsertPosition.LaneChoiceType.Constant,InsertionType=CardInsertionType.Bottom},true,gs,ca,pa,-1,false,false,HistoryType.ActionBanish);
                    });
                    f.DrainEffects();
                    Require(candidate.CurrentGameZoneType==GameZoneType.Purgatory,"Banish failed.");
                    Require(actor.QueryAttribute(AttributeType.Attack).Value==power && actor.QueryAttribute(AttributeType.MaxHealth).Value==health && actor.QueryAttribute(AttributeType.Drain).Value==0,"Ordinary Banish incorrectly fired Consume reward.");
                }
            });
            test("silenced-imbued-dies-restores-consumable-printed-effect", delegate {
                using(var f=new OriginalTargetedSummonTests.LifecycleFixture(config,Mudcrab)) {
                    Simulation s=f.Sim; int me=s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard victim=f.Create(Khajiit,me,GameZoneName.CreatureBoard);
                    f.Event(delegate { CardActions.Silence.StaticExecuteSingleTarget(DecisionRestoration.State(s,s._turnStateStack.Peek()),null,victim,s.Players[me],-1); });
                    Require(victim.Silenced,"Fixture silence failed.");
                    f.Event(delegate { CardActions.Destroy.StaticExecute(DecisionRestoration.State(s,s._turnStateStack.Peek()),new List<ServerCard>{victim},null,-1,false,null); });
                    f.Until(delegate { return f.CemeteryHas(me,Khajiit); });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    var grave=s.CardCatalog.GatherCardsInZone(delegate(ServerCard c){return c.CardId==Khajiit;},s.Players[me].NetId,GameZoneName.Cemetery);
                    Require(grave.Count==1 && !grave[0].Silenced,"Native cemetery copy retained silence.");
                    ServerCard consumer=Consume(f,me,grave[0]);
                    Require(consumer.QueryAttribute(AttributeType.Attack).Value==6 && consumer.QueryAttribute(AttributeType.MaxHealth).Value==5 && consumer.QueryAttribute(AttributeType.Drain).Value>0,"Printed cemetery Consume reward was not restored after silence/death.");
                }
            });
        }
        static ServerCard Consume(OriginalTargetedSummonTests.LifecycleFixture f,int me,ServerCard selected)
        {
            Simulation s=f.Sim; f.AutoAnswerTargets=false;
            ServerCard consumer=f.Create(Consumer,me,GameZoneName.Hand);
            f.Mana(me,30); f.Refresh(me);
            var list=s._turnStateStack.Peek().CurrentCommandItem.CommandList;
            var play=new Decision{Type=CcgRpc.DecisionType.Play,CommandUid=list.ListUid,ActorCardNetUid=consumer.NetUid.Id};
            Require(CommandRestoration.ValidateDecision(play,list,s.CardCatalog,s.Players[me]),"Consumer paid play invalid.");
            s.PlayerServiceClients[me]._decisionMessages.Enqueue(play);
            f.Until(delegate { return Prompt(s)!=null; });
            var prompt=Prompt(s); var choices=s._turnStateStack.Peek().CurrentCommandItem.CommandList;
            int index=-1;
            for(int i=0;i<prompt.DisplayCards.Count;i++) if(prompt.DisplayCards[i].CardNetUid==selected.NetUid.Id) index=i;
            Require(index>=0,"Exact selected Imbued absent from display.");
            var decision=new Decision{Type=CcgRpc.DecisionType.CustomChoice,CommandUid=choices.ListUid,CustomChoice=index};
            Require(CommandRestoration.ValidateDecision(decision,choices,s.CardCatalog,s.Players[me]),"Native custom packet rejected.");
            s.PlayerServiceClients[me]._decisionMessages.Enqueue(decision);
            f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
            Require(selected.CurrentGameZoneType==GameZoneType.Purgatory,"Selected creature not consumed.");
            Require(s._turnStateStack.Peek().CustomUICards.Count==0,"Custom state leaked.");
            return consumer;
        }
        static CardCommandState Prompt(Simulation s)
        {
            var item=s._turnStateStack.Peek().CurrentCommandItem; if(item==null)return null;
            foreach(var c in item.CommandList.ValidCommands)if(c.CustomCardUiEnum==(int)CustomCardUiType.TargetCemetery)return c;
            return null;
        }
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
