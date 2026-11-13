using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Native diagnostic "modcards" (Run-NativeDiagnostic.ps1 -Menu modcards [-ModsList <list>]): every collectible card of
    // the loaded mods is played for real in a fresh ordinary match against one friendly and one enemy Mudcrab: creatures
    // into the left lane, items onto the friendly creature, actions and supports at their first legal target (the enemy
    // first), supports then activated; then two turns pass so start/end-of-turn rules run. One TSV row per card:
    // ok / FAIL with the reason, where the card ended up and what happened to both creatures and both players.
    public static class OfflineModCardSweep
    {
        private const int Crab = 620729069;

        public static IEnumerator Run(string path)
        {
            yield return null;
            // the card rules a normal match loads first (EnsureEngine); without them the first cards seed with no effects
            OfflineNativeMatch.PrepareEngine();
            GameConfiguration config = GameManager.Instance.Config;
            StringBuilder tsv = new StringBuilder("card\tid\ttype\tresult\tzone\tfriend\tfoe\tmy_health\tfoe_health\thand\n");
            int ok = 0, fail = 0;
            HashSet<string> logged = new HashSet<string>();
            // Optional filter: exports/mod-card-sweep-only.txt with one card name per line.
            string only = Path.Combine(Path.GetDirectoryName(path), "mod-card-sweep-only.txt");
            List<string> names = File.Exists(only) ? new List<string>(File.ReadAllLines(only)) : null;
            foreach (int id in new List<int>(OfflineMods.CardIds))
            {
                Card card = config.FindCardById(id);
                if (card == null || string.IsNullOrEmpty(card.ExportCode)) continue;
                if (names != null && !names.Contains(card.Name)) continue;
                string row;
                try { row = Sweep(config, card); ok++; }
                catch (Exception e)
                {
                    row = "FAIL\t" + Clean(e.Message) + "\t\t\t\t\t"; fail++;
                    if (logged.Add(e.Message)) Debug.LogWarning("OFFLINE MOD CARD SWEEP: " + card.Name + " failed: " + e);   // full stack once per message
                }
                tsv.Append(Clean(card.Name)).Append('\t').Append(id).Append('\t').Append(card.Definition).Append('\t').Append(row).Append('\n');
                yield return null;
            }
            File.WriteAllText(path, tsv.ToString());
            Debug.Log("OFFLINE MOD CARD SWEEP: " + ok + " ok, " + fail + " failed -> " + path);
        }

        private static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }

        private static CardCommandState StateOf(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard card)
        {
            CardCommandList list = f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
            foreach (CardCommandState s in list.ValidCommands) if (s.CardNetUid == card.NetUid.Id) return s;
            return null;
        }

        private static void Conditions(GameConfiguration config, OriginalTargetedSummonTests.LifecycleFixture f, int me, int enemy, ServerCard foe)
        {
            f.Create(Crab, me, GameZoneName.Cemetery);
            int item = 0;
            foreach (CardSet set in config.CardCollection)
                foreach (Card c in set.Cards)
                    if (item == 0 && c != null && c.Definition == "Item" && (c.Name == "Steel Sword" || c.Name == "Iron Sword")) item = c.ResourceId.Id;
            if (item != 0)
            {
                ServerCard sword = f.CreateIn(item, enemy, GameZoneName.ItemBoard, 0);
                f.Event(delegate { CardActions.Equip.StaticExecute(DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), sword, foe, 0); });
            }
            foe.SetAttribute(AttributeType.Taunt, 1);   // Guard is Taunt in the game data
        }

        private static ServerCard Pick(OriginalTargetedSummonTests.LifecycleFixture f, IList<int> targets, ServerCard foe, ServerCard friend)
        {
            if (targets == null || targets.Count == 0) return null;
            if (targets.Contains(foe.NetUid.Id)) return foe;
            if (targets.Contains(friend.NetUid.Id)) return friend;
            return f.Sim.CardCatalog.FindServerCard(new CardNetUid(targets[0]));
        }

        // What the match is waiting for (for failure messages).
        private static string Waiting(OriginalTargetedSummonTests.LifecycleFixture f)
        {
            try
            {
                TurnState top = f.Sim._turnStateStack.Peek();
                StringBuilder b = new StringBuilder("stack=" + f.Sim._turnStateStack.Count + " seq=" + (top.CurrentSequence != null));
                if (top.CurrentCommandItem != null)
                    foreach (CardCommandState c in top.CurrentCommandItem.CommandList.ValidCommands)
                        b.Append(" [card " + c.CardNetUid + " mask " + c.ValidCardCommandMask + " play " + c.PlayTargets.Count + " lanes " + c.LaneTargets.Count + "]");
                return b.ToString();
            }
            catch (Exception e) { return "state? " + e.Message; }
        }

        private static void Step(OriginalTargetedSummonTests.LifecycleFixture f, string name, Action a)
        {
            try { a(); }
            catch (Exception e) { throw new InvalidOperationException(name + ": " + e.Message + " | " + Waiting(f)); }
        }

        private static string Sweep(GameConfiguration config, Card card)
        {
            using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
            {
                int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                ServerCard friend = f.Create(Crab, me, GameZoneName.CreatureBoard);
                ServerCard foe = f.Create(Crab, enemy, GameZoneName.CreatureBoard);
                ServerCard src = f.Create(card.ResourceId.Id, me, GameZoneName.Hand);
                f.Mana(me, 30); f.Refresh(me);
                CardCommandState s = StateOf(f, src);
                if (s == null)
                {   // Cards with a play condition (a creature in the discard pile, an enemy with an item, a target with a keyword):
                    // give the bench what they ask for, then try again (2026-11-13: Amaranth, Let Me Guess, Succubus' Kiss).
                    Conditions(config, f, me, enemy, foe);
                    f.Refresh(me);
                    s = StateOf(f, src);
                }
                if (s == null) throw new InvalidOperationException("not playable from hand with 30 magicka, even with a creature in the discard pile, an equipped enemy and a keyword");
                Step(f, "play", delegate {
                    switch (card.Definition)
                    {
                        case "Creature": f.PlayCreatureLane(src, 0, me); break;
                        case "Item": f.PlayEquip(src, friend, me); break;
                        default:
                            if (s.PlayTargets.Count == 0 && s.LaneTargets.Count > 0) f.PlayLane(src, s.LaneTargets[0], me);
                            else f.Play(src, Pick(f, s.PlayTargets, foe, friend), me);
                            break;
                    }
                });
                if (card.Definition == "Support")
                {
                    f.Refresh(me);
                    CardCommandState a = StateOf(f, src);
                    if (a != null && (a.ValidCardCommandMask & (int)CardCommand.ActiveAbility) != 0)
                        Step(f, "activate", delegate { f.Activate(src, Pick(f, a.ActiveAbilityTargets, foe, friend), me); });
                }
                if (card.Name == "Riverwood Chicken")
                {   // The enemy kills the chicken; at the start of our next turn our creature gets +4/+0 and the alarm is gone.
                    int before = friend.QueryAttribute(AttributeType.Attack).Value;
                    Step(f, "end turn", delegate { f.EndTurn(); });
                    f.Refresh(enemy);
                    Step(f, "enemy attacks chicken", delegate { f.Attack(foe, src, enemy); });
                    StringBuilder seen = new StringBuilder();
                    foreach (ServerCard c in f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return x.CardId != Crab && x.CardId != card.ResourceId.Id; }, f.Sim.Players[me].NetId, GameZoneName.SupportBoard))
                        seen.Append(c.CardId).Append('@').Append(c.CurrentGameZoneName).Append(' ');
                    Debug.Log("OFFLINE MOD CARD SWEEP: after the chicken died: supports " + seen + "chicken " + src.CurrentGameZoneName);
                    Step(f, "enemy end turn", delegate { f.EndTurn(); });
                    int after = friend.QueryAttribute(AttributeType.Attack).Value;
                    int alarms = f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId != card.ResourceId.Id && c.CurrentGameZoneName == GameZoneName.SupportBoard; }, f.Sim.Players[me].NetId, GameZoneName.SupportBoard).Count;
                    if (after != before + 4)
                        throw new InvalidOperationException("Riverwood rampage: power " + before + " -> " + after + ", supports left " + alarms + " (chicken " + src.CurrentGameZoneName + ")");
                    f.Refresh(me);
                    Step(f, "friend attacks", delegate { f.Attack(friend, f.Avatar(enemy), me); });
                    Step(f, "end turn", delegate { f.EndTurn(); });
                    if (friend.QueryAttribute(AttributeType.Attack).Value != before)
                        throw new InvalidOperationException("Riverwood rampage did not end at the end of our turn: " + friend.QueryAttribute(AttributeType.Attack).Value);
                }
                else Step(f, "end turn", delegate { f.EndTurn(); f.EndTurn(); });
                Func<ServerCard, string> st = delegate(ServerCard c) { return c.CurrentGameZoneName + " " + c.QueryAttribute(AttributeType.Attack).Value + "/" + c.CurrentHealth; };
                return "ok\t" + src.CurrentGameZoneName + "\t" + st(friend) + "\t" + st(foe) + "\t" + f.Avatar(me).CurrentHealth + "\t" + f.Avatar(enemy).CurrentHealth + "\t" + f.HandCount(me);
            }
        }
    }
}
