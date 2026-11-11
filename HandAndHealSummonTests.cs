using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class HandAndHealSummonTests
    {
        private const int Crab = 620729069, Recruit = -100961620, Tutor = -643288632, Blow = -1226235166;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("effect-summon-recruit-chooses-own-action-in-hand", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard action = f.Create(Blow, me, GameZoneName.Hand), enemyAction = f.Create(Blow, enemy, GameZoneName.Hand);
                    ServerCard creature = f.Create(Crab, me, GameZoneName.Hand);
                    int before = action.QueryAttribute(AttributeType.ManaCost).Value;
                    ServerCard source = CreateByEffect(f, config, Recruit, me, false);
                    Choose(f, me, action, enemyAction, creature);
                    Require(action.QueryAttribute(AttributeType.ManaCost).Value == before - 2 && enemyAction.QueryAttribute(AttributeType.ManaCost).Value == before
                        && source.QueryGameJournalCount(CardJournal.Played) == 0, "Recruit must reduce only the chosen friendly action's cost by 2.");
                }
            });
            test("effect-summon-tutor-can-heal-itself", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard), foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = CreateByEffect(f, config, Tutor, me, true);
                    Require(source.CurrentHealth == 2, "Tutor fixture must be wounded before its queued Summon choice.");
                    Choose(f, me, source, null, null);
                    Require(source.CurrentHealth == 4 && friend.CurrentHealth == 1 && foe.CurrentHealth == 1
                        && source.QueryGameJournalCount(CardJournal.Played) == 0, "Tutor must heal only the chosen summoned creature.");
                }
            });
        }
        private static ServerCard CreateByEffect(OriginalTargetedSummonTests.LifecycleFixture f, GameConfiguration config, int id, int me, bool wound)
        {
            List<ServerCard> created = null; f.AutoAnswerTargets = false;
            f.Event(delegate {
                ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek());
                GameZone.Position p = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[me].NetId, GameZoneName.CreatureBoard, 1, CardInsertionType.Bottom);
                created = PlayerActions.CreateToken.StaticExecute(gs, null, config.FindCardById(id), false, GameZoneType.Board, p, f.Sim.Players[me].NetId, 1, -1, null);
                if (wound && created.Count == 1) created[0].ModifyCurrentHealth(-2, null, gs, true);
            });
            Require(created != null && created.Count == 1, "Native effect did not summon source."); return created[0];
        }
        private static void Choose(OriginalTargetedSummonTests.LifecycleFixture f, int me, ServerCard target, ServerCard excluded1, ServerCard excluded2)
        {
            f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                && f.Sim._turnStateStack.Peek().CurrentSequence == null && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
            CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList; CardCommandState choice = prompt.ValidCommands[0];
            Require(choice.PlayTargets.Contains(target.NetUid.Id) && (excluded1 == null || !choice.PlayTargets.Contains(excluded1.NetUid.Id))
                && (excluded2 == null || !choice.PlayTargets.Contains(excluded2.NetUid.Id)), "Summon offered incorrect targets.");
            f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                ActorCardNetUid = choice.CardNetUid, TargetCardNetUid = target.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
            f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
