using System;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    // Real paid Consume lifecycle: native selection packets, decline, empty discard and rewards.
    public static class OfflineConsumeChoiceTests
    {
        const int Consumer = 1646149449, Mudcrab = 620729069, Leviathan = 1427457192;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("consume-paid-summon-explicit-second-choice", delegate { Scenario(config, 0); });
            test("consume-paid-summon-decline", delegate { Scenario(config, 1); });
            test("consume-paid-summon-empty-discard", delegate { Scenario(config, 2); });
            test("consume-four-stat-rewards-real-paid-summon", delegate {
                int[] ids = {746861909, 190391314, 683060108, -286712589};
                int[] attack = {2,1,0,0}, health = {0,1,0,2};
                for (int n = 0; n < ids.Length; n++)
                using (var f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard food = f.Create(Mudcrab, me, GameZoneName.Cemetery);
                    ServerCard ally = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                    ServerCard enemy = f.Create(Leviathan, 1-me, GameZoneName.CreatureBoard);
                    ServerCard c = f.Create(ids[n], me, GameZoneName.Hand);
                    int a = c.QueryAttribute(AttributeType.Attack).Value, h = c.QueryAttribute(AttributeType.MaxHealth).Value;
                    f.Mana(me,30); f.Refresh(me); f.Play(c,null,me);
                    Require(food.CurrentGameZoneType == GameZoneType.Purgatory, "Reward did not consume chosen card.");
                    Require(c.QueryAttribute(AttributeType.Attack).Value == a+attack[n] && c.QueryAttribute(AttributeType.MaxHealth).Value == h+health[n], "Consume self stats differ: " + ids[n]);
                    Require(ally.QueryAttribute(AttributeType.Attack).Value == (n==2 ? 9 : 8) && ally.QueryAttribute(AttributeType.MaxHealth).Value == (n==2 ? 9 : 8), "Other-friendly reward differs.");
                    Require(enemy.QueryAttribute(AttributeType.Attack).Value == 8 && enemy.QueryAttribute(AttributeType.MaxHealth).Value == 8, "Consume reward affected enemy.");
                }
            });
        }
        static void Require(bool b, string message) { if (!b) throw new InvalidOperationException(message); }
        static void Scenario(GameConfiguration config, int mode)
        {
            Require(OriginalSpecRules.HasRestoredRules(Consumer), "Dragonknight translation missing: " + OriginalSpecRules.SkipReason(Consumer));
            using (var f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
            {
                Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                f.AutoAnswerTargets = false;
                ServerCard first = null, second = null;
                if (mode != 2) {
                    first = f.Create(Mudcrab, me, GameZoneName.Cemetery);
                    second = f.Create(Mudcrab, me, GameZoneName.Cemetery); // same card identity, distinct exact instance
                }
                f.Create(Leviathan, 1-me, GameZoneName.Cemetery); // opponent is not eligible
                f.Create(-1808851552, me, GameZoneName.Cemetery); // an item is not eligible
                ServerCard consumer = f.Create(Consumer, me, GameZoneName.Hand);
                f.Mana(me, 30); f.Refresh(me);
                int myHealth = f.Avatar(me).CurrentHealth, theirHealth = f.Avatar(1-me).CurrentHealth;
                int cost = consumer.QueryAttribute(AttributeType.ManaCost).Value;
                CardCommandList playList = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                var play = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = playList.ListUid, ActorCardNetUid = consumer.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(play, playList, s.CardCatalog, s.Players[me]), "Paid summon invalid.");
                s.PlayerServiceClients[me]._decisionMessages.Enqueue(play);
                if (mode != 2)
                {
                    f.Until(delegate { return Prompt(s) != null; });
                    var list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    var command = Prompt(s);
                    Require(command.DisplayCards.Count == 2 && command.NumActionChoices == 2, "Expected two exact cemetery choices.");
                    int selected = -1;
                    for (int i = 0; i < command.DisplayCards.Count; i++) if (command.DisplayCards[i].CardNetUid == second.NetUid.Id) selected = i;
                    Require(selected == 1, "Second cemetery instance index must remain exact.");
                    var invalid = new Decision { Type = CcgRpc.DecisionType.CustomChoice, CommandUid = list.ListUid, CustomChoice = 2 };
                    Require(!CommandRestoration.ValidateDecision(invalid, list, s.CardCatalog, s.Players[me]), "Out-of-range choice accepted.");
                    invalid.CustomChoice = -1;
                    Require(!CommandRestoration.ValidateDecision(invalid, list, s.CardCatalog, s.Players[me]), "Negative index accepted instead of native decline packet.");
                    invalid.CustomChoice = selected; invalid.ActorCardNetUid = consumer.NetUid.Id;
                    Require(!CommandRestoration.ValidateDecision(invalid, list, s.CardCatalog, s.Players[me]), "Malformed actor-bearing choice packet accepted.");
                    var choice = mode == 1
                        ? new Decision { Type = CcgRpc.DecisionType.NoCommand, CommandUid = list.ListUid }
                        : new Decision { Type = CcgRpc.DecisionType.CustomChoice, CommandUid = list.ListUid, CustomChoice = selected };
                    Require(CommandRestoration.ValidateDecision(choice, list, s.CardCatalog, s.Players[me]), "Explicit selection/decline rejected.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(choice);
                }
                f.Until(delegate { return OriginalTargetedSummonTests.Ready(s) && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != playList.ListUid; });
                Require(consumer.CurrentGameZoneType == GameZoneType.Board, "Consumer not played.");
                Require(s.Players[me].QueryAttribute(AttributeType.Mana).Value == 30-cost, "Consume play did not pay exact cost.");
                if (mode != 2) {
                    Require(first.CurrentGameZoneType == GameZoneType.Cemetery, "Unselected cemetery card moved.");
                    Require(second.CurrentGameZoneType == (mode == 0 ? GameZoneType.Purgatory : GameZoneType.Cemetery), "Chosen card movement differs.");
                }
                Require(f.Avatar(me).CurrentHealth == myHealth - (mode == 0 ? 2 : 0) && f.Avatar(1-me).CurrentHealth == theirHealth - (mode == 0 ? 2 : 0), "Consume reward applied without successful selection or failed to apply.");
                Require(s._turnStateStack.Peek().CustomUICards.Count == 0, "Custom choice cards leaked into the next action.");
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
    }
}
