using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalMagickaConditionTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069;
        private const int Minder = 1262508063, Jyggalag = -1641639169, Wizard = 458738242, Crusader = -212193127;
        private static OriginalMagickaConditionRules.Kind K(int id) { return OriginalMagickaConditionRules.KindOf(id); }

        // A supported original creature carrying (or lacking) the given attribute subtype.
        private static int CreatureWith(GameConfiguration config, CardSubtype attribute, bool has)
        {
            foreach (CardSet set in config.CardCollection) foreach (Card card in set.Cards)
            {
                int id = card.ResourceId.Id;
                if (!RuleImporter.IsSupported(id) || !OriginalCardRules.Contains(id)) continue;
                CardDefinition d = GameManager.Instance.LookUpCardDefinition(id);
                if (d == null || !d.Subtypes.Contains(CardSubtype.Creature) || card.Subtypes.Contains(attribute) != has) continue;
                return id;
            }
            throw new InvalidOperationException("No supported original creature fixture for attribute " + attribute + " has=" + has);
        }
        private static int MaxMana(ServerCard avatar) { return avatar.QueryAttribute(AttributeType.MaxMana).Value; }

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-magicka-condition-11-exact-atomic-metadata-owned-graph", delegate {
                List<Card> cards = OriginalMagickaConditionRules.Validate(config);
                Require(cards.Count == 11 && OriginalMagickaConditionRules.Count == 11, "Original magicka/condition inventory differs.");
                object before = cards[0].TriggeredEffects; string text = cards[10].Description;
                try {
                    cards[10].Description += " Extra effect."; bool rejected = false;
                    try { OriginalMagickaConditionRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Magicka/condition atomic preflight failed.");
                } finally { cards[10].Description = text; }
                OriginalMagickaConditionRules.Restore(config); before = cards[0].TriggeredEffects;
                OriginalMagickaConditionRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Magicka/condition graph identity changed.");
                PlayerRequirements.PlayerCountOfCardsRequirement count = (PlayerRequirements.PlayerCountOfCardsRequirement)config.FindCardById(Wizard).TriggeredEffects[0].EffectGroup.Effects[0].ExecutePlayerRequirements[0];
                try {
                    count.Conditional.Value = 1; bool rejected = false;
                    try { OriginalMagickaConditionRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed attribute-pair count admitted.");
                } finally { count.Conditional.Value = 2; }
                PlayerRequirements.PlayerAttributeValueRequirement runes = (PlayerRequirements.PlayerAttributeValueRequirement)config.FindCardById(Crusader).TriggeredEffects[0].EffectGroup.Effects[0].ExecutePlayerRequirements[0];
                try {
                    runes.LhsPlayer = PlayerRole.Self; bool rejected = false;
                    try { OriginalMagickaConditionRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed rune requirement player admitted.");
                } finally { runes.LhsPlayer = PlayerRole.Opponent; }
                foreach (Card card in cards) Require(RuleImporter.IsSupported(card.ResourceId.Id), "Magicka/condition ID not supported: " + card.ResourceId.Id);
            });

            test("original-max-magicka-native-avatar-buff-double-gain-and-no-refill", delegate {
                foreach (Card card in OriginalMagickaConditionRules.Validate(config))
                {
                    int id = card.ResourceId.Id, a = OriginalMagickaConditionRules.Value(id);
                    if (K(id) == OriginalMagickaConditionRules.Kind.MaxMagickaSummon)
                    {
                        EngineTests.Fixture f = new EngineTests.Fixture(config);
                        ServerCard source = f.CreateId(id, GameZoneName.CreatureBoard, 0, f.One);
                        int one = MaxMana(f.AvatarOne), two = MaxMana(f.AvatarTwo), mana = f.One.QueryAttribute(AttributeType.Mana).Value;
                        Summon(f, source);
                        Require(MaxMana(f.AvatarOne) == one + a && MaxMana(f.AvatarTwo) == two && f.One.QueryAttribute(AttributeType.Mana).Value == mana,
                            "Original max magicka Summon gain/owner/no-refill differs: " + id + " max=" + MaxMana(f.AvatarOne) + "/" + one);
                        f.AvatarOne.SetAttribute(AttributeType.DoubleMaxManaGained, 1);
                        Summon(f, source);
                        Require(MaxMana(f.AvatarOne) == one + a + 2 * a, "Native DoubleMaxManaGained not applied: " + id + " max=" + MaxMana(f.AvatarOne));
                    }
                    else if (K(id) == OriginalMagickaConditionRules.Kind.EachPlayerMaxMagicka)
                    {
                        EngineTests.Fixture f = new EngineTests.Fixture(config);
                        ServerCard source = f.CreateId(id, GameZoneName.Hand, 0, f.One);
                        int one = MaxMana(f.AvatarOne), two = MaxMana(f.AvatarTwo);
                        f.PlayEffects(source, null);
                        Require(MaxMana(f.AvatarOne) == one + a && MaxMana(f.AvatarTwo) == two + a, "Original each-player max magicka differs: " + id);
                    }
                }
            });

            test("original-conditional-draw-attribute-pair-and-opponent-rune", delegate {
                foreach (Card card in OriginalMagickaConditionRules.Validate(config))
                {
                    int id = card.ResourceId.Id;
                    if (K(id) == OriginalMagickaConditionRules.Kind.AttributePairDraw)
                    {
                        CardSubtype attribute = OriginalMagickaConditionRules.AttributeOf(id);
                        int same = CreatureWith(config, attribute, true), other = CreatureWith(config, attribute, false);
                        EngineTests.Fixture f = new EngineTests.Fixture(config);
                        for (int n = 0; n < 3; n++) f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One);
                        ServerCard source = f.CreateId(id, GameZoneName.CreatureBoard, 0, f.One);
                        f.CreateId(same, GameZoneName.CreatureBoard, 0, f.One);
                        f.CreateId(other, GameZoneName.CreatureBoard, 1, f.One);
                        f.CreateId(other, GameZoneName.CreatureBoard, 1, f.One);
                        f.CreateId(same, GameZoneName.CreatureBoard, 1, f.Two);
                        f.CreateId(same, GameZoneName.CreatureBoard, 1, f.Two);
                        int hand = Hand(f);
                        Summon(f, source);
                        Require(Hand(f) == hand, "Attribute-pair draw counted itself, other attributes or enemy creatures: " + id);
                        f.CreateId(same, GameZoneName.CreatureBoard, 1, f.One);
                        Summon(f, source);
                        Require(Hand(f) == hand + 1 && f.Catalog.FindCardCount(GameZoneName.Hand, f.Two.NetId) == 0, "Attribute-pair draw with two other creatures differs: " + id);
                    }
                    else if (K(id) == OriginalMagickaConditionRules.Kind.OpponentRuneDraw)
                    {
                        EngineTests.Fixture f = new EngineTests.Fixture(config);
                        for (int n = 0; n < 3; n++) f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One);
                        ServerCard source = f.CreateId(id, GameZoneName.CreatureBoard, 0, f.One);
                        f.Two.SetAttribute(AttributeType.NumRunesLost, 0); f.One.SetAttribute(AttributeType.NumRunesLost, 2);
                        int hand = Hand(f);
                        Summon(f, source);
                        Require(Hand(f) == hand, "Rune draw used own destroyed runes or intact opponent: " + id);
                        f.Two.SetAttribute(AttributeType.NumRunesLost, 1);
                        Summon(f, source);
                        Require(Hand(f) == hand + 1, "Rune draw with opponent destroyed rune differs: " + id);
                    }
                }
            });

            test("original-magicka-condition-paid-native-plays-and-turn-start-magicka", delegate {
                foreach (Card card in OriginalMagickaConditionRules.Validate(config))
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int id = card.ResourceId.Id, a = OriginalMagickaConditionRules.Value(id);
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source = f.Create(id, player, GameZoneName.Hand);
                    ServerCard avatar = f.Avatar(player), enemyAvatar = f.Avatar(1 - player);
                    if (K(id) == OriginalMagickaConditionRules.Kind.AttributePairDraw)
                    {
                        int same = CreatureWith(config, OriginalMagickaConditionRules.AttributeOf(id), true);
                        f.Create(same, player, GameZoneName.CreatureBoard); f.CreateIn(same, player, GameZoneName.CreatureBoard, 1);
                    }
                    int cost = source.QueryAttribute(AttributeType.ManaCost).Value, deck = f.DeckCount(player);
                    int max = MaxMana(avatar), enemyMax = MaxMana(enemyAvatar);
                    f.Event(delegate {
                        PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12);
                        if (K(id) == OriginalMagickaConditionRules.Kind.OpponentRuneDraw) s.Players[1 - player].SetAttribute(AttributeType.NumRunesLost, 1);
                    });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(source, null, player);
                    bool effect;
                    switch (K(id))
                    {
                        case OriginalMagickaConditionRules.Kind.MaxMagickaSummon:
                            effect = MaxMana(avatar) == max + a && MaxMana(enemyAvatar) == enemyMax && f.DeckCount(player) == deck;
                            // Native turn-start magicka sets current magicka from MaxMana, including the bonus.
                            f.Event(delegate { DecisionRestoration.RestoreStartTurnMagicka(s, s._turnStateStack.Peek(), s.Players[player]); });
                            effect = effect && s.Players[player].QueryAttribute(AttributeType.Mana).Value == MaxMana(avatar) && MaxMana(avatar) >= max + a;
                            break;
                        case OriginalMagickaConditionRules.Kind.EachPlayerMaxMagicka:
                            effect = MaxMana(avatar) == max + a && MaxMana(enemyAvatar) == enemyMax + a; break;
                        default: effect = f.DeckCount(player) == deck - 1; break;
                    }
                    bool creature = K(id) != OriginalMagickaConditionRules.Kind.EachPlayerMaxMagicka;
                    Require(effect && source.QueryGameJournalCount(CardJournal.Played) == 1
                        && source.CurrentGameZoneName == (creature ? GameZoneName.CreatureBoard : GameZoneName.Cemetery)
                        && (K(id) == OriginalMagickaConditionRules.Kind.MaxMagickaSummon || s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - cost),
                        "Original paid magicka/condition differs: " + id + " max=" + MaxMana(avatar) + "/" + max + " mana=" + s.Players[player].QueryAttribute(AttributeType.Mana).Value
                        + " deck=" + f.DeckCount(player) + "/" + deck);
                }
            });

            test("original-jyggalag-prophecy-free-play-and-parent-resumption", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Jyggalag))
                {
                    Simulation s = f.Sim; int parent = s._turnStateStack.Peek().ActivePlayerIndex, player = 1 - parent;
                    ServerCard avatar = f.Avatar(player), enemyAvatar = f.Avatar(parent);
                    int mana = s.Players[player].QueryAttribute(AttributeType.Mana).Value, max = MaxMana(avatar), enemyMax = MaxMana(enemyAvatar);
                    f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, s.Players[player], avatar.CurrentHealth - 25, -1); });
                    f.Until(delegate { return s._turnStateStack.Count == 2 && s._turnStateStack.Peek().Phase == TurnPhase.Interrupt
                        && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem != null
                        && !RuneProphecyRestoration.HasQueuedEffects(s._turnStateStack.Peek()); });
                    ServerCard source = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == Jyggalag; }, s.Players[player].NetId, GameZoneName.Prophecy);
                    Require(source != null, "Jyggalag's Incursion rune draw did not offer Prophecy.");
                    f.Play(source, null, player);
                    Require(MaxMana(avatar) == max + 2 && MaxMana(enemyAvatar) == enemyMax + 2 && source.QueryGameJournalCount(CardJournal.PlayedAsProphecy) == 1
                        && s.Players[player].QueryAttribute(AttributeType.Mana).Value == mana && s._turnStateStack.Count == 1 && s._turnStateStack.Peek().ActivePlayerIndex == parent,
                        "Original Jyggalag's Incursion Prophecy max magicka/resumption differs.");
                }
            });

            test("strategic-original-tree-minder-ramp-is-played", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 3);
                    ServerCard minder = OriginalTargetedSummonTests.Hand(f, Minder);
                    int max = MaxMana(f.AvatarOne);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == minder.NetUid.Id, "AI did not play Tree Minder.");
                    Require(MaxMana(f.AvatarOne) == max, "Max magicka ranking mutated native state.");
                }
            });
        }
        private static int Hand(EngineTests.Fixture f) { return f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId); }
        private static void Summon(EngineTests.Fixture f, ServerCard source)
        {
            Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, source, f.Players)), "Summon trigger not gathered: " + source.CardId);
            f.DrainTriggers();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
