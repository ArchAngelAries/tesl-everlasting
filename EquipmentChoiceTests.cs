using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    public static class EquipmentChoiceTests
    {
        private const int Courier = -511363521, Smith = 1914295650, Sword = -1126089104, Scimitar = 342030232, Crab = 620729069;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("courier-summon-selects-self-after-placement", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = f.Create(Courier, me, GameZoneName.Hand);
                    Require(OriginalSpecRules.HasRestoredRules(Courier), "Courier spec missing: " + OriginalSpecRules.SkipReason(Courier));
                    f.Mana(me, 20); f.Refresh(me);
                    ChooseAfterPlacement(f, source, source, friend, foe, me, false);
                    Require(source.CurrentGameZoneName == GameZoneName.CreatureBoard && source.QueryAttribute(AttributeType.Attack).Value == 3
                        && Items(f, source, Sword) == 1 && Items(f, friend, Sword) == 0 && Items(f, foe, Sword) == 0
                        && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 18 && source.QueryGameJournalCount(CardJournal.Played) == 1,
                        "Courier must equip the chosen self with one original +3/+0 Sword after one paid play.");
                }
            });
            test("lower-canton-smith-plot-and-item-identity", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard smith = f.Create(Smith, me, GameZoneName.Hand);
                    Require(OriginalSpecRules.HasRestoredRules(Smith), "Smith spec missing: " + OriginalSpecRules.SkipReason(Smith));
                    f.Mana(me, 20); f.Refresh(me); f.PlayCreatureLane(smith, 0, me);
                    Require(Items(f, smith, Scimitar) == 0 && smith.QueryAttribute(AttributeType.Attack).Value == 2,
                        "Plot fired on the first card played this turn.");
                }
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.Hand);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard smith = f.Create(Smith, me, GameZoneName.Hand);
                    f.Mana(me, 20); f.Refresh(me); f.PlayCreatureLane(friend, 0, me);
                    ChooseAfterPlacement(f, smith, friend, smith, foe, me, false);
                    Require(Items(f, friend, Scimitar) == 1 && Items(f, smith, Scimitar) == 0 && Items(f, foe, Scimitar) == 0
                        && friend.QueryAttribute(AttributeType.Attack).Value == 4 && friend.CurrentHealth == 3
                        && smith.CurrentGameZoneName == GameZoneName.CreatureBoard && smith.QueryGameJournalCount(CardJournal.Played) == 1,
                        "Plot must equip the chosen friendly with one original +2/+2 Scimitar.");
                }
            });
            test("wardcrafter-summon-can-choose-self-or-enemy", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = f.Create(-987994366, me, GameZoneName.Hand);
                    f.Mana(me, 20); f.Refresh(me);
                    ChooseAfterPlacement(f, source, source, friend, foe, me, true);
                    Require(source.QueryAttribute(AttributeType.Ward).Value > 0 && friend.QueryAttribute(AttributeType.Ward).Value == 0
                        && foe.QueryAttribute(AttributeType.Ward).Value == 0 && source.QueryGameJournalCount(CardJournal.Played) == 1,
                        "Wardcrafter must give Ward only to the chosen source.");
                }
            });
        }
        internal static void ChooseAfterPlacement(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard source,
            ServerCard chosen, ServerCard other, ServerCard enemy, int me, bool allowEnemy)
        {
            f.AutoAnswerTargets = false;
            CardCommandList play = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
            Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = play.ListUid, ActorCardNetUid = source.NetUid.Id,
                TargetCardNetUid = CardNetUid.GetInvalid().Id, LaneIndex = 1 };
            Require(CommandRestoration.ValidateDecision(decision, play, f.Sim.CardCatalog, f.Sim.Players[me]), "Creature play was not legal before the choice.");
            f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(decision);
            f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                && f.Sim._turnStateStack.Peek().CurrentSequence == null
                && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
            CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
            CardCommandState choices = prompt.ValidCommands[0];
            Require(choices.PlayTargets.Contains(chosen.NetUid.Id) && choices.PlayTargets.Contains(other.NetUid.Id)
                && choices.PlayTargets.Contains(enemy.NetUid.Id) == allowEnemy, "Choice must include both friendly creatures and match enemy eligibility.");
            f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play,
                CommandUid = prompt.ListUid, ActorCardNetUid = choices.CardNetUid, TargetCardNetUid = chosen.NetUid.Id,
                EquipCardNetUid = CardNetUid.GetInvalid().Id });
            f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim) && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != play.ListUid; });
        }
        private static int Items(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard target, int id)
        { return f.Sim.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.CardId == id && c.ItemTarget == target.InstanceId; }).Count; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
