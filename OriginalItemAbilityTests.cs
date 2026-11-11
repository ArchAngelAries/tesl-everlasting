using System;
using SPTESL;

namespace OfflineEngine
{
    // Installed-text item reconstructions: verify effects through paid equips, combat and real turns.
    public static class OriginalItemAbilityTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("item-summon-lane-count-real-paid-equip", delegate {
                foreach (int id in new int[] { 879461802, 1655938757 })
                foreach (int enemies in new int[] { 0, 2 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard wielder = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 1);
                    for (int n = 0; n < enemies; n++) f.CreateIn(Mudcrab, 1 - me, GameZoneName.CreatureBoard, 1);
                    f.CreateIn(Mudcrab, 1 - me, GameZoneName.CreatureBoard, 0); // must not count the other lane
                    ServerCard item = f.Create(id, me, GameZoneName.Hand);
                    int attack = Stat(wielder, AttributeType.Attack) + Stat(item, AttributeType.Attack);
                    int health = Stat(wielder, AttributeType.MaxHealth) + Stat(item, AttributeType.MaxHealth);
                    Equip(f, item, wielder, me);
                    Require(Stat(wielder, AttributeType.Attack) == attack + enemies && Stat(wielder, AttributeType.MaxHealth) == health + enemies,
                        "Lane-count item did not grant exactly one buff per enemy in wielder lane: " + id);
                }
            });
            test("item-temporary-drain-and-slay-real-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard wielder = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 0);
                    ServerCard victim = f.CreateIn(Mudcrab, 1 - me, GameZoneName.CreatureBoard, 0);
                    f.EndTurn(); f.EndTurn();
                    Equip(f, f.Create(-1476116893, me, GameZoneName.Hand), wielder, me);
                    Require(Stat(wielder, AttributeType.Drain) > 0, "Snake Tooth did not grant Drain to wielder.");
                    Equip(f, f.Create(529495682, me, GameZoneName.Hand), wielder, me);
                    int attack = Stat(wielder, AttributeType.Attack), health = Stat(wielder, AttributeType.MaxHealth);
                    f.Attack(wielder, victim, me);
                    Require(Stat(wielder, AttributeType.Attack) == attack + 1 && Stat(wielder, AttributeType.MaxHealth) == health + 1,
                        "Daedric Crescent Slay did not grant exactly +1/+1 to wielder.");
                    f.EndTurn();
                    Require(Stat(wielder, AttributeType.Drain) == 0, "Temporary item Drain survived end of turn.");
                }
            });
            test("item-conditional-dragonplate-real-paid-equip", delegate {
                int dragon = 0;
                foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                    if (c.HasSubtype(CardSubtype.Dragon) && c.HasSubtype(CardSubtype.Creature)) { dragon = c.ResourceId.Id; break; }
                Require(dragon != 0, "No original Dragon for discard fixture.");
                foreach (bool present in new bool[] { false, true })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard wielder = f.Create(Leviathan, me, GameZoneName.CreatureBoard);
                    f.Create(dragon, 1 - me, GameZoneName.Cemetery); // opponent's Dragon never qualifies
                    if (present) f.Create(dragon, me, GameZoneName.Cemetery);
                    int attack = Stat(wielder, AttributeType.Attack), health = Stat(wielder, AttributeType.MaxHealth);
                    Equip(f, f.Create(-1450677719, me, GameZoneName.Hand), wielder, me);
                    int bonus = present ? 4 : 2;
                    Require(Stat(wielder, AttributeType.Attack) == attack + bonus && Stat(wielder, AttributeType.MaxHealth) == health + bonus,
                        "Dragonplate bonus differs for own discard Dragon=" + present);
                }
            });
        }
        private static void Equip(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard item, ServerCard wielder, int me)
        {
            f.Mana(me, 30); f.Refresh(me); int cost = Stat(item, AttributeType.ManaCost);
            f.PlayEquip(item, wielder, me);
            Require(item.ItemTarget == wielder.InstanceId && item.CurrentGameZoneName == GameZoneName.ItemBoard,
                "Item did not equip to intended wielder.");
            Require(f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 30 - cost, "Equip payment differs.");
        }
        private static int Stat(ServerCard c, AttributeType attribute) { return c.QueryAttribute(attribute).Value; }
        private static void Require(bool yes, string message) { if (!yes) throw new InvalidOperationException(message); }
    }
}
