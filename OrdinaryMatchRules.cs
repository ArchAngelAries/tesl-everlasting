using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Bounded reconstruction of Default/base-avatar rules from installed native
    // templates/actions and original localization. Missing source lists are not
    // recovered server code. Story, puzzle and custom lane rules stay unsupported.
    public static class OrdinaryMatchRules
    {
        public const int BaseAvatarId = 1264418727;
        public const int DefaultScenarioId = -1242723717;
        public static bool Active { get; private set; }

        public static IDisposable Prepare(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (Active) throw new InvalidOperationException("An ordinary rules scope is already active.");
            Scenario scenario = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.Id == DefaultScenarioId; });
            Card avatar = config.FindCardById(BaseAvatarId);
            if (scenario == null || scenario.ResourceId.UniqueName != "Default" || avatar == null || avatar.Name != "Avatar")
                throw new NotSupportedException("Installed Default scenario/base Avatar identities differ.");
            if (config.PlayerDefinitions == null || config.PlayerDefinitions.Count != 1
                || Value(config.PlayerDefinitions[0], AttributeType.NumRunes) != 5
                || Value(config.PlayerDefinitions[0], AttributeType.NumStartingRunes) != 5
                || Value(config.PlayerDefinitions[0], AttributeType.RuneHealth) != 5
                || Value(config.PlayerDefinitions[0], AttributeType.RingUsesLeft) != 0
                || Value(config.PlayerDefinitions[0], AttributeType.CanUseRing) != 0
                || Value(avatar, AttributeType.StartingHealth) != 30 || Value(avatar, AttributeType.MaxMana) != 0
                || Value(avatar, AttributeType.GainHealthFromRuneBreak) != 0
                || Value(avatar, AttributeType.BlockDrawsFromRuneBreak) != 0)
                throw new NotSupportedException("Native ordinary avatar/player template differs from audited defaults.");
            if (scenario.DeckOverrides == null || scenario.DeckOverrides.Count != 0 || scenario.TriggeredEffects == null
                || scenario.TriggeredEffects.Count != 0 || scenario.GameProperties == null)
                throw new NotSupportedException("Default scenario includes unsupported or absent scenario rules.");
            if ((scenario.AvatarOverrides != null && scenario.AvatarOverrides.Count != 0)
                || (scenario.PlayerOverrides != null && scenario.PlayerOverrides.Count != 0))
                throw new NotSupportedException("Default overrides already contain rules; do not replace them.");
            ValidateCreatureLanes(scenario);
            if (config.LaneTriggers != null && config.LaneTriggers.Length != 0)
                throw new NotSupportedException("Lane definitions already exist; do not replace recovered rules.");
            return new Scope(config, scenario, avatar);
        }

        private static int Value(Entity entity, AttributeType name)
        {
            SPTESL.Attribute attribute = entity.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == name; });
            IntAttribute integer = attribute as IntAttribute;
            BoolAttribute boolean = attribute as BoolAttribute;
            if (integer != null) return integer.Value;
            if (boolean != null) return boolean.Value ? 1 : 0;
            throw new NotSupportedException("Missing ordinary native template attribute: " + name);
        }

        // Call before constructing any match players/deck cards when this ruleset
        // is enabled. Do not let its partial lane table stand in for other modes.
        public static void ValidateStart(MatchConfiguration.StartInfo info)
        {
            if (info == null || info.ScenarioId != DefaultScenarioId || info.Users == null || info.Users.Length != 2)
                throw new NotSupportedException("Ordinary reconstruction supports exactly two Default-scenario players.");
            foreach (MatchConfiguration.User user in info.Users)
                if (user.CustomDeck == null || user.CustomDeck.AvatarCardId != BaseAvatarId)
                    throw new NotSupportedException("Ordinary reconstruction supports only the installed base Avatar.");
            if (info.OptionalCreatureBoardOverride != null)
                foreach (GameZoneOverride zone in info.OptionalCreatureBoardOverride)
                    if (zone != null) throw new NotSupportedException("Custom start-info creature lanes are unsupported by ordinary rules.");
            if (info.PlayerHealthOverride > 0 || (info.OptionalExtraCards != null && info.OptionalExtraCards.Count != 0))
                throw new NotSupportedException("Health/extra-card start overrides require separate scenario validation.");
        }

        // Call within EventTurnStart after magicka restoration and before command
        // generation. Native UseRing consumes CanUseRing and RingUsesLeft itself.
        public static void ResetRingForTurn(Simulation sim, TurnState turn)
        {
            if (sim._scenarioId != DefaultScenarioId)
                throw new NotSupportedException("Ordinary ring reset cannot apply to another scenario.");
            PlayerState player = sim.Players[turn.ActivePlayerIndex];
            int uses = player.QueryAttribute(AttributeType.RingUsesLeft).Value;
            if (uses < 0 || uses > 3) throw new NotSupportedException("Ordinary ring charges are outside 0..3.");
            PlayerActions.SetAttribute.StaticExecute(sim.HistoryLog, player, AttributeType.CanUseRing, uses > 0 ? 1 : 0);
        }

        private static void ValidateCreatureLanes(Scenario scenario)
        {
            if (scenario.GameZoneOverrides == null || scenario.GameZoneOverrides.Count != 1)
                throw new NotSupportedException("Default lane override shape differs from installed ordinary rules.");
            GameZoneOverrideList list = scenario.GameZoneOverrides[0];
            if (list.SlotMask != PlayerSlotMask.BothPlayers || list.ZoneList.Count != 1)
                throw new NotSupportedException("Ordinary lanes require the native both-player creature-board override.");
            GameZoneOverride zone = list.ZoneList[0];
            if (zone.ZoneToOverride != GameZoneName.CreatureBoard || zone.LaneOverrides.Count != 2)
                throw new NotSupportedException("Ordinary rules support the native field/shadow pair only.");
            for (int i = 0; i < 2; i++)
            {
                LaneOverride item = zone.LaneOverrides[i];
                SizedCardLane lane = item.Lane as SizedCardLane;
                if (lane == null || lane.Capacity != 4 || lane.LaneType != (i == 0 ? CardLaneType.Default : CardLaneType.Stealth)
                    || lane.ExtraLaneType != CardLaneType.None || item.StartingCards.Count != 0)
                    throw new NotSupportedException("Ordinary native lane capacity/type/start cards differ.");
            }
        }

        internal static LaneTrigger ShadowLane()
        {
            LaneTrigger lane = new LaneTrigger();
            lane.Triggers.Add(CoverTrigger(EffectTriggerType.WhenCardEntersBoard));
            lane.Triggers.Add(CoverTrigger(EffectTriggerType.WhenCardChangesLane));
            return lane;
        }

        private static LaneTriggerItem CoverTrigger(EffectTriggerType type)
        {
            LaneTriggerItem item = new LaneTriggerItem();
            item.Conditionals.Add(new LaneCardConditionals.CardInLane {
                Condition = LaneCardConditionals.CardInLane.Requirement.Is, CardRole = CardRole.Inciter });
            item.Trigger.TriggerType = type;
            item.Trigger.SortOrder = TriggerSortOrder.GameRules_CoverLane;
            item.Trigger.ClientTargetingType = PlayTargetingType.NoTarget;
            Effect effect = new Effect();
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = new CardActions.Cover { MultipleTurns = false } };
            action.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Inciter });
            action.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
            effect.CardActions.Add(action);
            item.Trigger.EffectGroup.Effects.Add(effect);
            return item;
        }

        private sealed class Scope : IDisposable
        {
            private readonly GameConfiguration config;
            private readonly Scenario scenario;
            private readonly Card avatar;
            private readonly LaneTrigger[] oldLanes;
            private readonly List<AvatarOverride> oldAvatarOverrides;
            private readonly List<PlayerOverride> oldPlayerOverrides;
            private readonly List<TriggeredEffect> oldEffects;
            private readonly List<Aura> oldAuras;
            private readonly ActivatedAbility oldAbility;
            private bool disposed;
            public Scope(GameConfiguration config, Scenario scenario, Card avatar)
            {
                this.config = config; this.scenario = scenario; this.avatar = avatar;
                oldLanes = config.LaneTriggers; oldAvatarOverrides = scenario.AvatarOverrides; oldPlayerOverrides = scenario.PlayerOverrides;
                oldEffects = avatar.TriggeredEffects; oldAuras = avatar.Auras; oldAbility = avatar.ActivatedAbility;
                // Empty per-avatar wrappers leave the intact shared Avatar
                // definition's 15 effects and one aura attached by ServerCard.
                if (avatar.TriggeredEffects == null) avatar.TriggeredEffects = new List<TriggeredEffect>();
                if (avatar.Auras == null) avatar.Auras = new List<Aura>();
                if (avatar.ActivatedAbility == null) avatar.ActivatedAbility = new ActivatedAbility();
                scenario.AvatarOverrides = new List<AvatarOverride>();
                PlayerOverride second = new PlayerOverride { SlotMask = PlayerSlotMask.SecondPlayer };
                second.Override.EntityAttributes.Add(new IntAttribute { Name = AttributeType.RingUsesLeft, Value = 3 });
                second.Override.EntityAttributes.Add(new BoolAttribute { Name = AttributeType.CanUseRing, Value = true });
                scenario.PlayerOverrides = new List<PlayerOverride> { second };
                config.LaneTriggers = new LaneTrigger[(int)CardLaneType.Count];
                config.LaneTriggers[(int)CardLaneType.None] = new LaneTrigger();
                config.LaneTriggers[(int)CardLaneType.Default] = new LaneTrigger();
                config.LaneTriggers[(int)CardLaneType.Stealth] = ShadowLane();
                Active = true;
            }
            public void Dispose()
            {
                if (disposed) return; disposed = true;
                config.LaneTriggers = oldLanes; scenario.AvatarOverrides = oldAvatarOverrides; scenario.PlayerOverrides = oldPlayerOverrides;
                avatar.TriggeredEffects = oldEffects; avatar.Auras = oldAuras; avatar.ActivatedAbility = oldAbility;
                Active = false;
            }
        }
    }
}
