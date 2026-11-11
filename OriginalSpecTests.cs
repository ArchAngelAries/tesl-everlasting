using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    // Generic verification of every card emitted from the compiled text spec.
    public static class OriginalSpecTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069;
        private const int QuicksilverRelic = -1522132779, ImperialMight = 1084295204, Calm = -1143757804, LillandrilHexmage = 314822657,
            BrumaProfiteer = -835052994, GuildswornWayfarer = 1544252068;

        public static void Run(GameConfiguration config, Action<string,Action> test, string output)
        {
            OriginalTokenIdentityTests.Run(config, test);
            OriginalLegateTests.Run(config, test);
            IceSpikeTests.Run(config, test);
            SupportSummonTests.Run(config, test);
            CorsairShipTests.Run(config, test);
            EquipmentChoiceTests.Run(config, test);
            SelfTargetSummonTests.Run(config, test);
            OtherTargetSummonTests.Run(config, test);
            RestrictedSummonTests.Run(config, test);
            HandAndHealSummonTests.Run(config, test);
            BattleStealSummonTests.Run(config, test);
            GourmetSummonTests.Run(config, test);
            SweetRollTests.Run(config, test);
            AfterAttackTests.Run(config, test);
            FriendlyEquipmentTests.Run(config, test);
            FriendlyAttackTests.Run(config, test);
            AttackTimingTests.Run(config, test);
            ChargeAttackTests.Run(config, test);
            DuneSmugglerTests.Run(config, test);
            DrawAndDeathEventTests.Run(config, test);
            EventValueTests.Run(config, test);
            SurviveDamageTests.Run(config, test);
            OriginalGraphTests.Run(config, test, output);
            test("spec-compiled-cards-native-execution-smoke", delegate {
                StringBuilder report = new StringBuilder("card_id\tname\tnative_smoke\tdetail\n");
                List<string> failures = new List<string>();
                int count = 0;
                foreach (int id in new List<int>(OriginalSpecRules.PreparedIds))
                {
                    count++;
                    Card card = config.FindCardById(id);
                    string result = "PASS", detail = "";
                    try { detail = Smoke(config, card); }
                    catch (Exception e) { result = "FAIL"; detail = e.GetType().Name + ": " + e.Message; failures.Add(id + " " + card.Name + ": " + detail); }
                    report.AppendLine(id + "\t" + card.Name + "\t" + result + "\t" + detail.Replace("\t", " ").Replace("\n", " "));
                }
                File.WriteAllText(Path.Combine(output, "spec-smoke-results.tsv"), report.ToString());
                Require(count > 0, "No compiled spec cards were emitted.");
                Require(failures.Count == 0, failures.Count + " of " + count + " compiled cards failed native smoke: " + string.Join(" | ", failures.GetRange(0, Math.Min(8, failures.Count)).ToArray()));
            });

            test("spec-compiled-cards-real-paid-play", delegate {
                List<string> failures = new List<string>();
                StringBuilder report = new StringBuilder("card_id\tname\tpaid_play\tdetail\n");
                foreach (int id in new List<int>(OriginalSpecRules.PreparedIds))
                {
                    Card card = config.FindCardById(id);
                    string result = "PASS", detail = "";
                    try { detail = PaidPlay(config, card); }
                    catch (Exception e) { result = "FAIL"; detail = e.GetType().Name + ": " + e.Message; failures.Add(id + " " + card.Name + ": " + detail); }
                    report.AppendLine(id + "\t" + card.Name + "\t" + result + "\t" + detail.Replace("\t", " ").Replace("\n", " "));
                }
                File.WriteAllText(Path.Combine(output, "spec-paid-play-results.tsv"), report.ToString());
                Require(failures.Count == 0, failures.Count + " compiled cards failed real paid play: " + string.Join(" | ", failures.GetRange(0, Math.Min(8, failures.Count)).ToArray()));
            });

            // Steal through a real play: Miraak takes control of the chosen enemy creature in its own lane.
            test("steal-chosen-enemy-real-lifecycle", delegate {
                const int Miraak = -1874170560; // Summon: Steal an enemy creature.
                Require(OriginalSpecRules.HasRestoredRules(Miraak), "Miraak was not emitted: " + OriginalSpecRules.SkipReason(Miraak));
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, them = 1 - me;
                    ServerCard victim = f.CreateIn(Leviathan, them, GameZoneName.CreatureBoard, 1);
                    ServerCard miraak = f.Create(Miraak, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    CardCommandState command = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, miraak);
                    Require(command != null && (command.ValidCardCommandMask & (int)CardCommand.PlayToPosition) != 0,
                        "Miraak was not offered as a playable creature.");
                    // The Summon target is selected in the child decision after placement; the
                    // fixture answers that prompt with its sole eligible enemy creature.
                    f.PlayCreatureLane(miraak, 0, me);
                    Require(victim.OwnerNetId == s.Players[me].NetId && victim.CurrentGameZoneName == GameZoneName.CreatureBoard && victim.CurrentLaneIndex == 1,
                        "Stolen creature is not under the player's control in its own lane (owner " + victim.OwnerNetId.Id + ", zone " + victim.CurrentGameZoneName + ", lane " + victim.CurrentLaneIndex + ").");
                }
            });

            // Event-trigger families through the real lifecycle (attacks, combat damage, deaths, rune breaks, summons, draws).
            test("event-trigger-families-real-lifecycle", delegate {
                const int FieryImp = 1193892832, Northlander = 806805335, GrimChampion = 1329094148, Executioner = -433251899,
                    BrumaArmorer = 1239513645, EchoOfAkatosh = -386295049;
                foreach (int id in new int[] { FieryImp, Northlander, GrimChampion, Executioner, BrumaArmorer, EchoOfAkatosh })
                    Require(OriginalSpecRules.HasRestoredRules(id), "Event fixture card was not emitted: " + id + " " + OriginalSpecRules.SkipReason(id));
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, them = 1 - me;
                    ServerCard imp = f.CreateIn(FieryImp, me, GameZoneName.CreatureBoard, 1);
                    ServerCard northlander = f.CreateIn(Northlander, me, GameZoneName.CreatureBoard, 0);
                    ServerCard grim = f.CreateIn(GrimChampion, me, GameZoneName.CreatureBoard, 0);
                    ServerCard crab = f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                    ServerCard executioner = f.CreateIn(Executioner, me, GameZoneName.CreatureBoard, 1);
                    ServerCard armorer = f.CreateIn(BrumaArmorer, me, GameZoneName.CreatureBoard, 1); // a creature
                    f.Create(EchoOfAkatosh, me, GameZoneName.SupportBoard);
                    ServerCard wall = f.CreateIn(Leviathan, them, GameZoneName.CreatureBoard, 0);
                    f.EndTurn(); f.EndTurn(); // wake the creatures; my turn-start draw happens here (Echo of Akatosh)
                    Require(s._turnStateStack.Peek().ActivePlayerIndex == me, "Turn order did not return to the tested player.");
                    List<ServerCard> drawn = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Mudcrab && c.Enchantments.Count > 0; }, s.Players[me].NetId, GameZoneName.Hand);
                    Require(drawn.Count >= 1, "Echo of Akatosh did not give the drawn creature a random keyword.");
                    // Bruma Armorer: a played creature gets +0/+2.
                    ServerCard summoned = f.Create(Mudcrab, me, GameZoneName.Hand);
                    f.DrainEffects(); // the injected hand card can queue draw-event triggers (Echo of Akatosh); settle them before deciding
                    f.Mana(me, 30); f.Refresh(me);
                    f.Play(summoned, null, me);
                    Require(summoned.QueryAttribute(AttributeType.MaxHealth).Value == 3, // Mudcrab 2/1 + 0/+2
                        "Bruma Armorer did not give the summoned creature +0/+2: crab " + summoned.QueryAttribute(AttributeType.Attack).Value + "/" + summoned.QueryAttribute(AttributeType.MaxHealth).Value
                        + " zone " + summoned.CurrentGameZoneName + " enchantments " + summoned.Enchantments.Count + "; armorer zone " + armorer.CurrentGameZoneName + " triggers "
                        + string.Join(",", armorer.TriggeredEffects.ConvertAll(delegate(TriggeredEffect t) { return t.TriggerType + "/" + t.SortOrder + "/req" + t.TriggerCardRequirements.Count; }).ToArray()));


                    int imPower = imp.QueryAttribute(AttributeType.Attack).Value, enemy = f.Avatar(them).CurrentHealth;
                    f.Attack(imp, f.Avatar(them), me);
                    Require(f.Avatar(them).CurrentHealth == enemy - imPower - 2, "Fiery Imp's attack trigger did not deal 2 extra damage: " + enemy + " -> " + f.Avatar(them).CurrentHealth);

                    int northPower = northlander.QueryAttribute(AttributeType.Attack).Value;
                    f.Attack(northlander, wall, me);
                    Require(northlander.CurrentGameZoneType != GameZoneType.Board || northlander.QueryAttribute(AttributeType.Attack).Value == northPower + 2,
                        "Fearless Northlander did not gain +2/+0 when it took damage.");

                    int grimPower = grim.QueryAttribute(AttributeType.Attack).Value;
                    f.Attack(crab, wall, me); // the Mudcrab dies to the Leviathan in Grim Champion's lane
                    Require(crab.CurrentGameZoneType != GameZoneType.Board && grim.QueryAttribute(AttributeType.Attack).Value >= grimPower + 1,
                        "Grim Champion did not gain +1/+1 when a creature in its lane died.");

                    // Bring the enemy just above a rune threshold, then break it with a real attack.
                    int execPower = executioner.QueryAttribute(AttributeType.Attack).Value;
                    f.Event(delegate { f.Avatar(them).ModifyCurrentHealth(-(f.Avatar(them).CurrentHealth - 21), null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    f.Attack(executioner, f.Avatar(them), me);
                    Require(executioner.QueryAttribute(AttributeType.Attack).Value == execPower + 2, "Morthal Executioner did not gain +2/+0 when an enemy rune was destroyed (health "
                        + f.Avatar(them).CurrentHealth + ", runes lost " + s.Players[them].QueryAttribute(AttributeType.NumRunesLost).Value + ").");

                }
            });

            // Special attack rules from installed attributes (previously refused): Serpentine Stalker moves to attack the other lane,
            // Cicero may attack friendly creatures, Cadwell the Betrayer may attack its own player. Real attack decisions.
            test("special-attack-rules-move-friendly-own-face-real-lifecycle", delegate {
                const int Serpentine = -1253488284, Cicero = 785327970, Cadwell = 2025045762;
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, them = 1 - me;
                    ServerCard stalker = f.CreateIn(Serpentine, me, GameZoneName.CreatureBoard, 0);
                    ServerCard cicero = f.CreateIn(Cicero, me, GameZoneName.CreatureBoard, 0);
                    ServerCard cadwell = f.CreateIn(Cadwell, me, GameZoneName.CreatureBoard, 1);
                    ServerCard far = f.CreateIn(Mudcrab, them, GameZoneName.CreatureBoard, 1);
                    f.EndTurn(); f.EndTurn(); // wake the creatures through real turns
                    Require(s._turnStateStack.Peek().ActivePlayerIndex == me, "Turn order did not return to the attacker.");
                    PlayerState mine = s.Players[me], theirs = s.Players[them];
                    List<CardNetUid> stalkerTargets = CommandRestoration.AttackTargets(s.CardCatalog, mine, theirs, stalker, false);
                    Require(stalkerTargets.Contains(far.NetUid), "Serpentine Stalker was not offered the creature in the other lane.");
                    List<CardNetUid> ciceroTargets = CommandRestoration.AttackTargets(s.CardCatalog, mine, theirs, cicero, false);
                    Require(ciceroTargets.Contains(stalker.NetUid) && !ciceroTargets.Contains(cadwell.NetUid), "Cicero friendly-attack targets differ (same lane only).");
                    List<CardNetUid> cadwellTargets = CommandRestoration.AttackTargets(s.CardCatalog, mine, theirs, cadwell, false);
                    Require(cadwellTargets.Contains(f.Avatar(me).NetUid), "Cadwell the Betrayer was not offered its own player.");
                    f.Attack(stalker, far, me);
                    Require(stalker.CurrentLaneIndex == 1 && far.CurrentGameZoneType != GameZoneType.Board, "Serpentine Stalker did not move into the other lane and destroy the Mudcrab.");
                    int ownHealth = f.Avatar(me).CurrentHealth;
                    f.Attack(cadwell, f.Avatar(me), me);
                    Require(f.Avatar(me).CurrentHealth == ownHealth - cadwell.QueryAttribute(AttributeType.Attack).Value, "Cadwell's attack on its own player did not deal its power.");
                }
            });

            // Triggered player-chosen target through the real lifecycle: Mantikora's lane-relative Summon choice is a native
            // ChooseTarget decision after placement offering only enemies in its lane; the chosen creature is destroyed.
            test("triggered-chosen-target-lane-relative-summon-real-lifecycle", delegate {
                const int Mantikora = 735287286; // Summon: Destroy an enemy creature in this lane.
                Require(OriginalSpecRules.HasRestoredRules(Mantikora), "Mantikora was not emitted: " + OriginalSpecRules.SkipReason(Mantikora));
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    f.AutoAnswerTargets = false;
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, them = 1 - me;
                    ServerCard sameLane = f.CreateIn(Leviathan, them, GameZoneName.CreatureBoard, 0), otherLane = f.CreateIn(Leviathan, them, GameZoneName.CreatureBoard, 1);
                    ServerCard source = f.Create(Mantikora, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision play = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = source.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id, LaneIndex = 0 };
                    Require(CommandRestoration.ValidateDecision(play, list, s.CardCatalog, s.Players[me]), "Mantikora play command is invalid.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(play);
                    f.Until(delegate { return s._turnStateStack.Count == 2 && s._turnStateStack.Peek().CurrentCommandItem != null; });
                    CardCommandList choose = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    CardCommandState offer = choose.ValidCommands.Count == 1 ? choose.ValidCommands[0] : null;
                    Require(offer != null && (offer.ValidCardCommandMask & (int)CardCommand.ChooseTarget) != 0 && offer.PlayTargets.Count == 1 && offer.PlayTargets[0] == sameLane.NetUid.Id,
                        "Lane-relative Summon choice did not offer exactly the enemy in Mantikora's lane.");
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = choose.ListUid, ActorCardNetUid = offer.CardNetUid,
                        TargetCardNetUid = sameLane.NetUid.Id, EquipCardNetUid = CardNetUid.GetInvalid().Id });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    Require(sameLane.CurrentGameZoneType != GameZoneType.Board && otherLane.CurrentGameZoneType == GameZoneType.Board,
                        "Mantikora did not destroy exactly the chosen same-lane enemy.");
                }
            });

            // Invade + reconstructed Oblivion Gate levels through real plays, and the summon rule (a creature that enters play
            // other than by being played fires its own Summon: here an Invasion Scout entering the board Invades).
            test("invade-oblivion-gate-levels-and-effect-summon-real-lifecycle", delegate {
                const int Vanguard = 1759297880, Marauder = -499614382, Scout = 1285002775, Scamp = 175622366, Dremora = 76505525;
                foreach (int id in new int[] { Vanguard, Marauder, Scout })
                    Require(OriginalSpecRules.HasRestoredRules(id), "Invade fixture card was not emitted: " + id + " " + OriginalSpecRules.SkipReason(id));
                Require(RuleImporter.IsSupported(OriginalOblivionGateRules.Gate), "Oblivion Gate graph was not restored.");
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    Func<ServerCard> gate = delegate { List<ServerCard> g = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == OriginalOblivionGateRules.Gate; }, s.Players[me].NetId, GameZoneName.CreatureBoard); return g.Count == 1 ? g[0] : null; };
                    Action<int> play = delegate(int id) { ServerCard c = f.Create(id, me, GameZoneName.Hand); f.Mana(me, 30); f.Refresh(me); f.Play(c, null, me); };

                    play(Vanguard);
                    ServerCard g1 = gate();
                    Require(g1 != null && g1.CurrentLaneIndex == 1 && g1.QueryAttribute(AttributeType.OblivionGateLevel).Value == 1 && g1.CurrentHealth == 4,
                        "First Invade did not open a level 1 0/4 Oblivion Gate in the right lane.");
                    ServerCard scamp = null; play(Scamp);
                    scamp = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Scamp; }, s.Players[me].NetId, GameZoneName.CreatureBoard)[0];
                    Require(scamp.QueryAttribute(AttributeType.Attack).Value == 1 && scamp.QueryAttribute(AttributeType.MaxHealth).Value == 2, "Level 1 Gate did not give a summoned Daedra +0/+1.");

                    play(Marauder);
                    Require(gate() == g1 && g1.QueryAttribute(AttributeType.OblivionGateLevel).Value == 2 && g1.CurrentHealth == 6, "Second Invade did not level the Gate to 2 with +0/+2.");
                    play(Dremora);
                    ServerCard dremora = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == Dremora; }, s.Players[me].NetId, GameZoneName.CreatureBoard)[0];
                    Require(dremora.QueryAttribute(AttributeType.Attack).Value == 4 && dremora.QueryAttribute(AttributeType.MaxHealth).Value == 4, "Level 2 Gate did not give a summoned Daedra +1/+1.");

                    // Effect summon: the Scout enters the board without being played; its Summon: Invade must still fire.
                    ServerCard scout = f.CreateIn(Scout, me, GameZoneName.CreatureBoard, 1);
                    f.Event(delegate { ServerEffectUtilities.TriggerWhenCardEntersBoard(DecisionRestoration.State(s, s._turnStateStack.Peek()), scout); });
                    f.DrainEffects();
                    Require(g1.QueryAttribute(AttributeType.OblivionGateLevel).Value == 3 && g1.CurrentHealth == 8, "An effect-summoned creature's Summon (Invade) did not fire.");
                    ServerCard inHand = f.Create(Dremora, me, GameZoneName.Hand);
                    f.Event(delegate { OfflineAuraRestoration.Apply(DecisionRestoration.State(s, s._turnStateStack.Peek())); });
                    Require(inHand.QueryAttribute(AttributeType.ManaCost).Value == 3, "Level 3 Gate did not reduce a Daedra in hand by 1: " + inHand.QueryAttribute(AttributeType.ManaCost).Value);
                }
            });

            // Mobilize (OfflineMobilize): offered only for empty friendly lanes; a Recruit is summoned there and equips the item.
            test("mobilize-item-recruit-real-lifecycle", delegate {
                const int CovenantMail = -1308321544; // [+1/+3] Mobilize, Guard, Prophecy
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 1); // right lane occupied
                    ServerCard mail = f.Create(CovenantMail, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    CardCommandState command = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, mail);
                    Require(command != null && command.LaneTargets.Count == 1 && command.LaneTargets[0] == BoardLaneTarget.MyLeftLane,
                        "Mobilize lane targets differ from the single empty friendly lane: " + (command == null ? "none" : command.LaneTargets.ToString()));
                    f.PlayLane(mail, BoardLaneTarget.MyLeftLane, me);
                    List<ServerCard> left = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CurrentLaneIndex == 0; }, s.Players[me].NetId, GameZoneName.CreatureBoard);
                    Require(left.Count == 1 && left[0].HasSubtype(CardSubtype.Recruit), "Mobilize did not summon exactly one Recruit in the chosen lane.");
                    ServerCard recruit = left[0];
                    Require(recruit.QueryAttribute(AttributeType.Attack).Value == 2 && recruit.QueryAttribute(AttributeType.MaxHealth).Value == 4,
                        "Mobilized Recruit is not a 1/1 wielding the +1/+3 item: " + recruit.QueryAttribute(AttributeType.Attack).Value + "/" + recruit.QueryAttribute(AttributeType.MaxHealth).Value);
                    Require(mail.CurrentGameZoneType != GameZoneType.Hand, "Mobilize item stayed in hand.");
                }
            });

            // Persistent turn/event triggers and temporary effects through the restored lifecycle (real plays and turn ends):
            // owner-turn filtering, board-only sources, after-action and summon-other filters, end-of-turn expiry, Expertise.
            test("spec-turn-event-triggers-and-expertise-real-lifecycle", delegate {
                foreach (int id in new int[] { QuicksilverRelic, ImperialMight, Calm, LillandrilHexmage, BrumaProfiteer, GuildswornWayfarer })
                    Require(OriginalSpecRules.HasRestoredRules(id), "Turn/event fixture card was not emitted: " + id + " " + OriginalSpecRules.SkipReason(id));
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, them = 1 - me;
                    f.Create(QuicksilverRelic, me, GameZoneName.SupportBoard);
                    f.Create(QuicksilverRelic, me, GameZoneName.Hand); // in hand: must never fire
                    f.Create(ImperialMight, me, GameZoneName.SupportBoard);
                    f.Create(LillandrilHexmage, me, GameZoneName.CreatureBoard);
                    f.Create(BrumaProfiteer, me, GameZoneName.CreatureBoard);
                    ServerCard wayfarer = f.CreateIn(GuildswornWayfarer, me, GameZoneName.CreatureBoard, 1);
                    ServerCard enemy = f.Create(Leviathan, them, GameZoneName.CreatureBoard);
                    ServerCard calm = f.Create(Calm, me, GameZoneName.Hand), crab = f.Create(Mudcrab, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    int myHealth = f.Avatar(me).CurrentHealth, theirHealth = f.Avatar(them).CurrentHealth, enemyAttack = enemy.QueryAttribute(AttributeType.Attack).Value;
                    int wayfarerAttack = wayfarer.QueryAttribute(AttributeType.Attack).Value, myBoard = f.BoardCount(me);

                    f.Play(crab, null, me);
                    Require(f.Avatar(me).CurrentHealth == myHealth + 1, "Bruma Profiteer did not gain 1 health when another friendly creature was summoned.");
                    Require(f.Avatar(them).CurrentHealth == theirHealth, "Lillandril Hexmage fired for a creature play.");
                    f.Refresh(me);
                    f.Play(calm, enemy, me);
                    Require(enemy.QueryAttribute(AttributeType.Attack).Value == Math.Max(0, enemyAttack - 4), "Calm did not give -4/-0 this turn.");
                    Require(f.Avatar(them).CurrentHealth == theirHealth - 1, "Lillandril Hexmage did not deal 1 damage after an action.");
                    Require(wayfarer.QueryAttribute(AttributeType.Attack).Value == wayfarerAttack, "Expertise fired before the end of turn.");
                    myBoard = f.BoardCount(me);

                    f.EndTurn(); // my end: Imperial Might Grunt, Expertise; their start: no Relic damage
                    Require(f.BoardCount(me) == myBoard + 1, "Imperial Might did not summon a Grunt at the end of its owner's turn.");
                    Require(wayfarer.QueryAttribute(AttributeType.Attack).Value == wayfarerAttack + 1, "Expertise did not trigger after an action was played this turn.");
                    Require(enemy.QueryAttribute(AttributeType.Attack).Value == enemyAttack, "Calm's -4/-0 did not expire at the end of the turn.");
                    Require(f.Avatar(them).CurrentHealth == theirHealth - 1, "Quicksilver Relic fired at the start of the opponent's turn.");
                    myBoard = f.BoardCount(me);

                    f.EndTurn(); // their end (no action played): no Imperial Might/Expertise; my start: exactly one Relic (hand copy idle)
                    Require(f.BoardCount(me) == myBoard, "Imperial Might fired at the end of the opponent's turn.");
                    Require(wayfarer.QueryAttribute(AttributeType.Attack).Value == wayfarerAttack + 1, "Expertise fired on the opponent's turn.");
                    Require(f.Avatar(them).CurrentHealth == theirHealth - 2, "Quicksilver Relic did not deal exactly 1 damage at its owner's turn start (hand copy must not fire).");

                    f.EndTurn(); // my end without an action: Expertise must not fire
                    Require(wayfarer.QueryAttribute(AttributeType.Attack).Value == wayfarerAttack + 1, "Expertise fired without an action, item, or support played.");
                }
            });
        }

        // Board digest after the most recent Smoke (differential tests compare two graphs on identical fixtures).
        internal static string LastDigest;

        // A double card has no abilities of its own: it splits into its halves when drawn (OfflineDoubleCards); each half has its own tests.
        internal static string DoubleSplit(GameConfiguration config, Card card)
        {
            EngineTests.Fixture f = new EngineTests.Fixture(config);
            ServerCard d = f.CreateId(card.ResourceId.Id, GameZoneName.Deck, 0, f.One);
            PlayerActions.DrawCards.StaticExecute(f.State, f.One, 1, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.Untyped, true, false, -1);
            if (d.CurrentGameZoneType != GameZoneType.Hand) throw new InvalidOperationException("double card was not drawn");
            List<ServerCard> drawn = OfflineDoubleCards.SplitHands(f.State);
            if (drawn == null || drawn.Count != card.DoubleCardReferences.Count || d.CurrentGameZoneType == GameZoneType.Hand)
                throw new InvalidOperationException("double card did not split on draw (" + (drawn == null ? 0 : drawn.Count) + " cards)");
            foreach (ServerCard half in drawn)
                if (half.CurrentGameZoneType != GameZoneType.Hand) throw new InvalidOperationException("double card half not in hand: " + half.CardId);
            return "split into " + drawn.Count + " cards in hand";
        }

        internal static string Smoke(GameConfiguration config, Card card)
        {
            if (card.HasSubtype(CardSubtype.Double)) return DoubleSplit(config, card);
            LastDigest = null;
            EngineTests.Fixture f = new EngineTests.Fixture(config);
            for (int i = 0; i < 4; i++) { f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.One); f.CreateId(Mudcrab, GameZoneName.Deck, 0, f.Two); }
            for (int lane = 0; lane < 2; lane++) { f.CreateId(Leviathan, GameZoneName.CreatureBoard, lane, f.One); f.CreateId(Leviathan, GameZoneName.CreatureBoard, lane, f.Two); }
            ServerCard wounded = f.CreateId(Mudcrab, GameZoneName.CreatureBoard, 1, f.Two);
            if (card.ResourceId.Id == 1738444861) wounded.AddCover(true, f.State, null);
            ServerCard woundedBig = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, f.Two); woundedBig.ModifyCurrentHealth(-1, null, f.State, true);
            ServerCard woundedNear = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.Two); woundedNear.ModifyCurrentHealth(-1, null, f.State, true);
            int powered = PowerSample(config, card);
            if (powered != 0) f.CreateId(powered, GameZoneName.CreatureBoard, 0, f.Two);
            // Friendly-side samples: a wounded creature and a Guard on the board, a creature and an item in hand; an enemy support.
            ServerCard woundedFriend = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, f.One); woundedFriend.ModifyCurrentHealth(-1, null, f.State, true);
            int guard = KeywordSample(config, AttributeType.Taunt);
            if (guard != 0) f.CreateId(guard, GameZoneName.CreatureBoard, 1, f.One);
            f.CreateId(Mudcrab, GameZoneName.Hand, 0, f.One);
            int item = SampleOf(config, CardSubtype.Item), support = SampleOf(config, CardSubtype.Support);
            if (item != 0) f.CreateId(item, GameZoneName.Hand, 0, f.One);
            if (support != 0) f.CreateId(support, GameZoneName.SupportBoard, 0, f.Two);
            f.AvatarOne.SetAvatarHealthPreGame(20); f.AvatarTwo.SetAvatarHealthPreGame(20);
            if (card.ResourceId.Id == -908388106) f.AvatarOne.SetAvatarHealthPreGame(10);
            if (card.ResourceId.Id == -1577153690) f.AvatarTwo.SetAvatarHealthPreGame(30);
            CardDefinition definition = GameManager.Instance.LookUpCardDefinition(card.ResourceId.Id);
            // A played action resolves from the Board zone (DecisionRestoration.ProcessPlayCard moves it there first); original
            // action graphs require Self IsIn Board, so the fixture places actions where the real sequence does.
            GameZoneName zone = definition.Subtypes.Contains(CardSubtype.Creature) ? GameZoneName.CreatureBoard : definition.Subtypes.Contains(CardSubtype.Support) ? GameZoneName.SupportBoard
                : definition.Subtypes.Contains(CardSubtype.Spell) ? GameZoneName.Board : GameZoneName.Hand;
            ServerCard source = f.CreateId(card.ResourceId.Id, zone, 0, f.One);
            bool conditional = false;
            // A random heal may legitimately pick an unwounded creature; such cards are judged as conditional.
            if (HasRandomHeal(card)) conditional = true;
            // Samples for every subtype filter in the card's graphs (tribes on both boards; any subtype in deck and discard),
            // so tribe buffs and "draw a random X from your deck" have a real matching card.
            foreach (CardSubtype type in SubtypeFilters(card))
            {
                int sample = SampleOf(config, type);
                if (sample == 0) continue;
                bool creature = config.FindCardById(sample).HasSubtype(CardSubtype.Creature);
                if (creature) { f.CreateId(sample, GameZoneName.CreatureBoard, 0, f.One); f.CreateId(sample, GameZoneName.CreatureBoard, 0, f.Two); }
                f.CreateId(sample, GameZoneName.Deck, 0, f.One); f.CreateId(sample, GameZoneName.Cemetery, 0, f.One);
                f.CreateId(sample, GameZoneName.Hand, 0, f.One);
            }
            // A deck/discard sample satisfying every mana-cost filter (e.g. "draw a random 0-cost card from your deck").
            int costed = CostSample(config, card);
            if (costed != 0) { f.CreateId(costed, GameZoneName.Deck, 0, f.One); f.CreateId(costed, GameZoneName.Cemetery, 0, f.One); }
            // Raise Dead targets generic creatures, which SubtypeFilters deliberately excludes.
            if (card.ResourceId.Id == 1403985098)
            { f.CreateId(Mudcrab, GameZoneName.Cemetery, 0, f.One); f.CreateId(Mudcrab, GameZoneName.Cemetery, 0, f.Two); }
            if (card.ResourceId.Id == -355724207) f.CreateId(Mudcrab, GameZoneName.Cemetery, 0, f.One); // Flesh Atronach counts generic creatures.
            if (card.ResourceId.Id == 1812125842 || card.ResourceId.Id == 916842555)
                f.CreateId(card.ResourceId.Id, GameZoneName.Cemetery, 0, f.One); // Exact-name discard counters.
            // Death-scaled cards need a nonzero prior-turn event value to make their effects observable.
            if (card.ResourceId.Id == 1801637084 || card.ResourceId.Id == -229007472)
            { f.One.SetAttribute(AttributeType.CreaturesDeadThisTurn, 1); f.Two.SetAttribute(AttributeType.CreaturesDeadThisTurn, 2); }
            if (card.ResourceId.Id == -155831290) f.Two.SetAttribute(AttributeType.NumRunesLost, 2);
            if (card.ResourceId.Id == -1376421634) f.CreateId(card.ResourceId.Id, GameZoneName.Deck, 0, f.One);
            if (card.ResourceId.Id == -1975806074) f.CreateId(Mudcrab, GameZoneName.Cemetery, 0, f.Two);
            if (card.ResourceId.Id == -1994059211 || card.ResourceId.Id == -515379928 || card.ResourceId.Id == -462299413)
                f.CreateId(1691833255, GameZoneName.CreatureBoard, 0, f.Two);
            if (card.ResourceId.Id == 1361402109) f.CreateId(1691833255, GameZoneName.CreatureBoard, 0, f.One);
            // A private Consume graph queued by Pilfer is not visible to SubtypeFilters.
            // Give that graph a real graveyard candidate rather than accepting an empty no-op.
            foreach (TriggeredEffect outer in card.TriggeredEffects)
                foreach (Effect effect in outer.EffectGroup.Effects)
                    foreach (CardEffectAction action in effect.CardActions)
                        if (action.Action is OfflineConsumeChoices.BeginChoice) f.CreateId(Mudcrab, GameZoneName.Cemetery, 0, f.One);
            // Event cards for summon-other / after-action triggers exist before the digest so only the effect changes state.
            // (kept off the board so lanes keep room for moves, steals and token summons)
            ServerCard entered = f.CreateId(Mudcrab, GameZoneName.Hand, 0, f.One), played = f.CreateId(Calm, GameZoneName.Hand, 0, f.One);
            string before = Digest(f);
            List<string> ran = new List<string>();
            if (card.ActionPlayInformation != null && card.ActionPlayInformation.Count > 0)
            {
                PlayCardAbility ability = card.ActionPlayInformation[0];
                conditional |= HasCondition(ability.EffectGroup);
                ServerCard target = FirstTarget(f, source, ability.EffectGroup.Effects, ability.TargetSelectors);
                if (target == null && ability.PlayTargetingType == PlayTargetingType.RequiredTarget) throw new InvalidOperationException("no legal target in a populated board");
                f.PlayEffects(source, target); f.DrainTriggers(); ran.Add("play");
            }
            foreach (TriggeredEffect trigger in card.TriggeredEffects)
            {
                conditional |= HasCondition(trigger.EffectGroup);
                EffectTriggerType type = trigger.TriggerType;
                if (trigger.CustomUiType == CustomCardUiType.TargetCemetery)
                {
                    ServerEffectUtilities.TriggerContext customContext = new ServerEffectUtilities.TriggerContext { EffectOwner = source,
                        TriggeredEffect = trigger, TriggerEvent = new ServerEffectUtilities.TriggerEvent(type, source, f.Players),
                        ActingLaneIndex = source.CurrentLaneIndex, ActingLaneIndexType = (int)source.CurrentLaneType };
                    ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(f.State, customContext, null);
                    customContext.DidMainEffectsRun = true;
                    if (f.State.CustomUiCards.Count > 0)
                        ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(f.State, customContext,
                            new Decision { Type = CcgRpc.DecisionType.CustomChoice, CustomChoice = 0 });
                    else conditional = true;
                    f.State.CustomUiCards.Clear(); f.DrainTriggers(); ran.Add("Consume(chosen)");
                    continue;
                }
                if (trigger.ClientTargetingType != PlayTargetingType.NoTarget)
                {
                    // Player-chosen triggered target: execute the trigger with a legal chosen target, as the decision would.
                    ServerCard chosen = FirstTarget(f, source, trigger.EffectGroup.Effects, trigger.TargetSelectors);
                    if (chosen == null) throw new InvalidOperationException("no legal triggered target in a populated board");
                    ServerEffectUtilities.TriggerContext chosenContext = new ServerEffectUtilities.TriggerContext { EffectOwner = source, TriggeredEffect = trigger,
                        TriggerEvent = new ServerEffectUtilities.TriggerEvent(type, source, f.Players), ActingLaneIndex = source.CurrentLaneIndex, ActingLaneIndexType = (int)source.CurrentLaneType };
                    ServerEffectUtilities.TestAndExecuteTriggeredEffectFromQueue(f.State, chosenContext, new Decision { TargetCardNetUid = chosen.NetUid.Id });
                    f.DrainTriggers(); ran.Add(type + "(chosen)");
                    continue;
                }
                if (type == EffectTriggerType.WhenPlayerTurnStarts || type == EffectTriggerType.WhenPlayerTurnEnds)
                    ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(type, f.One));
                else if (Array.IndexOf(EventTypes, type) >= 0)
                {
                    // Event trigger: fire the native event with the first inciter/target pair that satisfies the trigger's own requirements.
                    List<ServerCard> cards = f.Catalog.GatherCardList(delegate(ServerCard x) { return true; });
                    List<ServerCard> targets = new List<ServerCard>(cards);
                    // A defend-trigger heal needs a wounded attacker to produce an observable change.
                    if (type == EffectTriggerType.WhenCardDefends) targets.Insert(0, woundedBig);
                    targets.Add(null);
                    ServerEffectUtilities.TriggerEvent found = default(ServerEffectUtilities.TriggerEvent); bool have = false;
                    foreach (ServerCard inciter in cards)
                    {
                        foreach (ServerCard eventTarget in targets)
                        {
                            ServerEffectUtilities.TriggerEvent candidate = new ServerEffectUtilities.TriggerEvent(type, inciter, eventTarget, f.Players, 1);
                            ServerEffectUtilities.TriggerContext probe = new ServerEffectUtilities.TriggerContext { EffectOwner = source, TriggeredEffect = trigger, TriggerEvent = candidate };
                            if (ServerEffectUtilities.TestTriggerEffectConditions(f.State, probe)) { found = candidate; have = true; break; }
                        }
                        if (have) break;
                    }
                    if (!have) throw new InvalidOperationException("no fixture event satisfies the " + type + " trigger requirements");
                    ServerEffectUtilities.TriggerEffectEvent(f.State, found);
                }
                else if (type == EffectTriggerType.WhenCardIsPlayed)
                    ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(type, played, f.Players));
                else
                {
                    ServerEffectUtilities.TriggerEvent evt = type == EffectTriggerType.Slay ? new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Slay, source, wounded, f.Players)
                        : type == EffectTriggerType.Pilfer ? new ServerEffectUtilities.TriggerEvent(EffectTriggerType.Pilfer, source, null, f.Players, 1)
                        : new ServerEffectUtilities.TriggerEvent(type, source, f.Players);
                    if (!ServerEffectUtilities.TriggerDirectEffectEvent(f.State, evt)) throw new InvalidOperationException("trigger " + type + " not gathered");
                }
                f.DrainTriggers(); ran.Add(trigger.TriggerType.ToString());
            }
            if (card.ActivatedAbility != null && card.ActivatedAbility.AbilityGroups.Count > 0)
            {
                ActivatedAbility a = card.ActivatedAbility;
                ServerCard target = a.TargetingType == PlayTargetingType.NoTarget ? null : FirstTarget(f, source, a.GatherAllEffects(), a.TargetSelectors);
                if (target == null && a.TargetingType == PlayTargetingType.RequiredTarget) throw new InvalidOperationException("no legal activation target");
                foreach (AbilityGroup g in a.AbilityGroups)
                {
                    conditional |= HasCondition(g.EffectGroup);
                    ServerEffectUtilities.TriggerContext context = new ServerEffectUtilities.TriggerContext { EffectOwner = source, TriggeredEffect = new TriggeredEffect { EffectGroup = g.EffectGroup, TriggerType = EffectTriggerType.AbilityEffect },
                        TriggerEvent = new ServerEffectUtilities.TriggerEvent(EffectTriggerType.AbilityEffect), ActingLaneIndex = source.CurrentLaneIndex, ActingLaneIndexType = (int)source.CurrentLaneType };
                    ServerEffectUtilities.ExecuteOnActivateCardEffects(f.State, context, new Decision { TargetCardNetUid = target == null ? CardNetUid.GetInvalid().Id : target.NetUid.Id }, null);
                }
                f.DrainTriggers(); ran.Add("activate");
            }
            if (card.Auras != null && card.Auras.Count > 0)
            {
                foreach (Aura aura in card.Auras)
                {
                    conditional |= aura.PlayerRequirements.Count > 0 || aura.CardRequirements.Count > 0;
                    foreach (AuraEnchantment ae in aura.EnchantmentsToApply)
                        conditional |= ae.StackCount.ValueMethod == ActionValueMethod.CountOfSelector;
                }
                OfflineAuraRestoration.Apply(f.State); ran.Add("aura");
            }
            if (ran.Count == 0 && card.TriggeredEffects.Count == 0 && card.ActionPlayInformation.Count == 0 && card.Auras.Count == 0
                && (card.ActivatedAbility == null || card.ActivatedAbility.AbilityGroups.Count == 0)) { LastDigest = Digest(f); return "native-attributes-only"; }
            if (ran.Count == 0) throw new InvalidOperationException("no ability executed");
            LastDigest = Digest(f);
            if (!conditional && LastDigest == before) throw new InvalidOperationException("abilities ran (" + string.Join(",", ran.ToArray()) + ") but no observable state changed");
            return string.Join(",", ran.ToArray());
        }

        internal static string PaidPlay(GameConfiguration config, Card card)
        {
            if (card.Definition == "Avatar") return "avatar card (player identity; not played from hand)";
            using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
            {
                Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                ServerCard source;
                string split = "";
                if (card.HasSubtype(CardSubtype.Double))
                {   // a double card in hand splits into its two halves before the move list is built (OfflineDoubleCards); a half is played
                    ServerCard d = f.Create(card.ResourceId.Id, player, GameZoneName.Hand);
                    List<ServerCard> drawn = null;
                    f.Event(delegate { drawn = OfflineDoubleCards.SplitHands(s, s._turnStateStack.Peek()); });
                    if (drawn == null || drawn.Count != card.DoubleCardReferences.Count || d.CurrentGameZoneType == GameZoneType.Hand)
                        throw new InvalidOperationException("double card did not split on draw (" + (drawn == null ? 0 : drawn.Count) + " cards)");
                    foreach (ServerCard half in drawn)
                        if (half.CurrentGameZoneType != GameZoneType.Hand || !card.DoubleCardReferences.Exists(delegate(UniqueResourceReference r) { return r.resourceId == half.CardId; }))
                            throw new InvalidOperationException("double card half not in hand: " + half.CardId);
                    source = drawn[0];
                    split = " split=" + drawn.Count;
                }
                else source = f.Create(card.ResourceId.Id, player, GameZoneName.Hand);
                f.Create(Leviathan, player, GameZoneName.CreatureBoard); f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                f.CreateIn(Mudcrab, 1 - player, GameZoneName.CreatureBoard, 1);
                ServerCard wounded = f.CreateIn(Leviathan, 1 - player, GameZoneName.CreatureBoard, 1);
                int powered = PowerSample(config, card);
                if (powered != 0) f.CreateIn(powered, 1 - player, GameZoneName.CreatureBoard, 0);
                f.Event(delegate { wounded.ModifyCurrentHealth(-1, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 30); });
                // Let queued reactions to the setup finish first (Dragon Cult Ghost reacts to magicka changes): a reaction that
                // resolves after the move list is built replaces the list, and a decision against the old list is dropped.
                for (int tick = 0; tick < 200; tick++)
                {
                    var q = s._turnStateStack.Peek().EffectTriggerQueues;
                    if (s._turnStateStack.Count == 1 && q.Pending.Count == 0 && q.Active.Count == 0 && q.After.Count == 0) break;
                    LifecycleRestoration.FixedUpdate(s);
                }
                s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                CardCommandState command = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, source);
                if (command == null || (command.ValidCardCommandMask & 15) == 0) throw new InvalidOperationException("not offered as a playable card");
                int cost = source.QueryAttribute(AttributeType.ManaCost).Value;
                if (command.EquipTargets.Count > 0) f.PlayEquip(source, s.CardCatalog.FindDynamicCard(new CardNetUid(command.EquipTargets[0])), player);
                else if (command.LaneTargets.Count > 0) f.PlayLane(source, command.LaneTargets[0], player);
                else
                {
                    try { f.Play(source, command.PlayTargets.Count > 0 ? s.CardCatalog.FindDynamicCard(new CardNetUid(command.PlayTargets[0])) : null, player); }
                    catch (InvalidOperationException e)
                    {   // "You win the game" (Break the Enchantment): the match ends at Results with the player as the winner
                        if (e.Message.EndsWith("Results", StringComparison.Ordinal) && s.WinnerId == s.Players[player].NetId)
                            return "won the game";
                        throw;
                    }
                }
                if (source.CurrentGameZoneType == GameZoneType.Hand)
                    throw new InvalidOperationException("card stayed in hand after play (lanes " + command.LaneTargets.Count + ", targets " + command.PlayTargets.Count
                        + ", mask " + command.ValidCardCommandMask + ", mana " + s.Players[player].QueryAttribute(AttributeType.Mana).Value
                        + ", queued decisions " + s.PlayerServiceClients[player]._decisionMessages.Count + ", last rejection " + DecisionRestoration.LastPlayRejection
                        + ", turn states " + s._turnStateStack.Count + ", phase " + s._turnStateStack.Peek().Phase + ", cost " + source.QueryAttribute(AttributeType.ManaCost).Value + ")");
                int mana = s.Players[player].QueryAttribute(AttributeType.Mana).Value;
                return "zone=" + source.CurrentGameZoneName + " mana=" + mana + "/" + (30 - cost) + split;
            }
        }

        private static ServerCard FirstTarget(EngineTests.Fixture f, ServerCard source, List<Effect> effects, List<DecisionConditionalExpression> selectors)
        {
            if (selectors == null || selectors.Count == 0) return null;
            // Prefer wounded enemies, then enemies, then wounded friends: damage and heal choices then observably change state.
            Predicate<ServerCard> wounded = delegate(ServerCard x) { return x.QueryAttribute(AttributeType.Wounded).Value > 0; };
            List<ServerCard> candidates = f.Catalog.GatherCardList(delegate(ServerCard x) { return x.OwnerNetId == f.Two.NetId && wounded(x); });
            candidates.AddRange(f.Catalog.GatherCardList(delegate(ServerCard x) { return x.OwnerNetId == f.Two.NetId && !wounded(x); }));
            candidates.AddRange(f.Catalog.GatherCardList(delegate(ServerCard x) { return x.OwnerNetId != f.Two.NetId && wounded(x); }));
            candidates.AddRange(f.Catalog.GatherCardList(delegate(ServerCard x) { return x.OwnerNetId != f.Two.NetId && !wounded(x); }));
            foreach (ServerCard c in candidates)
                if (CommandRestoration.ValidCardTarget(source, effects, selectors, c, f.Catalog,
                    new PlayerConditionalArguments(f.Catalog, f.One, f.One, c.OwnerNetId == f.One.NetId ? f.One : f.Two, c.OwnerNetId == f.One.NetId ? f.One : f.Two, f.Two), false, false)) return c;
            return null;
        }
        private static readonly EffectTriggerType[] EventTypes = { EffectTriggerType.WhenCardEntersBoard, EffectTriggerType.WhenCardDies, EffectTriggerType.WhenCardIsDrawn,
            EffectTriggerType.WhenCardTakesDamage, EffectTriggerType.WhenCardAttacks, EffectTriggerType.WhenCardWardIsBroken, EffectTriggerType.WhenCardChangesLane,
            EffectTriggerType.WhenCardIsEquipped, EffectTriggerType.WhenCardBreaksRune, EffectTriggerType.WhenCardIsHealed, EffectTriggerType.WhenCardConsumes,
            EffectTriggerType.WhenCardDefends, EffectTriggerType.WhenCardAttackComplete };
        private static List<CardSubtype> SubtypeFilters(Card card)
        {
            List<CardSubtype> types = new List<CardSubtype>();
            List<EffectGroup> groups = new List<EffectGroup>();
            foreach (TriggeredEffect t in card.TriggeredEffects) groups.Add(t.EffectGroup);
            foreach (PlayCardAbility a in card.ActionPlayInformation) groups.Add(a.EffectGroup);
            if (card.ActivatedAbility != null) foreach (AbilityGroup g in card.ActivatedAbility.AbilityGroups) groups.Add(g.EffectGroup);
            List<ICardConditionalExpression> all = new List<ICardConditionalExpression>();
            foreach (EffectGroup g in groups) foreach (Effect e in g.Effects) foreach (CardEffectAction a in e.CardActions) all.AddRange(a.Selectors);
            foreach (Aura aura in card.Auras) foreach (AuraEnchantment ae in aura.EnchantmentsToApply) all.AddRange(ae.Selectors);
            foreach (DecisionConditionalExpression d in DecisionSelectors(card)) all.AddRange(d.CardConditionalExpressions);
            foreach (TriggeredEffect t in card.TriggeredEffects)
                foreach (ICardRequirement r in t.TriggerCardRequirements)
                {
                    CardRequirements.SubtypeRequirement st = r as CardRequirements.SubtypeRequirement;
                    if (st != null && st.LhsCard == CardRole.Inciter) all.Add(st.Conditional);
                }
            foreach (ICardConditionalExpression c in all)
            {
                CardConditionals.Subtype st = c as CardConditionals.Subtype;
                if (st != null && st.SubtypeName != CardSubtype.Creature && st.SubtypeName != CardSubtype.Avatar && !types.Contains(st.SubtypeName)) types.Add(st.SubtypeName);
            }
            return types;
        }
        private static List<DecisionConditionalExpression> DecisionSelectors(Card card)
        {
            List<DecisionConditionalExpression> list = new List<DecisionConditionalExpression>();
            foreach (TriggeredEffect t in card.TriggeredEffects) list.AddRange(t.TargetSelectors);
            foreach (PlayCardAbility a in card.ActionPlayInformation) list.AddRange(a.TargetSelectors);
            if (card.ActivatedAbility != null) list.AddRange(card.ActivatedAbility.TargetSelectors);
            return list;
        }
        // A plain enemy creature satisfying every power filter of the card's chosen targets ("a creature with 0 power").
        internal static int PowerSample(GameConfiguration config, Card card)
        {
            List<CardConditionals.AttributeValue> powers = new List<CardConditionals.AttributeValue>();
            foreach (DecisionConditionalExpression d in DecisionSelectors(card))
                foreach (ICardConditionalExpression c in d.CardConditionalExpressions)
                {
                    CardConditionals.AttributeValue v = c as CardConditionals.AttributeValue;
                    if (v != null && v.AttributeName == AttributeType.Attack) powers.Add(v);
                }
            if (powers.Count == 0) return 0;
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
            {
                if (!c.HasSubtype(CardSubtype.Creature) || c.TriggeredEffects.Count > 0 || c.Auras.Count > 0) continue;
                SPTESL.IntAttribute attack = c.EntityAttributes.Find(delegate(SPTESL.Attribute x) { return x.Name == AttributeType.Attack; }) as SPTESL.IntAttribute;
                if (attack == null) continue;
                bool ok = true;
                foreach (CardConditionals.AttributeValue v in powers) ok &= Compare(attack.Value, v.Is, v.Value.ConstantValue);
                if (ok) return c.ResourceId.Id;
            }
            return 0;
        }
        private static bool Compare(int value, CardConditionals.AttributeValue.ComparisonType cmp, int want)
        {
            switch (cmp)
            {
                case CardConditionals.AttributeValue.ComparisonType.LessThan: return value < want;
                case CardConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo: return value <= want;
                case CardConditionals.AttributeValue.ComparisonType.EqualTo: return value == want;
                case CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo: return value >= want;
                default: return value > want;
            }
        }
        private static int CostSample(GameConfiguration config, Card card)
        {
            List<CardConditionals.AttributeValue> costs = new List<CardConditionals.AttributeValue>();
            List<EffectGroup> groups = new List<EffectGroup>();
            foreach (TriggeredEffect t in card.TriggeredEffects) groups.Add(t.EffectGroup);
            foreach (PlayCardAbility a in card.ActionPlayInformation) groups.Add(a.EffectGroup);
            foreach (EffectGroup g in groups) foreach (Effect e in g.Effects) foreach (CardEffectAction a in e.CardActions)
                foreach (ICardConditionalExpression c in a.Selectors)
                {
                    CardConditionals.AttributeValue v = c as CardConditionals.AttributeValue;
                    if (v != null && v.AttributeName == AttributeType.ManaCost) costs.Add(v);
                }
            if (costs.Count == 0) return 0;
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
            {
                if (!c.HasSubtype(CardSubtype.Creature) || c.TriggeredEffects.Count > 0 || c.ActionPlayInformation.Count > 0) continue;
                SPTESL.IntAttribute cost = c.EntityAttributes.Find(delegate(SPTESL.Attribute x) { return x.Name == AttributeType.ManaCost; }) as SPTESL.IntAttribute;
                if (cost == null) continue;
                bool ok = true;
                foreach (CardConditionals.AttributeValue v in costs)
                {
                    int want = v.Value.ConstantValue;
                    switch (v.Is)
                    {
                        case CardConditionals.AttributeValue.ComparisonType.LessThan: ok &= cost.Value < want; break;
                        case CardConditionals.AttributeValue.ComparisonType.LessThanOrEqualTo: ok &= cost.Value <= want; break;
                        case CardConditionals.AttributeValue.ComparisonType.EqualTo: ok &= cost.Value == want; break;
                        case CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo: ok &= cost.Value >= want; break;
                        case CardConditionals.AttributeValue.ComparisonType.GreaterThan: ok &= cost.Value > want; break;
                    }
                }
                if (ok) return c.ResourceId.Id;
            }
            return 0;
        }
        private static bool HasRandomHeal(Card card)
        {
            foreach (TriggeredEffect t in card.TriggeredEffects) foreach (Effect e in t.EffectGroup.Effects) foreach (CardEffectAction a in e.CardActions)
                if (a.Action is CardActions.Heal && a.SelectorSort != null && a.SelectorSort.Method == CardListSort.SortMethod.Random) return true;
            return false;
        }
        private static int KeywordSample(GameConfiguration config, AttributeType keyword)
        {
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
            {
                if (!c.HasSubtype(CardSubtype.Creature) || c.TriggeredEffects.Count > 0 || c.Auras.Count > 0 || c.ActionPlayInformation.Count > 0) continue;
                SPTESL.BoolAttribute flag = c.EntityAttributes.Find(delegate(SPTESL.Attribute x) { return x.Name == keyword; }) as SPTESL.BoolAttribute;
                if (flag != null && flag.Value) return c.ResourceId.Id;
            }
            return 0;
        }
        private static int SampleOf(GameConfiguration config, CardSubtype type)
        {
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
            {
                if (c.HasSubtype(type) && c.TriggeredEffects.Count == 0 && c.Auras.Count == 0
                    && c.ActionPlayInformation.Count == 0) return c.ResourceId.Id;
            }
            // With original graphs most tribe members carry effects; any member still works as a passive sample
            // (fixture cards are created directly, so their own triggers do not fire).
            foreach (CardSet set in config.CardCollection) foreach (Card c in set.Cards)
                if (c.HasSubtype(type) && c.Auras.Count == 0 && c.Production) return c.ResourceId.Id;
            return 0;
        }
        private static bool HasCondition(EffectGroup g)
        {
            if (g == null) return false;
            foreach (Effect e in g.Effects) if (e.ExecutePlayerRequirements.Count > 0) return true;
            return false;
        }
        private static string Digest(EngineTests.Fixture f)
        {
            StringBuilder s = new StringBuilder();
            foreach (ServerCard c in f.Catalog.GatherCardList(delegate(ServerCard x) { return true; }))
                s.Append(c.CardId).Append(':').Append(c.OwnerNetId.Id).Append(':').Append(c.CurrentGameZoneName).Append(':').Append(c.CurrentLaneIndex).Append(':').Append(c.QueryAttribute(AttributeType.Attack).Value)
                 .Append('/').Append(c.CurrentHealth).Append(':').Append(c.Enchantments.Count).Append(':').Append(c.QueryAttribute(AttributeType.Freeze).Value).Append(':').Append(c.Silenced).Append(';');
            foreach (PlayerState p in f.Players) s.Append(p.QueryAttribute(AttributeType.Mana).Value).Append('|');
            return s.ToString();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
