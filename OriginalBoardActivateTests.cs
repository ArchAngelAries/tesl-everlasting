using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalBoardActivateTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069, Rage = 1055804545;
        private const int Captain = 1322155544, Draugr = -481674007, Reinforcements = 398739133, Conflict = -1574884301, Wraithguard = 1839123663, Ravaging = 472460151, Vitality = 1469820870;

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("elixir-title-counters-real-activations", delegate {
                OfflineTitles.Publish(config, null);
                int[] ids = { 539510398, -646559647, -387279246, 1705848025 };
                string[] keys = { "title_the_deflector", "title_the_defender", "title_the_lightfooted", "title_the_vigorous" };
                for (int i = 0; i < ids.Length; i++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    OfflinePlayerStateData d = new OfflinePlayerStateData(); int threshold = i == 3 ? 100 : 50;
                    OfflineTitles.AddProgress(d, keys[i], threshold - 1); OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    ServerCard support = f.Create(ids[i], me, GameZoneName.SupportBoard);
                    ServerCard creature = f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                    f.Refresh(me); f.Activate(support, creature, me);
                    OfflineTitles.RecordProgress(s, d);
                    Require(OfflineTitles.Progress(d, keys[i]) == threshold && d.pendingTitleRewards.Contains(keys[i]),
                        "Elixir activation did not reach title threshold: " + keys[i]);
                }
            });
            test("mundus-title-counter-real-summon", delegate {
                OfflineTitles.Publish(config, null);
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    OfflineTitles.AddProgress(d, "title_the_walker_of_mundus", 49); OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    f.Create(1962253065, me, GameZoneName.SupportBoard);
                    ServerCard creature = f.Create(Mudcrab, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(creature, 0, me);
                    OfflineTitles.RecordProgress(s, d);
                    Require(OfflineTitles.Progress(d, "title_the_walker_of_mundus") == 50 && d.pendingTitleRewards.Contains("title_the_walker_of_mundus"),
                        "Mundus Stone's keyword did not reach the title threshold.");
                }
            });
            test("original-board-token-15-exact-metadata-token-identity", delegate {
                List<Card> cards = OriginalBoardTokenRules.Validate(config);
                Require(cards.Count == 15, "Original board-token inventory differs.");
                object before = cards[0].TriggeredEffects; OriginalBoardTokenRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Board-token graph identity changed.");
                foreach (Card card in cards)
                {
                    int token = OriginalBoardTokenRules.TokenId(config, card.ResourceId.Id);
                    Require(RuleImporter.IsSupported(card.ResourceId.Id) && RuleImporter.IsSupported(token), "Board token creator or token unsupported: " + card.ResourceId.Id + " / " + token);
                }
                PlayerActions.CreateToken create = (PlayerActions.CreateToken)config.FindCardById(Captain).TriggeredEffects[0].EffectGroup.Effects[0].PlayerActions[0].Action;
                try {
                    create.InsertPosition.Comparison = InsertPosition.PositionComparison.OtherLane; bool rejected = false;
                    try { OriginalBoardTokenRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed token placement admitted.");
                } finally { create.InsertPosition.Comparison = InsertPosition.PositionComparison.SameLane; }
            });

            test("original-board-token-native-placement-and-full-lane", delegate {
                foreach (Card card in OriginalBoardTokenRules.Validate(config))
                {
                    int id = card.ResourceId.Id; OriginalBoardTokenRules.Place place = OriginalBoardTokenRules.PlaceOf(id);
                    if (place == OriginalBoardTokenRules.Place.FillChosenLane) continue;
                    int token = OriginalBoardTokenRules.TokenId(config, id);
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    ServerCard source = f.CreateId(id, GameZoneName.CreatureBoard, 0, f.One);
                    Fire(f, source, OriginalBoardTokenRules.TriggerOf(id));
                    int left = Count(f, f.One, token, 0), right = Count(f, f.One, token, 1);
                    bool ok = place == OriginalBoardTokenRules.Place.SameLane ? left == 1 && right == 0
                        : place == OriginalBoardTokenRules.Place.OtherLane ? left == 0 && right == 1 : left == 1 && right == 1;
                    Require(ok && Count(f, f.Two, token, 0) + Count(f, f.Two, token, 1) == 0, "Board token placement differs: " + id + " left=" + left + " right=" + right);
                    if (place == OriginalBoardTokenRules.Place.SameLane)
                    {
                        EngineTests.Fixture g = new EngineTests.Fixture(config);
                        ServerCard full = g.CreateId(id, GameZoneName.CreatureBoard, 0, g.One);
                        for (int n = 0; n < 3; n++) g.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, g.One);
                        Fire(g, full, OriginalBoardTokenRules.TriggerOf(id));
                        Require(Count(g, g.One, token, 0) + Count(g, g.One, token, 1) == 0, "Board token entered a full lane: " + id);
                    }
                }
            });

            test("original-board-token-real-play-death-and-fill-lane", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard captain = f.Create(Captain, player, GameZoneName.Hand);
                    ServerCard draugr = f.Create(Draugr, player, GameZoneName.CreatureBoard);
                    ServerCard rage = f.Create(Rage, player, GameZoneName.Hand);
                    int herald = OriginalBoardTokenRules.TokenId(config, Captain), skeleton = OriginalBoardTokenRules.TokenId(config, Draugr);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(captain, null, player);
                    Require(s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == herald && c.CurrentLaneIndex == 0; }, s.Players[player].NetId, GameZoneName.CreatureBoard) != null,
                        "Northpoint Captain paid play did not summon its Herald in the same lane.");
                    f.Play(rage, draugr, player);
                    Require(f.CemeteryHas(player, Draugr) && s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == skeleton && c.CurrentLaneIndex == 0; }, s.Players[player].NetId, GameZoneName.CreatureBoard) != null,
                        "Deathless Draugr real Last Gasp did not summon its Skeleton in the death lane.");
                }
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard spell = f.Create(Reinforcements, player, GameZoneName.Hand);
                    f.CreateIn(Leviathan, player, GameZoneName.CreatureBoard, 1);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    CardCommandState state = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, spell);
                    Require(state != null && state.LaneTargets.Count == 2 && state.LaneTargets.Contains(BoardLaneTarget.MyRightLane), "Fill-a-lane choices differ from own lanes.");
                    f.PlayLane(spell, BoardLaneTarget.MyRightLane, player);
                    GameZone board = s.CardCatalog.FindGameZone(s.Players[player].NetId, GameZoneName.CreatureBoard);
                    Require(board.LaneList[1].IsLaneFull() && f.LaneCount(player, 0) == 0 && f.LaneCount(1 - player, 1) == 0 && spell.CurrentGameZoneName == GameZoneName.Cemetery,
                        "Imperial Reinforcements did not fill exactly the chosen own lane: lane1=" + f.LaneCount(player, 1));
                }
            });

            test("original-activate-support-10-exact-metadata-and-native-effects", delegate {
                List<Card> cards = OriginalActivateSupportRules.Validate(config);
                Require(cards.Count == 10, "Original Activate support inventory differs.");
                object before = cards[0].ActivatedAbility; OriginalActivateSupportRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].ActivatedAbility), "Activate graph identity changed.");
                foreach (Card card in cards)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int id = card.ResourceId.Id, a = OriginalActivateSupportRules.ValueA(id), b = OriginalActivateSupportRules.ValueB(id);
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard support = f.Create(id, player, GameZoneName.SupportBoard);
                    ServerCard ally = f.Create(Leviathan, player, GameZoneName.CreatureBoard), enemy = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                    ServerCard avatar = f.Avatar(player), enemyAvatar = f.Avatar(1 - player);
                    f.Event(delegate { avatar.SetAvatarHealthPreGame(29); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    OriginalActivateSupportRules.Kind kind = OriginalActivateSupportRules.KindOf(id);
                    bool targeted = kind != OriginalActivateSupportRules.Kind.OpponentDamage && kind != OriginalActivateSupportRules.Kind.Heal;
                    if (kind == OriginalActivateSupportRules.Kind.GuardFriendly)
                    {
                        CardCommandState state = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, support);
                        Require(state != null && !state.ActiveAbilityTargets.Contains(enemy.NetUid.Id) && state.ActiveAbilityTargets.Contains(ally.NetUid.Id), "Friendly-only activation targets differ.");
                    }
                    f.Activate(support, targeted ? ally : null, player);
                    bool ok;
                    switch (kind)
                    {
                        case OriginalActivateSupportRules.Kind.Buff: ok = ally.QueryAttribute(AttributeType.Attack).Value == 8 + a && ally.CurrentHealth == 8 + b; break;
                        case OriginalActivateSupportRules.Kind.Ward: ok = ally.QueryAttribute(AttributeType.Ward).Value > 0; break;
                        case OriginalActivateSupportRules.Kind.Cover: ok = ally.QueryAttribute(AttributeType.Cover).Value > 0; break;
                        case OriginalActivateSupportRules.Kind.GuardFriendly: ok = ally.QueryAttribute(AttributeType.Taunt).Value > 0; break;
                        case OriginalActivateSupportRules.Kind.BuffBreakthrough: ok = ally.QueryAttribute(AttributeType.Attack).Value == 8 + a && ally.CurrentHealth == 8 + b && ally.QueryAttribute(AttributeType.Breakthrough).Value > 0; break;
                        case OriginalActivateSupportRules.Kind.OpponentDamage: ok = enemyAvatar.CurrentHealth == 30 - a; break;
                        default: ok = avatar.CurrentHealth == 29 + a; break;
                    }
                    // Native IncrementAbilityUseCount does not add to TotalAbilityUse for UnlimitedUses supports.
                    Require(ok && enemy.CurrentHealth == 8 && support.NumTimesActivated == 1
                        && support.TotalAbilityUse == (support.QueryAttribute(AttributeType.UnlimitedUses).Value > 0 ? 0 : 1), "Original Activate effect differs: " + id);
                }
            });

            test("original-activate-support-paid-play-uses-exhaustion-and-unlimited", delegate {
                foreach (int id in new int[] { Conflict, Wraithguard })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard support = f.Create(id, player, GameZoneName.Hand);
                    ServerCard ally = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                    int cost = support.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(support, null, player);
                    Require(support.CurrentGameZoneName == GameZoneName.SupportBoard && s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - cost, "Support paid play differs: " + id);
                    for (int use = 0; use < 4; use++)
                    {
                        if (support.CurrentGameZoneName != GameZoneName.SupportBoard) break;
                        f.Event(delegate {
                            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                            support.OnEndTurn(s.Players[player].NetId, gs); support.OnStartTurn(s.Players[player].NetId, gs);
                        });
                        s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                        if (use == 0)
                        {
                            // Native per-turn limit: a second activation in the same turn is not offered.
                            f.Activate(support, ally, player);
                            CardCommandState again = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, support);
                            Require(again == null || (again.ValidCardCommandMask & (int)CardCommand.ActiveAbility) == 0, "Support activated twice in one turn: " + id);
                        }
                        else f.Activate(support, ally, player);
                    }
                    if (id == Conflict) Require(support.CurrentGameZoneName != GameZoneName.SupportBoard && f.CemeteryHas(player, id) && ally.QueryAttribute(AttributeType.Attack).Value == 11,
                        "Elixir of Conflict was not discarded after its three original uses.");
                    else Require(support.CurrentGameZoneName == GameZoneName.SupportBoard && support.NumTimesActivated == 4 && support.TotalAbilityUse == 0 && ally.QueryAttribute(AttributeType.Ward).Value > 0,
                        "Wraithguard II (UnlimitedUses, Indestructible) did not survive four uses.");
                }
            });

            test("strategic-original-activate-support-targets-friendly-buff", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    ServerCard support = ServerCardCatalog.CreateCard(f.State, Conflict, false, GameZoneName.SupportBoard, 0, CardInsertionType.Bottom, f.One.NetId, null, null);
                    ServerCard ally = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Activate && choice.ActorCardNetUid == support.NetUid.Id && choice.TargetCardNetUid == ally.NetUid.Id,
                        "AI did not activate the buff support on its own creature.");
                    Require(ally.QueryAttribute(AttributeType.Attack).Value == 8 && support.TotalAbilityUse == 0, "Activate ranking mutated native state.");
                }
            });
        }

        private static int Count(EngineTests.Fixture f, PlayerState owner, int token, int lane)
        { return f.Catalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == token && c.CurrentLaneIndex == lane; }, owner.NetId, GameZoneName.CreatureBoard).Count; }
        private static void Fire(EngineTests.Fixture f, ServerCard source, EffectTriggerType type)
        {
            Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(type, source, f.Players)), "Board token trigger not gathered: " + source.CardId);
            f.DrainTriggers();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
