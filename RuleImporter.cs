using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FullSerializer;
using SPTESL;

namespace OfflineEngine
{
    // Partial, reviewed development data, not recovered original server rules.
    public static class RuleImporter
    {
        private static readonly HashSet<int> supported = new HashSet<int>();
        public static bool IsSupported(int id) { return supported.Contains(id); }
        private static readonly Dictionary<int, int> metadataOnlyUses = new Dictionary<int, int>();
        public static void NoteMetadataOnlyUse(int id, string operation) { int n; metadataOnlyUses.TryGetValue(id, out n); metadataOnlyUses[id] = n + 1; }
        public static int MetadataOnlyUseCount(int id) { int n; return metadataOnlyUses.TryGetValue(id, out n) ? n : 0; }
        public static int Count { get { return supported.Count; } }

        public static void Load(GameConfiguration config, string directory)
        {
            NativeKeywordRules.Validate(config);
            OriginalCardRules.Validate(config);
            OriginalSpecialCreatureRules.Validate(config);
            OriginalTargetSpellRules.Validate(config);
            OriginalDamageSpellRules.Validate(config);
            OriginalItemRules.Validate(config);
            OriginalSimpleSummonRules.Validate(config);
            OriginalHealingSummonRules.Validate(config);
            OriginalTargetedSummonRules.Validate(config);
            OriginalSpellBattleRules.Validate(config);
            OriginalRemovalUtilityRules.Validate(config);
            OriginalMagickaConditionRules.Validate(config);
            OriginalKeywordTriggerRules.Validate(config);
            OriginalTokenRules.Validate(config);
            OriginalLaneSpellRules.Validate(config);
            OriginalVeteranRandomRules.Validate(config);
            OriginalBoardTokenRules.Validate(config);
            OriginalActivateSupportRules.Validate(config);
            OriginalAuraRules.Validate(config);
            OriginalRandomCardRules.Validate(config);
            OriginalBetrayRules.Validate(config);
            OriginalOblivionGateRules.Validate(config);
            // Original graphs decode first: text-compiled cards listed in prefer-original.tsv take the original instead.
            OfflineGraphics.Timed("OriginalGraphRules.Load", delegate { OriginalGraphRules.Load(config, directory); });
            OfflineGraphics.Timed("OriginalSpecRules.Prepare", delegate { OriginalSpecRules.Prepare(config, directory, delegate(int id) { return OwnedByHandWrittenRules(id) || OriginalGraphRules.Prefers(id); }); });
            fsData root = fsJsonParser.Parse(File.ReadAllText(OfflinePaths.DataNear(directory, "rule-bridge.json")));   // data/rule-bridge.json
            Keys(root, "schema", "scope", "source", "cards");
            if (Number(root, "schema") != 1 || String(root, "scope") != "partial-development-rules")
                throw new InvalidOperationException("Unknown rule bridge schema/scope.");
            List<Action> commits = new List<Action>();
            HashSet<int> seen = new HashSet<int>();
            StringBuilder report = new StringBuilder("card_id\tname\tresult\tdetail\n");
            foreach (fsData record in root.AsDictionary["cards"].AsList)
            {
                Keys(record, "card_id", "name", "type", "cost", "attack", "health", "source_file", "source_id", "effects");
                int id = Number(record, "card_id");
                if (!seen.Add(id)) throw new InvalidOperationException("Duplicate native ID in rule bridge.");
                Card card = GameManager.Instance.GetCard(id);
                string name = String(record, "name"), type = String(record, "type");
                if (card == null || card.Name != name) throw new InvalidOperationException("Native rule mapping differs: " + name);
                if (OwnedByHandWrittenRules(id) || OriginalSpecRules.IsOriginalId(id))
                {
                    report.AppendLine(id + "\t" + name + "\tsuperseded\texact original wrapper replaces reference bridge");
                    continue;
                }
                string mismatch = VerifyStats(card, record, type);
                if (mismatch != null)
                {
                    report.AppendLine(id + "\t" + name + "\trejected\t" + mismatch);
                    continue;
                }
                EffectGroup group = new EffectGroup();
                bool targeted = false;
                string trigger = null;
                foreach (fsData data in record.AsDictionary["effects"].AsList)
                {
                    Keys(data, "trigger", "type", "amount", "target");
                    string eventName = String(data, "trigger");
                    if (eventName != (type == "Action" ? "Play" : "Summon") || (trigger != null && trigger != eventName))
                        throw new InvalidOperationException("Unsupported/mixed trigger: " + name);
                    trigger = eventName;
                    string action = String(data, "type"), target = String(data, "target");
                    int amount = Number(data, "amount");
                    if (amount <= 0 || amount > 100) throw new InvalidOperationException("Unsupported action amount.");
                    Effect effect = new Effect();
                    if (action == "damage" && target == "creature")
                    {
                        targeted = true;
                        CardEffectAction native = new CardEffectAction();
                        native.Action = new CardActions.Damage { HealthLoss = new ActionValueData(amount), DamageSource = CardRole.Self };
                        native.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
                        effect.CardActions.Add(native);
                    }
                    else if ((action == "damage" || action == "drawCards") && (target == "player" || target == "players" || target == "opponent"))
                    {
                        PlayerEffectAction native = new PlayerEffectAction();
                        if (target != "players") native.Selectors.Add(new PlayerConditionals.PlayerRoleType {
                            Check = PlayerConditionals.PlayerRoleType.Requirement.Is,
                            Role = target == "player" ? PlayerRole.Self : PlayerRole.Opponent });
                        if (action == "damage") native.Action = new PlayerActions.DamageAvatarOfPlayer { Amount = new ActionValueData(amount) };
                        else native.Action = new PlayerActions.DrawCards { NumCards = new ActionValueData(amount),
                            DrawFrom = PlayerActions.DrawCards.DrawDeck.Self, OperationType = ServerCardCatalog.MoveOperationType.DefaultDraw,
                            StopAtHandLimit = false, IgnoreDeckingRule = false };
                        effect.PlayerActions.Add(native);
                    }
                    else throw new InvalidOperationException("Unsupported effect: " + name + " / " + action + " / " + target);
                    group.Effects.Add(effect);
                }
                List<TriggeredEffect> triggers = new List<TriggeredEffect>();
                List<PlayCardAbility> abilities = new List<PlayCardAbility>();
                if (type == "Action")
                    abilities.Add(new PlayCardAbility { EffectGroup = group, PlayTargetingType = targeted ? PlayTargetingType.RequiredTarget : PlayTargetingType.NoTarget,
                        TargetSelectors = targeted ? CreatureSelectors() : new List<DecisionConditionalExpression>() });
                else if (group.Effects.Count > 0)
                    triggers.Add(new TriggeredEffect { EffectGroup = group, TriggerType = EffectTriggerType.Summon,
                        ClientTargetingType = targeted ? PlayTargetingType.OptionalTarget : PlayTargetingType.NoTarget,
                        TargetSelectors = targeted ? CreatureSelectors() : new List<DecisionConditionalExpression>() });
                Card staged = card;
                List<TriggeredEffect> stagedTriggers = triggers;
                List<PlayCardAbility> stagedAbilities = abilities;
                commits.Add(delegate {
                    staged.TriggeredEffects = stagedTriggers;
                    staged.Auras = new List<Aura>();
                    staged.ActionPlayInformation = stagedAbilities;
                    staged.PlayRequirements = new PlayCardRequirements();
                    staged.BetrayRequirements = new PlayCardRequirements();
                    staged.ActivatedAbility = new ActivatedAbility();
                    supported.Add(staged.ResourceId.Id);
                });
                report.AppendLine(id + "\t" + name + "\tstaged\tpartial development rules; original statistics verified");
            }
            // Parsing/validation finishes before any rule field is changed.
            supported.Clear();
            // Card.StrongHintRequirements is [NonSerialized] with declared default new StrongHintRequirements();
            // BinaryFormatter skips that initializer, leaving null. Native ServerCard.Silence ->
            // RebuildSpecialRequirementsFromCard dereferences it, so restore the class's own empty default.
            foreach (CardSet set in config.CardCollection)
                foreach (Card native in set.Cards)
                    if (native.StrongHintRequirements == null) native.StrongHintRequirements = new StrongHintRequirements();
            foreach (Action commit in commits) commit();
            foreach (int id in NativeKeywordRules.Restore(config)) supported.Add(id);
            foreach (int id in OriginalCardRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\toriginal-native-rules\texact installed metadata; surviving shared Creature keyword graph retained");
            }
            foreach (int id in OriginalSpecialCreatureRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\toriginal-native-rules\texact metadata and surviving extra-attack algorithm");
            }
            foreach (int id in OriginalTargetSpellRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-wrapper\texact metadata and original native Destroy action");
            }
            foreach (int id in OriginalDamageSpellRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-wrapper\texact metadata and original native Damage action");
            }
            foreach (int id in OriginalItemRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\toriginal-native-item\texact stat-only metadata and surviving original Equip system");
            }
            foreach (int id in OriginalSimpleSummonRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-summon\texact original metadata and native DrawCards");
            }
            foreach (int id in OriginalHealingSummonRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-heal-summon\texact original metadata and native Heal");
            }
            foreach (int id in OriginalTargetedSummonRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-targeted-summon	exact original metadata and native Damage/Buff/Debuff/Freeze/DamageAvatarOfPlayer");
            }
            foreach (int id in OriginalSpellBattleRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-spell-battle	exact original metadata and native Debuff/Ward/Silence/Heal/Buff/GiveKeyword/Fight/DamageAvatarOfPlayer");
            }
            foreach (int id in OriginalRemovalUtilityRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-removal-utility	exact original metadata and native Damage/Destroy/Freeze/Cover/DrawCards with native conditionals");
            }
            foreach (int id in OriginalMagickaConditionRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-magicka-condition	exact original metadata; native avatar MaxMana Buff, DrawCards and PlayerRequirements");
            }
            foreach (int id in OriginalKeywordTriggerRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-keyword-trigger	exact original metadata; native Last Gasp/Slay/Pilfer triggers and native actions");
            }
            foreach (int id in OriginalTokenRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-token	exact original metadata and original CardReferences; native CreateToken to hand");
            }
            foreach (int id in OriginalLaneSpellRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-lane-spell	exact original metadata; native Lane decision, InTargetedLane, Freeze/Destroy/Damage");
            }
            foreach (int id in OriginalVeteranRandomRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-veteran-random	exact original metadata; reconstructed Veteran raise; native random SelectorSort");
            }
            foreach (int id in OriginalBoardTokenRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-board-token	exact original metadata; native CreateToken to board with native InsertPosition");
            }
            foreach (int id in OriginalActivateSupportRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "	" + config.FindCardById(id).Name + "	reconstructed-original-activate-support	exact original metadata and use attributes; native activation and actions");
            }
            foreach (int id in OriginalAuraRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-aura\texact original metadata; reconstructed aura recompute over native AuraEnchantments");
            }
            foreach (int id in OriginalRandomCardRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-random-card\texact original metadata; native RandomFromSelector pool and CreateToken");
            }
            foreach (int id in OriginalBetrayRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-betray\texact original metadata; reconstructed Betray orchestration");
            }
            foreach (int id in OriginalOblivionGateRules.Restore(config))
            {
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-original-oblivion-gate\texact original metadata; native Invade with reconstructed Gate level graph");
            }
            foreach (int id in OriginalSpecRules.Restore(config, directory))
            {
                if (supported.Contains(id)) continue;
                supported.Add(id);
                report.AppendLine(id + "\t" + config.FindCardById(id).Name + "\treconstructed-spec-compiled\texact installed text compiled by tools/card_compiler.py to native graphs");
            }
            // Original developer graphs for every card our batches do not cover (data/original-rules).
            foreach (int id in OriginalGraphRules.Restore(config, directory, delegate(int x) { return supported.Contains(x); }, report)) supported.Add(id);
            // Modding Suite cards carry their own graphs in the original form (OfflineMods).
            foreach (int id in OfflineMods.CardIds)
                if (!supported.Contains(id)) { supported.Add(id); report.AppendLine(id + "	" + config.FindCardById(id).Name + "	mod-card	Modding Suite card with its own original-form rule graph"); }
            // Card-specific animations of the original graphs, copied into the cards that run our rebuilt rules.
            OriginalFxOverlay.Apply(config, report);
            File.WriteAllText(Path.Combine(directory, "rule-import-results.tsv"), report.ToString());
            // Card's [NonSerialized] wrappers declare empty defaults that BinaryFormatter skips; restore those class
            // defaults for every card so no card is unplayable (cards without reconstructed text play with native metadata).
            int total = 0, defaulted = 0;
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    total++;
                    bool changed = false;
                    if (card.TriggeredEffects == null) { card.TriggeredEffects = new List<TriggeredEffect>(); changed = true; }
                    if (card.Auras == null) { card.Auras = new List<Aura>(); changed = true; }
                    if (card.ActionPlayInformation == null) { card.ActionPlayInformation = new List<PlayCardAbility>(); changed = true; }
                    if (card.PlayRequirements == null) { card.PlayRequirements = new PlayCardRequirements(); changed = true; }
                    if (card.BetrayRequirements == null) { card.BetrayRequirements = new PlayCardRequirements(); changed = true; }
                    if (card.ActivatedAbility == null) { card.ActivatedAbility = new ActivatedAbility(); changed = true; }
                    if (card.StrongHintRequirements == null) { card.StrongHintRequirements = new StrongHintRequirements(); changed = true; }
                    if (changed) defaulted++;
                }
            File.WriteAllText(Path.Combine(directory, "rule-import-summary.tsv"), "field\tvalue\nsupported_card_ids\t" + supported.Count
                + "\nplayable_card_ids\t" + total + "\nmetadata_only_card_ids\t" + (total - supported.Count) + "\nclass_default_wrappers_restored\t" + defaulted + "\n");
        }

        // Hand-written exact-original batches keep precedence over the generated spec emitter.
        public static bool OwnedByHandWrittenRules(int id)
        {
            return OriginalCardRules.Contains(id) || OriginalDamageSpellRules.IsOriginalId(id) || OriginalSimpleSummonRules.IsOriginalId(id) || OriginalHealingSummonRules.IsOriginalId(id)
                    || OriginalTargetedSummonRules.IsOriginalId(id)
                    || OriginalSpellBattleRules.IsOriginalId(id)
                    || OriginalRemovalUtilityRules.IsOriginalId(id)
                    || OriginalMagickaConditionRules.IsOriginalId(id)
                    || OriginalKeywordTriggerRules.IsOriginalId(id)
                    || OriginalTokenRules.IsOriginalId(id)
                    || OriginalLaneSpellRules.IsOriginalId(id)
                    || OriginalVeteranRandomRules.IsOriginalId(id)
                    || OriginalBoardTokenRules.IsOriginalId(id) || OriginalActivateSupportRules.IsOriginalId(id)
                    || OriginalAuraRules.IsOriginalId(id)
                    || OriginalRandomCardRules.IsOriginalId(id) || OriginalBetrayRules.IsOriginalId(id) || OriginalOblivionGateRules.IsOriginalId(id);
        }

        public static List<DecisionConditionalExpression> CreatureSelectors()
        {
            DecisionConditionalExpression expression = new DecisionConditionalExpression();
            expression.DecisionType = SPTESL.DecisionType.Card;
            expression.CardConditionalExpressions.Add(new CardConditionals.Subtype {
                Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
            expression.CardConditionalExpressions.Add(new CardConditionals.Zone {
                Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } });
            return new List<DecisionConditionalExpression> { expression };
        }

        private static string VerifyStats(Card card, fsData data, string type)
        {
            if (type != "Creature" && type != "Action") return "unsupported card type";
            bool creature = type == "Creature";
            CardDefinition definition = GameManager.Instance.LookUpCardDefinition(card.ResourceId.Id);
            if (definition == null || !definition.Subtypes.Contains(creature ? CardSubtype.Creature : CardSubtype.Spell)) return "native card type differs";
            foreach (SPTESL.Attribute attribute in card.EntityAttributes)
            {
                BoolAttribute flag = attribute as BoolAttribute;
                if (flag != null && flag.Value && EnchantmentUtilities.IsKeywordEnchantment(attribute.Name))
                    return "native keyword is absent from reviewed source";
            }
            if (AttributeValue(card, AttributeType.ManaCost) != Number(data, "cost")) return "native cost differs";
            if (creature && (AttributeValue(card, AttributeType.Attack) != Number(data, "attack") || AttributeValue(card, AttributeType.MaxHealth) != Number(data, "health")))
                return "native combat statistics differ";
            return null;
        }

        private static int AttributeValue(Card card, AttributeType name)
        {
            IntAttribute attribute = card.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == name; }) as IntAttribute;
            return attribute == null ? 0 : attribute.Value;
        }
        private static int Number(fsData data, string key)
        {
            fsData value = data.AsDictionary[key];
            if (!value.IsInt64) throw new InvalidOperationException("Expected integer rule field: " + key);
            return checked((int)value.AsInt64);
        }
        private static string String(fsData data, string key) { return data.AsDictionary[key].AsString; }
        private static void Keys(fsData data, params string[] expected)
        {
            if (!data.IsDictionary || data.AsDictionary.Count != expected.Length) throw new InvalidOperationException("Rule object has unknown/missing fields.");
            foreach (string key in expected) if (!data.AsDictionary.ContainsKey(key)) throw new InvalidOperationException("Missing rule field: " + key);
        }
    }
}
