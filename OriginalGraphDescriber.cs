using System.Collections.Generic;
using System.Text;
using SPTESL;

namespace OfflineEngine
{
    // Field-by-field description of reconstructed effect graphs. Restoration modules compare an owned
    // graph's description with a freshly built reference; any unknown node type is described by its
    // type name only and therefore cannot equal a reference graph built from known native nodes.
    internal static class OriginalGraphDescriber
    {
        internal static string Ability(PlayCardAbility a)
        {
            StringBuilder s = new StringBuilder();
            s.Append("T").Append(a.PlayTargetingType).Append("|U").Append(a.CustomUiType).Append("|D").Append(a.IsDamageOnly)
                .Append("|C").Append(a.CustomUiEffectGroups == null ? -1 : a.CustomUiEffectGroups.Count)
                .Append("|P").Append(a.PostCustomUiEffectGroup == null ? -1 : a.PostCustomUiEffectGroup.Effects.Count);
            Selectors(s, a.TargetSelectors);
            Group(s, a.EffectGroup);
            return s.ToString();
        }

        internal static string Activated(ActivatedAbility a)
        {
            StringBuilder s = new StringBuilder();
            s.Append("Act:").Append(a.TargetingType).Append(":").Append(a.ActivationPlayerRequirements.Count).Append(a.ActivationCardRequirements.Count);
            foreach (IPlayerRequirement requirement in a.ActivationPlayerRequirements) s.Append("|activation:").Append(PlayerRequirement(requirement));
            Selectors(s, a.TargetSelectors);
            foreach (AbilityGroup g in a.AbilityGroups)
            {
                s.Append("|group:").Append(g.CustomUiType).Append(":").Append(g.OptionalCustomUiType).Append(":").Append(g.CustomUiEffectGroups.Count)
                    .Append(":").Append(g.PostCustomUiEffectGroup == null ? -1 : g.PostCustomUiEffectGroup.Effects.Count);
                Group(s, g.EffectGroup);
            }
            return s.ToString();
        }

        internal static string Auras(List<Aura> auras)
        {
            StringBuilder s = new StringBuilder("Auras:" + auras.Count);
            foreach (Aura a in auras)
            {
                s.Append("|aura:").Append(a.ExclusiveAura).Append(":").Append(a.HideStatusFx).Append(":").Append(a.CardRequirements.Count).Append(a.PlayerRequirements.Count);
                foreach (AuraEnchantment e in a.EnchantmentsToApply)
                {
                    s.Append("|ae:").Append(e.Name).Append(":").Append(e.UseBuffFx).Append(":").Append(e.UseDebuffFx).Append(":").Append(Value(e.StackCount)).Append(":").Append(Enchantment(e.Enchantment));
                    foreach (ICardConditionalExpression c in e.Selectors) s.Append(",").Append(Condition(c));
                }
            }
            return s.ToString();
        }

        internal static string Trigger(TriggeredEffect t)
        {
            StringBuilder s = new StringBuilder();
            s.Append("Trig:").Append(t.TriggerType).Append(":").Append(t.ClientTargetingType).Append(":").Append(t.CustomUiType)
                .Append(":").Append(t.TriggerValueRequirements.Count).Append(t.TriggerCardRequirements.Count).Append(t.TriggerPlayerRequirements.Count)
                .Append(t.BeforeTargetingCardRequirements.Count).Append(t.BeforeTargetingPlayerRequirements.Count)
                .Append(":").Append(t.AfterRuneBreaks).Append(t.AreaEffect).Append(t.AllowReentrancy).Append(t.IgnoredByClientLog).Append(t.OpponentChooses)
                .Append(t.SecretTarget).Append(t.ItemUsesWielderAsEffectSource).Append(t.OptionalCustomUiType).Append(t.UseGenericIcon)
                .Append(t.CanMergeEffectsWithSelf).Append(t.ComplexMergeEffectsWithSelf)
                .Append(":C").Append(t.CustomUiEffectGroups == null ? -1 : t.CustomUiEffectGroups.Count)
                .Append(":P").Append(t.PostCustomUiEffectGroup == null ? -1 : t.PostCustomUiEffectGroup.Effects.Count);
            Selectors(s, t.TargetSelectors);
            Group(s, t.EffectGroup);
            return s.ToString();
        }

        private static void Selectors(StringBuilder s, List<DecisionConditionalExpression> selectors)
        {
            if (selectors == null) { s.Append("|nullsel"); return; }
            foreach (DecisionConditionalExpression d in selectors)
            {
                s.Append("|sel:").Append(d.DecisionType).Append(":L").Append(d.LaneConditionalExpressions.Count);
                foreach (ILaneConditionalExpression l in d.LaneConditionalExpressions)
                {
                    LaneConditionals.LaneOwner owner = l as LaneConditionals.LaneOwner;
                    s.Append(",lane:").Append(owner == null ? (l == null ? "null" : l.GetType().FullName) : "Owner:" + owner.OwnedBy + ":" + owner.PlayerRole);
                }
                foreach (ICardConditionalExpression c in d.CardConditionalExpressions) s.Append(",").Append(Condition(c));
            }
        }

        private static void Group(StringBuilder s, EffectGroup group)
        {
            if (group == null) { s.Append("|nullgroup"); return; }
            foreach (Effect e in group.Effects)
            {
                s.Append("|effect:").Append(e.ExecuteValueRequirements.Count).Append(e.ExecuteCardRequirements.Count)
                    .Append(e.ExecutePlayerRequirements.Count).Append(e.ExecuteLaneRequirements.Count);
                foreach (IPlayerRequirement r in e.ExecutePlayerRequirements) s.Append("|preq:").Append(PlayerRequirement(r));
                foreach (CardEffectAction c in e.CardActions)
                {
                    s.Append("|card:").Append(c.HashId).Append(":").Append(Action(c.Action));
                    CardListSort sort = c.SelectorSort;
                    s.Append(":sort:").Append(sort == null ? "null" : sort.Method + "/" + Value(sort.NumToReturn) + "/" + sort.SortAttribute + "/" + sort.Order + "/" + sort.RequireUniqueNames);
                    s.Append(":fail:").Append(c.FailureCard == null ? "null" : c.FailureCard.BaseCard.Id + "/" + Value(c.FailureCard.Power)).Append(":").Append(c.FailureCardOwner);
                    foreach (ICardConditionalExpression x in c.Selectors) s.Append(",").Append(Condition(x));
                }
                foreach (PlayerEffectAction p in e.PlayerActions)
                {
                    s.Append("|player:").Append(PlayerAction(p.Action));
                    foreach (IPlayerConditionalExpression x in p.Selectors)
                    {
                        PlayerConditionals.PlayerRoleType r = x as PlayerConditionals.PlayerRoleType;
                        s.Append(",").Append(r == null ? (x == null ? "null" : x.GetType().FullName) : "Role:" + r.Check + ":" + r.Role);
                    }
                }
            }
        }

        private static string PlayerAction(IPlayerAction action)
        {
            PlayerActions.BreakRunesOfPlayer breakRunes = action as PlayerActions.BreakRunesOfPlayer;
            if (breakRunes != null) return "BreakRunes:" + Value(breakRunes.NumRunes) + ":" + breakRunes.AssetHash;
            PlayerActions.DamageAvatarOfPlayer damage = action as PlayerActions.DamageAvatarOfPlayer;
            if (damage != null) return "DamageAvatar:" + Value(damage.Amount) + ":" + damage.AssetHash;
            PlayerActions.CreateToken token = action as PlayerActions.CreateToken;
            if (token != null)
            {
                InsertPosition ip = token.InsertPosition;
                return "Token:" + token.CardReference.ReferenceStyle + ":" + token.CardReference.CardIdReference + ":" + token.CardReference.CardRole + ":"
                    + Collection(token.CardReference.RandomCreationSelectors) + ":" + token.CardReference.CustomUiCardIndex + ":" + token.CreateInZoneType + ":" + Value(token.NumTokens)
                    + ":" + token.FillLane + ":" + token.SanitizeRule + ":" + (ip == null ? "null" : ip.Type + "/" + ip.Comparison + "/" + ip.Lane + "/" + ip.Lhs + "/" + ip.InsertionType
                    + "/" + ip.LaneType + "/" + ip.UseOtherLaneIfFull) + ":" + token.RareAttributes.Count + ":" + Enchantment(token.TokenBuff) + ":"
                    + (token.FailureCard == null ? "null" : token.FailureCard.BaseCard.Id + "/" + Value(token.FailureCard.Power)) + ":" + token.MergeTokenMessages + ":" + token.RevealToken;
            }
            PlayerActions.ModifyMana mana = action as PlayerActions.ModifyMana;
            if (mana != null) return "ModifyMana:" + Value(mana.Amount);
            PlayerActions.DrawCards draw = action as PlayerActions.DrawCards;
            if (draw != null) return "Draw:" + Value(draw.NumCards) + ":" + draw.StopAtHandLimit + ":" + draw.IgnoreDeckingRule + ":" + draw.DrawFrom + ":" + draw.OperationType;
            return action == null ? "null" : action.GetType().FullName;
        }

        private static string PlayerRequirement(IPlayerRequirement r)
        {
            OfflineTurnMechanics.OpponentMoreHandRequirement hand = r as OfflineTurnMechanics.OpponentMoreHandRequirement;
            if (hand != null) return "OpponentMoreHand:" + hand.LhsPlayer;
            OfflineTurnMechanics.TotalDeathsRequirement deaths = r as OfflineTurnMechanics.TotalDeathsRequirement;
            if (deaths != null) return "TotalDeathsThisTurn:" + deaths.LhsPlayer + ":" + deaths.Minimum;
            PlayerRequirements.PlayerCountOfCardsRequirement count = r as PlayerRequirements.PlayerCountOfCardsRequirement;
            if (count != null)
            {
                PlayerConditionals.CountOfCards c = count.Conditional;
                StringBuilder s = new StringBuilder("CountOfCards:" + count.LhsPlayer + ":" + c.Count_Is + ":" + c.Value + ":" + c.PowerOfFiveCard + ":" + c.SuccessFailVO
                    + ":" + c.SuppressClientSuccessMesage + ":" + c.SuppressClientFailMessage);
                foreach (ICardConditionalExpression x in c.Selectors) s.Append(";").Append(Condition(x));
                return s.ToString();
            }
            PlayerRequirements.PlayerAttributeValueRequirement attr = r as PlayerRequirements.PlayerAttributeValueRequirement;
            if (attr != null) return "PlayerAttr:" + attr.LhsPlayer + ":" + attr.Conditional.AttributeName + ":" + attr.Conditional.Is + ":" + attr.Conditional.Value;
            return r == null ? "null" : r.GetType().FullName;
        }

        private static string Collection(List<ICollectionConditionalExpression> selectors)
        {
            StringBuilder s = new StringBuilder("coll" + selectors.Count);
            foreach (ICollectionConditionalExpression c in selectors)
            {
                CollectionConditionals.Subtype t = c as CollectionConditionals.Subtype;
                s.Append(";").Append(t == null ? (c == null ? "null" : c.GetType().FullName) : "Subtype:" + t.Check + ":" + t.SubtypeName);
            }
            return s.ToString();
        }

        internal static string Condition(ICardConditionalExpression c)
        {
            CardConditionals.MatchesCardResource match = c as CardConditionals.MatchesCardResource;
            if (match != null) return "CardResource:" + match.Check + ":" + match.SpecificMatch.CardIdReference;
            CardConditionals.Subtype t = c as CardConditionals.Subtype;
            if (t != null) return "Subtype:" + t.Check + ":" + t.SubtypeName + ":" + t.TestSubtypesOfDouble;
            CardConditionals.SubtypeCount n = c as CardConditionals.SubtypeCount;
            if (n != null) return "SubtypeCount:" + n.Is + ":" + n.Value + ":" + string.Join("/", n.Subtypes.ConvertAll(delegate(CardSubtype x) { return x.ToString(); }).ToArray());
            CardConditionals.Zone z = c as CardConditionals.Zone;
            if (z != null) return "Zone:" + z.Check + ":" + z.ZoneTypeMask.Bits;
            CardConditionals.Relationship r = c as CardConditionals.Relationship;
            if (r != null) return "Rel:" + r.OtherCard + ":" + r.Are;
            CardConditionals.CardRoleType role = c as CardConditionals.CardRoleType;
            if (role != null) return "Role:" + role.Check + ":" + role.Role;
            CardConditionals.AttributeValue av = c as CardConditionals.AttributeValue;
            if (av != null) return "Attr:" + av.AttributeName + ":" + av.Is + ":" + av.UseWielderAsSource + ":" + Conditional(av.Value);
            CardConditionals.Damaged d = c as CardConditionals.Damaged;
            if (d != null) return "Damaged:" + d.Check;
            CardConditionals.InTargetedLane targeted = c as CardConditionals.InTargetedLane;
            if (targeted != null) return "TargetedLane:" + targeted.Lane + ":" + targeted.Owner;
            CardConditionals.InSameLane lane = c as CardConditionals.InSameLane;
            if (lane != null) return "SameLane:" + lane.Same + ":" + lane.OtherCard + ":" + lane.UseBoardLaneIndex + ":" + lane.UseItemOwner;
            return c == null ? "null" : c.GetType().FullName;
        }

        internal static string Action(ICardAction action)
        {
            if (action is OfflineTurnMechanics.DeathScaledDamage) return "DeathScaledDamage";
            OfflineTurnMechanics.EnemyRunesScaledBuff runeBuff = action as OfflineTurnMechanics.EnemyRunesScaledBuff;
            if (runeBuff != null) return "EnemyRunesScaledBuff:" + Enchantment(runeBuff.Enchantment);
            OfflineTurnMechanics.DeathScaledBuff deathBuff = action as OfflineTurnMechanics.DeathScaledBuff;
            if (deathBuff != null) return "DeathScaledBuff:" + Enchantment(deathBuff.Enchantment);
            CardActions.Damage damage = action as CardActions.Damage;
            if (damage != null) return "Damage:" + Value(damage.HealthLoss) + ":" + damage.DamageSource + ":" + damage.UseItemWielderAsDamageSource + ":" + damage.SeparateLogMessages;
            CardActions.BuffAttributeByAttack attackBuff = action as CardActions.BuffAttributeByAttack;
            if (attackBuff != null) return "BuffAttributeByAttack:" + attackBuff.ToAttributeName + ":" + attackBuff.ModificationType + ":" + attackBuff.Permanent;
            CardActions.BuffAttributeByHealth healthBuff = action as CardActions.BuffAttributeByHealth;
            if (healthBuff != null) return "BuffAttributeByHealth:" + healthBuff.ToAttributeName + ":" + healthBuff.ModificationType + ":" + healthBuff.Permanent;
            CardActions.Destroy destroy = action as CardActions.Destroy;
            if (destroy != null) return "Destroy:" + destroy.Sacrifice;
            CardActions.Freeze freeze = action as CardActions.Freeze;
            if (freeze != null) return "Freeze:" + freeze.PermanentFreeze + ":" + freeze.Type;
            CardActions.Cover cover = action as CardActions.Cover;
            if (cover != null) return "Cover:" + cover.MultipleTurns;
            CardActions.Debuff debuff = action as CardActions.Debuff;
            if (debuff != null) return "Debuff:" + debuff.SkipLogging + ":" + debuff.SanitizeRule + ":" + Enchantment(debuff.Enchantment);
            CardActions.Buff buff = action as CardActions.Buff;
            if (buff != null) return "Buff:" + buff.IgnoreInClientLog + ":" + buff.SimultaneousFx + ":" + buff.SanitizeRule + ":" + Enchantment(buff.Enchantment);
            CardActions.Heal heal = action as CardActions.Heal;
            if (heal != null) return "Heal:" + Value(heal.HealthGain) + ":" + heal.FullHeal + ":" + heal.HealToAVDValue + ":" + heal.DrainFxSelector.Count;
            CardActions.GiveKeyword keyword = action as CardActions.GiveKeyword;
            if (keyword != null) return "Keyword:" + keyword.KeywordAttrib + ":" + keyword.RandomKeyword + ":" + keyword.DisplayTitle.RawKey + ":" + keyword.DisplayText.RawKey;
            CardActions.CopyAllKeywords copyKeywords = action as CardActions.CopyAllKeywords;
            if (copyKeywords != null)
            {
                StringBuilder s = new StringBuilder("CopyAllKeywords:").Append(copyKeywords.RemoveOnCopy);
                foreach (ICardConditionalExpression c in copyKeywords.CopyFromSelectors) s.Append(";").Append(Condition(c));
                return s.ToString();
            }
            CardActions.Fight fight = action as CardActions.Fight;
            if (fight != null)
            {
                StringBuilder s = new StringBuilder("Fight");
                foreach (ICardConditionalExpression c in fight.InciterSelectors) s.Append(";").Append(Condition(c));
                return s.ToString();
            }
            if (action is CardActions.Ward) return "Ward";
            if (action is CardActions.Silence) return "Silence";
            return action == null ? "null" : action.GetType().FullName;
        }

        private static string Enchantment(SPTESL.Enchantment e)
        {
            if (e == null) return "null";
            StringBuilder s = new StringBuilder();
            s.Append(e.MaxTurns).Append(e.DeferApplication).Append(e.DeferApplicationLast).Append(e.BasicItemEnchantment).Append(e.KeepWhenMovesOffBoard)
                .Append(e.ExpiresAtStartOfTurn).Append(e.ExpiresAtStartOfSourcesTurn).Append(e.ExpiresAtEndOfTurn).Append(e.ExpiresAtEndOfFight)
                .Append(e.ExpiresOnAttack).Append(e.ExpiresOnIncomingDamage).Append(e.ExpiresAtEndOfProphecy).Append(e.ExpiresAtEndOfTurnAfterNotAttacking)
                .Append(e.ExpiresAfterRuneBreak).Append(e.PermanentExpiresOnLeavingBoard).Append(e.PermanentExpiresOnChange).Append(e.ResetWoundedState)
                .Append(":").Append(e.GrantedEffects.Count).Append(e.GrantedAuras.Count).Append(e.AddedCreatureSubtypes.Count).Append(e.NewColorSubtypes.Count)
                .Append(e.SubcardDistributionSubtypes.Count).Append(e.MechanicsEntries.Count).Append(e.CardReferences.Count).Append(e.TagReferences.Count);
            foreach (TriggeredEffect granted in e.GrantedEffects) s.Append(";granted{").Append(Trigger(granted)).Append("}");
            foreach (EnchantmentAttributeModification m in e.AttributeModifications)
                s.Append(";").Append(m.ModificationType).Append(":").Append(m.AttributeName).Append(":").Append(m.Value).Append(":").Append(m.DynamicValue).Append(":").Append(m.LockedDynamicValue);
            return s.ToString();
        }

        internal static string Value(ActionValueData v)
        {
            return v == null ? "null" : v.ValueMethod + ":" + v.ConstantValue + ":" + v.ConstantMultiplier + ":" + v.MinValue + ":" + v.MaxValue + ":" + v.Selectors.Count;
        }
        private static string Conditional(ConditionalValueData v)
        {
            return v == null ? "null" : v.ValueMethod + ":" + v.ConstantValue + ":" + v.ConstantMultiplier + ":" + v.MaxValue + ":" + v.AttributeName + ":" + v.RareAttributeName
                + ":" + v.CardRole + ":" + v.PlayerRole + ":" + v.Selectors.Count + ":" + v.Values.Count;
        }
    }
}
