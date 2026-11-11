using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalKeywordTriggerTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069, Rage = 1055804545, Rajhini = -756429718, Archein = 1153555094;
        private static OriginalKeywordTriggerRules.Kind K(int id) { return OriginalKeywordTriggerRules.KindOf(id); }
        private static EffectTriggerType T(int id) { return OriginalKeywordTriggerRules.TriggerOf(id); }

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-keyword-trigger-19-exact-atomic-metadata-owned-graph", delegate {
                List<Card> cards = OriginalKeywordTriggerRules.Validate(config);
                Require(cards.Count == 19 && OriginalKeywordTriggerRules.Count == 19, "Original keyword-trigger inventory differs.");
                object before = cards[0].TriggeredEffects; string text = cards[18].Description;
                try {
                    cards[18].Description += " Extra effect."; bool rejected = false;
                    try { OriginalKeywordTriggerRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Keyword-trigger atomic preflight failed.");
                } finally { cards[18].Description = text; }
                OriginalKeywordTriggerRules.Restore(config); before = cards[0].TriggeredEffects;
                OriginalKeywordTriggerRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Keyword-trigger graph identity changed.");
                TriggeredEffect pilfer = config.FindCardById(Rajhini).TriggeredEffects[0];
                try {
                    pilfer.TriggerType = EffectTriggerType.Slay; bool rejected = false;
                    try { OriginalKeywordTriggerRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed Pilfer trigger type admitted.");
                } finally { pilfer.TriggerType = EffectTriggerType.Pilfer; }
                foreach (Card card in cards) Require(RuleImporter.IsSupported(card.ResourceId.Id), "Keyword-trigger ID not supported: " + card.ResourceId.Id);
            });

            test("original-keyword-trigger-native-effects", delegate {
                foreach (Card card in OriginalKeywordTriggerRules.Validate(config))
                {
                    int id = card.ResourceId.Id, a = OriginalKeywordTriggerRules.ValueA(id), b = OriginalKeywordTriggerRules.ValueB(id);
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    for (int n = 0; n < 3; n++) f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One);
                    ServerCard source = f.CreateId(id, GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemyNear = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard enemyFar = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, f.Two);
                    ServerCard allyNear = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.One);
                    f.AvatarOne.SetAvatarHealthPreGame(20);
                    if (K(id) == OriginalKeywordTriggerRules.Kind.MaxMagickaDraw) f.AvatarOne.AddMaxManaBuff(f.State, a);
                    f.AvatarOne.RecalculateEnchantments();
                    int hand = Hand(f), mana = f.One.QueryAttribute(AttributeType.Mana).Value, max = f.AvatarOne.QueryAttribute(AttributeType.MaxMana).Value;
                    int attack = source.QueryAttribute(AttributeType.Attack).Value, health = source.CurrentHealth;
                    ServerEffectUtilities.TriggerEvent evt = T(id) == EffectTriggerType.Slay ? new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Slay, source, enemyFar, f.Players)
                        : T(id) == EffectTriggerType.Pilfer ? new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Pilfer, source, null, f.Players, 1)
                        : new ServerEffectUtilities.TriggerEvent(EffectTriggerType.LastGaspTrigger, source, f.Players);
                    Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, evt), "Keyword trigger not gathered: " + id);
                    f.DrainTriggers();
                    bool ok;
                    switch (K(id))
                    {
                        case OriginalKeywordTriggerRules.Kind.OpponentDamageHeal: ok = f.AvatarTwo.CurrentHealth == 30 - a && f.AvatarOne.CurrentHealth == 20 + b; break;
                        case OriginalKeywordTriggerRules.Kind.Heal: ok = f.AvatarOne.CurrentHealth == 20 + a && f.AvatarTwo.CurrentHealth == 30; break;
                        case OriginalKeywordTriggerRules.Kind.OpponentDamage: ok = f.AvatarTwo.CurrentHealth == 30 - a && f.AvatarOne.CurrentHealth == 20; break;
                        case OriginalKeywordTriggerRules.Kind.GainMagicka: ok = f.One.QueryAttribute(AttributeType.Mana).Value == mana + a && f.Two.QueryAttribute(AttributeType.Mana).Value == 20; break;
                        case OriginalKeywordTriggerRules.Kind.LaneEnemyDamage: ok = enemyNear.CurrentHealth == 8 - a && enemyFar.CurrentHealth == 8 && allyNear.CurrentHealth == 8; break;
                        case OriginalKeywordTriggerRules.Kind.LaneAllDamage: ok = enemyNear.CurrentHealth == 8 - a && allyNear.CurrentHealth == 8 - a && enemyFar.CurrentHealth == 8; break;
                        case OriginalKeywordTriggerRules.Kind.MaxMagickaDraw: ok = Hand(f) == hand + 1 && max >= a; break;
                        case OriginalKeywordTriggerRules.Kind.Draw: ok = Hand(f) == hand + 1 && f.Catalog.FindCardCount(GameZoneName.Hand, f.Two.NetId) == 0; break;
                        case OriginalKeywordTriggerRules.Kind.SelfBuff: ok = source.QueryAttribute(AttributeType.Attack).Value == attack + a && source.CurrentHealth == health + b && allyNear.QueryAttribute(AttributeType.Attack).Value == 8; break;
                        default: ok = f.AvatarOne.QueryAttribute(AttributeType.MaxMana).Value == max + a && f.AvatarTwo.QueryAttribute(AttributeType.MaxMana).Value == f.AvatarTwo.QueryAttribute(AttributeType.MaxMana).Value; break;
                    }
                    Require(ok, "Original keyword-trigger native effect differs: " + id);
                }
                // Sly Marshblade below 7 max magicka does not draw.
                EngineTests.Fixture g = new EngineTests.Fixture(config);
                g.CreateId(Mudcrab, GameZoneName.Deck, 0, g.One);
                ServerCard sly = g.CreateId(85837010, GameZoneName.CreatureBoard, 0, g.One);
                int before = Hand(g);
                ServerEffectUtilities.TriggerDirectEffectEvent(g.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.LastGaspTrigger, sly, g.Players));
                g.DrainTriggers();
                Require(Hand(g) == before, "Sly Marshblade drew below 7 max magicka.");
            });

            test("original-last-gasp-real-native-death-effects", delegate {
                foreach (Card card in OriginalKeywordTriggerRules.Validate(config))
                {
                    int id = card.ResourceId.Id; if (T(id) != EffectTriggerType.LastGaspTrigger) continue;
                    int a = OriginalKeywordTriggerRules.ValueA(id), b = OriginalKeywordTriggerRules.ValueB(id);
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard source = f.Create(id, player, GameZoneName.CreatureBoard);
                        ServerCard allyNear = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                        ServerCard enemyNear = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                        ServerCard enemyFar = f.CreateIn(Leviathan, 1 - player, GameZoneName.CreatureBoard, 1);
                        ServerCard rage = f.Create(Rage, player, GameZoneName.Hand);
                        ServerCard avatar = f.Avatar(player), enemyAvatar = f.Avatar(1 - player);
                        int deck = f.DeckCount(player);
                        f.Event(delegate {
                            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                            PlayerActions.SetMana.StaticExecute(gs, s.Players[player], 12);
                            avatar.SetAvatarHealthPreGame(29);
                            if (K(id) == OriginalKeywordTriggerRules.Kind.MaxMagickaDraw) { avatar.AddMaxManaBuff(gs, 7); avatar.RecalculateEnchantmentsWithTriggers(gs, null); }
                        });
                        s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                        int rageCost = rage.QueryAttribute(AttributeType.ManaCost).Value;
                        f.Play(rage, source, player);
                        bool ok;
                        switch (K(id))
                        {
                            case OriginalKeywordTriggerRules.Kind.OpponentDamageHeal: ok = enemyAvatar.CurrentHealth == 30 - a && avatar.CurrentHealth == 29 + b; break;
                            case OriginalKeywordTriggerRules.Kind.Heal: ok = avatar.CurrentHealth == 29 + a; break;
                            case OriginalKeywordTriggerRules.Kind.OpponentDamage: ok = enemyAvatar.CurrentHealth == 30 - a; break;
                            case OriginalKeywordTriggerRules.Kind.GainMagicka: ok = s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - rageCost + a; break;
                            case OriginalKeywordTriggerRules.Kind.LaneEnemyDamage: ok = enemyNear.CurrentHealth == 8 - a && enemyFar.CurrentHealth == 8 && allyNear.CurrentHealth == 8; break;
                            case OriginalKeywordTriggerRules.Kind.LaneAllDamage: ok = enemyNear.CurrentHealth == 8 - a && allyNear.CurrentHealth == 8 - a && enemyFar.CurrentHealth == 8; break;
                            default: ok = f.DeckCount(player) == deck - 1; break;
                        }
                        Require(ok && f.CemeteryHas(player, id), "Original real Last Gasp effect differs: " + id + " enemyNear=" + enemyNear.CurrentHealth + " allyNear=" + allyNear.CurrentHealth
                            + " avatar=" + avatar.CurrentHealth + " enemyAvatar=" + enemyAvatar.CurrentHealth + " deck=" + f.DeckCount(player) + "/" + deck);
                    }
                }
            });

            test("original-slay-pilfer-real-native-combat", delegate {
                foreach (Card card in OriginalKeywordTriggerRules.Validate(config))
                {
                    int id = card.ResourceId.Id; EffectTriggerType trigger = T(id);
                    if (trigger == EffectTriggerType.LastGaspTrigger) continue;
                    int a = OriginalKeywordTriggerRules.ValueA(id), b = OriginalKeywordTriggerRules.ValueB(id);
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard source = f.Create(id, player, GameZoneName.CreatureBoard);
                        // Every Slay creature destroys a 2/1 and survives its 2-power counterattack.
                        ServerCard victim = trigger == EffectTriggerType.Slay ? f.Create(Mudcrab, 1 - player, GameZoneName.CreatureBoard) : null;
                        ServerCard avatar = f.Avatar(player), enemyAvatar = f.Avatar(1 - player);
                        int deck = f.DeckCount(player), max = avatar.QueryAttribute(AttributeType.MaxMana).Value;
                        int attack = source.QueryAttribute(AttributeType.Attack).Value, health = source.CurrentHealth;
                        f.Event(delegate {
                            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                            PlayerActions.SetMana.StaticExecute(gs, s.Players[player], 5);
                            avatar.SetAvatarHealthPreGame(29);
                            source.OnStartTurn(s.Players[player].NetId, gs);
                        });
                        s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                        f.Attack(source, trigger == EffectTriggerType.Slay ? victim : enemyAvatar, player);
                        int taken = trigger == EffectTriggerType.Slay ? victim.QueryAttribute(AttributeType.Attack).Value : 0;
                        bool ok;
                        switch (K(id))
                        {
                            case OriginalKeywordTriggerRules.Kind.Draw: ok = f.DeckCount(player) == deck - 1; break;
                            case OriginalKeywordTriggerRules.Kind.Heal: ok = avatar.CurrentHealth == 29 + a; break;
                            case OriginalKeywordTriggerRules.Kind.GainMagicka: ok = s.Players[player].QueryAttribute(AttributeType.Mana).Value == 5 + a; break;
                            case OriginalKeywordTriggerRules.Kind.SelfBuff: ok = source.QueryAttribute(AttributeType.Attack).Value == attack + a && source.CurrentHealth == health - taken + b; break;
                            default: ok = avatar.QueryAttribute(AttributeType.MaxMana).Value == max + a; break;
                        }
                        bool hit = trigger == EffectTriggerType.Slay ? victim.CurrentGameZoneName != GameZoneName.CreatureBoard : enemyAvatar.CurrentHealth == 30 - attack;
                        Require(ok && hit, "Original real " + trigger + " effect differs: " + id + " attack=" + source.QueryAttribute(AttributeType.Attack).Value + "/" + attack
                            + " health=" + source.CurrentHealth + "/" + health + " deck=" + f.DeckCount(player) + "/" + deck + " mana=" + s.Players[player].QueryAttribute(AttributeType.Mana).Value
                            + " avatar=" + avatar.CurrentHealth + " enemyAvatar=" + enemyAvatar.CurrentHealth);
                    }
                }
                // Pilfer requires damage to the opponent: attacking a creature does not draw.
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source = f.Create(Rajhini, player, GameZoneName.CreatureBoard);
                    ServerCard victim = f.Create(Mudcrab, 1 - player, GameZoneName.CreatureBoard);
                    int deck = f.DeckCount(player);
                    f.Event(delegate { source.OnStartTurn(s.Players[player].NetId, DecisionRestoration.State(s, s._turnStateStack.Peek())); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Attack(source, victim, player);
                    Require(f.DeckCount(player) == deck && victim.CurrentGameZoneName != GameZoneName.CreatureBoard, "Pilfer fired without opponent damage.");
                }
            });
        }
        private static int Hand(EngineTests.Fixture f) { return f.Catalog.FindCardCount(GameZoneName.Hand, f.One.NetId); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
