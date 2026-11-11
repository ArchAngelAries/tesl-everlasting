using System;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalDrawStrategyTests
    {
        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("strategic-original-draw-summon-avoids-lethal-empty-deck",delegate {
                using(OriginalDamageStrategyTests.Fixture f=new OriginalDamageStrategyTests.Fixture(config))
                {
                    ServerCard source=ServerCardCatalog.CreateCard(f.State,-1728909427,false,GameZoneName.Hand,0,CardInsertionType.Bottom,f.One.NetId,null,null);
                    f.One.SetAttribute(AttributeType.Mana,12); f.One.SetAttribute(AttributeType.NumRunes,0);
                    f.AvatarOne.SetAvatarHealthPreGame(20);
                    Decision choice=f.Choose();
                    Require(choice.Type==CcgRpc.DecisionType.NoCommand,"AI summoned a lethal draw from an empty deck.");
                    Require(f.AvatarOne.CurrentHealth==20 && source.CurrentGameZoneName==GameZoneName.Hand,"Draw scoring mutated native state.");
                }
            });
            test("strategic-original-draw-summon-values-available-draw",delegate {
                using(OriginalDamageStrategyTests.Fixture f=new OriginalDamageStrategyTests.Fixture(config))
                {
                    ServerCard source=ServerCardCatalog.CreateCard(f.State,-1728909427,false,GameZoneName.Hand,0,CardInsertionType.Bottom,f.One.NetId,null,null);
                    ServerCard hidden=ServerCardCatalog.CreateCard(f.State,1427457192,false,GameZoneName.Deck,0,CardInsertionType.Bottom,f.One.NetId,null,null);
                    f.One.SetAttribute(AttributeType.Mana,12); f.One.SetAttribute(AttributeType.NumRunes,0);
                    Decision choice=f.Choose();
                    Require(choice.Type==CcgRpc.DecisionType.Play && choice.ActorCardNetUid==source.NetUid.Id,"AI rejected a useful original summon with a card left.");
                    Require(hidden.CurrentGameZoneName==GameZoneName.Deck && source.CurrentGameZoneName==GameZoneName.Hand,"Ranking drew a hidden card.");
                }
            });
        }
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
