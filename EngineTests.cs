using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    // Headless integration tests run in the shipped Unity/Mono runtime, never online.
    // Empty generic rules below are isolated TEST FIXTURES, not restored game rules.
    public static class EngineTests
    {
        private static readonly List<Action> restore = new List<Action>();
        private static string directory;
        private static StringBuilder results;
        private static int failures;
        public static int Run(GameConfiguration config, string output)
        {
            directory = output;
            results = new StringBuilder("test\tresult\tdetail\n");
            failures = 0;
            try
            {
                // Fixture mod authored with the TESL Modding Suite (fixtures/mods), loaded like a user's enabled mods.
                OfflineMods.EnsureApplied(config, Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(output.TrimEnd('/', '\\')), "fixtures"), "mods"), "active-mods.json"));
                RuleImporter.Load(config, output);
                PrepareGenericFixtures(config);
                Harmony harmony = new Harmony("local.tesl.offline.tests");
                CatalogMovementRestoration.Install(harmony);
                CommandRestoration.Install(harmony);
                DecisionRestoration.Install(harmony);
                LifecycleRestoration.Install(harmony);
                OriginalCardCombatTests.Run(config, Test);
                OriginalTargetSpellTests.Run(config, Test);
                OriginalDamageSpellTests.Run(config, Test);
                OriginalDamageStrategyTests.Run(config, Test);
                OriginalItemTests.Run(config, Test);
                OriginalSimpleSummonTests.Run(config, Test);
                OriginalDrawStrategyTests.Run(config, Test);
                OriginalScenarioDeckTests.Run(config, Test);
                OriginalHealingSummonTests.Run(config, Test);
                OriginalTargetedSummonTests.Run(config, Test);
                OriginalSpellBattleTests.Run(config, Test);
                OriginalRemovalUtilityTests.Run(config, Test);
                OfflineCardBehaviourTests.Run(config, Test);
                OriginalMagickaConditionTests.Run(config, Test);
                OriginalKeywordTriggerTests.Run(config, Test);
                OriginalTokenLaneTests.Run(config, Test);
                OriginalVeteranRandomTests.Run(config, Test);
                OriginalBoardActivateTests.Run(config, Test);
                OriginalAuraTests.Run(config, Test);
                OriginalRandomBetrayTests.Run(config, Test, output);
                OriginalSpecTests.Run(config, Test, output);
                OriginalItemAbilityTests.Run(config, Test);
                OriginalItemRoutingTests.Run(config, Test);
                OriginalWhileTests.Run(config, Test);
                OriginalWhileEquipmentTests.Run(config, Test);
                OfflineConsumeChoiceTests.Run(config, Test);
                ImbuedConsumeTests.Run(config, Test);
                ConsumeTargetTests.Run(config, Test);
                StoryLaneRuleTests.Run(config, Test);
                QuestGoalTests.Run(config, Test);
                OfflineCheatTests.Run(config, Test, output);
                TransformSweepTests.Run(config, Test, output);
                OriginalFxTests.Run(config, Test, output);
                EverlastingHostTests.Run(config, Test, output);
                EverlastingHost.RunTests(Test);   // installed extensions' own tests
                OfflineModTests.Run(config, Test, output);
                OfflineGoldStoreTests.Run(config, Test);
                StoryBranchEvidence.Run(config, Test, output);
                CampaignProgressTests.Run(config, Test, output);
                BoneArmorConsumeTests.Run(config, Test);
                AdditionalLifecycleTests.Run(config, Test);
                OfflineLocalProfileTests.Run(output, Test);
                OfflineDeckCatalogTests.Run(config, Test, output);
                OriginalRecipeMatchTests.Run(config, Test, output);
                TutorialScenarioTests.Run(config, Test, output);
                Test("native-protobuf-command-validation", delegate {
                    Require(EngineCommandTests.Run(output) == 0, "A native command validation case failed; see engine-command-test-results.tsv.");
                });
                Test("reviewed-native-rules-loaded", delegate { Require(RuleImporter.Count > 0, "No rule passed native validation."); });
                Test("native-creature-model", delegate {
                    Fixture f = new Fixture(config);
                    ServerCard c = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    Require(c.CurrentHealth > 7 && c.QueryAttribute(AttributeType.Attack).Value > 0, "Original combat statistics were not retained.");
                });
                Test("hidden-deck-draw-move", delegate {
                    Fixture f = new Fixture(config);
                    ServerCard card = f.Create("Enraged Mudcrab", GameZoneName.Deck, 0, f.One);
                    int oldUid = card.NetUid.Id;
                    List<ServerCard> drawn = PlayerActions.DrawCards.StaticExecute(f.State, f.One, 1,
                        PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, false, -1);
                    Require(drawn.Count == 1 && drawn[0] == card && card.CurrentGameZoneName == GameZoneName.Hand, "Native draw did not move the deck card into hand.");
                    Require(card.NetUid.Id != oldUid && f.Catalog.FindCardCountInDeck(f.One.NetId) == 0, "Hidden UID or source zone was not updated.");
                    Require(f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId) == 1, "Hand count differs.");
                });
                Test("move-full-lane-no-mutation", delegate {
                    Fixture f = new Fixture(config);
                    GameZone board = f.Catalog.FindGameZone(f.One.NetId, GameZoneName.CreatureBoard);
                    int capacity = board.LaneList[0].GetCapacity();
                    Require(capacity > 0 && capacity < 20, "Expected bounded board lane.");
                    for (int i = 0; i < capacity; i++) f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard c = f.Create("Enraged Mudcrab", GameZoneName.Hand, 0, f.One);
                    int uid = c.NetUid.Id;
                    GameZoneOperationResult moved = ServerCardCatalog.MoveCard(f.State, new GameZoneId(f.One.NetId, GameZoneType.Hand),
                        new GameZoneId(f.One.NetId, GameZoneType.Board), c.InstanceId, 0, CardInsertionType.Bottom, ServerCardCatalog.MoveOperationType.Untyped);
                    Require(moved != GameZoneOperationResult.Succeeded && c.CurrentGameZoneName == GameZoneName.Hand && c.NetUid.Id == uid,
                        "Failed full-lane move changed card state.");
                    Require(f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId) == 1 && board.FindCardCountInLane(board.LaneList[0].GetCardLaneType()) >= capacity, "Failed move lost a card.");
                });
                Test("native-summon-damage-both-players", delegate {
                    Fixture f = new Fixture(config);
                    ServerCard c = f.Create("Afflicted Alit", GameZoneName.CreatureBoard, 0, f.One);
                    Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, c, f.Players)), "Summon trigger was not gathered.");
                    f.DrainTriggers();
                    Require(f.AvatarOne.CurrentHealth == 28 && f.AvatarTwo.CurrentHealth == 28, "Afflicted Alit did not deal exactly 2 to each player.");
                });
                Test("native-action-damage-and-draw", delegate {
                    Fixture f = new Fixture(config);
                    f.Create("Enraged Mudcrab", GameZoneName.Deck, 0, f.One);
                    ServerCard action = f.Create("Ice Spike", GameZoneName.Hand, 0, f.One);
                    f.PlayEffects(action, null);
                    Require(f.AvatarOne.CurrentHealth == 30 && f.AvatarTwo.CurrentHealth == 28, "Ice Spike damaged the wrong player or wrong amount.");
                    Require(f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId) == 2 && f.Catalog.FindCardCountInDeck(f.One.NetId) == 0,
                        "Ice Spike did not draw exactly one card.");
                });
                Test("native-targeted-action-damage-and-draw", delegate {
                    Fixture f = new Fixture(config);
                    f.Create("Enraged Mudcrab", GameZoneName.Deck, 0, f.One);
                    ServerCard action = f.Create("Spirit Knife", GameZoneName.Hand, 0, f.One);
                    ServerCard target = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    int health = target.CurrentHealth;
                    f.PlayEffects(action, target);
                    Require(target.CurrentHealth == health - 7 && f.AvatarTwo.CurrentHealth == 30, "Spirit Knife did not damage its chosen creature by 7.");
                    Require(f.Catalog.FindCardCountInDeck(f.One.NetId) == 0, "Spirit Knife failed to draw.");
                });
                Test("target-selector-rejects-hand-and-avatar", delegate {
                    Fixture f = new Fixture(config);
                    ServerCard action = f.Create("Spirit Knife", GameZoneName.Hand, 0, f.One);
                    ServerCard board = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard hand = f.Create("Enraged Mudcrab", GameZoneName.Hand, 0, f.Two);
                    PlayCardAbility a = GameManager.Instance.GetCard(action.CardId).ActionPlayInformation[0];
                    List<CardNetUid> targets = f.Catalog.GatherSpellCardTargets(action, a.PlayTargetingType, a.EffectGroup.Effects, a.TargetSelectors, f.Players, false, false);
                    Require(targets.Contains(board.NetUid) && !targets.Contains(hand.NetUid) && !targets.Contains(f.AvatarTwo.NetUid), "Creature selector leaked a hidden card or avatar.");
                });
                Test("command-generation-and-stale-decision", delegate {
                    Fixture f = new Fixture(config);
                    f.Create("Ice Spike", GameZoneName.Hand, 0, f.One);
                    CardCommandItem item = f.Catalog.GenerateCardCommandItem(f.One, f.Two, f.Players);
                    Require(item.CommandList.ValidCommands.Count > 0 && item.CommandList.ListUid > 0, "No original protobuf commands generated.");
                    Decision stale = new Decision { Type = CcgRpc.DecisionType.NoCommand, CommandUid = item.CommandList.ListUid - 1 };
                    Require(!Simulation.ValidateDecision(stale, item.CommandList), "Stale command list was accepted.");
                });
                Test("native-sleep-freeze-action-limits", delegate {
                    Fixture f = new Fixture(config);
                    ServerCard attacker = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    Require(!CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, attacker), "New creature was allowed to attack while sleeping.");
                    attacker.OnStartTurn(f.One.NetId, f.State);
                    Require(CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, attacker), "Awake creature could not attack.");
                    attacker.SetAttribute(AttributeType.Freeze, 1);
                    Require(!CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, attacker), "Frozen creature was allowed to attack.");
                    attacker.SetAttribute(AttributeType.Freeze, 0);
                    int limit = attacker.QueryAttribute(AttributeType.NumAttacksPerTurn).Value;
                    Require(limit > 0 && limit < 10, "Unexpected attack limit.");
                    for (int i = 0; i < limit; i++) attacker.IncrementActionCount();
                    Require(!CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, attacker), "Creature exceeded native per-turn attack limit.");
                });
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-native-generic-tests") >= 0)
                {
                    EngineLifecycleTests.Run(config, Test);
                    OrdinaryRuleTests.Run(config, Test);
                    StrategicDecisionTests.Run(config, Test);
                }
            }
            finally
            {
                for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
                restore.Clear();
                File.WriteAllText(Path.Combine(output, "engine-test-results.tsv"), results.ToString());
            }
            return failures;
        }

        private static void Test(string name, Action action)
        {
            // Development: TESL_TEST_FILTER="part1;part2" runs only tests whose names contain a part.
            string filter = Environment.GetEnvironmentVariable("TESL_TEST_FILTER");
            if (!string.IsNullOrEmpty(filter) && Array.FindIndex(filter.Split(';'), delegate(string f) { return f.Length > 0 && name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0; }) < 0) return;
            UnityEngine.Debug.Log("ENGINE TEST: " + name);   // names a test that hangs
            try { action(); results.AppendLine(name + "\tPASS\t"); }
            catch (Exception ex) { failures++; results.AppendLine(name + "\tFAIL\t" + ex.ToString().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')); }
            File.WriteAllText(Path.Combine(directory, "engine-test-results.tsv"), results.ToString());
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        private static void PrepareGenericFixtures(GameConfiguration config)
        {
            StringBuilder audit = new StringBuilder("field\toriginal_count\tfixture\n");
            LaneTrigger[] original = config.LaneTriggers;
            audit.AppendLine("LaneTriggers\t" + (original == null ? -1 : original.Length) + "\tempty test lanes");
            config.LaneTriggers = new LaneTrigger[0];
            restore.Add(delegate { config.LaneTriggers = original; });
            bool useNativeGenericRules = Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-native-generic-tests") >= 0;
            foreach (CardDefinition definition in config.CardDefinitions)
            {
                CardDefinition d = definition;
                List<TriggeredEffect> effects = d.TriggeredEffects;
                List<Aura> auras = d.Auras;
                audit.AppendLine(d.Name + ".TriggeredEffects\t" + (effects == null ? -1 : effects.Count) + "\t" + (useNativeGenericRules ? "preserved native rules" : "empty generic test rules"));
                audit.AppendLine(d.Name + ".Auras\t" + (auras == null ? -1 : auras.Count) + "\t" + (useNativeGenericRules ? "preserved native auras" : "empty generic test auras"));
                if (!useNativeGenericRules)
                {
                    d.TriggeredEffects = new List<TriggeredEffect>();
                    d.Auras = new List<Aura>();
                    restore.Add(delegate { d.TriggeredEffects = effects; d.Auras = auras; });
                }
            }
            FullSerializer.fsData serialized;
            FullSerializer.fsSerializer serializer = new FullSerializer.fsSerializer();
            if (serializer.TrySerialize(typeof(List<CardDefinition>), config.CardDefinitions, out serialized).Succeeded)
                File.WriteAllText(Path.Combine(directory, useNativeGenericRules ? "native-card-definitions.json" : "test-card-definitions.json"), FullSerializer.fsJsonPrinter.PrettyJson(serialized));
            if (serializer.TrySerialize(typeof(GameProperties), config.Properties, out serialized).Succeeded)
                File.WriteAllText(Path.Combine(directory, "native-game-properties.json"), FullSerializer.fsJsonPrinter.PrettyJson(serialized));
            if (serializer.TrySerialize(typeof(List<GameZone>), config.Zones, out serialized).Succeeded)
                File.WriteAllText(Path.Combine(directory, "native-game-zones.json"), FullSerializer.fsJsonPrinter.PrettyJson(serialized));
            File.WriteAllText(Path.Combine(directory, "engine-test-fixture-audit.tsv"), audit.ToString());
        }

        internal sealed class Fixture
        {
            public readonly ServerCardCatalog Catalog;
            public readonly List<PlayerState> Players = new List<PlayerState>();
            public readonly PlayerState One, Two;
            public readonly ServerEffectUtilities.TriggerGameState State;
            public readonly ServerCard AvatarOne, AvatarTwo;
            public Fixture(GameConfiguration config)
            {
                RandomIntegerGenerator random = new RandomIntegerGenerator(12345);
                Catalog = new ServerCardCatalog(random);
                for (int i = 0; i < 2; i++)
                {
                    PlayerState p = new PlayerState { NetId = new PlayerUniqueId(i + 1), Name = "Offline test player " + i };
                    foreach (SPTESL.Attribute attribute in config.PlayerDefinitions[0].EntityAttributes)
                    {
                        IntAttribute integer = attribute as IntAttribute;
                        BoolAttribute boolean = attribute as BoolAttribute;
                        if (integer != null) p.SetAttribute(attribute.Name, integer.Value);
                        if (boolean != null) p.SetAttribute(attribute.Name, boolean.Value ? 1 : 0);
                    }
                    p.SetAttribute(AttributeType.HandSize, 10);
                    p.SetAttribute(AttributeType.Mana, 20);
                    for (int n = 0; n < 28; n++) p.MatchStatValues.Add(0);
                    Players.Add(p);
                    Catalog.CreatePlayerGameZones(p.NetId, config.Zones, new GameZoneOverrideList());
                }
                One = Players[0]; Two = Players[1];
                One.IsMyTurn = true;
                HistoryLog history = new HistoryLog();
                history.StartEvent(HistoryType.EventGameStart, One.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
                State = new ServerEffectUtilities.TriggerGameState(history, random, Catalog, Players, new ServerEffectUtilities.TriggerQueues(),
                    new List<ActiveQuest>(), new List<CustomDisplayCard>(), UniqueResourceId.GetInvalid().Id);
                Card avatar = null;
                foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                    if (GameManager.Instance.LookUpCardDefinition(c.ResourceId.Id).Subtypes.Contains(CardSubtype.Avatar)) { avatar = c; break; }
                Require(avatar != null, "No installed avatar definition found.");
                // Passive avatar data is a test fixture; its absent original effects remain unverified.
                if (avatar.TriggeredEffects == null)
                {
                    Card saved = avatar;
                    List<Aura> originalAuras = avatar.Auras;
                    ActivatedAbility originalAbility = avatar.ActivatedAbility;
                    avatar.TriggeredEffects = new List<TriggeredEffect>(); avatar.Auras = new List<Aura>();
                    avatar.ActivatedAbility = new ActivatedAbility();
                    restore.Add(delegate { saved.TriggeredEffects = null; saved.Auras = originalAuras; saved.ActivatedAbility = originalAbility; });
                }
                AvatarOne = CreateId(avatar.ResourceId.Id, GameZoneName.AvatarBoard, 0, One);
                AvatarTwo = CreateId(avatar.ResourceId.Id, GameZoneName.AvatarBoard, 0, Two);
                AvatarOne.SetAttribute(AttributeType.StartingHealth, 30); AvatarOne.SetAvatarHealthPreGame(30);
                AvatarTwo.SetAttribute(AttributeType.StartingHealth, 30); AvatarTwo.SetAvatarHealthPreGame(30);
            }
            public ServerCard Create(string name, GameZoneName zone, int lane, PlayerState owner)
            {
                GameConfiguration config = GameManager.Instance.Config;
                foreach (CardSet set in config.CardCollection) foreach (Card card in set.Cards)
                    if (card.Name == name && RuleImporter.IsSupported(card.ResourceId.Id)) return CreateId(card.ResourceId.Id, zone, lane, owner);
                throw new InvalidOperationException("Required reviewed card is unavailable: " + name);
            }
            internal ServerCard CreateId(int id, GameZoneName zone, int lane, PlayerState owner)
            {
                ServerCard c = ServerCardCatalog.CreateCard(State, id, false, zone, lane, CardInsertionType.Bottom, owner.NetId, null, null);
                Require(c != null, "Native card creation failed: " + id);
                return c;
            }
            public void DrainTriggers()
            {
                int guard = 0;
                while (State.TriggerQueues.Pending.Count > 0 || State.TriggerQueues.Active.Count > 0)
                {
                    Require(++guard <= 64, "Native trigger queue did not settle.");
                    State.TriggerQueues.Pending.Sort(ServerEffectUtilities.TriggerContext.SortComparison);
                    foreach (ServerEffectUtilities.TriggerContext pending in State.TriggerQueues.Pending) State.TriggerQueues.Active.Enqueue(pending);
                    State.TriggerQueues.Pending.Clear();
                    ServerEffectUtilities.TriggerContext context = State.TriggerQueues.Active.Dequeue();
                    if (OfflineConsumeChoices.Supported(context))
                    {
                        ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(State, context, null);
                        context.DidMainEffectsRun = true;
                        if (State.CustomUiCards.Count > 0)
                            ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(State, context,
                                new Decision { Type = CcgRpc.DecisionType.CustomChoice, CustomChoice = 0 });
                        State.CustomUiCards.Clear();
                        continue;
                    }
                    Decision chosen = null;
                    if (context.TriggeredEffect.ClientTargetingType != PlayTargetingType.NoTarget)
                    {
                        // Targeted trigger: choose its first legal target (or none), as the restored decision would offer.
                        List<CardNetUid> targets = CommandRestoration.SpellTargets(Catalog, context.EffectOwner, context.TriggeredEffect.ClientTargetingType,
                            context.TriggeredEffect.EffectGroup.Effects, context.TriggeredEffect.TargetSelectors, Players, false, context.TriggeredEffect.ItemUsesWielderAsEffectSource);
                        chosen = new Decision { TargetCardNetUid = targets.Count > 0 ? targets[0].Id : CardNetUid.GetInvalid().Id };
                    }
                    ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(State, context, chosen);
                }
            }
            public void PlayEffects(ServerCard card, ServerCard target)
            {
                PlayCardAbility ability = GameManager.Instance.GetCard(card.CardId).ActionPlayInformation[0];
                ServerEffectUtilities.TriggerContext context = new ServerEffectUtilities.TriggerContext {
                    EffectOwner = card, TriggeredEffect = new TriggeredEffect { EffectGroup = ability.EffectGroup },
                    TriggerEvent = new ServerEffectUtilities.TriggerEvent(EffectTriggerType.ActionEffect, card, Players), ActingLaneIndex = -1, ActingLaneIndexType = -1 };
                ServerEffectUtilities.ExecuteOnPlayCardEffects(State, context, new Decision {
                    TargetCardNetUid = target == null ? CardNetUid.GetInvalid().Id : target.NetUid.Id }, null);
            }
        }
    }
}
