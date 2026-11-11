using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalTokenLaneTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069, Rage = 1055804545;
        private const int Quartermaster = -474818882, Reclaimer = -146382907, CorpseCurse = 33791690, ArrowStorm = 105525150, DawnsWrath = -916534147, FireStorm = 1095134115;

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-token-6-exact-metadata-original-references-supported-tokens", delegate {
                List<Card> cards = OriginalTokenRules.Validate(config);
                Require(cards.Count == 6, "Original token inventory differs.");
                object before = cards[0].TriggeredEffects; string text = cards[5].Description;
                try {
                    cards[5].Description += " Extra effect."; bool rejected = false;
                    try { OriginalTokenRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Token atomic preflight failed.");
                } finally { cards[5].Description = text; }
                OriginalTokenRules.Restore(config); before = cards[0].TriggeredEffects; OriginalTokenRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Token graph identity changed.");
                foreach (Card card in cards)
                {
                    Require(RuleImporter.IsSupported(card.ResourceId.Id), "Token creator not supported: " + card.ResourceId.Id);
                    foreach (int token in OriginalTokenRules.TokenIds(config, card.ResourceId.Id))
                        Require(RuleImporter.IsSupported(token), "Created original token card is unsupported: " + token);
                }
                Require(OriginalTokenRules.TokenIds(config, Reclaimer).Length == 2, "Sentinel Reclaimer original references differ.");
                PlayerActions.CreateToken create = (PlayerActions.CreateToken)config.FindCardById(Quartermaster).TriggeredEffects[0].EffectGroup.Effects[0].PlayerActions[0].Action;
                int id = create.CardReference.CardIdReference;
                try {
                    create.CardReference.CardIdReference = Mudcrab; bool rejected = false;
                    try { OriginalTokenRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed token reference admitted.");
                } finally { create.CardReference.CardIdReference = id; }
            });

            test("original-token-native-create-in-hand-owner-and-full-hand", delegate {
                foreach (Card card in OriginalTokenRules.Validate(config))
                {
                    int id = card.ResourceId.Id; int[] tokens = OriginalTokenRules.TokenIds(config, id);
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    ServerCard source = f.CreateId(id, GameZoneName.CreatureBoard, 0, f.One);
                    Fire(f, source, OriginalTokenRules.TriggerOf(id));
                    List<ServerCard> hand = f.Catalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, f.One.NetId, GameZoneName.Hand);
                    Require(hand.Count == tokens.Length && f.Catalog.FindCardCount(GameZoneName.Hand, f.Two.NetId) == 0, "Token count/owner differs: " + id);
                    foreach (int token in tokens) Require(hand.Exists(delegate(ServerCard c) { return c.CardId == token; }), "Expected original token not created: " + id + " / " + token);
                    // Native CreateToken finds no Hand insertion position in a full hand.
                    EngineTests.Fixture g = new EngineTests.Fixture(config);
                    for (int n = 0; n < 10; n++) g.CreateId(Mudcrab, GameZoneName.Hand, 0, g.One);
                    ServerCard full = g.CreateId(id, GameZoneName.CreatureBoard, 0, g.One);
                    Fire(g, full, OriginalTokenRules.TriggerOf(id));
                    Require(g.Catalog.FindCardCount(GameZoneName.Hand, g.One.NetId) == 10
                        && !g.Catalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == tokens[0]; }, g.One.NetId, GameZoneName.Hand).Exists(delegate(ServerCard c) { return true; }),
                        "Token entered a full hand: " + id);
                }
            });

            test("original-token-real-play-death-and-playable-token", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard quartermaster = f.Create(Quartermaster, player, GameZoneName.Hand);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    int token = OriginalTokenRules.TokenIds(config, Quartermaster)[0];
                    f.Play(quartermaster, null, player);
                    ServerCard dagger = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == token; }, s.Players[player].NetId, GameZoneName.Hand);
                    Require(dagger != null && quartermaster.CurrentGameZoneName == GameZoneName.CreatureBoard, "Crown Quartermaster did not put its original Steel Dagger into hand.");
                    int attack = quartermaster.QueryAttribute(AttributeType.Attack).Value;
                    CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision equip = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = dagger.NetUid.Id, EquipCardNetUid = quartermaster.NetUid.Id };
                    Require(CommandRestoration.ValidateDecision(equip, list, s.CardCatalog, s.Players[player]), "Created Steel Dagger is not playable onto Crown Quartermaster.");
                    s.PlayerServiceClients[player]._decisionMessages.Enqueue(equip);
                    f.Until(delegate { return s._turnStateStack.Peek().CurrentCommandItem != null && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid
                        && s._turnStateStack.Peek().CurrentSequence == null && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek()); });
                    Require(quartermaster.QueryAttribute(AttributeType.Attack).Value > attack, "Created Steel Dagger did not equip natively.");
                }
                foreach (Card card in OriginalTokenRules.Validate(config))
                {
                    int id = card.ResourceId.Id; if (OriginalTokenRules.TriggerOf(id) != EffectTriggerType.LastGaspTrigger) continue;
                    int token = OriginalTokenRules.TokenIds(config, id)[0];
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard source = f.Create(id, player, GameZoneName.CreatureBoard);
                        ServerCard rage = f.Create(Rage, player, GameZoneName.Hand);
                        f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                        s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                        f.Play(rage, source, player);
                        Require(f.CemeteryHas(player, id) && s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == token; }, s.Players[player].NetId, GameZoneName.Hand) != null,
                            "Real Last Gasp token creation differs: " + id);
                    }
                }
            });

            test("original-lane-spell-6-exact-metadata-and-lane-target-legality", delegate {
                List<Card> cards = OriginalLaneSpellRules.Validate(config);
                Require(cards.Count == 6, "Original lane spell inventory differs.");
                object before = cards[0].ActionPlayInformation; OriginalLaneSpellRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].ActionPlayInformation), "Lane spell graph identity changed.");
                CardConditionals.InTargetedLane lane = (CardConditionals.InTargetedLane)config.FindCardById(CorpseCurse).ActionPlayInformation[0].EffectGroup.Effects[0].CardActions[0].Selectors[2];
                try {
                    lane.Owner = CardConditionals.InTargetedLane.OwnerRequirement.AnyPlayer; bool rejected = false;
                    try { OriginalLaneSpellRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed lane owner requirement admitted.");
                } finally { lane.Owner = CardConditionals.InTargetedLane.OwnerRequirement.SamePlayer; }
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 12);
                    ServerCard curse = OriginalTargetedSummonTests.Hand(f, CorpseCurse), wrath = OriginalTargetedSummonTests.Hand(f, DawnsWrath);
                    CardCommandList commands = f.Commands();
                    List<BoardLaneTarget> enemy = new List<BoardLaneTarget>(OriginalTargetedSummonTests.Command(commands, curse).LaneTargets);
                    List<BoardLaneTarget> any = new List<BoardLaneTarget>(OriginalTargetedSummonTests.Command(commands, wrath).LaneTargets);
                    Require(enemy.Count == 2 && enemy.Contains(BoardLaneTarget.OtherLeftLane) && enemy.Contains(BoardLaneTarget.OtherRightLane), "Enemy lane choices differ.");
                    Require(any.Count == 4 && OriginalTargetedSummonTests.Command(commands, wrath).PlayTargets.Count == 0, "Any-lane choices differ.");
                    Decision mine = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = commands.ListUid, ActorCardNetUid = curse.NetUid.Id, LaneTarget = BoardLaneTarget.MyLeftLane };
                    Require(!CommandRestoration.ValidateDecision(mine, commands, f.Sim.CardCatalog, f.One), "Own lane admitted for an enemy-lane spell.");
                }
            });

            test("original-lane-spell-6-paid-native-plays", delegate {
                foreach (Card card in OriginalLaneSpellRules.Validate(config))
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int id = card.ResourceId.Id, a = OriginalLaneSpellRules.Value(id);
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard spell = f.Create(id, player, GameZoneName.Hand);
                    ServerCard enemyBig = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                    ServerCard enemySmall = f.Create(Mudcrab, 1 - player, GameZoneName.CreatureBoard);
                    ServerCard enemyFar = f.CreateIn(Mudcrab, 1 - player, GameZoneName.CreatureBoard, 1);
                    ServerCard allyNear = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                    ServerCard allySmall = f.Create(Mudcrab, player, GameZoneName.CreatureBoard);
                    int cost = spell.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.PlayLane(spell, BoardLaneTarget.OtherLeftLane, player);
                    bool ok; OriginalLaneSpellRules.Kind kind = OriginalLaneSpellRules.KindOf(id);
                    switch (kind)
                    {
                        case OriginalLaneSpellRules.Kind.ShackleEnemyLane:
                            ok = enemyBig.QueryAttribute(AttributeType.Freeze).Value > 0 && enemySmall.QueryAttribute(AttributeType.Freeze).Value > 0
                                && enemyFar.QueryAttribute(AttributeType.Freeze).Value == 0 && allyNear.QueryAttribute(AttributeType.Freeze).Value == 0; break;
                        case OriginalLaneSpellRules.Kind.DestroyEnemyLowPowerLane:
                            ok = enemySmall.CurrentGameZoneName != GameZoneName.CreatureBoard && enemyBig.CurrentGameZoneName == GameZoneName.CreatureBoard
                                && enemyFar.CurrentGameZoneName == GameZoneName.CreatureBoard && allySmall.CurrentGameZoneName == GameZoneName.CreatureBoard; break;
                        case OriginalLaneSpellRules.Kind.DestroyLane:
                            ok = enemyBig.CurrentGameZoneName != GameZoneName.CreatureBoard && allyNear.CurrentGameZoneName != GameZoneName.CreatureBoard
                                && enemyFar.CurrentGameZoneName == GameZoneName.CreatureBoard; break;
                        default:
                            ok = enemyBig.CurrentHealth == 8 - a && allyNear.CurrentHealth == 8 - a && enemyFar.CurrentHealth == 1
                                && enemySmall.CurrentGameZoneName != GameZoneName.CreatureBoard; break;
                    }
                    Require(ok && spell.CurrentGameZoneName == GameZoneName.Cemetery && s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - cost
                        && spell.QueryGameJournalCount(CardJournal.Played) == 1, "Original paid lane spell differs: " + id);
                }
            });

            test("strategic-original-arrow-storm-chooses-killable-enemy-lane", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 3);
                    ServerCard storm = OriginalTargetedSummonTests.Hand(f, ArrowStorm);
                    f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard a = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 1, f.Two);
                    ServerCard b = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 1, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == storm.NetUid.Id && choice.LaneTarget == BoardLaneTarget.OtherRightLane,
                        "Original Arrow Storm strategy did not pick the lane with killable enemies: " + choice.Type + " " + choice.LaneTarget);
                    Require(a.CurrentGameZoneName == GameZoneName.CreatureBoard && b.CurrentGameZoneName == GameZoneName.CreatureBoard, "Lane ranking mutated native state.");
                }
            });
        }

        private static void Fire(EngineTests.Fixture f, ServerCard source, EffectTriggerType type)
        {
            Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(type, source, f.Players)), "Token trigger not gathered: " + source.CardId);
            f.DrainTriggers();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
