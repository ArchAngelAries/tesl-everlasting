using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Story/campaign scenarios beyond the reconstructed tutorial chapters. Installed scenario
    // data (decks via DeckOverrides or Deck zone overrides, lanes, starting cards, scripted
    // effects, AI opponent, first player) stays untouched; the engine applies it. Only the
    // NonSerialized gaps are filled with ordinary-match values: no avatar override (the
    // player's description carries the base 30-health Avatar), the template's 5 runes, and
    // the ring (3 charges) for the second player. Field and shadow lane rules are supplied;
    // scenarios with other lane types are refused until those lane rules are restored.
    // Provenance: ordinary-rules reconstruction; per-scenario values are not Reborn-observed.
    public static class StoryScenarioRules
    {
        public static bool Active { get; private set; }
        public static int CurrentScenarioId { get; private set; }

        public static IDisposable Prepare(GameConfiguration config, int scenarioId)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (Active || OrdinaryMatchRules.Active || TutorialScenarioRules.Active) throw new InvalidOperationException("Another offline rules scope is already active.");
            if (TutorialScenarioRules.Find(scenarioId) != null) throw new InvalidOperationException("Tutorial chapters use TutorialScenarioRules.");
            Scenario scenario = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.Id == scenarioId; });
            if (scenario == null || scenario.GameProperties == null || scenario.GameZoneOverrides == null || scenario.DeckOverrides == null || scenario.TriggeredEffects == null)
                throw new NotSupportedException("Story scenario is absent or lacks its installed rules: " + scenarioId);
            if ((scenario.AvatarOverrides != null && scenario.AvatarOverrides.Count != 0) || (scenario.PlayerOverrides != null && scenario.PlayerOverrides.Count != 0))
                throw new NotSupportedException("Story overrides already contain rules; do not replace them.");
            foreach (GameZoneOverrideList list in scenario.GameZoneOverrides)
                foreach (GameZoneOverride zone in list.ZoneList)
                    foreach (LaneOverride lane in zone.LaneOverrides)
                    {
                        SizedCardLane sized = lane.Lane as SizedCardLane;
                        if (sized == null) continue; // deck zones use InfiniteCardLane
                        bool known = sized.LaneType == CardLaneType.Default || sized.LaneType == CardLaneType.Stealth || Array.IndexOf(StoryLaneRules.Supported, sized.LaneType) >= 0;
                        if (!known || sized.ExtraLaneType != CardLaneType.None)
                            throw new NotSupportedException("Lane type " + sized.LaneType + " in " + scenario.ResourceId.UniqueName + " is not restored yet.");
                    }
            Card avatar = config.FindCardById(OrdinaryMatchRules.BaseAvatarId);
            if (avatar == null || avatar.Name != "Avatar") throw new NotSupportedException("Installed base Avatar differs.");
            if (config.LaneTriggers != null && config.LaneTriggers.Length != 0)
                throw new NotSupportedException("Lane definitions already exist; do not replace recovered rules.");
            return new Scope(config, scenario, avatar);
        }

        public static void ValidateStart(MatchConfiguration.StartInfo info)
        {
            if (info == null || info.ScenarioId != CurrentScenarioId || info.Users == null || info.Users.Length != 2)
                throw new NotSupportedException("Story scope supports exactly two players of the active scenario.");
            int humans = 0;
            foreach (MatchConfiguration.User user in info.Users)
            {
                if (user.CustomDeck == null) throw new NotSupportedException("Story player description is missing.");
                if (user.Type == PlayerType.Normal) humans++;
            }
            if (humans != 1) throw new NotSupportedException("Story scenarios require one human and one AI player.");
        }

        public static void ResetRingForTurn(Simulation sim, TurnState turn)
        {
            if (sim._scenarioId != CurrentScenarioId) throw new NotSupportedException("Story ring reset cannot apply to another scenario.");
            PlayerState player = sim.Players[turn.ActivePlayerIndex];
            int uses = player.QueryAttribute(AttributeType.RingUsesLeft).Value;
            if (uses < 0 || uses > 3) throw new NotSupportedException("Story ring charges are outside 0..3.");
            PlayerActions.SetAttribute.StaticExecute(sim.HistoryLog, player, AttributeType.CanUseRing, uses > 0 ? 1 : 0);
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
                config.LaneTriggers[(int)CardLaneType.Stealth] = OrdinaryMatchRules.ShadowLane();
                for (int i = 0; i < config.LaneTriggers.Length; i++) if (config.LaneTriggers[i] == null) config.LaneTriggers[i] = new LaneTrigger();
                StoryLaneRules.Install(config.LaneTriggers, config);
                CurrentScenarioId = scenario.ResourceId.Id; Active = true;
            }
            public void Dispose()
            {
                if (disposed) return; disposed = true;
                config.LaneTriggers = oldLanes; scenario.AvatarOverrides = oldAvatarOverrides; scenario.PlayerOverrides = oldPlayerOverrides;
                avatar.TriggeredEffects = oldEffects; avatar.Auras = oldAuras; avatar.ActivatedAbility = oldAbility;
                CurrentScenarioId = 0; Active = false;
            }
        }
    }
}
