using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalRemovalUtilityTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069; // Swamp Leviathan 8/8, Enraged Mudcrab 2/1.
        private const int Rage = 1055804545, Execute = -37005376, FinishOff = -1991073211, Leaflurker = -427132516, Footpad = -1248991397,
            Courier = 416064061, TinyDragon = 888500878, Web = 1640425267, Grasp = -1912323229;
        private static OriginalRemovalUtilityRules.Kind K(int id) { return OriginalRemovalUtilityRules.KindOf(id); }
        private static bool Gone(ServerCard c) { return c.IsDying() || c.CurrentGameZoneName != GameZoneName.CreatureBoard; }
        private static void Wound(EngineTests.Fixture f, ServerCard c) { c.ModifyCurrentHealth(-1, null, f.State, true); }

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-removal-utility-17-exact-atomic-metadata-owned-graph", delegate {
                List<Card> cards = OriginalRemovalUtilityRules.Validate(config);
                Require(cards.Count == 17 && OriginalRemovalUtilityRules.Count == 17, "Original removal/utility inventory differs.");
                object before = cards[0].ActionPlayInformation; string text = cards[16].Description;
                try {
                    cards[16].Description += " Extra effect."; bool rejected = false;
                    try { OriginalRemovalUtilityRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(before, cards[0].ActionPlayInformation), "Removal/utility atomic preflight failed.");
                } finally { cards[16].Description = text; }
                OriginalRemovalUtilityRules.Restore(config); before = cards[0].ActionPlayInformation;
                OriginalRemovalUtilityRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].ActionPlayInformation), "Removal/utility graph identity changed.");
                CardConditionals.AttributeValue limit = (CardConditionals.AttributeValue)config.FindCardById(Execute).ActionPlayInformation[0].TargetSelectors[0].CardConditionalExpressions[2];
                try {
                    limit.Value.ConstantValue = 3; bool rejected = false;
                    try { OriginalRemovalUtilityRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed Execute power limit admitted.");
                } finally { limit.Value.ConstantValue = 2; }
                TriggeredEffect gasp = config.FindCardById(Courier).TriggeredEffects[0];
                try {
                    gasp.TriggerType = EffectTriggerType.Summon; bool rejected = false;
                    try { OriginalRemovalUtilityRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed Last Gasp trigger type admitted.");
                } finally { gasp.TriggerType = EffectTriggerType.LastGaspTrigger; }
                foreach (Card card in cards) Require(RuleImporter.IsSupported(card.ResourceId.Id), "Removal/utility ID not supported: " + card.ResourceId.Id);
            });

            test("original-removal-utility-native-effects", delegate {
                foreach (Card card in OriginalRemovalUtilityRules.Validate(config))
                {
                    int id = card.ResourceId.Id, a = OriginalRemovalUtilityRules.Value(id);
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    OriginalRemovalUtilityRules.Kind kind = K(id);
                    bool creature = kind == OriginalRemovalUtilityRules.Kind.DestroyWoundedSummon || kind == OriginalRemovalUtilityRules.Kind.CoverSummon
                        || kind == OriginalRemovalUtilityRules.Kind.LastGaspDraw || kind == OriginalRemovalUtilityRules.Kind.LaneDamageSummon;
                    ServerCard source = f.CreateId(id, creature ? GameZoneName.CreatureBoard : GameZoneName.Hand, 0, f.One);
                    ServerCard ally = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard far = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, f.Two);
                    string fail = "Original removal/utility native effect differs: " + id;
                    switch (kind)
                    {
                        case OriginalRemovalUtilityRules.Kind.DamageCreatureSpell:
                            f.PlayEffects(source, enemy);
                            Require(a >= 8 ? (enemy.CurrentHealth <= 0 || Gone(enemy)) : enemy.CurrentHealth == 8 - a, fail);
                            Require(ally.CurrentHealth == 8 && f.AvatarTwo.CurrentHealth == 30, fail); break;
                        case OriginalRemovalUtilityRules.Kind.ShackleSpell:
                            f.PlayEffects(source, enemy);
                            Require(enemy.QueryAttribute(AttributeType.Freeze).Value > 0 && far.QueryAttribute(AttributeType.Freeze).Value == 0, fail); break;
                        case OriginalRemovalUtilityRules.Kind.OpponentDamageDraw:
                            f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One);
                            int hand = f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId);
                            f.PlayEffects(source, null);
                            Require(f.AvatarTwo.CurrentHealth == 30 - a && f.AvatarOne.CurrentHealth == 30
                                && f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId) == hand + 1 && f.Catalog.FindCardCountInDeck(f.One.NetId) == 0, fail); break;
                        case OriginalRemovalUtilityRules.Kind.DestroyLowPowerSpell:
                            ServerCard small = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 1, f.Two);
                            f.PlayEffects(source, small);
                            Require(Gone(small) && !Gone(enemy), fail); break;
                        case OriginalRemovalUtilityRules.Kind.DestroyWoundedEnemySpell:
                        case OriginalRemovalUtilityRules.Kind.DestroyWoundedSummon:
                            Wound(f, enemy);
                            f.PlayEffects(source, enemy);
                            Require(Gone(enemy) && !Gone(far) && !Gone(ally), fail); break;
                        case OriginalRemovalUtilityRules.Kind.CoverSummon:
                            f.PlayEffects(source, ally);
                            Require(ally.QueryAttribute(AttributeType.Cover).Value > 0, fail);
                            ServerCard guard = f.CreateId(Courier, GameZoneName.CreatureBoard, 1, f.One);
                            f.PlayEffects(source, guard);
                            Require(guard.QueryAttribute(AttributeType.Cover).Value == 0, "Native Guard/Cover exclusion bypassed: " + id); break;
                        case OriginalRemovalUtilityRules.Kind.LastGaspDraw:
                            f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One);
                            int before = f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId);
                            Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.LastGaspTrigger, source, f.Players)),
                                "Last Gasp trigger not gathered: " + id);
                            f.DrainTriggers();
                            Require(f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId) == before + 1 && f.Catalog.FindCardCount(GameZoneName.Hand, f.Two.NetId) == 0, fail); break;
                        case OriginalRemovalUtilityRules.Kind.LaneDamageSummon:
                            Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, source, f.Players)),
                                "Lane Summon trigger not gathered: " + id);
                            f.DrainTriggers();
                            Require(enemy.CurrentHealth == 8 - a && far.CurrentHealth == 8 && ally.CurrentHealth == 8 && f.AvatarTwo.CurrentHealth == 30, fail); break;
                    }
                }
            });

            test("original-removal-utility-command-target-legality", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 12);
                    ServerCard execute = OriginalTargetedSummonTests.Hand(f, Execute), finish = OriginalTargetedSummonTests.Hand(f, FinishOff),
                        lurker = OriginalTargetedSummonTests.Hand(f, Leaflurker), web = OriginalTargetedSummonTests.Hand(f, Web);
                    ServerCard bigAlly = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard smallAlly = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 1, f.One);
                    ServerCard bigEnemy = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard smallEnemy = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 1, f.Two);
                    CardCommandList commands = f.Commands();
                    List<int> low = new List<int>(OriginalTargetedSummonTests.Command(commands, execute).PlayTargets);
                    Require(low.Count == 2 && low.Contains(smallAlly.NetUid.Id) && low.Contains(smallEnemy.NetUid.Id), "Execute power-limit targets differ.");
                    CardCommandState finishState = OriginalTargetedSummonTests.Command(commands, finish);
                    Require(finishState == null || (finishState.ValidCardCommandMask & 15) == 0, "Finish Off playable without a Wounded enemy.");
                    CardCommandState lurkerState = OriginalTargetedSummonTests.Command(commands, lurker);
                    Require(lurkerState != null && (lurkerState.ValidCardCommandMask & (int)CardCommand.PlayToPosition) != 0 && lurkerState.PlayTargets.Count == 0,
                        "Leaflurker not playable without a Wounded creature.");
                    List<int> shackle = new List<int>(OriginalTargetedSummonTests.Command(commands, web).PlayTargets);
                    Require(shackle.Count == 2 && !shackle.Contains(bigAlly.NetUid.Id) && !shackle.Contains(smallAlly.NetUid.Id), "Shackle action targeted an ally.");
                    bigAlly.ModifyCurrentHealth(-1, null, f.State, true); bigEnemy.ModifyCurrentHealth(-1, null, f.State, true);
                    // A health debuff without damage is also Wounded (native HasHealthDebuff; UESP Legends:Wounded).
                    ServerCard debuffed = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 1, f.Two);
                    SPTESL.Enchantment minus = new SPTESL.Enchantment();
                    minus.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, -1));
                    CardActions.Buff.StaticExecute(f.State, null, new List<ServerCard> { debuffed }, minus, f.One, -1, false);
                    Require(debuffed.CurrentHealth == 7 && debuffed.QueryAttribute(AttributeType.Wounded).Value > 0, "Native health debuff did not set Wounded.");
                    commands = f.Commands();
                    List<int> wounded = new List<int>(OriginalTargetedSummonTests.Command(commands, finish).PlayTargets);
                    List<int> anyWounded = new List<int>(OriginalTargetedSummonTests.Command(commands, lurker).PlayTargets);
                    Require(wounded.Count == 2 && wounded.Contains(bigEnemy.NetUid.Id) && wounded.Contains(debuffed.NetUid.Id), "Finish Off targets differ from the Wounded enemies.");
                    Require(anyWounded.Count == 3 && anyWounded.Contains(bigAlly.NetUid.Id) && anyWounded.Contains(bigEnemy.NetUid.Id), "Leaflurker Wounded targets differ.");
                }
            });

            test("original-removal-utility-17-paid-native-plays", delegate {
                foreach (Card card in OriginalRemovalUtilityRules.Validate(config))
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int id = card.ResourceId.Id, a = OriginalRemovalUtilityRules.Value(id);
                    OriginalRemovalUtilityRules.Kind kind = K(id);
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source = f.Create(id, player, GameZoneName.Hand);
                    ServerCard ally = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                    ServerCard enemy = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                    ServerCard small = f.CreateIn(Mudcrab, 1 - player, GameZoneName.CreatureBoard, 1);
                    ServerCard enemyAvatar = f.Avatar(1 - player);
                    int cost = source.QueryAttribute(AttributeType.ManaCost).Value, deck = f.DeckCount(player);
                    f.Event(delegate {
                        PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12);
                        if (kind == OriginalRemovalUtilityRules.Kind.DestroyWoundedEnemySpell || kind == OriginalRemovalUtilityRules.Kind.DestroyWoundedSummon)
                            enemy.ModifyCurrentHealth(-1, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true);
                    });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    ServerCard target = kind == OriginalRemovalUtilityRules.Kind.OpponentDamageDraw || kind == OriginalRemovalUtilityRules.Kind.LastGaspDraw
                        || kind == OriginalRemovalUtilityRules.Kind.LaneDamageSummon ? null
                        : kind == OriginalRemovalUtilityRules.Kind.CoverSummon ? ally : kind == OriginalRemovalUtilityRules.Kind.DestroyLowPowerSpell ? small : enemy;
                    f.Play(source, target, player);
                    bool effect;
                    switch (kind)
                    {
                        case OriginalRemovalUtilityRules.Kind.DamageCreatureSpell: effect = a >= 8 ? enemy.CurrentGameZoneName != GameZoneName.CreatureBoard : enemy.CurrentHealth == 8 - a; break;
                        case OriginalRemovalUtilityRules.Kind.ShackleSpell: effect = enemy.QueryAttribute(AttributeType.Freeze).Value > 0; break;
                        case OriginalRemovalUtilityRules.Kind.OpponentDamageDraw: effect = enemyAvatar.CurrentHealth == 30 - a && f.DeckCount(player) == deck - 1; break;
                        case OriginalRemovalUtilityRules.Kind.DestroyLowPowerSpell: effect = small.CurrentGameZoneName != GameZoneName.CreatureBoard && enemy.CurrentHealth == 8; break;
                        case OriginalRemovalUtilityRules.Kind.CoverSummon: effect = ally.QueryAttribute(AttributeType.Cover).Value > 0; break;
                        case OriginalRemovalUtilityRules.Kind.LastGaspDraw: effect = f.DeckCount(player) == deck; break;
                        case OriginalRemovalUtilityRules.Kind.LaneDamageSummon: effect = enemy.CurrentHealth == 8 - a && small.CurrentHealth == 1; break; // lane 1 untouched
                        default: effect = enemy.CurrentGameZoneName != GameZoneName.CreatureBoard && small.CurrentHealth == 1; break;
                    }
                    bool creature = kind == OriginalRemovalUtilityRules.Kind.DestroyWoundedSummon || kind == OriginalRemovalUtilityRules.Kind.CoverSummon
                        || kind == OriginalRemovalUtilityRules.Kind.LastGaspDraw || kind == OriginalRemovalUtilityRules.Kind.LaneDamageSummon;
                    Require(effect && s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - cost && source.QueryGameJournalCount(CardJournal.Played) == 1
                        && source.CurrentGameZoneName == (creature ? GameZoneName.CreatureBoard : GameZoneName.Cemetery) && ally.CurrentHealth == 8,
                        "Original paid removal/utility differs: " + id + " enemy=" + enemy.CurrentHealth + "/" + enemy.CurrentGameZoneName + " small=" + small.CurrentHealth
                        + " src=" + source.CurrentGameZoneName + " mana=" + s.Players[player].QueryAttribute(AttributeType.Mana).Value + " deck=" + f.DeckCount(player) + "/" + deck);
                }
            });

            test("original-last-gasp-courier-native-death-draw", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard courier = f.Create(Courier, player, GameZoneName.CreatureBoard);
                    ServerCard rage = f.Create(Rage, player, GameZoneName.Hand);
                    int deck = f.DeckCount(player), enemyDeck = f.DeckCount(1 - player), hand = f.HandCount(player);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(rage, courier, player);
                    Require(f.BoardCount(player) == 0 && f.CemeteryHas(player, Courier) && f.DeckCount(player) == deck - 1 && f.HandCount(player) == hand
                        && f.DeckCount(1 - player) == enemyDeck, "Original Last Gasp death draw differs: deck=" + f.DeckCount(player) + "/" + deck + " hand=" + f.HandCount(player) + "/" + hand);
                }
            });

            test("original-removal-utility-prophecy-free-play-and-parent-resumption", delegate {
                int exercised = 0;
                foreach (Card card in OriginalRemovalUtilityRules.Validate(config))
                {
                    if (!OriginalTargetedSummonTests.Prophecy(card)) continue; exercised++;
                    int id = card.ResourceId.Id, a = OriginalRemovalUtilityRules.Value(id);
                    OriginalRemovalUtilityRules.Kind kind = K(id);
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, id))
                    {
                        Simulation s = f.Sim; int parent = s._turnStateStack.Peek().ActivePlayerIndex, player = 1 - parent;
                        ServerCard enemy = f.Create(Leviathan, parent, GameZoneName.CreatureBoard);
                        ServerCard ally = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                        int mana = s.Players[player].QueryAttribute(AttributeType.Mana).Value;
                        ServerCard avatar = f.Avatar(player);
                        f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, s.Players[player], avatar.CurrentHealth - 25, -1); });
                        f.Until(delegate { return s._turnStateStack.Count == 2 && s._turnStateStack.Peek().Phase == TurnPhase.Interrupt
                            && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem != null
                            && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek()); });
                        ServerCard source = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == id; }, s.Players[player].NetId, GameZoneName.Prophecy);
                        Require(source != null, "Removal/utility rune draw did not offer Prophecy: " + id);
                        ServerCard target = kind == OriginalRemovalUtilityRules.Kind.LaneDamageSummon ? null : kind == OriginalRemovalUtilityRules.Kind.CoverSummon ? ally : enemy;
                        f.Play(source, target, player);
                        bool effect = kind == OriginalRemovalUtilityRules.Kind.CoverSummon ? ally.QueryAttribute(AttributeType.Cover).Value > 0 : enemy.CurrentHealth == 8 - a;
                        Require(effect && source.QueryGameJournalCount(CardJournal.PlayedAsProphecy) == 1 && s.Players[player].QueryAttribute(AttributeType.Mana).Value == mana
                            && s._turnStateStack.Count == 1 && s._turnStateStack.Peek().ActivePlayerIndex == parent,
                            "Original free removal/utility effect/mana/parent resumption differs: " + id + " enemy=" + enemy.CurrentHealth);
                    }
                }
                Require(exercised == 3, "Removal/utility Prophecy inventory differs: " + exercised);
            });

            test("strategic-original-removal-execute-enemy-cover-ally", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 1);
                    ServerCard execute = OriginalTargetedSummonTests.Hand(f, Execute);
                    ServerCard ally = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 1, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == execute.NetUid.Id && choice.TargetCardNetUid == enemy.NetUid.Id,
                        "Original Execute strategy did not destroy the enemy creature.");
                    Require(!enemy.IsDying() && enemy.CurrentGameZoneName == GameZoneName.CreatureBoard, "Execute ranking mutated native state.");
                }
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 0);
                    ServerCard footpad = OriginalTargetedSummonTests.Hand(f, Footpad);
                    ServerCard ally = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == footpad.NetUid.Id && choice.TargetCardNetUid == ally.NetUid.Id,
                        "Original Cover Summon strategy did not cover its own creature.");
                }
            });
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
