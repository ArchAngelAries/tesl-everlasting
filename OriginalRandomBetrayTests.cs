using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalRandomBetrayTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069, Rage = 1055804545;
        private const int Revealing = 1268231575, Prototype = -1756786374, Sacrament = -987206472, DrainBlood = 6794648, Flames = 834379060, Bones = -1156862687;
        private const int ThievesDen = 1659589269, Lucien = 1787748980;

        public static void Run(GameConfiguration config, Action<string,Action> test, string output)
        {
            test("original-random-card-10-exact-metadata-native-pool-audit-and-creation", delegate {
                List<Card> cards = OriginalRandomCardRules.Validate(config);
                Require(cards.Count == 10, "Original random-card inventory differs.");
                foreach (Card card in cards) Require(RuleImporter.IsSupported(card.ResourceId.Id), "Random-card creator not registered: " + card.ResourceId.Id);
                StringBuilder report = new StringBuilder("creator_id\tcreator\tpool_subtype\tpool_size\tpool_supported\n");
                foreach (Card card in cards)
                {
                    List<Card> members = OriginalRandomCardRules.Pool(card.ResourceId.Id);
                    int supported = members.FindAll(delegate(Card c) { return RuleImporter.IsSupported(c.ResourceId.Id); }).Count;
                    Require(members.Count > 0, "Native random pool is empty: " + card.ResourceId.Id);
                    report.AppendLine(card.ResourceId.Id + "\t" + card.Name + "\t" + OriginalRandomCardRules.PoolOf(card.ResourceId.Id) + "\t" + members.Count + "\t" + supported);
                }
                File.WriteAllText(Path.Combine(output, "random-card-pools.tsv"), report.ToString());
                // Native creation for every creator: created cards come from the native pool, owner only.
                foreach (Card card in cards)
                {
                    int id = card.ResourceId.Id; EffectTriggerType trigger = OriginalRandomCardRules.TriggerOf(id);
                    EngineTests.Fixture f = new EngineTests.Fixture(config);
                    ServerCard source = f.CreateId(id, trigger == EffectTriggerType.ActionEffect ? GameZoneName.Hand : GameZoneName.CreatureBoard, 0, f.One);
                    if (trigger == EffectTriggerType.ActionEffect) f.PlayEffects(source, null);
                    else
                    {
                        Require(ServerEffectUtilities.TriggerDirectEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(trigger, source, f.Players)), "Random-card trigger not gathered: " + id);
                        f.DrainTriggers();
                    }
                    List<int> pool = OriginalRandomCardRules.Pool(id).ConvertAll(delegate(Card c) { return c.ResourceId.Id; });
                    // A random pool can contain the creator itself; exclude only its original instance.
                    List<ServerCard> created = f.Catalog.GatherCardsInZone(delegate(ServerCard c) { return c.NetUid != source.NetUid; }, f.One.NetId, GameZoneName.Hand);
                    Require(created.Count == OriginalRandomCardRules.CountOf(id) && f.Catalog.FindCardCount(GameZoneName.Hand, f.Two.NetId) == 0, "Random-card count/owner differs: " + id + " created=" + created.Count + " expected=" + OriginalRandomCardRules.CountOf(id)
                        + " opponentHand=" + f.Catalog.FindCardCount(GameZoneName.Hand, f.Two.NetId) + " ids=" + string.Join(",", created.ConvertAll(delegate(ServerCard c) { return c.CardId.ToString(); }).ToArray()));
                    foreach (ServerCard c in created) Require(pool.Contains(c.CardId), "Created card is outside the native pool: " + id + " / " + c.CardId);
                }
            });

            test("original-granted-pilfer-slay-aura-real-combat", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard den = f.Create(ThievesDen, player, GameZoneName.SupportBoard);
                    ServerCard lucien = f.CreateIn(Lucien, player, GameZoneName.CreatureBoard, 1);
                    ServerCard raider = f.Create(Leviathan, player, GameZoneName.CreatureBoard), slayer = f.CreateIn(Leviathan, player, GameZoneName.CreatureBoard, 1);
                    ServerCard prey = f.CreateIn(Mudcrab, 1 - player, GameZoneName.CreatureBoard, 1);
                    f.Event(delegate {
                        ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                        raider.OnStartTurn(s.Players[player].NetId, gs); slayer.OnStartTurn(s.Players[player].NetId, gs);
                        OfflineAuraRestoration.Apply(gs);
                    });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Attack(raider, f.Avatar(1 - player), player);
                    Require(raider.QueryAttribute(AttributeType.Attack).Value == 9 && f.Avatar(1 - player).CurrentHealth == 30 - 8, "Thieves' Den granted Pilfer did not trigger: " + raider.QueryAttribute(AttributeType.Attack).Value);
                    f.Attack(slayer, prey, player);
                    // Thieves' Den +1/+1 (Pilfer not fired here) does not apply; Lucien grants Slay +2/+2 to other friendly creatures.
                    Require(prey.CurrentGameZoneName != GameZoneName.CreatureBoard && slayer.QueryAttribute(AttributeType.Attack).Value == 10, "Lucien Lachance granted Slay did not trigger: " + slayer.QueryAttribute(AttributeType.Attack).Value);
                    Require(lucien.QueryAttribute(AttributeType.Attack).Value == 1, "Lucien granted Slay to itself.");
                }
            });

            test("original-betray-4-metadata-and-offer-sacrifice-free-replay", delegate {
                List<Card> cards = OriginalBetrayRules.Validate(config);
                Require(cards.Count == 4, "Original Betray inventory differs.");
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard spell = f.Create(DrainBlood, player, GameZoneName.Hand);
                    ServerCard fodder = f.Create(Mudcrab, player, GameZoneName.CreatureBoard), keeper = f.CreateIn(Leviathan, player, GameZoneName.CreatureBoard, 1);
                    ServerCard enemy = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                    int cost = spell.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(spell, enemy, player);
                    CardCommandList offer = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Require(offer.ValidCommands.Count == 1 && offer.MustSelectCommand && (offer.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.BetrayChoice) != 0
                        && offer.ValidCommands[0].CardNetUid == spell.NetUid.Id && offer.ValidCommands[0].ActiveAbilityTargets.Count == 2 && offer.ValidCommands[0].DisplayCards.Count == 1,
                        "Betray offer command differs: pending=" + OfflineBetrayRestoration.HasPending(s) + " count=" + offer.ValidCommands.Count
                        + " mask0=" + (offer.ValidCommands.Count > 0 ? offer.ValidCommands[0].ValidCardCommandMask : -1) + " must=" + offer.MustSelectCommand
                        + " zone=" + spell.CurrentGameZoneName + "/" + spell.CurrentGameZoneType + " betray=" + spell.QueryAttribute(AttributeType.Betray).Value
                        + " spell=" + spell.HasSubtype(CardSubtype.Spell) + " phase=" + s._turnStateStack.Peek().Phase + " stack=" + s._turnStateStack.Count);
                    Decision attack = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = offer.ListUid, ActorCardNetUid = fodder.NetUid.Id };
                    Require(!CommandRestoration.ValidateDecision(attack, offer, s.CardCatalog, s.Players[player]), "Ordinary command admitted during a Betray offer.");
                    f.Betray(spell, fodder, player);
                    CardCommandList replay = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Require(spell.CurrentGameZoneType == GameZoneType.Betray && fodder.CurrentGameZoneName != GameZoneName.CreatureBoard && f.CemeteryHas(player, Mudcrab)
                        && replay.ValidCommands.Count == 1 && (replay.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.BetrayPlay) != 0
                        && replay.ValidCommands[0].PlayTargets.Contains(enemy.NetUid.Id), "Betray sacrifice/replay command differs.");
                    f.Play(spell, enemy, player);
                    Require(enemy.QueryAttribute(AttributeType.Attack).Value == 4 && enemy.CurrentHealth == 4 && spell.CurrentGameZoneName == GameZoneName.Cemetery
                        && s.Players[player].QueryAttribute(AttributeType.Mana).Value == 12 - cost && keeper.CurrentGameZoneName == GameZoneName.CreatureBoard
                        && !OfflineBetrayRestoration.HasPending(s), "Betray free replay differs: enemy=" + enemy.QueryAttribute(AttributeType.Attack).Value + "/" + enemy.CurrentHealth
                        + " mana=" + s.Players[player].QueryAttribute(AttributeType.Mana).Value);
                    CardCommandList after = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    bool reoffered = false;
                    foreach (CardCommandState c in after.ValidCommands) if ((c.ValidCardCommandMask & (int)CardCommand.BetrayChoice) != 0) reoffered = true;
                    Require(!reoffered, "Replay offered Betray again.");
                }
            });

            test("original-betray-decline-no-creature-and-lane-replays", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard sacrament = f.Create(Sacrament, player, GameZoneName.Hand), other = f.Create(Sacrament, player, GameZoneName.Hand);
                    ServerCard ally = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                    ServerCard avatar = f.Avatar(player), enemyAvatar = f.Avatar(1 - player);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); avatar.SetAvatarHealthPreGame(29); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(sacrament, null, player);
                    f.Betray(sacrament, null, player);
                    Require(enemyAvatar.CurrentHealth == 28 && avatar.CurrentHealth == 31 && ally.CurrentGameZoneName == GameZoneName.CreatureBoard && !OfflineBetrayRestoration.HasPending(s)
                        && sacrament.CurrentGameZoneName == GameZoneName.Cemetery, "Betray decline differs.");
                    f.Play(rageFree(f, player), ally, player);
                    f.Play(other, null, player);
                    Require(!OfflineBetrayRestoration.HasPending(s) && OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, other) == null
                        && enemyAvatar.CurrentHealth == 26, "Betray offered without a creature to sacrifice.");
                }
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard bones = f.Create(Bones, player, GameZoneName.Hand), flames = f.Create(Flames, player, GameZoneName.Hand);
                    ServerCard e1 = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard), e2 = f.CreateIn(Leviathan, 1 - player, GameZoneName.CreatureBoard, 1);
                    int horror = OriginalBetrayRules.RisenHorror(config);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 20); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.PlayLane(bones, BoardLaneTarget.MyLeftLane, player);
                    ServerCard first = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == horror; }, s.Players[player].NetId, GameZoneName.CreatureBoard);
                    Require(first != null && first.CurrentLaneIndex == 0, "Rising of Bones did not summon in the chosen lane.");
                    f.Betray(bones, first, player);
                    f.PlayLane(bones, BoardLaneTarget.MyRightLane, player);
                    Require(f.LaneCount(player, 0) == 0 && f.LaneCount(player, 1) == 1, "Rising of Bones Betray replay lane differs.");
                    ServerCard second = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.CardId == horror; }, s.Players[player].NetId, GameZoneName.CreatureBoard);
                    f.PlayLane(flames, BoardLaneTarget.OtherLeftLane, player);
                    f.Betray(flames, second, player);
                    f.PlayLane(flames, BoardLaneTarget.OtherRightLane, player);
                    Require(e1.CurrentHealth == 6 && e2.CurrentHealth == 6 && f.BoardCount(player) == 0, "Traitor's Flames Betray lane replay differs: " + e1.CurrentHealth + "/" + e2.CurrentHealth);
                }
            });

            test("strategic-original-betray-sacrifices-only-when-worth-it", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard spell = f.Create(DrainBlood, player, GameZoneName.Hand);
                    ServerCard big = f.Create(Leviathan, player, GameZoneName.CreatureBoard);
                    ServerCard enemy = f.Create(Mudcrab, 1 - player, GameZoneName.CreatureBoard), enemy2 = f.CreateIn(Mudcrab, 1 - player, GameZoneName.CreatureBoard, 1);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(spell, enemy, player);
                    Decision choice = StrategicDecisionRestoration.ChooseDecision(s, s._turnStateStack.Peek().CurrentCommandItem.CommandList);
                    Require(choice.Type == CcgRpc.DecisionType.Betray && choice.DiscardCardNetUids.Count == 0, "AI sacrificed an 8/8 to replay a -2/-2.");
                }
            });
        }
        private static ServerCard rageFree(OriginalTargetedSummonTests.LifecycleFixture f, int player)
        {
            ServerCard rage = f.Create(Rage, player, GameZoneName.Hand);
            f.Sim._turnStateStack.Peek().CurrentCommandItem = f.Sim.CardCatalog.GenerateCardCommandItem(f.Sim.Players[player], f.Sim.Players[1 - player], f.Sim.Players);
            return rage;
        }
        private static int Hand(EngineTests.Fixture f, PlayerState p) { return f.Catalog.FindCardCount(GameZoneName.Hand, p.NetId); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
