using System;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class AttackTimingTests
    {
        private const int Crab = 620729069, Automaton = 1612844278, Guard = 550907952, FieryImp = 1193892832;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("ageless-automaton-guard-buff-before-combat", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Automaton), "Automaton not emitted: " + OriginalSpecRules.SkipReason(Automaton));
                    ServerCard automaton = f.Create(Automaton, me, GameZoneName.CreatureBoard);
                    ServerCard guard = f.Create(Guard, enemy, GameZoneName.CreatureBoard);
                    f.EndTurn(); f.EndTurn(); f.Attack(automaton, guard, me);
                    Require(guard.CurrentGameZoneType != GameZoneType.Board && f.Avatar(enemy).CurrentHealth == 29
                        && automaton.QueryAttribute(AttributeType.Attack).Value == 5 && automaton.QueryAttribute(AttributeType.Breakthrough).Value > 0,
                        "Guard attack must use 5 power and Breakthrough during combat; guard health=" + guard.CurrentHealth
                        + ", enemy health=" + f.Avatar(enemy).CurrentHealth + ", attacker power=" + automaton.QueryAttribute(AttributeType.Attack).Value);
                    f.EndTurn();
                    Require(automaton.QueryAttribute(AttributeType.Attack).Value == 2 && automaton.QueryAttribute(AttributeType.Breakthrough).Value == 0,
                        "Guard attack buffs must expire at end of turn.");
                }
            });
            test("ageless-automaton-other-target-does-not-buff", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard automaton = f.Create(Automaton, me, GameZoneName.CreatureBoard);
                    f.EndTurn(); f.EndTurn(); f.Attack(automaton, f.Avatar(enemy), me);
                    Require(automaton.QueryAttribute(AttributeType.Attack).Value == 2 && automaton.QueryAttribute(AttributeType.Breakthrough).Value == 0
                        && f.Avatar(enemy).CurrentHealth == 28, "Non-Guard attack incorrectly gained bonus.");
                }
            });
            test("attack-cancels-combat-when-trigger-destroys-defender", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard attacker = f.Create(Crab, me, GameZoneName.CreatureBoard);
                    ServerCard defender = f.Create(Guard, enemy, GameZoneName.CreatureBoard);
                    TriggeredEffect trigger = new TriggeredEffect { TriggerType = EffectTriggerType.WhenCardAttacks,
                        SortOrder = TriggerSortOrder.EndOfActiveQueue, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
                    trigger.TriggerCardRequirements.Add(new CardRequirements.EventRoleRequirement { LhsCard = CardRole.Inciter,
                        Conditional = new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self } });
                    CardEffectAction destroy = new CardEffectAction { Action = new CardActions.Destroy { Sacrifice = false } };
                    destroy.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.EventTarget });
                    Effect effect = new Effect(); effect.CardActions.Add(destroy); trigger.EffectGroup.Effects.Add(effect);
                    attacker.TriggeredEffects.Add(trigger);
                    try {
                        f.EndTurn(); f.EndTurn(); f.Attack(attacker, defender, me);
                        Require(defender.CurrentGameZoneType != GameZoneType.Board && attacker.CurrentHealth == 1
                            && attacker.QueryGameJournalCount(CardJournal.Attacked) == 0 && f.Avatar(enemy).CurrentHealth == 30,
                            "Combat continued after its defender was destroyed by a pre-combat trigger.");
                    } finally { attacker.TriggeredEffects.Remove(trigger); }
                }
            });
            test("attack-trigger-lethal-ends-game-before-combat-with-balanced-history", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    string step = "prepare";
                    try {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard enemyAvatar = f.Avatar(enemy);
                    ServerCard attacker = f.Create(FieryImp, me, GameZoneName.CreatureBoard);
                    ServerCard defender = f.Create(Guard, enemy, GameZoneName.CreatureBoard);
                    step = "turns"; f.EndTurn(); f.EndTurn();
                    step = "lower health";
                    f.Event(delegate { enemyAvatar.ModifyCurrentHealth(-28, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    step = "settle runes"; for (int i = 0; i < 100; i++) LifecycleRestoration.FixedUpdate(s);
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    step = "submit attack";
                    f.Refresh(me);
                    CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision decision = new Decision { Type = CcgRpc.DecisionType.Attack, CommandUid = list.ListUid,
                        ActorCardNetUid = attacker.NetUid.Id, TargetCardNetUid = defender.NetUid.Id };
                    Require(CommandRestoration.ValidateDecision(decision, list, s.CardCatalog, s.Players[me]), "Lethal attack command is invalid.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(decision);
                    step = "wait for results"; f.Until(delegate { return s._gameState.GamePhase == GamePhase.Results; });
                    step = "check result";
                    Require(s.WinnerId == s.Players[me].NetId && enemyAvatar.CurrentHealth <= 0
                        && defender.CurrentHealth == 4 && attacker.QueryGameJournalCount(CardJournal.Attacked) == 0,
                        "Lethal attack trigger failed to end the match before combat.");
                    step = "check replay"; CheckReplay(s);
                    } catch (Exception ex) { throw new InvalidOperationException("Lethal attack test at " + step + ": " + ex, ex); }
                }
            });
            test("lethal-card-play-ends-game-with-balanced-history", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard avatar = f.Avatar(enemy), spell = f.Create(1690856288, me, GameZoneName.Hand);
                    f.Event(delegate { avatar.ModifyCurrentHealth(-28, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    for (int i = 0; i < 100; i++) LifecycleRestoration.FixedUpdate(s);
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    f.Mana(me, 30); f.Refresh(me);
                    CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid,
                        ActorCardNetUid = spell.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id };
                    Require(CommandRestoration.ValidateDecision(decision, list, s.CardCatalog, s.Players[me]), "Lethal Ice Spike command is invalid.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(decision);
                    f.Until(delegate { return s._gameState.GamePhase == GamePhase.Results; });
                    Require(s.WinnerId == s.Players[me].NetId && avatar.CurrentHealth <= 0
                        && spell.QueryGameJournalCount(CardJournal.Played) == 1, "Lethal Ice Spike did not finish the match as a paid play.");
                    CheckReplay(s);
                }
            });
        }
        private static void CheckReplay(Simulation s)
        {
            foreach (PlayerServiceClient client in s.PlayerServiceClients)
            {
                HistoryQueue replay = s.HistoryLog.GenerateFullReplayForPlayer(client, HistoryLog.FullGameStateSendRule.Exclude);
                int depth = 0, gameEnds = 0;
                foreach (HistoryItem item in replay.Queue)
                {
                    if (HistoryUtilities.IsHeader(item.Type)) { depth++; if (item.Type == HistoryType.EventGameEnd) gameEnds++; }
                    if (HistoryUtilities.IsFooter(item.Type)) { depth--; Require(depth >= 0, "Match-end replay has an unmatched footer."); }
                }
                Require(depth == 0 && gameEnds == 1, "Match-end replay is unbalanced or lacks a game end: depth=" + depth + ", ends=" + gameEnds);
            }
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
