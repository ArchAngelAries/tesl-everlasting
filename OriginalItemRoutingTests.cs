using System;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Server lifecycle assertions only. Synthetic Last Gasp is attached to one runtime
    // item instance; installed card templates are never changed. No rendered/UI claim.
    public static class OriginalItemRoutingTests
    {
        private const int Mudcrab = 620729069, Leviathan = 1427457192, IronSword = -1808851552;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("item-last-gasp-once-after-wielder-death", delegate { LastGasp(config, false); });
            test("item-last-gasp-not-on-silence-disposal", delegate { LastGasp(config, true); });
            test("item-staff-of-ice-paid-equip-explicit-target", delegate { ChosenDamage(config, -876842357, 2); });
            test("item-quicksilver-crossbow-paid-equip-explicit-target", delegate { ChosenDamage(config, 2107660239, 1); });
        }

        private static void LastGasp(GameConfiguration config, bool silence)
        {
            using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
            {
                Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                ServerCard wielder = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                ServerCard item = f.Create(IronSword, me, GameZoneName.Hand);
                f.Mana(me, 30); f.Refresh(me); f.PlayEquip(item, wielder, me);
                TriggeredEffect trigger = new TriggeredEffect { TriggerType = EffectTriggerType.LastGaspTrigger,
                    ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
                PlayerEffectAction draw = new PlayerEffectAction { Action = new PlayerActions.DrawCards {
                    NumCards = new ActionValueData(1), DrawFrom = PlayerActions.DrawCards.DrawDeck.Self,
                    OperationType = ServerCardCatalog.MoveOperationType.DefaultDraw, StopAtHandLimit = false, IgnoreDeckingRule = false } };
                draw.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = PlayerRole.Self });
                Effect effect = new Effect(); effect.PlayerActions.Add(draw); trigger.EffectGroup.Effects.Add(effect);
                item.TriggeredEffects.Add(trigger);
                int hand = f.HandCount(me), deck = f.DeckCount(me);
                f.Event(delegate {
                    ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                    if (silence) wielder.Silence(gs, wielder);
                    else wielder.ModifyCurrentHealth(-wielder.CurrentHealth, null, gs, true);
                });
                // Force ordinary lifecycle ticks even when the injected event leaves an old ready command list.
                for (int i = 0; i < 32; i++) LifecycleRestoration.FixedUpdate(s);
                f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                int expected = silence ? 0 : 1;
                Require(f.HandCount(me) == hand + expected && f.DeckCount(me) == deck - expected,
                    "Item Last Gasp draw count differs after " + (silence ? "silence" : "death") + ": hand delta=" + (f.HandCount(me) - hand) + ", deck delta=" + (deck - f.DeckCount(me)));
                Require(item.CurrentGameZoneType != GameZoneType.Board, "Disposed item remains on board.");
                Require(silence ? wielder.CurrentGameZoneType == GameZoneType.Board : wielder.CurrentGameZoneType != GameZoneType.Board,
                    "Wielder lifecycle did not complete.");
            }
        }

        private static void ChosenDamage(GameConfiguration config, int id, int damage)
        {
            using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
            {
                Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                f.AutoAnswerTargets = false;
                ServerCard wielder = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                ServerCard other = f.Create(Leviathan, 1 - me, GameZoneName.CreatureBoard);
                ServerCard target = f.CreateIn(Leviathan, 1 - me, GameZoneName.CreatureBoard, 1);
                ServerCard item = f.Create(id, me, GameZoneName.Hand);
                f.Mana(me, 30); f.Refresh(me);
                int health = target.CurrentHealth, otherHealth = other.CurrentHealth;
                int cost = item.QueryAttribute(AttributeType.ManaCost).Value;
                CardCommandList commands = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision play = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = commands.ListUid,
                    ActorCardNetUid = item.NetUid.Id, EquipCardNetUid = wielder.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id };
                Require(CommandRestoration.ValidateDecision(play, commands, s.CardCatalog, s.Players[me]), "Paid item equip was not legal.");
                s.PlayerServiceClients[me]._decisionMessages.Enqueue(play);
                f.Until(delegate {
                    if (s._turnStateStack.Count != 2) return false;
                    TurnState top = s._turnStateStack.Peek();
                    return top.CurrentSequence == null && top.CurrentCommandItem != null
                        && top.CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (top.CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0;
                });
                TurnState choice = s._turnStateStack.Peek(); CardCommandList prompt = choice.CurrentCommandItem.CommandList;
                CardCommandState command = prompt.ValidCommands[0];
                Require(choice.ActivePlayerIndex == me && command.PlayTargets.Contains(target.NetUid.Id), "Item Summon prompt omitted selected enemy or wrong player chooses.");
                Require(target.CurrentHealth == health && other.CurrentHealth == otherHealth, "Damage occurred before explicit selection.");
                Decision select = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                    ActorCardNetUid = command.CardNetUid, TargetCardNetUid = target.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id };
                Require(CommandRestoration.ValidateDecision(select, prompt, s.CardCatalog, s.Players[me]), "Explicit item Summon target was not legal.");
                s.PlayerServiceClients[me]._decisionMessages.Enqueue(select);
                f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                Require(target.CurrentHealth == health - damage && other.CurrentHealth == otherHealth,
                    "Chosen item Summon damaged wrong target or wrong number of times.");
                Require(item.ItemTarget == wielder.InstanceId && item.CurrentGameZoneName == GameZoneName.ItemBoard
                    && s.Players[me].QueryAttribute(AttributeType.Mana).Value == 30 - cost,
                    "Item equip attachment/payment differs.");
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
