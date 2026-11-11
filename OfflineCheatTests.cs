using System;
using System.Collections.Generic;
using System.IO;
using SparkleServices;
using CcgRpc;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    public static class OfflineCheatTests
    {
        private const int Mudcrab = 620729069;

        public static void Run(GameConfiguration config, Action<string, Action> test, string output)
        {
            test("cheats-currency-unlock-all-cards-and-decks", delegate {
                OfflineInventoryService.PublishCatalog(config, Path.Combine(output, "native-inventory-catalog.tsv"));
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                OfflineCheats.AddCurrency(d, 1000, 500, 5);
                OfflineCheats.AddCurrency(d, -5000, 0, 0);
                Require(d.gold == 0 && d.gems == 500 && d.tickets == 5, "Currency cheat wrong or went negative.");
                int types = 0;
                foreach (Inventory.CardTypeLoadable t in OfflineInventoryService.CatalogTypes) types++;
                Require(types > 1000, "Catalog not published (" + types + " types).");
                d.AddCards(Mudcrab, 1);
                int added = OfflineCheats.UnlockAllCards(d);
                foreach (Inventory.CardTypeLoadable t in OfflineInventoryService.CatalogTypes)
                    Require(d.TotalCopies(t.typeHash) >= t.maxCopies, "Unlock left " + t.typeName + " at " + d.TotalCopies(t.typeHash) + "/" + t.maxCopies);
                Require(added > 0 && OfflineCheats.UnlockAllCards(d) == 0, "Unlock all cards is not idempotent.");
                int decks = OfflineCheats.UnlockAllDecks(d);
                Require(decks > 0 && OfflineCheats.UnlockAllDecks(d) == 0 && d.decks.Count == decks, "Unlock all decks granted " + decks + " then not idempotent.");
            });
            test("cheats-free-deck-building-lifts-copy-limits-only-when-on", delegate {
                OfflineInventoryService.PublishCatalog(config, Path.Combine(output, "native-inventory-catalog.tsv"));
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                d.AddCards(Mudcrab, 2);
                Inventory.CardTypeLoadable crab = null, unique = null;
                foreach (Inventory.CardTypeLoadable t in OfflineInventoryService.List(d).inventory.cardTypes)
                {
                    if (t.typeHash == Mudcrab) crab = t;
                    if (unique == null && t.maxCopies == 1) unique = t;
                }
                Require(crab != null && crab.maxCopies == 3 && crab.numRegular == 2 && unique != null && unique.numRegular == 0,
                    "Off: the usual limits and real counts must be reported (" + (crab == null ? "no crab" : crab.maxCopies + "/" + crab.numRegular) + ").");
                d.freeDeckBuilding = true;
                foreach (Inventory.CardTypeLoadable t in OfflineInventoryService.List(d).inventory.cardTypes)
                    Require(t.maxCopies == OfflineInventoryService.FreeDeckCopies && t.numRegular >= OfflineInventoryService.FreeDeckCopies,
                        "On: " + t.typeName + " reports " + t.numRegular + " owned, limit " + t.maxCopies);
                Require(d.TotalCopies(Mudcrab) == 2, "Free deck building must not change the real collection.");
            });
            // Owner report 2026-11-05: after several cheats a played card stayed floating and End Turn did nothing. A decision
            // for an old move list must be answered with the current list (the client re-syncs), and play must go on.
            test("cheats-then-stale-play-resyncs-and-play-continues", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim;
                    int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    if (s.Players[me].PlayerType != (int)PlayerType.Normal) s.Players[me].PlayerType = (int)PlayerType.Normal;
                    ServerCard crab = f.Create(Mudcrab, me, GameZoneName.Hand), second = f.Create(Mudcrab, me, GameZoneName.Hand);
                    f.Mana(me, 10); f.Refresh(me);
                    int oldList = s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid;
                    f.Event(delegate { f.Avatar(me).ModifyCurrentHealth(-15, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    foreach (OfflineCheats.MatchCheat c in new OfflineCheats.MatchCheat[] { OfflineCheats.MatchCheat.Magicka, OfflineCheats.MatchCheat.Magicka,
                        OfflineCheats.MatchCheat.Draw, OfflineCheats.MatchCheat.Heal, OfflineCheats.MatchCheat.Heal, OfflineCheats.MatchCheat.Magicka,
                        OfflineCheats.MatchCheat.Magicka, OfflineCheats.MatchCheat.Heal })
                    {
                        string why = OfflineCheats.Apply(s, c);
                        Require(why == null, c + " cheat unavailable: " + why);
                        f.Until(delegate { return OfflineCheats.Unavailable(s) == null; });
                    }
                    Require(s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != oldList, "Cheats did not replace the move list.");
                    int resends = DecisionRestoration.Resends;
                    s.PlayerServiceClients[me]._decisionMessages.Enqueue(new CcgRpc.Decision { Type = CcgRpc.DecisionType.Play, CommandUid = oldList,
                        ActorCardNetUid = crab.NetUid.Id, TargetCardNetUid = CardNetUid.GetInvalid().Id });
                    for (int tick = 0; tick < 20; tick++) LifecycleRestoration.FixedUpdate(s);
                    Require(DecisionRestoration.Resends == resends + 1, "A play for the old move list was dropped without re-sending the moves.");
                    Require(s.Players[me].LastSentCommandItem != null
                        && s.Players[me].LastSentCommandItem.CommandList.ListUid == s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid,
                        "The client was not sent the current move list.");
                    Require(crab.CurrentGameZoneName == GameZoneName.Hand, "The stale play must not happen.");
                    f.Play(crab, null, me);
                    Require(crab.CurrentGameZoneName == GameZoneName.CreatureBoard, "Play after the cheats did not reach the board.");
                    f.Play(second, null, me);
                    Require(second.CurrentGameZoneName == GameZoneName.CreatureBoard, "A second play after the cheats did not reach the board.");
                    f.EndTurn();
                }
            });
            // The client sends lane/slot -1 for actions (GameNetworkManager.PlayInstantCardWithTarget); owner 2026-11-06: Rapid
            // Shot on an enemy creature was rejected every time.
            foreach (int rapidShot in new int[] { -775726033, -2039499331 })
            {
                int id = rapidShot;
                test("client-action-play-with-invalid-position-" + id, delegate {
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim;
                        int me = s._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard shot = f.Create(id, me, GameZoneName.Hand), crab = f.Create(Mudcrab, 1 - me, GameZoneName.CreatureBoard);
                        f.Mana(me, 10); f.Refresh(me);
                        int resends = DecisionRestoration.Resends;
                        CcgRpc.CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                        s.PlayerServiceClients[me]._decisionMessages.Enqueue(new CcgRpc.Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid,
                            ActorCardNetUid = shot.NetUid.Id, TargetCardNetUid = crab.NetUid.Id, LaneIndex = ServerCard.InvalidPositionIndex, PositionIndex = ServerCard.InvalidPositionIndex });
                        f.Until(delegate { return s._turnStateStack.Peek().CurrentCommandItem != null && s._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != list.ListUid; });
                        Require(DecisionRestoration.Resends == resends, "The client's action play (lane/slot -1) was rejected.");
                        Require(shot.CurrentGameZoneName != GameZoneName.Hand && crab.CurrentGameZoneName != GameZoneName.CreatureBoard,
                            "Rapid Shot did not resolve on the Mudcrab: shot " + shot.CurrentGameZoneName + ", crab " + crab.CurrentGameZoneName);
                    }
                });
            }
            test("cheats-menu-blocks-board-input-only-over-its-window", delegate {
                Rect window = new Rect(40, 60, 360, 680);
                OfflineMenuInputGuard.Open = true; OfflineMenuInputGuard.Window = window;
                try
                {
                    BoardInputController.RawInput over = new BoardInputController.RawInput { screenPosition = new Vector3(100, Screen.height - 100, 0),
                        touchEvent = BoardInputController.TouchEventType.Held, lastTouchEvent = BoardInputController.TouchEventType.None };
                    OfflineMenuInputGuard.RawInputPostfix(ref over);
                    Require(over.touchEvent == BoardInputController.TouchEventType.None, "a press on the menu reached the board");
                    BoardInputController.RawInput outside = new BoardInputController.RawInput { screenPosition = new Vector3(window.xMax + 50, Screen.height - 100, 0),
                        touchEvent = BoardInputController.TouchEventType.Held };
                    OfflineMenuInputGuard.RawInputPostfix(ref outside);
                    Require(outside.touchEvent == BoardInputController.TouchEventType.Held, "a press beside the menu was blocked");
                    BoardInputController.RawInput drag = new BoardInputController.RawInput { screenPosition = new Vector3(100, Screen.height - 100, 0),
                        touchEvent = BoardInputController.TouchEventType.Up_Slow, lastTouchEvent = BoardInputController.TouchEventType.Held };
                    OfflineMenuInputGuard.RawInputPostfix(ref drag);
                    Require(drag.touchEvent == BoardInputController.TouchEventType.Up_Slow, "a board drag ending over the menu was cut off");
                    OfflineMenuInputGuard.Open = false;
                    BoardInputController.RawInput closed = new BoardInputController.RawInput { screenPosition = new Vector3(100, Screen.height - 100, 0),
                        touchEvent = BoardInputController.TouchEventType.Held };
                    OfflineMenuInputGuard.RawInputPostfix(ref closed);
                    Require(closed.touchEvent == BoardInputController.TouchEventType.Held, "input blocked with the menu closed");
                }
                finally { OfflineMenuInputGuard.Open = false; }
            });
            test("cheats-discard-pile-holds-dead-creatures-items-and-played-actions", delegate {
                // owner 2026-11-06: items and some dead creatures were missing from the F10 discard picker
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, foe = 1 - me;
                    if (s.Players[me].PlayerType != (int)PlayerType.Normal) s.Players[me].PlayerType = (int)PlayerType.Normal;
                    ServerCard wall = f.CreateIn(1427457192, foe, GameZoneName.CreatureBoard, 0);
                    ServerCard crab = f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0), wielder = f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                    f.EndTurn(); f.EndTurn(); f.Mana(me, 20); f.Refresh(me);
                    ServerCard sword = f.Create(-1808851552, me, GameZoneName.Hand), blow = f.Create(1744549196, me, GameZoneName.Hand);
                    f.Refresh(me); f.PlayEquip(sword, wielder, me);
                    f.Attack(crab, wall, me); f.Attack(wielder, wall, me);
                    f.Refresh(me); f.Play(blow, wall, me);
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    List<string> names = new List<string>();
                    foreach (ServerCard c in OfflineCheats.DiscardPile(s)) names.Add(c.GetCardName());
                    string where = "crab " + crab.CurrentGameZoneName + ", wielder " + wielder.CurrentGameZoneName + ", sword " + sword.CurrentGameZoneName + ", blow " + blow.CurrentGameZoneName;
                    Require(names.FindAll(delegate(string n) { return n == crab.GetCardName(); }).Count == 2 && names.Contains(sword.GetCardName()) && names.Contains(blow.GetCardName()),
                        "discard pile: [" + string.Join(", ", names.ToArray()) + "]; " + where);
                    // the client is told the action went to the discard pile (Reborn: ADD_TO_DISCARD; ours keeps the instance: MOVE_CARDS)
                    bool told = false;
                    foreach (HistoryItem h in s.HistoryLog.GenerateFullReplayForPlayer(s.PlayerServiceClients[me], HistoryLog.FullGameStateSendRule.Exclude).Queue)
                        if (h.Type == HistoryType.ActionMoveCards && h.AffectedCardsArray.Count > 0 && h.AffectedCardsArray[0].CardNetUid == blow.NetUid.Id
                            && h.AffectedCardsArray[0].CurrentPosition.GameZoneName == (int)GameZoneName.Cemetery) told = true;
                    Require(told, "the client was not told the played action went to the discard pile; " + where);
                }
            });
            test("cheats-match-discard-beast-form-heal-opponent-and-runes", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, foe = 1 - me;
                    PlayerState human = s.Players[me], other = s.Players[foe];
                    if (human.PlayerType != (int)PlayerType.Normal) human.PlayerType = (int)PlayerType.Normal;
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.ReturnFromDiscard) == "your discard pile is empty", "empty discard pile not reported");
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.RestoreRune) == "all your runes are intact", "intact runes not reported");
                    // discard pile -> hand (the chosen card, not the most recent one)
                    ServerCard first = f.Create(Mudcrab, me, GameZoneName.Cemetery), second = f.Create(Mudcrab, me, GameZoneName.Cemetery);
                    List<ServerCard> pile = OfflineCheats.DiscardPile(s);
                    Require(pile.Count == 2, "discard pile shows " + pile.Count + " cards");
                    ServerCard chosen = pile[0], left = pile[1];
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.ReturnFromDiscard, 0) == null, "return from discard refused");
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    Require(chosen.CurrentGameZoneName == GameZoneName.Hand && left.CurrentGameZoneName == GameZoneName.Cemetery,
                        "return from discard: chosen " + chosen.CurrentGameZoneName + ", other " + left.CurrentGameZoneName);
                    // Beast Form without a rune break
                    ServerCard harbinger = f.CreateIn(-1497585773, me, GameZoneName.CreatureBoard, 0);
                    ServerCard enemyWolf = f.CreateIn(-1497585773, foe, GameZoneName.CreatureBoard, 0);
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.BeastForm) == null, "Beast Form refused");
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    Require(harbinger.CardId == 1629862753 && enemyWolf.CardId == -1497585773, "Beast Form: mine " + harbinger.CardId + ", enemy " + enemyWolf.CardId);
                    Require(OfflineCheats.Unavailable(s) == null, "the turn did not continue after Beast Form: " + OfflineCheats.Unavailable(s));
                    // heal the opponent
                    ServerCard foeAvatar = f.Avatar(foe);
                    f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, other, 12, -1); });
                    s.CheckForRuneBreaks(); f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    int foeHealth = foeAvatar.CurrentHealth;
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.HealOpponent) == null && foeAvatar.CurrentHealth == foeHealth + 10, "heal opponent: " + foeHealth + " -> " + foeAvatar.CurrentHealth);
                    // restore runes: yours (health 18 = two runes broken) and the opponent's
                    foreach (bool mine in new bool[] { true, false })
                    {
                        PlayerState p = mine ? human : other; ServerCard a = f.Avatar(mine ? me : foe);
                        f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, p, a.CurrentHealth - 18, -1); });
                        s.CheckForRuneBreaks(); f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                        int runes = p.QueryAttribute(AttributeType.NumRunes).Value;
                        Require(runes == 3 && a.CurrentHealth == 18, (mine ? "my" : "foe") + " setup: runes " + runes + ", health " + a.CurrentHealth);
                        Require(OfflineCheats.Apply(s, mine ? OfflineCheats.MatchCheat.RestoreRune : OfflineCheats.MatchCheat.RestoreOpponentRune) == null, "restore rune refused");
                        f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                        s.CheckForRuneBreaks(); f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                        Require(p.QueryAttribute(AttributeType.NumRunes).Value == 4 && p.QueryAttribute(AttributeType.NumRunesLost).Value == 1 && a.CurrentHealth == 23,
                            (mine ? "my" : "foe") + " rune restore: runes " + p.QueryAttribute(AttributeType.NumRunes).Value + ", lost " + p.QueryAttribute(AttributeType.NumRunesLost).Value + ", health " + a.CurrentHealth);
                    }
                }
            });
            test("cheats-match-magicka-draw-heal-win-real-simulation", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim;
                    int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    PlayerState human = s.Players[me];
                    if (human.PlayerType != (int)PlayerType.Normal) human.PlayerType = (int)PlayerType.Normal; // fixture seats are generic
                    string why = OfflineCheats.Unavailable(s);
                    Require(why == null, "Cheats unavailable at the fixture's decision prompt: " + why);
                    int mana = human.QueryAttribute(AttributeType.Mana).Value;
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.Magicka) == null && human.QueryAttribute(AttributeType.Mana).Value == mana + 5, "+5 magicka failed.");
                    int max = f.Avatar(me).QueryAttribute(AttributeType.MaxMana).Value;
                    string maxWhy = OfflineCheats.Apply(s, OfflineCheats.MatchCheat.MaxMagicka);
                    int maxAfter = f.Avatar(me).QueryAttribute(AttributeType.MaxMana).Value;
                    Require(maxWhy == null && maxAfter == max + 1, "+1 max magicka failed: " + max + " -> " + maxAfter + " (" + maxWhy + "), enchantments " + f.Avatar(me).Enchantments.Count
                        + ", avatar zone " + f.Avatar(me).CurrentGameZoneName);
                    int hand = f.HandCount(me);
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.Draw) == null && f.HandCount(me) == hand + 1, "Draw failed: hand " + hand + " -> " + f.HandCount(me));
                    f.Event(delegate { f.Avatar(me).ModifyCurrentHealth(-12, null, DecisionRestoration.State(s, s._turnStateStack.Peek()), true); });
                    int health = f.Avatar(me).CurrentHealth;
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.Heal) == null && f.Avatar(me).CurrentHealth == health + 10, "Heal 10 failed: " + health + " -> " + f.Avatar(me).CurrentHealth);
                    Require(s._turnStateStack.Peek().CurrentCommandItem != null, "Command list not regenerated after a cheat.");
                    ServerCard foeAvatar = f.Avatar(1 - me); // a defeated avatar leaves AvatarBoard, so keep the instance
                    Require(OfflineCheats.Apply(s, OfflineCheats.MatchCheat.Win) == null && foeAvatar.CurrentHealth <= 0, "Win cheat did not defeat the opponent avatar.");
                }
            });
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
