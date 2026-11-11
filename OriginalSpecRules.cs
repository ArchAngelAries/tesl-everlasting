using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FullSerializer;
using SPTESL;

namespace OfflineEngine
{
    // Emits native effect graphs from tools/card_compiler.py output (compiled-card-specs.json), which is parsed from the
    // exact installed card text. Reconstructed wrappers, not recovered server graphs: every effect runs in intact native
    // actions/conditionals already proven by the hand-written batches. A card is emitted only when its installed
    // description equals the spec's raw text and every ability maps onto supported native nodes; otherwise it is skipped
    // with a recorded reason (see spec-emission-report.tsv). Never guesses beyond the compiled spec.
    public static class OriginalSpecRules
    {
        private sealed class Target { public string Kind, Pick, Side, Lane, Who, PowerCmp, Tribe, Zone, CardType, Keyword; public bool Other, Avatar, Wounded; public int Power = -1, CostMin = -1, CardId; }
        private sealed class Pool { public List<string> Types = new List<string>(); public int CostMin = -1, CostMax = -1, CardId; public string Keyword; }
        private sealed class Eff { public string Action, Keyword, Token, Place, Zone, ValueFrom, DamageSource; public int Amount, Attack, Health, Count; public bool HasStats, Temporary, Fill; public Target Target, Per; public Pool Pool; public fsData Condition; }
        private sealed class Ability { public List<string> Triggers = new List<string>(); public List<Eff> Effects = new List<Eff>(); public Target Event; public fsData ActivationCondition; public bool Consume, PostPlacement; }
        private sealed class Spec { public int Id; public string Raw, Definition; public List<Ability> Abilities = new List<Ability>(); }
        private sealed class Graph
        {
            public List<TriggeredEffect> Triggers = new List<TriggeredEffect>();
            public PlayCardAbility Play; public ActivatedAbility Activated; public List<Aura> Auras = new List<Aura>();
        }

        private static readonly Dictionary<int, Graph> prepared = new Dictionary<int, Graph>();
        private static readonly Dictionary<Card, string> owned = new Dictionary<Card, string>();
        private static readonly Dictionary<int, string> skipped = new Dictionary<int, string>();
        public static int PreparedCount { get { return prepared.Count; } }
        public static bool IsOriginalId(int id) { return prepared.ContainsKey(id); }
        // A compiled spec or the adopted original graph (OriginalGraphRules prefer-original.tsv): the card has restored rules.
        public static bool HasRestoredRules(int id) { return prepared.ContainsKey(id) || OriginalGraphRules.Prefers(id); }
        public static IEnumerable<int> PreparedIds { get { return prepared.Keys; } }
        public static string SkipReason(int id) { string r; return skipped.TryGetValue(id, out r) ? r : null; }

        // Parse and compile into pending graphs without mutating cards. alreadySupported: IDs owned by other rule classes.
        public static void Prepare(GameConfiguration config, string directory, Predicate<int> alreadyOwned)
        {
            prepared.Clear(); skipped.Clear();
            string path = Path.Combine(directory, "compiled-card-specs.json");
            if (!File.Exists(path)) return;
            fsData root = fsJsonParser.Parse(File.ReadAllText(path));
            foreach (fsData item in root.AsList)
            {
                Dictionary<string, fsData> d = item.AsDictionary;
                string status = d["status"].AsString;
                if (status != "compiled" && !(status == "notext" && d["definition"].AsString != "Double" && d["definition"].AsString != "Action")) continue;
                Spec spec = ReadSpec(d);
                if (alreadyOwned(spec.Id)) continue;
                Card card = config.FindCardById(spec.Id);
                if (card == null) { skipped[spec.Id] = "card absent"; continue; }
                if (SameText(card.Description) != SameText(spec.Raw)) { skipped[spec.Id] = "installed text differs from compiled spec"; continue; }
                try
                {
                    if (!owned.ContainsKey(card)) OriginalCardRules.ValidateNoExtraRules(card);
                    prepared[spec.Id] = Build(config, card, spec);
                }
                catch (NotSupportedException e) { skipped[spec.Id] = e.Message; }
            }
        }

        // The audit TSV stores line breaks as escaped "\n" and a missing description as ""; compare the same text either way.
        private static string SameText(string text)
        { return System.Text.RegularExpressions.Regex.Replace((text ?? "").Replace("\\n", "\n").Replace("\\r", ""), @"\s+", " ").Trim(); }

        public static int[] Restore(GameConfiguration config, string directory)
        {
            List<int> ids = new List<int>();
            StringBuilder report = new StringBuilder("card_id\tresult\tdetail\n");
            foreach (KeyValuePair<int, Graph> pair in prepared)
            {
                Card card = config.FindCardById(pair.Key);
                string description = Describe(pair.Value);
                string previous;
                if (owned.TryGetValue(card, out previous))
                {
                    if (previous != Describe(Current(card))) throw new NotSupportedException("Owned spec graph changed or gained extra behavior: " + pair.Key);
                }
                else
                {
                    card.TriggeredEffects = pair.Value.Triggers;
                    card.ActionPlayInformation = pair.Value.Play == null ? new List<PlayCardAbility>() : new List<PlayCardAbility> { pair.Value.Play };
                    card.ActivatedAbility = pair.Value.Activated ?? new ActivatedAbility();
                    card.Auras = pair.Value.Auras;
                    owned[card] = description;
                }
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                ids.Add(pair.Key);
                report.AppendLine(pair.Key + "\temitted\t" + card.Name);
            }
            foreach (KeyValuePair<int, string> pair in skipped) report.AppendLine(pair.Key + "\tskipped\t" + pair.Value.Replace("\t", " "));
            File.WriteAllText(Path.Combine(directory, "spec-emission-report.tsv"), report.ToString());
            return ids.ToArray();
        }

        private static Graph Current(Card card)
        {
            return new Graph { Triggers = card.TriggeredEffects ?? new List<TriggeredEffect>(), Play = card.ActionPlayInformation != null && card.ActionPlayInformation.Count == 1 ? card.ActionPlayInformation[0] : null,
                Activated = card.ActivatedAbility, Auras = card.Auras ?? new List<Aura>() };
        }
        private static string Describe(Graph g)
        {
            StringBuilder s = new StringBuilder();
            foreach (TriggeredEffect t in g.Triggers) s.Append("T{").Append(OriginalGraphDescriber.Trigger(t)).Append("}");
            if (g.Play != null) s.Append("P{").Append(OriginalGraphDescriber.Ability(g.Play)).Append("}");
            if (g.Activated != null && g.Activated.AbilityGroups.Count > 0) s.Append("A{").Append(OriginalGraphDescriber.Activated(g.Activated)).Append("}");
            if (g.Auras.Count > 0) s.Append("U{").Append(OriginalGraphDescriber.Auras(g.Auras)).Append("}");
            return s.ToString();
        }

        // ------------------------------------------------------------------ reading
        private static Spec ReadSpec(Dictionary<string, fsData> d)
        {
            Spec spec = new Spec { Id = (int)d["id"].AsInt64, Raw = d["raw"].AsString, Definition = d["definition"].AsString };
            foreach (fsData a in d["abilities"].AsList)
            {
                Ability ability = new Ability();
                ability.Consume = Bool(a.AsDictionary, "consume");
                ability.PostPlacement = Bool(a.AsDictionary, "postPlacement");
                if (a.AsDictionary.ContainsKey("activationCondition") && a.AsDictionary["activationCondition"].IsDictionary)
                    ability.ActivationCondition = a.AsDictionary["activationCondition"];
                foreach (fsData t in a.AsDictionary["trigger"].AsList) ability.Triggers.Add(t.AsString);
                if (a.AsDictionary.ContainsKey("event") && a.AsDictionary["event"].IsDictionary) ability.Event = ReadTarget(a.AsDictionary["event"].AsDictionary);
                foreach (fsData e in a.AsDictionary["effects"].AsList) ability.Effects.Add(ReadEffect(e.AsDictionary));
                spec.Abilities.Add(ability);
            }
            return spec;
        }
        private static Eff ReadEffect(Dictionary<string, fsData> e)
        {
            Eff eff = new Eff { Action = e["action"].AsString };
            eff.Amount = Int(e, "amount"); eff.Count = Int(e, "count");
            if (e.ContainsKey("attack") && !e["attack"].IsNull) { eff.Attack = (int)e["attack"].AsInt64; eff.HasStats = true; }
            if (e.ContainsKey("health") && !e["health"].IsNull) { eff.Health = (int)e["health"].AsInt64; eff.HasStats = true; }
            eff.Keyword = Str(e, "keyword"); eff.Token = Str(e, "token"); eff.Place = Str(e, "place");
            eff.ValueFrom = Str(e, "valueFrom");
            eff.DamageSource = Str(e, "damageSource");
            if (eff.ValueFrom != null && ((eff.ValueFrom != "consumedPower" && eff.ValueFrom != "consumedHealth" && eff.ValueFrom != "consumedStats" && eff.ValueFrom != "eventValue" && eff.ValueFrom != "turnDeaths" && eff.ValueFrom != "enemyRunesLost" && eff.ValueFrom != "selfPower")
                || (eff.Action != "damage" && eff.Action != "heal" && eff.Action != "stats" && eff.Action != "draw") || e.ContainsKey("amount") || e.ContainsKey("per")))
                throw new NotSupportedException("invalid consumed value expression");
            eff.Zone = Str(e, "zone"); eff.Temporary = Bool(e, "temporary"); eff.Fill = Bool(e, "fill");
            if (e.ContainsKey("pool") && e["pool"].IsDictionary)
            {
                Dictionary<string, fsData> pd = e["pool"].AsDictionary;
                eff.Pool = new Pool { Keyword = Str(pd, "keyword") };
                if (pd.ContainsKey("types") && pd["types"].IsList) foreach (fsData x in pd["types"].AsList) eff.Pool.Types.Add(x.AsString);
                if (pd.ContainsKey("costMin") && pd["costMin"].IsInt64) eff.Pool.CostMin = (int)pd["costMin"].AsInt64;
                if (pd.ContainsKey("costMax") && pd["costMax"].IsInt64) eff.Pool.CostMax = (int)pd["costMax"].AsInt64;
                if (pd.ContainsKey("cardId") && pd["cardId"].IsInt64) eff.Pool.CardId = (int)pd["cardId"].AsInt64;
            }
            if (e.ContainsKey("per") && e["per"].IsDictionary) eff.Per = ReadTarget(e["per"].AsDictionary);
            if (e.ContainsKey("condition")) eff.Condition = e["condition"];
            if (e.ContainsKey("target") && e["target"].IsDictionary) eff.Target = ReadTarget(e["target"].AsDictionary);
            return eff;
        }
        private static Target ReadTarget(Dictionary<string, fsData> t)
        {
            Target target = new Target { Kind = Str(t, "kind"), Pick = Str(t, "pick"), Side = Str(t, "side"), Lane = Str(t, "lane"), Who = Str(t, "who"),
                Other = Bool(t, "other"), Avatar = Bool(t, "avatar"), Wounded = Bool(t, "wounded"), Tribe = Str(t, "tribe"),
                Zone = Str(t, "zone"), CardType = Str(t, "cardType"), Keyword = Str(t, "keyword"), CardId = Int(t, "cardId") };
            if (t.ContainsKey("power") && t["power"].IsList) { target.PowerCmp = t["power"].AsList[0].AsString; target.Power = (int)t["power"].AsList[1].AsInt64; }
            if (t.ContainsKey("costMin") && t["costMin"].IsInt64) target.CostMin = (int)t["costMin"].AsInt64;
            return target;
        }
        private static int Int(Dictionary<string, fsData> d, string k) { return d.ContainsKey(k) && d[k].IsInt64 ? (int)d[k].AsInt64 : 0; }
        private static string Str(Dictionary<string, fsData> d, string k) { return d.ContainsKey(k) && d[k].IsString ? d[k].AsString : null; }
        private static bool Bool(Dictionary<string, fsData> d, string k) { return d.ContainsKey(k) && d[k].IsBool && d[k].AsBool; }

        // ------------------------------------------------------------------ building
        private static Graph Build(GameConfiguration config, Card card, Spec spec)
        {
            Graph g = new Graph();
            bool creature = spec.Definition == "Creature", action = spec.Definition == "Action", support = spec.Definition == "Support";
            List<Eff> playEffects = new List<Eff>();
            Target playChoice = null;
            bool nativeOnly = spec.Abilities.Count == 0 && !action; // no rules text: complete with native metadata
            foreach (Ability ability in spec.Abilities)
                foreach (string trigger in ability.Triggers)
                {
                    if (ability.ActivationCondition != null && trigger != "Activate") throw new NotSupportedException("activation condition outside Activate");
                    if (ability.PostPlacement && (trigger != "Summon" || !creature || ability.Triggers.Count != 1))
                        throw new NotSupportedException("post-placement choice requires a creature Summon");
                    // Sentences fully represented by installed native attributes (immunities, keyword lists, item stats).
                    if (trigger == "Native") { nativeOnly = true; continue; }
                    if (!ability.Consume && ability.Effects.Exists(delegate(Eff e) { return e.ValueFrom != null && e.ValueFrom != "eventValue" && e.ValueFrom != "turnDeaths" && e.ValueFrom != "enemyRunesLost" && e.ValueFrom != "selfPower"; }))
                        throw new NotSupportedException("consumed value outside Consume");
                    if (ability.Consume && ability.Effects.Exists(delegate(Eff e) { return e.ValueFrom == "turnDeaths" || e.ValueFrom == "enemyRunesLost" || e.ValueFrom == "selfPower"; }))
                        throw new NotSupportedException("dynamic player count inside Consume is not supported");
                    if (ability.Consume)
                    {
                        if ((trigger != "Summon" && trigger != "Pilfer") || (!creature && spec.Definition != "Item") || ability.Triggers.Count != 1)
                            throw new NotSupportedException("Consume currently requires a creature or item Summon");
                        TriggeredEffect consume = new TriggeredEffect { TriggerType = EffectTriggerType.Summon,
                            SortOrder = TriggerSortOrder.EndOfActiveQueue, ClientTargetingType = PlayTargetingType.NoTarget,
                            CustomUiType = CustomCardUiType.TargetCemetery, EffectGroup = new EffectGroup(),
                            CustomUiEffectGroups = new List<EffectGroup>(), PostCustomUiEffectGroup = new EffectGroup() };
                        Effect build = new Effect();
                        build.CardActions.Add(CardAction(new OfflineConsumeChoices.BuildCemeteryChoices(),
                            new Target { Kind = "card", Pick = "all", Side = "friendly", Zone = "discard", CardType = "Creature" }, trigger));
                        consume.EffectGroup.Effects.Add(build);
                        EffectGroup selected = new EffectGroup();
                        Effect remove = new Effect();
                        CardEffectAction native = new CardEffectAction { HashId = -1, Action = new CardActions.Consume() };
                        native.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.CustomUITarget });
                        remove.CardActions.Add(native); selected.Effects.Add(remove);
                        Target rewardChoice = null;
                        foreach (Eff e in ability.Effects)
                            if (e.Target != null && e.Target.Pick == "choose") {
                                if (rewardChoice != null && !SameTarget(rewardChoice, e.Target)) throw new NotSupportedException("multiple Consume reward targets");
                                rewardChoice = e.Target;
                            }
                        string valueFrom = null;
                        foreach (Eff e in ability.Effects) if (e.ValueFrom != null) {
                            if (valueFrom != null && valueFrom != e.ValueFrom) throw new NotSupportedException("multiple consumed values in one reward");
                            valueFrom = e.ValueFrom;
                        }
                        if (valueFrom == "consumedStats") {
                            if (ability.Effects.Count != 1 || ability.Effects[0].Action != "stats" || ability.Effects[0].Target.Kind != "self"
                                || ability.Effects[0].Attack != 1 || ability.Effects[0].Health != 1 || ability.Effects[0].Temporary || ability.Effects[0].Condition != null)
                                throw new NotSupportedException("consumedStats requires one permanent self +power/+health reward");
                            TriggeredEffect powerReward = ConsumedStatReward(AttributeType.Attack);
                            TriggeredEffect healthReward = ConsumedStatReward(AttributeType.MaxHealth);
                            native.Action = new OfflineConsumeChoices.ConsumeValueReward { Reward = powerReward, HealthReward = healthReward, Attribute = AttributeType.Attack };
                        }
                        else if (valueFrom != null) {
                            TriggeredEffect reward = new TriggeredEffect { TriggerType = EffectTriggerType.Summon,
                                SortOrder = TriggerSortOrder.EndOfActiveQueue, ClientTargetingType = rewardChoice == null ? PlayTargetingType.NoTarget : PlayTargetingType.RequiredTarget,
                                EffectGroup = new EffectGroup() };
                            if (rewardChoice != null) reward.TargetSelectors.Add(Decision(rewardChoice, trigger));
                            foreach (Eff e in ability.Effects) reward.EffectGroup.Effects.Add(Effect(config, card, e, trigger));
                            native.Action = new OfflineConsumeChoices.ConsumeValueReward { Reward = reward,
                                Attribute = valueFrom == "consumedPower" ? AttributeType.Attack : AttributeType.MaxHealth };
                        }
                        else if (rewardChoice == null)
                            foreach (Eff e in ability.Effects) selected.Effects.Add(Effect(config, card, e, trigger));
                        else {
                            TriggeredEffect reward = new TriggeredEffect { TriggerType = EffectTriggerType.Summon,
                                SortOrder = TriggerSortOrder.EndOfActiveQueue, ClientTargetingType = PlayTargetingType.RequiredTarget,
                                EffectGroup = new EffectGroup() };
                            reward.TargetSelectors.Add(Decision(rewardChoice, trigger));
                            foreach (Eff e in ability.Effects) reward.EffectGroup.Effects.Add(Effect(config, card, e, trigger));
                            Effect queue = new Effect();
                            queue.CardActions.Add(CardAction(new OfflineConsumeChoices.QueueReward { Reward = reward }, new Target { Kind = "self" }, trigger));
                            selected.Effects.Add(queue);
                        }
                        consume.CustomUiEffectGroups.Add(selected);
                        if (trigger == "Pilfer") {
                            TriggeredEffect outer = new TriggeredEffect { TriggerType = EffectTriggerType.Pilfer, SortOrder = TriggerSortOrder.EndOfActiveQueue,
                                ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
                            Effect begin = new Effect(); begin.CardActions.Add(CardAction(new OfflineConsumeChoices.BeginChoice { Choice = consume }, new Target { Kind = "self" }, trigger));
                            outer.EffectGroup.Effects.Add(begin); g.Triggers.Add(outer);
                        }
                        else g.Triggers.Add(consume);
                        continue;
                    }
                    bool chooses = ability.Effects.Exists(delegate(Eff e) { return e.Target != null && e.Target.Pick == "choose"; });
                    // A Summon choice relative to the creature's own lane is made after it is placed (a triggered decision), not at play.
                    bool laneRelative = ability.Effects.Exists(delegate(Eff e) { return e.Target != null && e.Target.Pick == "choose" && e.Target.Lane != null; });
                    if ((trigger == "Play" && action) || (trigger == "Summon" && creature && chooses && !laneRelative && !ability.PostPlacement))
                    {
                        foreach (Eff e in ability.Effects)
                            if (e.Target != null && e.Target.Pick == "choose")
                            {
                                if (playChoice != null && !SameTarget(playChoice, e.Target)) throw new NotSupportedException("multiple independent play targets");
                                playChoice = e.Target;
                            }
                        playEffects.AddRange(ability.Effects);
                        continue;
                    }
                    EffectTriggerType type;
                    switch (trigger)
                    {
                        case "Summon": type = EffectTriggerType.Summon; break;
                        case "SelfConsumed": case "SelfConsumes": type = EffectTriggerType.WhenCardConsumes; break;
                        case "LastGasp": type = EffectTriggerType.LastGaspTrigger; break;
                        case "Slay": type = EffectTriggerType.Slay; break;
                        case "Pilfer": type = EffectTriggerType.Pilfer; break;
                        case "Veteran": type = EffectTriggerType.Veteran; break;
                        case "Plot": type = EffectTriggerType.Plot; break;
                        case "Expertise": type = EffectTriggerType.Expertise; break;
                        case "StartOfTurn": case "StartOfEachTurn": type = EffectTriggerType.WhenPlayerTurnStarts; break;
                        case "EndOfTurn": case "EndOfEachTurn": type = EffectTriggerType.WhenPlayerTurnEnds; break;
                        case "SummonOther": case "CreatureSummoned": type = EffectTriggerType.WhenCardEntersBoard; break;
                        case "CreatureDies": type = EffectTriggerType.WhenCardDies; break;
                        case "CardDrawn": type = EffectTriggerType.WhenCardIsDrawn; break;
                        case "SelfDamaged": case "SelfDamagedByCreature": case "SelfDamagedSurvives": case "CreatureDamaged": type = EffectTriggerType.WhenCardTakesDamage; break;
                        case "SelfAttacks": case "SelfAttacksGuard": case "CreatureAttacks": type = EffectTriggerType.WhenCardAttacks; break;
                        case "AfterSelfAttacks": type = EffectTriggerType.WhenCardAttackComplete; break;
                        case "SelfDefends": type = EffectTriggerType.WhenCardDefends; break;
                        case "SelfWardBroken": type = EffectTriggerType.WhenCardWardIsBroken; break;
                        case "SelfMoves": case "CreatureMoves": type = EffectTriggerType.WhenCardChangesLane; break;
                        case "SelfEquipped": case "FriendlyCreatureEquipped": type = EffectTriggerType.WhenCardIsEquipped; break;
                        case "RuneDestroyed": type = EffectTriggerType.WhenCardBreaksRune; break;
                        case "HealthGained": type = EffectTriggerType.WhenCardIsHealed; break;
                        case "AfterAction": type = EffectTriggerType.WhenCardIsPlayed; break;
                        case "Activate":
                            if (!support || g.Activated != null) throw new NotSupportedException("Activate outside a single support ability");
                            g.Activated = new ActivatedAbility(); AbilityGroup group = new AbilityGroup();
                            if (ability.ActivationCondition != null) g.Activated.ActivationPlayerRequirements.Add(Condition(ability.ActivationCondition));
                            foreach (Eff e in ability.Effects) group.EffectGroup.Effects.Add(Effect(config, card, e, trigger));
                            g.Activated.AbilityGroups.Add(group); g.Activated.TargetingType = PlayTargetingType.NoTarget;
                            continue;
                        case "Aura":
                            foreach (Eff e in ability.Effects) g.Auras.Add(AuraOf(e));
                            continue;
                        default: throw new NotSupportedException("trigger " + trigger + " not emitted yet");
                    }
                    // Items carry creature keyword triggers that act through the wielder (OfflineItemTriggers).
                    if ((type == EffectTriggerType.Summon || type == EffectTriggerType.LastGaspTrigger || type == EffectTriggerType.Slay
                        || type == EffectTriggerType.Pilfer || type == EffectTriggerType.Veteran) && !creature && spec.Definition != "Item"
                        && !(support && type == EffectTriggerType.Summon))
                        throw new NotSupportedException(trigger + " on non-creature");
                    if (type == EffectTriggerType.Expertise && action) throw new NotSupportedException("Expertise on an action");
                    TriggeredEffect te = new TriggeredEffect { TriggerType = type, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
                    AddTriggerRequirements(te, trigger);
                    AddEventRequirements(te, trigger, ability.Event);
                    // A single conditional chosen Summon has no decision when its condition is false.
                    // Retain the effect requirement as well, in case state changes before resolution.
                    if (ability.PostPlacement && chooses && ability.Effects.Count == 1 && ability.Effects[0].Condition != null)
                        te.TriggerPlayerRequirements.Add(Condition(ability.Effects[0].Condition));
                    // Event-driven triggers resolve from the queue (not Instant): native movement events (draw, lane change) must not
                    // run card effects synchronously inside a move (CatalogMovementRestoration rejects that).
                    if (Array.IndexOf(EventTriggers, trigger) >= 0 || trigger == "SummonOther" || trigger == "AfterAction") te.SortOrder = TriggerSortOrder.EndOfActiveQueue;
                    // Consume emits its event after the move has completed. Imbued rewards must precede
                    // the consumer's following effects (e.g. Drain before Dragonknight damage).
                    // Ordering reconstructed from reference behavior; native Instant implements it.
                    if (trigger == "SelfConsumed") te.SortOrder = TriggerSortOrder.Instant;
                    if (chooses)
                    {
                        // Player-chosen target for a triggered ability: the restored lifecycle pushes a native ChooseTarget decision
                        // (human UI or AI ChoiceScore); with no legal target the decision is skipped and the effect finds nothing.
                        Target choice = null;
                        foreach (Eff e in ability.Effects)
                            if (e.Target != null && e.Target.Pick == "choose")
                            {
                                if (choice != null && !SameTarget(choice, e.Target)) throw new NotSupportedException("multiple independent triggered targets");
                                choice = e.Target;
                            }
                        te.ClientTargetingType = PlayTargetingType.RequiredTarget;
                        te.TargetSelectors.Add(Decision(choice, trigger));
                        // The native default sort order (Instant) resolves a trigger inside its event without a decision; a chosen
                        // target needs the queued path so the lifecycle pump can push the ChooseTarget decision.
                        te.SortOrder = TriggerSortOrder.EndOfActiveQueue;
                    }
                    foreach (Eff e in ability.Effects) te.EffectGroup.Effects.Add(Effect(config, card, e, trigger));
                    g.Triggers.Add(te);
                }
            // Activated abilities with a chosen target.
            if (support && spec.Abilities.Exists(delegate(Ability a) { return a.Triggers.Contains("Activate") && a.Effects.Exists(delegate(Eff e) { return e.Target != null && e.Target.Pick == "choose"; }); }))
            {
                Ability a = spec.Abilities.Find(delegate(Ability x) { return x.Triggers.Contains("Activate"); });
                Target choice = a.Effects.Find(delegate(Eff e) { return e.Target != null && e.Target.Pick == "choose"; }).Target;
                g.Activated.TargetingType = PlayTargetingType.RequiredTarget;
                g.Activated.TargetSelectors.Add(Decision(choice, "Activate"));
            }
            if (playEffects.Count > 0)
            {
                EffectGroup group = new EffectGroup();
                foreach (Eff e in playEffects) group.Effects.Add(Effect(config, card, e, "Play"));
                g.Play = new PlayCardAbility { EffectGroup = group, PlayTargetingType = playChoice == null ? PlayTargetingType.NoTarget : creature ? PlayTargetingType.OptionalTarget : PlayTargetingType.RequiredTarget };
                if (playChoice != null)
                {
                    if (creature && playChoice.Lane != null) throw new NotSupportedException("drop-lane-relative Summon target");
                    g.Play.TargetSelectors.Add(Decision(playChoice, "Play"));
                }
            }
            if (g.Triggers.Count == 0 && g.Play == null && g.Activated == null && g.Auras.Count == 0 && !nativeOnly) throw new NotSupportedException("no emitted ability");
            if (action && g.Play == null) throw new NotSupportedException("action without a play ability");
            return g;
        }

        // Persistent (board) triggers: native turn/summon/play events are gathered from every dynamic card, so the source
        // must be on the board; "your turn" / "you summon" / "you play" restrict the event player or card to the owner.
        private static void AddTriggerRequirements(TriggeredEffect te, string trigger)
        {
            bool turn = trigger == "StartOfTurn" || trigger == "EndOfTurn", each = trigger == "StartOfEachTurn" || trigger == "EndOfEachTurn";
            if (!turn && !each && trigger != "SummonOther" && trigger != "AfterAction") return;
            te.TriggerCardRequirements.Add(new CardRequirements.ZoneRequirement { LhsCard = CardRole.Self, Conditional = Board() });
            if (turn)
            {
                PlayerRequirements.PlayerEventRoleRequirement mine = new PlayerRequirements.PlayerEventRoleRequirement { LhsPlayer = PlayerRole.Self };
                mine.Conditional.Check = PlayerConditionals.PlayerRoleType.Requirement.Is; mine.Conditional.Role = PlayerRole.Inciter;
                te.TriggerPlayerRequirements.Add(mine);
            }
            if (trigger == "SummonOther" || trigger == "AfterAction")
            {
                te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.Inciter, Conditional = Subtype(trigger == "SummonOther" ? CardSubtype.Creature : CardSubtype.Spell) });
                te.TriggerCardRequirements.Add(new CardRequirements.RelationshipRequirement { LhsCard = CardRole.Inciter, Conditional = Rel(true) });
                te.TriggerCardRequirements.Add(new CardRequirements.EventRoleRequirement { LhsCard = CardRole.Inciter,
                    Conditional = new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = CardRole.Self } });
            }
        }

        // Event triggers (native event roles: TriggerEvent(type, inciter, target)). The source must be on the board; the event card is
        // matched by the optional ability "event" spec (side, tribe, card type, keyword, other), relative to the source.
        //   CreatureSummoned/SummonOther, CreatureDies, CardDrawn: inciter = the summoned / dying / drawn card.
        //   SelfDamaged, SelfAttacks, SelfWardBroken, SelfMoves: inciter = this card.  SelfEquipped: target (wielder) = this card.
        //   RuneDestroyed: inciter = the avatar whose rune broke (event side picks whose).  HealthGained: target = your avatar.
        private static readonly string[] EventTriggers = { "CreatureSummoned", "CreatureDies", "CardDrawn", "CreatureAttacks", "SelfDamaged", "SelfDamagedByCreature", "SelfDamagedSurvives", "CreatureDamaged", "SelfAttacks", "SelfAttacksGuard", "SelfWardBroken",
            "SelfMoves", "CreatureMoves", "SelfEquipped", "FriendlyCreatureEquipped", "SelfDefends", "AfterSelfAttacks", "RuneDestroyed", "HealthGained", "SelfConsumed", "SelfConsumes" };
        private static void AddEventRequirements(TriggeredEffect te, string trigger, Target ev)
        {
            if (Array.IndexOf(EventTriggers, trigger) < 0) { if (ev != null && trigger != "SummonOther") throw new NotSupportedException("event filter on " + trigger); return; }
            // Native Consume first banishes the cemetery card, then raises the event with it as Target.
            // Its printed "when consumed" ability must remain eligible in Purgatory, not require the board.
            if (trigger == "SelfConsumed") { te.TriggerCardRequirements.Add(Is(CardRole.EventTarget, CardRole.Self)); return; }
            te.TriggerCardRequirements.Add(new CardRequirements.ZoneRequirement { LhsCard = CardRole.Self, Conditional = Board() });
            switch (trigger)
            {
                case "SelfDamaged": case "SelfAttacks": case "AfterSelfAttacks": case "SelfWardBroken": case "SelfMoves": case "SelfConsumes":
                    te.TriggerCardRequirements.Add(Is(CardRole.Inciter, CardRole.Self)); return;
                case "SelfAttacksGuard":
                    te.TriggerCardRequirements.Add(Is(CardRole.Inciter, CardRole.Self));
                    te.TriggerCardRequirements.Add(new CardRequirements.AttributeValue { LhsCard = CardRole.EventTarget, Conditional = new CardConditionals.AttributeValue {
                        AttributeName = AttributeType.Taunt, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(1) } });
                    return;
                case "SelfDamagedByCreature":
                    te.TriggerCardRequirements.Add(Is(CardRole.Inciter, CardRole.Self));
                    te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.EventTarget, Conditional = Subtype(CardSubtype.Creature) });
                    return;
                case "SelfDamagedSurvives":
                    te.TriggerCardRequirements.Add(Is(CardRole.Inciter, CardRole.Self));
                    te.TriggerCardRequirements.Add(new CardRequirements.IsDyingRequirement { LhsCard = CardRole.Self,
                        Conditional = new CardConditionals.IsDying { Dying = CardConditionals.IsDying.Requirement.IsNot } });
                    return;
                case "SelfDefends":
                    te.TriggerCardRequirements.Add(Is(CardRole.Inciter, CardRole.Self));
                    te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.EventTarget, Conditional = Subtype(CardSubtype.Creature) });
                    return;
                case "SelfEquipped":
                    te.TriggerCardRequirements.Add(Is(CardRole.EventTarget, CardRole.Self)); return;
                case "FriendlyCreatureEquipped":
                    te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.EventTarget, Conditional = Subtype(CardSubtype.Creature) });
                    te.TriggerCardRequirements.Add(new CardRequirements.RelationshipRequirement { LhsCard = CardRole.EventTarget, Conditional = Rel(true) });
                    return;
                case "HealthGained":
                    te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.EventTarget, Conditional = Subtype(CardSubtype.Avatar) });
                    te.TriggerCardRequirements.Add(new CardRequirements.RelationshipRequirement { LhsCard = CardRole.EventTarget, Conditional = Rel(true) });
                    return;
                case "RuneDestroyed":
                    te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.Inciter, Conditional = Subtype(CardSubtype.Avatar) });
                    if (ev != null && ev.Side != null && ev.Side != "any")
                        te.TriggerCardRequirements.Add(new CardRequirements.RelationshipRequirement { LhsCard = CardRole.Inciter, Conditional = Rel(ev.Side == "friendly") });
                    return;
            }
            // CreatureSummoned / CreatureDies / CardDrawn: filter the event card.
            Target f = ev ?? new Target { Side = "friendly", Other = trigger != "CardDrawn" };
            if (f.Lane != null && f.Lane != "this") throw new NotSupportedException("event lane qualifier " + f.Lane);
            string type = f.CardType ?? (trigger == "CardDrawn" ? null : "Creature");
            if (type != null) te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.Inciter, Conditional = Subtype(SubtypeNamed(type)) });
            if (f.Side == "friendly" || f.Side == "enemy")
                te.TriggerCardRequirements.Add(new CardRequirements.RelationshipRequirement { LhsCard = CardRole.Inciter, Conditional = Rel(f.Side == "friendly") });
            if (f.Tribe != null) te.TriggerCardRequirements.Add(new CardRequirements.SubtypeRequirement { LhsCard = CardRole.Inciter, Conditional = Subtype(SubtypeNamed(f.Tribe)) });
            if (f.Other) te.TriggerCardRequirements.Add(IsNot(CardRole.Inciter, CardRole.Self));
            if (f.Keyword != null) te.TriggerCardRequirements.Add(new CardRequirements.AttributeValue { LhsCard = CardRole.Inciter, Conditional = new CardConditionals.AttributeValue {
                AttributeName = AttributeNamed(f.Keyword), Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(1) } });
            if (f.CostMin >= 0) te.TriggerCardRequirements.Add(new CardRequirements.AttributeValue { LhsCard = CardRole.Inciter, Conditional = new CardConditionals.AttributeValue {
                AttributeName = AttributeType.ManaCost, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(f.CostMin) } });
            if (f.Lane == "this") te.TriggerCardRequirements.Add(new CardRequirements.SameLaneRequirement { LhsCard = CardRole.Inciter,
                Conditional = new CardConditionals.InSameLane { Same = CardConditionals.InSameLane.Requirement.Is, OtherCard = CardRole.Self, UseBoardLaneIndex = trigger == "CreatureDies" } });
        }
        private static CardRequirements.EventRoleRequirement Is(CardRole lhs, CardRole role)
        { return new CardRequirements.EventRoleRequirement { LhsCard = lhs, Conditional = new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = role } }; }
        private static CardRequirements.EventRoleRequirement IsNot(CardRole lhs, CardRole role)
        { return new CardRequirements.EventRoleRequirement { LhsCard = lhs, Conditional = new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = role } }; }

        private static bool SameTarget(Target a, Target b)
        { return a.Tribe == b.Tribe && a.Zone == b.Zone && a.CardType == b.CardType && a.CardId == b.CardId && a.Keyword == b.Keyword && a.Side == b.Side && a.Lane == b.Lane && a.Other == b.Other && a.Avatar == b.Avatar && a.Wounded == b.Wounded && a.Power == b.Power && a.PowerCmp == b.PowerCmp && a.CostMin == b.CostMin; }

        private static Effect Effect(GameConfiguration config, Card card, Eff e, string trigger)
        {
            if (e.ValueFrom == "enemyRunesLost" && e.Action != "stats") throw new NotSupportedException("enemyRunesLost only supports stats");
            if (e.ValueFrom == "selfPower" && e.Action != "damage" && e.Action != "draw") throw new NotSupportedException("selfPower supports damage or draw");
            if (e.ValueFrom == "eventValue" && trigger != "SelfDamaged" && trigger != "SelfDamagedByCreature" && trigger != "HealthGained")
                throw new NotSupportedException("event value outside supported damage/healing trigger");
            Effect effect = new Effect();
            if (e.Condition != null) effect.ExecutePlayerRequirements.Add(Condition(e.Condition));
            Target t = e.Target;
            switch (e.Action)
            {
                case "damage":
                    if (e.ValueFrom == "turnDeaths")
                    {
                        RequireCard(t); effect.CardActions.Add(CardAction(new OfflineTurnMechanics.DeathScaledDamage(), t, trigger)); break;
                    }
                    if (e.DamageSource != null)
                    {
                        if (e.DamageSource != "inciter" || trigger != "CreatureAttacks" || t.Kind != "player" || t.Who != "opponent")
                            throw new NotSupportedException("unsupported damage source");
                        CardEffectAction damage = new CardEffectAction { HashId = -1, Action = new CardActions.Damage { HealthLoss = EffectValue(e, trigger), DamageSource = CardRole.Inciter } };
                        damage.Selectors.Add(Subtype(CardSubtype.Avatar)); damage.Selectors.Add(Board()); damage.Selectors.Add(Rel(false));
                        effect.CardActions.Add(damage);
                    }
                    else if (t.Kind == "player") foreach (PlayerRole role in Roles(t)) effect.PlayerActions.Add(Player(new PlayerActions.DamageAvatarOfPlayer { Amount = EffectValue(e, trigger), AssetHash = -1 }, role));
                    else effect.CardActions.Add(CardAction(new CardActions.Damage { HealthLoss = EffectValue(e, trigger), DamageSource = CardRole.Self }, t, trigger));
                    break;
                case "breakRune":
                    foreach (PlayerRole role in Roles(t)) effect.PlayerActions.Add(Player(new PlayerActions.BreakRunesOfPlayer { NumRunes = new ActionValueData(1), AssetHash = -1 }, role));
                    break;
                case "heal":
                    if (t.Kind != "player")
                    {
                        // Heal a creature (native Heal on the card target).
                        effect.CardActions.Add(CardAction(new CardActions.Heal { HealthGain = EffectValue(e, trigger), FullHeal = e.ValueFrom == null && e.Amount <= 0, HealToAVDValue = false }, t, trigger));
                        break;
                    }
                    if (t.Who != "self") throw new NotSupportedException("heal target " + t.Who);
                    CardEffectAction heal = new CardEffectAction { HashId = -1, Action = new CardActions.Heal { HealthGain = EffectValue(e, trigger), FullHeal = false, HealToAVDValue = false } };
                    heal.Selectors.Add(Subtype(CardSubtype.Avatar)); heal.Selectors.Add(Board()); heal.Selectors.Add(Rel(true));
                    effect.CardActions.Add(heal); break;
                case "healTo":
                    if (t == null || t.Kind != "player" || t.Who != "self") throw new NotSupportedException("healTo requires self player");
                    CardEffectAction healTo = new CardEffectAction { HashId = -1, Action = new CardActions.Heal { HealthGain = new ActionValueData(e.Amount), HealToAVDValue = true } };
                    healTo.Selectors.Add(Subtype(CardSubtype.Avatar)); healTo.Selectors.Add(Board()); healTo.Selectors.Add(Rel(true));
                    effect.CardActions.Add(healTo); break;
                case "draw":
                    foreach (PlayerRole role in Roles(t))
                        effect.PlayerActions.Add(Player(new PlayerActions.DrawCards { NumCards = EffectValue(e, trigger), DrawFrom = PlayerActions.DrawCards.DrawDeck.Self,
                            OperationType = ServerCardCatalog.MoveOperationType.DefaultDraw, StopAtHandLimit = false, IgnoreDeckingRule = false }, role));
                    break;
                case "magicka":
                    effect.PlayerActions.Add(Player(new PlayerActions.ModifyMana { Amount = new ActionValueData(e.Amount) }, PlayerRole.Self)); break;
                case "maxMagicka":
                    CardEffectAction ramp = new CardEffectAction { HashId = -1, Action = new CardActions.Buff { Enchantment = Stats(AttributeType.MaxMana, e.Amount, AttributeType.None, 0) } };
                    ramp.Selectors.Add(Subtype(CardSubtype.Avatar)); ramp.Selectors.Add(Board()); ramp.Selectors.Add(Rel(true));
                    effect.CardActions.Add(ramp); break;
                case "stats":
                    RequireCard(t);
                    SPTESL.Enchantment stats = Stats(AttributeType.Attack, e.Attack, AttributeType.MaxHealth, e.Health);
                    stats.ExpiresAtEndOfTurn = e.Temporary;
                    if (e.ValueFrom != null)
                    {
                        if (e.Attack < 0 || e.Health < 0) throw new NotSupportedException("negative consumed stat multiplier");
                        ICardAction scaled = e.ValueFrom == "enemyRunesLost" ? (ICardAction)new OfflineTurnMechanics.EnemyRunesScaledBuff { Enchantment = stats }
                            : e.ValueFrom == "turnDeaths" ? (ICardAction)new OfflineTurnMechanics.DeathScaledBuff { Enchantment = stats }
                            : new CardActions.StackingBuff { Enchantment = stats, StackCount = EffectValue(e, trigger) };
                        effect.CardActions.Add(CardAction(scaled, t, trigger));
                    }
                    else if (e.Per != null)
                    {
                        // "+N/+M for each X": the per-unit enchantment stacked by the native count of matching cards.
                        if (e.Attack < 0 || e.Health < 0) throw new NotSupportedException("scaled debuff not emitted yet");
                        effect.CardActions.Add(CardAction(new CardActions.StackingBuff { Enchantment = stats, StackCount = PerValue(e.Per, 1, trigger) }, t, trigger));
                    }
                    else effect.CardActions.Add(CardAction(e.Attack < 0 || e.Health < 0 ? (ICardAction)new CardActions.Debuff { Enchantment = stats } : new CardActions.Buff { Enchantment = stats }, t, trigger));
                    break;
                case "keyword":
                    RequireCard(t);
                    ICardAction kw;
                    if (e.Temporary)
                    {
                        // "... this turn": the keyword attribute as an end-of-turn enchantment.
                        SPTESL.Enchantment temp = Stats((AttributeType)Enum.Parse(typeof(AttributeType), e.Keyword), 1, AttributeType.None, 0);
                        temp.ExpiresAtEndOfTurn = true;
                        kw = new CardActions.Buff { Enchantment = temp };
                    }
                    else if (e.Keyword == "Ward") kw = new CardActions.Ward();
                    else if (e.Keyword == "Cover") kw = new CardActions.Cover { MultipleTurns = false };
                    else kw = new CardActions.GiveKeyword { KeywordAttrib = (AttributeType)Enum.Parse(typeof(AttributeType), e.Keyword), RandomKeyword = false };
                    effect.CardActions.Add(CardAction(kw, t, trigger)); break;
                case "loseKeyword":
                    RequireCard(t);
                    ICardAction lose = e.Keyword == "Cover" ? (ICardAction)new CardActions.RemoveCover()
                        : new CardActions.Debuff { Enchantment = EnchantmentUtilities.CreateKeywordEnchantment(AttributeNamed(e.Keyword), EnchantmentAttributeModificationType.Set, 0) };
                    effect.CardActions.Add(CardAction(lose, t, trigger)); break;
                case "stealEnemyKeywords": case "copyFriendlyKeywords":
                    if (t == null || t.Kind != "self") throw new NotSupportedException(e.Action + " requires self");
                    bool stealingKeywords = e.Action == "stealEnemyKeywords";
                    CardActions.CopyAllKeywords copyKeywords = new CardActions.CopyAllKeywords { RemoveOnCopy = stealingKeywords };
                    copyKeywords.CopyFromSelectors.AddRange(Base(new Target { Kind = "card", Pick = "all", Side = stealingKeywords ? "enemy" : "friendly", Zone = "board", CardType = "Creature" }, trigger));
                    effect.CardActions.Add(CardAction(copyKeywords, t, trigger)); break;
                case "destroy": RequireCard(t); effect.CardActions.Add(CardAction(new CardActions.Destroy { Sacrifice = false }, t, trigger)); break;
                case "shackle": RequireCard(t); effect.CardActions.Add(CardAction(new CardActions.Freeze { PermanentFreeze = false, Type = EnchantmentUtilities.FreezeType.Normal }, t, trigger)); break;
                case "silence": RequireCard(t); effect.CardActions.Add(CardAction(new CardActions.Silence(), t, trigger)); break;
                case "battle":
                    RequireCard(t);
                    CardActions.Fight fight = new CardActions.Fight();
                    fight.InciterSelectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self });
                    effect.CardActions.Add(CardAction(fight, t, trigger)); break;
                case "randomKeyword": RequireCard(t);
                    effect.CardActions.Add(CardAction(new CardActions.GiveRandomKeywords { NumKeywords = new ActionValueData(Math.Max(1, e.Count)) }, t, trigger)); break;
                case "sacrifice": RequireCard(t); effect.CardActions.Add(CardAction(new CardActions.Destroy { Sacrifice = true }, t, trigger)); break;
                case "cost":
                    // Change magicka cost (negative = cheaper), e.g. of cards in hand.
                    RequireCard(t);
                    SPTESL.Enchantment cost = Stats(AttributeType.ManaCost, e.Amount, AttributeType.None, 0); cost.ExpiresAtEndOfTurn = e.Temporary;
                    effect.CardActions.Add(CardAction(e.Amount < 0 ? (ICardAction)new CardActions.Debuff { Enchantment = cost } : new CardActions.Buff { Enchantment = cost }, t, trigger)); break;
                case "doublePower": case "doubleStats":
                    RequireCard(t);
                    effect.CardActions.Add(CardAction(new CardActions.BuffAttributeByAttack { ToAttributeName = AttributeType.Attack,
                        ModificationType = EnchantmentAttributeModificationType.Add }, t, trigger));
                    if (e.Action == "doubleStats")
                        effect.CardActions.Add(CardAction(new CardActions.BuffAttributeByHealth { ToAttributeName = AttributeType.MaxHealth,
                            ModificationType = EnchantmentAttributeModificationType.Add }, t, trigger));
                    break;
                case "powerFromHealth":
                    RequireCard(t);
                    effect.CardActions.Add(CardAction(new CardActions.BuffAttributeByHealth { ToAttributeName = AttributeType.Attack,
                        ModificationType = EnchantmentAttributeModificationType.Add }, t, trigger)); break;
                case "extraAttack":
                    RequireCard(t);
                    SPTESL.Enchantment extra = Stats(AttributeType.NumAttacksPerTurn, Math.Max(1, e.Count), AttributeType.None, 0); extra.ExpiresAtEndOfTurn = true;
                    effect.CardActions.Add(CardAction(new CardActions.Buff { Enchantment = extra }, t, trigger)); break;
                case "steal":
                    // Take control of a chosen creature in its own lane on your side (native ReturnToBoardNewOwner).
                    if (t == null || t.Pick != "choose") throw new NotSupportedException("steal without a chosen target");
                    effect.CardActions.Add(CardAction(new CardActions.ReturnToBoardNewOwner { NewOwner = PlayerRole.Self, DontMoveToFullBoard = true,
                        InsertPosition = new InsertPosition { Type = InsertPosition.LaneChoiceType.Relative, Lhs = CardRole.DecisionTarget, InsertionType = CardInsertionType.Bottom,
                            Comparison = InsertPosition.PositionComparison.SameLane } }, t, trigger)); break;
                case "shuffleToken":
                    int shuffled = TokenId(config, card, e.Token, e.HasStats, e.Attack, e.Health);
                    for (int i = 0; i < Math.Max(1, e.Count); i++)
                        effect.PlayerActions.Add(Player(Token(shuffled, GameZoneType.Deck, new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Random }), PlayerRole.Self));
                    break;
                case "unsummon": RequireCard(t); effect.CardActions.Add(CardAction(new CardActions.ReturnToHand(), t, trigger)); break;
                case "banish": RequireCard(t); effect.CardActions.Add(CardAction(new CardActions.Banish(), t, trigger)); break;
                case "drawCopy":
                    if (t == null || t.Kind != "card" || !((t.Pick == "random" && t.Side == "enemy" && t.Zone == "deck" && (t.CardType == "Card" || t.CardType == "Creature"))
                        || (t.Pick == "choose" && t.Side == "friendly" && t.Zone == "board" && t.CardType == "Creature")))
                        throw new NotSupportedException("drawCopy requires a random opponent deck card or a chosen friendly creature");
                    ICardAction copy = t.Side == "friendly" ? (ICardAction)new CardActions.ReturnToHand { DrawCopies = true }
                        : new CardActions.ReturnToHandNewOwner { DrawCopies = true };
                    effect.CardActions.Add(CardAction(copy, t, trigger)); break;
                case "tutor":
                    // "Draw a random X from your deck/discard pile": native ReturnToHand on one random matching own card.
                    for (int i = 0; i < Math.Max(1, e.Count); i++)
                    {
                        CardEffectAction tutor = new CardEffectAction { HashId = -1, Action = new CardActions.ReturnToHand() };
                        tutor.Selectors.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = e.Zone == "deck" ? GameZoneType.Deck : GameZoneType.Cemetery } });
                        tutor.Selectors.Add(Rel(true));
                        tutor.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = CardRole.Self });
                        tutor.Selectors.AddRange(CardPoolSelectors(e.Pool));
                        tutor.SelectorSort = new CardListSort { Method = CardListSort.SortMethod.Random, NumToReturn = new ActionValueData(1) };
                        effect.CardActions.Add(tutor);
                    }
                    break;
                case "handRandom":
                    for (int i = 0; i < Math.Max(1, e.Count); i++)
                        effect.PlayerActions.Add(Player(new PlayerActions.CreateToken { CardReference = RandomReference(e.Pool), CreateInZoneType = GameZoneType.Hand, NumTokens = new ActionValueData(1),
                            InsertPosition = new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Bottom } }, PlayerRole.Self));
                    break;
                case "summonRandom":
                    bool creaturePool = e.Pool != null && e.Pool.Types.Contains("Creature");
                    // Non-creature pools (e.g. elixir supports) go to their single native support lane.
                    List<InsertPosition> randomPlaces = creaturePool ? Places(card, e)
                        : new List<InsertPosition> { new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Bottom } };
                    foreach (InsertPosition place in randomPlaces)
                    {
                        if (e.Zone == "collection")
                        {
                            foreach (PlayerRole role in t == null ? new List<PlayerRole> { PlayerRole.Self } : Roles(t))
                                effect.PlayerActions.Add(Player(new PlayerActions.CreateToken { CardReference = RandomReference(e.Pool), CreateInZoneType = GameZoneType.Board, NumTokens = new ActionValueData(1), InsertPosition = place }, role));
                            continue;
                        }
                        if (t != null) throw new NotSupportedException("summonRandom player target outside collection");
                        // An opponent's discarded creature is moved to the caster's side with native ownership transfer.
                        bool opposingDiscard = e.Zone == "opponentDiscard";
                        ICardAction action = opposingDiscard
                            ? (ICardAction)new CardActions.ReturnToBoardNewOwner { NewOwner = PlayerRole.Self, InsertPosition = place, DontMoveToFullBoard = true }
                            : new CardActions.ReturnToBoard { InsertPosition = place, DontMoveToFullBoard = true };
                        CardEffectAction bring = new CardEffectAction { HashId = -1, Action = action };
                        bring.Selectors.Add(new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = e.Zone == "deck" ? GameZoneType.Deck : GameZoneType.Cemetery } });
                        bring.Selectors.Add(Rel(!opposingDiscard));
                        bring.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = CardRole.Self });
                        bring.Selectors.AddRange(CardPoolSelectors(e.Pool));
                        bring.SelectorSort = new CardListSort { Method = CardListSort.SortMethod.Random, NumToReturn = new ActionValueData(1) };
                        effect.CardActions.Add(bring);
                    }
                    break;
                case "invade":
                    effect.PlayerActions.Add(Player(new PlayerActions.Invade { TimesToInvade = new ActionValueData(Math.Max(1, e.Count)), IsSummonInvade = trigger == "Summon" }, PlayerRole.Self));
                    break;
                case "move": RequireCard(t); effect.CardActions.Add(CardAction(new CardActions.ChangeLane(), t, trigger)); break;
                case "transform":
                    RequireCard(t);
                    effect.CardActions.Add(CardAction(new CardActions.TransformCard { CardReference = new EditorCardReference(TokenId(config, card, e.Token, e.HasStats, e.Attack, e.Health)) }, t, trigger));
                    break;
                case "handToken":
                    int hand = TokenId(config, card, e.Token, false, 0, 0);
                    for (int i = 0; i < Math.Max(1, e.Count); i++) effect.PlayerActions.Add(Player(Token(hand, GameZoneType.Hand, new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Bottom }), PlayerRole.Self));
                    break;
                case "summonToken":
                    int board = TokenId(config, card, e.Token, e.HasStats, e.Attack, e.Health);
                    if (e.Fill)
                    {
                        // "Fill this lane with X": native CreateToken.FillLane in the source's lane.
                        if (card.Definition != "Creature") throw new NotSupportedException("lane fill for a non-creature source");
                        PlayerActions.CreateToken fill = Token(board, GameZoneType.Board, new InsertPosition { Type = InsertPosition.LaneChoiceType.Relative, Lhs = CardRole.Self,
                            InsertionType = CardInsertionType.Bottom, Comparison = InsertPosition.PositionComparison.SameLane });
                        fill.FillLane = true; fill.NumTokens = new ActionValueData(0);
                        effect.PlayerActions.Add(Player(fill, PlayerRole.Self));
                        break;
                    }
                    foreach (InsertPosition p in Places(card, e)) effect.PlayerActions.Add(Player(Token(board, GameZoneType.Board, p), PlayerRole.Self));
                    break;
                case "equipToken":
                    RequireCard(t);
                    int item = ItemTokenId(config, card, e.Token, e.HasStats, e.Attack, e.Health);
                    effect.CardActions.Add(CardAction(new CardActions.EquipNewItem { ItemReference = new EditorCardReference(item) }, t, trigger));
                    break;
                default: throw new NotSupportedException("action " + e.Action + " not emitted yet");
            }
            return effect;
        }

        // Board placement for created/summoned creatures. Creature sources use their own lane (or the other / each lane).
        // Non-creature sources (actions, supports, items) have no lane: reconstructed with the native Random lane choice that falls
        // back to the other lane when full; UESP does not document this placement.
        private static List<InsertPosition> Places(Card card, Eff e)
        {
            List<InsertPosition> places = new List<InsertPosition>();
            if (e.Place == "each")
            {
                // Count applies to each lane, not to the board as a whole (Bandit Ambush).
                // Constant placement does not spill a full lane's summons into the other lane.
                for (int i = 0; i < Math.Max(1, e.Count); i++)
                    places.Add(new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Left, InsertionType = CardInsertionType.Bottom });
                for (int i = 0; i < Math.Max(1, e.Count); i++)
                    places.Add(new InsertPosition { Type = InsertPosition.LaneChoiceType.Constant, Lane = LaneIndexEnum.Right, InsertionType = CardInsertionType.Bottom });
                return places;
            }
            bool sourceless = card.Definition == "Action" || card.Definition == "Support" || card.Definition == "Item";
            if (sourceless && e.Place == "other") throw new NotSupportedException("other-lane token for a non-creature source");
            for (int i = 0; i < Math.Max(1, e.Count); i++)
                places.Add(sourceless || e.Place == "random" ? new InsertPosition { Type = InsertPosition.LaneChoiceType.Random, UseOtherLaneIfFull = true, InsertionType = CardInsertionType.Bottom }
                    : new InsertPosition { Type = InsertPosition.LaneChoiceType.Relative, Lhs = CardRole.Self, InsertionType = CardInsertionType.Bottom,
                        Comparison = e.Place == "other" ? InsertPosition.PositionComparison.OtherLane : InsertPosition.PositionComparison.SameLane });
            return places;
        }

        private static CardSubtype SubtypeNamed(string name) { return (CardSubtype)Enum.Parse(typeof(CardSubtype), name); }
        private static AttributeType AttributeNamed(string name) { return (AttributeType)Enum.Parse(typeof(AttributeType), name); }

        // Native RandomFromSelector over the installed collection (GameManager.GetRandomCardFromSelectorPool pool rules).
        private static EditorCardReference RandomReference(Pool pool)
        {
            if (pool == null) throw new NotSupportedException("random creation without a pool");
            if (pool.CardId != 0) throw new NotSupportedException("random collection creation by specific resource is unsupported");
            EditorCardReference reference = new EditorCardReference { ReferenceStyle = EditorCardReferenceStyle.RandomFromSelector };
            foreach (string type in pool.Types)
                reference.RandomCreationSelectors.Add(new CollectionConditionals.Subtype { Check = CollectionConditionals.Subtype.Requirement.Is, SubtypeName = SubtypeNamed(type) });
            if (pool.CostMin >= 0) reference.RandomCreationSelectors.Add(new CollectionConditionals.AttributeValue { AttributeName = AttributeType.ManaCost,
                Is = CollectionConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, ValueData = new ConditionalValueData(pool.CostMin) });
            if (pool.CostMax >= 0) reference.RandomCreationSelectors.Add(new CollectionConditionals.AttributeValue { AttributeName = AttributeType.ManaCost,
                Is = CollectionConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo, ValueData = new ConditionalValueData(pool.CostMax) });
            if (pool.Keyword != null) reference.RandomCreationSelectors.Add(new CollectionConditionals.AttributeValue { AttributeName = AttributeNamed(pool.Keyword),
                Is = CollectionConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, ValueData = new ConditionalValueData(1) });
            if (reference.RandomCreationSelectors.Count == 0) throw new NotSupportedException("unfiltered random pool");
            return reference;
        }
        // The same pool as card selectors for cards already in a deck or discard pile.
        private static List<ICardConditionalExpression> CardPoolSelectors(Pool pool)
        {
            List<ICardConditionalExpression> list = new List<ICardConditionalExpression>();
            if (pool == null) return list;
            if (pool.CardId != 0) { CardConditionals.MatchesCardResource match = new CardConditionals.MatchesCardResource { Check = CardConditionals.MatchesCardResource.Requirement.Is };
                match.SpecificMatch.SetToSpecificCard(pool.CardId); list.Add(match); }
            foreach (string type in pool.Types) list.Add(Subtype(SubtypeNamed(type)));
            if (pool.CostMin >= 0) list.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.ManaCost, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(pool.CostMin) });
            if (pool.CostMax >= 0) list.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.ManaCost, Is = CardConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo, Value = new ConditionalValueData(pool.CostMax) });
            if (pool.Keyword != null) list.Add(new CardConditionals.AttributeValue { AttributeName = AttributeNamed(pool.Keyword), Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(1) });
            return list;
        }
        // "for each X": native CountOfSelector over the board, multiplied per unit.
        private static ActionValueData PerValue(Target per, int unit, string trigger)
        {
            ActionValueData value = new ActionValueData(ActionValueMethod.CountOfSelector) { ConstantMultiplier = unit };
            value.Selectors.AddRange(Base(per, trigger));
            return value;
        }
        private static ActionValueData EffectValue(Eff e, string trigger)
        { return e.ValueFrom == "selfPower" ? new ActionValueData(ActionValueMethod.ValueOfCardAttribute) { CardRole = CardRole.Self, AttributeName = AttributeType.Attack, ConstantMultiplier = 1 } :
            e.ValueFrom != null ? new ActionValueData(ActionValueMethod.TriggerValue) :
            e.Per == null ? new ActionValueData(e.Amount) : PerValue(e.Per, e.Amount, trigger); }
        private static TriggeredEffect ConsumedStatReward(AttributeType attribute)
        {
            TriggeredEffect result = new TriggeredEffect { TriggerType = EffectTriggerType.Summon, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            Effect effect = new Effect();
            effect.CardActions.Add(CardAction(new CardActions.StackingBuff { Enchantment = Stats(attribute, 1, AttributeType.None, 0),
                StackCount = new ActionValueData(ActionValueMethod.TriggerValue) }, new Target { Kind = "self" }, "Summon"));
            result.EffectGroup.Effects.Add(effect); return result;
        }

        // ------------------------------------------------------------------ targets
        private static void RequireCard(Target t) { if (t == null || t.Kind == "player") throw new NotSupportedException("card action with a player target"); }
        private static CardConditionals.Zone ZoneOf(string zone)
        {
            GameZoneType bits = zone == "hand" ? GameZoneType.Hand : zone == "deck" ? GameZoneType.Deck : zone == "discard" ? GameZoneType.Cemetery : GameZoneType.Board;
            return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = bits } };
        }
        private static List<PlayerRole> Roles(Target t)
        {
            if (t == null || t.Kind != "player") throw new NotSupportedException("player action with a card target");
            if (t.Who == "both") return new List<PlayerRole> { PlayerRole.Self, PlayerRole.Opponent };
            if (t.Who == "inciter") return new List<PlayerRole> { PlayerRole.Inciter };
            return new List<PlayerRole> { t.Who == "opponent" ? PlayerRole.Opponent : PlayerRole.Self };
        }
        private static CardEffectAction CardAction(ICardAction native, Target t, string trigger)
        {
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = native };
            if (t.Kind == "self") { action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self }); return action; }
            // "it" in event triggers: the event card (summoned / drawn / dying creature) or the event's target.
            if (t.Kind == "inciter") { action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Inciter }); return action; }
            if (t.Kind == "eventTarget") { action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.EventTarget }); return action; }
            if (t.Pick == "choose") { action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget }); return action; }
            action.Selectors.AddRange(Base(t, trigger));
            if (t.Pick == "random") action.SelectorSort = new CardListSort { Method = CardListSort.SortMethod.Random, NumToReturn = new ActionValueData(1) };
            else if (t.Pick != "all") throw new NotSupportedException("target pick " + t.Pick);
            return action;
        }
        private static DecisionConditionalExpression Decision(Target t, string trigger)
        {
            DecisionConditionalExpression d = new DecisionConditionalExpression { DecisionType = DecisionType.Card };
            d.CardConditionalExpressions.AddRange(Base(t, trigger));
            return d;
        }
        private static List<ICardConditionalExpression> Base(Target t, string trigger)
        {
            List<ICardConditionalExpression> list = new List<ICardConditionalExpression>();
            if (t.Avatar)
            {
                CardConditionals.SubtypeCount types = new CardConditionals.SubtypeCount { Is = CardConditionals.SubtypeCount.ComparisonType.GreaterThanOrEqualTo, Value = 1 };
                types.Subtypes.Add(CardSubtype.Creature); types.Subtypes.Add(CardSubtype.Avatar); list.Add(types);
            }
            else if (t.CardType != "Card") list.Add(Subtype(SubtypeNamed(t.CardType ?? "Creature")));
            if (t.CostMin >= 0) list.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.ManaCost,
                Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(t.CostMin) });
            if (t.CardId != 0)
            {
                CardConditionals.MatchesCardResource match = new CardConditionals.MatchesCardResource { Check = CardConditionals.MatchesCardResource.Requirement.Is };
                match.SpecificMatch.SetToSpecificCard(t.CardId); list.Add(match);
            }
            list.Add(ZoneOf(t.Zone));
            if (t.Keyword != null) list.Add(new CardConditionals.AttributeValue { AttributeName = AttributeNamed(t.Keyword), Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(1) });
            if (t.Side == "enemy") list.Add(Rel(false)); else if (t.Side == "friendly") list.Add(Rel(true));
            if (t.Tribe != null) list.Add(Subtype((CardSubtype)Enum.Parse(typeof(CardSubtype), t.Tribe)));
            if (t.Other) list.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = CardRole.Self });
            if (t.Lane == "this") list.Add(new CardConditionals.InSameLane { Same = CardConditionals.InSameLane.Requirement.Is, OtherCard = CardRole.Self, UseBoardLaneIndex = trigger == "LastGasp" });
            else if (t.Lane == "other") list.Add(new CardConditionals.InSameLane { Same = CardConditionals.InSameLane.Requirement.IsNot, OtherCard = CardRole.Self, UseBoardLaneIndex = trigger == "LastGasp" });
            else if (t.Lane != null) throw new NotSupportedException("lane qualifier " + t.Lane);
            if (t.Wounded) list.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.Wounded, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThan, Value = new ConditionalValueData(0) });
            if (t.Power >= 0) list.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.Attack, Value = new ConditionalValueData(t.Power),
                Is = t.PowerCmp == "less" ? CardConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo : CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo });
            return list;
        }

        // ------------------------------------------------------------------ conditions, auras, tokens
        private static IPlayerRequirement Condition(fsData c)
        {
            Dictionary<string, fsData> d = c.AsDictionary;
            string type = d["type"].AsString;
            if (type == "opponentMoreHand")
                return new OfflineTurnMechanics.OpponentMoreHandRequirement { LhsPlayer = PlayerRole.Self };
            if (type == "creaturesDiedThisTurn")
                return new OfflineTurnMechanics.TotalDeathsRequirement { LhsPlayer = PlayerRole.Self, Minimum = (int)d["min"].AsInt64 };
            if (type == "friendlyDiedThisTurn" || type == "enemyDiedThisTurn")
            {
                PlayerRequirements.PlayerAttributeValueRequirement r = new PlayerRequirements.PlayerAttributeValueRequirement { LhsPlayer = type == "enemyDiedThisTurn" ? PlayerRole.Opponent : PlayerRole.Self };
                r.Conditional.AttributeName = AttributeType.CreaturesDeadThisTurn;
                r.Conditional.Is = PlayerConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo;
                r.Conditional.Value = (int)d["min"].AsInt64; return r;
            }
            if (type == "equipped" || type == "selfKeyword")
            {
                // Count only the acting source, using intact native equipment/attribute predicates.
                PlayerRequirements.PlayerCountOfCardsRequirement r = new PlayerRequirements.PlayerCountOfCardsRequirement { LhsPlayer = PlayerRole.Self };
                r.Conditional.Count_Is = PlayerConditionals.CountOfCards.ComparisonType.GreaterThanOrEqualTo;
                r.Conditional.Value = 1;
                r.Conditional.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self });
                if (type == "equipped") r.Conditional.Selectors.Add(new CardConditionals.HasItemEquipped { EquipState = CardConditionals.HasItemEquipped.Requirement.HasEquipment });
                else r.Conditional.Selectors.Add(new CardConditionals.AttributeValue { AttributeName = AttributeNamed(Str(d, "keyword")),
                    Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(1) });
                return r;
            }
            if (type == "turn")
            {
                string who = Str(d, "who");
                if (who != "self" && who != "opponent") throw new NotSupportedException("turn condition needs self/opponent");
                PlayerRequirements.PlayerCurrentTurnRequirement r = new PlayerRequirements.PlayerCurrentTurnRequirement { LhsPlayer = PlayerRole.Self };
                r.Conditional.Current_Turn = who == "self" ? PlayerConditionals.PlayerCurrentTurn.Requirement.Is : PlayerConditionals.PlayerCurrentTurn.Requirement.IsNot;
                return r;
            }
            if (type == "health")
            {
                PlayerRequirements.PlayerCountOfCardsRequirement r = new PlayerRequirements.PlayerCountOfCardsRequirement { LhsPlayer = PlayerRole.Self };
                r.Conditional.Count_Is = PlayerConditionals.CountOfCards.ComparisonType.GreaterThanOrEqualTo;
                r.Conditional.Value = 1;
                r.Conditional.Selectors.Add(Subtype(CardSubtype.Avatar)); r.Conditional.Selectors.Add(Board()); r.Conditional.Selectors.Add(Rel(true));
                r.Conditional.Selectors.Add(new CardConditionals.CurrentHealthValue {
                    Current_Health = CardConditionals.CurrentHealthValue.ComparisonType.GreaterThanOrEqualTo,
                    HealthValue = new ConditionalValueData((int)d["min"].AsInt64) });
                return r;
            }
            if (type == "opponentDestroyedRune")
            {
                PlayerRequirements.PlayerAttributeValueRequirement r = new PlayerRequirements.PlayerAttributeValueRequirement { LhsPlayer = PlayerRole.Opponent };
                r.Conditional.AttributeName = AttributeType.NumRunesLost; r.Conditional.Is = PlayerConditionals.AttributeValue.ComparisonType.GreaterThan; r.Conditional.Value = 0;
                return r;
            }
            if (type == "maxMagicka" || type == "otherCreatures")
            {
                PlayerRequirements.PlayerCountOfCardsRequirement r = new PlayerRequirements.PlayerCountOfCardsRequirement { LhsPlayer = PlayerRole.Self };
                r.Conditional.Count_Is = PlayerConditionals.CountOfCards.ComparisonType.GreaterThanOrEqualTo;
                if (type == "maxMagicka")
                {
                    r.Conditional.Value = 1; r.Conditional.Selectors.Add(Subtype(CardSubtype.Avatar)); r.Conditional.Selectors.Add(Board()); r.Conditional.Selectors.Add(Rel(true));
                    r.Conditional.Selectors.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.MaxMana, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo,
                        Value = new ConditionalValueData((int)d["min"].AsInt64) });
                }
                else
                {
                    r.Conditional.Value = (int)d["min"].AsInt64;
                    r.Conditional.Selectors.Add(Subtype(CardSubtype.Creature)); r.Conditional.Selectors.Add(Board()); r.Conditional.Selectors.Add(Rel(true));
                    if (d.ContainsKey("attribute") && d["attribute"].IsString) r.Conditional.Selectors.Add(Subtype((CardSubtype)Enum.Parse(typeof(CardSubtype), d["attribute"].AsString)));
                    r.Conditional.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.IsNot, Role = CardRole.Self });
                }
                return r;
            }
            if (type == "count")
            {
                // "If you have another Orc", "if you have no cards in hand", ...: native CountOfCards over the target spec.
                Target counted = ReadTarget(d["target"].AsDictionary);
                PlayerRequirements.PlayerCountOfCardsRequirement r = new PlayerRequirements.PlayerCountOfCardsRequirement { LhsPlayer = PlayerRole.Self };
                bool atMost = d.ContainsKey("max") && d["max"].IsInt64;
                r.Conditional.Count_Is = atMost ? PlayerConditionals.CountOfCards.ComparisonType.LessThanOrEqualTo : PlayerConditionals.CountOfCards.ComparisonType.GreaterThanOrEqualTo;
                r.Conditional.Value = atMost ? (int)d["max"].AsInt64 : d.ContainsKey("min") && d["min"].IsInt64 ? (int)d["min"].AsInt64 : 1;
                r.Conditional.Selectors.AddRange(Base(counted, "Condition"));
                return r;
            }
            if (type == "moreHealth")
            {
                PlayerRequirements.PlayerHealthComparisonRequirement r = new PlayerRequirements.PlayerHealthComparisonRequirement { LhsPlayer = PlayerRole.Self };
                r.Conditional.Is = PlayerConditionals.HealthComparison.ComparisonType.GreaterThan; r.Conditional.CompareTo = PlayerRole.Opponent;
                return r;
            }
            if (type == "creatureEachLane")
            {
                PlayerRequirements.PlayerHasCreatureInLanesRequirement r = new PlayerRequirements.PlayerHasCreatureInLanesRequirement { LhsPlayer = PlayerRole.Self };
                r.Conditional.ExcludeCardRole = CardRole.Self;
                return r;
            }
            throw new NotSupportedException("condition " + type + " not emitted yet");
        }

        private static Aura AuraOf(Eff e)
        {
            if ((e.Action != "stats" && e.Action != "keyword") || e.Target == null
                || (e.Target.Kind != "self" && (e.Target.Kind != "card" || e.Target.Pick != "all"))) throw new NotSupportedException("aura " + e.Action);
            if (e.Temporary || (e.Per != null && e.Action != "stats")) throw new NotSupportedException("temporary/non-stat per aura not implemented");
            AuraEnchantment ae = new AuraEnchantment { Name = "Spec aura", StackCount = new ActionValueData(1) };
            if (e.Per != null) ae.StackCount = PerValue(e.Per, 1, "Aura");
            if (e.Attack != 0) ae.Enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, e.Attack));
            if (e.Health != 0) ae.Enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, e.Health));
            if (e.Action == "keyword") ae.Enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeNamed(e.Keyword), 1));
            if (e.Target.Kind == "self") ae.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self });
            else ae.Selectors.AddRange(Base(e.Target, "Aura"));
            Aura aura = new Aura { NameForEditor = "Spec aura" }; aura.EnchantmentsToApply.Add(ae);
            // Preserve continuous conditions in native aura requirements; never silently emit an unconditional buff.
            if (e.Condition != null) aura.PlayerRequirements.Add(Condition(e.Condition));
            return aura;
        }

        // Unique installed card by exact name (stats checked when the text states them); the source card's own
        // original CardReferences disambiguate duplicates. Never invented.
        private static int TokenId(GameConfiguration config, Card source, string name, bool checkStats, int attack, int health)
        {
            int reviewed = ReviewedTokenId(config, source, name);
            if (reviewed != 0)
            {
                Card token = config.FindCardById(reviewed);
                if (checkStats && (Stat(token, AttributeType.Attack) != attack || Stat(token, AttributeType.MaxHealth) != health))
                    throw new NotSupportedException("reviewed token statistics differ from the compiled spec: " + name);
                return reviewed;
            }
            List<int> matches = new List<int>();
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                if (string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase) && !matches.Contains(c.ResourceId.Id)
                    && (!checkStats || (Stat(c, AttributeType.Attack) == attack && Stat(c, AttributeType.MaxHealth) == health))) matches.Add(c.ResourceId.Id);
            if (matches.Count > 1 && source.CardReferences != null)
                matches = matches.FindAll(delegate(int id) { return source.CardReferences.Exists(delegate(UniqueResourceReference r) { return r.resourceId == id; }); });
            if (matches.Count != 1) throw new NotSupportedException("token '" + name + "' identity not unique (" + matches.Count + ")");
            return matches[0];
        }

        private static int ItemTokenId(GameConfiguration config, Card source, string name, bool checkStats, int attack, int health)
        {
            List<int> matches = new List<int>();
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                if (c.HasSubtype(CardSubtype.Item) && string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
                    && !matches.Contains(c.ResourceId.Id) && (!checkStats || (Stat(c, AttributeType.Attack) == attack && Stat(c, AttributeType.MaxHealth) == health)))
                    matches.Add(c.ResourceId.Id);
            if (matches.Count > 1 && source.CardReferences != null)
                matches = matches.FindAll(delegate(int id) { return source.CardReferences.Exists(delegate(UniqueResourceReference r) { return r.resourceId == id; }); });
            if (matches.Count != 1) throw new NotSupportedException("item token '" + name + "' identity not unique (" + matches.Count + ")");
            return matches[0];
        }

        // D3, 2026-10-24: narrow installed-identity translations. A phrase is accepted only for
        // the reviewed source ID; never singularize or strip qualifiers globally. Native keyword
        // metadata supplies the stated Guard/Charge. Counts, triggers, lane placement and Invade
        // remain in the original compiled abilities. See ORIGINAL-TOKEN-IDENTITY-NOTES.md.
        private static int ReviewedTokenId(GameConfiguration config, Card source, string phrase)
        {
            int id = 0, attack = 0, health = 0;
            string expectedName = null;
            AttributeType keyword = AttributeType.Attack; // sentinel: no required boolean keyword
            switch (source.ResourceId.Id)
            {
                case 1480705345: // Golden Saint: preserve this printing's shared, non-reentrant Summon graph
                case -1596715503:
                    if (phrase == "Golden Saint") { id = source.ResourceId.Id; expectedName = "Golden Saint"; attack = 4; health = 4; keyword = AttributeType.Taunt; }
                    break;
                case -374586671: // Strategic Deployment: surviving installed reference selects Imperial Recruit
                    if (phrase == "Recruits")
                    {
                        if (source.CardReferences == null || source.CardReferences.Count != 1 || source.CardReferences[0].resourceId != -2084284159)
                            throw new NotSupportedException("Strategic Deployment's original Recruit reference changed");
                        id = -2084284159; expectedName = "Recruit"; attack = 1; health = 1;
                    }
                    break;
                case 195135320: // Bandit Ambush: two in each lane
                    if (phrase == "Cruel Bandits") { id = 906449635; expectedName = "Cruel Bandit"; attack = 1; health = 1; }
                    break;
                case -1038531871: // Bedlam: hand
                case -705626882: // Created Invasion Party branch: Invade, then two on board
                    if (phrase == "Scamps") { id = 175622366; expectedName = "Scamp"; attack = 1; health = 1; }
                    break;
                case 1345209521: // Knight of Gnisis
                    if (phrase == "Colovian Trooper with Guard") { id = 1403480832; expectedName = "Colovian Trooper"; attack = 2; health = 2; keyword = AttributeType.Taunt; }
                    break;
                case 1439548280: // Marked Man
                    if (phrase == "Makeshift Defenses with Guard") { id = 741809568; expectedName = "Makeshift Defenses"; attack = 0; health = 2; keyword = AttributeType.Taunt; }
                    break;
                case -2144786523: // Exclusive Raiding Party
                case -140181666: // Normal Raiding Party; both create the reviewed Normal token
                    if (phrase == "Nord Firebrands with Charge") { id = 195169233; expectedName = "Nord Firebrand"; attack = 1; health = 1; keyword = AttributeType.Charge; }
                    break;
            }
            if (id == 0) return 0;
            Card card = config.FindCardById(id);
            if (card == null || !string.Equals(card.Name, expectedName, StringComparison.Ordinal)
                || Stat(card, AttributeType.Attack) != attack || Stat(card, AttributeType.MaxHealth) != health)
                throw new NotSupportedException("reviewed token identity/statistics changed: " + id + " " + expectedName);
            if (keyword != AttributeType.Attack)
            {
                BoolAttribute flag = card.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == keyword; }) as BoolAttribute;
                if (flag == null || !flag.Value) throw new NotSupportedException("reviewed token lost native " + keyword + ": " + id);
            }
            return id;
        }
        private static int Stat(Card c, AttributeType name) { IntAttribute a = c.EntityAttributes.Find(delegate(SPTESL.Attribute x) { return x.Name == name; }) as IntAttribute; return a == null ? 0 : a.Value; }
        private static PlayerActions.CreateToken Token(int id, GameZoneType zone, InsertPosition position)
        { return new PlayerActions.CreateToken { CardReference = new EditorCardReference(id), CreateInZoneType = zone, NumTokens = new ActionValueData(1), InsertPosition = position }; }

        private static PlayerEffectAction Player(IPlayerAction action, PlayerRole role)
        {
            PlayerEffectAction p = new PlayerEffectAction { Action = action };
            p.Selectors.Add(new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = role });
            return p;
        }
        private static SPTESL.Enchantment Stats(AttributeType a, int x, AttributeType b, int y)
        {
            SPTESL.Enchantment e = new SPTESL.Enchantment();
            if (x != 0) e.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, a, x));
            if (y != 0) e.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, b, y));
            return e;
        }
        private static CardConditionals.Subtype Subtype(CardSubtype s) { return new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = s }; }
        private static CardConditionals.Zone Board() { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }
        private static CardConditionals.Relationship Rel(bool friends) { return new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = friends ? CardConditionals.Relationship.RelationshipType.Friends : CardConditionals.Relationship.RelationshipType.Enemies }; }
    }
}
