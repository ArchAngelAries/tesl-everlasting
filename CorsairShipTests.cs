using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    public static class CorsairShipTests
    {
        private const int Ship = -949205489, Corsair = -733290941, Dagger = 939372786, Mudcrab = 620729069;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("corsair-ship-summon-and-equip-friendly-creatures", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Require(OriginalSpecRules.HasRestoredRules(Ship), "Corsair Ship spec not emitted: " + OriginalSpecRules.SkipReason(Ship));
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard existing = f.Create(Mudcrab, me, GameZoneName.CreatureBoard);
                    ServerCard ship = f.Create(Ship, me, GameZoneName.Hand);
                    f.Mana(me, 20); f.Refresh(me); f.Play(ship, null, me);
                    List<ServerCard> corsairs = Cards(f, me, GameZoneName.CreatureBoard, Corsair);
                    Require(ship.CurrentGameZoneName == GameZoneName.SupportBoard && ship.QueryGameJournalCount(CardJournal.Played) == 1
                        && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 16 && corsairs.Count == 1,
                        "Ship did not perform one four-magicka support play and summon one Corsair.");
                    CheckDagger(f, corsairs[0], me, 2);
                    Require(existing.QueryAttribute(AttributeType.Attack).Value == 2 && Items(f, existing).Count == 0,
                        "Ship equipped an existing creature rather than its summoned Corsair.");
                    ServerCard later = f.Create(Mudcrab, me, GameZoneName.Hand);
                    f.Refresh(me); f.PlayCreatureLane(later, 1, me);
                    CheckDagger(f, later, me, 3);
                    List<ServerCard> enemies = null;
                    f.Event(delegate {
                        GameZone.Position position = f.Sim.CardCatalog.FindInsertionPosition(f.Sim.Players[enemy].NetId,
                            GameZoneName.CreatureBoard, 0, CardInsertionType.Bottom);
                        enemies = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                            null, config.FindCardById(Mudcrab), false, GameZoneType.Board, position, f.Sim.Players[enemy].NetId, 1, -1, null);
                    });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
                    Require(enemies != null && enemies.Count == 1 && enemies[0].QueryAttribute(AttributeType.Attack).Value == 2
                        && Items(f, enemies[0]).Count == 0 && Cards(f, me, GameZoneName.ItemBoard, Dagger).Count == 2,
                        "Enemy summon or an earlier creature received an extra Dagger.");
                }
            });
        }
        private static List<ServerCard> Cards(OriginalTargetedSummonTests.LifecycleFixture f, int owner, GameZoneName zone, int id)
        { return f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == id; }, f.Sim.Players[owner].NetId, zone); }
        private static List<ServerCard> Items(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard c)
        { return f.Sim.CardCatalog.GatherCardList(delegate(ServerCard x) { return x.HasSubtype(CardSubtype.Item) && x.ItemTarget == c.InstanceId; }); }
        private static void CheckDagger(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard target, int owner, int power)
        {
            List<ServerCard> items = Items(f, target);
            Require(items.Count == 1 && items[0].CardId == Dagger && items[0].OwnerNetId == f.Sim.Players[owner].NetId
                && target.QueryAttribute(AttributeType.Attack).Value == power,
                "Summoned friendly creature must equip one original Steel Dagger and gain exactly one power.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
