using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    public static class OriginalTargetSpellTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("original-javelin-exact-metadata-atomic-preflight", delegate {
                List<Card> cards = OriginalTargetSpellRules.Validate(config);
                Card first = cards[0], alternate = cards[1]; object wrapper = first.ActionPlayInformation;
                string description = alternate.Description;
                try {
                    alternate.Description += " Additional effect."; bool rejected = false;
                    try { OriginalTargetSpellRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(wrapper, first.ActionPlayInformation), "Javelin recovery mutated before complete metadata validation.");
                } finally { alternate.Description = description; }
                int[] restored = OriginalTargetSpellRules.Restore(config);
                Require(restored.Length == 2 && Object.ReferenceEquals(wrapper, first.ActionPlayInformation), "Javelin restoration is not idempotent.");
            });
            test("original-javelin-both-owner-targets-native-destroy-and-immunity", delegate {
                foreach (int id in new int[] { OriginalTargetSpellRules.PiercingJavelinId, OriginalTargetSpellRules.AlternatePiercingJavelinId })
                {
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    ServerCard spell = f.CreateId(id, GameZoneName.Hand, 0, f.One);
                    ServerCard ally = f.CreateId(794235553, GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.CreateId(794235553, GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard hand = f.CreateId(794235553, GameZoneName.Hand, 0, f.One);
                    PlayCardAbility ability = config.FindCardById(id).ActionPlayInformation[0];
                    List<CardNetUid> targets = f.Catalog.GatherSpellCardTargets(spell, ability.PlayTargetingType, ability.EffectGroup.Effects,
                        ability.TargetSelectors, f.Players, false, false);
                    Require(targets.Contains(ally.NetUid) && targets.Contains(enemy.NetUid) && !targets.Contains(hand.NetUid)
                        && !targets.Contains(f.AvatarOne.NetUid) && !targets.Contains(f.AvatarTwo.NetUid), "Original destroy target selectors include non-board creatures or omit one owner.");
                    enemy.SetAttribute(AttributeType.ImmuneToEnemyActionTargeting, 1);
                    targets = f.Catalog.GatherSpellCardTargets(spell, ability.PlayTargetingType, ability.EffectGroup.Effects, ability.TargetSelectors, f.Players, false, false);
                    Require(!targets.Contains(enemy.NetUid), "Native enemy-action targeting immunity was bypassed.");
                    enemy.SetAttribute(AttributeType.ImmuneToEnemyActionTargeting, 0);
                    ally.SetAttribute(AttributeType.Immortal, 1); f.PlayEffects(spell, ally); f.DrainTriggers();
                    Require(ally.CurrentGameZoneName == GameZoneName.CreatureBoard && !ally.IsDying(), "Native Immortal destroy protection was bypassed.");
                    ally.SetAttribute(AttributeType.Immortal, 0); ally.SetAttribute(AttributeType.Indestructible, 1);
                    f.PlayEffects(spell, ally); f.DrainTriggers();
                    Require(ally.CurrentGameZoneName == GameZoneName.CreatureBoard && !ally.IsDying(), "Native Indestructible destroy protection was bypassed.");
                    ally.SetAttribute(AttributeType.Indestructible, 0);
                    f.PlayEffects(spell, enemy); f.DrainTriggers();
                    Require(f.Catalog.FindCardCount(GameZoneName.CreatureBoard, f.Two.NetId) == 0
                        && f.Catalog.FindCardCount(GameZoneName.Cemetery, f.Two.NetId) == 1 && !enemy.Sacrificed,
                        "Native Javelin destruction did not discard enemy to cemetery without sacrifice.");
                    f.PlayEffects(spell, ally); f.DrainTriggers();
                    Require(f.Catalog.FindCardCount(GameZoneName.CreatureBoard, f.One.NetId) == 0
                        && f.Catalog.FindCardCount(GameZoneName.Cemetery, f.One.NetId) == 1 && hand.CurrentGameZoneName == GameZoneName.Hand,
                        "Native Javelin destruction omitted ally or affected an unselected hand card.");
                }
            });
            test("original-javelin-native-paid-targeted-play", delegate {
                using (LifecycleFixture f = new LifecycleFixture(config))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard spell = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == OriginalTargetSpellRules.PiercingJavelinId; }, s.Players[player].NetId, GameZoneName.Hand);
                    ServerCard target = f.Create(794235553, 1 - player, GameZoneName.CreatureBoard);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(spell, target, player);
                    Require(s.Players[player].QueryAttribute(AttributeType.Mana).Value == 7 && spell.CurrentGameZoneName == GameZoneName.Cemetery
                        && spell.QueryGameJournalCount(CardJournal.Played) == 1 && spell.QueryGameJournalCount(CardJournal.PlayedAsProphecy) == 0
                        && s.CardCatalog.FindCardCount(GameZoneName.CreatureBoard, s.Players[1 - player].NetId) == 0,
                        "Original paid Javelin target resolution/mana/cemetery/journal differs.");
                }
            });
            test("original-javelin-native-prophecy-free-targeted-play", delegate {
                using (LifecycleFixture f = new LifecycleFixture(config))
                {
                    Simulation s = f.Sim; int parent = s._turnStateStack.Peek().ActivePlayerIndex, player = 1 - parent;
                    ServerCard target = f.Create(794235553, parent, GameZoneName.CreatureBoard);
                    int mana = s.Players[player].QueryAttribute(AttributeType.Mana).Value;
                    ServerCard avatar = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, s.Players[player].NetId, GameZoneName.AvatarBoard);
                    f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, s.Players[player], avatar.CurrentHealth - 25, -1); });
                    f.Until(delegate { return s._turnStateStack.Count == 2 && s._turnStateStack.Peek().Phase == TurnPhase.Interrupt
                        && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem != null
                        && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek()); });
                    ServerCard spell = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == OriginalTargetSpellRules.PiercingJavelinId; }, s.Players[player].NetId, GameZoneName.Prophecy);
                    Require(spell != null && spell.QueryAttribute(AttributeType.Prophecy).Value == 1 && spell.QueryAttribute(AttributeType.ManaCost).Value == 5, "Rune draw did not retain original Javelin keyword/cost.");
                    f.Play(spell, target, player);
                    Require(s.Players[player].QueryAttribute(AttributeType.Mana).Value == mana && spell.CurrentGameZoneName == GameZoneName.Cemetery
                        && spell.QueryGameJournalCount(CardJournal.PlayedAsProphecy) == 1 && s._turnStateStack.Peek().ActivePlayerIndex == parent
                        && s.Players[player].QueryAttribute(AttributeType.NumRunes).Value == 4
                        && s.CardCatalog.FindCardCount(GameZoneName.CreatureBoard, s.Players[parent].NetId) == 0,
                        "Original free targeted Javelin failed to destroy/resume or consumed mana/extra runes.");
                }
            });
        }
        private static bool Ready(Simulation s)
        { return s._gameState.GamePhase == GamePhase.MainLoop && s._turnStateStack.Count == 1 && s._turnStateStack.Peek().Phase == TurnPhase.PlayCards
            && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem != null && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek()); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        // Bounded server fixture; twelve duplicate originals isolate targeted action
        // and rune orchestration. This is not a legal user deck or physical UI test.
        private sealed class LifecycleFixture : IDisposable
        {
            public readonly Simulation Sim = new Simulation();
            private readonly IDisposable scope;
            public LifecycleFixture(GameConfiguration config)
            {
                scope = OrdinaryMatchRules.Prepare(config);
                try
                {
                    MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo { ScenarioId = OrdinaryMatchRules.DefaultScenarioId, RandomNumberGenerator = new RandomIntegerGenerator(12345) };
                    for (int slot = 0; slot < 2; slot++)
                    {
                        PlayerDescription d = new PlayerDescription { PlayerName = "Original Javelin server test " + slot, AvatarCardId = OrdinaryMatchRules.BaseAvatarId, AiOpponentId = 0 };
                        for (int n = 0; n < 12; n++) { d.Deck.Add(OriginalTargetSpellRules.PiercingJavelinId); d.Premium.Add(false); }
                        info.Users[slot] = new MatchConfiguration.User { CustomDeck = d, Type = PlayerType.AiOpponent, EncryptionKey = new byte[32], ObserverEncryptionKey = new byte[32], SecretKey = "javelin-test-" + slot, SpectatorKey = "javelin-observer-" + slot };
                    }
                    LifecycleRestoration.Start(Sim, null, info); Sim._disableAiDecisions = true;
                    Until(delegate { return Sim._gameState.GamePhase == GamePhase.Mulligan; });
                    foreach (PlayerState player in Sim.Players) Sim.ReportMulliganDecision(player, new MulliganResponse { Action = MulliganAction.Commit, MulliganCardNetworkUids = new List<int>() });
                    Until(delegate { return Ready(Sim); });
                }
                catch { Dispose(); throw; }
            }
            public ServerCard Create(int id, int owner, GameZoneName zone)
            {
                ServerCard result = null;
                Event(delegate { result = ServerCardCatalog.CreateCard(DecisionRestoration.State(Sim, Sim._turnStateStack.Peek()), id, false, zone, 0,
                    CardInsertionType.Bottom, Sim.Players[owner].NetId, null, null); }); return result;
            }
            public void Event(Action action)
            { Sim.HistoryLog.StartEvent(HistoryType.EventTrigger, Sim.CurrentPlayer.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1); try { action(); } finally { Sim.HistoryLog.EndEvent(); } }
            public void Play(ServerCard spell, ServerCard target, int player)
            {
                CardCommandList list = Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
                Decision decision = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = spell.NetUid.Id, TargetCardNetUid = target.NetUid.Id };
                Require(CommandRestoration.ValidateDecision(decision, list, Sim.CardCatalog, Sim.Players[player]), "Original targeted Javelin command is invalid.");
                Sim.PlayerServiceClients[player]._decisionMessages.Enqueue(decision);
                Until(delegate { return Ready(Sim) && Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
            }
            public void Until(Func<bool> done)
            { for (int tick = 0; tick < 500; tick++) { if (done()) return; LifecycleRestoration.FixedUpdate(Sim); } throw new InvalidOperationException("Javelin native sequence did not settle: " + Sim._gameState.GamePhase); }
            public void Dispose()
            { RuneProphecyRestoration.Clear(Sim); if (Sim.PlayerServiceClients != null) foreach (PlayerServiceClient client in Sim.PlayerServiceClients) if (client != null) client.Stop(); scope.Dispose(); }
        }
    }
}
