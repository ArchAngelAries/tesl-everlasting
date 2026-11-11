using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalVeteranRandomTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069, Rage = 1055804545, Ashlander = -938950495, Haunting = -822105628, Naarifin = 148278048, Prodigy = 670172603;
        private static OriginalVeteranRandomRules.Kind K(int id) { return OriginalVeteranRandomRules.KindOf(id); }

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-veteran-random-15-exact-atomic-metadata-owned-graph", delegate {
                List<Card> cards = OriginalVeteranRandomRules.Validate(config);
                Require(cards.Count == 15, "Original Veteran/random inventory differs.");
                object before = cards[0].TriggeredEffects; string text = cards[14].Description;
                try {
                    cards[14].Description += " Extra effect."; bool rejected = false;
                    try { OriginalVeteranRandomRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Veteran/random atomic preflight failed.");
                } finally { cards[14].Description = text; }
                OriginalVeteranRandomRules.Restore(config); before = cards[0].TriggeredEffects; OriginalVeteranRandomRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].TriggeredEffects), "Veteran/random graph identity changed.");
                CardListSort sort = config.FindCardById(Ashlander).TriggeredEffects[0].EffectGroup.Effects[0].CardActions[0].SelectorSort;
                try {
                    sort.NumToReturn = new ActionValueData(2); bool rejected = false;
                    try { OriginalVeteranRandomRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed random target count admitted.");
                } finally { sort.NumToReturn = new ActionValueData(1); }
                foreach (Card card in cards) Require(RuleImporter.IsSupported(card.ResourceId.Id), "Veteran/random ID not supported: " + card.ResourceId.Id);
            });

            test("original-veteran-real-first-surviving-attack-only", delegate {
                foreach (Card card in OriginalVeteranRandomRules.Validate(config))
                {
                    int id = card.ResourceId.Id; if (OriginalVeteranRandomRules.TriggerOf(id) != EffectTriggerType.Veteran) continue;
                    int a = OriginalVeteranRandomRules.ValueA(id), b = OriginalVeteranRandomRules.ValueB(id);
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard source = f.Create(id, player, GameZoneName.CreatureBoard);
                        ServerCard enemyAvatar = f.Avatar(1 - player);
                        int attack = source.QueryAttribute(AttributeType.Attack).Value, health = source.CurrentHealth;
                        for (int round = 0; round < 2; round++)
                        {
                            f.Event(delegate {
                                ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                                source.OnEndTurn(s.Players[player].NetId, gs); source.OnStartTurn(s.Players[player].NetId, gs);
                            });
                            s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                            f.Attack(source, enemyAvatar, player);
                            bool ok = K(id) == OriginalVeteranRandomRules.Kind.VeteranSelfGuard
                                ? source.QueryAttribute(AttributeType.Taunt).Value > 0
                                : source.QueryAttribute(AttributeType.Attack).Value == attack + a && source.CurrentHealth == health + b;
                            Require(ok && source.QueryGameJournalCount(CardJournal.Attacked) == round + 1,
                                "Original Veteran differs after attack " + (round + 1) + ": " + id + " attack=" + source.QueryAttribute(AttributeType.Attack).Value + "/" + attack
                                + " health=" + source.CurrentHealth + "/" + health + " attacked=" + source.QueryGameJournalCount(CardJournal.Attacked));
                        }
                    }
                }
                // A Veteran creature that dies in its first attack does not trigger.
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard prodigy = f.Create(Prodigy, player, GameZoneName.CreatureBoard);
                    ServerCard guard = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                    f.Event(delegate { prodigy.OnStartTurn(s.Players[player].NetId, DecisionRestoration.State(s, s._turnStateStack.Peek())); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Attack(prodigy, guard, player);
                    Require(prodigy.CurrentGameZoneName != GameZoneName.CreatureBoard && guard.CurrentHealth == 7 && prodigy.QueryAttribute(AttributeType.Attack).Value == 1,
                        "Dying Veteran creature triggered or combat differs.");
                }
            });

            test("original-random-native-selector-sort-picks-exact-count-and-side", delegate {
                foreach (Card card in OriginalVeteranRandomRules.Validate(config))
                {
                    int id = card.ResourceId.Id; OriginalVeteranRandomRules.Kind kind = K(id);
                    if (kind == OriginalVeteranRandomRules.Kind.VeteranSelfBuff || kind == OriginalVeteranRandomRules.Kind.VeteranSelfGuard) continue;
                    int a = OriginalVeteranRandomRules.ValueA(id), n = OriginalVeteranRandomRules.ValueB(id);
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    // Last Gasp sources resolve from Deathrattle, so keep them off the board (a board source could pick itself).
                    bool offBoard = kind == OriginalVeteranRandomRules.Kind.RandomEnemyDestroy || OriginalVeteranRandomRules.TriggerOf(id) == EffectTriggerType.LastGaspTrigger;
                    ServerCard source = f.CreateId(id, offBoard ? GameZoneName.Hand : GameZoneName.CreatureBoard, 0, f.One);
                    List<ServerCard> enemies = new List<ServerCard>(), allies = new List<ServerCard>();
                    for (int i = 0; i < 4; i++) enemies.Add(f.CreateId(Leviathan, GameZoneName.CreatureBoard, i % 2, f.Two));
                    for (int i = 0; i < 2; i++) allies.Add(f.CreateId(Leviathan, GameZoneName.CreatureBoard, i % 2, f.One));
                    if (kind == OriginalVeteranRandomRules.Kind.RandomEnemyDestroy) f.PlayEffects(source, null);
                    else
                    {
                        Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(OriginalVeteranRandomRules.TriggerOf(id), source, f.Players)),
                            "Random trigger not gathered: " + id);
                        f.DrainTriggers();
                    }
                    int hitEnemies = 0, hitAllies = 0;
                    foreach (ServerCard c in enemies) if (Hit(c, kind, a)) hitEnemies++;
                    foreach (ServerCard c in allies) if (Hit(c, kind, a)) hitAllies++;
                    bool avatarHit = f.AvatarTwo.CurrentHealth != 30;
                    switch (kind)
                    {
                        case OriginalVeteranRandomRules.Kind.RandomEnemyDamage:
                            Require(hitEnemies + (avatarHit ? 1 : 0) == 1 && hitAllies == 0 && f.AvatarOne.CurrentHealth == 30
                                && (!avatarHit || f.AvatarTwo.CurrentHealth == 30 - a), "Random enemy damage count/side differs: " + id); break;
                        case OriginalVeteranRandomRules.Kind.RandomFriendlyBuff:
                            Require(hitAllies == 1 && hitEnemies == 0, "Random friendly buff count/side differs: " + id); break;
                        default:
                            Require(hitEnemies == n && hitAllies == 0 && !avatarHit, "Random enemy creature effect count/side differs: " + id + " hit=" + hitEnemies); break;
                    }
                }
                // With fewer matches than the count, native SortList returns all of them.
                EngineTests.Fixture g = new EngineTests.Fixture(config);
                ServerCard power = g.CreateId(Naarifin, GameZoneName.Hand, 0, g.One);
                ServerCard e1 = g.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, g.Two), e2 = g.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, g.Two);
                g.PlayEffects(power, null);
                Require(Gone(e1) && Gone(e2), "Power of Naarifin did not destroy every enemy when fewer than three exist.");
            });

            test("original-random-real-death-and-paid-play", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard ashlander = f.Create(Ashlander, player, GameZoneName.CreatureBoard);
                    ServerCard haunting = f.Create(Haunting, player, GameZoneName.CreatureBoard);
                    ServerCard ally = f.CreateIn(Leviathan, player, GameZoneName.CreatureBoard, 1);
                    ServerCard rage = f.Create(Rage, player, GameZoneName.Hand), rage2 = f.Create(Rage, player, GameZoneName.Hand);
                    ServerCard enemyAvatar = f.Avatar(1 - player);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(rage, ashlander, player);
                    Require(enemyAvatar.CurrentHealth == 27, "Brutal Ashlander real Last Gasp did not hit the only enemy (opponent).");
                    f.Play(rage2, haunting, player);
                    Require(ally.QueryAttribute(AttributeType.Attack).Value == 11 && ally.CurrentHealth == 11, "Haunting Spirit real Last Gasp did not buff the only friendly creature.");
                }
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard power = f.Create(Naarifin, player, GameZoneName.Hand);
                    List<ServerCard> enemies = new List<ServerCard>();
                    for (int i = 0; i < 4; i++) enemies.Add(f.CreateIn(Mudcrab, 1 - player, GameZoneName.CreatureBoard, i % 2));
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(power, null, player);
                    Require(f.BoardCount(1 - player) == 1 && power.CurrentGameZoneName == GameZoneName.Cemetery
                        && s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - power.QueryAttribute(AttributeType.ManaCost).Value,
                        "Power of Naarifin paid play did not destroy exactly three of four enemies.");
                }
            });

            test("strategic-original-veteran-pilfer-trigger-valuation", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    // Black Marsh Prodigy (1/1, Veteran +4/+4) should attack the undefended opponent (survives, triggers
                    // Veteran) rather than trade into a 2/1 where it dies without its Veteran reward.
                    ServerCard prodigy = f.Create("Black Marsh Prodigy", GameZoneName.CreatureBoard, 0, f.One);
                    prodigy.OnStartTurn(f.One.NetId, f.State);
                    ServerCard crab = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 0, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Attack && choice.ActorCardNetUid == prodigy.NetUid.Id && choice.TargetCardNetUid == f.AvatarTwo.NetUid.Id,
                        "AI did not take the surviving Veteran attack.");
                    Require(prodigy.QueryAttribute(AttributeType.Attack).Value == 1 && crab.CurrentHealth == 1, "Veteran ranking mutated native state.");
                }
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 6);
                    ServerCard power = OriginalTargetedSummonTests.Hand(f, Naarifin);
                    for (int i = 0; i < 3; i++) f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, i % 2, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == power.NetUid.Id, "AI did not play Power of Naarifin into three enemies.");
                }
            });
        }

        private static bool Gone(ServerCard c) { return c.IsDying() || c.CurrentGameZoneName != GameZoneName.CreatureBoard; }
        private static bool Hit(ServerCard c, OriginalVeteranRandomRules.Kind kind, int a)
        {
            switch (kind)
            {
                case OriginalVeteranRandomRules.Kind.RandomEnemyDamage: return c.CurrentHealth == 8 - a;
                case OriginalVeteranRandomRules.Kind.RandomFriendlyBuff: return c.QueryAttribute(AttributeType.Attack).Value == 8 + a;
                case OriginalVeteranRandomRules.Kind.RandomEnemyShackle: return c.QueryAttribute(AttributeType.Freeze).Value > 0;
                default: return Gone(c);
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
