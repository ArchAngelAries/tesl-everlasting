using System;
using System.Collections.Generic;
using System.Reflection;
using CcgRpc;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Reuses the shipped model, history protocol, effects, and card rules. This class
    // is only installed by the explicit offline entry point, against a publicized
    // in-memory game assembly. No remote service credentials are used here.
    public static class LifecycleRestoration
    {
        public static void Install(Harmony harmony)
        {
            foreach (string name in new string[] { "Start", "RegisterPlayer", "FixedUpdate", "ExecuteGamePhase", "ProcessEndTurn" })
                harmony.Patch(AccessTools.Method(typeof(Simulation), name, null, null),
                    new HarmonyMethod(typeof(LifecycleRestoration).GetMethod(name + "Prefix", BindingFlags.Static | BindingFlags.NonPublic)), null, null, null, null);
        }

        private static bool StartPrefix(Simulation __instance, TcpNetworkServer tcpServer, MatchConfiguration.StartInfo startInfo)
        { Start(__instance, tcpServer, startInfo); return false; }
        private static bool FixedUpdatePrefix(Simulation __instance)
        { FixedUpdate(__instance); return false; }
        private static bool ExecuteGamePhasePrefix(Simulation __instance)
        { ExecuteGamePhase(__instance); return false; }
        private static bool ProcessEndTurnPrefix(Simulation __instance, TurnState turnState, ref Simulation.SequenceStatus __result)
        { __result = ProcessEndTurn(__instance, turnState); return false; }
        private static bool RegisterPlayerPrefix(Simulation __instance, Scenario scenario, PlayerUniqueId playerUid,
            byte[] encryptionKey, byte[] observerEncryptionKey, string secretKey, string spectatorKey,
            PlayerDescription playerDescription, PlayerType playerType, RetryInfo retryInfo, int rank, int legendRank,
            bool allowTP, bool arenaBonusMatch, GameZoneOverride startInfoOptionalGameZoneOverride,
            List<MatchConfiguration.StartInfo.ExtraZoneCards> extraZoneCards, int playerStartingHealthOverride,
            List<int> matchStatValues, List<int> completedEvents)
        {
            RegisterPlayer(__instance, scenario, playerUid, encryptionKey, observerEncryptionKey, secretKey, spectatorKey,
                playerDescription, playerType, retryInfo, rank, legendRank, allowTP, arenaBonusMatch,
                startInfoOptionalGameZoneOverride, extraZoneCards, playerStartingHealthOverride, matchStatValues, completedEvents);
            return false;
        }

        // Set by the caller for scenarios that must open with the human (puzzles: "Win this turn",
        // whose turn-end rule makes the human lose at every turn end), whose installed first player is Random.
        public static bool HumanFirst, AiFirst;

        public static void Start(Simulation sim, TcpNetworkServer server, MatchConfiguration.StartInfo info)
        {
            if (info == null || info.Users == null || info.Users.Length != 2)
                throw new ArgumentException("Offline matches require exactly two player descriptions.");
            if (OrdinaryMatchRules.Active) OrdinaryMatchRules.ValidateStart(info);
            if (TutorialScenarioRules.Active) TutorialScenarioRules.ValidateStart(info);
            if (StoryScenarioRules.Active) StoryScenarioRules.ValidateStart(info);
            GameConfiguration config = GameManager.Instance.Config;
            if (config == null || config.CardDefinitions == null || config.CardDefinitions.Count == 0
                || config.PlayerDefinitions == null || config.PlayerDefinitions.Count == 0
                || config.Zones == null || config.Zones.Count == 0)
                throw new InvalidOperationException("The complete shipped game configuration must be loaded before Simulation.Start.");
            Scenario scenario = GameManager.Instance.FindScenario(info.ScenarioId);
            if (scenario == null) scenario = config.Scenarios.Find(delegate(Scenario x) { return x.ResourceId.UniqueName == "Default"; });
            if (scenario != null && (scenario.AvatarOverrides == null || scenario.PlayerOverrides == null
                || scenario.DeckOverrides == null || scenario.GameZoneOverrides == null || scenario.TriggeredEffects == null))
                throw new NotSupportedException("Offline scenario is missing source override/effect fields: " + scenario.ResourceId.UniqueName
                    + ". AvatarOverrides and PlayerOverrides are NonSerialized; source definitions or explicit test fixtures are required.");
            sim._networkServer = server;
            sim.RandomGenerator = info.RandomNumberGenerator ?? new RandomIntegerGenerator(Environment.TickCount);
            sim.CardCatalog = new ServerCardCatalog(sim.RandomGenerator);
            sim.CardCatalog.ExcludeCardIds = new List<int>(info.ExcludedCardIds);
            sim.HistoryLog = new HistoryLog();
            sim.Players.Clear();
            sim.CurrentOpponents.Clear();
            sim.CurrentPlayer = null;
            RuneProphecyRestoration.Clear(sim);
            sim._turnStateStack.Clear();
            sim._gameState = new GameState();
            sim._gameProperties = scenario == null ? config.Properties : scenario.GameProperties;
            if (sim._gameProperties == null || sim._gameProperties.GameStartActions == null || sim._gameProperties.TurnStartActions == null
                || sim._gameProperties.TurnEndActions == null || sim._gameProperties.LossConditions == null)
                throw new NotSupportedException("Offline scenario/game properties are missing native start/turn/loss rules.");
            sim._scenarioId = scenario == null ? info.ScenarioId : scenario.ResourceId.Id;
            sim._scenarioLossStreak = info.ScenarioLossStreak;
            sim._skipIntroCheat = info.SkipIntro;
            sim._isBonusMatch = info.IsBonusMatch;
            sim._isLegendsMatch = info.IsLegendsMatch;
            sim._matchMode = info.MatchMode;
            sim._matchType = info.MatchType;
            sim.PlayerServiceClients = new PlayerServiceClient[2];
            sim.ObserverClients.Clear();
            sim.ActiveQuests.Clear();
            sim.WinnerId = PlayerUniqueId.GetInvalid();
            sim.LoserId = PlayerUniqueId.GetInvalid();
            sim.StrandedGame = false;
            sim.ConcededGame = false;
            sim._hasWrittenReplayFilesAtEndOfGame = false;
            sim.GameLengthStopwatch.Reset();
            sim.GameCompletionStopwatch.Reset();
            int first = 0;
            if (scenario != null && scenario.StartingPlayer != SinglePlayerFirstToAct.Random)
            {
                PlayerType wanted = scenario.StartingPlayer == SinglePlayerFirstToAct.HumanPlayer ? PlayerType.Normal : PlayerType.AiOpponent;
                first = info.Users[0].Type == wanted ? 0 : 1;
            }
            else if (HumanFirst) first = info.Users[0].Type == PlayerType.Normal ? 0 : 1;
            else if (AiFirst) first = info.Users[0].Type == PlayerType.AiOpponent ? 0 : 1;
            else first = sim.RandomGenerator.GenerateInRange(0, 1);
            for (int slot = 0; slot < 2; slot++)
            {
                int index = slot == 0 ? first : 1 - first;
                MatchConfiguration.User user = info.Users[index];
                RegisterPlayer(sim, scenario, new PlayerUniqueId(slot + 1), user.EncryptionKey, user.ObserverEncryptionKey,
                    user.SecretKey, user.SpectatorKey, user.CustomDeck, user.Type, user.RetryInfo, user.Rank,
                    user.LegendRank, user.AllowTP, user.ArenaBonusMatch, info.OptionalCreatureBoardOverride[index],
                    info.OptionalExtraCards, info.PlayerHealthOverride, info.MatchStats.Count > index ? info.MatchStats[index] : null,
                    info.CompletedEvents);
            }
            sim._gameState.GamePhase = GamePhase.GatherPlayers;
        }

        public static void RegisterPlayer(Simulation sim, Scenario scenario, PlayerUniqueId uid, byte[] encryptionKey,
            byte[] observerEncryptionKey, string secretKey, string spectatorKey, PlayerDescription description,
            PlayerType type, RetryInfo retryInfo, int rank, int legendRank, bool allowTP, bool bonusMatch,
            GameZoneOverride boardOverride, List<MatchConfiguration.StartInfo.ExtraZoneCards> extras,
            int healthOverride, List<int> stats, List<int> completedEvents)
        {
            if (description == null) throw new ArgumentNullException("description");
            int slot = sim.Players.Count;
            if (slot >= 2) throw new InvalidOperationException("Player registration exceeds two players.");
            GameConfiguration config = GameManager.Instance.Config;
            if (config == null || config.PlayerDefinitions == null || config.PlayerDefinitions.Count == 0
                || (config.PlayerDefinitions.Count != 1 && slot >= config.PlayerDefinitions.Count))
                throw new InvalidOperationException("No configured player definition for offline player registration.");
            PlayerState player = new PlayerState();
            player.NetId = uid;
            player.Name = description.PlayerName;
            player.PlayerType = (int)type;
            player.IsHuman = type == PlayerType.Normal;
            player.Rank = rank;
            player.LegendRank = legendRank;
            player.ArenaBonusMatch = bonusMatch;
            player.ScenarioLossStreak = sim._scenarioLossStreak;
            player.DeckModifiedTime = description.DeckModifiedTime;
            player.AiOpponentReference.resourceId = description.AiOpponentId;
            player.MulliganState = sim._gameProperties.MulliganDisabled ? PlayerMulliganState.Mulligan_Complete : PlayerMulliganState.Mulligan_Draw;
            if (stats != null) player.MatchStatValues.AddRange(stats);
            while (player.MatchStatValues.Count < 28) player.MatchStatValues.Add(0);
            if (completedEvents != null) player.CompletedEvents.AddRange(completedEvents);
            for (int i = 0; i < Math.Min(description.InventoryTypes.Count, description.InventoryValues.Count); i++)
                player.AccountInventory[description.InventoryTypes[i]] = description.InventoryValues[i];
            // The installed configuration has one shared player template. Each
            // PlayerState receives its own copied attributes before slot overrides.
            CopyAttributes(config.PlayerDefinitions[config.PlayerDefinitions.Count == 1 ? 0 : slot], player);
            PlayerSlotMask mask = (slot == 0 ? PlayerSlotMask.FirstPlayer : PlayerSlotMask.SecondPlayer) |
                (player.IsHuman ? PlayerSlotMask.Human : PlayerSlotMask.Ai);
            if (scenario != null)
                foreach (PlayerOverride item in scenario.PlayerOverrides)
                    if ((item.SlotMask & mask) != 0) CopyAttributes(item.Override, player);

            GameZoneOverrideList zoneOverrides = new GameZoneOverrideList();
            if (scenario != null)
                foreach (GameZoneOverrideList item in scenario.GameZoneOverrides)
                    if ((item.SlotMask & mask) != 0)
                        foreach (GameZoneOverride zone in item.ZoneList) MergeZoneOverride(zoneOverrides, zone);
            if (boardOverride != null) MergeZoneOverride(zoneOverrides, boardOverride);
            sim.Players.Add(player);
            sim.PlayerServiceClients[slot] = new PlayerServiceClient(player.IsHuman, uid, encryptionKey,
                secretKey, spectatorKey, retryInfo, false, allowTP, false);
            sim.CardCatalog.CreatePlayerGameZones(uid, config.Zones, zoneOverrides);

            List<int> cards = new List<int>();
            List<bool> premiums = new List<bool>();
            for (int i = 0; i < description.Deck.Count; i++)
            { cards.Add(description.Deck[i]); premiums.Add(i < description.Premium.Count && description.Premium[i]); }
            if (scenario != null)
                foreach (DeckOverride item in scenario.DeckOverrides)
                    if ((item.SlotMask & mask) != 0)
                    {
                        CustomDeck deck = config.CustomDecks.Find(delegate(CustomDeck x) { return x.ResourceId.Id == item.Override.resourceId; });
                        if (deck == null || deck.Decks.Count == 0) throw new InvalidOperationException("Scenario deck is absent from loaded configuration.");
                        PresetDeck preset = deck.Decks.Find(delegate(PresetDeck x) { return x.Difficulty == item.Difficulty; }) ?? deck.Decks[0];
                        cards.Clear(); premiums.Clear();
                        // Preset recipes are NonSerialized in the shipped config: reconstruct when absent.
                        List<DeckCardInfo> recipe = preset.Cards != null && preset.Cards.Count > 0 ? preset.Cards : OfflineAiDecks.Reconstruct(config, deck, preset);
                        foreach (DeckCardInfo card in recipe)
                            for (int n = 0; n < card.Count; n++) { cards.Add(card.Id); premiums.Add(false); }
                    }
            bool zoneOverridesDeck = zoneOverrides.ZoneList.Exists(delegate(GameZoneOverride z) { return z.ZoneToOverride == GameZoneName.Deck; });
            if (!zoneOverridesDeck)
                for (int i = 0; i < cards.Count; i++) SeedCard(sim, player, cards[i], premiums[i], GameZoneName.Deck, 0);
            foreach (GameZoneOverride zone in zoneOverrides.ZoneList)
                for (int lane = 0; lane < zone.LaneOverrides.Count; lane++)
                    foreach (DeckCardInfo card in zone.LaneOverrides[lane].StartingCards)
                        for (int n = 0; n < card.Count; n++) SeedCard(sim, player, card.Id, false, zone.ZoneToOverride, lane);
            if (extras != null)
                foreach (MatchConfiguration.StartInfo.ExtraZoneCards extra in extras)
                    foreach (DeckCardInfo card in extra.CardsToAdd)
                        for (int n = 0; n < card.Count; n++) SeedCard(sim, player, card.Id, false, extra.ZoneName, 0);
            foreach (ServerCard card in sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, uid, GameZoneName.Deck))
            { card.StartedInDeck = true; player.StartingDeck.Add(card.CardId); player.StartingDeckPremiums.Add(card.Premium); }
            // The intro's class icons use the actual final seeded deck, including
            // scenario overrides. Reuse the shipped deck color calculation.
            Deck displayDeck = new Deck();
            foreach (int id in player.StartingDeck) displayDeck.AddCard(id);
            player.StartingDeckColors.AddRange(displayDeck.GetDeckCardColors(false));
            player.StartingDeckColors.Sort();
            player.DeckDisplayColors.AddRange(player.StartingDeckColors);
            // Scenario StartingCards encode scripted draw order (including the
            // first tutorial). A scenario may still explicitly shuffle through
            // its original GameStartActions. Only ordinary seeded decks receive
            // the registration shuffle here.
            if (!zoneOverridesDeck) sim.CardCatalog.ShuffleCardsInZone(GameZoneName.Deck, uid);

            int avatarId = description.AvatarCardId;
            if (scenario != null)
                foreach (AvatarOverride item in scenario.AvatarOverrides)
                    if ((item.SlotMask & mask) != 0) avatarId = item.Override.CardIdReference;
            if (!Simulation.IsValidAvatarCard(avatarId)) throw new InvalidOperationException("Offline player has no valid avatar card.");
            ServerCard avatar = SeedCard(sim, player, avatarId, false, GameZoneName.AvatarDeck, 0);
            if (healthOverride > 0 && player.IsHuman)
            { avatar.SetAttribute(AttributeType.StartingHealth, healthOverride); avatar.SetAvatarHealthPreGame(healthOverride); }
        }

        private static void CopyAttributes(PlayerDefinition definition, PlayerState player)
        {
            if (definition == null || definition.EntityAttributes == null)
                throw new NotSupportedException("Offline player template is missing attribute definitions.");
            foreach (SPTESL.Attribute value in definition.EntityAttributes)
            {
                IntAttribute integer = value as IntAttribute;
                BoolAttribute boolean = value as BoolAttribute;
                if (integer != null) player.SetAttribute(value.Name, integer.Value);
                else if (boolean != null) player.SetAttribute(value.Name, boolean.Value ? 1 : 0);
            }
        }
        private static void MergeZoneOverride(GameZoneOverrideList list, GameZoneOverride zone)
        { list.ZoneList.RemoveAll(delegate(GameZoneOverride x) { return x.ZoneToOverride == zone.ZoneToOverride; }); list.ZoneList.Add(zone); }
        private static ServerCard SeedCard(Simulation sim, PlayerState player, int id, bool premium, GameZoneName name, int lane)
        {
            if (GameManager.Instance.GetCard(id) == null) throw new InvalidOperationException("Unknown card in offline deck: " + id);
            GameZone zone = sim.CardCatalog.FindGameZone(player.NetId, name);
            if (zone == null) throw new InvalidOperationException("Required game zone is absent: " + name);
            GameZone.Position position = zone.FindInsertionPosition(lane, CardInsertionType.Bottom, sim.RandomGenerator);
            if (position.CardIndex < 0) throw new InvalidOperationException("Initial cards exceed zone capacity: " + name);
            ServerCard card = new ServerCard(new CardInstanceId(++sim.CardCatalog._cardInstanceIdCounter), id,
                player.NetId, UniqueResourceId.GetInvalid().Id, zone, zone.LaneList[lane].GetCardLaneType(),
                zone.LaneList[lane].GetExtraCardLaneType(), lane, ServerCardCatalog.GenerateNewCardNetUid(sim.CardCatalog), null);
            card.Premium = premium || card.Premium;
            zone.InsertCard(card.InstanceId, position);
            sim.CardCatalog._cards.Add(card.InstanceId, card);
            return card;
        }

        public static void FixedUpdate(Simulation sim)
        {
            if (sim.PlayerServiceClients == null || sim.Players.Count != 2) return;
            for (int i = 0; i < sim.PlayerServiceClients.Length; i++)
            {
                PlayerServiceClient client = sim.PlayerServiceClients[i];
                if (client == null) continue;
                if (sim._networkServer != null && client.IsExpectingHuman())
                {
                    TcpNetworkServer.NewConnection connection = sim._networkServer.AcquireRequestedConnection(client.GetSecretKey(), null);
                    if (connection.Client != null)
                    {
                        bool reconnect = client.GetConnectionStatus() != PlayerServiceClient.ConnectionStatus.NeverConnected;
                        client.SetConnection(connection.Client, connection.UserName, sim._isUsingHeartbeats);
                        InitClient init = new InitClient();
                        init.PlayerInstanceId = client.GetPlayerUid().Id;
                        init.ScenarioId = sim._scenarioId;
                        init.RetryInfo = client.retryInfo;
                        init.GameserverVersion = "offline-restoration-experimental";
                        client.SendInitClientMessage(init);
                        if (sim._gameState.GamePhase != GamePhase.GatherPlayers) sim.SendFullStateToSpecificClient(client, true);
                        sim.Players[i].FirstConnectionSeconds = (int)sim.GameLengthStopwatch.Elapsed.TotalSeconds;
                        if (reconnect) { if (sim.OnPlayerReconnect != null) sim.OnPlayerReconnect(); }
                        else if (sim.OnPlayerInitialConnection != null) sim.OnPlayerInitialConnection();
                    }
                }
                client.ExtractMessagesFromNetworkStream();
                Interrupt interrupt;
                while ((interrupt = client.PopNextInterrupt()) != null) sim.OnInterrupt(interrupt, client.GetPlayerUid());
                // Local testing deliberately excludes the developer cheat message queue.
                if (client.IsTrueNetworkConnection()) client.DectectConnectionLoss(sim._isUsingHeartbeats);
            }
            ExecuteGamePhase(sim);
        }

        public static void ExecuteGamePhase(Simulation sim)
        {
            if (sim._gameState.GamePhase == GamePhase.Results) return;
            if (sim._gameState.GamePhase == GamePhase.GatherPlayers)
            {
                foreach (PlayerServiceClient client in sim.PlayerServiceClients)
                    if (client.GetConnectionStatus() != PlayerServiceClient.ConnectionStatus.Connected) return;
                HistoryItem initial = new HistoryItem();
                initial.Type = HistoryType.InitialGameState;
                initial.FullGameState = sim.CreateFullGameState();
                // Exclude removes FullGameState payloads on any history type,
                // including InitialGameState, unless the payload opts out.
                initial.FullGameState.NeverExclude = true;
                foreach (PlayerState player in sim.Players) initial.MatchIntroPlayers.Add(player.HistoryCreate_FullPlayerDescription(sim.CardCatalog));
                sim.HistoryLog.AddInitialGamestate(initial);
                sim._gameState.GamePhase = GamePhase.Start;
                sim.GameLengthStopwatch.Start();
                if (sim.OnMatchStart != null) sim.OnMatchStart(sim._matchMode, sim._matchType);
                TurnState first = new TurnState();
                first.ActivePlayerIndex = 0;
                first.CurrentSequence = new SequenceData();
                first.CurrentSequence.Type = SequenceType.StartGame;
                sim.PushTurnState(first);
                return;
            }
            if (sim._gameState.GamePhase == GamePhase.Mulligan)
            {
                for (int i = 0; i < 2; i++)
                {
                    Decision decision;
                    while ((decision = sim.PlayerServiceClients[i].PopNextDecision()) != null)
                    {
                        // Native SendMulliganDecision sends CommandUid=0 and the
                        // complete selected UID list, including on final commit.
                        // It never sends an actor UID or a single-card toggle.
                        PlayerState player = sim.Players[i];
                        if (sim.PlayerServiceClients[i].GetPlayerUid() != player.NetId) continue;
                        TryApplyMulliganDecision(sim, player, decision);
                    }
                }
                if (!sim.IsAllPlayerMulliganDecisionsMade()) return;
                sim._turnStateStack.Clear();
                sim._gameState.GamePhase = GamePhase.MainLoop;
                // Scenario pre-placed board cards render only from a FullGameState. With a
                // mulligan it cannot be sent at game start (it would close the client's
                // mulligan), so send it once the mulligan is over (DuringMulligan=false).
                // (SendSequenceHistoryToClients peeks the turn stack, so the first turn comes first.)
                sim.AddFirstMainLoopTurnState();
                if (HasStartingBoardCards(sim)) { AddStartingFullState(sim); Send(sim); }
                return;
            }
            if (sim._turnStateStack.Count == 0) throw new InvalidOperationException("Active offline match has no turn state.");
            sim.GatherCurrentPlayerAndOpponents();
            TurnState turn = sim._turnStateStack.Peek();
            if (PumpEffects(sim, turn)) { Send(sim); return; }
            sim.ProcessDiscards();
            sim.CheckForRuneBreaks();
            if (RuneProphecyRestoration.HasQueuedEffects(turn)) return;
            sim.FlushDeathrattleZone();
            if (RuneProphecyRestoration.HasQueuedEffects(turn)) return;
            if (sim._gameState.GamePhase == GamePhase.MainLoop)
            {
                bool firstLost = sim.Players[0].ForceLoss || sim.DidPlayerLose(sim._gameProperties.LossConditions, sim.Players[0], sim.Players[1]);
                bool secondLost = sim.Players[1].ForceLoss || sim.DidPlayerLose(sim._gameProperties.LossConditions, sim.Players[1], sim.Players[0]);
                if (firstLost || secondLost)
                {
                    // The match can end while a play, attack, or interrupt sequence is still open.
                    // Results never resume that sequence, so close its outstanding native history
                    // events before emitting EventGameEnd, including any nested parent events.
                    sim.HistoryLog.TryEndAllEvents();
                    RuneProphecyRestoration.Clear(sim); sim.EndGame(firstLost, secondLost); Send(sim); return;
                }
            }
            if (turn.CurrentSequence != null)
            {
                SequenceType sequenceType = turn.CurrentSequence.Type;
                sim.ProcessSequence(turn);
                if (sequenceType == SequenceType.Decision && turn.CurrentSequence == null && turn.Phase == TurnPhase.End)
                {
                    turn.CurrentSequence = new SequenceData();
                    turn.CurrentSequence.Type = SequenceType.EndTurn;
                }
                Send(sim);
                return;
            }
            if (sim._gameState.GamePhase == GamePhase.Start)
            {
                if (sim._gameProperties.MulliganDisabled)
                { sim._turnStateStack.Clear(); sim._gameState.GamePhase = GamePhase.MainLoop; sim.AddFirstMainLoopTurnState(); }
                else { turn.Phase = TurnPhase.Mulligan; sim.StartMulligan(); }
                return;
            }
            if (turn.Phase == TurnPhase.Interrupt)
            { if (RuneProphecyRestoration.TryFinishInterrupt(sim, turn)) sim.PopTurnState(); Send(sim); return; }
            if (turn.Phase == TurnPhase.Start) turn.Phase = TurnPhase.PlayCards;
            if (turn.Phase == TurnPhase.PlayCards)
            {
                if (turn.CurrentCommandItem == null)
                { OfflineDoubleCards.SplitHands(sim, turn); turn.CurrentCommandItem = sim.CardCatalog.GenerateCardCommandItem(sim.CurrentPlayer, sim.CurrentOpponents[0], sim.Players); Send(sim); }
                sim.GetPlayerDecision(turn);
                return;
            }
            if (turn.Phase == TurnPhase.End)
            {
                int next = 1 - turn.TurnPlayerIndex;
                sim.PopTurnState();
                TurnState nextTurn = new TurnState(); nextTurn.ActivePlayerIndex = next;
                nextTurn.CurrentSequence = new SequenceData(); nextTurn.CurrentSequence.Type = SequenceType.StartTurn;
                sim.PushTurnState(nextTurn);
                Send(sim);
            }
        }

        private static readonly System.Reflection.FieldInfo historyMessages = AccessTools.Field(typeof(HistoryLog), "_messageLog");
        private static readonly System.Reflection.FieldInfo historySentIndex = AccessTools.Field(typeof(HistoryLog), "_lastMessageSentIndex");
        public static int SkippedResends;

        // Nothing new for the client: no unsent history and the prompt it already has (same list uid). The main loop calls
        // Send every tick while a decision sequence waits; SendSequenceHistoryToClients always appends the current command
        // list, so the client used to receive the same prompt ~60 times a second (the custom-choice picker rebuilt itself
        // each time: visible flicker on Explore, 2026-11-04).
        public static bool NothingToSend(Simulation sim)
        {
            if (sim.CurrentPlayer == null || sim._gameState.GamePhase == GamePhase.Mulligan || sim._turnStateStack.Count == 0) return false;
            CardCommandItem next = sim._turnStateStack.Peek().CurrentCommandItem, last = sim.CurrentPlayer.LastSentCommandItem;
            if (next == null || last == null || next.CommandList == null || last.CommandList == null || next.CommandList.ListUid != last.CommandList.ListUid) return false;
            System.Collections.IList log = historyMessages == null ? null : historyMessages.GetValue(sim.HistoryLog) as System.Collections.IList;
            if (log == null || historySentIndex == null) return false;
            return (int)historySentIndex.GetValue(sim.HistoryLog) >= log.Count - 1;
        }

        private static void Send(Simulation sim)
        {
            if (NothingToSend(sim)) { SkippedResends++; return; }
            // The client floats ("ready") exactly the cards in FloatingCards items. Original
            // protocol (Reborn capture 2026-10-19): a FLOATING_CARDS item precedes each new
            // CARD_COMMAND_LIST, generated by the native turn-player creature/attack-target rule.
            if (sim.CurrentPlayer != null && sim._gameState.GamePhase != GamePhase.Mulligan && sim._turnStateStack.Count > 0)
            {
                CardCommandItem next = sim._turnStateStack.Peek().CurrentCommandItem;
                if (next != null && !Object.ReferenceEquals(next, sim.CurrentPlayer.LastSentCommandItem))
                {
                    HistoryItem floating = sim.GenerateFloatingCardHistoryItem();
                    floating.FloatingCardsUpdate = ReadyToAttack(sim, floating.FloatingCardsUpdate);
                    sim.HistoryLog.AddFloatingCardItem(floating);
                }
            }
            sim.SendSequenceHistoryToClients(HistoryLog.FullGameStateSendRule.Exclude, false);
        }

        public static bool TryApplyMulliganDecision(Simulation sim, PlayerState player, Decision decision)
        {
            MulliganResponse response;
            if (sim == null || player == null || decision == null || sim._gameState.GamePhase != GamePhase.Mulligan
                || !sim.Players.Contains(player) || player.MulliganState != PlayerMulliganState.Mulligan_Draw
                || !sim._gameState.MulliganPlayerResponses.TryGetValue(player.NetId.Id, out response)
                || response.Action == MulliganAction.Commit || response.Action == MulliganAction.Timeout
                || player.LastSentCommandItem == null || player.LastSentCommandItem.CommandList == null
                || !player.LastSentCommandItem.CommandList.IsMulliganPhase
                || (decision.Type != CcgRpc.DecisionType.Mulligan && decision.Type != CcgRpc.DecisionType.MulliganCommit)
                || decision.CommandUid != 0 || decision.ActorCardNetUid != 0 || decision.TargetCardNetUid != 0
                || decision.EquipCardNetUid != 0 || decision.LaneIndex != 0 || decision.PositionIndex != 0
                || decision.LaneTarget != BoardLaneTarget.None || decision.CustomChoice != 0)
                return false;
            CardCommandList commands = player.LastSentCommandItem.CommandList;
            if (decision.DiscardCardNetUids.Count > commands.ValidCommands.Count
                || decision.DiscardCardNetUids.Count > sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId))
                return false;
            List<int> selected = new List<int>();
            foreach (int uid in decision.DiscardCardNetUids)
            {
                if (uid <= 0 || selected.Contains(uid)) return false;
                ServerCard card = sim.CardCatalog.FindDynamicCard(new CardNetUid(uid));
                bool offered = false;
                foreach (CardCommandState command in commands.ValidCommands)
                    if (command.CardNetUid == uid) { offered = true; break; }
                if (card == null || card.OwnerNetId != player.NetId || card.CurrentGameZoneName != GameZoneName.Hand || !offered)
                    return false;
                selected.Add(uid);
            }
            // Validate every UID before publishing a new selection or executing
            // native redraw. A bad packet cannot partially change a selection.
            response.MulliganCardNetworkUids = selected;
            if (decision.Type == CcgRpc.DecisionType.MulliganCommit)
            {
                response.Action = MulliganAction.Commit;
                sim.ReportMulliganDecision(player, response);
            }
            else sim._gameState.MulliganPlayerResponses[player.NetId.Id] = response;
            return true;
        }

        // Called inside the single EventGameStart, before its original draw
        // actions. The native board needs this action to construct its lanes;
        // SequenceInitialGameState alone does not initialize the board.
        public static void AddMatchIntro(Simulation sim)
        { sim.HistoryLog.AddInstant(CreateMatchIntro(sim)); }

        // Client SequenceInitialGameState applies nothing; only a FullGameState item
        // populates scenario starting board cards and player attributes (ring). The
        // Reborn capture shows MATCH_INTRO then FULL_GAME_STATE at game start. Callers
        // restrict this to mulligan-disabled scenarios: a full state with
        // DuringMulligan=false would close the client's mulligan phase early.
        internal static bool HasStartingBoardCards(Simulation sim)
        {
            foreach (PlayerState player in sim.Players)
                foreach (GameZoneName zone in new GameZoneName[] { GameZoneName.CreatureBoard, GameZoneName.SupportBoard })
                    if (sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, zone).Count > 0) return true;
            return false;
        }

        internal static void AddStartingFullState(Simulation sim)
        {
            HistoryItem item = new HistoryItem();
            item.Type = HistoryType.FullGameState;
            item.FullGameState = sim.CreateFullGameState();
            item.FullGameState.NeverExclude = true;
            item.FloatingCardsUpdate = ReadyToAttack(sim, sim.GenerateFloatingCards());
            sim.HistoryLog.AddFullGameState(item);
        }

        // The native floating rule lists creatures that have attack targets; the restored target gatherer doesn't check
        // whether the creature can still attack this turn, so a creature that already attacked (most visibly a Charge
        // creature on the turn it is played) kept floating and the client let it attack again: the attack animation played
        // and the server refused it (owner 2026-11-10). Keep only creatures that can attack now, as the command list does.
        internal static FloatingCards ReadyToAttack(Simulation sim, FloatingCards floating)
        {
            if (floating == null || sim._turnStateStack.Count == 0) return floating;
            TurnState turn = sim._turnStateStack.Peek();
            PlayerState player = sim.Players[turn.TurnPlayerIndex];
            PlayerState opponent = sim.Players.Find(delegate(PlayerState p) { return p.NetId != player.NetId; });
            List<int> keep = new List<int>();
            foreach (int uid in floating.CardNetUids)
            {
                ServerCard card = sim.CardCatalog.FindServerCard(new CardNetUid(uid));
                if (card != null && CommandRestoration.CanAttack(sim.CardCatalog, player, opponent, card)) keep.Add(uid);
            }
            floating.CardNetUids.Clear();
            foreach (int uid in keep) floating.CardNetUids.Add(uid);
            return floating;
        }

        internal static HistoryItem CreateMatchIntro(Simulation sim)
        {
            if (sim == null || sim.Players.Count != 2 || sim.CardCatalog == null)
                throw new InvalidOperationException("Match intro requires two registered native players and their zones.");
            GameZone first = sim.CardCatalog.FindGameZone(sim.Players[0].NetId, GameZoneName.CreatureBoard);
            GameZone second = sim.CardCatalog.FindGameZone(sim.Players[1].NetId, GameZoneName.CreatureBoard);
            // BoardState.InitializeBoard mirrors these ownerless lane descriptors
            // for both players. Do not misrepresent a distinct opponent layout.
            if (first == null || second == null || first.LaneList.Count < 1 || first.LaneList.Count > 2
                || first.LaneList.Count != second.LaneList.Count)
                throw new NotSupportedException("Native match intro requires matching one- or two-lane creature boards.");
            HistoryItem intro = new HistoryItem();
            intro.Type = HistoryType.ActionMatchIntro;
            intro.ActingPlayerInstanceId = PlayerUniqueId.GetInvalid().Id;
            intro.ActorCardNetUid = CardNetUid.GetInvalid().Id;
            intro.TargetCardNetUid = CardNetUid.GetInvalid().Id;
            intro.AssetHash = -1;
            intro.IsBonusMatch = sim._isBonusMatch;
            for (int lane = 0; lane < first.LaneList.Count; lane++)
            {
                MessageDynamicCardLane description = sim.CardCatalog.GetBasicDynamicLaneInformation(
                    sim.Players[0].NetId, GameZoneName.CreatureBoard, lane);
                MessageDynamicCardLane opponent = sim.CardCatalog.GetBasicDynamicLaneInformation(
                    sim.Players[1].NetId, GameZoneName.CreatureBoard, lane);
                if (description.LaneType != opponent.LaneType || description.ExtraLaneType != opponent.ExtraLaneType
                    || description.LaneSize != opponent.LaneSize)
                    throw new NotSupportedException("Native match intro cannot mirror asymmetric creature lane " + lane + ".");
                intro.MatchIntroLanes.Add(description);
            }
            // Native ordering is first-to-act then second; SequenceMatchIntro uses
            // this order for its coin UI and NetId for player/opponent orientation.
            foreach (PlayerState player in sim.Players)
                intro.MatchIntroPlayers.Add(player.HistoryCreate_FullPlayerDescription(sim.CardCatalog));
            return intro;
        }

        private static bool PumpEffects(Simulation sim, TurnState turn)
        {
            ServerEffectUtilities.TriggerQueues queues = turn.EffectTriggerQueues;
            if (RuneProphecyRestoration.TryPushInterrupt(sim, turn)) return true;
            if (queues.Active.Count == 0 && queues.Pending.Count > 0)
            {
                queues.Pending.Sort(ServerEffectUtilities.TriggerContext.SortComparison);
                foreach (ServerEffectUtilities.TriggerContext effect in queues.Pending) queues.Active.Enqueue(effect);
                queues.Pending.Clear();
            }
            if (queues.Active.Count == 0 && queues.After.Count > 0)
            {
                queues.After.Sort(ServerEffectUtilities.TriggerContext.SortComparison);
                foreach (ServerEffectUtilities.TriggerContext effect in queues.After) queues.Active.Enqueue(effect);
                queues.After.Clear();
            }
            if (queues.Active.Count == 0) return false;
            ServerEffectUtilities.TriggerContext context = queues.Active.Peek();
            ServerEffectUtilities.TriggerGameState state = Context(sim, turn);
            // Continuous conditions must reflect the event before its queued consequence runs.
            // E.g. a broken Ward removes Prankster Mage's Guard before native AddCover tests Guard.
            OfflineAuraRestoration.Apply(state);
            if (!ServerEffectUtilities.TestTriggerEffectConditions(state, context) || !ServerEffectUtilities.TestBeforeTargetingEffectConditions(state, context))
            { queues.Active.Dequeue(); return true; }
            // Custom-card choices (original graphs): the native TestAndExecuteTriggeredEffectFromQueue runs the main group
            // (BuildCustomUiServerCards / BuildCustomUiCards fill CustomUICards), then the chosen CustomUiEffectGroup and
            // the PostCustomUiEffectGroup; this pump supplies the decision between the two phases for every UI type.
            bool custom = context.TriggeredEffect.CustomUiType != CustomCardUiType.None;
            bool needsDecision = ServerEffectUtilities.IsPlayerDecisionRequired(context, turn.CustomUICards);
            if (needsDecision)
            {
                if (turn.Phase != TurnPhase.Interrupt)
                { sim.CreateAndPushDecisionState(turn, context); return true; }
                if (turn.TriggerDecision == null)
                {
                    if (turn.CurrentCommandItem == null)
                        turn.CurrentCommandItem = sim.CardCatalog.GenerateEffectCommandItem(context, sim.Players[turn.ActivePlayerIndex],
                            sim.Players[1 - turn.ActivePlayerIndex], sim.Players, turn.CustomUICards);
                    // A second phase can reuse an interrupt state (Consume then target).
                    // Run the native-style empty-choice fallback after regenerating its command list.
                    DecisionRestoration.CheckForAutoDecision(sim, turn, context);
                    if (turn.TriggerDecision == null) sim.GetPlayerDecision(turn);
                }
                if (turn.TriggerDecision == null) return true;
            }
            queues.Active.Dequeue();
            if (custom && context.DidMainEffectsRun && turn.TriggerDecision == null)
            {
                // Nothing to choose from (e.g. an empty deck): the choice phase ends without running a choice group.
                sim.HistoryLog.AddInstant(new HistoryItem { Type = HistoryType.ActionEndCustomUi, ActorCardNetUid = context.EffectOwner.NetUid.Id,
                    ActingPlayerInstanceId = context.EffectOwner.OwnerNetId.Id, AssetHash = -1 });
                turn.CustomUICards.Clear(); turn.CurrentCommandItem = null;
                DecisionRestoration.FinishCustomAction(state, context.EffectOwner);
                return true;
            }
            bool completedCustom = custom && context.DidMainEffectsRun;
            ServerEffectUtilities.StartTriggerEventLog(sim.HistoryLog, ref context);
            ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(state, context, turn.TriggerDecision);
            turn.TriggerDecision = null;
            turn.CurrentCommandItem = null;
            if (!context.DidMainEffectsRun && context.TriggeredEffect.CustomUiType != CustomCardUiType.None)
            {
                context.DidMainEffectsRun = true;
                if (ServerEffectUtilities.IsPlayerDecisionRequired(context, turn.CustomUICards))
                {
                    sim.HistoryLog.AddInstant(new HistoryItem {
                        Type = HistoryType.ActionStartCustomUi, ActorCardNetUid = context.EffectOwner.NetUid.Id,
                        ActingPlayerInstanceId = context.EffectOwner.OwnerNetId.Id, AssetHash = -1 });
                    queues.Active.Enqueue(context); return true;
                }
            }
            if (completedCustom) sim.HistoryLog.AddInstant(new HistoryItem { Type = HistoryType.ActionEndCustomUi,
                ActorCardNetUid = context.EffectOwner.NetUid.Id, ActingPlayerInstanceId = context.EffectOwner.OwnerNetId.Id, AssetHash = -1 });
            ServerEffectUtilities.EndTriggerEventLog(state, ref context);
            if (custom) turn.CustomUICards.Clear();
            if (completedCustom) DecisionRestoration.FinishCustomAction(state, context.EffectOwner);
            return true;
        }

        private static ServerEffectUtilities.TriggerGameState Context(Simulation sim, TurnState turn)
        { return new ServerEffectUtilities.TriggerGameState(sim.HistoryLog, sim.RandomGenerator, sim.CardCatalog, sim.Players, turn.EffectTriggerQueues, sim.ActiveQuests, turn.CustomUICards, sim._scenarioId); }

        public static Simulation.SequenceStatus ProcessEndTurn(Simulation sim, TurnState turn)
        {
            ServerEffectUtilities.TriggerGameState state = Context(sim, turn);
            PlayerState player = sim.Players[turn.TurnPlayerIndex];
            if (turn.CurrentSequence.Stage == 0)
            {
                turn.CurrentSequence.EventStarted = true;
                sim.HistoryLog.StartEvent(HistoryType.EventTurnEnd, player.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1,
                    false, EffectTriggerType.Invalid, SanitizeRule.Default, null, UIGameMechanicTypes.Invalid, -1, -1);
                OfflineTurnMechanics.EndTurn(state, sim, player);
                ServerEffectUtilities.TriggerEffectEvent(state, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnEnds, player));
                return Simulation.SequenceStatus.Processing;
            }
            if (turn.CurrentSequence.Stage == 1)
            {
                ServerEffectUtilities.ExecutePlayerEffectActions(state, sim._gameProperties.TurnEndActions,
                    new PlayerConditionalArguments(sim.CardCatalog, player, player, null, null, sim.Players[1 - turn.TurnPlayerIndex]),
                    new CardConditionalArguments(sim.CardCatalog, null, null, null, null, null, LaneIdentifier.GetInvalid()),
                    new TriggerConditionalArguments(0, AttributeType.None));
                return Simulation.SequenceStatus.Processing;
            }
            if (turn.CurrentSequence.Stage == 2)
            {
                foreach (ServerCard card in sim.CardCatalog.GatherDynamicCardList()) card.OnEndTurn(player.NetId, state);
                ServerEffectUtilities.TriggerEffectEvent(state, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnEndsCleanup, player));
                return Simulation.SequenceStatus.Processing;
            }
            player.IsMyTurn = false;
            turn.CurrentCommandItem = null;
            turn.Phase = TurnPhase.End;
            return Simulation.SequenceStatus.Complete;
        }
    }
}
