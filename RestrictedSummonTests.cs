using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class RestrictedSummonTests
    {
        private const int Crab = 620729069, Leviathan = 1427457192, Fervor = -1653188426;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "high-priestess", 1746885506, false, false);
            Case(config, test, "spiteful-dremora", 1462928361, false, true);
            Case(config, test, "vicious-dreugh", 1022895797, true, false);
            test("spiteful-dremora-self-targets-when-power-reduced", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = f.Create(1462928361, me, GameZoneName.Hand);
                    f.Event(delegate { source.SetAttribute(AttributeType.Attack, 2); });
                    Require(source.QueryAttribute(AttributeType.Attack).Value == 2, "Fixture did not reduce Dremora's power.");
                    f.Mana(me, 20); f.Refresh(me);
                    EquipmentChoiceTests.ChooseAfterPlacement(f, source, source, friend, foe, me, true);
                    Require(f.CemeteryHas(me, 1462928361) && friend.CurrentHealth == 1 && foe.CurrentHealth == 1,
                        "A 2-power Dremora must be allowed to destroy itself, leaving the other creatures untouched.");
                }
            });
        }
        private static void Case(GameConfiguration config, Action<string, Action> test, string label, int id, bool support, bool anySide)
        {
            test("effect-summoned-restricted-target-" + label, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    GameZoneName zone = support ? GameZoneName.SupportBoard : GameZoneName.CreatureBoard;
                    int targetId = support ? Fervor : Crab;
                    ServerCard friend = f.Create(targetId, me, zone), foe = f.Create(targetId, enemy, zone);
                    ServerCard strong = support ? f.Create(Leviathan, enemy, GameZoneName.CreatureBoard) : f.CreateIn(Leviathan, enemy, GameZoneName.CreatureBoard, 1);
                    List<ServerCard> created = null; f.AutoAnswerTargets = false;
                    f.Event(delegate {
                        GameZone.Position p = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[me].NetId, GameZoneName.CreatureBoard, 1, CardInsertionType.Bottom);
                        created = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, config.FindCardById(id), false, GameZoneType.Board, p, f.Sim.Players[me].NetId, 1, -1, null);
                    });
                    Require(created != null && created.Count == 1, "Native effect did not summon the source.");
                    f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                        && f.Sim._turnStateStack.Peek().CurrentSequence == null && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    CardCommandState choice = prompt.ValidCommands[0];
                    Require(choice.PlayTargets.Contains(foe.NetUid.Id) && choice.PlayTargets.Contains(friend.NetUid.Id) == anySide
                        && !choice.PlayTargets.Contains(strong.NetUid.Id) && !choice.PlayTargets.Contains(created[0].NetUid.Id),
                        "Target list violated the card's side, type or power restriction.");
                    f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                        ActorCardNetUid = choice.CardNetUid, TargetCardNetUid = foe.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
                    Require(f.CemeteryHas(enemy, targetId) && friend.CurrentGameZoneName == zone && strong.CurrentGameZoneName == GameZoneName.CreatureBoard
                        && created[0].QueryGameJournalCount(CardJournal.Played) == 0, "Effect-summoned source did not destroy exactly the chosen target.");
                }
            });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
