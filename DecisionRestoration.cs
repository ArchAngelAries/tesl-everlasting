using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Reconstruction, not recovered server source. This uses the shipped rules/actions
    // and rejects mechanics whose missing orchestration has not been reconstructed.
    public static class DecisionRestoration
    {
        public static void Install(Harmony harmony)
        {
            string[] methods = { "ProcessStartGame", "ProcessStartTurn", "ProcessDecision", "ProcessPlayCard", "ProcessAttack", "ProcessActivate", "ProcessBetray", "ExecuteMulliganDecision", "GetPlayerDecision", "CheckForAutoDecision", "CreateAndPushDecisionState", "ProcessDiscards", "CheckForRuneBreaks", "ProcessCardInterrupt" };
            foreach (string name in methods)
                harmony.Patch(AccessTools.Method(typeof(Simulation), name), new HarmonyMethod(typeof(DecisionRestoration).GetMethod(name + "Prefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(DiscardUtilities), "DiscardCards"), new HarmonyMethod(typeof(DecisionRestoration).GetMethod("DiscardCardsPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(ServerCardCatalog), "GenerateCardCommandItem"), null, new HarmonyMethod(typeof(OfflineBetrayRestoration).GetMethod("GenerateCardCommandItemPostfix")), null, null, null);
            OfflineSummonRestoration.Install(harmony);
            OfflineActionGuards.Install(harmony);
            OfflineItemTriggers.Install(harmony);
            OfflineQuestHooks.Install(harmony);
            // The shipped EnchantmentUtilities.RecalculateAuras is empty; route it to the reconstructed recompute.
            harmony.Patch(AccessTools.Method(typeof(EnchantmentUtilities), "RecalculateAuras"), new HarmonyMethod(typeof(OfflineAuraRestoration).GetMethod("RecalculateAurasPrefix")), null, null, null, null);
            OfflineConditionGuards.Install(harmony);
        }

        public static ServerEffectUtilities.TriggerGameState State(Simulation s, TurnState t)
        {
            if (t.CustomUICards == null) t.CustomUICards = new List<CustomDisplayCard>();
            return new ServerEffectUtilities.TriggerGameState(s.HistoryLog, s.RandomGenerator, s.CardCatalog, s.Players, t.EffectTriggerQueues, s.ActiveQuests, t.CustomUICards, s._scenarioId);
        }

        public static void ExecutePlayerActions(Simulation s, TurnState t, PlayerState player, List<PlayerEffectAction> actions)
        {
            PlayerState opponent = s.Players.Find(delegate(PlayerState p) { return p.NetId != player.NetId; });
            ServerEffectUtilities.ExecutePlayerEffectActions(State(s, t), actions,
                new PlayerConditionalArguments(s.CardCatalog, player, player, null, null, opponent),
                new CardConditionalArguments(s.CardCatalog, null, null, null, null, null, LaneIdentifier.GetInvalid()),
                new TriggerConditionalArguments(0, AttributeType.None));
        }

        private static void Begin(Simulation s, TurnState t, HistoryType type, ServerCard actor, ServerCard target)
        { Begin(s, t, type, actor, target, -1); }

        private static void Begin(Simulation s, TurnState t, HistoryType type, ServerCard actor, ServerCard target, int assetHash)
        {
            t.CurrentSequence.EventStarted = true;
            s.HistoryLog.StartEvent(type, s.Players[t.ActivePlayerIndex].NetId,
                actor == null ? CardNetUid.GetInvalid() : actor.NetUid,
                target == null ? CardNetUid.GetInvalid() : target.NetUid, assetHash);
        }

        public static Simulation.SequenceStatus ProcessStartGame(Simulation s, TurnState t)
        {
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            if (t.CurrentSequence.Stage == 0)
            {
                Begin(s, t, HistoryType.EventGameStart, null, null);
                LifecycleRestoration.AddMatchIntro(s);
                if (s._gameProperties.MulliganDisabled && Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-no-start-fullstate") < 0) LifecycleRestoration.AddStartingFullState(s);
                foreach (PlayerState player in s.Players)
                {
                    player.MulliganState = s._gameProperties.MulliganDisabled ? PlayerMulliganState.Mulligan_Complete : PlayerMulliganState.Mulligan_Draw;
                }
                // Execute the game-start event once. Native action selectors build
                // their own target list over all players; an empty selector list
                // targets both players and must not be run once per player again.
                ExecutePlayerActions(s, t, s.Players[t.ActivePlayerIndex], s._gameProperties.GameStartActions);
                return Simulation.SequenceStatus.Processing;
            }
            if (t.CurrentSequence.Stage == 1)
            {
                ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenGameStarts));
                if (s._gameProperties.MulliganDisabled) s.ProcessGameStartSummons(t);
                return Simulation.SequenceStatus.Processing;
            }
            return Simulation.SequenceStatus.Complete;
        }

        public static Simulation.SequenceStatus ProcessStartTurn(Simulation s, TurnState t)
        {
            PlayerState p = s.Players[t.ActivePlayerIndex];
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            if (t.CurrentSequence.Stage == 0)
            {
                Begin(s, t, HistoryType.EventTurnStart, null, null);
                foreach (PlayerState other in s.Players) other.IsMyTurn = other == p;
                foreach (PlayerState other in s.Players) other.SetAttribute(AttributeType.CreaturesDeadThisTurn, 0);
                p.NumCardsPlayedThisTurn = 0;
                OfflineTurnMechanics.StartTurn(p);
                p.TookAction = false;
                foreach (ServerCard card in s.CardCatalog.GatherDynamicCardList()) card.OnStartTurn(p.NetId, gs);
                // The turn player's avatar is the event's inciter (same player as before): Corpselight Farmer checks the inciter's
                // MaxMana (an avatar attribute) for "if you have 7 or more max magicka" (transform sweep 2026-11-06).
                ServerCard turnAvatar = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, p.NetId, GameZoneName.AvatarBoard);
                ServerEffectUtilities.TriggerEffectEvent(gs, turnAvatar != null
                    ? new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStartsCleanup, turnAvatar, s.Players)
                    : new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStartsCleanup, p));
                return Simulation.SequenceStatus.Processing;
            }
            if (t.CurrentSequence.Stage == 1)
            {
                RestoreStartTurnMagicka(s, t, p);
                if (OrdinaryMatchRules.Active) OrdinaryMatchRules.ResetRingForTurn(s, t);
                else if (TutorialScenarioRules.Active && TutorialScenarioRules.Current.SecondPlayerRing) TutorialScenarioRules.ResetRingForTurn(s, t);
                else if (StoryScenarioRules.Active) StoryScenarioRules.ResetRingForTurn(s, t);
                OfflineModes.OnTurnStart(s, t, p);
                ExecutePlayerActions(s, t, p, s._gameProperties.TurnStartActions);
                ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, p));
                return Simulation.SequenceStatus.Processing;
            }
            t.Phase = TurnPhase.PlayCards;
            OfflineDoubleCards.SplitHands(s, t);
            t.CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(p, s.CurrentOpponents[0], s.Players);
            return Simulation.SequenceStatus.Complete;
        }

        internal static void RestoreStartTurnMagicka(Simulation s, TurnState t, PlayerState player)
        {
            ServerCard avatar = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard card) { return card.HasSubtype(CardSubtype.Avatar); },
                player.NetId, GameZoneName.AvatarBoard);
            if (avatar == null || !avatar.QueryAttribute(AttributeType.MaxMana).HasAttribute)
                throw new NotSupportedException("Turn-start magicka requires a board avatar with native MaxMana.");
            // DoubleMaxManaGained (Pure-Blood Elder's aura on the avatar) is applied by the game itself: ServerCard.AddEnchantment
            // multiplies every positive MaxMana Add enchantment by 2^value, so the natural +1 below becomes +2 (decompiled
            // ServerCard, 2026-11-13). A player-level value has no native reader and stays gated.
            if (player.CheatMana != 0 || player.QueryAttribute(AttributeType.DoubleMaxManaGained).Value != 0)
                throw new NotSupportedException("Turn-start magicka with cheats or a player-level DoubleMaxManaGained is not validated.");
            int turns = player.QueryAttribute(AttributeType.TurnCount).Value;
            if (turns < 0 || turns == Int32.MaxValue)
                throw new NotSupportedException("Invalid native player TurnCount for magicka growth.");
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            // Player TurnCount advances only on this player's turn. Card.NumTurns
            // advances on every global turn and cannot drive natural magicka.
            PlayerActions.SetAttribute.StaticExecute(s.HistoryLog, player, AttributeType.TurnCount, turns + 1);
            if (turns < Simulation.MaxManaUnbuffed_)
            {
                // Keep bonuses in the native enchantment model. Setting a raw
                // attribute would lose them on the next aura recalculation.
                avatar.AddMaxManaBuff(gs, 1);
                avatar.RecalculateEnchantmentsWithTriggers(gs, null);
                HistoryLoggingUtilities.LogFullCardRefresh(s.HistoryLog, s.CardCatalog, new List<ServerCard> { avatar }, false);
            }
            PlayerActions.SetMana.StaticExecute(gs, player, avatar.QueryAttribute(AttributeType.MaxMana).Value);
        }

        public static void ExecuteMulliganDecision(Simulation s, PlayerState player)
        {
            MulliganResponse response = s._gameState.MulliganPlayerResponses[player.NetId.Id];
            if (player.MulliganState == PlayerMulliganState.Mulligan_Complete) return;
            TurnState t = s._turnStateStack.Peek();
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            List<ServerCard> replacements = new List<ServerCard>();
            if (response.MulliganCardNetworkUids != null)
                foreach (int uid in response.MulliganCardNetworkUids)
                {
                    ServerCard card = s.CardCatalog.FindDynamicCard(new CardNetUid(uid));
                    if (card == null || card.OwnerNetId != player.NetId || card.CurrentGameZoneName != GameZoneName.Hand || replacements.Contains(card))
                        throw new InvalidOperationException("Invalid offline mulligan card selection.");
                    replacements.Add(card);
                }
            // The client's SequenceMulliganResponse reads the returned cards from this event's header: with none it treats
            // the hand as kept, closes the mulligan and the redraw that follows has no slot (owner 2026-11-06: the redrawn
            // card never appeared). Only the net ids are read; no card identity is sent.
            List<HistoryCard> returned = new List<HistoryCard>();
            foreach (ServerCard card in replacements) returned.Add(new HistoryCard { CardNetUid = card.NetUid.Id });
            s.HistoryLog.StartEvent(HistoryType.EventMulliganResponse, player.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1,
                false, EffectTriggerType.Invalid, SanitizeRule.Default, returned, UIGameMechanicTypes.Invalid, -1, -1);
            HistoryItem discarded = new HistoryItem();
            discarded.Type = HistoryType.ActionDiscardForMulligan;
            discarded.ActingPlayerInstanceId = player.NetId.Id;
            discarded.AssetHash = -1;
            // The client's SequenceMulliganDiscard handles only cards whose position is the DECK (it animates them into
            // the deck and the redraw then fills their slots); any other position recycles the card with no animation and
            // the redrawn card has no slot to fill (owner 2026-11-06: the redrawn card never appeared). The cards wait in
            // View while the replacements are drawn (so the same card cannot come back), so the history shows them where
            // they end: the owner's deck.
            foreach (ServerCard card in replacements)
            {
                Move(gs, card, GameZoneType.View, 0);
                HistoryCard shown = card.HistoryCreate_FullStateCard(s.CardCatalog);
                shown.CurrentPosition.GameZoneName = (int)GameZoneName.Deck;
                shown.CurrentPosition.GameZoneType = (int)GameZoneType.Deck;
                shown.CurrentPosition.LaneIndex = 0;
                shown.CurrentPosition.PositionIndex = 0;
                shown.PreviousGameZoneType = (int)GameZoneType.Hand;
                discarded.AffectedCardsArray.Add(shown);
            }
            if (replacements.Count > 0) s.HistoryLog.AddInstant(discarded);
            player.MulliganState = PlayerMulliganState.Mulligan_Redraw;
            PlayerActions.DrawCards.StaticExecute(gs, player, replacements.Count, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.Untyped, true, false, -1);
            foreach (ServerCard card in replacements) Move(gs, card, GameZoneType.Deck, 0);
            s.CardCatalog.ShuffleCardsInZone(GameZoneName.Deck, player.NetId);
            player.MulliganState = PlayerMulliganState.Mulligan_Complete;
            player.LastSentCommandItem = null;
            s.HistoryLog.EndEvent();
        }

        public static Simulation.DecisionStatus ProcessDecision(Simulation s, TurnState t)
        {
            Decision d = t.CurrentSequence.PlayerDecision;
            if (d == null) return Simulation.DecisionStatus.Error;
            switch (d.Type)
            {
                case CcgRpc.DecisionType.Play: return ProcessPlayCard(s, t, d);
                case CcgRpc.DecisionType.Attack: return ProcessAttack(s, t, d);
                case CcgRpc.DecisionType.Activate: return ProcessActivate(s, t, d);
                case CcgRpc.DecisionType.Betray: return ProcessBetray(s, t, d);
                case CcgRpc.DecisionType.ToHand: return s.ProcessToHand(t, d);
                case CcgRpc.DecisionType.UseRing: return s.ProcessRing(t, d);
                case CcgRpc.DecisionType.NoCommand:
                    Begin(s, t, HistoryType.EventRequestEndTurn, null, null);
                    t.Phase = TurnPhase.End;
                    return Simulation.DecisionStatus.EndPhase;
                default: throw new NotSupportedException("Offline decision orchestration not restored: " + d.Type);
            }
        }

        private static ServerCard Card(Simulation s, int uid) { return s.CardCatalog.FindDynamicCard(new CardNetUid(uid)); }

        private static void Move(ServerEffectUtilities.TriggerGameState gs, ServerCard card, GameZoneType destination, int lane)
        {
            GameZoneOperationResult result = ServerCardCatalog.MoveCard(gs, new GameZoneId(card.OwnerNetId, card.CurrentGameZoneType), new GameZoneId(card.OwnerNetId, destination), card.InstanceId, lane, CardInsertionType.Bottom, ServerCardCatalog.MoveOperationType.Untyped);
            if (result != GameZoneOperationResult.Succeeded) throw new InvalidOperationException("Offline card move failed: " + result);
        }

        // Actions waiting on their custom-card choice (main thread).
        private static readonly List<ServerCard> pendingCustomActions = new List<ServerCard>();

        // After a custom choice resolved: a pending action finishes like any played action.
        public static void FinishCustomAction(ServerEffectUtilities.TriggerGameState gs, ServerCard owner)
        {
            if (owner == null || !pendingCustomActions.Remove(owner)) return;
            ServerEffectUtilities.TriggerWhenCardEntersBoard(gs, owner);
            if (owner.CurrentGameZoneType == GameZoneType.Board) ActionToCemetery(gs, owner);
        }

        // A resolved action goes to its owner's discard pile. The native move sends no history, so the client kept the card
        // in its play zone and its discard pile view never listed actions (owner 2026-11-06; Reborn's client is told:
        // ADD_TO_DISCARD, capture 2026-11-05). The same instance stays in the cemetery (Betray replays it from there), so the
        // client gets MOVE_CARDS to the cemetery, which recycles its play-zone card and adds it to the discard list.
        public static void ActionToCemetery(ServerEffectUtilities.TriggerGameState gs, ServerCard card)
        {
            ServerCardCatalog.MoveToCemetery(gs, card, ServerCardCatalog.MoveOperationType.Untyped);
            if (gs.Log == null || card.CurrentGameZoneType != GameZoneType.Cemetery) return;
            HistoryItem item = new HistoryItem { Type = HistoryType.ActionMoveCards, ActorCardNetUid = card.NetUid.Id, AssetHash = -1 };
            item.AffectedCardsArray.Add(card.HistoryCreate_FullStateCard(gs.CardCatalog));
            gs.Log.AddInstant(item);
        }

        private static ServerEffectUtilities.TriggerContext PlayContext(ServerCard card, PlayCardAbility ability)
        {
            TriggeredEffect effect = new TriggeredEffect();
            effect.TriggerType = EffectTriggerType.ActionEffect;
            effect.EffectGroup = ability.EffectGroup;
            effect.CustomUiType = ability.CustomUiType;
            effect.CustomUiEffectGroups = ability.CustomUiEffectGroups;
            effect.PostCustomUiEffectGroup = ability.PostCustomUiEffectGroup;
            effect.TargetSelectors = ability.TargetSelectors;
            return new ServerEffectUtilities.TriggerContext { EffectOwner = card, TriggeredEffect = effect, TriggerEvent = new ServerEffectUtilities.TriggerEvent(EffectTriggerType.ActionEffect), ActingLaneIndex = card.CurrentLaneIndex, ActingLaneIndexType = (int)card.CurrentLaneType };
        }

        // Play decisions the server rejects are logged with the check that failed (numbered in ProcessPlayCard order).
        public static string LastPlayRejection;
        private static Simulation.DecisionStatus Reject(string check, Decision d)
        {
            LastPlayRejection = "check " + check + " actor " + (d == null ? 0 : d.ActorCardNetUid) + " lane " + (d == null ? BoardLaneTarget.None : d.LaneTarget);
            UnityEngine.Debug.LogWarning("OFFLINE PLAY REJECTED: " + LastPlayRejection);
            return Simulation.DecisionStatus.Error;
        }

        public static Simulation.DecisionStatus ProcessPlayCard(Simulation s, TurnState t, Decision d)
        {
            if (t.CurrentSequence.Stage != 0) return Simulation.DecisionStatus.Complete;
            PlayerState p = s.Players[t.ActivePlayerIndex];
            if (d == null || d.DiscardCardNetUids.Count > 1) return Reject("1", d);
            ServerCard card = Card(s, d.ActorCardNetUid), target = Card(s, d.TargetCardNetUid), equip = Card(s, d.EquipCardNetUid);
            ServerCard discard = d.DiscardCardNetUids.Count == 0 ? null : Card(s, d.DiscardCardNetUids[0]);
            if (card == null || card.OwnerNetId != p.NetId || !PlayCardHandler.PlayCardIsValid(s, p, card, target, equip, discard, d)) return Reject("2", d);
            GameZone.Position position;
            if (!CommandRestoration.TryGetPlayPosition(s.CardCatalog, p, card, d, out position)) return Reject("3", d);
            Card definition = GameManager.Instance.GetCard(card.CardId);
            int level = p.GetActionLevel(card.CardId);
            PlayCardAbility ability = definition.ActionPlayInformation.Count > 0 ? definition.ActionPlayInformation[level] : null;
            bool customAction = ability != null && ability.CustomUiType != CustomCardUiType.None;
            bool mobilize = card.HasSubtype(CardSubtype.Item) && equip == null;
            if (mobilize && (card.QueryAttribute(AttributeType.Mobilize).Value <= 0 || !OfflineMobilize.LaneTargets(s.CardCatalog, card, p).Contains(d.LaneTarget)))
                return Reject("4", d);
            bool fromBetray = card.CurrentGameZoneType == GameZoneType.Betray;
            if (OfflineBetrayRestoration.HasPending(s) != fromBetray) return Reject("5", d);
            bool free = card.CurrentGameZoneType == GameZoneType.Prophecy || fromBetray;
            int cost = free ? 0 : card.QueryAttribute(AttributeType.ManaCost).Value;
            if (p.QueryAttribute(AttributeType.Mana).Value < cost) return Reject("6", d);
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            if (mobilize) equip = OfflineMobilize.SummonRecruit(s, gs, p, card, d.LaneTarget);
            CatalogMovementRestoration.ValidateTransferDependencies(gs, card,
                s.CardCatalog.FindGameZone(card.OwnerNetId, card.CurrentGameZoneName),
                s.CardCatalog.FindGameZone(card.OwnerNetId, card.FindGameZoneName(GameZoneType.Board)),
                card.OwnerNetId, d.LaneIndex, ServerCardCatalog.MoveOperationType.Untyped);
            if (discard != null)
            {
                GameZone deathrattle = s.CardCatalog.FindGameZone(discard.OwnerNetId, discard.FindGameZoneName(GameZoneType.Deathrattle));
                GameZone cemetery = s.CardCatalog.FindGameZone(discard.OwnerNetId, GameZoneName.Cemetery);
                if (!(deathrattle is DynamicGameZone) || !(cemetery is DynamicGameZone)
                    || deathrattle.LaneList.Count == 0 || cemetery.LaneList.Count == 0
                    || deathrattle.LaneList[0].IsLaneFull() || cemetery.LaneList[0].IsLaneFull())
                    throw new NotSupportedException("Offline sacrifice requires available native deathrattle/cemetery destinations.");
                CatalogMovementRestoration.ValidateTransferDependencies(gs, discard,
                    s.CardCatalog.FindGameZone(discard.OwnerNetId, discard.CurrentGameZoneName), deathrattle,
                    discard.OwnerNetId, 0, ServerCardCatalog.MoveOperationType.Untyped);
            }
            // Resolve original placement FX before payment/sacrifice/history.
            // Native SequenceCardPlay requires this to complete its animation.
            int playAssetHash = NativeHistoryFx.PlayHash(card);
            Begin(s, t, HistoryType.EventPlayCard, card, target, playAssetHash);
            if (discard != null) { discard.Sacrificed = true; DiscardCards(gs, new List<ServerCard> { discard }, -1, false); }
            s.PayCastingCost(gs, cost, p.NetId);
            card.AddToJournal(CardJournal.Played);
            if (free && !fromBetray) card.AddToJournal(CardJournal.PlayedAsProphecy);
            p.NumCardsPlayedThisTurn++;
            p.LastCardPlayedId = card.CardId;
            p.TookAction = true;
            if (card.FindGameZoneName(GameZoneType.Board) == GameZoneName.CreatureBoard)
            {
                GameZoneOperationResult move = ServerCardCatalog.MoveCard(gs, new GameZoneId(card.OwnerNetId, card.CurrentGameZoneType),
                    new GameZoneId(card.OwnerNetId, GameZoneType.Board), card.InstanceId, position, ServerCardCatalog.MoveOperationType.Untyped);
                if (move != GameZoneOperationResult.Succeeded) throw new InvalidOperationException("Offline selected-slot play move failed: " + move);
            }
            else Move(gs, card, GameZoneType.Board, d.LaneIndex);
            HistoryItem play = new HistoryItem { Type = HistoryType.ActionPlayToBoard, ActorCardNetUid = card.NetUid.Id, TargetCardNetUid = d.TargetCardNetUid, EquipCardNetUid = d.EquipCardNetUid, ActingPlayerInstanceId = p.NetId.Id, AssetHash = playAssetHash };
            play.AffectedCardsArray.Add(card.HistoryCreate_FullStateCard(s.CardCatalog));
            s.HistoryLog.AddInstant(play);
            if (equip != null) CardActions.Equip.StaticExecute(gs, card, equip, card.FindOverrideOrDefaultAssetHash(CardFxAction.Equip));
            if (equip != null) OfflineItemTriggers.AfterEquipPlay(gs, card, equip, s.Players);
            ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardIsPlayed, card, s.Players));
            // Quest counting for plays/summons lived in the stripped server play path (native counting helper).
            ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.PlayCount, gs, card, card, 1);
            if (card.HasSubtype(CardSubtype.Creature)) ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.SummonCount, gs, card, card, 1);
            if (free && !fromBetray) ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.ProphecySummonCount, gs, card, card, 1);
            ServerCard outerPlay = OfflineSummonRestoration.BeginPlay(card); // the play sequence fires this card's Summon itself
            try
            {
                if (customAction)
                {
                    // Action with a custom-card choice: the main group builds the choices now; the choice phase is
                    // queued as an already-run context so the effect pump asks for the CustomChoice decision and the
                    // native queue executor runs the chosen group and the post group. The action stays on the board
                    // until then (original choice groups require Self IsIn Board) and goes to the cemetery afterwards.
                    ServerEffectUtilities.TriggerContext context = PlayContext(card, ability);
                    ServerEffectUtilities.ExecuteOnPlayCardEffects(gs, context, d, null);
                    context.DidMainEffectsRun = true;
                    t.EffectTriggerQueues.Active.Enqueue(context);
                    pendingCustomActions.Add(card);
                    s.HistoryLog.AddInstant(new HistoryItem { Type = HistoryType.ActionStartCustomUi, ActorCardNetUid = card.NetUid.Id,
                        ActingPlayerInstanceId = card.OwnerNetId.Id, AssetHash = -1 });
                }
                else if (ability != null) ServerEffectUtilities.ExecuteOnPlayCardEffects(gs, PlayContext(card, ability), d, null);
                else ServerEffectUtilities.TriggerWhenCardEntersBoard(gs, card);
            }
            finally { OfflineSummonRestoration.EndPlay(outerPlay); }
            if (card.HasSubtype(CardSubtype.Creature) || card.HasSubtype(CardSubtype.Support))
                ServerEffectUtilities.TriggerDirectEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, card, s.Players));
            OfflineTurnMechanics.AfterPlay(gs, p, card, s.Players);
            if (card.HasSubtype(CardSubtype.Spell) && !pendingCustomActions.Contains(card)) ActionToCemetery(gs, card);
            OfflineBetrayRestoration.AfterPlay(s, t, card, fromBetray);
            s.AddBonusMilliseconds(Simulation.BonusTimeForCardPlayMilliseconds);
            return Simulation.DecisionStatus.Processing;
        }

        public static Simulation.DecisionStatus ProcessAttack(Simulation s, TurnState t, Decision d)
        {
            ServerCard attacker = Card(s, d.ActorCardNetUid), defender = Card(s, d.TargetCardNetUid);
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            if (t.CurrentSequence.Stage == 0)
            {
                if (attacker == null || defender == null || attacker.OwnerNetId != s.Players[t.ActivePlayerIndex].NetId || !attacker.CanCardAttack(false) || !PlayCardHandler.AttackIsValid(s, s.Players[t.ActivePlayerIndex], d)) return Simulation.DecisionStatus.Error;
                Begin(s, t, HistoryType.EventAttack, attacker, defender);
                // "May move to attack creatures in the other lane": the attacker moves into the defender's lane first.
                if (attacker.QueryAttribute(AttributeType.MoveToAttackOtherLane).Value > 0 && defender.HasSubtype(CardSubtype.Creature)
                    && defender.CurrentGameZoneName == GameZoneName.CreatureBoard && defender.CurrentLaneIndex != attacker.CurrentLaneIndex)
                {
                    // Same native relocation as CardActions.ChangeLane (relative other lane, then MoveCard within the board).
                    InsertPosition to = new InsertPosition { Type = InsertPosition.LaneChoiceType.Relative, Lhs = CardRole.Self, Comparison = InsertPosition.PositionComparison.OtherLane };
                    GameZone.Position position = to.EvaluateValue(new CardConditionalArguments(s.CardCatalog, attacker, null, null, null, null, LaneIdentifier.GetInvalid()),
                        attacker.OwnerNetId, attacker.CurrentGameZoneName, LaneIdentifier.GetInvalid(), s.RandomGenerator);
                    ServerCardCatalog.MoveCard(gs, new GameZoneId(attacker.OwnerNetId, attacker.CurrentGameZoneType), new GameZoneId(attacker.OwnerNetId, GameZoneType.Board),
                        attacker.InstanceId, position, ServerCardCatalog.MoveOperationType.Untyped);
                    if (attacker.CurrentLaneIndex != defender.CurrentLaneIndex) throw new InvalidOperationException("Move-to-attack relocation failed for card " + attacker.CardId + ".");
                }
                attacker.IncrementActionCount();
                attacker.RemoveCover(gs);
                attacker.AttackWasCancelled = false;
                s.HistoryLog.AddInstant(new HistoryItem { Type = HistoryType.ActionPrepAttack, ActorCardNetUid = attacker.NetUid.Id, TargetCardNetUid = defender.NetUid.Id, AssetHash = -1 });
                ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardPreparesAttack, attacker, defender, s.Players));
                return Simulation.DecisionStatus.Processing;
            }
            if (t.CurrentSequence.Stage == 1)
            {
                if (attacker == null || defender == null || attacker.IsDying() || defender.IsDying() || attacker.AttackWasCancelled) return Simulation.DecisionStatus.Complete;
                ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardAttacks, attacker, defender, s.Players));
                ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardDefends, defender, attacker, s.Players));
                // Let the lifecycle resolve queued attack/defend triggers and their decisions before combat.
                // E.g. Ageless Automaton's attack-triggered power and Breakthrough must affect this fight.
                return Simulation.DecisionStatus.Processing;
            }
            if (t.CurrentSequence.Stage == 2)
            {
                if (attacker == null || defender == null || attacker.IsDying() || defender.IsDying() || attacker.AttackWasCancelled
                    || attacker.CurrentGameZoneType != GameZoneType.Board || defender.CurrentGameZoneType != GameZoneType.Board)
                    return Simulation.DecisionStatus.Complete;
                s.HistoryLog.AddInstant(new HistoryItem { Type = HistoryType.ActionDoAttack, ActorCardNetUid = attacker.NetUid.Id, TargetCardNetUid = defender.NetUid.Id, AssetHash = -1 });
                // Reconstructed server bookkeeping: no surviving client code records CardJournal.Attacked or raises
                // EffectTriggerType.Veteran. Record the attack only once it actually resolves (cancelled attacks do not count).
                attacker.AddToJournal(CardJournal.Attacked);
                CardActions.Fight.StaticExecute(gs, attacker, new List<ServerCard> { defender }, -1);
                // Quest counting for attacks (stripped server attack path; native counting helper).
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.AttackCount, gs, attacker, defender, 1);
                if (!defender.HasSubtype(CardSubtype.Avatar) && defender.IsDying()) ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.AttackAndKilledCount, gs, attacker, defender, 1);
                s.Players[t.ActivePlayerIndex].TookAction = true;
                s.AddBonusMilliseconds(Simulation.BonusTimeForAttackMilliseconds);
                return Simulation.DecisionStatus.Processing;
            }
            if (attacker != null && defender != null)
            {
                ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardAttackComplete, attacker, defender, s.Players));
                ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardDefendComplete, defender, attacker, s.Players));
                // UESP Legends:Veteran: "After this creature first attacks and survives". Raised as the native direct
                // Veteran trigger once, after the first resolved attack, only while the attacker is alive on the board.
                if (attacker.QueryGameJournalCount(CardJournal.Attacked) == 1 && !attacker.IsDying() && attacker.CurrentGameZoneType == GameZoneType.Board)
                    ServerEffectUtilities.TriggerDirectEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Veteran, attacker, defender, s.Players));
                s.HistoryLog.AddInstant(new HistoryItem { Type = HistoryType.ActionEndAttack, ActorCardNetUid = attacker.NetUid.Id, TargetCardNetUid = defender.NetUid.Id, AssetHash = -1 });
            }
            return Simulation.DecisionStatus.Complete;
        }

        public static Simulation.DecisionStatus ProcessActivate(Simulation s, TurnState t, Decision d)
        {
            if (t.CurrentSequence.Stage != 0) return Simulation.DecisionStatus.Complete;
            ServerCard card = Card(s, d.ActorCardNetUid);
            if (card == null || card.OwnerNetId != s.Players[t.ActivePlayerIndex].NetId || !PlayCardHandler.ActivateCardIsValid(s, s.Players[t.ActivePlayerIndex], d)) return Simulation.DecisionStatus.Error;
            Begin(s, t, HistoryType.EventActivatedAbility, card, Card(s, d.TargetCardNetUid));
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.BeforeActivatedAbility, card, s.Players));
            foreach (AbilityGroup group in card.ActivatedAbility.AbilityGroups)
            {
                TriggeredEffect effect = new TriggeredEffect { EffectGroup = group.EffectGroup, TriggerType = EffectTriggerType.AbilityEffect };
                bool choice = group.CustomUiType != CustomCardUiType.None;
                if (choice)
                {   // A custom-card choice (College of Winterhold: pick one of three actions), as for played actions: the main group
                    // deals the choices, the choice step is queued as an already-run context so the effect pump asks for the
                    // CustomChoice decision, and the native executor runs the chosen group and the post group.
                    effect.CustomUiType = group.CustomUiType;
                    effect.CustomUiEffectGroups = group.CustomUiEffectGroups;
                    effect.PostCustomUiEffectGroup = group.PostCustomUiEffectGroup;
                    effect.TargetSelectors = card.ActivatedAbility.TargetSelectors;
                }
                ServerEffectUtilities.TriggerContext context = new ServerEffectUtilities.TriggerContext { EffectOwner = card, TriggeredEffect = effect, TriggerEvent = new ServerEffectUtilities.TriggerEvent(EffectTriggerType.AbilityEffect), ActingLaneIndex = card.CurrentLaneIndex, ActingLaneIndexType = (int)card.CurrentLaneType };
                ServerEffectUtilities.ExecuteOnActivateCardEffects(gs, context, d, null);
                if (choice)
                {
                    context.DidMainEffectsRun = true;
                    t.EffectTriggerQueues.Active.Enqueue(context);
                    s.HistoryLog.AddInstant(new HistoryItem { Type = HistoryType.ActionStartCustomUi, ActorCardNetUid = card.NetUid.Id,
                        ActingPlayerInstanceId = card.OwnerNetId.Id, AssetHash = -1 });
                }
            }
            card.IncrementAbilityUseCount();
            card.IncrementActivationCount();
            s.Players[t.ActivePlayerIndex].TookAction = true;
            ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.AfterActivatedAbility, card, s.Players));
            return Simulation.DecisionStatus.Processing;
        }

        public static Simulation.DecisionStatus ProcessBetray(Simulation s, TurnState t, Decision d)
        { return OfflineBetrayRestoration.ProcessBetray(s, t, d); }

        public static void GetPlayerDecision(Simulation s, TurnState t)
        {
            if (t.CurrentCommandItem == null || t.CurrentCommandItem.CommandList == null) return;
            PlayerState p = s.Players[t.ActivePlayerIndex];
            Decision decision;
            if (p.IsHuman || s._disableAiDecisions)
            {
                PlayerServiceClient client = s.PlayerServiceClients[t.ActivePlayerIndex];
                decision = client == null ? null : client.PopNextDecision();
            }
            else decision = StrategicDecisionRestoration.ChooseDecision(s, t.CurrentCommandItem.CommandList);
            if (decision == null) return;
            bool fromClient = p.IsHuman || s._disableAiDecisions;
            if (fromClient) NormalizeInstantPlay(decision, t.CurrentCommandItem.CommandList);
            if (p.IsHuman) LogClientDecision(s, t, decision);
            if (!CommandRestoration.ValidateDecision(decision, t.CurrentCommandItem.CommandList, s.CardCatalog, p))
            {
                // A player's decision for an older or changed move list (a cheat, a reaction or a mod replaced the list
                // after it was sent): the client is waiting with the card lifted. Send the current list again so it
                // re-syncs instead of waiting forever (owner report 2026-11-05: floating cards, End Turn did nothing).
                if (p.IsHuman || s._disableAiDecisions) ResendMoves(s, t, decision);   // a decision from the client
                return;
            }
            Simulation.SelectDecision(t, decision);
        }

        // Every decision the player's client sends, in the game log (owner reports of stuck plays, 2026-11-05).
        private static void LogClientDecision(Simulation s, TurnState t, Decision d)
        {
            try
            {
                ServerCard actor = d.ActorCardNetUid > 0 ? s.CardCatalog.FindServerCard(new CardNetUid(d.ActorCardNetUid)) : null;
                ServerCard target = d.TargetCardNetUid > 0 ? s.CardCatalog.FindServerCard(new CardNetUid(d.TargetCardNetUid)) : null;
                UnityEngine.Debug.Log("OFFLINE DECISION: " + d.Type + " actor " + d.ActorCardNetUid + (actor == null ? "" : "=" + actor.GetCardName() + "@" + actor.CurrentGameZoneName)
                    + " target " + d.TargetCardNetUid + (target == null ? "" : "=" + target.GetCardName() + "@" + target.CurrentGameZoneName)
                    + " lane " + d.LaneIndex + " slot " + d.PositionIndex + " discard " + d.DiscardCardNetUids.Count + " list " + d.CommandUid
                    + " (current " + t.CurrentCommandItem.CommandList.ListUid + ", depth " + s._turnStateStack.Count + ")");
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("OFFLINE DECISION: not described: " + e.Message); }
        }

        // The client sends lane and slot -1 (ServerCard.InvalidPositionIndex) for a card that has no board position: actions
        // with or without a target (GameNetworkManager.PlayInstantCardWithTarget / NoTarget). The engine's "no position" is
        // 0/0, so such a play was rejected and every action played from the client stayed floating (owner, 2026-11-05/06:
        // Rapid Shot on an enemy creature; the first "floating cards" report).
        public static void NormalizeInstantPlay(Decision d, CardCommandList list)
        {
            if (d == null || list == null || d.Type != CcgRpc.DecisionType.Play) return;
            if (d.LaneIndex != ServerCard.InvalidPositionIndex || d.PositionIndex != ServerCard.InvalidPositionIndex) return;
            foreach (CardCommandState c in list.ValidCommands)
                if (c.CardNetUid == d.ActorCardNetUid && (c.ValidCardCommandMask & (int)CardCommand.PlayToPosition) == 0) { d.LaneIndex = 0; d.PositionIndex = 0; return; }
        }

        public static int Resends;
        public static void ResendMoves(Simulation s, TurnState t, Decision decision)
        {
            Resends++;
            UnityEngine.Debug.LogWarning("OFFLINE DECISION RESYNC: " + decision.Type + " for list " + decision.CommandUid + " (current "
                + t.CurrentCommandItem.CommandList.ListUid + ") not accepted; current moves sent again.");
            s.SendSequenceHistoryToClients();
        }

        // A deterministic testing opponent; original strategic AI is not present.
        public static Decision ChooseDecision(Simulation s, CardCommandList commands)
        {
            Decision cemeteryChoice = OfflineConsumeChoices.Choose(commands);
            if (cemeteryChoice != null) return cemeteryChoice;
            foreach (CardCommandState c in commands.ValidCommands)
            {
                // Triggered target selection uses the native PLAY decision type.
                // Select one offered target; its effect executes through the
                // child turn's TriggerDecision, rather than ProcessPlayCard.
                if ((c.ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0)
                {
                    if (c.PlayTargets.Count > 0)
                        return NewDecision(commands, c, CcgRpc.DecisionType.Play, c.PlayTargets[0]);
                    if (c.LaneTargets.Count > 0)
                    {
                        Decision choice = NewDecision(commands, c, CcgRpc.DecisionType.Play, CardNetUid.GetInvalid().Id);
                        choice.LaneTarget = c.LaneTargets[0];
                        return choice;
                    }
                    throw new NotSupportedException("Offline effect target command has no offered card or lane target.");
                }
                if ((c.ValidCardCommandMask & (int)CardCommand.Attack) != 0 && c.AttackTargets.Count > 0)
                    return NewDecision(commands, c, CcgRpc.DecisionType.Attack, c.AttackTargets[0]);
                if ((c.ValidCardCommandMask & ((int)CardCommand.PlayToPosition | (int)CardCommand.PlayPositionless | (int)CardCommand.PlayToEquip | (int)CardCommand.PlayToLane)) != 0)
                {
                    Decision d = NewDecision(commands, c, CcgRpc.DecisionType.Play, c.PlayTargets.Count > 0 ? c.PlayTargets[0] : CardNetUid.GetInvalid().Id);
                    d.EquipCardNetUid = c.EquipTargets.Count > 0 ? c.EquipTargets[0] : CardNetUid.GetInvalid().Id;
                    if (c.ValidBoardLanes != 0) for (int lane = 0; lane < 4; lane++) if ((c.ValidBoardLanes & (1 << lane)) != 0) { d.LaneIndex = lane; break; }
                    if (c.LaneTargets.Count > 0) d.LaneTarget = c.LaneTargets[0];
                    return d;
                }
                if ((c.ValidCardCommandMask & (int)CardCommand.ActiveAbility) != 0)
                {
                    Decision activation = NewDecision(commands, c, CcgRpc.DecisionType.Activate, c.ActiveAbilityTargets.Count > 0 ? c.ActiveAbilityTargets[0] : CardNetUid.GetInvalid().Id);
                    if (c.LaneTargets.Count > 0) activation.LaneTarget = c.LaneTargets[0];
                    return activation;
                }
                if ((c.ValidCardCommandMask & (int)CardCommand.PlayToHand) != 0)
                    return NewDecision(commands, c, CcgRpc.DecisionType.ToHand, CardNetUid.GetInvalid().Id);
                if (c.NumActionChoices > 0) throw new NotSupportedException("Offline custom-choice decision orchestration is not restored.");
            }
            if (commands.MustSelectCommand)
                throw new NotSupportedException("No restored AI selector for required offline command.");
            return new Decision { CommandUid = commands.ListUid, Type = CcgRpc.DecisionType.NoCommand,
                ActorCardNetUid = CardNetUid.GetInvalid().Id, TargetCardNetUid = CardNetUid.GetInvalid().Id, EquipCardNetUid = CardNetUid.GetInvalid().Id };
        }

        private static Decision NewDecision(CardCommandList list, CardCommandState card, CcgRpc.DecisionType type, int target)
        { return new Decision { CommandUid = list.ListUid, Type = type, ActorCardNetUid = card.CardNetUid,
            TargetCardNetUid = target, EquipCardNetUid = CardNetUid.GetInvalid().Id }; }

        public static void CheckForAutoDecision(Simulation s, TurnState t, ServerEffectUtilities.TriggerContext context)
        {
            if (t.CurrentCommandItem == null) return;
            CardCommandList list = t.CurrentCommandItem.CommandList;
            if (list.ValidCommands.Count == 0 && !list.MustSelectCommand)
                Simulation.SelectDecision(t, new Decision { CommandUid = list.ListUid, Type = CcgRpc.DecisionType.NoCommand,
                    ActorCardNetUid = CardNetUid.GetInvalid().Id, TargetCardNetUid = CardNetUid.GetInvalid().Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
        }

        public static TurnState CreateAndPushDecisionState(Simulation s, TurnState parent, ServerEffectUtilities.TriggerContext context)
        {
            PlayerUniqueId owner = context.EffectOwner == null ? s.Players[parent.ActivePlayerIndex].NetId : context.EffectOwner.OwnerNetId;
            int index = s.Players.FindIndex(delegate(PlayerState p) { return p.NetId == owner; });
            if (context.TriggeredEffect.OpponentChooses) index = 1 - index;
            TurnState child = new TurnState { ActivePlayerIndex = index, TurnPlayerIndex = parent.TurnPlayerIndex, Phase = TurnPhase.Interrupt, CustomUICards = parent.CustomUICards };
            if (parent.EffectTriggerQueues.Active.Count > 0) parent.EffectTriggerQueues.Active.Dequeue();
            child.EffectTriggerQueues.Active.Enqueue(context);
            child.CurrentCommandItem = s.CardCatalog.GenerateEffectCommandItem(context, s.Players[index], s.Players[1-index], s.Players, child.CustomUICards);
            if (child.CurrentCommandItem == null) throw new InvalidOperationException("Offline effect command generation returned no command.");
            s.PushTurnState(child);
            return child;
        }

        public static void DiscardCards(ServerEffectUtilities.TriggerGameState gs, List<ServerCard> discards, int assetHash, bool fullHand)
        {
            foreach (ServerCard card in new List<ServerCard>(discards))
            {
                if (card == null || card.CurrentGameZoneType == GameZoneType.Cemetery || card.CurrentGameZoneType == GameZoneType.Deathrattle) continue;
                bool death = card.CurrentGameZoneType == GameZoneType.Board;
                bool avatar = card.HasSubtype(CardSubtype.Avatar);
                if (avatar)
                {
                    if (!death || !card.IsDying()) continue; // Native Immortal prevents health-based death.
                    if (card.TriggeredEffects.Exists(delegate(TriggeredEffect e) { return e.TriggerType == EffectTriggerType.LastGaspTrigger; }))
                        throw new NotSupportedException("Offline avatar Last Gasp/deathrattle behavior is not restored.");
                }
                // The client has no ActionDeath sequence. Original/Reborn protocol for a
                // creature board death (2026-10-19 capture): ADD_TO_DISCARD (the low-level
                // Deathrattle transfer) and then MOVE_CARDS of the dying instance into the
                // Deathrattle zone, which is what removes its board view. Avatar deaths keep
                // ActionDeath and end the match.
                bool deferMove = death && !avatar;
                HistoryItem item = new HistoryItem { Type = death && avatar ? HistoryType.ActionDeath : HistoryType.ActionMoveCards, ActorCardNetUid = card.NetUid.Id, AssetHash = assetHash };
                if (gs.Log != null && !deferMove) gs.Log.AddInstant(item);
                if (death)
                {
                    if (card.HasSubtype(CardSubtype.Creature))
                    {
                        PlayerState owner = gs.Players.Find(delegate(PlayerState p) { return p.NetId == card.OwnerNetId; });
                        if (owner != null) owner.ModifyAttribute(AttributeType.CreaturesDeadThisTurn, 1);
                    }
                    ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardDies, card, card.KilledByCard, gs.Players));
                    if (!avatar && card.KilledByCard != null) ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.DestroyCount, gs, card.KilledByCard, card, 1);
                    if (avatar)
                    {
                        // Native Avatar definition maps this to AvatarCemetery.
                        // Preserve its original instance/health rather than invoking
                        // the creature-only Deathrattle/Cemetery cloning helper.
                        ServerCardCatalog.MoveToCemetery(gs, card, ServerCardCatalog.MoveOperationType.Untyped);
                        if (card.CurrentGameZoneName != card.FindGameZoneName(GameZoneType.Cemetery))
                            throw new InvalidOperationException("Dying avatar failed to enter its configured cemetery.");
                    }
                    else
                    {
                        ServerCardCatalog.MoveToDeathrattle(gs, card, ServerCardCatalog.MoveOperationType.Untyped);
                        // Attach the moved card before logging: a Last Gasp below may send history.
                        item.AffectedCardsArray.Add(card.HistoryCreate_FullStateCard(gs.CardCatalog));
                        if (gs.Log != null) gs.Log.AddInstant(item);
                        // Item Last Gasp belongs to the wielder's death, forwarded by OfflineItemTriggers.
                        // Item disposal also runs here (including after silence); firing it again duplicates that ability.
                        if (!card.HasSubtype(CardSubtype.Item))
                            ServerEffectUtilities.TriggerDirectEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.LastGaspTrigger, card, gs.Players));
                    }
                }
                else ServerCardCatalog.MoveToCemetery(gs, card, ServerCardCatalog.MoveOperationType.Untyped);
                if (!deferMove) item.AffectedCardsArray.Add(card.HistoryCreate_FullStateCard(gs.CardCatalog));
                // A board death enters Deathrattle, while a non-board discard moves
                // directly to Cemetery. The low-level transfer owns the latter's
                // discard event; only the logical death discard is emitted here.
                if (death && !avatar) ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardDiscarded, card, gs.Players));
            }
        }

        public static void ProcessDiscards(Simulation s)
        {
            if (s._turnStateStack.Count == 0) return;
            // Continuous card auras are recomputed before deaths are gathered, so removing an aura's health can kill.
            OfflineAuraRestoration.Apply(State(s, s._turnStateStack.Peek()));
            List<ServerCard> dead = s.CardCatalog.GatherCardList(delegate(ServerCard card) { return card.ShouldBeDiscarded(s.CardCatalog); });
            if (dead.Count > 0) DiscardCards(State(s, s._turnStateStack.Peek()), dead, -1, false);
        }

        public static void CheckForRuneBreaks(Simulation s)
        {
            if (s._turnStateStack.Count == 0) return;
            TurnState t = s._turnStateStack.Peek();
            ServerEffectUtilities.TriggerGameState gs = State(s, t);
            foreach (PlayerState player in s.Players)
            {
                ServerCard avatar = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard card) { return card.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarBoard);
                if (avatar == null || avatar.CurrentHealth <= 0) continue;
                int runeHealth = player.QueryAttribute(AttributeType.RuneHealth).Value;
                if (runeHealth <= 0) continue;
                while (player.QueryAttribute(AttributeType.NumRunes).Value > 0 && avatar.CurrentHealth <= player.QueryAttribute(AttributeType.NumRunes).Value * runeHealth)
                {
                    RuneProphecyRestoration.ValidateRunePlayer(player);
                    s.HistoryLog.StartEvent(HistoryType.EventRuneBreak, player.NetId, avatar.NetUid, CardNetUid.GetInvalid(), -1);
                    PlayerActions.SetAttribute.StaticExecute(s.HistoryLog, player, AttributeType.NumRunes, player.QueryAttribute(AttributeType.NumRunes).Value - 1);
                    PlayerActions.SetAttribute.StaticExecute(s.HistoryLog, player, AttributeType.NumRunesLost, player.QueryAttribute(AttributeType.NumRunesLost).Value + 1);
                    OfflineTitleFeats.RuneBroken(player.NetId.Id); // The Runebreaker: one count per actual break
                    ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardBreaksRune, avatar, avatar.LastDamagedByCard, s.Players));
                    // Beast Form: a broken rune (either player's) turns werewolves on the board into their beast form, as the
                    // native DestroyRunes action fires it (PlayerActions) and Reborn does on a rune break (capture 2026-11-05:
                    // CHANGE_CARD right after the rune trigger). Owner 2026-11-06: werewolves never transformed.
                    ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenBeastFormChange, avatar, avatar, s.Players));
                    foreach (ServerCard card in s.CardCatalog.GatherDynamicCardList()) card.CullExpiredEnchantments(CullEnchantmentReason.AfterRuneBreak, gs);
                    if (player.QueryAttribute(AttributeType.BlockDrawsFromRuneBreak).Value <= 0)
                        t.EffectTriggerQueues.CardDrawInterrupt.Add(new ServerEffectUtilities.CardInterruptContext { PlayerId = player.NetId, FromRuneBreak = true, RequireProphecyToPlay = true, IgnoreManaCostRestrictions = true, AssetHash = -1 });
                    s.HistoryLog.EndEvent();
                }
            }
        }

        public static Simulation.SequenceStatus ProcessCardInterrupt(Simulation s, TurnState t)
        {
            return RuneProphecyRestoration.ProcessCardInterrupt(s, t);
        }

        public static bool ProcessStartGamePrefix(Simulation __instance, TurnState __0, ref Simulation.SequenceStatus __result) { __result = ProcessStartGame(__instance, __0); return false; }
        public static bool ProcessStartTurnPrefix(Simulation __instance, TurnState __0, ref Simulation.SequenceStatus __result) { __result = ProcessStartTurn(__instance, __0); return false; }
        public static bool ProcessDecisionPrefix(Simulation __instance, TurnState __0, ref Simulation.DecisionStatus __result) { __result = ProcessDecision(__instance, __0); return false; }
        public static bool ProcessPlayCardPrefix(Simulation __instance, TurnState __0, Decision __1, ref Simulation.DecisionStatus __result) { __result = ProcessPlayCard(__instance, __0, __1); return false; }
        public static bool ProcessAttackPrefix(Simulation __instance, TurnState __0, Decision __1, ref Simulation.DecisionStatus __result) { __result = ProcessAttack(__instance, __0, __1); return false; }
        public static bool ProcessActivatePrefix(Simulation __instance, TurnState __0, Decision __1, ref Simulation.DecisionStatus __result) { __result = ProcessActivate(__instance, __0, __1); return false; }
        public static bool ProcessBetrayPrefix(Simulation __instance, TurnState __0, Decision __1, ref Simulation.DecisionStatus __result) { __result = ProcessBetray(__instance, __0, __1); return false; }
        public static bool ExecuteMulliganDecisionPrefix(Simulation __instance, PlayerState __0) { ExecuteMulliganDecision(__instance, __0); return false; }
        public static bool GetPlayerDecisionPrefix(Simulation __instance, TurnState __0) { GetPlayerDecision(__instance, __0); return false; }
        public static bool CheckForAutoDecisionPrefix(Simulation __instance, TurnState __0, ServerEffectUtilities.TriggerContext __1) { CheckForAutoDecision(__instance, __0, __1); return false; }
        public static bool CreateAndPushDecisionStatePrefix(Simulation __instance, TurnState __0, ServerEffectUtilities.TriggerContext __1, ref TurnState __result) { __result = CreateAndPushDecisionState(__instance, __0, __1); return false; }
        public static bool ProcessDiscardsPrefix(Simulation __instance) { ProcessDiscards(__instance); return false; }
        public static bool CheckForRuneBreaksPrefix(Simulation __instance) { CheckForRuneBreaks(__instance); return false; }
        public static bool ProcessCardInterruptPrefix(Simulation __instance, TurnState __0, ref Simulation.SequenceStatus __result) { __result = ProcessCardInterrupt(__instance, __0); return false; }
        public static bool DiscardCardsPrefix(ServerEffectUtilities.TriggerGameState __0, List<ServerCard> __1, int __2, bool __3) { DiscardCards(__0, __1, __2, __3); return false; }
    }
}
