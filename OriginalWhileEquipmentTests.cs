using System;
using SPTESL;

namespace OfflineEngine
{
    // Scratch proposal: parent integrates and runs. Installed-text reconstruction,
    // exercising native equip/remove, Ward damage and dynamic count lifecycles.
    public static class OriginalWhileEquipmentTests
    {
        private const int Mudcrab = 620729069, IronSword = -1808851552;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("while-equipment-paid-equip-detach-and-silence", delegate {
                foreach (int id in new int[] {1140811777, 1200203004, -801177855})
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard c = Play(f, id, me), item = f.Create(IronSword, me, GameZoneName.Hand);
                    bool horse = id == -801177855;
                    AttributeType keyword = horse ? AttributeType.Breakthrough : AttributeType.Taunt;
                    int a = Stat(c, AttributeType.Attack), h = Stat(c, AttributeType.MaxHealth);
                    int ia = Stat(item, AttributeType.Attack), ih = Stat(item, AttributeType.MaxHealth);
                    Require(Stat(c, keyword) == 0, "Unequipped Rihad keyword active.");
                    Equip(f, item, c, me); Settle(f);
                    Require(Stat(c, AttributeType.Attack) == a + ia + (horse ? 3 : 0)
                        && Stat(c, AttributeType.MaxHealth) == h + ih + (horse ? 0 : 3)
                        && Stat(c, keyword) > 0, "Equip failed to grant conditional stats/keyword: " + id);
                    Settle(f);
                    Require(Stat(c, AttributeType.Attack) == a + ia + (horse ? 3 : 0), "Repeated settle duplicated equipment bonus.");
                    f.Event(delegate { item.RemoveItemTarget(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), c); });
                    Settle(f);
                    Require(Stat(c, AttributeType.Attack) == a && Stat(c, AttributeType.MaxHealth) == h && Stat(c, keyword) == 0,
                        "Detach retained equipment aura: " + id);
                    Equip(f, f.Create(IronSword, me, GameZoneName.Hand), c, me); Settle(f);
                    f.Event(delegate { c.Silence(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), c); });
                    Settle(f);
                    Require(Stat(c, AttributeType.Attack) == a && Stat(c, AttributeType.MaxHealth) == h && Stat(c, keyword) == 0,
                        "Silence retained equipment stats/keyword: " + id);
                }
            });
            test("while-prankster-native-ward-damage-break-cover-and-regain", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard c = Play(f, 161387582, me);
                    ServerCard enemy = f.CreateIn(Mudcrab, 1-me, GameZoneName.CreatureBoard, 0);
                    int h = c.CurrentHealth;
                    Require(Stat(c, AttributeType.Ward)>0 && Stat(c, AttributeType.Taunt)>0, "Ward did not enable Guard.");
                    f.Event(delegate { CardActions.Damage.StaticExecuteSingleTarget(
                        DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), enemy, c, 1, false, null); });
                    Settle(f);
                    Require(c.CurrentHealth == h && Stat(c, AttributeType.Ward)==0 && Stat(c, AttributeType.Taunt)==0
                        && Stat(c, AttributeType.Cover)>0, "Ward damage did not prevent damage/remove Guard/grant Cover.");
                    f.Event(delegate { c.AddWard(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), c); });
                    Settle(f);
                    Require(Stat(c, AttributeType.Ward)>0 && Stat(c, AttributeType.Taunt)>0, "Regained Ward failed to restore Guard.");
                    f.Event(delegate { c.Silence(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), c); });
                    Settle(f);
                    Require(Stat(c, AttributeType.Ward)==0 && Stat(c, AttributeType.Taunt)==0 && Stat(c, AttributeType.Cover)==0,
                        "Silence retained Prankster keyword or fired false Ward-break Cover.");
                }
            });
            test("while-mammoth-per-count-entry-death-keyword-loss-no-duplicates", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard c = Play(f, -2040126054, me);
                    Require(Stat(c, AttributeType.Attack)==4, "Mammoth counted itself.");
                    f.CreateIn(-2040126054, 1-me, GameZoneName.CreatureBoard, 1); Settle(f);
                    Require(Stat(c, AttributeType.Attack)==4, "Mammoth counted enemy Breakthrough.");
                    ServerCard one = f.CreateIn(-2040126054, me, GameZoneName.CreatureBoard, 1);
                    ServerCard two = f.CreateIn(-2040126054, me, GameZoneName.CreatureBoard, 0); Settle(f);
                    Require(Stat(c, AttributeType.Attack)==8, "Mammoth failed to count other friendlies across lanes.");
                    Settle(f); Settle(f);
                    Require(Stat(c, AttributeType.Attack)==8 && Stat(one, AttributeType.Attack)==8, "Count aura duplicated during settle.");
                    f.Event(delegate { one.Silence(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), one); }); Settle(f);
                    Require(Stat(c, AttributeType.Attack)==6, "Losing a counted keyword did not reduce aura.");
                    f.Event(delegate { two.ModifyCurrentHealth(-two.CurrentHealth, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); }); Settle(f);
                    Require(Stat(c, AttributeType.Attack)==4, "Counted creature death did not remove final stack.");
                }
            });
        }
        private static ServerCard Play(OriginalTargetedSummonTests.LifecycleFixture f, int id, int me)
        {
            Require(OriginalSpecRules.HasRestoredRules(id), "Card not emitted: " + id);
            ServerCard c = f.Create(id, me, GameZoneName.Hand); f.Mana(me, 30); f.Refresh(me); f.Play(c, null, me); Settle(f); return c;
        }
        private static void Equip(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard item, ServerCard c, int me)
        {
            f.Mana(me,30); f.Refresh(me); int cost = Stat(item, AttributeType.ManaCost); f.PlayEquip(item,c,me);
            Require(item.ItemTarget == c.InstanceId && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value==30-cost,"Paid equip failed.");
        }
        private static void Settle(OriginalTargetedSummonTests.LifecycleFixture f)
        { for (int i=0;i<16;i++) LifecycleRestoration.FixedUpdate(f.Sim); f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); }); }
        private static int Stat(ServerCard c, AttributeType a) { return c.QueryAttribute(a).Value; }
        private static void Require(bool yes, string message) { if (!yes) throw new InvalidOperationException(message); }
    }
}
