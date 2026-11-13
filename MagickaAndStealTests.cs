using System;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Deck gauntlet 2026-11-08: Pure-Blood Elder (DoubleMaxManaGained) and Miraak (stealing an equipped creature) hit engine
    // guards. The game itself doubles max magicka gains on the avatar, and a stolen creature keeps its items.
    public static class MagickaAndStealTests
    {
        private const int Crab = 620729069;
        private const int PureBloodElder = -794652648;

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("pure-blood-elder-doubles-turn-start-max-magicka", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim;
                    int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard elder = f.Create(PureBloodElder, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    f.PlayCreatureLane(elder, 0, me);
                    ServerCard avatar = f.Avatar(me);
                    if (avatar.QueryAttribute(AttributeType.DoubleMaxManaGained).Value <= 0)
                        throw new InvalidOperationException("Pure-Blood Elder's aura did not reach its avatar.");
                    if (f.Avatar(enemy).QueryAttribute(AttributeType.DoubleMaxManaGained).Value != 0)
                        throw new InvalidOperationException("Pure-Blood Elder's aura reached the enemy avatar.");
                    int turns = s.Players[me].QueryAttribute(AttributeType.TurnCount).Value;
                    if (turns >= Simulation.MaxManaUnbuffed_) throw new InvalidOperationException("Fixture is past the natural magicka cap.");
                    int max = avatar.QueryAttribute(AttributeType.MaxMana).Value;
                    f.Event(delegate { DecisionRestoration.RestoreStartTurnMagicka(s, s._turnStateStack.Peek(), s.Players[me]); });
                    int now = avatar.QueryAttribute(AttributeType.MaxMana).Value;
                    if (now != max + 2 || s.Players[me].QueryAttribute(AttributeType.Mana).Value != now)
                        throw new InvalidOperationException("Turn-start gain with Pure-Blood Elder: max " + max + " -> " + now
                            + ", magicka " + s.Players[me].QueryAttribute(AttributeType.Mana).Value);
                }
            });

            test("stolen-creature-keeps-its-equipped-items", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim;
                    int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    int creatureId = Find(config, "Creature", new[] { "Fearless Northlander", "Torval Crook", "Dunmer Tyro" });
                    int itemId = Find(config, "Item", new[] { "Steel Sword", "Iron Sword", "Steel Dagger", "Iron Mace" });
                    ServerCard wielder = f.CreateIn(creatureId, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard item = f.CreateIn(itemId, enemy, GameZoneName.ItemBoard, 0);
                    f.Event(delegate { CardActions.Equip.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), item, wielder, 0); });
                    int power = wielder.QueryAttribute(AttributeType.Attack).Value;
                    if (item.ItemTarget != wielder.InstanceId) throw new InvalidOperationException("Fixture equip failed.");
                    GameZoneOperationResult moved = GameZoneOperationResult.Failed;
                    int end = f.LaneCount(me, 0);
                    f.Event(delegate {
                        moved = ServerCardCatalog.MoveCard(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                            new GameZoneId(s.Players[enemy].NetId, GameZoneType.Board), new GameZoneId(s.Players[me].NetId, GameZoneType.Board),
                            wielder.InstanceId, new GameZone.Position { LaneIndex = 0, CardIndex = end }, ServerCardCatalog.MoveOperationType.Untyped);
                    });
                    if (moved != GameZoneOperationResult.Succeeded) throw new InvalidOperationException("Steal move failed: " + moved);
                    if (wielder.OwnerNetId != s.Players[me].NetId || wielder.CurrentGameZoneName != GameZoneName.CreatureBoard)
                        throw new InvalidOperationException("The stolen creature is not on the thief's board.");
                    if (item.OwnerNetId != s.Players[me].NetId || item.CurrentGameZoneName != GameZoneName.ItemBoard || item.ItemTarget != wielder.InstanceId)
                        throw new InvalidOperationException("The item did not follow its stolen wielder: owner " + item.OwnerNetId + " zone " + item.CurrentGameZoneName);
                    if (wielder.QueryAttribute(AttributeType.Attack).Value != power)
                        throw new InvalidOperationException("The stolen creature lost its item bonus: " + power + " -> " + wielder.QueryAttribute(AttributeType.Attack).Value);
                }
            });
        }

        private static int Find(GameConfiguration config, string definition, string[] names)
        {
            foreach (string name in names)
                foreach (CardSet set in config.CardCollection)
                    foreach (Card c in set.Cards)
                        if (c != null && c.Name == name && c.Definition == definition) return c.ResourceId.Id;
            throw new InvalidOperationException("No " + definition + " found among the candidates.");
        }
    }
}
