using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // New bounded strategy over restored native rules, not recovered original AI.
    // No search evaluates hidden decks, opposing hands, or consumes native RNG.
    public static class StrategicDecisionRestoration
    {
        private sealed class Candidate { public Decision Decision; public double Score; }
        private const int PlayMask = (int)CardCommand.PlayToPosition | (int)CardCommand.PlayPositionless |
            (int)CardCommand.PlayToLane | (int)CardCommand.PlayToEquip;

        public static Decision ChooseDecision(Simulation sim, CardCommandList commands)
        {
            if (sim == null || sim.CardCatalog == null || commands == null || sim._turnStateStack.Count == 0)
                throw new InvalidOperationException("Strategic decision requires a native active command context.");
            TurnState turn = sim._turnStateStack.Peek();
            PlayerState self = sim.Players[turn.ActivePlayerIndex];
            Decision cemeteryChoice = OfflineConsumeChoices.Choose(commands);
            if (cemeteryChoice != null && CommandRestoration.ValidateDecision(cemeteryChoice, commands, sim.CardCatalog, self)) return cemeteryChoice;
            PlayerState enemy = sim.Players.Find(delegate(PlayerState p) { return p.NetId != self.NetId; });
            if (enemy == null) throw new NotSupportedException("Strategy requires an ordinary two-player match.");
            List<Candidate> candidates = new List<Candidate>();
            ServerCard enemyAvatar = Avatar(sim, enemy);
            int readyFaceDamage = 0;
            foreach (CardCommandState command in commands.ValidCommands)
            {
                ServerCard actor = Find(sim, command.CardNetUid);
                if (actor != null && enemyAvatar != null && command.AttackTargets.Contains(enemyAvatar.NetUid.Id)
                    && (command.ValidCardCommandMask & (int)CardCommand.Attack) != 0
                    && CommandRestoration.CanAttack(sim.CardCatalog, self, enemy, actor)
                    && PlayCardHandler.AttackIsValid(sim, self, New(commands, command, CcgRpc.DecisionType.Attack, enemyAvatar.NetUid.Id)))
                    readyFaceDamage += PredictDamage(actor, enemyAvatar, Power(actor));
            }
            foreach (CardCommandState command in commands.ValidCommands)
            {
                int mask = command.ValidCardCommandMask;
                if (command.NumActionChoices > 0 || command.CustomCardUiEnum != 0) continue;
                ServerCard actor = Find(sim, command.CardNetUid);
                if ((mask & (int)CardCommand.BetrayChoice) != 0 && actor != null)
                {
                    // Reconstructed: decline unless replaying the action is worth more than the cheapest sacrifice.
                    Add(candidates, sim, self, commands, new Decision { CommandUid = commands.ListUid, ActorCardNetUid = command.CardNetUid, Type = CcgRpc.DecisionType.Betray }, 0.01);
                    double replay = BetrayReplayValue(sim, actor, self, enemy);
                    foreach (int uid in command.ActiveAbilityTargets)
                    {
                        ServerCard sacrifice = Find(sim, uid);
                        if (sacrifice == null) continue;
                        Decision betray = new Decision { CommandUid = commands.ListUid, ActorCardNetUid = command.CardNetUid, Type = CcgRpc.DecisionType.Betray };
                        betray.DiscardCardNetUids.Add(uid);
                        Add(candidates, sim, self, commands, betray, replay - CardValue(sacrifice) - 15);
                    }
                    continue;
                }
                if ((mask & (int)CardCommand.UseRing) != 0 && UsefulRing(sim, self, enemy))
                    Add(candidates, sim, self, commands, New(commands, command, CcgRpc.DecisionType.UseRing, 0), 12);
                if (actor == null) continue;
                if ((mask & (int)CardCommand.ChooseTarget) != 0)
                {
                    foreach (int uid in command.PlayTargets)
                    {
                        ServerCard target = Find(sim, uid);
                        if (!Visible(target, self)) continue;
                        Add(candidates, sim, self, commands, New(commands, command, CcgRpc.DecisionType.Play, uid),
                            ChoiceScore(sim, turn, actor, target, self, enemy));
                    }
                    foreach (BoardLaneTarget lane in command.LaneTargets)
                    {
                        Decision choice = New(commands, command, CcgRpc.DecisionType.Play, 0); choice.LaneTarget = lane;
                        Add(candidates, sim, self, commands, choice, 1);
                    }
                    continue;
                }
                if (actor.OwnerNetId != self.NetId) continue;
                if ((mask & (int)CardCommand.Attack) != 0 && !sim._disableAiAttacking)
                    foreach (int uid in command.AttackTargets)
                    {
                        ServerCard target = Find(sim, uid);
                        if (target == null || target.OwnerNetId != enemy.NetId || !Visible(target, self)) continue;
                        Decision attack = New(commands, command, CcgRpc.DecisionType.Attack, uid);
                        if (!CommandRestoration.CanAttack(sim.CardCatalog, self, enemy, actor)
                            || !PlayCardHandler.AttackIsValid(sim, self, attack)) continue;
                        double score = ScoreAttack(actor, target, enemyAvatar) + AttackTriggerBonus(sim, actor, target, self, enemy);
                        if (target == enemyAvatar && readyFaceDamage >= target.CurrentHealth && score > 0) score += 500000;
                        Add(candidates, sim, self, commands, attack, score);
                    }
                if ((mask & PlayMask) != 0 && SafeCard(actor, self))
                    GatherPlays(candidates, sim, turn, commands, command, actor, self, enemy);
                if ((mask & (int)CardCommand.PlayToHand) != 0)
                    Add(candidates, sim, self, commands, New(commands, command, CcgRpc.DecisionType.ToHand, 0), 1);
                if ((mask & (int)CardCommand.ActiveAbility) != 0 && actor.ActivatedAbility != null
                    && actor.ActivatedAbility.AbilityGroups.Count == 1 && actor.ActivatedAbility.AbilityGroups[0].CustomUiType == CustomCardUiType.None)
                {
                    List<int> targets = new List<int>(command.ActiveAbilityTargets);
                    if (targets.Count == 0) targets.Add(0);
                    foreach (int uid in targets)
                    {
                        ServerCard target = Find(sim, uid);
                        if (uid != 0 && !Visible(target, self)) continue;
                        Decision ability = New(commands, command, CcgRpc.DecisionType.Activate, uid);
                        if (!PlayCardHandler.ActivateCardIsValid(sim, self, ability)) continue;
                        Add(candidates, sim, self, commands, ability,
                            ScoreEffects(sim, actor, target, self, enemy, actor.ActivatedAbility.AbilityGroups[0].EffectGroup));
                    }
                }
            }
            Decision pass = new Decision { CommandUid = commands.ListUid, Type = CcgRpc.DecisionType.NoCommand };
            bool passAllowed = CommandRestoration.ValidateDecision(pass, commands, sim.CardCatalog, self);
            Candidate best = null;
            foreach (Candidate candidate in candidates)
                if ((!passAllowed || candidate.Score > 0) && (best == null || candidate.Score > best.Score
                    || (candidate.Score == best.Score && Compare(candidate.Decision, best.Decision) < 0))) best = candidate;
            if (best != null) return best.Decision;
            if (passAllowed) return pass;
            throw new NotSupportedException("Required native command has no supported strategic decision.");
        }

        private static void GatherPlays(List<Candidate> candidates, Simulation sim, TurnState turn, CardCommandList list,
            CardCommandState command, ServerCard actor, PlayerState self, PlayerState enemy)
        {
            Card card = GameManager.Instance.GetCard(actor.CardId);
            if (OriginalItemRules.IsOriginalId(actor.CardId))
            {
                GatherItemPlays(candidates, sim, turn, list, command, actor, self, enemy);
                return;
            }
            int level = self.GetActionLevel(actor.CardId);
            PlayCardAbility ability = card.ActionPlayInformation.Count > level ? card.ActionPlayInformation[level] : null;
            if (ability != null && ability.CustomUiType != CustomCardUiType.None) return;
            List<int> targets = new List<int>(command.PlayTargets);
            if (targets.Count == 0) targets.Add(0);
            List<BoardLaneTarget> laneTargets = new List<BoardLaneTarget>(command.LaneTargets);
            if (laneTargets.Count == 0) laneTargets.Add(BoardLaneTarget.None);
            GameZone board = sim.CardCatalog.FindGameZone(self.NetId, actor.FindGameZoneName(GameZoneType.Board));
            if (board == null) return;
            bool slotted = (command.ValidCardCommandMask & (int)CardCommand.PlayToPosition) != 0;
            for (int lane = 0; lane < (slotted ? board.LaneList.Count : 1); lane++)
            {
                if (slotted && (command.ValidBoardLanes & (1 << lane)) == 0) continue;
                bool full = slotted && board.LaneList[lane].IsLaneFull();
                List<int> sacrifices = new List<int>();
                if (full)
                    foreach (int uid in list.FullLaneCardUids)
                    {
                        ServerCard discard = Find(sim, uid);
                        if (discard != null && discard.OwnerNetId == self.NetId && discard.CurrentLaneIndex == lane
                            && discard.CurrentGameZoneName == board.Name && discard.QueryAttribute(AttributeType.Indestructible).Value == 0)
                            sacrifices.Add(uid);
                    }
                else sacrifices.Add(0);
                foreach (int uid in targets)
                {
                    ServerCard target = Find(sim, uid);
                    if (uid != 0 && !Visible(target, self)) continue;
                    foreach (BoardLaneTarget laneTarget in laneTargets)
                        foreach (int discardUid in sacrifices)
                            for (int position = 0; position <= (slotted ? board.LaneList[lane].GetCurrentLength() : 0); position++)
                            {
                                Decision play = New(list, command, CcgRpc.DecisionType.Play, uid);
                                play.LaneIndex = lane; play.PositionIndex = position; play.LaneTarget = laneTarget;
                                if (discardUid != 0) play.DiscardCardNetUids.Add(discardUid);
                                ServerCard discard = Find(sim, discardUid);
                                if (!CommandRestoration.ValidateDecision(play, list, sim.CardCatalog, self)
                                    || !PlayCardHandler.PlayCardIsValid(sim, self, actor, target, null, discard, play)) continue;
                                GameZone.Position nativePosition;
                                if (!CommandRestoration.TryGetPlayPosition(sim.CardCatalog, self, actor, play, out nativePosition)) continue;
                                try
                                {
                                    ServerEffectUtilities.TriggerGameState state = new ServerEffectUtilities.TriggerGameState(sim.HistoryLog, sim.RandomGenerator,
                                        sim.CardCatalog, sim.Players, turn.EffectTriggerQueues, sim.ActiveQuests, turn.CustomUICards, sim._scenarioId);
                                    CatalogMovementRestoration.ValidateTransferDependencies(state, actor,
                                        sim.CardCatalog.FindGameZone(actor.OwnerNetId, actor.CurrentGameZoneName), board,
                                        self.NetId, lane, ServerCardCatalog.MoveOperationType.Untyped);
                                    if (discard != null)
                                    {
                                        GameZone deathrattle = sim.CardCatalog.FindGameZone(self.NetId, discard.FindGameZoneName(GameZoneType.Deathrattle));
                                        GameZone cemetery = sim.CardCatalog.FindGameZone(self.NetId, GameZoneName.Cemetery);
                                        if (deathrattle == null || cemetery == null || deathrattle.LaneList.Count == 0 || cemetery.LaneList.Count == 0
                                            || deathrattle.LaneList[0].IsLaneFull() || cemetery.LaneList[0].IsLaneFull()) continue;
                                        CatalogMovementRestoration.ValidateTransferDependencies(state, discard, board, deathrattle,
                                            self.NetId, 0, ServerCardCatalog.MoveOperationType.Untyped);
                                    }
                                }
                                catch (NotSupportedException) { continue; }
                                double score = actor.HasSubtype(CardSubtype.Creature) ? CardValue(actor) + 8 + LaneTempo(sim, actor, self, enemy, lane) : 0;
                                if (ability != null) score += ScoreEffects(sim, actor, target, self, enemy, ability.EffectGroup, Lane(self, enemy, laneTarget));
                                if (card.TriggeredEffects != null)
                                    foreach (TriggeredEffect trigger in card.TriggeredEffects)
                                        if (trigger.TriggerType == EffectTriggerType.Summon && trigger.CustomUiType == CustomCardUiType.None)
                                            score += ScoreEffects(sim, actor, target, self, enemy, trigger.EffectGroup);
                                if (actor.HasSubtype(CardSubtype.Creature))
                                    score += 0.5 * (TriggerValue(sim, actor, EffectTriggerType.Pilfer, self, enemy) + TriggerValue(sim, actor, EffectTriggerType.Slay, self, enemy)
                                        + TriggerValue(sim, actor, EffectTriggerType.Veteran, self, enemy) + TriggerValue(sim, actor, EffectTriggerType.LastGaspTrigger, self, enemy));
                                if (discard != null) score -= CardValue(discard) + 15;
                                score -= Math.Max(0, actor.QueryAttribute(AttributeType.ManaCost).Value) * 0.25;
                                Add(candidates, sim, self, list, play, score);
                            }
                }
            }
        }

        private static void GatherItemPlays(List<Candidate> candidates, Simulation sim, TurnState turn, CardCommandList list,
            CardCommandState command, ServerCard item, PlayerState self, PlayerState enemy)
        {
            // Mobilize: a 1/1 Recruit wielding the item in an empty friendly lane (OfflineMobilize).
            if (item.QueryAttribute(AttributeType.Mobilize).Value > 0)
                foreach (BoardLaneTarget lane in command.LaneTargets)
                {
                    Decision mobilize = New(list, command, CcgRpc.DecisionType.Play, 0);
                    mobilize.LaneTarget = lane;
                    if (!CommandRestoration.ValidateDecision(mobilize, list, sim.CardCatalog, self)
                        || !PlayCardHandler.PlayCardIsValid(sim, self, item, null, null, null, mobilize)) continue;
                    Add(candidates, sim, self, list, mobilize, 8 + item.QueryAttribute(AttributeType.Attack).Value * 3
                        + item.QueryAttribute(AttributeType.MaxHealth).Value * 1.5 - item.QueryAttribute(AttributeType.ManaCost).Value * 0.25);
                }
            foreach (int uid in command.EquipTargets)
            {
                ServerCard wielder = Find(sim, uid);
                if (wielder == null || wielder.OwnerNetId != self.NetId || wielder.CurrentGameZoneName != GameZoneName.CreatureBoard) continue;
                Decision play = New(list, command, CcgRpc.DecisionType.Play, 0);
                play.EquipCardNetUid = uid;
                if (!CommandRestoration.ValidateDecision(play, list, sim.CardCatalog, self)
                    || !PlayCardHandler.PlayCardIsValid(sim, self, item, null, wielder, null, play)) continue;
                try
                {
                    CatalogMovementRestoration.ValidateTransferDependencies(DecisionRestoration.State(sim, turn), item,
                        sim.CardCatalog.FindGameZone(self.NetId, item.CurrentGameZoneName),
                        sim.CardCatalog.FindGameZone(self.NetId, GameZoneName.ItemBoard), self.NetId, 0, ServerCardCatalog.MoveOperationType.Untyped);
                }
                catch (NotSupportedException) { continue; }
                int attack = item.QueryAttribute(AttributeType.Attack).Value;
                int health = item.QueryAttribute(AttributeType.MaxHealth).Value;
                double score = attack * 3 + health * 1.5 - item.QueryAttribute(AttributeType.ManaCost).Value * 0.25;
                if (CommandRestoration.CanAttack(sim.CardCatalog, self, enemy, wielder))
                {
                    score += attack * 2;
                    ServerCard avatar = Avatar(sim, enemy);
                    if (avatar != null && CommandRestoration.AttackTargets(sim.CardCatalog, self, enemy, wielder, false).Contains(avatar.NetUid)
                        && DiesFrom(wielder, avatar, PredictDamage(wielder, avatar, Power(wielder) + attack))) score += 1000000;
                }
                Add(candidates, sim, self, list, play, score);
            }
        }

        private static bool SafeCard(ServerCard actor, PlayerState self)
        {
            // User direction (2026-10-15): every item may be played (equip, or Mobilize to an empty lane).
            if (actor.QueryAttribute(AttributeType.Betray).Value > 0) return false;
            Card card = GameManager.Instance.GetCard(actor.CardId);
            if (card != null && card.TriggeredEffects != null)
                foreach (TriggeredEffect trigger in card.TriggeredEffects)
                    if ((trigger.CustomUiType != CustomCardUiType.None && trigger.CustomUiType != CustomCardUiType.TargetCemetery) || trigger.SecretTarget) return false;
            return card != null && card.PlayRequirements != null && card.ActionPlayInformation != null
                && (actor.CurrentGameZoneType == GameZoneType.Prophecy || actor.CurrentGameZoneType == GameZoneType.Betray
                    || actor.CanAffordCard(self.QueryAttribute(AttributeType.Mana).Value));
        }

        private static bool UsefulRing(Simulation sim, PlayerState self, PlayerState enemy)
        {
            int mana = self.QueryAttribute(AttributeType.Mana).Value;
            foreach (ServerCard card in sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, self.NetId, GameZoneName.Hand))
            {
                Card definition = GameManager.Instance.GetCard(card.CardId);
                if (definition == null || definition.PlayRequirements == null || definition.ActionPlayInformation == null
                    || definition.PlayRequirements.PlayerRequirements.Count != 0 || card.HasSubtype(CardSubtype.Item)
                    || card.QueryAttribute(AttributeType.Betray).Value > 0 || card.QueryAttribute(AttributeType.Mobilize).Value > 0
                    || card.CanAffordCard(mana) || !card.CanAffordCard(mana + 1)) continue;
                bool custom = false;
                foreach (PlayCardAbility ability in definition.ActionPlayInformation) if (ability.CustomUiType != CustomCardUiType.None) custom = true;
                if (custom) continue;
                bool canTarget = true;
                try
                {
                    foreach (PlayCardAbility ability in definition.ActionPlayInformation)
                        if (!CardCommandUtilities.TargetRequirementsMet(ability.PlayTargetingType,
                            CommandRestoration.SpellTargets(sim.CardCatalog, card, ability.PlayTargetingType, ability.EffectGroup.Effects,
                                ability.TargetSelectors, sim.Players, ability.IsDamageOnly, false).Count > 0
                            || CommandRestoration.LaneTargets(sim.CardCatalog, card, ability.PlayTargetingType, ability.TargetSelectors, self, enemy).Count > 0)) canTarget = false;
                }
                catch (NotSupportedException) { canTarget = false; }   // a targeting rule not restored yet: never worth the ring (it stalled the AI's turn)
                if (!canTarget || CardCommandUtilities.HasManaCostRestriction(card, self.NetId, sim.CardCatalog)) continue;
                double summonValue = 0;
                if (definition.TriggeredEffects != null)
                    foreach (TriggeredEffect trigger in definition.TriggeredEffects)
                    {
                        if ((trigger.CustomUiType != CustomCardUiType.None && trigger.CustomUiType != CustomCardUiType.TargetCemetery) || trigger.SecretTarget) { custom = true; break; }
                        if (trigger.TriggerType == EffectTriggerType.Summon) summonValue += ScoreEffects(sim, card, null, self, enemy, trigger.EffectGroup);
                    }
                if (custom || summonValue < -1000000) continue;
                GameZone board = sim.CardCatalog.FindGameZone(self.NetId, card.FindGameZoneName(GameZoneType.Board));
                if (board == null) continue;
                foreach (ICardLane lane in board.LaneList) if (!lane.IsLaneFull()) return true;
            }
            return false;
        }

        public static double ScoreAttack(ServerCard attacker, ServerCard defender, ServerCard enemyAvatar)
        {
            int damage = PredictDamage(attacker, defender, Power(attacker));
            bool defenderDies = DiesFrom(attacker, defender, damage);
            bool defendingFirst = Has(defender, AttributeType.FirstStrike) && !Has(attacker, AttributeType.FirstStrike);
            int counter = defender.CanCardCounterAttack() ? PredictDamage(defender, attacker, Power(defender)) : 0;
            bool attackerDies = DiesFrom(defender, attacker, counter);
            if (defendingFirst && attackerDies) { damage = 0; defenderDies = false; }
            if (Has(attacker, AttributeType.FirstStrike) && !Has(defender, AttributeType.FirstStrike) && defenderDies) { counter = 0; attackerDies = false; }
            if (defender.HasSubtype(CardSubtype.Avatar))
                return defenderDies ? 1000000 : damage * 6 + (Has(attacker, AttributeType.Drain) ? damage * 2 : 0) - (attackerDies ? CardValue(attacker) : 0);
            double score = (defenderDies ? CardValue(defender) + 18 : damage * 1.5) -
                (attackerDies ? CardValue(attacker) + 12 : counter * 1.5);
            if (defenderDies && defender.MustBeAttackedFirst()) score += 9;
            if (Has(defender, AttributeType.Ward) && damage == 0 && Power(attacker) > 0) score += 4;
            if (Has(attacker, AttributeType.Breakthrough) && damage > defender.CurrentHealth && enemyAvatar != null)
            {
                int excess = damage - defender.CurrentHealth;
                int face = PredictDamage(attacker, enemyAvatar, excess);
                score += DiesFrom(attacker, enemyAvatar, face) ? 1000000 : face * 6;
            }
            return score;
        }

        // Mirrors the ordinary surviving Damage/Fight attributes without mutating
        // native cards. Triggered effects and redirection are not simulated here.
        private static int PredictDamage(ServerCard source, ServerCard target, int raw)
        {
            if (raw <= 0 || !target.CanTakeDamageFromCard(source, Has(source, AttributeType.Wounded))
                || (Has(target, AttributeType.Ward) && !source.CanIgnoreEnemyKeywords(target))) return 0;
            CardAttributeQueryResult percent = target.QueryAttribute(AttributeType.PercentDamageTaken);
            int value = percent.HasAttribute ? (int)Math.Round(raw * (percent.Value / 100.0), MidpointRounding.AwayFromZero) : raw;
            if (Has(target, AttributeType.DoubleDamageFromSkeevers) && source.HasSubtype(CardSubtype.Skeever)) value *= 2;
            return Math.Max(0, value);
        }
        private static bool DiesFrom(ServerCard source, ServerCard target, int damage)
        {
            return damage > 0 && !Has(target, AttributeType.Immortal) && (damage >= target.CurrentHealth ||
                (target.HasSubtype(CardSubtype.Creature) && Has(source, AttributeType.Deathtouch)
                && !Has(target, AttributeType.ImmuneToLethal) && !target.CanIgnoreEnemyKeywords(source)));
        }

        private static double ChoiceScore(Simulation sim, TurnState turn, ServerCard actor, ServerCard target, PlayerState self, PlayerState enemy)
        {
            if (turn.EffectTriggerQueues.Active.Count > 0)
                return ScoreEffects(sim, actor, target, self, enemy, turn.EffectTriggerQueues.Active.Peek().TriggeredEffect.EffectGroup);
            // Offered targets remain legal, but unidentified effect polarity is
            // intentionally neutral rather than assuming enemy damage.
            return 1;
        }

        // Mirrors the native ServerEffectUtilities BoardLaneTarget -> LaneIdentifier conversion.
        private static LaneIdentifier Lane(PlayerState self, PlayerState enemy, BoardLaneTarget lane)
        {
            if (lane == BoardLaneTarget.None) return LaneIdentifier.GetInvalid();
            int index = lane != BoardLaneTarget.MyLeftLane && lane != BoardLaneTarget.OtherLeftLane ? 1 : 0;
            PlayerUniqueId owner = lane != BoardLaneTarget.MyLeftLane && lane != BoardLaneTarget.MyRightLane ? enemy.NetId : self.NetId;
            return new LaneIdentifier(GameZoneName.CreatureBoard, owner, index);
        }

        private static double ScoreEffects(Simulation sim, ServerCard actor, ServerCard target, PlayerState self, PlayerState enemy, EffectGroup group)
        { return ScoreEffects(sim, actor, target, self, enemy, group, LaneIdentifier.GetInvalid()); }

        private static double ScoreEffects(Simulation sim, ServerCard actor, ServerCard target, PlayerState self, PlayerState enemy, EffectGroup group, LaneIdentifier lane)
        {
            if (group == null) return 0;
            double score = 0;
            CardConditionalArguments ca = new CardConditionalArguments(sim.CardCatalog, actor, actor, target, target, null, lane);
            PlayerConditionalArguments pa = new PlayerConditionalArguments(sim.CardCatalog, sim.Players.Find(delegate(PlayerState p) { return p.NetId == actor.OwnerNetId; }), self,
                target == null ? null : sim.Players.Find(delegate(PlayerState p) { return p.NetId == target.OwnerNetId; }),
                target == null ? null : sim.Players.Find(delegate(PlayerState p) { return p.NetId == target.OwnerNetId; }),
                sim.Players.Find(delegate(PlayerState p) { return p.NetId != actor.OwnerNetId; }));
            foreach (Effect effect in group.Effects)
            {
                if (effect.ExecuteValueRequirements.Count != 0 || effect.ExecuteCardRequirements.Count != 0 || effect.ExecuteLaneRequirements.Count != 0) continue;
                // Evaluate native player requirements read-only (no history log); a failed requirement means no effect.
                bool met = true;
                foreach (IPlayerRequirement requirement in effect.ExecutePlayerRequirements)
                    if (!(requirement is PlayerRequirements.PlayerCountOfCardsRequirement || requirement is PlayerRequirements.PlayerAttributeValueRequirement)
                        || !requirement.IsRequirementMet(default(PlayerConditionalLog), pa, ca, default(TriggerConditionalArguments))) { met = false; break; }
                if (!met) continue;
                foreach (CardEffectAction action in effect.CardActions)
                {
                    CardActions.Damage damage = action.Action as CardActions.Damage;
                    CardActions.Destroy destroy = action.Action as CardActions.Destroy;
                    CardActions.Buff buff = action.Action as CardActions.Buff;
                    CardActions.Debuff debuff = action.Action as CardActions.Debuff;
                    CardActions.Freeze freeze = action.Action as CardActions.Freeze;
                    SPTESL.Enchantment stats = buff != null ? buff.Enchantment : debuff != null ? debuff.Enchantment : null;
                    CardActions.Ward ward = action.Action as CardActions.Ward;
                    CardActions.Silence silence = action.Action as CardActions.Silence;
                    CardActions.Heal heal = action.Action as CardActions.Heal;
                    CardActions.GiveKeyword keyword = action.Action as CardActions.GiveKeyword;
                    CardActions.Fight fight = action.Action as CardActions.Fight;
                    CardActions.Cover cover = action.Action as CardActions.Cover;
                    if (ward != null || silence != null || heal != null || keyword != null || fight != null || cover != null)
                    {
                        if (heal != null && (heal.HealthGain.ValueMethod != ActionValueMethod.Constant || heal.FullHeal || heal.HealToAVDValue)) continue;
                        if (keyword != null && (keyword.RandomKeyword || keyword.KeywordAttrib != AttributeType.Taunt)) continue;
                        bool plain = true;
                        foreach (ICardConditionalExpression selector in action.Selectors)
                            if (!(selector is CardConditionals.CardRoleType) && !(selector is CardConditionals.Subtype)
                                && !(selector is CardConditionals.Zone) && !(selector is CardConditionals.Relationship)
                                && !(selector is CardConditionals.InTargetedLane) && !(selector is CardConditionals.AttributeValue)) plain = false;
                        if (!plain) continue;
                        List<ServerCard> seen = new List<ServerCard>();
                        foreach (PlayerState player in sim.Players)
                        {
                            seen.AddRange(sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.CreatureBoard));
                            seen.AddRange(sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.AvatarBoard));
                        }
                        List<ServerCard> chosen = CardConditionals.EvaluateSelectorsOnServerCardList(action.Selectors, seen, ca, pa, default(TriggerConditionalArguments));
                        double share = RandomShare(action, chosen.Count);
                        foreach (ServerCard recipient in chosen)
                            score += share * (ward != null ? KeywordGainScore(recipient, self, AttributeType.Ward, 4)
                                : cover != null ? (Has(recipient, AttributeType.Taunt) || Has(recipient, AttributeType.ImmuneToCover) ? 0 : KeywordGainScore(recipient, self, AttributeType.Cover, 2))
                                : keyword != null ? KeywordGainScore(recipient, self, AttributeType.Taunt, 2)
                                : silence != null ? SilenceScore(recipient, self)
                                : heal != null ? HealScore(recipient, self, heal.HealthGain.ConstantValue)
                                : FightScore(actor, recipient, self));
                        continue;
                    }
                    if ((damage == null && destroy == null && stats == null && freeze == null)
                        || (damage != null && damage.HealthLoss.ValueMethod != ActionValueMethod.Constant)) continue;
                    bool simple = true;
                    foreach (ICardConditionalExpression selector in action.Selectors)
                        if (!(selector is CardConditionals.CardRoleType) && !(selector is CardConditionals.Subtype)
                            && !(selector is CardConditionals.Zone) && !(selector is CardConditionals.Relationship)
                            && !(selector is CardConditionals.InSameLane) && !(selector is CardConditionals.AttributeValue)
                            && !(selector is CardConditionals.Damaged) && !(selector is CardConditionals.InTargetedLane)) simple = false;
                    if (!simple) continue;
                    List<ServerCard> visible = new List<ServerCard>();
                    foreach (PlayerState player in sim.Players)
                    {
                        visible.AddRange(sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.CreatureBoard));
                        visible.AddRange(sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.AvatarBoard));
                    }
                    List<ServerCard> recipients = CardConditionals.EvaluateSelectorsOnServerCardList(action.Selectors, visible, ca, pa, default(TriggerConditionalArguments));
                    double fraction = RandomShare(action, recipients.Count);
                    foreach (ServerCard recipient in recipients)
                        score += fraction * (destroy != null ? DestroyScore(recipient, self) : damage != null ? DamageScore(actor, recipient, self, damage.HealthLoss.ConstantValue)
                            : stats != null ? StatScore(recipient, self, stats) : ShackleScore(recipient, self, freeze));
                }
                foreach (PlayerEffectAction action in effect.PlayerActions)
                {
                    bool simple = true;
                    foreach (IPlayerConditionalExpression selector in action.Selectors) if (!(selector is PlayerConditionals.PlayerRoleType)) simple = false;
                    if (!simple) continue;
                    foreach (PlayerState player in sim.Players)
                    {
                        bool selected = true;
                        foreach (IPlayerConditionalExpression selector in action.Selectors)
                            if (!selector.Evaluate(player, pa, ca, default(TriggerConditionalArguments), default(PlayerConditionalLog))) selected = false;
                        if (!selected) continue;
                        PlayerActions.DamageAvatarOfPlayer damage = action.Action as PlayerActions.DamageAvatarOfPlayer;
                        if (damage != null && damage.Amount.ValueMethod == ActionValueMethod.Constant)
                        { ServerCard avatar = Avatar(sim, player); if (avatar != null) score += DamageScore(actor, avatar, self, damage.Amount.ConstantValue); }
                        PlayerActions.DrawCards draw = action.Action as PlayerActions.DrawCards;
                        if (draw != null && draw.NumCards.ValueMethod == ActionValueMethod.Constant)
                            score += DrawScore(sim, actor, player, self, draw);
                    }
                }
            }
            return score;
        }

        private static double DamageScore(ServerCard actor, ServerCard target, PlayerState self, int raw)
        {
            int damage = PredictDamage(actor, target, raw);
            double score = target.HasSubtype(CardSubtype.Avatar) ? damage * 6 : damage * 2;
            if (DiesFrom(actor, target, damage)) score += target.HasSubtype(CardSubtype.Avatar) ?
                (target.OwnerNetId == self.NetId ? 2000000 : 1000000) : CardValue(target) + 12;
            return target.OwnerNetId == self.NetId ? -score : score;
        }

        // Draw-pile counts are public. Never inspect card identities or simulate RNG.
        // Empty-deck health/rune arithmetic mirrors original DrawOutOfCardsCard.
        private static double DrawScore(Simulation sim, ServerCard actor, PlayerState player, PlayerState self, PlayerActions.DrawCards draw)
        {
            PlayerState deckOwner = draw.DrawFrom == PlayerActions.DrawCards.DrawDeck.Self ? player
                : sim.Players.Find(delegate(PlayerState p) { return p.NetId != player.NetId; });
            int remaining = sim.CardCatalog.FindCardCountInDeck(deckOwner.NetId);
            int hand = sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
            if (actor.OwnerNetId == player.NetId && actor.CurrentGameZoneName == GameZoneName.Hand) hand--;
            ServerCard avatar = Avatar(sim, player);
            int health = avatar == null ? 0 : avatar.CurrentHealth;
            int runes = player.QueryAttribute(AttributeType.NumRunes).Value;
            int runeHealth = player.QueryAttribute(AttributeType.RuneHealth).Value;
            double score = 0;
            for (int i = 0; i < draw.NumCards.ConstantValue; i++)
            {
                if (draw.StopAtHandLimit && hand >= player.FindHandSize()) break;
                if (remaining > 0)
                {
                    remaining--;
                    if (hand < player.FindHandSize()) { hand++; score += 8; }
                    else score -= 2;
                }
                else if (!draw.IgnoreDeckingRule && avatar != null)
                {
                    int nextHealth = Math.Min(health, runes * runeHealth);
                    score -= (health - nextHealth) * 6;
                    if (runes > 0) { runes--; score -= 5; }
                    if (nextHealth <= 0 && health > 0) score -= 2000000;
                    health = nextHealth;
                }
            }
            return player.NetId == self.NetId ? score : -score;
        }
        // Reconstructed ranking only. Match the surviving ServerCard.Kill rejection
        // predicates without executing destruction, advancing RNG or changing cards.
        private static double DestroyScore(ServerCard target, PlayerState self)
        {
            if (target == null || !target.HasSubtype(CardSubtype.Creature) || target.CurrentGameZoneType != GameZoneType.Board
                || target.IsDying() || Has(target, AttributeType.Immortal) || Has(target, AttributeType.Indestructible)) return 0;
            double score = CardValue(target) + 12;
            return target.OwnerNetId == self.NetId ? -score : score;
        }
        // Reconstructed ranking only: constant Add stat enchantments and native Freeze
        // eligibility are read from visible board state without applying them.
        private static double StatScore(ServerCard target, PlayerState self, SPTESL.Enchantment enchantment)
        {
            if (target != null && target.HasSubtype(CardSubtype.Avatar) && target.CurrentGameZoneType == GameZoneType.Board)
            {
                int gain = 0;
                foreach (EnchantmentAttributeModification m in enchantment.AttributeModifications)
                    if (m.ModificationType == EnchantmentAttributeModificationType.Add && m.AttributeName == AttributeType.MaxMana) gain += m.Value; else return 0;
                double ramp = gain * 3;
                return target.OwnerNetId == self.NetId ? ramp : -ramp;
            }
            if (target == null || !target.HasSubtype(CardSubtype.Creature) || target.CurrentGameZoneType != GameZoneType.Board || target.IsDying()) return 0;
            int attack = 0, health = 0;
            foreach (EnchantmentAttributeModification m in enchantment.AttributeModifications)
            {
                if (m.ModificationType != EnchantmentAttributeModificationType.Add) return 0;
                if (m.AttributeName == AttributeType.Attack) attack += m.Value;
                else if (m.AttributeName == AttributeType.MaxHealth) health += m.Value;
                else return 0;
            }
            double score = target.CurrentHealth + health <= 0 && !Has(target, AttributeType.Immortal)
                ? -(CardValue(target) + 12)
                : Math.Max(-Power(target), attack) * 3 + health * 1.5;
            return target.OwnerNetId == self.NetId ? score : -score;
        }
        // Best visible-target value of replaying a Betray action (read-only estimate).
        private static double BetrayReplayValue(Simulation sim, ServerCard action, PlayerState self, PlayerState enemy)
        {
            Card card = GameManager.Instance.GetCard(action.CardId);
            if (card == null || card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0) return 0;
            PlayCardAbility ability = card.ActionPlayInformation[0];
            if (ability.PlayTargetingType == PlayTargetingType.NoTarget) return ScoreEffects(sim, action, null, self, enemy, ability.EffectGroup);
            double best = 0;
            foreach (PlayerState player in sim.Players)
                foreach (ServerCard target in sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.CreatureBoard))
                    best = Math.Max(best, ScoreEffects(sim, action, target, self, enemy, ability.EffectGroup));
            if (ability.TargetSelectors.Exists(delegate(DecisionConditionalExpression d) { return d.DecisionType == SPTESL.DecisionType.Lane; }))
                foreach (BoardLaneTarget lane in new BoardLaneTarget[] { BoardLaneTarget.MyLeftLane, BoardLaneTarget.MyRightLane, BoardLaneTarget.OtherLeftLane, BoardLaneTarget.OtherRightLane })
                    best = Math.Max(best, ScoreEffects(sim, action, null, self, enemy, ability.EffectGroup, Lane(self, enemy, lane)));
            return best;
        }

        // Native SelectorSort.Random returns NumToReturn of the matches: score the expectation, not every match.
        private static double RandomShare(CardEffectAction action, int matches)
        {
            if (action.SelectorSort == null || action.SelectorSort.Method != CardListSort.SortMethod.Random || matches <= 0) return 1;
            if (action.SelectorSort.NumToReturn.ValueMethod != ActionValueMethod.Constant) return 0;
            int n = action.SelectorSort.NumToReturn.ConstantValue;
            return n <= 0 ? 1 : Math.Min(n, matches) / (double)matches;
        }

        // Reconstructed valuation of the creature's own keyword triggers (no mutation, no hidden information).
        private static double TriggerValue(Simulation sim, ServerCard card, EffectTriggerType type, PlayerState self, PlayerState enemy)
        {
            Card definition = GameManager.Instance.GetCard(card.CardId);
            double score = 0;
            if (definition != null && definition.TriggeredEffects != null)
                foreach (TriggeredEffect trigger in definition.TriggeredEffects)
                    if (trigger.TriggerType == type && trigger.CustomUiType == CustomCardUiType.None && trigger.ClientTargetingType == PlayTargetingType.NoTarget)
                        score += ScoreEffects(sim, card, null, self, enemy, trigger.EffectGroup);
            return score;
        }

        // Pilfer on avatar damage, Slay on a predicted kill, Veteran on a first attack the attacker survives.
        private static double AttackTriggerBonus(Simulation sim, ServerCard attacker, ServerCard defender, PlayerState self, PlayerState enemy)
        {
            int damage = PredictDamage(attacker, defender, Power(attacker));
            bool defenderDies = DiesFrom(attacker, defender, damage);
            int counter = defender.HasSubtype(CardSubtype.Creature) && defender.CanCardCounterAttack() ? PredictDamage(defender, attacker, Power(defender)) : 0;
            if (Has(attacker, AttributeType.FirstStrike) && !Has(defender, AttributeType.FirstStrike) && defenderDies) counter = 0;
            bool attackerDies = DiesFrom(defender, attacker, counter);
            double bonus = 0;
            if (defender.HasSubtype(CardSubtype.Avatar) && damage > 0) bonus += TriggerValue(sim, attacker, EffectTriggerType.Pilfer, self, enemy);
            if (defender.HasSubtype(CardSubtype.Creature) && defenderDies) bonus += TriggerValue(sim, attacker, EffectTriggerType.Slay, self, enemy);
            if (!attackerDies && attacker.QueryGameJournalCount(CardJournal.Attacked) == 0) bonus += TriggerValue(sim, attacker, EffectTriggerType.Veteran, self, enemy);
            return bonus;
        }

        private static double KeywordGainScore(ServerCard target, PlayerState self, AttributeType keyword, double value)
        {
            if (target == null || !target.HasSubtype(CardSubtype.Creature) || target.CurrentGameZoneType != GameZoneType.Board || target.IsDying() || Has(target, keyword)) return 0;
            return target.OwnerNetId == self.NetId ? value : -value;
        }
        private static double SilenceScore(ServerCard target, PlayerState self)
        {
            if (target == null || !target.HasSubtype(CardSubtype.Creature) || target.CurrentGameZoneType != GameZoneType.Board || target.IsDying()
                || Has(target, AttributeType.ImmuneToSilence)) return 0;
            double value = (Has(target, AttributeType.Ward) ? 4 : 0) + (Has(target, AttributeType.Deathtouch) ? 6 : 0) + (Has(target, AttributeType.Taunt) ? 3 : 0)
                + (Has(target, AttributeType.Drain) ? 2 : 0) + (Has(target, AttributeType.Regenerate) ? 2 : 0) + (Has(target, AttributeType.Breakthrough) ? 2 : 0);
            return target.OwnerNetId == self.NetId ? -value : value;
        }
        private static double HealScore(ServerCard target, PlayerState self, int amount)
        {
            if (target == null || amount <= 0 || Has(target, AttributeType.NoHealing)) return 0;
            double value = target.HasSubtype(CardSubtype.Avatar) ? Math.Min(amount, Math.Max(0, 30 - target.CurrentHealth) + 1) * 1.5 : 0;
            return target.OwnerNetId == self.NetId ? value : -value;
        }
        // Battle is scored with the existing combat predictor (instigator as attacker); no state change.
        private static double FightScore(ServerCard actor, ServerCard target, PlayerState self)
        {
            if (target == null || actor == null || !target.HasSubtype(CardSubtype.Creature) || target.CurrentGameZoneType != GameZoneType.Board) return 0;
            double score = ScoreAttack(actor, target, null);
            return target.OwnerNetId == self.NetId ? -Math.Abs(score) : score;
        }
        private static double ShackleScore(ServerCard target, PlayerState self, CardActions.Freeze freeze)
        {
            if (target == null || !target.HasSubtype(CardSubtype.Creature) || target.CurrentGameZoneType != GameZoneType.Board || target.IsDying()
                || !CardActions.Freeze.CanFreeze(target, freeze.PermanentFreeze)) return 0;
            double score = Power(target) * 2 + 2;
            return target.OwnerNetId == self.NetId ? -score : score;
        }
        private static double LaneTempo(Simulation sim, ServerCard actor, PlayerState self, PlayerState enemy, int lane)
        {
            double ours = 0, theirs = 0;
            foreach (ServerCard card in sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CurrentLaneIndex == lane; }, self.NetId, GameZoneName.CreatureBoard)) ours += Power(card);
            foreach (ServerCard card in sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CurrentLaneIndex == lane; }, enemy.NetId, GameZoneName.CreatureBoard)) theirs += Power(card);
            return Math.Max(-8, Math.Min(8, theirs - ours));
        }
        private static double CardValue(ServerCard card) { return Power(card) * 3 + Math.Max(0, card.CurrentHealth) * 1.5 + (Has(card, AttributeType.Ward) ? 4 : 0) + (Has(card, AttributeType.Deathtouch) ? 6 : 0); }
        private static int Power(ServerCard card) { return Math.Max(0, card.QueryAttribute(AttributeType.Attack).Value); }
        private static bool Has(ServerCard card, AttributeType attribute) { return card.QueryAttribute(attribute).Value > 0; }
        private static ServerCard Find(Simulation sim, int uid) { return uid == 0 ? null : sim.CardCatalog.FindDynamicCard(new CardNetUid(uid)); }
        private static ServerCard Avatar(Simulation sim, PlayerState player) { return sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard card) { return card.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarBoard); }
        private static bool Visible(ServerCard card, PlayerState self) { return card != null && (card.CurrentGameZoneType == GameZoneType.Board ||
            (card.OwnerNetId == self.NetId && (card.CurrentGameZoneType == GameZoneType.Hand || card.CurrentGameZoneType == GameZoneType.Prophecy))); }
        private static Decision New(CardCommandList list, CardCommandState command, CcgRpc.DecisionType type, int target)
        { return new Decision { CommandUid = list.ListUid, ActorCardNetUid = command.CardNetUid, Type = type, TargetCardNetUid = target }; }
        private static void Add(List<Candidate> candidates, Simulation sim, PlayerState self, CardCommandList list, Decision decision, double score)
        { if (CommandRestoration.ValidateDecision(decision, list, sim.CardCatalog, self)) candidates.Add(new Candidate { Decision = decision, Score = score }); }
        private static int Compare(Decision a, Decision b)
        {
            int value = ((int)a.Type).CompareTo((int)b.Type); if (value != 0) return value;
            value = a.ActorCardNetUid.CompareTo(b.ActorCardNetUid); if (value != 0) return value;
            value = a.TargetCardNetUid.CompareTo(b.TargetCardNetUid); if (value != 0) return value;
            value = a.LaneIndex.CompareTo(b.LaneIndex); if (value != 0) return value;
            value = a.PositionIndex.CompareTo(b.PositionIndex); if (value != 0) return value;
            value = ((int)a.LaneTarget).CompareTo((int)b.LaneTarget); if (value != 0) return value;
            return (a.DiscardCardNetUids.Count == 0 ? 0 : a.DiscardCardNetUids[0]).CompareTo(b.DiscardCardNetUids.Count == 0 ? 0 : b.DiscardCardNetUids[0]);
        }
    }
}
