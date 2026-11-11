using System;
using System.Collections.Generic;
using System.Reflection;
using CcgRpc;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Only installed by the explicit local offline bootstrap. Uses surviving native
    // requirement evaluators and protobuf command models. Unreconstructed choices fail.
    public static class CommandRestoration
    {
        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(CardCommandUtilities), "PlayCardPrerequisitesMet", "PlayPrefix", null);
            // Story lane play restrictions (Jail, Mine Depths, Incinerator, Sewer): clear refused lane bits.
            harmony.Patch(AccessTools.Method(typeof(ServerCardCatalog), "FillPlayCardCommandStateNoPrerequisites"), null,
                new HarmonyMethod(typeof(CommandRestoration).GetMethod("PlayLanesPostfix")), null, null, null);
            Patch(harmony, typeof(CardCommandUtilities), "AttackCardPrerequisitesMet", "AttackPrefix", null);
            Patch(harmony, typeof(CardCommandUtilities), "ActiveAbilityPrerequisitesMet", "ActivePrefix", null);
            Patch(harmony, typeof(CardCommandUtilities), "StrongHintPrerequisitesMet", "HintPrefix", null);
            foreach (MethodInfo m in typeof(CardCommandUtilities).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "IsValidSpellTarget")
                {
                    ParameterInfo[] p = m.GetParameters();
                    if (p[p.Length - 3].ParameterType == typeof(PlayerConditionalArguments))
                        harmony.Patch(m, new HarmonyMethod(typeof(CommandRestoration).GetMethod("SpellCardPrefix")), null, null, null, null);
                    else if (p[p.Length - 1].ParameterType == typeof(PlayerConditionalArguments))
                        harmony.Patch(m, new HarmonyMethod(typeof(CommandRestoration).GetMethod("SpellLanePrefix")), null, null, null, null);
                }
            Patch(harmony, typeof(CardCommandUtilities), "GatherAttackTargets", "AttackTargetsPrefix", null);
            Patch(harmony, typeof(ServerCardCatalog), "GatherSpellCardTargets", "SpellTargetsPrefix", null);
            Patch(harmony, typeof(ServerCardCatalog), "GatherSpellLaneTargets", "LaneTargetsPrefix", null);
            Patch(harmony, typeof(ServerCardCatalog), "GatherMobilizeLaneTargets", "MobilizePrefix", null);
            Patch(harmony, typeof(ServerCardCatalog), "GenerateCardCommandItem", "MainPrefix", null);
            Patch(harmony, typeof(ServerCardCatalog), "GenerateProphecyCardCommandItem", "ProphecyPrefix", null);
            Patch(harmony, typeof(ServerCardCatalog), "GenerateBetrayCardCommandItem", "BetrayPrefix", null);
            Patch(harmony, typeof(ServerCardCatalog), "GenerateEffectCommandItem", "EffectPrefix", null);
            Patch(harmony, typeof(Simulation), "ValidateDecision", "ValidatePrefix", null);
        }

        public static void PlayLanesPostfix(ServerCardCatalog __instance, CardCommandState __0, ServerCard __1, PlayerState __3)
        {
            StoryLaneRules.RestrictPlayLanes(__0, __1, __3, __instance);
            if (__0.PlayTargets.Count > 0 && string.IsNullOrEmpty(__0.TargetingPromptLocKey)) __0.TargetingPromptLocKey = PromptOf(__1);
        }

        // The instruction the client shows on a card waiting for its target ("choose a creature..."): the card's targeting
        // effect's TargetingPrompt (Reborn sends the same key, e.g. Morkul Gatekeeper's Summon Prompt1). Without it a dropped
        // creature hovers with no hint and End Turn stays blocked (owner report 2026-11-05).
        public static string PromptOf(ServerCard card)
        {
            if (card == null) return "";
            // the running card's effects first; else the original card data (reconstructed rules, e.g. Morkul Gatekeeper's
            // play ability, carry no prompt of their own)
            string key = PromptIn(card.TriggeredEffects);
            if (key.Length == 0) { Card original = OriginalGraphRules.Original(card.CardId); if (original != null) key = PromptIn(original.TriggeredEffects); }
            return key;
        }

        private static string PromptIn(List<TriggeredEffect> effects)
        {
            if (effects == null) return "";
            TriggeredEffect best = null;
            foreach (TriggeredEffect e in effects)
                if (e != null && e.ClientTargetingType != PlayTargetingType.NoTarget && !string.IsNullOrEmpty(e.TargetingPrompt.RawKey)
                    && (best == null || e.TriggerType == EffectTriggerType.Summon)) best = e;
            return best == null ? "" : best.TargetingPrompt.RawKey;
        }

        private static void Patch(Harmony harmony, Type type, string name, string prefix, Type[] arguments)
        {
            MethodInfo original = arguments == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, arguments);
            if (original == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(original, new HarmonyMethod(typeof(CommandRestoration).GetMethod(prefix)), null, null, null, null);
        }

        private static CardConditionalArguments CardArgs(ServerCardCatalog catalog, ServerCard card, ServerCard target, LaneIdentifier lane)
        { return new CardConditionalArguments(catalog, card, card, target, target, null, lane); }
        private static LaneIdentifier CardLane(ServerCard card)
        { return new LaneIdentifier(card.CurrentGameZoneName, card.OwnerNetId, card.CurrentLaneIndex); }
        private static PlayerConditionalArguments PlayerArgs(ServerCardCatalog c, PlayerState p, PlayerState o, PlayerState target)
        { return new PlayerConditionalArguments(c, p, p, target, target, o); }

        private static bool Requirements(List<IPlayerRequirement> pr, List<ICardRequirement> cr, List<ILaneRequirement> lr,
            PlayerConditionalArguments p, CardConditionalArguments c, LaneConditionalArguments l)
        {
            if (pr != null) foreach (IPlayerRequirement r in pr)
                if (!r.IsRequirementMet(default(PlayerConditionalLog), p, c, default(TriggerConditionalArguments))) return false;
            if (cr != null) foreach (ICardRequirement r in cr)
                if (!r.IsRequirementMet(c, p, default(TriggerConditionalArguments))) return false;
            if (lr != null) foreach (ILaneRequirement r in lr)
                if (!r.IsRequirementMet(l, c, p)) return false;
            return true;
        }

        public static bool CanPlay(ServerCard instance, Card card, ref BoardLaneMask legal, PlayerConditionalArguments p, bool betray)
        {
            if (instance == null || card == null || p.SelfPlayerState == null || instance.OwnerNetId != p.SelfPlayerState.NetId) return false;
            RequireRules(instance, "play");
            GameZoneType z = instance.CurrentGameZoneType;
            if (z != GameZoneType.Hand && z != GameZoneType.Prophecy && z != GameZoneType.Betray) return false;
            PlayCardRequirements req = betray ? card.BetrayRequirements : card.PlayRequirements;
            if (req == null || card.ActionPlayInformation == null)
                throw new NotSupportedException("Native play rules have not been restored for card " + card.Name + " (" + instance.CardId + ").");
            if (z != GameZoneType.Prophecy && z != GameZoneType.Betray)
            {
                if (!instance.CanAffordCard(p.SelfPlayerState.QueryAttribute(AttributeType.Mana).Value) ||
                    CardCommandUtilities.HasManaCostRestriction(instance, p.SelfPlayerState.NetId, p.CardCatalog)) return false;
            }
            PlayerAttributeQueryResult limit = p.SelfPlayerState.QueryAttribute(AttributeType.MaxCardsPlayedPerTurn);
            if (limit.HasAttribute && limit.Value > 0 && p.SelfPlayerState.NumCardsPlayedThisTurn >= limit.Value) return false;
            if (!Requirements(req.PlayerRequirements, null, null, p, CardArgs(p.CardCatalog, instance, null, LaneIdentifier.GetInvalid()), default(LaneConditionalArguments))) return false;
            // Filled lanes remain legal: native UI supports replacing a friendly creature.
            if (instance.HasSubtype(CardSubtype.Creature) && legal.Value == 0) return false;
            return true;
        }

        public static bool CanAttack(ServerCardCatalog catalog, PlayerState player, PlayerState opponent, ServerCard attacker)
        {
            if (attacker != null && !attacker.HasSubtype(CardSubtype.Avatar)) RequireRules(attacker, "attack");
            if (attacker == null || player == null || opponent == null || attacker.OwnerNetId != player.NetId ||
                attacker.CurrentGameZoneType != GameZoneType.Board || attacker.IsDying() || !attacker.CanCardAttack(false) ||
                player.QueryAttribute(AttributeType.PlayerCannotAttack).Value > 0) return false;
            AttackCardRequirements r = attacker.AttackRequirements;
            return r == null || Requirements(r.PlayerRequirements, r.CardRequirements, r.LaneRequirements,
                PlayerArgs(catalog, player, opponent, null), CardArgs(catalog, attacker, null, LaneIdentifier.GetInvalid()),
                new LaneConditionalArguments(catalog, CardLane(attacker), LaneIdentifier.GetInvalid()));
        }

        public static bool CanActivate(ServerCard card, PlayerConditionalArguments p)
        {
            if (card != null && card.HasPotentialActivatedAbility()) RequireRules(card, "activate");
            if (card == null || p.SelfPlayerState == null || card.OwnerNetId != p.SelfPlayerState.NetId || card.IsDying() ||
                !card.HasPotentialActivatedAbility() || card.Silenced || card.HasConsumedAllAbilityUsesForThisTurn()) return false;
            ActivatedAbility a = card.ActivatedAbility;
            if (card.QueryAttribute(AttributeType.TotalUsesAllowed).Value > 0 &&
                card.TotalAbilityUse >= card.QueryAttribute(AttributeType.TotalUsesAllowed).Value && card.QueryAttribute(AttributeType.UnlimitedUses).Value <= 0) return false;
            return Requirements(a.ActivationPlayerRequirements, a.ActivationCardRequirements, null, p,
                CardArgs(p.CardCatalog, card, null, LaneIdentifier.GetInvalid()), default(LaneConditionalArguments));
        }

        private static void RequireRules(ServerCard card, string operation)
        {
            // User direction (2026-10-15): nothing is blocked. A card whose text effects are not yet reconstructed plays
            // with its native metadata (stats, keywords, class-default wrappers); RuleImporter reports coverage.
            if (!RuleImporter.IsSupported(card.CardId)) RuleImporter.NoteMetadataOnlyUse(card.CardId, operation);
        }

        public static List<CardNetUid> AttackTargets(ServerCardCatalog catalog, PlayerState player, PlayerState opponent, ServerCard attacker, bool allowImmune)
        {
            List<CardNetUid> result = new List<CardNetUid>();
            if (attacker == null) return result;
            if (!attacker.HasSubtype(CardSubtype.Avatar)) RequireRules(attacker, "gather attack targets for");
            List<ServerCard> all = catalog.GatherCardsInZone(delegate(ServerCard x) { return !x.IsDying(); }, opponent.NetId, GameZoneName.CreatureBoard);
            // Special attack rules from installed attributes (card text; native validation routes through this list):
            //  MoveToAttackOtherLane (Serpentine Stalker): may move to attack creatures in the other lane; that lane's Guards apply
            //    and the move needs room in it. CanAttackFriendlies (Cicero): may also attack friendly creatures it could reach.
            //  CanAttackFriendFace (Cadwell the Betrayer): may also attack its own player. Opponent Guards restrict only enemy targets.
            if (attacker.QueryAttribute(AttributeType.MoveToAttackOtherLane).Value > 0 && !attacker.HasSubtype(CardSubtype.Avatar))
            {
                int other = attacker.CurrentLaneIndex == 0 ? 1 : 0;
                if (!catalog.IsLaneFull(GameZoneName.CreatureBoard, other, attacker.OwnerNetId))
                {
                    List<ServerCard> there = all.FindAll(delegate(ServerCard x) { return x.CurrentLaneIndex == other; });
                    List<ServerCard> guardsThere = there.FindAll(delegate(ServerCard x) { return x.MustBeAttackedFirst() && !x.ProtectedByCover(); });
                    foreach (ServerCard target in there)
                    {
                        if (!attacker.CanIgnoreMustBeAttackedFirst() && guardsThere.Count > 0 && !guardsThere.Contains(target)) continue;
                        if (target.QueryAttribute(AttributeType.CannotBeAttacked).Value > 0 || !target.CanBeExplicitlyTargetedByOpponentAttack(attacker)) continue;
                        if (!allowImmune && !target.CanTakeDamageFromCard(attacker, attacker.QueryAttribute(AttributeType.Wounded).Value > 0)) continue;
                        result.Add(target.NetUid);
                    }
                }
            }
            if (attacker.QueryAttribute(AttributeType.CanAttackFriendlies).Value > 0)
            {
                bool reach = attacker.QueryAttribute(AttributeType.CanAttackOtherLane).Value > 0;
                foreach (ServerCard friend in catalog.GatherCardsInZone(delegate(ServerCard x) { return !x.IsDying() && x != attacker; }, attacker.OwnerNetId, GameZoneName.CreatureBoard))
                    if ((reach || friend.CurrentLaneIndex == attacker.CurrentLaneIndex) && friend.QueryAttribute(AttributeType.CannotBeAttacked).Value == 0) result.Add(friend.NetUid);
            }
            if (attacker.QueryAttribute(AttributeType.CanAttackFriendFace).Value > 0)
                foreach (ServerCard self in catalog.GatherCardsInZone(delegate(ServerCard x) { return true; }, attacker.OwnerNetId, GameZoneName.AvatarBoard)) result.Add(self.NetUid);
            List<ServerCard> guards = new List<ServerCard>();
            foreach (ServerCard target in all)
                if (target.MustBeAttackedFirst() && !target.ProtectedByCover() && (target.CurrentLaneIndex == attacker.CurrentLaneIndex || target.GuardsBothLanes())) guards.Add(target);
            bool ignoreGuard = attacker.CanIgnoreMustBeAttackedFirst();
            bool anyLane = attacker.QueryAttribute(AttributeType.CanAttackOtherLane).Value > 0;
            all.AddRange(catalog.GatherCardsInZone(delegate(ServerCard x) { return true; }, opponent.NetId, GameZoneName.AvatarBoard));
            foreach (ServerCard target in all)
            {
                bool avatar = target.HasSubtype(CardSubtype.Avatar);
                if (!avatar && target.CurrentLaneIndex != attacker.CurrentLaneIndex && !anyLane && !target.CanBeAttackedFromAnyLane()) continue;
                if (!ignoreGuard && guards.Count > 0 && !guards.Contains(target)) continue;
                if (target.QueryAttribute(AttributeType.CannotBeAttacked).Value > 0 || !target.CanBeExplicitlyTargetedByOpponentAttack(attacker)) continue;
                if (!allowImmune && !target.CanTakeDamageFromCard(attacker, attacker.QueryAttribute(AttributeType.Wounded).Value > 0)) continue;
                result.Add(target.NetUid);
            }
            return result;
        }

        public static bool ValidCardTarget(ServerCard source, List<Effect> effects, List<DecisionConditionalExpression> selectors, ServerCard target,
            ServerCardCatalog catalog, PlayerConditionalArguments p, bool damageOnly, bool useWielder)
        {
            if (source == null || target == null || selectors == null || selectors.Count == 0) return false;
            if (useWielder && source.HasSubtype(CardSubtype.Item))
            {
                source = catalog.FindServerCard(source.ItemTarget);
                if (source == null) return false;
            }
            if (!target.CanBeExplicitlyTargetedByCard(source, effects, damageOnly)) return false;
            CardConditionalArguments args = CardArgs(catalog, source, target, LaneIdentifier.GetInvalid());
            foreach (DecisionConditionalExpression selector in selectors)
            {
                if (selector.DecisionType != SPTESL.DecisionType.Card) continue;
                bool valid = true;
                foreach (ICardConditionalExpression e in selector.CardConditionalExpressions)
                    if (!e.Evaluate(target, args, p, default(TriggerConditionalArguments))) { valid = false; break; }
                if (valid) return true;
            }
            return false;
        }

        public static bool ValidLaneTarget(ServerCard source, List<DecisionConditionalExpression> selectors, GameZoneName zone, PlayerUniqueId owner,
            int index, ServerCardCatalog catalog, PlayerConditionalArguments p)
        {
            if (source == null || selectors == null || selectors.Count == 0) return false;
            GameZone gameZone = catalog.FindGameZone(owner, zone);
            if (gameZone == null || index < 0 || index >= gameZone.LaneList.Count) return false;
            LaneIdentifier lane = new LaneIdentifier(zone, owner, index);
            CardConditionalArguments args = CardArgs(catalog, source, null, lane);
            LaneConditionalArguments la = new LaneConditionalArguments(catalog, CardLane(source), lane);
            foreach (DecisionConditionalExpression selector in selectors)
            {
                if (selector.DecisionType == SPTESL.DecisionType.Card) continue;
                if (selector.DecisionType == SPTESL.DecisionType.LaneLeftOrRight)
                {   // "Choose a lane" (both sides of it: Trial of Flame, Haunted Amulet): offered on the chooser's side; valid when
                    // its conditions hold on either side of that lane. The card's own effects act on both sides of the chosen lane.
                    if (owner != source.OwnerNetId) continue;
                    foreach (PlayerUniqueId side in new PlayerUniqueId[] { owner, OtherOwner(catalog, owner) })
                    {
                        GameZone sideZone = catalog.FindGameZone(side, zone);
                        if (sideZone == null || index >= sideZone.LaneList.Count) continue;
                        LaneIdentifier sideLane = new LaneIdentifier(zone, side, index);
                        CardConditionalArguments sideArgs = CardArgs(catalog, source, null, sideLane);
                        LaneConditionalArguments sideLa = new LaneConditionalArguments(catalog, CardLane(source), sideLane);
                        bool sideValid = true;
                        foreach (ILaneConditionalExpression e in selector.LaneConditionalExpressions)
                            if (!e.Evaluate(sideLane, sideLa, sideArgs, p)) { sideValid = false; break; }
                        if (sideValid) return true;
                    }
                    continue;
                }
                bool valid = true;
                foreach (ILaneConditionalExpression e in selector.LaneConditionalExpressions)
                    if (!e.Evaluate(lane, la, args, p)) { valid = false; break; }
                if (valid) return true;
            }
            return false;
        }

        private static PlayerUniqueId OtherOwner(ServerCardCatalog catalog, PlayerUniqueId owner)
        {
            foreach (ServerCard c in catalog.GatherDynamicCardList()) if (c != null && c.OwnerNetId != owner) return c.OwnerNetId;
            return owner;
        }

        public static List<CardNetUid> SpellTargets(ServerCardCatalog catalog, ServerCard card, PlayTargetingType type, List<Effect> effects,
            List<DecisionConditionalExpression> selectors, List<PlayerState> players, bool damageOnly, bool useWielder)
        {
            List<CardNetUid> targets = new List<CardNetUid>();
            if (type == PlayTargetingType.NoTarget) return targets;
            PlayerState player = players.Find(delegate(PlayerState p) { return p.NetId == card.OwnerNetId; });
            PlayerState opponent = players.Find(delegate(PlayerState p) { return p.NetId != card.OwnerNetId; });
            foreach (ServerCard target in catalog.GatherCardList(delegate(ServerCard x) { return true; }))
            {
                PlayerState targetPlayer = players.Find(delegate(PlayerState p) { return p.NetId == target.OwnerNetId; });
                if (ValidCardTarget(card, effects, selectors, target, catalog, PlayerArgs(catalog, player, opponent, targetPlayer), damageOnly, useWielder)) targets.Add(target.NetUid);
            }
            return targets;
        }

        public static List<BoardLaneTarget> LaneTargets(ServerCardCatalog catalog, ServerCard card, PlayTargetingType type,
            List<DecisionConditionalExpression> selectors, PlayerState player, PlayerState opponent)
        {
            List<BoardLaneTarget> targets = new List<BoardLaneTarget>();
            if (type == PlayTargetingType.NoTarget) return targets;
            for (int side = 0; side < 2; side++) for (int lane = 0; lane < 2; lane++)
            {
                PlayerState owner = side == 0 ? player : opponent;
                if (ValidLaneTarget(card, selectors, GameZoneName.CreatureBoard, owner.NetId, lane, catalog, PlayerArgs(catalog, player, opponent, owner)))
                    targets.Add((BoardLaneTarget)(1 + side * 2 + lane));
            }
            return targets;
        }

        private static CardCommandItem NewItem(ServerCardCatalog catalog, bool required)
        { return new CardCommandItem { CommandList = new CardCommandList { ListUid = ++catalog._commandListUuid, MustSelectCommand = required }, OpponentCommandList = new OpponentCardCommandList() }; }
        private static void AddItem(CardCommandItem item, CardCommandState c)
        {
            if (c.ValidCardCommandMask == 0) return;
            item.CommandList.ValidCommands.Add(c);
            if ((c.ValidCardCommandMask & ((int)CardCommand.Attack | (int)CardCommand.ActiveAbility)) != 0)
                item.OpponentCommandList.ActionablePublicCardNetUids.Add(c.CardNetUid);
        }

        public static CardCommandItem MainCommands(ServerCardCatalog catalog, PlayerState player, PlayerState opponent, List<PlayerState> players)
        {
            CardCommandItem item = NewItem(catalog, false);
            foreach (ServerCard card in catalog.GatherCardList(delegate(ServerCard x) { return x.OwnerNetId == player.NetId; }))
            {
                CardCommandState command = new CardCommandState { CardNetUid = card.NetUid.Id };
                if (card.CurrentGameZoneType == GameZoneType.Hand) catalog.FillPlayCardCommandState(command, card, player, opponent, players);
                if (card.CurrentGameZoneType == GameZoneType.Board)
                {
                    if (CanAttack(catalog, player, opponent, card))
                    {
                        List<CardNetUid> targets = AttackTargets(catalog, player, opponent, card, false);
                        foreach (CardNetUid t in targets) command.AttackTargets.Add(t.Id);
                        if (targets.Count > 0) command.ValidCardCommandMask |= (int)CardCommand.Attack;
                    }
                    // Avatars never appear in these lists (Reborn capture 2026-10-19).
                    else if (card.HasSubtype(CardSubtype.Avatar)) { }
                    else if (card.IsSleeping() || card.QueryAttribute(AttributeType.Freeze).Value > 0) item.CommandList.NotReadyCardUids.Add(card.NetUid.Id);
                    else if (card.HasConsumedAllActionsForThisTurn()) item.CommandList.AlreadyUsedCardUids.Add(card.NetUid.Id);
                    if (CanActivate(card, PlayerArgs(catalog, player, opponent, null)))
                    {
                        ActivatedAbility a = card.ActivatedAbility;
                        if (a.AbilityGroups.Count != 1)
                            throw new NotSupportedException("Activated abilities with several groups are not restored for card " + card.CardId + ".");
                        List<CardNetUid> targets = SpellTargets(catalog, card, a.TargetingType, a.GatherAllEffects(), a.TargetSelectors, players, false, false);
                        List<BoardLaneTarget> lanes = LaneTargets(catalog, card, a.TargetingType, a.TargetSelectors, player, opponent);
                        if (CardCommandUtilities.TargetRequirementsMet(a.TargetingType, targets.Count > 0 || lanes.Count > 0))
                        {
                            command.ValidCardCommandMask |= (int)CardCommand.ActiveAbility;
                            foreach (CardNetUid t in targets) command.ActiveAbilityTargets.Add(t.Id);
                            command.LaneTargets.AddRange(lanes);
                        }
                    }
                }
                AddItem(item, command);
            }
            item.CommandList.ValidCommands.Add(new CardCommandState { CardNetUid = CardNetUid.GetInvalid().Id, ValidCardCommandMask = (int)CardCommand.EndTurn |
                (CardCommandUtilities.ManaRingRequirementsMet(player) ? (int)CardCommand.UseRing : 0) });
            catalog.AttachFullLaneCardsToCommandList(player, item.CommandList);
            item.OpponentCommandList.ListUid = item.CommandList.ListUid;
            return item;
        }

        public static CardCommandItem ProphecyCommands(ServerCardCatalog c, PlayerState p, PlayerState o, List<PlayerState> ps, ServerCard card, bool hand)
        {
            CardCommandItem item = NewItem(c, true);
            CardCommandState command = new CardCommandState { CardNetUid = card.NetUid.Id };
            c.FillPlayCardCommandState(command, card, p, o, ps);
            command.ValidCardCommandMask |= hand ? (int)CardCommand.PlayToHand : (int)CardCommand.DeclineToPlay;
            AddItem(item, command);
            return item;
        }

        public static CardCommandItem EffectCommands(ServerCardCatalog c, ServerEffectUtilities.TriggerContext ctx, PlayerState p, PlayerState o, List<PlayerState> ps, List<CustomDisplayCard> display)
        {
            if (OfflineConsumeChoices.Supported(ctx) && ctx.DidMainEffectsRun) return OfflineConsumeChoices.Commands(c, ctx, display);
            if (ctx.EffectOwner != null && ctx.TriggeredEffect.CustomUiType != CustomCardUiType.None && ctx.DidMainEffectsRun)
                return OfflineConsumeChoices.GeneralCommands(c, ctx, display);
            // A custom-UI effect whose first phase targets (ClientTargetingType set) uses the ordinary target prompt here;
            // its choice phase is built above once the main group ran.
            // SecretTarget (False Luminary, Galyn, Berne Clan Nightstalker) only hides the choice from the opponent; offline the
            // opponent is the AI, so the ordinary target prompt serves it.
            // A vision (Bringer of Nightmares: "See a vision of a random creature, then choose a creature to transform into
            // it") is a display card shown with an ordinary target prompt; it rides on the command's DisplayCards below.
            if (ctx.EffectOwner == null)
            {
                string displayIds = "";
                if (display != null) foreach (CustomDisplayCard d in display) displayIds += (displayIds.Length > 0 ? "," : "") + (d == null ? "?" : d.CardInstanceId.ToString());
                throw new NotSupportedException("Custom UI, secret targets, or lane-owned trigger choices are not restored ("
                    + (ctx.EffectOwner == null ? "no effect owner, lane " + ctx.ActingLaneIndex : "owner card " + ctx.EffectOwner.CardId + (ctx.EffectOwner.CreatorCardId != 0 ? " created by " + ctx.EffectOwner.CreatorCardId : ""))
                    + ", trigger " + (ctx.TriggeredEffect == null ? "?" : ctx.TriggeredEffect.TriggerType + "/" + ctx.TriggeredEffect.CustomUiType)
                    + ", display [" + displayIds + "]).");
            }
            TriggeredEffect e = ctx.TriggeredEffect;
            CardCommandItem item = NewItem(c, e.ClientTargetingType == PlayTargetingType.RequiredTarget);
            CardCommandState command = new CardCommandState { CardNetUid = ctx.EffectOwner.NetUid.Id, ValidCardCommandMask = (int)CardCommand.ChooseTarget,
                TargetingPromptLocKey = e.TargetingPrompt.RawKey ?? "" };
            foreach (CardNetUid t in SpellTargets(c, ctx.EffectOwner, e.ClientTargetingType, e.EffectGroup.Effects, e.TargetSelectors, ps, false, e.ItemUsesWielderAsEffectSource)) command.PlayTargets.Add(t.Id);
            if (display != null) foreach (CustomDisplayCard d in display) if (d != null && d.HistoryCard != null) command.DisplayCards.Add(d.HistoryCard);
            command.LaneTargets.AddRange(LaneTargets(c, ctx.EffectOwner, e.ClientTargetingType, e.TargetSelectors, p, o));
            // No legal target: nothing to choose, so the trigger resolves without a target (its chosen-target actions find nothing).
            if (command.PlayTargets.Count == 0 && command.LaneTargets.Count == 0) { item.CommandList.MustSelectCommand = false; return item; }
            // An optional target can be skipped: the client shows Cancel (Reborn capture 2026-11-05: Morkul Gatekeeper's
            // Summon prompt is ChooseTarget|DeclineToPlay); the decline (NoCommand) resolves the effect without a target.
            if (e.ClientTargetingType == PlayTargetingType.OptionalTarget) command.ValidCardCommandMask |= (int)CardCommand.DeclineToPlay;
            AddItem(item, command);
            return item;
        }

        public static bool ValidateDecision(Decision decision, CardCommandList list)
        {
            if (decision != null && decision.Type == CcgRpc.DecisionType.CustomChoice) return OfflineConsumeChoices.Validate(decision, list);
            if (decision == null || list == null || decision.CommandUid != list.ListUid) return false;
            if (decision.ActorCardNetUid < 0 || decision.TargetCardNetUid < 0 || decision.EquipCardNetUid < 0 ||
                (int)decision.LaneTarget < 0 || (int)decision.LaneTarget > (int)BoardLaneTarget.AllRightLanes || decision.CustomChoice != 0) return false;
            if (decision.Type != CcgRpc.DecisionType.Play && (decision.PositionIndex != 0 || decision.LaneIndex != 0)) return false;
            if (decision.Type == CcgRpc.DecisionType.NoCommand)
            {
                if (decision.ActorCardNetUid != 0 || decision.TargetCardNetUid != 0 || decision.EquipCardNetUid != 0 || decision.LaneTarget != BoardLaneTarget.None || decision.DiscardCardNetUids.Count != 0) return false;
                if (!list.MustSelectCommand) return true;
                foreach (CardCommandState x in list.ValidCommands) if ((x.ValidCardCommandMask & (int)CardCommand.DeclineToPlay) != 0) return true;
                return false;
            }
            CardCommandState c = null;
            foreach (CardCommandState x in list.ValidCommands) if (x.CardNetUid == decision.ActorCardNetUid || (x.SourceOverrideNetUid != 0 && x.SourceOverrideNetUid == decision.ActorCardNetUid)) { c = x; break; }
            if (c == null) return false;
            int mask = c.ValidCardCommandMask;
            switch (decision.Type)
            {
                case CcgRpc.DecisionType.Attack: return (mask & (int)CardCommand.Attack) != 0 && c.AttackTargets.Contains(decision.TargetCardNetUid) &&
                    decision.EquipCardNetUid == 0 && decision.LaneTarget == BoardLaneTarget.None && decision.DiscardCardNetUids.Count == 0;
                case CcgRpc.DecisionType.Activate: return (mask & (int)CardCommand.ActiveAbility) != 0 && decision.EquipCardNetUid == 0 &&
                    decision.DiscardCardNetUids.Count == 0 && TargetSelectionValid(decision, c.ActiveAbilityTargets, c.LaneTargets);
                case CcgRpc.DecisionType.Play:
                    // An item that may also be thrown (Spear of Embers) offers both: a target is the throw, a wielder the equip.
                    bool equipInstead = (mask & (int)CardCommand.PlayToEquip) != 0 && decision.EquipCardNetUid != 0 && decision.TargetCardNetUid == 0;
                    if ((mask & (int)CardCommand.ChooseTarget) != 0 && !equipInstead)
                        return decision.PositionIndex == 0 && decision.LaneIndex == 0 && decision.EquipCardNetUid == 0
                            && decision.DiscardCardNetUids.Count == 0 && TargetSelectionValid(decision, c.PlayTargets, c.LaneTargets);
                    if ((mask & 15) == 0) return false;
                    if ((mask & (int)CardCommand.PlayToPosition) != 0 && (decision.LaneIndex < 0 || decision.LaneIndex > 31 || (c.ValidBoardLanes & (1 << decision.LaneIndex)) == 0)) return false;
                    if ((mask & (int)CardCommand.PlayToPosition) == 0 && decision.LaneIndex != 0) return false;
                    // This protobuf model carries lane masks, but no lane lengths.
                    // Bound the selected slot against the live catalog before selecting
                    // or executing the decision; positionless cards keep the native zero.
                    if (decision.PositionIndex < 0 || ((mask & (int)CardCommand.PlayToPosition) == 0 && decision.PositionIndex != 0)) return false;
                    bool fullLane = (mask & (int)CardCommand.PlayToPosition) != 0 && (c.FullBoardLanes & (1 << decision.LaneIndex)) != 0;
                    if (fullLane != (decision.DiscardCardNetUids.Count == 1) || decision.DiscardCardNetUids.Count > 1) return false;
                    if (fullLane && !list.FullLaneCardUids.Contains(decision.DiscardCardNetUids[0])) return false;
                    if ((mask & (int)CardCommand.PlayToEquip) != 0 && !c.EquipTargets.Contains(decision.EquipCardNetUid) && !c.LaneTargets.Contains(decision.LaneTarget)) return false;
                    if ((mask & (int)CardCommand.PlayToEquip) == 0 && decision.EquipCardNetUid != 0) return false;
                    // An item's lane targets are Mobilize alternatives to equipping, never a requirement of an equip decision.
                    if ((mask & (int)CardCommand.PlayToEquip) != 0 && decision.EquipCardNetUid != 0)
                        return decision.LaneTarget == BoardLaneTarget.None && (equipInstead
                            || TargetSelectionValid(decision, c.PlayTargets, new Google.Protobuf.Collections.RepeatedField<BoardLaneTarget>()));
                    return TargetSelectionValid(decision, c.PlayTargets, c.LaneTargets);
                case CcgRpc.DecisionType.ToHand: return (mask & (int)CardCommand.PlayToHand) != 0 && decision.TargetCardNetUid == 0 && decision.EquipCardNetUid == 0 && decision.LaneTarget == BoardLaneTarget.None && decision.DiscardCardNetUids.Count == 0;
                case CcgRpc.DecisionType.Betray:
                    // Client BetrayChoice sends the sacrifice in DiscardCardNetUids; BetrayDecline sends none.
                    return (mask & (int)CardCommand.BetrayChoice) != 0 && decision.TargetCardNetUid == 0 && decision.EquipCardNetUid == 0
                        && decision.LaneTarget == BoardLaneTarget.None && decision.PositionIndex == 0 && decision.LaneIndex == 0 && decision.DiscardCardNetUids.Count <= 1
                        && (decision.DiscardCardNetUids.Count == 0 || c.ActiveAbilityTargets.Contains(decision.DiscardCardNetUids[0]));
                case CcgRpc.DecisionType.UseRing: return (mask & (int)CardCommand.UseRing) != 0 && decision.TargetCardNetUid == 0 && decision.EquipCardNetUid == 0 && decision.LaneTarget == BoardLaneTarget.None && decision.DiscardCardNetUids.Count == 0;
                default: throw new NotSupportedException("Offline decision validation has not been restored for " + decision.Type + ".");
            }
        }

        public static bool ValidateDecision(Decision decision, CardCommandList list, ServerCardCatalog catalog, PlayerState player)
        {
            if (!ValidateDecision(decision, list)) return false;
            if (decision.Type == CcgRpc.DecisionType.CustomChoice) return OfflineConsumeChoices.ValidateLive(decision, list, catalog, player);
            if (decision.Type != CcgRpc.DecisionType.Play) return true;
            if (catalog == null || player == null) return false;
            foreach (CardCommandState command in list.ValidCommands)
                if (command.CardNetUid == decision.ActorCardNetUid || (command.SourceOverrideNetUid != 0 && command.SourceOverrideNetUid == decision.ActorCardNetUid))
                {
                    if ((command.ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0) return true;
                    GameZone.Position position;
                    return TryGetPlayPosition(catalog, player, catalog.FindDynamicCard(new CardNetUid(decision.ActorCardNetUid)), decision, out position);
                }
            return false;
        }

        // Resolve a native UI insertion boundary before any play mutation. For a full
        // list lane the UI still includes the selected sacrifice, so removing a card
        // to the left of the boundary shifts the insertion index by one.
        public static bool TryGetPlayPosition(ServerCardCatalog catalog, PlayerState player, ServerCard card, Decision decision, out GameZone.Position position)
        {
            position = new GameZone.Position();
            if (decision == null) return false;
            position = new GameZone.Position { LaneIndex = decision.LaneIndex, CardIndex = decision.PositionIndex };
            if (catalog == null || player == null || card == null || card.OwnerNetId != player.NetId
                || decision.PositionIndex < 0 || decision.DiscardCardNetUids.Count > 1) return false;
            GameZone board = catalog.FindGameZone(player.NetId, card.FindGameZoneName(GameZoneType.Board));
            if (board == null || decision.LaneIndex < 0 || decision.LaneIndex >= board.LaneList.Count) return false;
            ICardLane lane = board.LaneList[decision.LaneIndex];
            if (lane == null) return false;
            bool creature = board.Name == GameZoneName.CreatureBoard;
            if (!creature)
                return decision.PositionIndex == 0 && decision.DiscardCardNetUids.Count == 0 && !lane.IsLaneFull();
            // Other lane implementations need separate explicit slot semantics.
            if (!(lane is SizedCardLane) && !(lane is InfiniteCardLane))
                throw new NotSupportedException("Offline creature insertion requires a native list lane.");
            int count = lane.GetCurrentLength();
            if (decision.PositionIndex > count) return false;
            bool full = lane.IsLaneFull();
            if (full != (decision.DiscardCardNetUids.Count == 1)) return false;
            if (!full) return lane.IsSlotFree(position.CardIndex);
            ServerCard discard = catalog.FindDynamicCard(new CardNetUid(decision.DiscardCardNetUids[0]));
            if (discard == null || discard.OwnerNetId != player.NetId || discard.CurrentGameZoneName != board.Name
                || discard.CurrentLaneIndex != decision.LaneIndex || discard.QueryAttribute(AttributeType.Indestructible).Value > 0) return false;
            int discardIndex = lane.FindCardIndex(discard.InstanceId);
            if (discardIndex < 0 || count - 1 >= lane.GetCapacity()) return false;
            if (discardIndex < position.CardIndex) position.CardIndex--;
            return position.CardIndex >= 0 && position.CardIndex <= count - 1;
        }

        private static bool TargetSelectionValid(Decision decision, Google.Protobuf.Collections.RepeatedField<int> cards, Google.Protobuf.Collections.RepeatedField<BoardLaneTarget> lanes)
        {
            bool hasCard = decision.TargetCardNetUid != CardNetUid.GetInvalid().Id;
            bool hasLane = decision.LaneTarget != BoardLaneTarget.None;
            if (hasCard && !cards.Contains(decision.TargetCardNetUid)) return false;
            if (hasLane && !lanes.Contains(decision.LaneTarget)) return false;
            if (hasCard && hasLane) return false;
            return cards.Count == 0 && lanes.Count == 0 ? !hasCard && !hasLane : hasCard || hasLane;
        }

        public static bool PlayPrefix(ServerCard cardInstance, Card card, ref BoardLaneMask legalCreatureBoardLanes, PlayerConditionalArguments playerArguments, bool testBetray, ref bool __result) { __result = CanPlay(cardInstance, card, ref legalCreatureBoardLanes, playerArguments, testBetray); return false; }
        public static bool AttackPrefix(ServerCardCatalog cardCatalog, PlayerState player, PlayerState opponent, ServerCard attacker, ref bool __result) { __result = CanAttack(cardCatalog, player, opponent, attacker); return false; }
        public static bool ActivePrefix(ServerCard cardInstance, PlayerConditionalArguments playerArguments, ref bool __result) { __result = CanActivate(cardInstance, playerArguments); return false; }
        public static bool HintPrefix(ServerCardCatalog cardCatalog, PlayerState player, PlayerState opponent, ServerCard attacker, ref bool __result) { StrongHintRequirements r = attacker.StrongHintRequirements; __result = r != null && (r.PlayerRequirements.Count + r.CardRequirements.Count + r.LaneRequirements.Count > 0) && Requirements(r.PlayerRequirements, r.CardRequirements, r.LaneRequirements, PlayerArgs(cardCatalog, player, opponent, null), CardArgs(cardCatalog, attacker, null, LaneIdentifier.GetInvalid()), new LaneConditionalArguments(cardCatalog, CardLane(attacker), LaneIdentifier.GetInvalid())); return false; }
        public static bool SpellCardPrefix(ServerCard cardInstance, List<Effect> effects, List<DecisionConditionalExpression> targetSelector, ServerCard targetCard, ServerCardCatalog cardCatalog, PlayerConditionalArguments playerArguments, bool isDamageOnlySpell, bool useItemOwnerAsSource, ref bool __result) { __result = ValidCardTarget(cardInstance, effects, targetSelector, targetCard, cardCatalog, playerArguments, isDamageOnlySpell, useItemOwnerAsSource); return false; }
        public static bool SpellLanePrefix(ServerCard cardInstance, List<DecisionConditionalExpression> TargetSelector, GameZoneName zoneName, PlayerUniqueId zonePlayerId, int laneIndex, ServerCardCatalog cardCatalog, PlayerConditionalArguments playerArguments, ref bool __result) { __result = ValidLaneTarget(cardInstance, TargetSelector, zoneName, zonePlayerId, laneIndex, cardCatalog, playerArguments); return false; }
        public static bool AttackTargetsPrefix(ServerCardCatalog cardCatalog, PlayerState player, PlayerState opponent, ServerCard attacker, bool allowImmuneTargets, ref List<CardNetUid> __result) { __result = AttackTargets(cardCatalog, player, opponent, attacker, allowImmuneTargets); return false; }
        public static bool SpellTargetsPrefix(ServerCardCatalog __instance, ServerCard cardInstance, PlayTargetingType targetingType, List<Effect> effects, List<DecisionConditionalExpression> targetSelector, List<PlayerState> players, bool isDamageOnlySpell, bool useItemOwnerAsSource, ref List<CardNetUid> __result) { __result = SpellTargets(__instance, cardInstance, targetingType, effects, targetSelector, players, isDamageOnlySpell, useItemOwnerAsSource); return false; }
        public static bool LaneTargetsPrefix(ServerCardCatalog __instance, ServerCard cardInstance, PlayTargetingType targetingType, List<DecisionConditionalExpression> targetSelector, PlayerState player, PlayerState opponentPlayer, ref List<BoardLaneTarget> __result) { __result = LaneTargets(__instance, cardInstance, targetingType, targetSelector, player, opponentPlayer); return false; }
        public static bool MobilizePrefix(ServerCardCatalog __instance, ServerCard cardInstance, PlayerState player, ref List<BoardLaneTarget> __result) { __result = OfflineMobilize.LaneTargets(__instance, cardInstance, player); return false; }
        public static bool MainPrefix(ServerCardCatalog __instance, PlayerState player, PlayerState opponentPlayer, List<PlayerState> players, ref CardCommandItem __result) { __result = MainCommands(__instance, player, opponentPlayer, players); return false; }
        public static bool ProphecyPrefix(ServerCardCatalog __instance, PlayerState player, PlayerState opponentPlayer, List<PlayerState> players, ServerCard prophecyCard, bool allowPlayToHand, ref CardCommandItem __result) { __result = ProphecyCommands(__instance, player, opponentPlayer, players, prophecyCard, allowPlayToHand); return false; }
        public static bool BetrayPrefix() { throw new NotSupportedException("Betray sacrifice and replay command generation is not restored."); }
        public static bool EffectPrefix(ServerCardCatalog __instance, ServerEffectUtilities.TriggerContext context, PlayerState player, PlayerState opponentPlayer, List<PlayerState> players, List<CustomDisplayCard> displayCards, ref CardCommandItem __result) { __result = EffectCommands(__instance, context, player, opponentPlayer, players, displayCards); return false; }
        public static bool ValidatePrefix(Decision decision, CardCommandList commandList, ref bool __result) { __result = ValidateDecision(decision, commandList); return false; }
    }
}
