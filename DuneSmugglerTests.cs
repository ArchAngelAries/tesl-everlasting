using System;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class DuneSmugglerTests
    {
        private const int Crab = 620729069, Smuggler = 1597455647;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("dune-smuggler-summon-move-and-friendly-movement", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Smuggler), "Smuggler not emitted: " + OriginalSpecRules.SkipReason(Smuggler));
                    ServerCard friend = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    ServerCard otherLane = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1);
                    ServerCard foe = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard smuggler = f.Create(Smuggler, me, GameZoneName.Hand);
                    f.AutoAnswerTargets = false; f.Mana(me, 30); f.Refresh(me);
                    CardCommandList play = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision d = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = play.ListUid, ActorCardNetUid = smuggler.NetUid.Id,
                        TargetCardNetUid = CardNetUid.GetInvalid().Id, LaneIndex = 0 };
                    Require(CommandRestoration.ValidateDecision(d, play, s.CardCatalog, s.Players[me]), "Smuggler play invalid.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(d);
                    f.Until(delegate { return s._turnStateStack.Count > 1 && s._turnStateStack.Peek().CurrentCommandItem != null
                        && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (s._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = s._turnStateStack.Peek().CurrentCommandItem.CommandList; CardCommandState choice = prompt.ValidCommands[0];
                    Require(choice.PlayTargets.Contains(friend.NetUid.Id) && !choice.PlayTargets.Contains(otherLane.NetUid.Id)
                        && !choice.PlayTargets.Contains(foe.NetUid.Id) && !choice.PlayTargets.Contains(smuggler.NetUid.Id), "Summon movement choices differ.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                        ActorCardNetUid = choice.CardNetUid, TargetCardNetUid = friend.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s) && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != play.ListUid; });
                    Require(friend.CurrentLaneIndex == 1 && friend.QueryAttribute(AttributeType.Attack).Value == 3
                        && friend.QueryAttribute(AttributeType.MaxHealth).Value == 2 && otherLane.QueryAttribute(AttributeType.Attack).Value == 2,
                        "Summon movement did not give its target exactly +1/+1.");
                    int power = smuggler.QueryAttribute(AttributeType.Attack).Value, health = smuggler.QueryAttribute(AttributeType.MaxHealth).Value;
                    Move(f, smuggler, 1);
                    Require(smuggler.QueryAttribute(AttributeType.Attack).Value == power + 1 && smuggler.QueryAttribute(AttributeType.MaxHealth).Value == health + 1,
                        "Smuggler's own movement did not grant +1/+1.");
                    Move(f, foe, 1);
                    Require(foe.QueryAttribute(AttributeType.Attack).Value == 2 && foe.QueryAttribute(AttributeType.MaxHealth).Value == 1,
                        "An opposing creature received the friendly movement bonus.");
                    f.Event(delegate { CardActions.Silence.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, smuggler, s.Players[me], -1); });
                    Move(f, friend, 0);
                    Require(friend.QueryAttribute(AttributeType.Attack).Value == 3 && friend.QueryAttribute(AttributeType.MaxHealth).Value == 2,
                        "Silenced Smuggler granted another movement bonus.");
                }
            });
            test("dune-smuggler-summon-without-other-friendly-target", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard smuggler = f.Create(Smuggler, me, GameZoneName.Hand);
                    f.AutoAnswerTargets = false; f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(smuggler, 0, me);
                    Require(smuggler.CurrentGameZoneName == GameZoneName.CreatureBoard && foe.CurrentLaneIndex == 0,
                        "Summon without a legal friendly target did not settle without moving a creature.");
                }
            });
        }
        private static void Move(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard card, int lane)
        {
            f.Event(delegate {
                Require(ServerCardCatalog.MoveCard(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                    new GameZoneId(card.OwnerNetId, GameZoneType.Board), new GameZoneId(card.OwnerNetId, GameZoneType.Board), card.InstanceId,
                    new GameZone.Position { LaneIndex = lane, CardIndex = 0 }, ServerCardCatalog.MoveOperationType.Untyped) == GameZoneOperationResult.Succeeded,
                    "Test movement failed.");
            });
            f.DrainEffects();
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
