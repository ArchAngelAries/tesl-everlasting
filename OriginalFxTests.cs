using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Card-specific animations reach the client's history (owner report 2026-11-06: Mudcrab Anklesnapper's crab run missing).
    public static class OriginalFxTests
    {
        private const int Mudcrab = 620729069, Anklesnapper = -602835722, CrabRun = -1100280150;

        public static void Run(GameConfiguration config, Action<string, Action> test, string output)
        {
            test("original-fx-anklesnapper-plays-its-crab-run", delegate
            {
                List<HistoryItem> h = PlayAndLog(config, Anklesnapper);
                Require(Has(h, HistoryType.ActionPlayCustomFx, CrabRun), "no ActionPlayCustomFx " + CrabRun + " in the history: " + Types(h));
            });
            test("original-fx-original-graph-summon-plays-its-custom-fx", delegate
            {
                // any creature that runs its original graph with a Summon PlayCustomFx
                int id = 0, hash = 0;
                foreach (CardSet set in config.CardCollection)
                {
                    foreach (Card card in set.Cards)
                    {
                        if (card == null || !card.HasSubtype(CardSubtype.Creature) || card.ResourceId.Id == Anklesnapper) continue;
                        Card src = OriginalGraphRules.Original(card.ResourceId.Id);
                        if (src == null || !Object.ReferenceEquals(card.TriggeredEffects, src.TriggeredEffects)) continue;
                        foreach (TriggeredEffect t in card.TriggeredEffects)
                            if (t != null && t.TriggerType == EffectTriggerType.Summon && t.TriggerCardRequirements.Count <= 1 && t.EffectGroup.Effects.Count == 1
                                && t.EffectGroup.Effects[0].CardActions.Count > 0 && t.EffectGroup.Effects[0].CardActions[0].Action is CardActions.PlayCustomFx
                                && t.EffectGroup.Effects[0].ExecuteCardRequirements.Count == 0 && t.EffectGroup.Effects[0].ExecutePlayerRequirements.Count == 0
                                && SelfOnly(t.EffectGroup.Effects[0].CardActions[0].Selectors))
                            { id = card.ResourceId.Id; hash = t.EffectGroup.Effects[0].CardActions[0].HashId; }
                        if (id != 0) break;
                    }
                    if (id != 0) break;
                }
                Require(id != 0, "no original-graph creature with a plain Summon PlayCustomFx found");
                List<HistoryItem> h = PlayAndLog(config, id);
                Require(Has(h, HistoryType.ActionPlayCustomFx, hash) || Has(h, HistoryType.ActionPossibleCustomFx, hash),
                    id + ": no custom FX " + hash + " in the history: " + Types(h));
            });
            test("original-fx-overlay-report", delegate
            {
                // every rebuilt card whose original has card-specific FX: which original FX hashes our rules now carry
                StringBuilder report = new StringBuilder("card_id\tname\toriginal_fx\tcarried\tmissing\tours\toriginal\n");
                int cards = 0, full = 0;
                foreach (CardSet set in config.CardCollection)
                    foreach (Card card in set.Cards)
                    {
                        if (card == null) continue;
                        Card src = OriginalGraphRules.Original(card.ResourceId.Id);
                        if (src == null || src.Name != card.Name || Object.ReferenceEquals(card.TriggeredEffects, src.TriggeredEffects)) continue;
                        List<int> want = Hashes(src), have = Hashes(card);
                        if (want.Count == 0) continue;
                        cards++;
                        List<string> missing = new List<string>();
                        foreach (int x in want) if (!have.Remove(x)) missing.Add(x.ToString());
                        if (missing.Count == 0) full++;
                        report.Append(card.ResourceId.Id).Append('\t').Append(card.Name).Append('\t').Append(want.Count).Append('\t')
                            .Append(want.Count - missing.Count).Append('\t').Append(string.Join(",", missing.ToArray())).Append('\t')
                            .Append(missing.Count > 0 ? Layout(card) : "").Append('\t').Append(missing.Count > 0 ? Layout(src) : "").Append('\n');
                    }
                File.WriteAllText(Path.Combine(output, "original-fx-overlay.tsv"), report.ToString());
                Require(cards > 0 && full * 10 >= cards * 9, "only " + full + " of " + cards + " rebuilt cards carry all their original FX (exports/original-fx-overlay.tsv)");
            });
        }

        private static List<HistoryItem> PlayAndLog(GameConfiguration config, int id)
        {
            using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
            {
                Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                ServerCard card = f.Create(id, me, GameZoneName.Hand);
                f.CreateIn(Mudcrab, 1 - me, GameZoneName.CreatureBoard, 0);
                f.Mana(me, 20); f.Refresh(me);
                f.PlayCreatureLane(card, 1, me);
                List<HistoryItem> list = new List<HistoryItem>();
                foreach (HistoryItem h in s.HistoryLog.GenerateFullReplayForPlayer(s.PlayerServiceClients[me], HistoryLog.FullGameStateSendRule.Exclude).Queue) list.Add(h);
                return list;
            }
        }

        private static string Layout(Card c)
        {
            StringBuilder b = new StringBuilder();
            if (c.TriggeredEffects != null) foreach (TriggeredEffect t in c.TriggeredEffects) if (t != null) Group(b, "T:" + t.TriggerType, t.EffectGroup);
            if (c.ActionPlayInformation != null) foreach (PlayCardAbility p in c.ActionPlayInformation) if (p != null) Group(b, "P", p.EffectGroup);
            if (c.ActivatedAbility != null && c.ActivatedAbility.AbilityGroups != null) foreach (AbilityGroup g in c.ActivatedAbility.AbilityGroups) if (g != null) Group(b, "A", g.EffectGroup);
            if (c.Auras != null && c.Auras.Count > 0) b.Append("auras:" + c.Auras.Count + " ");
            return b.ToString();
        }

        private static void Group(StringBuilder b, string key, EffectGroup g)
        {
            b.Append(key).Append('[');
            if (g != null) foreach (Effect e in g.Effects) if (e != null)
            {
                foreach (CardEffectAction a in e.CardActions) if (a != null && a.Action != null) b.Append(a.Action.GetType().Name).Append(a.HashId != -1 ? "*" : "").Append(',');
                foreach (PlayerEffectAction a in e.PlayerActions) if (a != null && a.Action != null) b.Append("p:").Append(a.Action.GetType().Name).Append(a.HashId != -1 ? "*" : "").Append(',');
                b.Append('|');
            }
            b.Append("] ");
        }

        private static List<int> Hashes(Card c)
        {
            List<int> l = new List<int>();
            List<EffectGroup> groups = new List<EffectGroup>();
            if (c.TriggeredEffects != null) foreach (TriggeredEffect t in c.TriggeredEffects) if (t != null) groups.Add(t.EffectGroup);
            if (c.ActionPlayInformation != null) foreach (PlayCardAbility p in c.ActionPlayInformation) if (p != null) groups.Add(p.EffectGroup);
            if (c.ActivatedAbility != null && c.ActivatedAbility.AbilityGroups != null) foreach (AbilityGroup g in c.ActivatedAbility.AbilityGroups) if (g != null) groups.Add(g.EffectGroup);
            foreach (EffectGroup g in groups)
                if (g != null)
                    foreach (Effect e in g.Effects)
                        if (e != null)
                        {
                            foreach (CardEffectAction a in e.CardActions) if (a != null && a.HashId != -1 && a.HashId != 0) l.Add(a.HashId);
                            foreach (PlayerEffectAction a in e.PlayerActions)
                                if (a != null)
                                {
                                    int v = a.HashId;
                                    PlayerActions.DamageAvatarOfPlayer d = a.Action as PlayerActions.DamageAvatarOfPlayer;
                                    PlayerActions.BreakRunesOfPlayer r = a.Action as PlayerActions.BreakRunesOfPlayer;
                                    if (d != null && d.AssetHash != -1 && d.AssetHash != 0) v = d.AssetHash;
                                    if (r != null && r.AssetHash != -1 && r.AssetHash != 0) v = r.AssetHash;
                                    if (v != -1 && v != 0) l.Add(v);
                                }
                        }
            return l;
        }

        // the FX plays whenever the Summon does (no board condition on its selectors)
        private static bool SelfOnly(List<ICardConditionalExpression> selectors)
        {
            foreach (ICardConditionalExpression x in selectors) if (!(x is CardConditionals.CardRoleType) && !(x is CardConditionals.Zone)) return false;
            return true;
        }

        private static bool Has(List<HistoryItem> h, HistoryType type, int hash)
        {
            foreach (HistoryItem i in h) if (i.Type == type && i.AssetHash == hash) return true;
            return false;
        }

        private static string Types(List<HistoryItem> h)
        {
            StringBuilder b = new StringBuilder();
            foreach (HistoryItem i in h) if (i.Type.ToString().StartsWith("Action")) b.Append(i.Type).Append(':').Append(i.AssetHash).Append(' ');
            return b.ToString();
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
