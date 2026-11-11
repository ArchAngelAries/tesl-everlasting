using System;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    // Scratch proposal: parent must integrate/build/run. Real paid item equip and native custom packets.
    public static class BoneArmorConsumeTests
    {
        const int Armor = 1664227168, Mudcrab = 620729069, Leviathan = 1427457192;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("bone-armor-paid-equip-explicit-consume-wielder-and-silence", delegate { Scenario(config, 0); });
            test("bone-armor-paid-equip-decline", delegate { Scenario(config, 1); });
            test("bone-armor-paid-equip-empty-cemetery", delegate { Scenario(config, 2); });
        }
        static void Scenario(GameConfiguration config, int mode)
        {
            Require(OriginalSpecRules.HasRestoredRules(Armor), "Bone Armor translation missing: " + OriginalSpecRules.SkipReason(Armor));
            using (var f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
            {
                Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                f.AutoAnswerTargets = false;
                ServerCard first = null, second = null;
                if (mode != 2) {
                    first = f.Create(Mudcrab, me, GameZoneName.Cemetery);
                    second = f.Create(Leviathan, me, GameZoneName.Cemetery);
                }
                ServerCard wielder = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                ServerCard ally = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                ServerCard item = f.Create(Armor, me, GameZoneName.Hand);
                int basePower = wielder.QueryAttribute(AttributeType.Attack).Value;
                int baseHealth = wielder.QueryAttribute(AttributeType.MaxHealth).Value;
                int itemPower = item.QueryAttribute(AttributeType.Attack).Value;
                int itemHealth = item.QueryAttribute(AttributeType.MaxHealth).Value;
                int consumedHealth = second == null ? 0 : second.QueryAttribute(AttributeType.MaxHealth).Value;
                int cost = item.QueryAttribute(AttributeType.ManaCost).Value;
                f.Mana(me, 30); f.Refresh(me);
                CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                var play = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid,
                    ActorCardNetUid = item.NetUid.Id, EquipCardNetUid = wielder.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(play, list, s.CardCatalog, s.Players[me]), "Paid Bone Armor equip rejected.");
                s.PlayerServiceClients[me]._decisionMessages.Enqueue(play);
                if (mode != 2) {
                    f.Until(delegate { return Prompt(s) != null; });
                    var prompt = Prompt(s);
                    Require(prompt.CardNetUid == wielder.NetUid.Id, "Item Consume actor must be wielder.");
                    Require(prompt.DisplayCards.Count == 2, "Wrong own cemetery choices.");
                    int chosen = -1;
                    for (int i = 0; i < prompt.DisplayCards.Count; i++)
                        if (prompt.DisplayCards[i].CardNetUid == second.NetUid.Id) chosen = i;
                    Require(chosen >= 0, "Selected food instance missing.");
                    var current = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    var choice = mode == 1 ? new Decision { Type = CcgRpc.DecisionType.NoCommand, CommandUid = current.ListUid }
                        : new Decision { Type = CcgRpc.DecisionType.CustomChoice, CommandUid = current.ListUid, CustomChoice = chosen };
                    Require(CommandRestoration.ValidateDecision(choice, current, s.CardCatalog, s.Players[me]), "Consume selection/decline rejected.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(choice);
                }
                f.Until(delegate { return item.CurrentGameZoneName == GameZoneName.ItemBoard && OriginalTargetedSummonTests.Ready(s)
                    && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
                int reward = mode == 0 ? consumedHealth : 0;
                Require(item.ItemTarget == wielder.InstanceId, "Item attached to wrong creature.");
                Require(wielder.QueryAttribute(AttributeType.Attack).Value == basePower + itemPower
                    && wielder.QueryAttribute(AttributeType.MaxHealth).Value == baseHealth + itemHealth + reward
                    && wielder.CurrentHealth == baseHealth + itemHealth + reward, "Exact wielder stats differ.");
                Require(ally.QueryAttribute(AttributeType.Attack).Value == 8 && ally.QueryAttribute(AttributeType.MaxHealth).Value == 8,
                    "Consume affected other ally.");
                Require(item.QueryAttribute(AttributeType.Attack).Value == itemPower && item.QueryAttribute(AttributeType.MaxHealth).Value == itemHealth,
                    "Reward incorrectly modified item stats.");
                Require(s.Players[me].QueryAttribute(AttributeType.Mana).Value == 30-cost, "Item did not pay exact cost.");
                if (mode != 2) {
                    Require(first.CurrentGameZoneType == GameZoneType.Cemetery, "Unselected food moved.");
                    Require(second.CurrentGameZoneType == (mode == 0 ? GameZoneType.Purgatory : GameZoneType.Cemetery), "Consume/decline movement differs.");
                }
                Require(s._turnStateStack.Peek().CustomUICards.Count == 0, "Item Consume custom state leaked.");
                if (mode == 0) {
                    f.Event(delegate { CardActions.Silence.StaticExecuteSingleTarget(DecisionRestoration.State(s,s._turnStateStack.Peek()), null, wielder, s.Players[me], -1); });
                    f.DrainEffects();
                    Require(wielder.Silenced && wielder.QueryAttribute(AttributeType.MaxHealth).Value == baseHealth,
                        "Silence retained Consume health reward.");
                }
            }
        }
        static CardCommandState Prompt(Simulation s)
        {
            var item = s._turnStateStack.Peek().CurrentCommandItem;
            if (item == null || item.CommandList == null) return null;
            foreach (var command in item.CommandList.ValidCommands)
                if (command.CustomCardUiEnum == (int)CustomCardUiType.TargetCemetery) return command;
            return null;
        }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
