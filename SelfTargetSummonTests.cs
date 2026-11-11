using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    public static class SelfTargetSummonTests
    {
        private const int Crab = 620729069;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Case(config, test, "barded-guar", -1209831649, AttributeType.Taunt, 1, true, false, false);
            Case(config, test, "morkul-gatekeeper", 1336815414, AttributeType.Attack, 2, true, true, false);
            Case(config, test, "pillaging-tribune", -1543808196, AttributeType.Drain, 1, false, false, true);
            Case(config, test, "skooma-racketeer", -622045452, AttributeType.Deathtouch, 1, true, false, false);
            Case(config, test, "ravenous-crocodile", -1661672729, AttributeType.None, -2, false, true, false);
            Case(config, test, "cloudrest-illusionist", 652289987, AttributeType.Attack, -4, true, true, true);
            Case(config, test, "savage-ogre", 842097822, AttributeType.Attack, 5, true, true, true);
            Case(config, test, "bitterfish-witch", 2095683028, AttributeType.None, -1, true, true, false);
            Case(config, test, "worm-kings-agent", 723253705, AttributeType.None, -2, true, true, false);
            ConditionalCase(config, test, "empire-dreadmage", 1849059690, false);
            ConditionalCase(config, test, "ash-piercer", 980278843, true);
            test("self-target-summon-black-hand-messenger-last-gasp", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = f.Create(-1118032492, me, GameZoneName.Hand);
                    f.Mana(me, 20); f.Refresh(me);
                    EquipmentChoiceTests.ChooseAfterPlacement(f, source, source, friend, foe, me, true);
                    // Native zone moves clone cards. The original board instance moves to RemoveFromGame;
                    // locate its cemetery replacement instead of reading the obsolete instance's journal.
                    // Messenger's printed Drain heals for its self-damage and its Last Gasp damage.
                    Require(f.CemeteryHas(me, -1118032492) && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 17
                        && f.Avatar(enemy).CurrentHealth == 28 && f.Avatar(me).CurrentHealth == 34
                        && friend.CurrentHealth == 1 && foe.CurrentHealth == 1,
                        "Messenger state: zone=" + source.CurrentGameZoneName + " health=" + source.CurrentHealth
                        + " played=" + source.QueryGameJournalCount(CardJournal.Played) + " enemyAvatar=" + f.Avatar(enemy).CurrentHealth
                        + " ownAvatar=" + f.Avatar(me).CurrentHealth + " friend=" + friend.CurrentHealth + " foe=" + foe.CurrentHealth);
                }
            });
            test("effect-summoned-wardcrafter-chooses-self", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    f.AutoAnswerTargets = false;
                    List<ServerCard> created = null;
                    f.Event(delegate {
                        GameZone.Position position = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[me].NetId,
                            GameZoneName.CreatureBoard, 0, CardInsertionType.Bottom);
                        created = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, config.FindCardById(-987994366), false, GameZoneType.Board, position, f.Sim.Players[me].NetId, 1, -1, null);
                    });
                    Require(created != null && created.Count == 1, "Native effect did not summon Wardcrafter.");
                    ServerCard source = created[0];
                    f.Until(delegate { return f.Sim._turnStateStack.Count > 1 && f.Sim._turnStateStack.Peek().CurrentCommandItem != null
                        && f.Sim._turnStateStack.Peek().CurrentSequence == null
                        && f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    CardCommandState choices = prompt.ValidCommands[0];
                    Require(choices.PlayTargets.Contains(source.NetUid.Id), "Effect-summoned Wardcrafter cannot choose herself.");
                    f.Sim.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play,
                        CommandUid = prompt.ListUid, ActorCardNetUid = choices.CardNetUid, TargetCardNetUid = source.NetUid.Id,
                        EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
                    Require(source.QueryAttribute(AttributeType.Ward).Value > 0 && source.QueryGameJournalCount(CardJournal.Played) == 0,
                        "Effect-summoned Wardcrafter did not gain Ward or was incorrectly counted as played.");
                }
            });
        }
        private static void ConditionalCase(GameConfiguration config, Action<string, Action> test, string label, int id, bool needsStrongCreature)
        {
            test("conditional-self-target-summon-" + label, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source = f.Create(id, me, GameZoneName.Hand);
                    int health = source.CurrentHealth;
                    f.AutoAnswerTargets = false; f.Mana(me, 20); f.Refresh(me); f.PlayCreatureLane(source, 0, me);
                    Require(source.CurrentGameZoneName == GameZoneName.CreatureBoard && source.CurrentHealth == health,
                        "Unsatisfied condition must not ask for a target or apply the effect.");
                }
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.CreateIn(needsStrongCreature ? 1427457192 : Crab, me, GameZoneName.CreatureBoard, 0);
                    if (!needsStrongCreature) f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = f.Create(id, me, GameZoneName.Hand);
                    int health = source.CurrentHealth, power = source.QueryAttribute(AttributeType.Attack).Value;
                    f.Mana(me, 20); f.Refresh(me);
                    EquipmentChoiceTests.ChooseAfterPlacement(f, source, source, friend, foe, me, true);
                    Require(source.CurrentHealth == health - (needsStrongCreature ? 3 : 2)
                        && source.QueryAttribute(AttributeType.Attack).Value == power - (needsStrongCreature ? 0 : 2)
                        && foe.CurrentHealth == 1, "Satisfied Summon condition did not apply to the chosen source.");
                }
            });
        }
        private static void Case(GameConfiguration config, Action<string, Action> test, string label, int id,
            AttributeType attribute, int delta, bool allowEnemy, bool additive, bool temporary)
        {
            test("self-target-summon-" + label, delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                    ServerCard source = f.Create(id, me, GameZoneName.Hand);
                    int before = attribute == AttributeType.None ? source.CurrentHealth : source.QueryAttribute(attribute).Value;
                    int cost = source.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Mana(me, 20); f.Refresh(me);
                    EquipmentChoiceTests.ChooseAfterPlacement(f, source, source, friend, foe, me, allowEnemy);
                    int after = attribute == AttributeType.None ? source.CurrentHealth : source.QueryAttribute(attribute).Value;
                    Require(source.CurrentGameZoneName == GameZoneName.CreatureBoard && source.QueryGameJournalCount(CardJournal.Played) == 1
                        && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 20 - cost
                        && (additive ? after == before + delta : after > 0) && friend.CurrentHealth == 1 && foe.CurrentHealth == 1,
                        "Summon did not apply its effect once to the chosen source; observed " + before + " -> " + after);
                    if (temporary) {
                        f.AutoAnswerTargets = true; f.EndTurn();
                        Require(source.QueryAttribute(attribute).Value == before, "Temporary Summon keyword did not expire at turn end.");
                    }
                }
            });
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
