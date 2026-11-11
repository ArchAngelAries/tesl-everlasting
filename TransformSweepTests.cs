using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Every card whose original rules transform a card (ChangeCard / TransformCard; owner request 2026-11-06 after Beast
    // Form never fired). Each card gets its own trigger in a real match fixture, and the report
    // (exports/transform-sweep.tsv) lists both boards and hands before and after, so every transformation can be read.
    // A card whose transformation needs a condition the sweep does not set up shows "no change" and is checked by hand.
    public static class TransformSweepTests
    {
        private const int Cheesemancer = 629135180;
        private static readonly int[] CheeseForms = { 200833199, 2055538882, -970385524, -1737008155, 2095300518 };
        private const int Mudcrab = 620729069, Leviathan = 1427457192, KwamaEgg = -1898613375, TinyDragon = 888500878;

        // card id, how to trigger it
        private static readonly object[][] Cards = {
            new object[] { -542954561, "play" }, new object[] { 733292913, "ring" }, new object[] { -2091996017, "activate" },
            new object[] { -863628557, "play" }, new object[] { 2067112919, "anchor" }, new object[] { -456442787, "play" },
            new object[] { 185413842, "play" }, new object[] { 1834368623, "rune" }, new object[] { 231111514, "play" },
            new object[] { 2060250390, "septim" }, new object[] { 2050393439, "farmer" }, new object[] { -438401061, "activate" },
            new object[] { 1591893004, "startturn" }, new object[] { 629135180, "drawtop" }, new object[] { -1775714845, "doppel" },
            new object[] { -856056876, "doppel" }, new object[] { 548145076, "inhand-endturn" }, new object[] { 1237320719, "inhand-endturn" },
            new object[] { -454368526, "drawtop-endturn" }, new object[] { -2081071272, "deck" }, new object[] { 2123609999, "egg" },
            new object[] { -571709705, "egg" }, new object[] { 1886197306, "conjurer" }, new object[] { -1898613375, "egg" },
            new object[] { 1909777226, "egg" }, new object[] { 553654387, "paint" }, new object[] { 73297253, "madman" },
            new object[] { 186738128, "activate" }, new object[] { 681793781, "play" }, new object[] { -488524776, "play" },
            new object[] { 564003897, "jiub" }, new object[] { 1452355374, "play" }, new object[] { 340699056, "play" },
            new object[] { -2036935735, "rune" }, new object[] { -1816206504, "rune" }, new object[] { 975414274, "play" },
            new object[] { -1755823232, "play" }, new object[] { 935076603, "rune" }, new object[] { -1497585773, "rune" },
            new object[] { -1970454943, "deck" }, new object[] { -2098382750, "play" }, new object[] { 716521723, "play" },
            new object[] { -426404054, "rune" }, new object[] { 694231768, "play" }, new object[] { -1192168222, "play" },
            new object[] { -1723035750, "delphine" }, new object[] { 441756923, "skyforge" }, new object[] { 1034021589, "waves" },
            new object[] { -1216391466, "rune" } };

        public static void Run(GameConfiguration config, Action<string, Action> test, string output)
        {
            test("transform-cheesemancer-drawn-becomes-a-random-cheesemancer", delegate
            {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard top = null;
                    f.Event(delegate { top = ServerCardCatalog.CreateCard(DecisionRestoration.State(s, s._turnStateStack.Peek()), Cheesemancer, false, GameZoneName.Deck, 0, CardInsertionType.Top, s.Players[me].NetId, null, null); });
                    string kinds = "";
                    foreach (TriggeredEffect t in top.TriggeredEffects) kinds += t.TriggerType + ",";
                    f.Event(delegate { PlayerActions.DrawCards.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[me], 1,
                        PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1); });
                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                    ServerCard now = s.CardCatalog.FindServerCard(top.NetUid);
                    string state = "zone " + now.CurrentGameZoneName + ", id " + now.CardId + ", counter " + now.QueryAttribute(RareAttributeType.CheesemancerCounter).Value + ", triggers " + kinds;
                    if (now.CardId == Cheesemancer) throw new InvalidOperationException("a drawn Cheesemancer did not change: " + state);
                    if (Array.IndexOf(CheeseForms, now.CardId) < 0) throw new InvalidOperationException("a drawn Cheesemancer became an unexpected card: " + state);
                }
            });
            test("transform-sweep-all-transforming-cards-report", delegate
            {
                StringBuilder report = new StringBuilder("card_id\tname\ttrigger\tresult\tchanged\tbefore\tafter\n");
                foreach (object[] entry in Cards)
                {
                    int id = (int)entry[0]; string how = (string)entry[1];
                    Card def = config.FindCardById(id);
                    string name = def == null ? "?" : def.Name, result, changed = "", before = "", after = "";
                    try
                    {
                        using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                        {
                            Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, foe = 1 - me;
                            if (s.Players[me].PlayerType != (int)PlayerType.Normal) s.Players[me].PlayerType = (int)PlayerType.Normal;
                            ServerCard ally = f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                            ServerCard weak = f.CreateIn(Mudcrab, foe, GameZoneName.CreatureBoard, 0);
                            ServerCard big = f.CreateIn(Leviathan, foe, GameZoneName.CreatureBoard, 1);
                            bool creature = def != null && def.HasSubtype(CardSubtype.Creature);
                            bool support = def != null && def.HasSubtype(CardSubtype.Support);
                            ServerCard card = null;
                            bool drawTop = how == "drawtop" || how == "drawtop-endturn";
                            bool inHand = how == "play" || how == "inhand-endturn" || how == "paint" || how == "deck" || how == "waves" || how == "doppel";
                            if (drawTop) { ServerCard top = null; f.Event(delegate { top = ServerCardCatalog.CreateCard(DecisionRestoration.State(s, s._turnStateStack.Peek()), id, false, GameZoneName.Deck, 0, CardInsertionType.Top, s.Players[me].NetId, null, null); }); card = top; }
                            else if (inHand) card = f.Create(id, me, GameZoneName.Hand);
                            else if (support) card = f.Create(id, me, GameZoneName.SupportBoard);
                            else card = f.CreateIn(id, me, GameZoneName.CreatureBoard, how == "jiub" ? 0 : 1);
                            if (how == "pilfer" || how == "slay" || how == "activate" || how == "conjurer" || how == "jiub" || how == "skyforge") { f.EndTurn(); f.EndTurn(); }   // awake
                            if (how == "paint") f.CreateIn(KwamaEgg, me, GameZoneName.CreatureBoard, 1);
                            f.Mana(me, 20); f.Refresh(me);
                            Dictionary<int, int> ids = Ids(s);
                            string deckBefore = Deck(s, me);
                            before = Board(s, me);
                            switch (how)
                            {
                                case "rune":
                                    ServerCard foeAvatar = f.Avatar(foe);
                                    f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, s.Players[foe], foeAvatar.CurrentHealth - 25, -1); });
                                    s.CheckForRuneBreaks(); f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                                    break;
                                case "endturn": case "inhand-endturn": f.EndTurn(); break;
                                case "drawtop": case "drawtop-endturn":
                                    f.Event(delegate { PlayerActions.DrawCards.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[me], 1,
                                        PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1); });
                                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                                    if (how == "drawtop-endturn") f.EndTurn();
                                    break;
                                case "egg": for (int n = 0; n < 4; n++) f.EndTurn(); break;   // two of the owner's turns
                                case "madman": for (int n = 0; n < 6; n++) f.EndTurn(); break;   // +1/+1 per own turn end until 5 power
                                case "ring": case "anchor":
                                    // a creature on top of the deck, then a native draw
                                    f.Event(delegate { ServerCardCatalog.CreateCard(DecisionRestoration.State(s, s._turnStateStack.Peek()), Mudcrab, false, GameZoneName.Deck, 0, CardInsertionType.Top, s.Players[me].NetId, null, null);
                                        PlayerActions.DrawCards.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[me], 1, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1); });
                                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                                    break;
                                case "septim": f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[me], 30); }); f.Refresh(me); f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); }); break;
                                case "farmer":
                                {
                                    string why = "";
                                    for (int n = 0; n < 7; n++) { string r = OfflineCheats.Apply(s, OfflineCheats.MatchCheat.MaxMagicka); if (r != null) why = r; f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); }); }
                                    changed = "max magicka " + f.Avatar(me).QueryAttribute(AttributeType.MaxMana).Value + (why.Length > 0 ? " (cheat: " + why + ")" : "") + "; ";
                                    f.EndTurn(); f.EndTurn();
                                    changed += "after turns " + f.Avatar(me).QueryAttribute(AttributeType.MaxMana).Value + "; ";
                                    break;
                                }
                                case "conjurer":
                                {
                                    f.Attack(card, f.Avatar(foe), me);
                                    ServerCard summoned = f.Create(Mudcrab, me, GameZoneName.Hand); f.Mana(me, 20); f.Refresh(me); f.PlayCreatureLane(summoned, 0, me); break;
                                }
                                case "jiub": f.Attack(card, weak, me); break;
                                case "delphine": { ServerCard dragon = f.Create(TinyDragon, me, GameZoneName.Hand); f.Refresh(me); f.PlayCreatureLane(dragon, 0, me); break; }
                                case "skyforge":
                                    for (int n = 0; n < 3; n++)
                                    {
                                        CardCommandState c = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, card);
                                        if (c == null || (c.ValidCardCommandMask & (int)CardCommand.ActiveAbility) == 0) throw new InvalidOperationException("cannot activate (use " + (n + 1) + ")");
                                        f.Activate(card, c.ActiveAbilityTargets.Count > 0 ? s.CardCatalog.FindServerCard(new CardNetUid(c.ActiveAbilityTargets[0])) : null, me);
                                        f.EndTurn(); f.EndTurn(); f.Mana(me, 20); f.Refresh(me);
                                    }
                                    break;
                                case "waves":
                                {
                                    CardCommandState c = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, card);
                                    if (c == null || c.LaneTargets.Count == 0) throw new InvalidOperationException("no lane target");
                                    f.PlayLane(card, c.LaneTargets.Contains(BoardLaneTarget.OtherLeftLane) ? BoardLaneTarget.OtherLeftLane : c.LaneTargets[0], me); break;
                                }
                                case "paint":
                                {
                                    CardCommandState c = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, card);
                                    if (c == null || c.PlayTargets.Count == 0) throw new InvalidOperationException("no egg target");
                                    f.Play(card, s.CardCatalog.FindServerCard(new CardNetUid(c.PlayTargets[0])), me); break;
                                }
                                case "startturn": f.EndTurn(); f.EndTurn(); break;
                                case "drawn":
                                    // on top of the deck, then a native draw
                                    f.Event(delegate { PlayerActions.DrawCards.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[me], 1,
                                        PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1); });
                                    f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                                    break;
                                case "mana": f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[me], 3); }); f.Refresh(me); f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); }); break;
                                case "played": { ServerCard other = f.Create(Mudcrab, me, GameZoneName.Hand); f.Refresh(me); f.Play(other, null, me); break; }
                                case "pilfer": f.Attack(card, f.Avatar(foe), me); break;
                                case "slay": f.Attack(card, weak, me); break;
                                case "activate":
                                {
                                    CardCommandState c = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, card);
                                    if (c == null || (c.ValidCardCommandMask & (int)CardCommand.ActiveAbility) == 0) throw new InvalidOperationException("cannot activate");
                                    ServerCard target = c.ActiveAbilityTargets.Count > 0 ? s.CardCatalog.FindServerCard(new CardNetUid(Prefer(c.ActiveAbilityTargets, weak, big))) : null;
                                    f.Activate(card, target, me);
                                    break;
                                }
                                default:   // play from hand: creatures to lane 1 (Summon prompts answered with their first target), actions on a target
                                {
                                    CardCommandState c = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, card);
                                    if (c == null || (c.ValidCardCommandMask & 15) == 0) throw new InvalidOperationException("not playable");
                                    if ((c.ValidCardCommandMask & (int)CardCommand.PlayToPosition) != 0) f.PlayCreatureLane(card, 1, me);
                                    else
                                    {
                                        ServerCard target = c.PlayTargets.Count > 0 ? s.CardCatalog.FindServerCard(new CardNetUid(Prefer(c.PlayTargets, weak, big))) : null;
                                        f.Play(card, target, me);
                                    }
                                    break;
                                }
                            }
                            for (int tick = 0; tick < 40; tick++) LifecycleRestoration.FixedUpdate(s);
                            after = Board(s, me);
                            string deckAfter = Deck(s, me);
                            if (deckAfter != deckBefore) after += " | deck now: " + deckAfter;
                            string note = changed, transformed = "";   // note: setup details written above (e.g. max magicka)
                            foreach (KeyValuePair<int, int> kv in Ids(s))
                            {
                                int old;
                                if (ids.TryGetValue(kv.Key, out old) && old != kv.Value) transformed += Name(config, old) + "->" + Name(config, kv.Value) + "; ";
                            }
                            changed = note + transformed;
                            result = transformed.Length > 0 ? "transformed" : before == after ? "no change" : "board changed";
                        }
                    }
                    catch (Exception e) { Exception x = e; while (x.InnerException != null) x = x.InnerException; result = "ERROR " + x.GetType().Name + ": " + x.Message.Replace('\t', ' ').Replace('\n', ' '); }
                    report.Append(id).Append('\t').Append(name).Append('\t').Append(how).Append('\t').Append(result).Append('\t').Append(changed)
                        .Append('\t').Append(before).Append('\t').Append(after).Append('\n');
                }
                File.WriteAllText(Path.Combine(output, "transform-sweep.tsv"), report.ToString());
            });
        }

        private static int Prefer(Google.Protobuf.Collections.RepeatedField<int> targets, ServerCard weak, ServerCard big)
        {
            if (targets.Contains(big.NetUid.Id)) return big.NetUid.Id;
            if (targets.Contains(weak.NetUid.Id)) return weak.NetUid.Id;
            return targets[0];
        }

        private static Dictionary<int, int> Ids(Simulation s)
        {
            Dictionary<int, int> d = new Dictionary<int, int>();
            foreach (ServerCard c in s.CardCatalog.GatherDynamicCardList()) d[c.NetUid.Id] = c.CardId;
            return d;
        }

        // the distinct cards in the owner's deck (a whole-deck transformation shows here)
        private static string Deck(Simulation s, int me)
        {
            List<string> names = new List<string>();
            foreach (ServerCard c in s.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return true; }, s.Players[me].NetId, GameZoneName.Deck))
                if (!names.Contains(c.GetCardName())) names.Add(c.GetCardName());
            return names.Count + " kinds: " + string.Join(",", names.ToArray());
        }

        private static string Name(GameConfiguration config, int id) { Card c = config.FindCardById(id); return c == null ? id.ToString() : c.Name; }

        private static string Board(Simulation s, int me)
        {
            StringBuilder b = new StringBuilder();
            for (int p = 0; p < 2; p++)
                foreach (GameZoneName zone in new GameZoneName[] { GameZoneName.CreatureBoard, GameZoneName.SupportBoard, GameZoneName.Hand })
                {
                    List<string> names = new List<string>();
                    foreach (ServerCard c in s.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return true; }, s.Players[p].NetId, zone))
                        names.Add(c.GetCardName() + (zone == GameZoneName.CreatureBoard ? " " + c.QueryAttribute(AttributeType.Attack).Value + "/" + c.CurrentHealth : ""));
                    b.Append(p == me ? "me." : "foe.").Append(zone).Append("=[").Append(string.Join(",", names.ToArray())).Append("] ");
                }
            return b.ToString().Trim();
        }
    }
}
