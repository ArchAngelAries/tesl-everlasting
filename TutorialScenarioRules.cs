using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Bounded scope for The Forgotten Hero Act 1 Chapters 1-3. Installed scenario
    // data (ordered zone decks, lanes, start/turn actions, scripted effects) stays
    // untouched. Only the NonSerialized AvatarOverrides/PlayerOverrides and absent
    // lane rules are supplied, using values observed in the 2026-10-19 TESL-Reborn
    // capture (exports/reborn-tutorial-ground-truth.json):
    //   avatar zone card ids -1212154068 "Avatar 10 HP" (Ch1) / -442850926 "Avatar 20 HP" (Ch2-3),
    //   starting runes 0/0/3, ring only for Ch3's second player (3 charges, once per turn).
    // Provenance: Reborn-observed, not original-server-proven.
    public static class TutorialScenarioRules
    {
        public sealed class Chapter
        {
            public int ScenarioId; public string Name; public SinglePlayerFirstToAct First;
            public int AvatarId; public int Health; public int Runes; public bool SecondPlayerRing;
        }

        public static readonly Chapter[] Chapters = {
            new Chapter { ScenarioId = 1595477420, Name = "TFH_Act1_Chapter1", First = SinglePlayerFirstToAct.AiPlayer,
                AvatarId = -1212154068, Health = 10, Runes = 0, SecondPlayerRing = false },
            new Chapter { ScenarioId = 740282472, Name = "TFH_Act1_Chapter2", First = SinglePlayerFirstToAct.HumanPlayer,
                AvatarId = -442850926, Health = 20, Runes = 0, SecondPlayerRing = false },
            new Chapter { ScenarioId = 1722475851, Name = "TFH_Act1_Chapter3", First = SinglePlayerFirstToAct.AiPlayer,
                AvatarId = -442850926, Health = 20, Runes = 3, SecondPlayerRing = true },
        };

        public static bool Active { get; private set; }
        public static Chapter Current { get; private set; }

        public static Chapter Find(int scenarioId)
        { return Array.Find(Chapters, delegate(Chapter c) { return c.ScenarioId == scenarioId; }); }

        public static IDisposable Prepare(GameConfiguration config, int scenarioId)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (Active || OrdinaryMatchRules.Active) throw new InvalidOperationException("Another offline rules scope is already active.");
            Chapter chapter = Find(scenarioId);
            if (chapter == null) throw new NotSupportedException("Scenario " + scenarioId + " is not a reconstructed tutorial chapter.");
            Scenario scenario = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.Id == scenarioId; });
            if (scenario == null || scenario.ResourceId.UniqueName != chapter.Name || scenario.StartingPlayer != chapter.First
                || scenario.GameProperties == null || scenario.GameZoneOverrides == null || scenario.GameZoneOverrides.Count != 2
                || scenario.DeckOverrides == null || scenario.DeckOverrides.Count != 0 || scenario.TriggeredEffects == null)
                throw new NotSupportedException("Installed tutorial scenario differs from the audited shape: " + chapter.Name);
            if ((scenario.AvatarOverrides != null && scenario.AvatarOverrides.Count != 0)
                || (scenario.PlayerOverrides != null && scenario.PlayerOverrides.Count != 0))
                throw new NotSupportedException("Tutorial overrides already contain rules; do not replace them.");
            foreach (GameZoneOverrideList list in scenario.GameZoneOverrides)
                foreach (GameZoneOverride zone in list.ZoneList)
                {
                    if (zone.ZoneToOverride == GameZoneName.Deck) continue;
                    if (zone.ZoneToOverride != GameZoneName.CreatureBoard || zone.LaneOverrides.Count != 2)
                        throw new NotSupportedException("Tutorial zone override differs from Deck/CreatureBoard pair.");
                    foreach (LaneOverride lane in zone.LaneOverrides)
                    {
                        SizedCardLane sized = lane.Lane as SizedCardLane;
                        if (sized == null || sized.Capacity != 4 || sized.ExtraLaneType != CardLaneType.None
                            || (sized.LaneType != CardLaneType.Default && sized.LaneType != CardLaneType.Stealth))
                            throw new NotSupportedException("Tutorial lane differs from Default/Shadow capacity 4.");
                    }
                }
            Card avatar = config.FindCardById(chapter.AvatarId);
            if (avatar == null || avatar.Definition != "Avatar" || Value(avatar, AttributeType.StartingHealth) != chapter.Health
                || Value(avatar, AttributeType.MaxMana) != 0 || Value(avatar, AttributeType.GainHealthFromRuneBreak) != 0
                || Value(avatar, AttributeType.BlockDrawsFromRuneBreak) != 0)
                throw new NotSupportedException("Installed tutorial avatar card differs: " + chapter.AvatarId);
            if (config.PlayerDefinitions == null || config.PlayerDefinitions.Count != 1
                || Value(config.PlayerDefinitions[0], AttributeType.RuneHealth) != 5)
                throw new NotSupportedException("Native player template differs from audited defaults.");
            if (config.LaneTriggers != null && config.LaneTriggers.Length != 0)
                throw new NotSupportedException("Lane definitions already exist; do not replace recovered rules.");
            return new Scope(config, scenario, avatar, chapter);
        }

        private static int Value(Entity entity, AttributeType name)
        {
            SPTESL.Attribute attribute = entity.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == name; });
            IntAttribute integer = attribute as IntAttribute;
            BoolAttribute boolean = attribute as BoolAttribute;
            if (integer != null) return integer.Value;
            if (boolean != null) return boolean.Value ? 1 : 0;
            throw new NotSupportedException("Missing native template attribute: " + name);
        }

        // Exactly one human and one AI, the active chapter, no ad-hoc overrides.
        public static void ValidateStart(MatchConfiguration.StartInfo info)
        {
            if (info == null || Current == null || info.ScenarioId != Current.ScenarioId || info.Users == null || info.Users.Length != 2)
                throw new NotSupportedException("Tutorial scope supports exactly two players of the active chapter.");
            int humans = 0;
            foreach (MatchConfiguration.User user in info.Users)
            {
                if (user.CustomDeck == null) throw new NotSupportedException("Tutorial player description is missing.");
                if (user.Type == PlayerType.Normal) humans++;
            }
            if (humans != 1) throw new NotSupportedException("Tutorial chapters require one human and one AI player.");
            if (info.OptionalCreatureBoardOverride != null)
                foreach (GameZoneOverride zone in info.OptionalCreatureBoardOverride)
                    if (zone != null) throw new NotSupportedException("Custom start-info lanes are unsupported in tutorials.");
            if (info.PlayerHealthOverride > 0 || (info.OptionalExtraCards != null && info.OptionalExtraCards.Count != 0))
                throw new NotSupportedException("Health/extra-card start overrides are unsupported in tutorials.");
        }

        // Same once-per-turn reset as ordinary rules, only for a ring holder.
        public static void ResetRingForTurn(Simulation sim, TurnState turn)
        {
            if (Current == null || sim._scenarioId != Current.ScenarioId)
                throw new NotSupportedException("Tutorial ring reset cannot apply to another scenario.");
            PlayerState player = sim.Players[turn.ActivePlayerIndex];
            int uses = player.QueryAttribute(AttributeType.RingUsesLeft).Value;
            if (uses < 0 || uses > 3) throw new NotSupportedException("Tutorial ring charges are outside 0..3.");
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
            public Scope(GameConfiguration config, Scenario scenario, Card avatar, Chapter chapter)
            {
                this.config = config; this.scenario = scenario; this.avatar = avatar;
                oldLanes = config.LaneTriggers; oldAvatarOverrides = scenario.AvatarOverrides; oldPlayerOverrides = scenario.PlayerOverrides;
                oldEffects = avatar.TriggeredEffects; oldAuras = avatar.Auras; oldAbility = avatar.ActivatedAbility;
                // Empty per-card wrappers keep the shared Avatar definition's effects/aura.
                if (avatar.TriggeredEffects == null) avatar.TriggeredEffects = new List<TriggeredEffect>();
                if (avatar.Auras == null) avatar.Auras = new List<Aura>();
                if (avatar.ActivatedAbility == null) avatar.ActivatedAbility = new ActivatedAbility();
                AvatarOverride both = new AvatarOverride { SlotMask = PlayerSlotMask.BothPlayers };
                both.Override.CardIdReference = chapter.AvatarId;
                scenario.AvatarOverrides = new List<AvatarOverride> { both };
                PlayerOverride all = new PlayerOverride { SlotMask = PlayerSlotMask.BothPlayers };
                all.Override.EntityAttributes.Add(new IntAttribute { Name = AttributeType.NumRunes, Value = chapter.Runes });
                all.Override.EntityAttributes.Add(new IntAttribute { Name = AttributeType.NumStartingRunes, Value = chapter.Runes });
                scenario.PlayerOverrides = new List<PlayerOverride> { all };
                if (chapter.SecondPlayerRing)
                {
                    PlayerOverride second = new PlayerOverride { SlotMask = PlayerSlotMask.SecondPlayer };
                    second.Override.EntityAttributes.Add(new IntAttribute { Name = AttributeType.RingUsesLeft, Value = 3 });
                    second.Override.EntityAttributes.Add(new BoolAttribute { Name = AttributeType.CanUseRing, Value = true });
                    scenario.PlayerOverrides.Add(second);
                }
                config.LaneTriggers = new LaneTrigger[(int)CardLaneType.Count];
                config.LaneTriggers[(int)CardLaneType.None] = new LaneTrigger();
                config.LaneTriggers[(int)CardLaneType.Default] = new LaneTrigger();
                config.LaneTriggers[(int)CardLaneType.Stealth] = OrdinaryMatchRules.ShadowLane();
                Current = chapter; Active = true;
            }
            public void Dispose()
            {
                if (disposed) return; disposed = true;
                config.LaneTriggers = oldLanes; scenario.AvatarOverrides = oldAvatarOverrides; scenario.PlayerOverrides = oldPlayerOverrides;
                avatar.TriggeredEffects = oldEffects; avatar.Auras = oldAuras; avatar.ActivatedAbility = oldAbility;
                Current = null; Active = false;
            }
        }
    }
}
