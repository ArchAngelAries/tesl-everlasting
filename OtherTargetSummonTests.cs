using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    public static class OtherTargetSummonTests
    {
        private const int Crab = 620729069, Guard = 1700638025;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "cursed-spectre", 767299130, 0);
            Case(config, test, "earthbone-spinner", 1911637876, 1);
            Case(config, test, "dwarven-dynamo", 880884825, 2);
            test("other-target-effect-summoned-dynamo", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    List<ServerCard> created = null; f.AutoAnswerTargets = false;
                    f.Event(delegate {
                        GameZone.Position p = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[me].NetId, GameZoneName.CreatureBoard, 1, CardInsertionType.Bottom);
                        created = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, config.FindCardById(880884825), false, GameZoneType.Board, p, f.Sim.Players[me].NetId, 1, -1, null);
                    });
                    Require(created != null && created.Count == 1, "Native effect did not summon Dynamo.");
                    f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                        && f.Sim._turnStateStack.Peek().CurrentSequence == null && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    CardCommandState choice = prompt.ValidCommands[0];
                    Require(choice.PlayTargets.Contains(friend.NetUid.Id) && !choice.PlayTargets.Contains(created[0].NetUid.Id), "Effect-summoned Dynamo must choose another creature.");
                    f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                        ActorCardNetUid = choice.CardNetUid, TargetCardNetUid = friend.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
                    Require(friend.QueryAttribute(AttributeType.Attack).Value == 5 && friend.CurrentHealth == 4 && friend.QueryAttribute(AttributeType.Taunt).Value > 0
                        && created[0].QueryGameJournalCount(CardJournal.Played) == 0, "Effect-summoned Dynamo did not buff and Guard the chosen other creature.");
                }
            });
        }
        private static void Case(GameConfiguration config, Action<string, Action> test, string name, int id, int mode)
        {
            test("other-target-summon-" + name, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(Guard, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = f.Create(id, me, GameZoneName.Hand);
                    ServerCard target = mode == 2 ? friend : foe;
                    int manaCost = source.QueryAttribute(AttributeType.ManaCost).Value;
                    f.AutoAnswerTargets = false; f.Mana(me, 20); f.Refresh(me);
                    CardCommandList play = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision d = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = play.ListUid, ActorCardNetUid = source.NetUid.Id,
                        TargetCardNetUid = CardNetUid.GetInvalid().Id, LaneIndex = 1 };
                    Require(CommandRestoration.ValidateDecision(d, play, f.Sim.CardCatalog, f.Sim.Players[me]), "Play was not legal before targeting.");
                    f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(d);
                    f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                        && f.Sim._turnStateStack.Peek().CurrentSequence == null && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    CardCommandState choice = prompt.ValidCommands[0];
                    Require(choice.PlayTargets.Contains(friend.NetUid.Id) && choice.PlayTargets.Contains(foe.NetUid.Id)
                        && !choice.PlayTargets.Contains(source.NetUid.Id), "Another-creature choice has incorrect targets.");
                    f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                        ActorCardNetUid = choice.CardNetUid, TargetCardNetUid = target.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim) && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != play.ListUid; });
                    bool effect = mode == 2 ? friend.QueryAttribute(AttributeType.Attack).Value == 5 && friend.CurrentHealth == 4 && friend.QueryAttribute(AttributeType.Taunt).Value > 0
                        : foe.QueryAttribute(AttributeType.Taunt).Value == 0 && foe.CurrentHealth == (mode == 1 ? 1 : 2);
                    Require(effect && source.CurrentGameZoneName == GameZoneName.CreatureBoard && source.QueryGameJournalCount(CardJournal.Played) == 1
                        && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 20 - manaCost,
                        "Chosen other creature did not receive the correct Summon effects after a paid play.");
                }
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source = f.Create(id, me, GameZoneName.Hand);
                    f.AutoAnswerTargets = false; f.Mana(me, 20); f.Refresh(me); f.PlayCreatureLane(source, 0, me);
                    Require(source.CurrentGameZoneName == GameZoneName.CreatureBoard, "With no other creature, Summon should settle without a choice.");
                }
            });
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
