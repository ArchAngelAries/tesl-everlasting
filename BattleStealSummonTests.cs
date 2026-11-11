using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class BattleStealSummonTests
    {
        private const int Crab = 620729069, Leviathan = 1427457192, Orc = 899518525;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "emeric-battle", 728231988, 0);
            Case(config, test, "arena-champion-battle", 536764784, 0);
            Case(config, test, "stoneshard-orc-count", 1345679378, 1);
            Case(config, test, "miraak-steals", -1874170560, 2);
        }
        private static void Case(GameConfiguration config, Action<string, Action> test, string label, int id, int mode)
        {
            test("effect-summon-" + label, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 0);
                    if (mode == 1) f.CreateIn(Orc, me, GameZoneName.CreatureBoard, 0);
                    ServerCard target = f.Create(mode == 1 ? Leviathan : Crab, enemy, GameZoneName.CreatureBoard);
                    List<ServerCard> created = null; f.AutoAnswerTargets = false;
                    f.Event(delegate {
                        GameZone.Position p = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[me].NetId, GameZoneName.CreatureBoard, 1, CardInsertionType.Bottom);
                        created = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, config.FindCardById(id), false, GameZoneType.Board, p, f.Sim.Players[me].NetId, 1, -1, null);
                    });
                    Require(created != null && created.Count == 1, "Native effect did not summon source.");
                    f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                        && f.Sim._turnStateStack.Peek().CurrentSequence == null && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList; CardCommandState choice = prompt.ValidCommands[0];
                    Require(choice.PlayTargets.Contains(target.NetUid.Id) && !choice.PlayTargets.Contains(friend.NetUid.Id)
                        && !choice.PlayTargets.Contains(created[0].NetUid.Id), "Summon choice must target an enemy creature only.");
                    f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                        ActorCardNetUid = choice.CardNetUid, TargetCardNetUid = target.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
                    bool effect = mode == 0 ? f.CemeteryHas(enemy, Crab) && created[0].CurrentHealth < created[0].QueryAttribute(AttributeType.MaxHealth).Value
                        : mode == 1 ? target.CurrentHealth == 6
                        : f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Crab; }, f.Sim.Players[me].NetId, GameZoneName.CreatureBoard).Count == 1
                            && f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Crab; }, f.Sim.Players[enemy].NetId, GameZoneName.CreatureBoard).Count == 0;
                    Require(effect && created[0].QueryGameJournalCount(CardJournal.Played) == 0 && friend.CurrentHealth == 8,
                        "Effect-summoned card did not apply its own Battle, Orc-count damage or steal exactly as expected.");
                }
            });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
