using System;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    // Native two-stage packets through real paid play, with no automatic target answering.
    public static class ConsumeTargetTests
    {
        const int Butcher = -1179285674, Mudcrab = 620729069, Leviathan = 1427457192;
        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("consume-then-explicit-enemy-target", delegate { RunCase(config,0); });
            test("consume-target-decline-no-reward", delegate { RunCase(config,1); });
            test("consume-target-empty-grave-no-reward", delegate { RunCase(config,2); });
            test("consume-target-no-enemy-still-consumes", delegate { RunCase(config,3); });
            test("consume-power-snapshot-before-board-choice", delegate { ValueCase(config, false, 3); });
            test("consume-zero-power-is-zero-damage", delegate { ValueCase(config, false, 0); });
            test("consume-health-reward-uses-selected-stats", delegate { ValueCase(config, true, 5); ValueCase(config, true, 2); });
            test("consume-pilfer-snapshot-both-stats-once", delegate { PilferCase(config,0); });
            test("consume-pilfer-decline-counts-once", delegate { PilferCase(config,1); });
            test("consume-pilfer-empty-counts-once", delegate { PilferCase(config,2); });
        }
        static void PilferCase(GameConfiguration config,int mode)
        {
            using(var f=new OriginalTargetedSummonTests.LifecycleFixture(config,Mudcrab))
            {
                Simulation s=f.Sim; int me=s._turnStateStack.Peek().ActivePlayerIndex;
                ServerCard food=mode==2?null:f.Create(Leviathan,me,GameZoneName.Cemetery);
                if(food!=null){food.SetAttribute(AttributeType.Attack,3);food.SetAttribute(AttributeType.MaxHealth,5);}
                ServerCard source=f.Create(-44117704,me,GameZoneName.Hand);
                f.Mana(me,30);f.Refresh(me);f.Play(source,null,me);
                Check(food==null || food.CurrentGameZoneType==GameZoneType.Cemetery,"Pilfer consumed on Summon.");
                f.EndTurn();f.EndTurn();f.AutoAnswerTargets=false;f.Refresh(me);
                int a=source.QueryAttribute(AttributeType.Attack).Value,h=source.QueryAttribute(AttributeType.MaxHealth).Value;
                int count=s.Players[me].MatchStatValues[23];
                Send(s,me,new Decision{Type=CcgRpc.DecisionType.Attack,CommandUid=List(s).ListUid,ActorCardNetUid=source.NetUid.Id,TargetCardNetUid=f.Avatar(1-me).NetUid.Id});
                if(mode!=2){
                    f.Until(delegate{return Prompt(s,true)!=null;});
                    Send(s,me,new Decision{Type=mode==1?CcgRpc.DecisionType.NoCommand:CcgRpc.DecisionType.CustomChoice,CommandUid=List(s).ListUid,CustomChoice=0});
                }
                f.Until(delegate{return s.Players[me].MatchStatValues[23]>count && OriginalTargetedSummonTests.Ready(s);});
                Check(s.Players[me].MatchStatValues[23]==count+1,"Pilfer counted once per choice phase instead of once per attack.");
                Check(source.QueryAttribute(AttributeType.Attack).Value==a+(mode==0?3:0) && source.QueryAttribute(AttributeType.MaxHealth).Value==h+(mode==0?5:0),"Pilfer stats differ.");
                if(food!=null)Check(food.CurrentGameZoneType==(mode==0?GameZoneType.Purgatory:GameZoneType.Cemetery),"Pilfer consume/decline differs.");
            }
        }
        static void ValueCase(GameConfiguration config,bool heal,int value)
        {
            using(var f=new OriginalTargetedSummonTests.LifecycleFixture(config,Mudcrab))
            {
                Simulation s=f.Sim; int me=s._turnStateStack.Peek().ActivePlayerIndex; f.AutoAnswerTargets=false;
                ServerCard wrong=f.Create(Leviathan,me,GameZoneName.Cemetery);
                ServerCard food=f.Create(Mudcrab,me,GameZoneName.Cemetery);
                // Controlled graveyard attribute changes ensure the reward uses instance stats, not printed metadata.
                food.SetAttribute(heal?AttributeType.MaxHealth:AttributeType.Attack,value);
                ServerCard enemy=f.Create(Leviathan,1-me,GameZoneName.CreatureBoard);
                ServerCard source=f.Create(heal?-866240926:-1798266156,me,GameZoneName.Hand);
                f.Mana(me,30); f.Refresh(me);
                int before=f.Avatar(me).CurrentHealth;
                Send(s,me,new Decision{Type=CcgRpc.DecisionType.Play,CommandUid=List(s).ListUid,ActorCardNetUid=source.NetUid.Id});
                f.Until(delegate{return Prompt(s,true)!=null;});
                int index=-1; var p=Prompt(s,true);
                for(int i=0;i<p.DisplayCards.Count;i++)if(p.DisplayCards[i].CardNetUid==food.NetUid.Id)index=i;
                Check(index>=0,"Selected food absent.");
                Send(s,me,new Decision{Type=CcgRpc.DecisionType.CustomChoice,CommandUid=List(s).ListUid,CustomChoice=index});
                if(!heal){
                    f.Until(delegate{return Prompt(s,false)!=null;});
                    Check(food.CurrentGameZoneType==GameZoneType.Purgatory,"Not consumed before target.");
                    food.SetAttribute(AttributeType.Attack,17); // Must not alter the already captured reward.
                    Send(s,me,new Decision{Type=CcgRpc.DecisionType.Play,CommandUid=List(s).ListUid,ActorCardNetUid=source.NetUid.Id,TargetCardNetUid=enemy.NetUid.Id});
                }
                f.Until(delegate{return source.CurrentGameZoneType==GameZoneType.Board && OriginalTargetedSummonTests.Ready(s);});
                Check(wrong.CurrentGameZoneType==GameZoneType.Cemetery && food.CurrentGameZoneType==GameZoneType.Purgatory,"Wrong instance consumed.");
                Check(heal ? f.Avatar(me).CurrentHealth==before+value : enemy.CurrentHealth==8-value,"Consumed snapshot reward differs.");
                Check(s._turnStateStack.Peek().CustomUICards.Count==0,"Display state leaked.");
            }
        }
        static void Check(bool b,string message) { if (!b) throw new InvalidOperationException(message); }
        static CardCommandList List(Simulation s) { var i=s._turnStateStack.Peek().CurrentCommandItem; return i==null?null:i.CommandList; }
        static CardCommandState Prompt(Simulation s,bool custom)
        {
            var l=List(s); if(l==null || s._turnStateStack.Count<2)return null;
            foreach(var c in l.ValidCommands)
                if(custom ? c.CustomCardUiEnum==(int)CustomCardUiType.TargetCemetery : (c.ValidCardCommandMask&(int)CardCommand.ChooseTarget)!=0)return c;
            return null;
        }
        static void Send(Simulation s,int me,Decision d)
        {
            Check(CommandRestoration.ValidateDecision(d,List(s),s.CardCatalog,s.Players[me]),"Packet rejected: "+d.Type);
            s.PlayerServiceClients[me]._decisionMessages.Enqueue(d);
        }
        static void RunCase(GameConfiguration config,int mode)
        {
            using(var f=new OriginalTargetedSummonTests.LifecycleFixture(config,Mudcrab))
            {
                Simulation s=f.Sim; int me=s._turnStateStack.Peek().ActivePlayerIndex;
                f.AutoAnswerTargets=false;
                ServerCard food=mode==2?null:f.Create(Mudcrab,me,GameZoneName.Cemetery);
                ServerCard ally=f.Create(Leviathan,me,GameZoneName.CreatureBoard);
                ServerCard first=mode==3?null:f.Create(Leviathan,1-me,GameZoneName.CreatureBoard);
                ServerCard second=mode==3?null:f.Create(Leviathan,1-me,GameZoneName.CreatureBoard);
                ServerCard source=f.Create(Butcher,me,GameZoneName.Hand);
                int cost=source.QueryAttribute(AttributeType.ManaCost).Value;
                f.Mana(me,30); f.Refresh(me);
                Send(s,me,new Decision{Type=CcgRpc.DecisionType.Play,CommandUid=List(s).ListUid,ActorCardNetUid=source.NetUid.Id});
                if(mode!=2)
                {
                    f.Until(delegate{return Prompt(s,true)!=null;});
                    Check(Prompt(s,false)==null,"Board choice appeared before Consume.");
                    Send(s,me,new Decision{Type=mode==1?CcgRpc.DecisionType.NoCommand:CcgRpc.DecisionType.CustomChoice,CommandUid=List(s).ListUid,CustomChoice=0});
                }
                if(mode==0)
                {
                    f.Until(delegate{return Prompt(s,false)!=null;});
                    Check(food.CurrentGameZoneType==GameZoneType.Purgatory,"Board prompt preceded actual Consume.");
                    var p=Prompt(s,false);
                    Check(p.PlayTargets.Contains(first.NetUid.Id)&&p.PlayTargets.Contains(second.NetUid.Id)&&!p.PlayTargets.Contains(ally.NetUid.Id),"Target eligibility differs.");
                    var invalid=new Decision{Type=CcgRpc.DecisionType.Play,CommandUid=List(s).ListUid,ActorCardNetUid=source.NetUid.Id,TargetCardNetUid=ally.NetUid.Id};
                    Check(!CommandRestoration.ValidateDecision(invalid,List(s),s.CardCatalog,s.Players[me]),"Friendly target admitted.");
                    Send(s,me,new Decision{Type=CcgRpc.DecisionType.Play,CommandUid=List(s).ListUid,ActorCardNetUid=source.NetUid.Id,TargetCardNetUid=second.NetUid.Id});
                }
                f.Until(delegate{return source.CurrentGameZoneType==GameZoneType.Board && OriginalTargetedSummonTests.Ready(s);});
                Check(s.Players[me].QueryAttribute(AttributeType.Mana).Value==30-cost,"Paid cost differs.");
                Check(s._turnStateStack.Peek().CustomUICards.Count==0,"Cemetery choice leaked.");
                if(food!=null)Check(food.CurrentGameZoneType==(mode==1?GameZoneType.Cemetery:GameZoneType.Purgatory),"Consume/decline differs.");
                Check(ally.QueryAttribute(AttributeType.Freeze).Value==0,"Ally shackled.");
                if(first!=null)Check(first.QueryAttribute(AttributeType.Freeze).Value==0 && (second.QueryAttribute(AttributeType.Freeze).Value>0)==(mode==0),"Wrong creature shackled or reward leaked.");
            }
        }
    }
}
