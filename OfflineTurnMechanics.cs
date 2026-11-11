using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed server-side dispatch for the per-turn keyword triggers whose native EffectTriggerType exists in the
    // shipped client but whose server orchestration is absent (UESP Legends definitions):
    //   Plot      - "triggers if you have previously played another card on the same turn" (fires when the Plot card is played).
    //   Expertise - "At the end of your turn, if you played an action, item, or support this turn" (the card must be played;
    //               an item equipped by an effect does not count).
    public static class OfflineTurnMechanics
    {
        public sealed class OpponentMoreHandRequirement : IPlayerRequirement
        {
            public PlayerRole LhsPlayer { get; set; }
            public IPlayerConditionalExpression GetConditionalExpression() { return null; }
            public bool IsRequirementMet(PlayerConditionalLog log, PlayerConditionalArguments players, CardConditionalArguments cards, TriggerConditionalArguments trigger)
            {
                if (players.SelfPlayerState == null || players.OpponentPlayerState == null || players.CardCatalog == null) return false;
                return players.CardCatalog.FindCardCount(GameZoneName.Hand, players.OpponentPlayerState.NetId)
                    > players.CardCatalog.FindCardCount(GameZoneName.Hand, players.SelfPlayerState.NetId);
            }
        }

        // Counts deaths on both sides; per-owner attributes reset at every turn start.
        public sealed class TotalDeathsRequirement : IPlayerRequirement
        {
            public PlayerRole LhsPlayer { get; set; }
            public int Minimum;
            public IPlayerConditionalExpression GetConditionalExpression() { return null; }
            public bool IsRequirementMet(PlayerConditionalLog log, PlayerConditionalArguments players, CardConditionalArguments cards, TriggerConditionalArguments trigger)
            {
                if (players.SelfPlayerState == null || players.OpponentPlayerState == null) return false;
                return players.SelfPlayerState.QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value
                    + players.OpponentPlayerState.QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value >= Minimum;
            }
        }

        private static int TotalDeaths(ServerEffectUtilities.TriggerGameState gs)
        {
            int total = 0;
            foreach (PlayerState player in gs.Players) total += player.QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value;
            return total;
        }

        // Snapshot one total for one native damage action: separate per-player hits would break Ward.
        public sealed class DeathScaledDamage : ICardAction
        {
            public CardFxAction GetFxActionType() { return new CardActions.Damage().GetFxActionType(); }
            public void Execute(CardActionArguments args)
            { new CardActions.Damage { HealthLoss = new ActionValueData(TotalDeaths(args.TriggerGameState)), DamageSource = CardRole.Self }.Execute(args); }
        }

        public sealed class DeathScaledBuff : ICardAction
        {
            public Enchantment Enchantment;
            public CardFxAction GetFxActionType() { return new CardActions.StackingBuff().GetFxActionType(); }
            public void Execute(CardActionArguments args)
            {
                int deaths = TotalDeaths(args.TriggerGameState);
                if (deaths > 0) new CardActions.StackingBuff { Enchantment = Enchantment, StackCount = new ActionValueData(deaths) }.Execute(args);
            }
        }
        public sealed class EnemyRunesScaledBuff : ICardAction
        {
            public Enchantment Enchantment;
            public CardFxAction GetFxActionType() { return new CardActions.StackingBuff().GetFxActionType(); }
            public void Execute(CardActionArguments args)
            {
                ServerCard source = args.CardArguments.SelfCard;
                if (source == null) return;
                foreach (PlayerState player in args.TriggerGameState.Players)
                    if (player.NetId != source.OwnerNetId)
                    {
                        int count = player.QueryAttribute(AttributeType.NumRunesLost).Value;
                        if (count > 0) new CardActions.StackingBuff { Enchantment = Enchantment, StackCount = new ActionValueData(count) }.Execute(args);
                        return;
                    }
            }
        }
        private static readonly Dictionary<PlayerState, bool> playedNonCreature = new Dictionary<PlayerState, bool>();

        public static void StartTurn(PlayerState player) { playedNonCreature[player] = false; }

        public static bool PlayedNonCreatureThisTurn(PlayerState player)
        {
            bool value;
            return playedNonCreature.TryGetValue(player, out value) && value;
        }

        // Called from the restored play sequence after Summon, before an action moves to the discard pile.
        public static void AfterPlay(ServerEffectUtilities.TriggerGameState gs, PlayerState player, ServerCard card, List<PlayerState> players)
        {
            if (card.HasSubtype(CardSubtype.Spell) || card.HasSubtype(CardSubtype.Item) || card.HasSubtype(CardSubtype.Support))
                playedNonCreature[player] = true;
            // NumCardsPlayedThisTurn already counts this card, so another card was played earlier when it exceeds one.
            if (player.NumCardsPlayedThisTurn > 1)
            {
                ServerEffectUtilities.TriggerDirectEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Plot, card, players));
                if (card.TriggeredEffects.Exists(delegate(TriggeredEffect t) { return t.TriggerType == EffectTriggerType.Plot; }))
                    ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.PlotCount, gs, card, card, 1);
            }
        }

        // Called at the start of the restored end-turn sequence, before WhenPlayerTurnEnds.
        public static void EndTurn(ServerEffectUtilities.TriggerGameState gs, Simulation sim, PlayerState player)
        {
            if (!PlayedNonCreatureThisTurn(player)) return;
            foreach (ServerCard card in sim.CardCatalog.GatherDynamicCardList())
            {
                if (card.OwnerNetId != player.NetId || card.CurrentGameZoneType != GameZoneType.Board) continue;
                if (!card.TriggeredEffects.Exists(delegate(TriggeredEffect t) { return t.TriggerType == EffectTriggerType.Expertise; })) continue;
                ServerEffectUtilities.TriggerDirectEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Expertise, card, sim.Players));
                ActiveQuestUtilities.ExecuteCardQuestEvent(QuestGoalType.ExpertiseCount, gs, card, card, 1);
            }
        }
    }
}
