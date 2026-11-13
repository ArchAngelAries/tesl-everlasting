using System;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Deck gauntlet 2026-11-08: Pure-Blood Elder (DoubleMaxManaGained) and Miraak (stealing an equipped creature) hit engine
    // guards. The game itself doubles max magicka gains on the avatar, and a stolen creature keeps its items.
    public static class MagickaAndStealTests
    {
        private const int Crab = 620729069;
        private const int PureBloodElder = -794652648;

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("pure-blood-elder-doubles-turn-start-max-magicka", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim;
                    int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard elder = f.Create(PureBloodElder, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    f.PlayCreatureLane(elder, 0, me);
                    ServerCard avatar = f.Avatar(me);
                    if (avatar.QueryAttribute(AttributeType.DoubleMaxManaGained).Value <= 0)
                        throw new InvalidOperationException("Pure-Blood Elder's aura did not reach its avatar.");
                    if (f.Avatar(enemy).QueryAttribute(AttributeType.DoubleMaxManaGained).Value != 0)
                        throw new InvalidOperationException("Pure-Blood Elder's aura reached the enemy avatar.");
                    int turns = s.Players[me].QueryAttribute(AttributeType.TurnCount).Value;
                    if (turns >= Simulation.MaxManaUnbuffed_) throw new InvalidOperationException("Fixture is past the natural magicka cap.");
                    int max = avatar.QueryAttribute(AttributeType.MaxMana).Value;
                    f.Event(delegate { DecisionRestoration.RestoreStartTurnMagicka(s, s._turnStateStack.Peek(), s.Players[me]); });
                    int now = avatar.QueryAttribute(AttributeType.MaxMana).Value;
                    if (now != max + 2 || s.Players[me].QueryAttribute(AttributeType.Mana).Value != now)
                        throw new InvalidOperationException("Turn-start gain with Pure-Blood Elder: max " + max + " -> " + now
                            + ", magicka " + s.Players[me].QueryAttribute(AttributeType.Mana).Value);
                }
            });

            test("card-event-titles-have-goals", delegate {
                OfflineTitles.Publish(config, null);
                foreach (string key in OfflineTitles.CardEventTitles())
                    if (OfflineTitles.Kind(key) != "counter") throw new InvalidOperationException("Card-event title has no goal: " + key + " (" + OfflineTitles.Kind(key) + ")");
            });

            test("wrothgar-forge-title-counts-forged-items", delegate {
                OfflineTitles.Publish(config, null);
                int forge = OfflineTitles.CardId("Wrothgar Forge");
                if (forge == 0) throw new InvalidOperationException("Wrothgar Forge not found.");
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    OfflineTitles.AddProgress(d, "title_the_forger", 29);
                    OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    f.CreateIn(forge, me, GameZoneName.SupportBoard, 0);
                    ServerCard creature = f.Create(Crab, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(creature, 0, me);
                    OfflineTitles.RecordProgress(s, d);
                    if (OfflineTitles.Progress(d, "title_the_forger") != 30 || !d.pendingTitleRewards.Contains("title_the_forger"))
                        throw new InvalidOperationException("Wrothgar Forge's 30th item did not grant The Forger: progress " + OfflineTitles.Progress(d, "title_the_forger"));
                }
            });

            test("title-hooks-imperial-tactician-and-pact-warleader", delegate {
                OfflineTitles.Publish(config, null);
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    OfflineTitles.AddProgress(d, OfflineTitleHooks.ImperialTactician, 199);
                    OfflineTitles.AddProgress(d, OfflineTitleHooks.PactWarleader, 199);
                    OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    ServerCard left = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1);
                    ServerCard summoned = f.Create(Crab, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(summoned, 0, me);
                    f.EndTurn(); f.EndTurn();
                    f.Refresh(me);
                    f.Attack(left, f.Avatar(enemy), me);
                    OfflineTitles.RecordProgress(s, d);
                    if (!d.pendingTitleRewards.Contains(OfflineTitleHooks.ImperialTactician))
                        throw new InvalidOperationException("A summon with a creature in each lane did not count: " + OfflineTitles.Progress(d, OfflineTitleHooks.ImperialTactician));
                    if (!d.pendingTitleRewards.Contains(OfflineTitleHooks.PactWarleader))
                        throw new InvalidOperationException("A first surviving attack did not count as Veteran: " + OfflineTitles.Progress(d, OfflineTitleHooks.PactWarleader));
                }
            });

            test("creature-opponents-do-not-borrow-a-voice", delegate {
                int creatures = 0, raced = 0, wrong = 0;
                System.Text.StringBuilder sample = new System.Text.StringBuilder();
                foreach (AiOpponent ai in config.AiOpponents)
                {
                    if (ai == null) continue;
                    MatchPlayerData data = new MatchPlayerData { AvatarUniqueName = ai.ResourceId.UniqueName, Name = OfflineEmotes.OpponentDisplayName(ai), isAIPlayer = true };
                    bool creature = OfflineEmotes.IsCreatureOpponent(data);
                    bool race = OfflineEmotes.OpponentRace(ai, data.Name) != null;
                    if (creature) { creatures++; sample.Append(ai.ResourceId.UniqueName).Append("; "); }
                    string id = ai.ResourceId.UniqueName;
                    if (creature && (id == "DBH_Act2_Ep4_JailGuards" || id == "DBH_Act3_Ep5_TreasuryGuard" || id == "CWC_Act1_Ep1_MistveilGuard" || id == "CWC_Act1_Ep1_DragonPriest" || id == "IOM_Act2_Ep3_MelielsNetchHeder" || id == "CallerOfBeasts_wild_beastcaller"))
                        throw new InvalidOperationException("A person was treated as a creature: " + id);
                    if (race) raced++;
                    if (creature && race) wrong++;
                }
                UnityEngine.Debug.Log("OFFLINE EMOTES TEST: " + creatures + " creature opponents (silent), " + raced + " with a race. e.g. " + sample);
                if (wrong > 0) throw new InvalidOperationException(wrong + " opponents with a race were treated as creatures.");
                if (creatures == 0) throw new InvalidOperationException("No creature opponents recognized.");
            });

            test("stolen-creature-keeps-its-equipped-items", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim;
                    int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    int creatureId = Find(config, "Creature", new[] { "Fearless Northlander", "Torval Crook", "Dunmer Tyro" });
                    int itemId = Find(config, "Item", new[] { "Steel Sword", "Iron Sword", "Steel Dagger", "Iron Mace" });
                    ServerCard wielder = f.CreateIn(creatureId, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard item = f.CreateIn(itemId, enemy, GameZoneName.ItemBoard, 0);
                    f.Event(delegate { CardActions.Equip.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), item, wielder, 0); });
                    int power = wielder.QueryAttribute(AttributeType.Attack).Value;
                    if (item.ItemTarget != wielder.InstanceId) throw new InvalidOperationException("Fixture equip failed.");
                    GameZoneOperationResult moved = GameZoneOperationResult.Failed;
                    int end = f.LaneCount(me, 0);
                    f.Event(delegate {
                        moved = ServerCardCatalog.MoveCard(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                            new GameZoneId(s.Players[enemy].NetId, GameZoneType.Board), new GameZoneId(s.Players[me].NetId, GameZoneType.Board),
                            wielder.InstanceId, new GameZone.Position { LaneIndex = 0, CardIndex = end }, ServerCardCatalog.MoveOperationType.Untyped);
                    });
                    if (moved != GameZoneOperationResult.Succeeded) throw new InvalidOperationException("Steal move failed: " + moved);
                    if (wielder.OwnerNetId != s.Players[me].NetId || wielder.CurrentGameZoneName != GameZoneName.CreatureBoard)
                        throw new InvalidOperationException("The stolen creature is not on the thief's board.");
                    if (item.OwnerNetId != s.Players[me].NetId || item.CurrentGameZoneName != GameZoneName.ItemBoard || item.ItemTarget != wielder.InstanceId)
                        throw new InvalidOperationException("The item did not follow its stolen wielder: owner " + item.OwnerNetId + " zone " + item.CurrentGameZoneName);
                    if (wielder.QueryAttribute(AttributeType.Attack).Value != power)
                        throw new InvalidOperationException("The stolen creature lost its item bonus: " + power + " -> " + wielder.QueryAttribute(AttributeType.Attack).Value);
                }
            });
        }

        private static int Find(GameConfiguration config, string definition, string[] names)
        {
            foreach (string name in names)
                foreach (CardSet set in config.CardCollection)
                    foreach (Card c in set.Cards)
                        if (c != null && c.Name == name && c.Definition == definition) return c.ResourceId.Id;
            throw new InvalidOperationException("No " + definition + " found among the candidates.");
        }
    }
}
