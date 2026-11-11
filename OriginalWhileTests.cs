using System;
using SPTESL;

namespace OfflineEngine
{
    // Conditional continuous effects reconstructed from installed text using native Aura requirements.
    // Exercise removal as well as activation; smoke alone cannot prove a while condition.
    public static class OriginalWhileTests
    {
        private const int Mudcrab = 620729069;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("while-turn-stats-keyword-and-silence-real-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard corprus = Play(f, -1370043641, me), guerrilla = Play(f, -1413193108, me);
                    int power = Stat(corprus, AttributeType.Attack);
                    Require(Stat(guerrilla, AttributeType.Deathtouch) == 0, "Opponent-turn Lethal active on own turn.");
                    f.EndTurn();
                    Require(Stat(corprus, AttributeType.Attack) == power - 5 && Stat(guerrilla, AttributeType.Deathtouch) > 0,
                        "Turn auras failed to switch on opponent turn.");
                    f.EndTurn();
                    Require(Stat(corprus, AttributeType.Attack) == power && Stat(guerrilla, AttributeType.Deathtouch) == 0,
                        "Turn auras failed to switch back.");
                    f.Event(delegate { corprus.Silence(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), corprus); });
                    Settle(f);
                    Require(Stat(corprus, AttributeType.Attack) == power - 5, "Silence retained conditional stat aura.");
                }
            });
            test("while-max-magicka-stats-and-guard-threshold", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard root = Play(f, 287504025, me), avatar = f.Avatar(me);
                    int attack = Stat(root, AttributeType.Attack), health = Stat(root, AttributeType.MaxHealth);
                    Require(Stat(root, AttributeType.Taunt) == 0, "Guard active below seven max magicka.");
                    int delta = 7 - Stat(avatar, AttributeType.MaxMana);
                    f.Event(delegate { ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek());
                        avatar.AddMaxManaBuff(gs, delta); avatar.RecalculateEnchantmentsWithTriggers(gs, null); });
                    Settle(f);
                    Require(Stat(root, AttributeType.Attack) == attack + 2 && Stat(root, AttributeType.MaxHealth) == health + 2 && Stat(root, AttributeType.Taunt) > 0,
                        "Seven max magicka did not grant both stats and Guard.");
                    Settle(f);
                    Require(Stat(root, AttributeType.Attack) == attack + 2, "Aura duplicated during repeated recompute.");
                    f.Event(delegate { ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek());
                        avatar.AddMaxManaBuff(gs, -1); avatar.RecalculateEnchantmentsWithTriggers(gs, null); });
                    Settle(f);
                    Require(Stat(root, AttributeType.Attack) == attack && Stat(root, AttributeType.MaxHealth) == health && Stat(root, AttributeType.Taunt) == 0,
                        "Dropping below threshold retained aura.");
                }
            });
            test("while-avatar-current-health-threshold", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard dragon = Play(f, -1366891703, me), avatar = f.Avatar(me);
                    f.Event(delegate { avatar.ModifyCurrentHealth(30 - avatar.CurrentHealth, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                    Settle(f); int attack = Stat(dragon, AttributeType.Attack), health = Stat(dragon, AttributeType.MaxHealth);
                    f.Event(delegate { avatar.ModifyCurrentHealth(-1, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                    Settle(f);
                    Require(Stat(dragon, AttributeType.Attack) == attack - 5 && Stat(dragon, AttributeType.MaxHealth) == health - 5,
                        "Current health dropping to 29 did not remove +5/+5.");
                    f.Event(delegate { avatar.ModifyCurrentHealth(1, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                    Settle(f);
                    Require(Stat(dragon, AttributeType.Attack) == attack && Stat(dragon, AttributeType.MaxHealth) == health, "Healing to 30 did not restore aura.");
                }
            });
            test("while-enemy-lane-count-enters-and-leaves", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard cutthroat = Play(f, 1099519659, me);
                    int power = Stat(cutthroat, AttributeType.Attack), lane = cutthroat.CurrentLaneIndex;
                    f.CreateIn(Mudcrab, 1 - me, GameZoneName.CreatureBoard, 1 - lane); Settle(f);
                    Require(Stat(cutthroat, AttributeType.Attack) == power, "Other lane incorrectly removes bonus.");
                    ServerCard enemy = f.CreateIn(Mudcrab, 1 - me, GameZoneName.CreatureBoard, lane); Settle(f);
                    Require(Stat(cutthroat, AttributeType.Attack) == power - 2, "Same-lane enemy did not remove bonus.");
                    f.Event(delegate { enemy.ModifyCurrentHealth(-enemy.CurrentHealth, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                    Settle(f);
                    Require(Stat(cutthroat, AttributeType.Attack) == power, "Last same-lane enemy death did not restore bonus.");
                }
            });
        }
        private static ServerCard Play(OriginalTargetedSummonTests.LifecycleFixture f, int id, int me)
        {
            Require(OriginalSpecRules.HasRestoredRules(id), "While card not emitted: " + id + " " + OriginalSpecRules.SkipReason(id));
            ServerCard c = f.Create(id, me, GameZoneName.Hand); f.Mana(me, 30); f.Refresh(me); f.Play(c, null, me); return c;
        }
        private static void Settle(OriginalTargetedSummonTests.LifecycleFixture f)
        { for (int i = 0; i < 8; i++) LifecycleRestoration.FixedUpdate(f.Sim); f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); }); }
        private static int Stat(ServerCard c, AttributeType a) { return c.QueryAttribute(a).Value; }
        private static void Require(bool yes, string message) { if (!yes) throw new InvalidOperationException(message); }
    }
}
