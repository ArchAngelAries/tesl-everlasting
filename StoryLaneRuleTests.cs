using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Story lane reconstructions (StoryLaneRules) through native lane-trigger evaluation:
    // each test sets the fixture's lane types natively, fires the native event and drains triggers.
    public static class StoryLaneRuleTests
    {
        private const int Mudcrab = 620729069;
        private const int WindyScenario = 1569860756; // TFH_Act2_Chapter9 (installed, Windy lanes)

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            Lane(config, test, "story-lane-torment-damages-summoned-creature", CardLaneType.Torment, delegate(EngineTests.Fixture f) {
                ServerCard crab = Enter(f, f.One, 0);
                Require(crab.CurrentHealth == crab.QueryAttribute(AttributeType.MaxHealth).Value - 1, "Torment did not deal 1 damage."); });
            Lane(config, test, "story-lane-armor-doubles-health", CardLaneType.Armor, delegate(EngineTests.Fixture f) {
                int before = GameManager.Instance.GetCard(Mudcrab).GetIntegerAttribute(AttributeType.MaxHealth);
                ServerCard crab = Enter(f, f.One, 0);
                Require(crab.QueryAttribute(AttributeType.MaxHealth).Value == before * 2, "Armor did not double health."); });
            Lane(config, test, "story-lane-order-sets-stats-to-cost", CardLaneType.Order, delegate(EngineTests.Fixture f) {
                ServerCard crab = Enter(f, f.One, 0);
                int cost = crab.QueryAttribute(AttributeType.ManaCost).Value;
                Require(crab.QueryAttribute(AttributeType.Attack).Value == cost && crab.QueryAttribute(AttributeType.MaxHealth).Value == Math.Max(1, cost), "Order stats differ from cost."); });
            Lane(config, test, "story-lane-flanking-buffs-other-lane", CardLaneType.Flanking, delegate(EngineTests.Fixture f) {
                ServerCard other = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 1, f.One);
                int attack = other.QueryAttribute(AttributeType.Attack).Value;
                Enter(f, f.One, 0);
                Require(other.QueryAttribute(AttributeType.Attack).Value == attack + 1, "Flanking did not give +1/+0 to the other lane."); });
            Lane(config, test, "story-lane-venom-aura-grants-lethal-in-lane-only", CardLaneType.Venom, delegate(EngineTests.Fixture f) {
                ServerCard here = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.One);
                ServerCard there = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 1, f.One);
                OfflineAuraRestoration.Apply(f.State);
                Require(here.QueryAttribute(AttributeType.Deathtouch).Value > 0, "Venom lane creature lacks Lethal.");
                Require(there.QueryAttribute(AttributeType.Deathtouch).Value == 0 || f.Catalog.QueryCreatureBoardLaneType(f.One.NetId, 1) == CardLaneType.Venom, "Venom leaked to another lane."); });
            Lane(config, test, "story-lane-graveyard-summons-rotting-draugr", CardLaneType.Graveyard, delegate(EngineTests.Fixture f) {
                ServerCard crab = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.One);
                int before = f.Catalog.GatherCardsInZone(delegate(ServerCard x) { return x.CurrentLaneIndex == 0; }, f.One.NetId, GameZoneName.CreatureBoard).Count;
                ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardDies, crab, null, f.Players));
                f.DrainTriggers();
                int after = f.Catalog.GatherCardsInZone(delegate(ServerCard x) { return x.CurrentLaneIndex == 0 && GameManager.Instance.GetCard(x.CardId).Name == "Rotting Draugr"; }, f.One.NetId, GameZoneName.CreatureBoard).Count;
                Require(before >= 1 && after == 1, "Graveyard did not summon one Rotting Draugr."); });
            Lane(config, test, "story-lane-windy-moves-opposing-creature-at-turn-end", CardLaneType.Windy, delegate(EngineTests.Fixture f) {
                // Fixture lanes may both become Windy, so the creature can move and move back: count moves.
                ServerCard crab = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.Two);
                ServerCard own = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.One);
                int moves = StoryLaneRules.WindyMoves;
                ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnEnds, f.One));
                f.DrainTriggers();
                Require(StoryLaneRules.WindyMoves > moves, "Windy did not move the opposing creature.");
                Require(own.CurrentLaneIndex == 0, "Windy moved the ending player's creature."); });
            Lane(config, test, "story-lane-smelting-destroys-own-creatures-and-draws", CardLaneType.Smelting, delegate(EngineTests.Fixture f) {
                ServerCard a = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.One);
                ServerCard b = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.One);
                ServerCard theirs = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.Two);
                f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One); f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One);
                int hand = f.Catalog.GatherCardsInZone(delegate(ServerCard x) { return true; }, f.One.NetId, GameZoneName.Hand).Count;
                ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, f.One));
                f.DrainTriggers();
                int onBoard = f.Catalog.GatherCardsInZone(delegate(ServerCard x) { return x.CurrentLaneIndex == 0 && !x.IsDying(); }, f.One.NetId, GameZoneName.CreatureBoard).Count;
                int drawn = f.Catalog.GatherCardsInZone(delegate(ServerCard x) { return true; }, f.One.NetId, GameZoneName.Hand).Count - hand;
                Require(onBoard == 0, "Smelting left the starting player's creatures in the lane.");
                Require(drawn == 2, "Smelting drew " + drawn + " cards instead of one per destroyed creature.");
                Require(theirs.CurrentGameZoneName == GameZoneName.CreatureBoard && !theirs.IsDying(), "Smelting destroyed the other player's creature."); });
            Lane(config, test, "story-lane-jail-refuses-creature-play-lane", CardLaneType.Jail, delegate(EngineTests.Fixture f) {
                ServerCard crab = f.CreateId(Mudcrab, GameZoneName.Hand, 0, f.One);
                CardCommandState state = new CardCommandState { ValidBoardLanes = 3, ValidCardCommandMask = (int)CardCommand.PlayToPosition };
                StoryLaneRules.RestrictPlayLanes(state, crab, f.One, f.Catalog);
                Require((state.ValidBoardLanes & 1) == 0, "Jail lane remained playable."); });
            Lane(config, test, "story-lane-library-discounts-hand-actions", CardLaneType.Library, delegate(EngineTests.Fixture f) {
                f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 0, f.One);
                ServerCard action = null;
                foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                    if (action == null && c.Definition == "Action" && RuleImporter.IsSupported(c.ResourceId.Id) && c.GetIntegerAttribute(AttributeType.ManaCost) >= 2)
                        action = f.CreateId(c.ResourceId.Id, GameZoneName.Hand, 0, f.One);
                Require(action != null, "No reviewed action for Library test.");
                int before = action.QueryAttribute(AttributeType.ManaCost).Value;
                OfflineAuraRestoration.Apply(f.State);
                Require(action.QueryAttribute(AttributeType.ManaCost).Value == before - 1, "Library discount not applied."); });
        }

        private static ServerCard Enter(EngineTests.Fixture f, PlayerState owner, int lane)
        {
            ServerCard card = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, lane, owner);
            ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardEntersBoard, card, f.Players));
            f.DrainTriggers();
            return card;
        }

        private static void Lane(GameConfiguration config, Action<string, Action> test, string name, CardLaneType type, Action<EngineTests.Fixture> body)
        {
            test(name, delegate {
                using (StoryScenarioRules.Prepare(config, WindyScenario))
                {
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    foreach (PlayerState p in f.Players)
                        PlayerActions.ChangeLaneType.StaticExecute(f.State.Log, f.Catalog, p, f.Catalog.QueryCreatureBoardLaneType(p.NetId, 0), type);
                    Require(f.Catalog.QueryCreatureBoardLaneType(f.One.NetId, 0) == type, "Fixture lane type was not changed to " + type);
                    body(f);
                }
            });
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
