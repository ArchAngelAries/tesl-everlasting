using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Registration/draw only. Empty scenario wrappers and base avatars below are
    // explicit TEST fixtures; they do not restore Chapter1 health/runes/magicka.
    public static class OriginalScenarioDeckTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("original-chapter1-ordered-zone-decks-registration-and-first-draw", delegate {
                Scenario original = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.Id == 1595477420; });
                Require(original != null && original.ResourceId.UniqueName == "TFH_Act1_Chapter1"
                    && original.StartingPlayer == SinglePlayerFirstToAct.AiPlayer && original.GameProperties.MulliganDisabled
                    && original.DeckOverrides.Count == 0 && original.GameZoneOverrides.Count == 2,
                    "Exact original Chapter1 setup differs.");
                Require(original.GameProperties.GameStartActions.Count == 2, "Chapter1 start action count differs.");
                foreach (PlayerEffectAction action in original.GameProperties.GameStartActions)
                    Require(!(action.Action is PlayerActions.ShuffleCardsInZone) && !(action.Action is PlayerActions.DrawCards),
                        "Original tutorial unexpectedly shuffles or gives ordinary opening draw.");
                List<AvatarOverride> avatars = original.AvatarOverrides;
                List<PlayerOverride> players = original.PlayerOverrides;
                Scenario fixture = new Scenario {
                    ResourceId = original.ResourceId, GameProperties = original.GameProperties,
                    StartingPlayer = original.StartingPlayer, IsSinglePlayer = original.IsSinglePlayer,
                    GameZoneOverrides = original.GameZoneOverrides, DeckOverrides = original.DeckOverrides,
                    // These lists belong to fixture only. Native source instruction graph
                    // remains unchanged; no full Start or instruction execution occurs.
                    AvatarOverrides = new List<AvatarOverride>(), PlayerOverrides = new List<PlayerOverride>(),
                    TriggeredEffects = original.TriggeredEffects
                };
                using (IDisposable ordinaryFixture = OrdinaryMatchRules.Prepare(config))
                {
                    Simulation sim = new Simulation();
                    sim.RandomGenerator = new RandomIntegerGenerator(12345);
                    sim.CardCatalog = new ServerCardCatalog(sim.RandomGenerator);
                    sim.CardCatalog.ExcludeCardIds = new List<int>();
                    sim.HistoryLog = new HistoryLog();
                    sim._gameProperties = original.GameProperties;
                    sim.PlayerServiceClients = new PlayerServiceClient[2];
                    int[] ai = {1714445403,-1734732697,1714445403,-1866183707,-675856486,1714445403,-675856486,-1866183707,1714445403,1714445403};
                    int[] human = {494443404,1128875378,-741858815,844499977,1717577005,1449346166,438381503,373560332,-741858815,1717577005};
                    Register(sim, fixture, PlayerType.AiOpponent, 1);
                    Register(sim, fixture, PlayerType.Normal, 2);
                    AssertDeck(sim, sim.Players[0], ai);
                    AssertDeck(sim, sim.Players[1], human);
                    foreach (PlayerState player in sim.Players)
                    {
                        GameZone board = sim.CardCatalog.FindGameZone(player.NetId, GameZoneName.CreatureBoard);
                        Require(board.LaneList.Count == 2 && board.LaneList[0].GetCapacity() == 4 && board.LaneList[1].GetCapacity() == 4
                            && board.LaneList[0].GetCardLaneType() == CardLaneType.Default && board.LaneList[1].GetCardLaneType() == CardLaneType.Default,
                            "Original tutorial lane override was lost.");
                        Require(sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == 0, "Registration invented an opening hand.");
                        player.SetAttribute(AttributeType.HandSize,10); // TEST fixture for direct native draw only.
                    }
                    sim.HistoryLog.StartEvent(HistoryType.EventGameStart, sim.Players[0].NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
                    ServerEffectUtilities.TriggerGameState state = new ServerEffectUtilities.TriggerGameState(sim.HistoryLog, sim.RandomGenerator,
                        sim.CardCatalog, sim.Players, new ServerEffectUtilities.TriggerQueues(), new List<ActiveQuest>(),
                        new List<CustomDisplayCard>(), UniqueResourceId.GetInvalid().Id); // No tutorial instruction triggers in this fixture.
                    AssertDraw(state, sim.Players[0], ai[0]);
                    AssertDraw(state, sim.Players[1], human[0]);
                    Require(sim.CardCatalog.FindCardCountInDeck(sim.Players[0].NetId) == 9 && sim.CardCatalog.FindCardCountInDeck(sim.Players[1].NetId) == 9,
                        "Native first draw changed unexpected deck counts.");
                }
                Require(Object.ReferenceEquals(original.AvatarOverrides, avatars) && Object.ReferenceEquals(original.PlayerOverrides, players),
                    "Test replaced original missing scenario wrappers.");
            });
        }
        private static void Register(Simulation sim, Scenario fixture, PlayerType type, int uid)
        {
            PlayerDescription description = new PlayerDescription { PlayerName = "TEST ordered scenario", AvatarCardId = OrdinaryMatchRules.BaseAvatarId };
            // Deliberately unrelated fallback recipe. Original scenario Deck zone
            // override must wholly replace this and preserve its exact native order.
            for (int i = 0; i < 12; i++) { description.Deck.Add(157797006); description.Premium.Add(false); }
            LifecycleRestoration.RegisterPlayer(sim, fixture, new PlayerUniqueId(uid), null, null, "test-secret", "",
                description, type, null, 12, 0, false, false, null, null, 0, null, null);
        }
        private static void AssertDeck(Simulation sim, PlayerState player, int[] expected)
        {
            GameZone deck = sim.CardCatalog.FindGameZone(player.NetId, GameZoneName.Deck);
            Require(sim.CardCatalog.FindCardCountInDeck(player.NetId) == expected.Length && player.StartingDeck.Count == expected.Length,
                "Fallback custom recipe leaked into native scenario deck.");
            for (int i = 0; i < expected.Length; i++)
                Require(sim.CardCatalog.FindDynamicCard(deck.LaneList[0].GetCard(i)).CardId == expected[i] && player.StartingDeck[i] == expected[i],
                    "Original ordered scenario recipe changed at slot " + i + ".");
        }
        private static void AssertDraw(ServerEffectUtilities.TriggerGameState state, PlayerState player, int expected)
        {
            List<ServerCard> drawn = PlayerActions.DrawCards.StaticExecute(state, player, 1, PlayerActions.DrawCards.DrawDeck.Self,
                ServerCardCatalog.MoveOperationType.DefaultDraw, false, false, -1);
            Require(drawn.Count == 1 && drawn[0].CardId == expected && drawn[0].CurrentGameZoneName == GameZoneName.Hand,
                "Native draw did not consume the exact original first card.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
