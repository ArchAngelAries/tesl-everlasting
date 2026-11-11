using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class DrawAndDeathEventTests
    {
        private const int Crab = 620729069, Librarian = 322740160, Fence = 1351032432, Generator = -1346805516;
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("feasting-hunger-heals-per-enemy-creature", delegate {
                foreach (int count in new int[] { 0, 3 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    f.Event(delegate { f.Avatar(me).ModifyCurrentHealth(-1, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    for (int i = 0; i < count; i++) f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, i % 2);
                    ServerCard hunger = f.Create(-2052923845, me, GameZoneName.Hand);
                    int before = f.Avatar(me).CurrentHealth, theirs = f.Avatar(enemy).CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(hunger, 1, me);
                    Require(f.Avatar(me).CurrentHealth == before + count && f.Avatar(enemy).CurrentHealth == theirs,
                        "Feasting Hunger counted the wrong creatures: " + count);
                }
            });
            test("rayvat-grants-player-ward-that-absorbs-damage", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard source = f.Create(-1116852705, me, GameZoneName.Hand);
                    ServerCard attacker = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(source, 0, me);
                    Require(f.Avatar(me).QueryAttribute(AttributeType.Ward).Value > 0 && f.Avatar(enemy).QueryAttribute(AttributeType.Ward).Value == 0,
                        "Rayvat did not give Ward only to its controller.");
                    f.Event(delegate { CardActions.Damage.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), attacker, f.Avatar(me), 2, false, null); }); f.DrainEffects();
                    Require(f.Avatar(me).CurrentHealth == 30 && f.Avatar(me).QueryAttribute(AttributeType.Ward).Value == 0, "Player Ward did not absorb damage.");
                    f.Event(delegate { CardActions.Damage.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), attacker, f.Avatar(me), 2, false, null); }); f.DrainEffects();
                    Require(f.Avatar(me).CurrentHealth == 28, "Damage after the Ward broke did not apply.");
                }
            });
            test("blessing-of-the-grove-heals-per-friendly-creature", delegate {
                foreach (int count in new int[] { 0, 3 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    f.Event(delegate { f.Avatar(me).ModifyCurrentHealth(-1, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    for (int i = 0; i < count; i++) f.CreateIn(Crab, me, GameZoneName.CreatureBoard, i % 2);
                    f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard action = f.Create(1268972504, me, GameZoneName.Hand);
                    int before = f.Avatar(me).CurrentHealth, theirs = f.Avatar(enemy).CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me); f.Play(action, null, me);
                    Require(f.Avatar(me).CurrentHealth == before + 2 * count && f.Avatar(enemy).CurrentHealth == theirs,
                        "Blessing of the Grove count=" + count + " health=" + before + "->" + f.Avatar(me).CurrentHealth
                        + " opponent=" + theirs + "->" + f.Avatar(enemy).CurrentHealth + " board=" + f.BoardCount(me) + " action=" + action.CurrentGameZoneName);
                }
            });
            test("lyris-titanborn-damages-to-next-rune-or-zero", delegate {
                for (int noRunes = 0; noRunes < 2; noRunes++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    f.CreateIn(-1577153690, me, GameZoneName.CreatureBoard, 0);
                    if (noRunes != 0) s.Players[enemy].SetAttribute(AttributeType.NumRunes, 0);
                    int mine = f.Avatar(me).CurrentHealth;
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()), new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, s.Players[enemy])); }); f.DrainEffects();
                    Require(f.Avatar(enemy).CurrentHealth == 30, "Lyris fired on the enemy's turn.");
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()), new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, s.Players[me])); }); f.DrainEffects();
                    Require(f.Avatar(enemy).CurrentHealth == (noRunes == 0 ? 25 : 0) && f.Avatar(me).CurrentHealth == mine,
                        "Lyris did not damage to the next rune or zero: " + noRunes);
                }
            });
            test("lyris-titanborn-breaks-rune-and-draws-in-turn-transition", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    f.CreateIn(-1577153690, me, GameZoneName.CreatureBoard, 0);
                    f.EndTurn();
                    int deck = f.DeckCount(enemy), hand = f.HandCount(enemy), runes = s.Players[enemy].QueryAttribute(AttributeType.NumRunes).Value;
                    f.EndTurn();
                    Require(f.Avatar(enemy).CurrentHealth == 25 && s.Players[enemy].QueryAttribute(AttributeType.NumRunes).Value == runes - 1
                        && f.DeckCount(enemy) == deck - 1 && f.HandCount(enemy) == hand + 1, "Lyris did not produce exactly one rune-break draw.");
                }
            });
            test("hircine-healing-restores-to-threshold-on-owner-turn", delegate {
                int[] ids = { -908388106, -1797069156 }, thresholds = { 15, 30 };
                for (int i = 0; i < ids.Length; i++)
                for (int above = 0; above < 2; above++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    int initial = above == 0 ? 10 : thresholds[i] + 2;
                    f.Event(delegate { f.Avatar(me).ModifyCurrentHealth(initial - f.Avatar(me).CurrentHealth, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    f.Create(ids[i], me, GameZoneName.SupportBoard);
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()), new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, s.Players[enemy])); }); f.DrainEffects();
                    Require(f.Avatar(me).CurrentHealth == initial, "Hircine healing triggered on the opponent's turn.");
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()), new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, s.Players[me])); }); f.DrainEffects();
                    Require(f.Avatar(me).CurrentHealth == Math.Max(initial, thresholds[i]), "Hircine healing threshold differs: " + ids[i]);
                }
            });
            test("attuned-dremora-copies-friendly-keywords", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard first = f.CreateIn(1691833255, me, GameZoneName.CreatureBoard, 0);
                    ServerCard second = f.CreateIn(-163473279, me, GameZoneName.CreatureBoard, 1);
                    ServerCard foe = f.CreateIn(1427457192, enemy, GameZoneName.CreatureBoard, 0);
                    foe.SetAttribute(AttributeType.Charge, 1);
                    f.Event(delegate { foe.AddWard(DecisionRestoration.State(s, s._turnStateStack.Peek()), foe); }); f.DrainEffects();
                    ServerCard source = f.Create(1361402109, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(source, 1, me);
                    Require(source.QueryAttribute(AttributeType.Taunt).Value > 0 && source.QueryAttribute(AttributeType.Ward).Value > 0
                        && source.QueryAttribute(AttributeType.Deathtouch).Value > 0 && source.QueryAttribute(AttributeType.Charge).Value == 0, "Attuned Dremora copied the wrong keywords.");
                    Require(first.QueryAttribute(AttributeType.Taunt).Value > 0 && first.QueryAttribute(AttributeType.Ward).Value > 0
                        && second.QueryAttribute(AttributeType.Deathtouch).Value > 0, "Copying removed keywords from allies.");
                }
            });
            test("blades-other-lane-targets-and-effects", delegate {
                int[] ids = { 639072281, 353303482, 489360587 };
                for (int i = 0; i < ids.Length; i++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard same = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    ServerCard friend = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1);
                    ServerCard foe = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1);
                    ServerCard source = f.Create(ids[i], me, GameZoneName.Hand);
                    int attack = friend.QueryAttribute(AttributeType.Attack).Value, health = friend.CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me); f.AutoAnswerTargets = false;
                    CardCommandList play = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = play.ListUid,
                        ActorCardNetUid = source.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id, LaneIndex = 0 });
                    f.Until(delegate { return s._turnStateStack.Count > 1 && s._turnStateStack.Peek().CurrentCommandItem != null
                        && s._turnStateStack.Peek().CurrentSequence == null && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands.Count == 1
                        && (s._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands[0].ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0; });
                    CardCommandList prompt = s._turnStateStack.Peek().CurrentCommandItem.CommandList; CardCommandState choices = prompt.ValidCommands[0];
                    Require(choices.PlayTargets.Contains(friend.NetUid.Id) && choices.PlayTargets.Contains(foe.NetUid.Id)
                        && !choices.PlayTargets.Contains(same.NetUid.Id) && !choices.PlayTargets.Contains(source.NetUid.Id), "Other-lane choice target filter differs.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = prompt.ListUid,
                        ActorCardNetUid = choices.CardNetUid, TargetCardNetUid = friend.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s) && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != play.ListUid; });
                    Require(friend.QueryAttribute(AttributeType.Attack).Value == attack + (i == 0 ? 0 : i == 1 ? 2 : 1)
                        && friend.CurrentHealth == health + (i == 0 ? 2 : i == 1 ? 0 : 1)
                        && (i != 2 || friend.QueryAttribute(AttributeType.Taunt).Value > 0), "Other-lane effect differs: " + ids[i]);
                }
            });
            test("blades-stalwart-gains-current-health-on-slay", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard stalwart = f.CreateIn(-1237441152, me, GameZoneName.CreatureBoard, 0);
                    ServerCard victim = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    int attack = stalwart.QueryAttribute(AttributeType.Attack).Value, healthAtSlay = stalwart.CurrentHealth;
                    f.EndTurn(); f.EndTurn(); f.Attack(stalwart, victim, me);
                    Require(victim.CurrentGameZoneName != GameZoneName.CreatureBoard
                        && stalwart.QueryAttribute(AttributeType.Attack).Value == attack + healthAtSlay,
                        "Blades Stalwart timing: initial power " + attack + ", final power " + stalwart.QueryAttribute(AttributeType.Attack).Value
                        + ", final health " + stalwart.CurrentHealth + ", victim zone " + victim.CurrentGameZoneName);
                }
            });
            test("faded-wraith-draws-current-power", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard wraith = f.Create(878082570, me, GameZoneName.Hand);
                    wraith.SetAttribute(AttributeType.Attack, 3);
                    int hand = f.HandCount(me), deck = f.DeckCount(me);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(wraith, 0, me);
                    Require(f.HandCount(me) == hand + 2 && f.DeckCount(me) == deck - 3, "Faded Wraith did not draw its current power.");
                }
            });
            test("dremora-archer-damages-by-current-power", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    ServerCard foe = f.CreateIn(1427457192, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard archer = f.Create(-1272405782, me, GameZoneName.Hand);
                    archer.SetAttribute(AttributeType.Attack, 3); int health = foe.CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me);
                    EquipmentChoiceTests.ChooseAfterPlacement(f, archer, foe, friend, foe, me, true);
                    Require(foe.CurrentHealth == health - 3 && friend.CurrentHealth == 1, "Dremora Archer did not use its current power on its chosen target.");
                }
            });
            test("daedric-summoning-creates-a-daedra-for-each-player", delegate {
                for (int attempt = 0; attempt < 10; attempt++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard action = f.Create(1091469154, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.Play(action, null, me);
                    foreach (int owner in new int[] { me, enemy })
                    {
                        List<ServerCard> creatures = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[owner].NetId, GameZoneName.CreatureBoard);
                        List<ServerCard> all = s.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.OwnerNetId == s.Players[owner].NetId && c.HasSubtype(CardSubtype.Daedra); });
                        Require(all.Count > 0, "Daedric Summoning player " + owner + " board count " + creatures.Count
                            + " ids " + string.Join(",", creatures.ConvertAll(delegate(ServerCard c) { return c.CardId.ToString(); }).ToArray()));
                    }
                }
            });
            test("bringer-of-nightmares-summoned-by-effect-vision-and-transform", delegate {
                // Summoned by another card (Daedric Summoning), its Summon shows a vision card and then asks for a creature to
                // transform: a target prompt with a display card and no custom UI (checkpoint 33).
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int Bringer = -1755823232;
                    ServerCard crab = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard bringer = f.CreateIn(Bringer, me, GameZoneName.CreatureBoard, 0);
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                        new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, bringer, s.Players)); });
                    f.Until(delegate { f.AnswerTargetPrompt(); return OriginalTargetedSummonTests.Ready(s); });
                    int crabs = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Crab; }, s.Players[enemy].NetId, GameZoneName.CreatureBoard).Count;
                    int bringers = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Bringer; }, s.Players[me].NetId, GameZoneName.CreatureBoard).Count;
                    Require(crabs + bringers == 1, "Bringer of Nightmares did not transform exactly one creature (crabs " + crabs + ", bringers " + bringers + ").");
                }
            });
            test("memory-wraith-banishes-opponent-discard", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard own = f.Create(Crab, me, GameZoneName.Cemetery);
                    ServerCard foeCreature = f.Create(Crab, enemy, GameZoneName.Cemetery);
                    ServerCard foeAction = f.Create(-1879015996, enemy, GameZoneName.Cemetery);
                    ServerCard wraith = f.Create(-1975806074, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(wraith, 0, me);
                    Require(own.CurrentGameZoneName == GameZoneName.Cemetery && foeCreature.CurrentGameZoneName != GameZoneName.Cemetery
                        && foeAction.CurrentGameZoneName != GameZoneName.Cemetery, "Memory Wraith failed to banish the entire enemy discard.");
                }
            });
            test("forerunner-veteran-draws-one-matching-deck-copy", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard source = f.CreateIn(-1376421634, me, GameZoneName.CreatureBoard, 0);
                    f.EndTurn(); f.EndTurn();
                    f.Create(-1376421634, me, GameZoneName.Deck); f.Create(-1376421634, me, GameZoneName.Deck);
                    int hand = f.HandCount(me), deck = f.DeckCount(me);
                    f.Attack(source, f.Avatar(enemy), me);
                    List<ServerCard> copies = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == -1376421634; }, s.Players[me].NetId, GameZoneName.Hand);
                    Require(copies.Count == 1 && f.HandCount(me) == hand + 1 && f.DeckCount(me) == deck - 1, "Veteran did not draw exactly one matching copy.");
                    f.EndTurn(); f.EndTurn(); hand = f.HandCount(me); deck = f.DeckCount(me);
                    f.Attack(source, f.Avatar(enemy), me);
                    Require(f.HandCount(me) == hand && f.DeckCount(me) == deck, "Veteran triggered again on a second attack.");
                }
            });
            test("sound-the-alarm-removes-enemy-cover", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    ServerCard first = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard second = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1);
                    f.Event(delegate { ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                        friend.AddCover(true, gs, null); first.AddCover(true, gs, null); second.AddCover(true, gs, null); }); f.DrainEffects();
                    ServerCard alarm = f.Create(1738444861, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.Play(alarm, null, me);
                    Require(friend.QueryAttribute(AttributeType.Cover).Value > 0 && first.QueryAttribute(AttributeType.Cover).Value == 0
                        && second.QueryAttribute(AttributeType.Cover).Value == 0, "Sound the Alarm removed the wrong Cover.");
                    f.Event(delegate { first.AddCover(true, DecisionRestoration.State(s, s._turnStateStack.Peek()), null); }); f.DrainEffects();
                    Require(first.QueryAttribute(AttributeType.Cover).Value > 0, "A creature could not regain Cover.");
                }
            });
            test("ahnassi-steals-keywords-from-both-enemy-lanes", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.CreateIn(1691833255, me, GameZoneName.CreatureBoard, 0);
                    ServerCard first = f.CreateIn(1691833255, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard second = f.CreateIn(-163473279, enemy, GameZoneName.CreatureBoard, 1);
                    ServerCard source = f.Create(-462299413, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(source, 1, me);
                    Require(source.QueryAttribute(AttributeType.Taunt).Value > 0 && source.QueryAttribute(AttributeType.Ward).Value > 0
                        && source.QueryAttribute(AttributeType.Deathtouch).Value > 0, "Ahnassi did not gain all three distinct keywords.");
                    Require(first.QueryAttribute(AttributeType.Taunt).Value == 0 && first.QueryAttribute(AttributeType.Ward).Value == 0
                        && second.QueryAttribute(AttributeType.Taunt).Value == 0 && second.QueryAttribute(AttributeType.Deathtouch).Value == 0
                        && friend.QueryAttribute(AttributeType.Taunt).Value > 0 && friend.QueryAttribute(AttributeType.Ward).Value > 0,
                        "Ahnassi failed to strip both enemies or affected a friendly.");
                }
            });
            test("dread-clannfear-and-intimidate-remove-enemy-guard", delegate {
                foreach (int id in new int[] { -1994059211, -515379928 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.CreateIn(1691833255, me, GameZoneName.CreatureBoard, 0);
                    ServerCard foe = f.CreateIn(1691833255, enemy, GameZoneName.CreatureBoard, 0);
                    ServerCard second = f.CreateIn(1691833255, enemy, GameZoneName.CreatureBoard, 1);
                    ServerCard unguarded = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1);
                    ServerCard source = f.Create(id, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    if (id == -1994059211) f.PlayCreatureLane(source, 1, me); else f.Play(source, null, me);
                    Require(friend.QueryAttribute(AttributeType.Taunt).Value > 0 && foe.QueryAttribute(AttributeType.Taunt).Value == 0
                        && second.QueryAttribute(AttributeType.Taunt).Value == 0 && foe.QueryAttribute(AttributeType.Ward).Value > 0,
                        "Guard removal affected the wrong side or removed Ward: " + id);
                    f.Event(delegate {
                        foreach (ServerCard target in new ServerCard[] { foe, unguarded })
                        {
                            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, s._turnStateStack.Peek());
                            target.AddEnchantment(gs, new EnchantmentInstance(EnchantmentUtilities.CreateKeywordEnchantment(AttributeType.Taunt), 1, source.CardId, target), source);
                            target.RecalculateEnchantmentsWithTriggers(gs, source);
                        }
                    }); f.DrainEffects();
                    Require(foe.QueryAttribute(AttributeType.Taunt).Value > 0 && unguarded.QueryAttribute(AttributeType.Taunt).Value > 0,
                        "A later Guard grant failed after Guard removal: " + id);
                    f.Event(delegate { foe.Silence(DecisionRestoration.State(s, s._turnStateStack.Peek()), source); }); f.DrainEffects();
                    Require(foe.QueryAttribute(AttributeType.Taunt).Value == 0, "Silence unexpectedly restored native Guard.");
                }
            });
            test("iliac-sorceror-doubles-when-ward-breaks", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard sorceror = f.CreateIn(-1399661641, me, GameZoneName.CreatureBoard, 0);
                    ServerCard attacker = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    int attack = sorceror.QueryAttribute(AttributeType.Attack).Value, health = sorceror.CurrentHealth;
                    f.Event(delegate { CardActions.Damage.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), attacker, sorceror, 1, false, null); });
                    f.DrainEffects();
                    Require(sorceror.QueryAttribute(AttributeType.Attack).Value == attack * 2 && sorceror.CurrentHealth == health
                        && sorceror.QueryAttribute(AttributeType.Ward).Value == 0, "Sorceror did not double when Ward prevented damage.");
                    f.Event(delegate { sorceror.AddWard(DecisionRestoration.State(s, s._turnStateStack.Peek()), sorceror); });
                    f.DrainEffects();
                    f.Event(delegate { CardActions.Damage.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), attacker, sorceror, 1, false, null); });
                    f.DrainEffects();
                    Require(sorceror.QueryAttribute(AttributeType.Attack).Value == attack * 4 && sorceror.CurrentHealth == health, "Sorceror did not double on a second Ward break.");
                }
            });
            test("world-eaters-eyrie-doubles-friendly-seven-cost-summons", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    f.Create(1021793029, me, GameZoneName.SupportBoard);
                    ServerCard low = f.Create(Crab, me, GameZoneName.Hand);
                    low.SetAttribute(AttributeType.ManaCost, 6);
                    ServerCard high = f.Create(1427457192, me, GameZoneName.Hand);
                    int lowAttack = low.QueryAttribute(AttributeType.Attack).Value, highAttack = high.QueryAttribute(AttributeType.Attack).Value, highHealth = high.CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(low, 0, me);
                    Require(low.QueryAttribute(AttributeType.Attack).Value == lowAttack, "Eyrie doubled a creature below seven cost.");
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(high, 0, me);
                    Require(high.QueryAttribute(AttributeType.Attack).Value == highAttack * 2 && high.CurrentHealth == highHealth * 2, "Eyrie did not double a seven-cost creature.");
                    f.EndTurn();
                    ServerCard enemyHigh = f.Create(1427457192, enemy, GameZoneName.Hand);
                    f.Mana(enemy, 30); f.Refresh(enemy); f.PlayCreatureLane(enemyHigh, 0, enemy);
                    Require(enemyHigh.QueryAttribute(AttributeType.Attack).Value == highAttack && enemyHigh.CurrentHealth == highHealth, "Eyrie doubled an enemy creature.");
                }
            });
            test("fighters-guild-elite-doubles-on-expertise", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard elite = f.CreateIn(-1563068710, me, GameZoneName.CreatureBoard, 0);
                    ServerCard foe = f.CreateIn(1427457192, enemy, GameZoneName.CreatureBoard, 0);
                    int attack = elite.QueryAttribute(AttributeType.Attack).Value, health = elite.CurrentHealth;
                    f.EndTurn(); Require(elite.QueryAttribute(AttributeType.Attack).Value == attack, "Expertise fired without a noncreature play.");
                    f.EndTurn();
                    for (int n = 1; n <= 2; n++)
                    {
                        ServerCard calm = f.Create(-1143757804, me, GameZoneName.Hand);
                        f.Mana(me, 30); f.Refresh(me); f.Play(calm, foe, me);
                        Require(elite.QueryAttribute(AttributeType.Attack).Value == attack, "Expertise fired before the end of turn.");
                        f.EndTurn(); attack *= 2; health *= 2;
                        Require(elite.QueryAttribute(AttributeType.Attack).Value == attack && elite.CurrentHealth == health, "Elite did not double once at end of turn.");
                        if (n < 2) f.EndTurn();
                    }
                }
            });
            test("dremora-markynaz-doubles-self-on-summon", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard card = f.Create(-553062902, me, GameZoneName.Hand);
                    int attack = card.QueryAttribute(AttributeType.Attack).Value, health = card.CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(card, 0, me);
                    List<ServerCard> board = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == -553062902; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(board.Count == 1 && board[0].QueryAttribute(AttributeType.Attack).Value == 2 * attack
                        && board[0].CurrentHealth == 2 * health, "Markynaz did not double itself when it was the only target.");
                }
            });
            test("dremora-markynaz-doubles-chosen-wounded-creature", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard friend = f.CreateIn(1427457192, me, GameZoneName.CreatureBoard, 0);
                    ServerCard foe = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    f.Event(delegate { friend.ModifyCurrentHealth(-3, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    int attack = friend.QueryAttribute(AttributeType.Attack).Value, health = friend.CurrentHealth;
                    ServerCard source = f.Create(-553062902, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    EquipmentChoiceTests.ChooseAfterPlacement(f, source, friend, source, foe, me, true);
                    Require(friend.QueryAttribute(AttributeType.Attack).Value == attack * 2 && friend.CurrentHealth == health * 2
                        && foe.CurrentHealth == 1, "Markynaz did not double only the selected wounded creature's current stats.");
                }
            });
            test("elsweyr-lookout-doubles-on-pilfer", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard card = f.CreateIn(1978118591, me, GameZoneName.CreatureBoard, 0);
                    int attack = card.QueryAttribute(AttributeType.Attack).Value, health = card.CurrentHealth;
                    f.EndTurn(); f.EndTurn(); f.Attack(card, f.Avatar(enemy), me);
                    Require(card.QueryAttribute(AttributeType.Attack).Value == 2 * attack && card.CurrentHealth == 2 * health,
                        "Elsweyr Lookout did not double its stats on Pilfer.");
                }
            });
            test("riften-pillager-counts-opponent-runes", delegate {
                foreach (int count in new int[] { 0, 3, 5 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    s.Players[me].SetAttribute(AttributeType.NumRunesLost, 2); s.Players[enemy].SetAttribute(AttributeType.NumRunesLost, count);
                    ServerCard card = f.Create(-155831290, me, GameZoneName.Hand);
                    int attack = card.QueryAttribute(AttributeType.Attack).Value, health = card.QueryAttribute(AttributeType.MaxHealth).Value, enchantments = card.Enchantments.Count;
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(card, 0, me);
                    List<ServerCard> board = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == -155831290; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(board.Count == 1 && board[0].QueryAttribute(AttributeType.Attack).Value == attack + count
                        && board[0].QueryAttribute(AttributeType.MaxHealth).Value == health + count, "Riften Pillager rune count differs: " + count);
                    if (count == 0) Require(board[0].Enchantments.Count == enchantments, "Zero runes added an enchantment.");
                }
            });
            test("gortwog-summons-random-deck-creature-on-owner-turn", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    f.CreateIn(-704695871, me, GameZoneName.CreatureBoard, 0);
                    List<ServerCard> old = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[me].NetId, GameZoneName.Deck);
                    f.Event(delegate { foreach (ServerCard c in old) ServerCardCatalog.MoveToPurgatory(DecisionRestoration.State(s, s._turnStateStack.Peek()), c); });
                    ServerCard creature = f.Create(1427457192, me, GameZoneName.Deck);
                    f.Create(-1879015996, me, GameZoneName.Deck);
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()), new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, s.Players[enemy])); });
                    f.DrainEffects();
                    Require(creature.CurrentGameZoneName == GameZoneName.Deck, "Gortwog triggered on the opponent's turn.");
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()), new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, s.Players[me])); });
                    f.DrainEffects();
                    Require(creature.CurrentGameZoneName == GameZoneName.CreatureBoard && f.DeckCount(me) == 1, "Gortwog failed to summon the deck creature.");
                }
            });
            test("gortwog-respects-full-lanes", delegate {
                for (int fullBoard = 0; fullBoard <= 1; fullBoard++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    f.CreateIn(-704695871, me, GameZoneName.CreatureBoard, 0);
                    for (int i = 0; i < 3; i++) f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    if (fullBoard != 0) for (int i = 0; i < 4; i++) f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1);
                    List<ServerCard> old = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[me].NetId, GameZoneName.Deck);
                    f.Event(delegate { foreach (ServerCard c in old) ServerCardCatalog.MoveToPurgatory(DecisionRestoration.State(s, s._turnStateStack.Peek()), c); });
                    ServerCard creature = f.Create(1427457192, me, GameZoneName.Deck);
                    f.Event(delegate { ServerEffectUtilities.TriggerEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()), new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, s.Players[me])); });
                    f.DrainEffects();
                    Require(fullBoard == 0 ? creature.CurrentGameZoneName == GameZoneName.CreatureBoard && creature.CurrentLaneIndex == 1
                        : creature.CurrentGameZoneName == GameZoneName.Deck, "Gortwog did not respect full lanes: " + fullBoard);
                }
            });
            test("gladiator-arena-damages-starting-player", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    f.Create(-2028696251, me, GameZoneName.SupportBoard);
                    int mine = f.Avatar(me).CurrentHealth, theirs = f.Avatar(enemy).CurrentHealth;
                    f.EndTurn();
                    Require(f.Avatar(me).CurrentHealth == mine && f.Avatar(enemy).CurrentHealth == theirs - 2, "Arena did not damage the opposing starting player.");
                    f.EndTurn();
                    Require(f.Avatar(me).CurrentHealth == mine - 2 && f.Avatar(enemy).CurrentHealth == theirs - 2, "Arena did not damage its owner's starting turn.");
                }
            });
            test("brilliant-experiment-draws-friendly-copy", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard original = f.CreateIn(1427457192, me, GameZoneName.CreatureBoard, 0);
                    f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1);
                    ServerCard experiment = f.Create(-1594328847, me, GameZoneName.Hand);
                    int hand = f.HandCount(me);
                    f.Mana(me, 30); f.Refresh(me); f.Play(experiment, original, me);
                    List<ServerCard> copies = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == original.CardId; }, s.Players[me].NetId, GameZoneName.Hand);
                    Require(copies.Count == 1 && copies[0] != original && original.CurrentGameZoneName == GameZoneName.CreatureBoard
                        && f.HandCount(me) == hand, "Brilliant Experiment did not copy its chosen friendly creature.");
                }
            });
            test("blackmail-draws-opponent-creature-copy", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard action = f.Create(-229601922, me, GameZoneName.Hand);
                    List<ServerCard> old = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[enemy].NetId, GameZoneName.Deck);
                    f.Event(delegate { foreach (ServerCard c in old) ServerCardCatalog.MoveToPurgatory(DecisionRestoration.State(s, s._turnStateStack.Peek()), c); });
                    ServerCard original = f.Create(1427457192, enemy, GameZoneName.Deck);
                    f.Create(-1879015996, enemy, GameZoneName.Deck);
                    int deck = f.DeckCount(enemy), hand = f.HandCount(me);
                    f.Mana(me, 30); f.Refresh(me); f.Play(action, null, me);
                    List<ServerCard> copies = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == original.CardId; }, s.Players[me].NetId, GameZoneName.Hand);
                    Require(copies.Count == 1 && copies[0] != original && f.DeckCount(enemy) == deck && f.HandCount(me) == hand
                        && original.CurrentGameZoneName == GameZoneName.Deck, "Blackmail did not copy only the opposing creature.");
                }
            });
            test("angry-grahl-compares-hand-after-play", delegate {
                for (int relation = -1; relation <= 1; relation++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard grahl = f.Create(-1071100254, me, GameZoneName.Hand);
                    int attack = grahl.QueryAttribute(AttributeType.Attack).Value, health = grahl.QueryAttribute(AttributeType.MaxHealth).Value;
                    while (f.HandCount(enemy) < f.HandCount(me) - 1 + relation) f.Create(Crab, enemy, GameZoneName.Hand);
                    while (f.HandCount(me) - 1 + relation < f.HandCount(enemy)) f.Create(Crab, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(grahl, 0, me);
                    List<ServerCard> board = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == -1071100254; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    int bonus = relation > 0 ? 2 : 0;
                    Require(board.Count == 1 && board[0].QueryAttribute(AttributeType.Attack).Value == attack + bonus
                        && board[0].QueryAttribute(AttributeType.MaxHealth).Value == health + bonus, "Angry Grahl hand comparison differs: " + relation);
                }
            });
            test("blood-magic-lord-summon-and-slay", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int lordId = -765026889;
                    Require(OriginalSpecRules.HasRestoredRules(lordId), "Blood Magic Lord spec missing: " + OriginalSpecRules.SkipReason(lordId));
                    ServerCard lord = f.Create(lordId, me, GameZoneName.Hand);
                    ServerCard victim = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(lord, 0, me);
                    List<ServerCard> spells = BloodSpells(s, me);
                    Require(spells.Count == 1 && spells[0].HasSubtype(CardSubtype.Spell), "Summon did not create one Blood Magic spell.");
                    f.EndTurn(); f.EndTurn(); f.Attack(lord, victim, me);
                    spells = BloodSpells(s, me);
                    Require(victim.CurrentGameZoneType != GameZoneType.Board && spells.Count == 2 && spells[1].HasSubtype(CardSubtype.Spell),
                        "Slay did not create a second Blood Magic spell.");
                }
            });
            test("raise-dead-both-discard-piles", delegate {
                for (int scenario = 0; scenario < 3; scenario++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int raise = 1403985098, guard = 550907952;
                    Require(OriginalSpecRules.HasRestoredRules(raise), "Raise Dead spec missing: " + OriginalSpecRules.SkipReason(raise));
                    f.Create(Crab, me, GameZoneName.Cemetery);
                    if (scenario != 1) f.Create(guard, enemy, GameZoneName.Cemetery);
                    if (scenario == 2) for (int lane = 0; lane < 2; lane++) for (int n = 0; n < 4; n++) f.CreateIn(Crab, me, GameZoneName.CreatureBoard, lane);
                    ServerCard spell = f.Create(raise, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.Play(spell, null, me);
                    List<ServerCard> own = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Crab; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    List<ServerCard> theirs = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == guard; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    int enemyDiscard = s.CardCatalog.FindCardCount(GameZoneName.Cemetery, s.Players[enemy].NetId);
                    if (scenario == 2) Require(own.Count == 8 && theirs.Count == 0 && enemyDiscard == 1, "Full board lost or moved a discarded creature.");
                    else Require(own.Count == 1 && theirs.Count == (scenario == 0 ? 1 : 0) && enemyDiscard == 0,
                        "Raise Dead did not summon the expected creatures under the caster's control (scenario " + scenario + ").");
                }
            });
            test("blood-magic-lord-title-paid-spell", delegate {
                OfflineTitles.Publish(config, null);
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    OfflineTitles.AddProgress(d, "title_the_blood_magic_lord", 49);
                    OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    ServerCard ordinary = f.Create(Crab, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(ordinary, 0, me);
                    Require(!d.pendingTitleRewards.Contains("title_the_blood_magic_lord"), "An ordinary creature granted Blood Magic Lord.");
                    ServerCard spell = f.Create(-1879015996, me, GameZoneName.Hand); // Drain Life.
                    f.Refresh(me); f.Play(spell, null, me);
                    OfflineTitles.RecordProgress(s, d);
                    Require(OfflineTitles.Progress(d, "title_the_blood_magic_lord") == 50 && d.pendingTitleRewards.Contains("title_the_blood_magic_lord"),
                        "The 50th paid Blood Magic spell did not grant its title.");
                }
            });
            test("aundae-end-turn-counts-both-sides-and-resets", delegate {
                OfflineTitles.Publish(config, null);
                for (int kills = 2; kills <= 3; kills++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int sorcerer = 2019049161;
                    Require(OriginalSpecRules.HasRestoredRules(sorcerer), "Aundae spec missing: " + OriginalSpecRules.SkipReason(sorcerer));
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    OfflineTitles.AddProgress(d, "title_aundae_clan_bloodkin", 9);
                    OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    f.CreateIn(sorcerer, me, GameZoneName.CreatureBoard, 0);
                    for (int i = 0; i < kills; i++) Kill(f, f.CreateIn(Crab, i == 1 ? me : enemy, GameZoneName.CreatureBoard, 1));
                    int mine = s.Players[me].QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value;
                    int theirs = s.Players[enemy].QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value;
                    Require(mine == 1 && theirs == kills - 1, "Turn death counts differ: " + mine + "/" + theirs);
                    f.EndTurn();
                    Require(BloodSpells(s, me).Count == (kills == 3 ? 1 : 0), "Aundae threshold differed at " + kills + " deaths.");
                    OfflineTitles.RecordProgress(s, d);
                    Require(OfflineTitles.Progress(d, "title_aundae_clan_bloodkin") == (kills == 3 ? 10 : 9)
                        && d.pendingTitleRewards.Contains("title_aundae_clan_bloodkin") == (kills == 3), "Aundae title counter differed at " + kills + " deaths.");
                    Require(s.Players[me].QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value == 0
                        && s.Players[enemy].QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value == 0, "Death counts did not reset on the next turn.");
                    f.EndTurn(); f.EndTurn();
                    Require(BloodSpells(s, me).Count == (kills == 3 ? 1 : 0), "Earlier-turn deaths triggered Aundae again.");
                }
            });
            test("aundae-title-full-hand", delegate {
                OfflineTitles.Publish(config, null);
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    OfflineTitles.AddProgress(d, "title_aundae_clan_bloodkin", 9); OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    f.CreateIn(2019049161, me, GameZoneName.CreatureBoard, 0);
                    for (int i = 0; i < 3; i++) Kill(f, f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1));
                    while (f.HandCount(me) < s.Players[me].FindHandSize()) f.Create(Crab, me, GameZoneName.Hand);
                    f.EndTurn(); OfflineTitles.RecordProgress(s, d);
                    Require(OfflineTitles.Progress(d, "title_aundae_clan_bloodkin") == 9 && !d.pendingTitleRewards.Contains("title_aundae_clan_bloodkin"),
                        "A spell that did not enter a full hand counted toward Aundae's title.");
                }
            });
            test("stormcloak-avenger-friendly-deaths", delegate {
                for (int scenario = 0; scenario < 3; scenario++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int avenger = -662946703, token = -220910969;
                    Require(OriginalSpecRules.HasRestoredRules(avenger), "Stormcloak Avenger spec missing: " + OriginalSpecRules.SkipReason(avenger));
                    f.CreateIn(avenger, me, GameZoneName.CreatureBoard, 0);
                    for (int i = 0; i < 3; i++) Kill(f, f.CreateIn(Crab, scenario == 0 && i == 2 ? enemy : me, GameZoneName.CreatureBoard, 1));
                    if (scenario == 2) for (int i = 0; i < 3; i++) f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    f.EndTurn();
                    List<ServerCard> made = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == token; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(made.Count == (scenario == 1 ? 1 : 0), "Stormcloak Avenger generated the wrong number of tokens in scenario " + scenario);
                    if (made.Count == 1) Require(made[0].CurrentLaneIndex == 0 && made[0].QueryAttribute(AttributeType.Attack).Value == 2
                        && made[0].QueryAttribute(AttributeType.MaxHealth).Value == 2, "Rallying Stormcloak identity, lane, or stats differ.");
                }
            });
            test("vulture-and-stormcloak-camp-turn-death-condition", delegate {
                for (int death = 0; death <= 1; death++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int vultureId = 1099709816, campId = 2072942963;
                    Require(OriginalSpecRules.HasRestoredRules(vultureId) && OriginalSpecRules.HasRestoredRules(campId), "Turn-death card spec missing.");
                    f.Create(campId, me, GameZoneName.SupportBoard);
                    if (death != 0) Kill(f, f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1));
                    ServerCard vulture = f.Create(vultureId, me, GameZoneName.Hand);
                    int attack = vulture.QueryAttribute(AttributeType.Attack).Value, health = vulture.QueryAttribute(AttributeType.MaxHealth).Value;
                    int foeHealth = f.Avatar(enemy).CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(vulture, 0, me);
                    Require(vulture.QueryAttribute(AttributeType.Attack).Value == attack + 2 * death
                        && vulture.QueryAttribute(AttributeType.MaxHealth).Value == health + 2 * death, "Feasting Vulture death condition differs.");
                    f.EndTurn();
                    Require(f.Avatar(enemy).CurrentHealth == foeHealth - 2 * death, "Stormcloak Camp death condition differs.");
                }
            });
            test("death-scaled-vampire-and-scythe", delegate {
                for (int scenario = 0; scenario < 3; scenario++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int vampireId = -229007472, scytheId = 1801637084, leviathan = 1427457192;
                    Require(OriginalSpecRules.HasRestoredRules(vampireId) && OriginalSpecRules.HasRestoredRules(scytheId), "Death-scaled specs missing.");
                    int deaths = scenario == 0 ? 0 : 3;
                    if (deaths > 0) { s.Players[me].SetAttribute(AttributeType.CreaturesDeadThisTurn, 1); s.Players[enemy].SetAttribute(AttributeType.CreaturesDeadThisTurn, 2); }
                    ServerCard target = f.CreateIn(leviathan, enemy, GameZoneName.CreatureBoard, 0);
                    if (scenario == 2) f.Event(delegate { CardActions.Ward.StaticExecuteSingleTarget(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, target, -1); });
                    int targetHealth = target.CurrentHealth;
                    ServerCard vampire = f.CreateIn(vampireId, me, GameZoneName.CreatureBoard, 0);
                    int attack = vampire.QueryAttribute(AttributeType.Attack).Value, health = vampire.QueryAttribute(AttributeType.MaxHealth).Value;
                    int enchantments = vampire.Enchantments.Count;
                    f.Event(delegate { ServerEffectUtilities.TriggerDirectEffectEvent(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                        new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Summon, vampire, s.Players)); });
                    f.DrainEffects();
                    List<ServerCard> boardVampires = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == vampireId; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    List<string> vampires = new List<string>();
                    foreach (ServerCard c in s.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.CardId == vampireId; }))
                        vampires.Add(c.CurrentGameZoneName + "/" + c.OwnerNetId.Id + "/" + c.QueryAttribute(AttributeType.Attack).Value + "/" + c.CurrentHealth);
                    Require(boardVampires.Count == 1 && boardVampires[0].QueryAttribute(AttributeType.Attack).Value == attack + deaths && boardVampires[0].QueryAttribute(AttributeType.MaxHealth).Value == health + deaths,
                        "Brotherhood Vampire scaling differs at " + deaths + " deaths; board count " + boardVampires.Count + ", instances " + string.Join(",", vampires.ToArray())
                        + " versus base " + attack + "/" + health + ", counts " + s.Players[me].QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value
                        + "/" + s.Players[enemy].QueryAttribute(AttributeType.CreaturesDeadThisTurn).Value);
                    if (deaths == 0) Require(boardVampires[0].Enchantments.Count == enchantments, "Zero deaths added a phantom buff enchantment.");
                    ServerCard scythe = f.Create(scytheId, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.Play(scythe, target, me);
                    Require(target.CurrentHealth == targetHealth - (scenario == 1 ? deaths : 0), "Death Scythe damage or Ward differs in scenario " + scenario);
                    if (scenario == 2) Require(target.QueryAttribute(AttributeType.Ward).Value == 0, "Death Scythe did not consume Ward.");
                }
            });
            test("death-scaled-paid-after-actual-deaths", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int vampireId = -229007472, scytheId = 1801637084, leviathan = 1427457192;
                    ServerCard vampire = f.Create(vampireId, me, GameZoneName.Hand);
                    ServerCard scythe = f.Create(scytheId, me, GameZoneName.Hand);
                    ServerCard target = f.CreateIn(leviathan, enemy, GameZoneName.CreatureBoard, 0);
                    int attack = vampire.QueryAttribute(AttributeType.Attack).Value, health = vampire.QueryAttribute(AttributeType.MaxHealth).Value;
                    f.Mana(me, 30); f.Refresh(me);
                    for (int i = 0; i < 3; i++) Kill(f, f.CreateIn(Crab, i == 1 ? me : enemy, GameZoneName.CreatureBoard, 1));
                    Require(s._turnStateStack.Peek().ActivePlayerIndex == me, "Forced deaths changed the active player.");
                    f.Refresh(me); f.PlayCreatureLane(vampire, 0, me);
                    List<ServerCard> board = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == vampireId; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(board.Count == 1 && board[0].QueryAttribute(AttributeType.Attack).Value == attack + 3 && board[0].QueryAttribute(AttributeType.MaxHealth).Value == health + 3,
                        "Paid Vampire did not receive the three-death buff.");
                    int targetHealth = target.CurrentHealth; f.Refresh(me); f.Play(scythe, target, me);
                    Require(target.CurrentHealth == targetHealth - 3, "Paid Death Scythe did not deal three damage after real deaths.");
                }
            });
            test("blademaster-porcia-enemy-death-activation", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int porciaId = 2128326289;
                    Require(OriginalSpecRules.HasRestoredRules(porciaId), "Porcia spec missing: " + OriginalSpecRules.SkipReason(porciaId));
                    ServerCard porcia = f.Create(porciaId, me, GameZoneName.SupportBoard);
                    f.Mana(me, 30); f.Refresh(me);
                    Require(!CanActivate(s, porcia), "Porcia was offered before any enemy died.");
                    Kill(f, f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 1)); f.Refresh(me);
                    Require(!CanActivate(s, porcia), "A friendly death enabled Porcia.");
                    Kill(f, f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1)); f.Refresh(me);
                    Require(CanActivate(s, porcia), "An enemy death did not enable Porcia.");
                    Decision activation = new Decision { Type = CcgRpc.DecisionType.Activate, ActorCardNetUid = porcia.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id };
                    Require(PlayCardHandler.ActivateCardIsValid(s, s.Players[me], activation), "Porcia is offered but native activation validation rejects it.");
                    f.Event(delegate { f.Avatar(me).SetAvatarHealthPreGame(29); });
                    f.Refresh(me); f.Activate(porcia, null, me);
                    f.Until(delegate { return porcia.NumTimesActivated > 0 && OriginalTargetedSummonTests.Ready(s); });
                    Require(f.Avatar(me).CurrentHealth == 30 && s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == 1150098037; },
                        s.Players[me].NetId, GameZoneName.CreatureBoard).Count == 1, "Queued Porcia activation after an actual enemy death had wrong effects.");
                }
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int porciaId = 2128326289, gruntId = 1150098037;
                    ServerCard porcia = f.Create(porciaId, me, GameZoneName.SupportBoard);
                    s.Players[enemy].SetAttribute(AttributeType.CreaturesDeadThisTurn, 1);
                    f.Event(delegate { f.Avatar(me).SetAvatarHealthPreGame(29); });
                    f.Refresh(me);
                    CardCommandList commands = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision activation = new Decision { Type = CcgRpc.DecisionType.Activate, CommandUid = commands.ListUid,
                        ActorCardNetUid = porcia.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id };
                    Require(CommandRestoration.ValidateDecision(activation, commands, s.CardCatalog, s.Players[me])
                        && PlayCardHandler.ActivateCardIsValid(s, s.Players[me], activation), "Porcia activation validation failed.");
                    Simulation.SelectDecision(s._turnStateStack.Peek(), activation);
                    f.Until(delegate { return porcia.NumTimesActivated > 0 && OriginalTargetedSummonTests.Ready(s); });
                    List<ServerCard> grunts = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == gruntId; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(grunts.Count == 1 && f.Avatar(me).CurrentHealth == 30, "Porcia result: grunts=" + grunts.Count + ", health=" + f.Avatar(me).CurrentHealth
                        + ", support zone=" + porcia.CurrentGameZoneName + ", uses=" + porcia.NumTimesActivated);
                }
            });
            test("blademaster-porcia-queued-activation", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard porcia = f.Create(2128326289, me, GameZoneName.SupportBoard);
                    s.Players[enemy].SetAttribute(AttributeType.CreaturesDeadThisTurn, 1);
                    f.Event(delegate { f.Avatar(me).SetAvatarHealthPreGame(29); });
                    f.Mana(me, 30); f.Refresh(me); f.Activate(porcia, null, me);
                    f.Until(delegate { return porcia.NumTimesActivated > 0 && OriginalTargetedSummonTests.Ready(s); });
                    Require(porcia.NumTimesActivated == 1 && f.Avatar(me).CurrentHealth == 30
                        && s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == 1150098037; }, s.Players[me].NetId, GameZoneName.CreatureBoard).Count == 1,
                        "Queued Porcia activation did not execute its effects once.");
                }
            });
            test("flesh-atronach-counts-own-discard-creatures", delegate {
                for (int count = 0; count <= 3; count += 3)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    const int flesh = -355724207;
                    Require(OriginalSpecRules.HasRestoredRules(flesh), "Flesh Atronach spec missing: " + OriginalSpecRules.SkipReason(flesh));
                    ServerCard source = f.Create(flesh, me, GameZoneName.Hand);
                    for (int i = 0; i < count; i++) f.Create(Crab, me, GameZoneName.Cemetery);
                    f.Create(Crab, enemy, GameZoneName.Cemetery); f.Create(Crab, enemy, GameZoneName.Cemetery);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(source, 0, me);
                    f.Until(delegate { return s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == flesh; }, s.Players[me].NetId, GameZoneName.CreatureBoard).Count == 1; });
                    List<ServerCard> board = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == flesh; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(board[0].QueryAttribute(AttributeType.Attack).Value == 1 + count && board[0].QueryAttribute(AttributeType.MaxHealth).Value == 1 + count,
                        "Flesh Atronach counted the wrong discard creatures at " + count);
                }
            });
            test("flesh-atronach-title-after-summon-buff", delegate {
                OfflineTitles.Publish(config, null);
                for (int discarded = 18; discarded <= 19; discarded++)
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    OfflinePlayerStateData d = new OfflinePlayerStateData(); OfflineTitles.AddActiveQuests(s, d, s.Players[me]);
                    ServerCard flesh = f.Create(-355724207, me, GameZoneName.Hand);
                    for (int i = 0; i < discarded; i++) f.Create(Crab, me, GameZoneName.Cemetery);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(flesh, 0, me);
                    List<ServerCard> board = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == -355724207; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(board.Count == 1 && board[0].QueryAttribute(AttributeType.Attack).Value == 1 + discarded, "Flesh Atronach did not reach expected power.");
                    OfflineTitles.RecordProgress(s, d);
                    Require(d.pendingTitleRewards.Contains("title_the_master_of_flesh") == (discarded == 19), "Flesh Atronach title threshold differed at " + discarded + " discarded creatures.");
                }
            });
            test("discard-copy-counts-buff-and-draw", delegate {
                foreach (int id in new[] { 1812125842, 916842555 })
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(id), "Discard-copy card spec missing: " + OriginalSpecRules.SkipReason(id));
                    ServerCard source = f.Create(id, me, GameZoneName.Hand);
                    int attack = source.QueryAttribute(AttributeType.Attack).Value, health = source.QueryAttribute(AttributeType.MaxHealth).Value;
                    f.Create(id, me, GameZoneName.Cemetery); f.Create(id, me, GameZoneName.Cemetery);
                    f.Create(id, enemy, GameZoneName.Cemetery); f.Create(Crab, me, GameZoneName.Cemetery);
                    int deck = f.DeckCount(me), hand = f.HandCount(me);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(source, 0, me);
                    List<ServerCard> board = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == id; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(board.Count == 1, "Discard-copy card did not enter the board: " + id);
                    if (id == 1812125842)
                        Require(board[0].QueryAttribute(AttributeType.Attack).Value == attack + 8 && board[0].QueryAttribute(AttributeType.MaxHealth).Value == health + 8,
                            "Spiny Haj Mota did not count exactly two friendly copies.");
                    else Require(f.DeckCount(me) == deck - 2 && f.HandCount(me) == hand + 1,
                        "Gray Viper Brigand did not draw exactly two cards for friendly copies.");
                }
            });
            test("draw-observers-owned-turn-and-generated-hand-card", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Librarian) && OriginalSpecRules.HasRestoredRules(Fence), "Draw observer specs missing.");
                    ServerCard librarian = f.Create(Librarian, me, GameZoneName.CreatureBoard);
                    f.Create(Fence, me, GameZoneName.CreatureBoard);
                    ServerCard generated = f.Create(Crab, me, GameZoneName.Hand);
                    f.DrainEffects();
                    Require(librarian.QueryAttribute(AttributeType.Attack).Value == 0 && generated.QueryAttribute(AttributeType.ManaCost).Value == 1,
                        "Creating a card in hand incorrectly counted as drawing it.");
                    List<ServerCard> tokens = null;
                    f.Event(delegate {
                        GameZone.Position position = s.CardCatalog.FindInsertionPosition(s.Players[me].NetId, GameZoneName.Hand, 0, CardInsertionType.Bottom);
                        tokens = PlayerActions.CreateToken.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null,
                            config.FindCardById(Crab), false, GameZoneType.Hand, position, s.Players[me].NetId, 1, -1, null);
                    });
                    f.DrainEffects();
                    Require(tokens != null && tokens.Count == 1 && tokens[0].QueryAttribute(AttributeType.ManaCost).Value == 1
                        && librarian.QueryAttribute(AttributeType.Attack).Value == 0, "Effect-created hand token incorrectly counted as a draw.");
                    List<ServerCard> drawn = Draw(f, me);
                    Require(drawn.Count == 1 && drawn[0].QueryAttribute(AttributeType.ManaCost).Value == 0
                        && librarian.QueryAttribute(AttributeType.Attack).Value == 1 && generated.QueryAttribute(AttributeType.ManaCost).Value == 1,
                        "Friendly draw did not buff Librarian and discount only the drawn card.");
                    Draw(f, enemy);
                    Require(librarian.QueryAttribute(AttributeType.Attack).Value == 1, "Opponent draw triggered Librarian.");
                    ServerCard discarded = f.Create(Crab, me, GameZoneName.Cemetery);
                    f.Event(delegate { CardActions.ReturnToHand.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                        null, discarded, s.Players[me], false, false, false, -1, false, false); });
                    f.DrainEffects();
                    Require(discarded.QueryAttribute(AttributeType.ManaCost).Value == 0 && librarian.QueryAttribute(AttributeType.Attack).Value == 2,
                        "Drawing a card from discard did not trigger the draw observers.");
                    f.EndTurn();
                    drawn = Draw(f, me);
                    Require(drawn.Count == 1 && drawn[0].QueryAttribute(AttributeType.ManaCost).Value == 1
                        && librarian.QueryAttribute(AttributeType.Attack).Value == 2, "Friendly draw on opponent's turn triggered effects.");
                }
            });
            test("waiting-generator-enemy-death-lane-and-turn", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(Generator), "Generator spec missing.");
                    f.CreateIn(Generator, me, GameZoneName.CreatureBoard, 0);
                    ServerCard friend = f.CreateIn(Crab, me, GameZoneName.CreatureBoard, 0);
                    ServerCard wrongLane = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 1);
                    ServerCard rightLane = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    int max = MaxMana(f, me);
                    Kill(f, friend); Kill(f, wrongLane);
                    Require(MaxMana(f, me) == max, "Generator triggered for a friendly or other-lane death.");
                    Kill(f, rightLane);
                    Require(MaxMana(f, me) == max + 1, "Enemy death in Generator's lane did not grant exactly one max magicka.");
                    f.EndTurn(); max = MaxMana(f, me);
                    ServerCard opponentTurn = f.CreateIn(Crab, enemy, GameZoneName.CreatureBoard, 0);
                    Kill(f, opponentTurn);
                    Require(MaxMana(f, me) == max, "Generator triggered on the opponent's turn.");
                }
            });
            test("draw-copy-triggers-observers-without-moving-original", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard librarian = f.Create(Librarian, me, GameZoneName.CreatureBoard);
                    f.Create(Fence, me, GameZoneName.CreatureBoard);
                    ServerCard original = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[enemy].NetId, GameZoneName.Deck)[0];
                    int enemyDeck = f.DeckCount(enemy), hand = f.HandCount(me);
                    ServerCard copy = null;
                    f.Event(delegate { copy = CardActions.ReturnToHand.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                        null, original, s.Players[me], true, false, false, -1, false, false); });
                    f.DrainEffects();
                    Require(copy != null && copy != original && copy.CardId == original.CardId && copy.OwnerNetId == s.Players[me].NetId
                        && original.CurrentGameZoneName == GameZoneName.Deck && f.DeckCount(enemy) == enemyDeck && f.HandCount(me) == hand + 1
                        && copy.QueryAttribute(AttributeType.ManaCost).Value == 0 && librarian.QueryAttribute(AttributeType.Attack).Value == 1,
                        "Drawing a copy did not trigger exactly once, discount the copy, or preserve the original.");
                    while (f.HandCount(me) < s.Players[me].FindHandSize()) f.Create(Crab, me, GameZoneName.Hand);
                    f.DrainEffects();
                    ServerCard skipped = null;
                    f.Event(delegate { skipped = CardActions.ReturnToHand.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()),
                        null, original, s.Players[me], true, false, true, -1, false, false); });
                    f.DrainEffects();
                    Require(skipped == null && librarian.QueryAttribute(AttributeType.Attack).Value == 1 && f.DeckCount(enemy) == enemyDeck,
                        "A copy skipped due to a full hand incorrectly fired a draw event.");
                }
            });
            test("orb-of-vaermina-paid-play-and-three-copy-activations", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    Require(OriginalSpecRules.HasRestoredRules(1519628395), "Orb spec missing: " + OriginalSpecRules.SkipReason(1519628395));
                    ServerCard librarian = f.Create(Librarian, me, GameZoneName.CreatureBoard);
                    f.Create(Fence, me, GameZoneName.CreatureBoard);
                    ServerCard orb = f.Create(1519628395, me, GameZoneName.Hand);
                    List<ServerCard> opposingDeck = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[enemy].NetId, GameZoneName.Deck);
                    f.Event(delegate { foreach (ServerCard card in opposingDeck) ServerCardCatalog.MoveToPurgatory(DecisionRestoration.State(s, s._turnStateStack.Peek()), card); });
                    f.Create(1427457192, enemy, GameZoneName.Deck); // Opponent's only card differs from every card in our deck.
                    int deck = f.DeckCount(enemy), mana = 30, cost = orb.QueryAttribute(AttributeType.ManaCost).Value;
                    f.Mana(me, mana); f.Refresh(me); f.Play(orb, null, me);
                    Require(orb.CurrentGameZoneName == GameZoneName.SupportBoard && s.Players[me].QueryAttribute(AttributeType.Mana).Value == mana - cost,
                        "Orb was not paid for and placed as a support.");
                    for (int use = 1; use <= 3; use++)
                    {
                        if (use > 1) f.Event(delegate {
                            ServerEffectUtilities.TriggerGameState state = DecisionRestoration.State(s, s._turnStateStack.Peek());
                            orb.OnEndTurn(s.Players[me].NetId, state); orb.OnStartTurn(s.Players[me].NetId, state);
                        });
                        HashSet<int> before = new HashSet<int>();
                        foreach (ServerCard card in s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[me].NetId, GameZoneName.Hand)) before.Add(card.NetUid.Id);
                        f.Refresh(me); f.Activate(orb, null, me);
                        List<ServerCard> copies = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return !before.Contains(c.NetUid.Id); }, s.Players[me].NetId, GameZoneName.Hand);
                        Require(copies.Count == 1 && copies[0].CardId == 1427457192 && copies[0].QueryAttribute(AttributeType.ManaCost).Value == 6
                            && f.DeckCount(enemy) == deck && librarian.QueryAttribute(AttributeType.Attack).Value == use && (use == 3 || orb.TotalAbilityUse == use),
                            "Orb activation mismatch use=" + use + ", copies=" + copies.Count
                            + ", cost=" + (copies.Count == 1 ? copies[0].QueryAttribute(AttributeType.ManaCost).Value : -1)
                            + ", enemy deck=" + f.DeckCount(enemy) + "/" + deck + ", librarian=" + librarian.QueryAttribute(AttributeType.Attack).Value
                            + ", total uses=" + orb.TotalAbilityUse + ", zone=" + orb.CurrentGameZoneName);
                        if (use == 1)
                        {
                            CardCommandState command = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, orb);
                            Require(command == null || (command.ValidCardCommandMask & (int)CardCommand.ActiveAbility) == 0, "Orb activated twice in one turn.");
                        }
                    }
                    Require(orb.CurrentGameZoneName != GameZoneName.SupportBoard, "Orb remains after its third use.");
                }
            });
        }
        private static List<ServerCard> BloodSpells(Simulation s, int player)
        { return s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Blood_Magic); }, s.Players[player].NetId, GameZoneName.Hand); }
        private static bool CanActivate(Simulation s, ServerCard card)
        {
            CardCommandState command = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, card);
            return command != null && (command.ValidCardCommandMask & (int)CardCommand.ActiveAbility) != 0;
        }
        private static List<ServerCard> Draw(OriginalTargetedSummonTests.LifecycleFixture f, int player)
        {
            List<ServerCard> result = null;
            f.Event(delegate { result = PlayerActions.DrawCards.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                f.Sim.Players[player], 1, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, false, -1); });
            f.DrainEffects(); return result;
        }
        private static void Kill(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard target)
        {
            f.Event(delegate { CardActions.Destroy.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()),
                new List<ServerCard> { target }, null, -1, false, null); });
            for (int i = 0; i < 24; i++) LifecycleRestoration.FixedUpdate(f.Sim);
            f.Until(delegate { return OriginalTargetedSummonTests.Ready(f.Sim); });
        }
        private static int MaxMana(OriginalTargetedSummonTests.LifecycleFixture f, int owner) { return f.Avatar(owner).QueryAttribute(AttributeType.MaxMana).Value; }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
