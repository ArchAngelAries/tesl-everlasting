using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalSpellBattleTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069; // Swamp Leviathan 8/8, Enraged Mudcrab 2/1.
        private const int Curse = 627783150, Ward = 171706845, Suppress = 1009056840, Potion = 1898861704, Housecarl = 207833797, Zealot = -1314585700, Hunter = -117580191;
        private static OriginalSpellBattleRules.Kind K(int id) { return OriginalSpellBattleRules.KindOf(id); }

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-spell-battle-17-exact-atomic-metadata-owned-graph", delegate {
                List<Card> cards = OriginalSpellBattleRules.Validate(config);
                Require(cards.Count == 17 && OriginalSpellBattleRules.Count == 17, "Original spell/battle inventory differs.");
                object before = cards[0].ActionPlayInformation; string text = cards[16].Description;
                try {
                    cards[16].Description += " Extra effect."; bool rejected = false;
                    try { OriginalSpellBattleRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(before, cards[0].ActionPlayInformation), "Spell/battle atomic preflight failed.");
                } finally { cards[16].Description = text; }
                OriginalSpellBattleRules.Restore(config); before = cards[0].ActionPlayInformation;
                OriginalSpellBattleRules.Restore(config);
                Require(Object.ReferenceEquals(before, cards[0].ActionPlayInformation), "Spell/battle graph identity changed.");
                CardActions.Fight fight = (CardActions.Fight)config.FindCardById(Zealot).ActionPlayInformation[0].EffectGroup.Effects[0].CardActions[0].Action;
                ICardConditionalExpression inciter = fight.InciterSelectors[0];
                try {
                    fight.InciterSelectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.DecisionTarget });
                    bool rejected = false;
                    try { OriginalSpellBattleRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed native Fight instigator admitted.");
                } finally { fight.InciterSelectors.Clear(); fight.InciterSelectors.Add(inciter); }
                CardActions.Heal heal = (CardActions.Heal)config.FindCardById(Potion).ActionPlayInformation[0].EffectGroup.Effects[0].CardActions[0].Action;
                try {
                    heal.FullHeal = true; bool rejected = false;
                    try { OriginalSpellBattleRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed native Heal mode admitted.");
                } finally { heal.FullHeal = false; }
                foreach (Card card in cards) Require(RuleImporter.IsSupported(card.ResourceId.Id), "Spell/battle ID not supported: " + card.ResourceId.Id);
            });

            test("original-spell-battle-native-debuff-ward-silence-heal-drain-guard-battle", delegate {
                foreach (Card card in OriginalSpellBattleRules.Validate(config))
                {
                    int id = card.ResourceId.Id, a = OriginalSpellBattleRules.ValueA(id), b = OriginalSpellBattleRules.ValueB(id);
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    bool creature = K(id) == OriginalSpellBattleRules.Kind.BuffGuardSummon || K(id) == OriginalSpellBattleRules.Kind.BattleSummon;
                    ServerCard source = f.CreateId(id, creature ? GameZoneName.CreatureBoard : GameZoneName.Hand, 0, f.One);
                    ServerCard ally = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, f.One);
                    ServerCard enemy = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.Two);
                    f.AvatarOne.SetAvatarHealthPreGame(20);
                    string fail = "Original spell/battle native effect differs: " + id;
                    switch (K(id))
                    {
                        case OriginalSpellBattleRules.Kind.DebuffSpell:
                            f.PlayEffects(source, enemy);
                            Require(enemy.QueryAttribute(AttributeType.Attack).Value == 8 + a && enemy.CurrentHealth == 8 + b && ally.CurrentHealth == 8, fail); break;
                        case OriginalSpellBattleRules.Kind.WardSpell:
                            f.PlayEffects(source, ally);
                            Require(ally.QueryAttribute(AttributeType.Ward).Value > 0 && enemy.QueryAttribute(AttributeType.Ward).Value == 0, fail); break;
                        case OriginalSpellBattleRules.Kind.SilenceSpell:
                            ServerCard warder = f.CreateId(Ward, GameZoneName.Hand, 0, f.Two);
                            f.PlayEffects(warder, enemy);
                            ServerCard buffer = f.CreateId(Housecarl, GameZoneName.CreatureBoard, 1, f.Two);
                            f.PlayEffects(buffer, enemy);
                            Require(enemy.QueryAttribute(AttributeType.Ward).Value > 0 && enemy.QueryAttribute(AttributeType.Taunt).Value > 0
                                && enemy.QueryAttribute(AttributeType.Attack).Value == 10, "Silence fixture preconditions failed: " + id);
                            f.PlayEffects(source, enemy);
                            Require(enemy.QueryAttribute(AttributeType.Ward).Value == 0 && enemy.QueryAttribute(AttributeType.Taunt).Value == 0
                                && enemy.QueryAttribute(AttributeType.Attack).Value == 8 && enemy.CurrentHealth == 8, fail);
                            ServerCard immune = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, f.Two);
                            f.PlayEffects(warder, immune); immune.SetAttribute(AttributeType.ImmuneToSilence, 1);
                            f.PlayEffects(source, immune);
                            Require(immune.QueryAttribute(AttributeType.Ward).Value > 0, "Native ImmuneToSilence bypassed: " + id); break;
                        case OriginalSpellBattleRules.Kind.HealSpell:
                            f.PlayEffects(source, null);
                            Require(f.AvatarOne.CurrentHealth == 20 + a && f.AvatarTwo.CurrentHealth == 30 && ally.CurrentHealth == 8, fail); break;
                        case OriginalSpellBattleRules.Kind.DrainSpell:
                            f.PlayEffects(source, null);
                            Require(f.AvatarTwo.CurrentHealth == 30 - a && f.AvatarOne.CurrentHealth == 20 + b && enemy.CurrentHealth == 8, fail); break;
                        case OriginalSpellBattleRules.Kind.BuffGuardSummon:
                            f.PlayEffects(source, ally);
                            Require(ally.QueryAttribute(AttributeType.Attack).Value == 8 + a && ally.CurrentHealth == 8 + b
                                && ally.QueryAttribute(AttributeType.Taunt).Value > 0 && enemy.QueryAttribute(AttributeType.Taunt).Value == 0, fail); break;
                        case OriginalSpellBattleRules.Kind.BattleSummon:
                            int power = source.QueryAttribute(AttributeType.Attack).Value, health = source.CurrentHealth;
                            f.PlayEffects(source, enemy);
                            Require(enemy.CurrentHealth == 8 - power && (source.CurrentHealth == health - 8 || source.IsDying()) && ally.CurrentHealth == 8
                                && f.AvatarTwo.CurrentHealth == 30, fail); break;
                    }
                }
            });

            test("original-spell-battle-command-target-legality", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 12);
                    ServerCard curse = OriginalTargetedSummonTests.Hand(f, Curse), zealot = OriginalTargetedSummonTests.Hand(f, Zealot),
                        housecarl = OriginalTargetedSummonTests.Hand(f, Housecarl), potion = OriginalTargetedSummonTests.Hand(f, Potion);
                    CardCommandList empty = f.Commands();
                    Require(OriginalTargetedSummonTests.Command(empty, curse) == null || (OriginalTargetedSummonTests.Command(empty, curse).ValidCardCommandMask & 15) == 0,
                        "Targeted action playable with no creature target.");
                    foreach (ServerCard card in new ServerCard[] { zealot, housecarl })
                    {
                        CardCommandState state = OriginalTargetedSummonTests.Command(empty, card);
                        Require(state != null && (state.ValidCardCommandMask & (int)CardCommand.PlayToPosition) != 0 && state.PlayTargets.Count == 0,
                            "Creature with no Summon target not playable: " + card.CardId);
                    }
                    Require(OriginalTargetedSummonTests.Command(empty, potion) != null && OriginalTargetedSummonTests.Command(empty, potion).PlayTargets.Count == 0,
                        "Untargeted heal action not playable.");
                    ServerCard ally = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    CardCommandList commands = f.Commands();
                    List<int> battle = new List<int>(OriginalTargetedSummonTests.Command(commands, zealot).PlayTargets);
                    List<int> any = new List<int>(OriginalTargetedSummonTests.Command(commands, curse).PlayTargets);
                    Require(battle.Count == 1 && battle[0] == enemy.NetUid.Id, "Battle Summon targets differ from one enemy creature.");
                    Require(any.Count == 2 && any.Contains(ally.NetUid.Id) && any.Contains(enemy.NetUid.Id), "Creature action targets differ.");
                }
            });

            test("original-spell-battle-17-paid-native-plays-cost-target-cemetery-and-single-effect", delegate {
                foreach (Card card in OriginalSpellBattleRules.Validate(config))
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int id = card.ResourceId.Id, a = OriginalSpellBattleRules.ValueA(id), b = OriginalSpellBattleRules.ValueB(id);
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard source = f.Create(id, player, GameZoneName.Hand);
                    ServerCard ally = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                    ServerCard enemy = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                    ServerCard avatar = f.Avatar(player), enemyAvatar = f.Avatar(1 - player);
                    int cost = source.QueryAttribute(AttributeType.ManaCost).Value, power = source.QueryAttribute(AttributeType.Attack).Value;
                    f.Event(delegate {
                        PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12);
                        avatar.SetAvatarHealthPreGame(29);
                    });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    OriginalSpellBattleRules.Kind kind = K(id);
                    ServerCard target = kind == OriginalSpellBattleRules.Kind.HealSpell || kind == OriginalSpellBattleRules.Kind.DrainSpell ? null
                        : kind == OriginalSpellBattleRules.Kind.WardSpell || kind == OriginalSpellBattleRules.Kind.BuffGuardSummon ? ally : enemy;
                    f.Play(source, target, player);
                    bool effect;
                    switch (kind)
                    {
                        case OriginalSpellBattleRules.Kind.DebuffSpell: effect = enemy.QueryAttribute(AttributeType.Attack).Value == 8 + a && enemy.CurrentHealth == 8 + b; break;
                        case OriginalSpellBattleRules.Kind.WardSpell: effect = ally.QueryAttribute(AttributeType.Ward).Value > 0; break;
                        case OriginalSpellBattleRules.Kind.SilenceSpell: effect = enemy.CurrentHealth == 8 && enemy.QueryAttribute(AttributeType.Attack).Value == 8; break;
                        case OriginalSpellBattleRules.Kind.HealSpell: effect = avatar.CurrentHealth == 29 + a && enemyAvatar.CurrentHealth == 30; break;
                        case OriginalSpellBattleRules.Kind.DrainSpell: effect = avatar.CurrentHealth == 29 + b && enemyAvatar.CurrentHealth == 30 - a; break;
                        case OriginalSpellBattleRules.Kind.BuffGuardSummon: effect = ally.QueryAttribute(AttributeType.Attack).Value == 8 + a && ally.CurrentHealth == 8 + b
                            && ally.QueryAttribute(AttributeType.Taunt).Value > 0; break;
                        default: effect = enemy.CurrentHealth == 8 - power && f.BoardCount(player) == 1; break; // instigator dies to the 8-power counterattack
                    }
                    bool creature = kind == OriginalSpellBattleRules.Kind.BuffGuardSummon || kind == OriginalSpellBattleRules.Kind.BattleSummon;
                    // A battle instigator that dies is retired natively to RemoveFromGame and replaced by a new cemetery instance.
                    bool died = kind == OriginalSpellBattleRules.Kind.BattleSummon;
                    if (died) effect = effect && source.CurrentGameZoneName == GameZoneName.RemoveFromGame && f.CemeteryHas(player, id);
                    Require(effect && s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - cost && (died || source.QueryGameJournalCount(CardJournal.Played) == 1)
                        && (creature || source.CurrentGameZoneName == GameZoneName.Cemetery)
                        && (kind == OriginalSpellBattleRules.Kind.BattleSummon || !creature || source.CurrentGameZoneName == GameZoneName.CreatureBoard),
                        "Original paid spell/battle cost/target/single-effect/zone differs: " + id + " enemy=" + enemy.CurrentHealth + " power=" + power
                        + " board=" + f.BoardCount(player) + " src=" + source.CurrentGameZoneName + "/" + source.CurrentHealth + " mana=" + s.Players[player].QueryAttribute(AttributeType.Mana).Value
                        + " cost=" + cost + " played=" + source.QueryGameJournalCount(CardJournal.Played));
                }
            });

            test("original-spell-battle-prophecy-free-play-and-parent-resumption", delegate {
                int exercised = 0;
                foreach (Card card in OriginalSpellBattleRules.Validate(config))
                {
                    if (!OriginalTargetedSummonTests.Prophecy(card)) continue; exercised++;
                    int id = card.ResourceId.Id, a = OriginalSpellBattleRules.ValueA(id), b = OriginalSpellBattleRules.ValueB(id);
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
                        Require(source != null, "Spell/battle rune draw did not offer Prophecy: " + id);
                        int power = source.QueryAttribute(AttributeType.Attack).Value;
                        OriginalSpellBattleRules.Kind kind = K(id);
                        ServerCard target = kind == OriginalSpellBattleRules.Kind.HealSpell ? null : kind == OriginalSpellBattleRules.Kind.BuffGuardSummon ? ally : enemy;
                        f.Play(source, target, player);
                        bool effect = kind == OriginalSpellBattleRules.Kind.HealSpell ? avatar.CurrentHealth == 25 + a
                            : kind == OriginalSpellBattleRules.Kind.SilenceSpell ? avatar.CurrentHealth == 25 && enemy.CurrentHealth == 8
                            : kind == OriginalSpellBattleRules.Kind.BuffGuardSummon ? ally.QueryAttribute(AttributeType.Attack).Value == 8 + a && ally.CurrentHealth == 8 + b && ally.QueryAttribute(AttributeType.Taunt).Value > 0
                            : enemy.CurrentHealth == 8 - power;
                        bool died = kind == OriginalSpellBattleRules.Kind.BattleSummon;
                        if (died) effect = effect && source.CurrentGameZoneName == GameZoneName.RemoveFromGame && f.CemeteryHas(player, id);
                        Require(effect && (died || source.QueryGameJournalCount(CardJournal.PlayedAsProphecy) == 1) && s.Players[player].QueryAttribute(AttributeType.Mana).Value == mana
                            && s._turnStateStack.Count == 1 && s._turnStateStack.Peek().ActivePlayerIndex == parent,
                            "Original free spell/battle effect/mana/parent resumption differs: " + id + " enemy=" + enemy.CurrentHealth + " power=" + power
                            + " src=" + source.CurrentGameZoneName + "/" + source.CurrentHealth + " prophecyJournal=" + source.QueryGameJournalCount(CardJournal.PlayedAsProphecy)
                            + " mana=" + s.Players[player].QueryAttribute(AttributeType.Mana).Value + "/" + mana + " stack=" + s._turnStateStack.Count);
                    }
                }
                Require(exercised == 5, "Spell/battle Prophecy inventory differs: " + exercised);
            });

            test("strategic-original-spell-battle-ward-friendly-curse-enemy-battle-favourable", delegate {
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 0);
                    ServerCard ward = OriginalTargetedSummonTests.Hand(f, Ward);
                    ServerCard ally = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == ward.NetUid.Id && choice.TargetCardNetUid == ally.NetUid.Id,
                        "Original Ward strategy did not ward its own creature.");
                }
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 0);
                    ServerCard curse = OriginalTargetedSummonTests.Hand(f, Curse);
                    ServerCard ally = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 1, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == curse.NetUid.Id && choice.TargetCardNetUid == enemy.NetUid.Id,
                        "Original Curse strategy did not kill the enemy creature.");
                    Require(enemy.CurrentHealth == 1 && ally.CurrentHealth == 1, "Curse ranking mutated native state.");
                }
                using (OriginalDamageStrategyTests.Fixture f = new OriginalDamageStrategyTests.Fixture(config))
                {
                    f.One.SetAttribute(AttributeType.Mana, 6);
                    ServerCard hunter = OriginalTargetedSummonTests.Hand(f, Hunter);
                    ServerCard small = f.Create("Enraged Mudcrab", GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard big = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 1, f.Two);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == hunter.NetUid.Id && choice.TargetCardNetUid == small.NetUid.Id,
                        "Original Battle strategy did not choose the favourable fight.");
                    Require(small.CurrentHealth == 1 && big.CurrentHealth == 8, "Battle ranking mutated native state.");
                }
            });
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
