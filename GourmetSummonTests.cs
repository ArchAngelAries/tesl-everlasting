using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class GourmetSummonTests
    {
        private const int Crab = 620729069, Leviathan = 1427457192, SweetRoll = 1843354014;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "normal", 694231768);
            Case(config, test, "exclusive", -1192168222);
        }
        private static void Case(GameConfiguration config, Action<string, Action> test, string label, int id)
        {
            test("effect-summoned-gourmet-transform-" + label, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Card definition = config.FindCardById(id);
                    Require(definition.CardReferences != null && definition.CardReferences.Count == 1 && definition.CardReferences[0].resourceId == SweetRoll,
                        "Installed Gourmet Sweet Roll reference differs.");
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard), foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard strong = f.CreateIn(Leviathan, enemy, GameZoneName.CreatureBoard, 1);
                    List<ServerCard> created = null; f.AutoAnswerTargets = false;
                    f.Event(delegate {
                        GameZone.Position p = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[me].NetId, GameZoneName.CreatureBoard, 1, CardInsertionType.Bottom);
                        created = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, definition, false, GameZoneType.Board, p, f.Sim.Players[me].NetId, 1, -1, null);
                    });
                    Require(created != null && created.Count == 1, "Native effect did not summon Gourmet.");
                    f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                        && f.Sim._turnStateStack.Peek().CurrentSequence == null && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList; CardCommandState choice = prompt.ValidCommands[0];
                    Require(choice.PlayTargets.Contains(foe.NetUid.Id) && !choice.PlayTargets.Contains(friend.NetUid.Id)
                        && !choice.PlayTargets.Contains(strong.NetUid.Id) && !choice.PlayTargets.Contains(created[0].NetUid.Id), "Gourmet offered an illegal target.");
                    f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                        ActorCardNetUid = choice.CardNetUid, TargetCardNetUid = foe.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
                    // Native TransformCard moves the old instance to Purgatory and creates a replacement.
                    List<ServerCard> rolls = f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == SweetRoll; },
                        f.Sim.Players[enemy].NetId, GameZoneName.CreatureBoard);
                    Require(rolls.Count == 1 && rolls[0].OwnerNetId == f.Sim.Players[enemy].NetId && rolls[0].CurrentLaneIndex == 0
                        && rolls[0].QueryAttribute(AttributeType.Attack).Value == 0 && rolls[0].CurrentHealth == 1
                        && friend.CurrentHealth == 1 && strong.CurrentHealth == 8 && created[0].QueryGameJournalCount(CardJournal.Played) == 0,
                        "Expected one original 0/1 Sweet Roll in the target lane; got " + rolls.Count + " (old target zone " + foe.CurrentGameZoneName + ").");
                }
            });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
