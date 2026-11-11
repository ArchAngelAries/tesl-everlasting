using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SPTESL;

namespace OfflineEngine
{
    // Cards that run our rebuilt rules (not their original graph) lost the original's card-specific animations: the
    // PlayCustomFx actions (Mudcrab Anklesnapper's crab run, owner report 2026-11-06) and the HashId that picks a special
    // FX for a Damage/CreateToken/Buff action (-1 = the generic default). This copies only those visual parts from the
    // original graph into ours; the rules themselves are untouched. Groups are matched by trigger type (n-th of a type),
    // play ability or activated ability group; actions by type, in order. Each changed card gets its own shallow copies,
    // because rebuilt cards can share rule objects.
    public static class OriginalFxOverlay
    {
        public static int CardsChanged { get; private set; }
        public static int FxCopied { get; private set; }
        public static int FxUnplaced { get; private set; }

        private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static T Copy<T>(T o) where T : class { return o == null ? null : (T)Clone.Invoke(o, null); }

        public static void Apply(GameConfiguration config, StringBuilder report)
        {
            CardsChanged = 0; FxCopied = 0; FxUnplaced = 0; changed.Clear();
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    if (card == null) continue;
                    Card src = OriginalGraphRules.Original(card.ResourceId.Id);
                    if (src == null || src.Name != card.Name || !HasFx(src)) continue;
                    if (Object.ReferenceEquals(card.TriggeredEffects, src.TriggeredEffects)) continue;   // runs the original graph
                    int copied = 0, unplaced = 0;
                    OriginalGraphRules.Fields plain = OriginalGraphRules.Capture(card);
                    OwnCopies(card);
                    Dictionary<string, EffectGroup> ours = Groups(card), theirs = Groups(src);
                    List<EffectGroup> all = new List<EffectGroup>(ours.Values);
                    Dictionary<object, bool> claimed = new Dictionary<object, bool>();
                    foreach (KeyValuePair<string, EffectGroup> g in theirs)
                    {
                        EffectGroup mine;
                        if (!ours.TryGetValue(g.Key, out mine)) mine = Fallback(g.Key, ours);
                        Overlay(g.Value, mine, all, claimed, ref copied, ref unplaced);
                    }
                    if (copied > 0) { CardsChanged++; FxCopied += copied; changed.Add(new Change { Card = card, Plain = plain, Fx = OriginalGraphRules.Capture(card) }); }
                    else OriginalGraphRules.Put(card, plain);
                    FxUnplaced += unplaced;
                    if (copied > 0 || unplaced > 0)
                        report.AppendLine(card.ResourceId.Id + "\t" + card.Name + "\toriginal-fx-overlay\t" + copied + " original FX copied into our rules"
                            + (unplaced > 0 ? ", " + unplaced + " with no matching action" : ""));
                }
            UnityEngine.Debug.Log("ORIGINAL FX OVERLAY: " + CardsChanged + " cards, " + FxCopied + " FX copied, " + FxUnplaced + " unplaced.");
        }

        private sealed class Change { public Card Card; public OriginalGraphRules.Fields Plain, Fx; }
        private static readonly List<Change> changed = new List<Change>();

        // The hand-written batches' exact-shape checks (Validate) see their rules as built, without the copied FX.
        public static IDisposable Hide()
        {
            foreach (Change c in changed) OriginalGraphRules.Put(c.Card, c.Plain);
            return new Restore();
        }

        private sealed class Restore : IDisposable
        {
            private bool done;
            public void Dispose()
            {
                if (done) return;
                done = true;
                foreach (Change c in changed) OriginalGraphRules.Put(c.Card, c.Fx);
            }
        }

        private static bool HasFx(Card c)
        {
            foreach (EffectGroup g in Groups(c).Values)
                foreach (Effect e in g.Effects)
                {
                    foreach (CardEffectAction a in e.CardActions) if (a != null && (a.Action is CardActions.PlayCustomFx || a.HashId != -1)) return true;
                    foreach (PlayerEffectAction a in e.PlayerActions) if (a != null && a.HashId != -1) return true;
                }
            return false;
        }

        // Keys: "T:<trigger>#n", "P#n" (play abilities), "A#n" (activated ability groups).
        private static Dictionary<string, EffectGroup> Groups(Card c)
        {
            Dictionary<string, EffectGroup> d = new Dictionary<string, EffectGroup>();
            Dictionary<EffectTriggerType, int> seen = new Dictionary<EffectTriggerType, int>();
            if (c.TriggeredEffects != null)
                foreach (TriggeredEffect t in c.TriggeredEffects)
                {
                    if (t == null || t.EffectGroup == null) continue;
                    int n; seen.TryGetValue(t.TriggerType, out n); seen[t.TriggerType] = n + 1;
                    d["T:" + t.TriggerType + "#" + n] = t.EffectGroup;
                }
            if (c.ActionPlayInformation != null)
                for (int i = 0; i < c.ActionPlayInformation.Count; i++)
                    if (c.ActionPlayInformation[i] != null && c.ActionPlayInformation[i].EffectGroup != null) d["P#" + i] = c.ActionPlayInformation[i].EffectGroup;
            if (c.ActivatedAbility != null && c.ActivatedAbility.AbilityGroups != null)
                for (int i = 0; i < c.ActivatedAbility.AbilityGroups.Count; i++)
                    if (c.ActivatedAbility.AbilityGroups[i] != null && c.ActivatedAbility.AbilityGroups[i].EffectGroup != null) d["A#" + i] = c.ActivatedAbility.AbilityGroups[i].EffectGroup;
            return d;
        }

        // A rebuilt card may run an original Summon as a play ability, or the reverse.
        private static EffectGroup Fallback(string key, Dictionary<string, EffectGroup> ours)
        {
            EffectGroup g;
            if (key == "T:" + EffectTriggerType.Summon + "#0" && ours.TryGetValue("P#0", out g)) return g;
            if (key == "P#0" && ours.TryGetValue("T:" + EffectTriggerType.Summon + "#0", out g)) return g;
            return null;
        }

        // Each original FX goes to our matching group first, then to any of our groups (a rebuilt rule may use a sibling
        // trigger such as WhenPlayerTurnStarts for WhenPlayerTurnStartsCleanup, or another activated ability group).
        private static void Overlay(EffectGroup original, EffectGroup mine, List<EffectGroup> all, Dictionary<object, bool> claimed, ref int copied, ref int unplaced)
        {
            List<EffectGroup> order = new List<EffectGroup>();
            if (mine != null) order.Add(mine);
            foreach (EffectGroup g in all) if (!order.Contains(g)) order.Add(g);
            foreach (Effect oe in original.Effects)
            {
                if (oe == null) continue;
                for (int i = 0; i < oe.CardActions.Count; i++)
                {
                    CardEffectAction a = oe.CardActions[i];
                    if (a == null || a.Action == null) continue;
                    if (a.Action is CardActions.PlayCustomFx)
                    {
                        if (HasCustomFx(order, a.HashId, claimed)) { copied++; continue; }
                        // before our first action of the type that follows it in the original effect
                        Type next = null;
                        for (int j = i + 1; j < oe.CardActions.Count && next == null; j++)
                            if (oe.CardActions[j] != null && oe.CardActions[j].Action != null && !(oe.CardActions[j].Action is CardActions.PlayCustomFx)) next = oe.CardActions[j].Action.GetType();
                        if (next == null && oe.PlayerActions.Count > 0 && oe.PlayerActions[0] != null && oe.PlayerActions[0].Action != null) next = oe.PlayerActions[0].Action.GetType();
                        bool done = false;
                        if (next != null) foreach (EffectGroup g in order) if (!done && Insert(g, a, next, false)) done = true;
                        if (!done && mine != null) done = Insert(mine, a, null, true);
                        if (done) { claimed[a] = true; copied++; } else unplaced++;
                    }
                    else if (a.HashId != -1)
                    {
                        if (Place(order, a.Action.GetType(), Kind(a.Action), Visual(a), claimed)) copied++; else unplaced++;
                    }
                }
                foreach (PlayerEffectAction a in oe.PlayerActions)
                    if (a != null && a.Action != null && a.HashId != -1)
                    {
                        if (Place(order, a.Action.GetType(), Kind(a.Action), Visual(a), claimed)) copied++; else unplaced++;
                    }
            }
        }

        private static bool HasCustomFx(List<EffectGroup> groups, int hash, Dictionary<object, bool> claimed)
        {
            foreach (EffectGroup g in groups)
                foreach (Effect e in g.Effects)
                    if (e != null)
                        foreach (CardEffectAction a in e.CardActions)
                            if (a != null && a.Action is CardActions.PlayCustomFx && a.HashId == hash && !claimed.ContainsKey(a)) { claimed[a] = true; return true; }
            return false;
        }

        private static bool Insert(EffectGroup g, CardEffectAction fx, Type before, bool anywhere)
        {
            if (before != null)
                foreach (Effect e in g.Effects)
                {
                    if (e == null) continue;
                    for (int k = 0; k < e.CardActions.Count; k++)
                        if (e.CardActions[k] != null && e.CardActions[k].Action != null && e.CardActions[k].Action.GetType() == before) { e.CardActions.Insert(k, fx); return true; }
                    foreach (PlayerEffectAction p in e.PlayerActions)
                        if (p != null && p.Action != null && p.Action.GetType() == before) { e.CardActions.Add(fx); return true; }
                }
            if (!anywhere) return false;
            // no matching action: the first effect that does something
            foreach (Effect e in g.Effects)
                if (e != null && (e.CardActions.Count > 0 || e.PlayerActions.Count > 0)) { e.CardActions.Insert(0, fx); return true; }
            return false;
        }

        // The original's FX replaces ours (the generic default, or none) on the first unclaimed action of the same type, else
        // of the same FX kind (a player DamageAvatarOfPlayer for a card Damage on the avatar, our Ward for a Buff, ...).
        private static bool Place(List<EffectGroup> groups, Type type, CardFxAction fx, int hash, Dictionary<object, bool> claimed)
        {
            foreach (EffectGroup g in groups) if (SetHash(g, type, CardFxAction.CountOf, hash, claimed)) return true;
            if (fx == CardFxAction.CountOf) return false;
            foreach (EffectGroup g in groups) if (SetHash(g, null, fx, hash, claimed)) return true;
            return false;
        }

        // FX kind for matching: player avatar damage counts as Damage, our Ward as the original's ward Buff.
        private static CardFxAction Kind(ICardAction a)
        {
            if (a is CardActions.Ward) return CardFxAction.Buff;
            return a.GetFxActionType();
        }

        private static CardFxAction Kind(IPlayerAction a)
        {
            if (a is PlayerActions.DamageAvatarOfPlayer) return CardFxAction.Damage;
            return a.GetFxActionType();
        }

        // The FX an original action shows: DamageAvatarOfPlayer and BreakRunesOfPlayer log their own AssetHash field.
        private static int Visual(CardEffectAction a) { return a.HashId; }

        private static int Visual(PlayerEffectAction a)
        {
            PlayerActions.DamageAvatarOfPlayer d = a.Action as PlayerActions.DamageAvatarOfPlayer;
            if (d != null && d.AssetHash != -1 && d.AssetHash != 0) return d.AssetHash;
            PlayerActions.BreakRunesOfPlayer r = a.Action as PlayerActions.BreakRunesOfPlayer;
            if (r != null && r.AssetHash != -1 && r.AssetHash != 0) return r.AssetHash;
            return a.HashId;
        }

        // Those two read the field, not the action's HashId: a per-card copy of the action carries the FX.
        private static IPlayerAction WithOwnHash(IPlayerAction a, int hash)
        {
            PlayerActions.DamageAvatarOfPlayer d = a as PlayerActions.DamageAvatarOfPlayer;
            if (d != null) { d = Copy(d); d.AssetHash = hash; return d; }
            PlayerActions.BreakRunesOfPlayer r = a as PlayerActions.BreakRunesOfPlayer;
            if (r != null) { r = Copy(r); r.AssetHash = hash; return r; }
            return a;
        }

        private static bool SetHash(EffectGroup g, Type type, CardFxAction fx, int hash, Dictionary<object, bool> claimed)
        {
            foreach (Effect e in g.Effects)
            {
                if (e == null) continue;
                foreach (CardEffectAction a in e.CardActions)
                    if (a != null && a.Action != null && !(a.Action is CardActions.PlayCustomFx) && !claimed.ContainsKey(a)
                        && (type != null ? a.Action.GetType() == type : Kind(a.Action) == fx)) { a.HashId = hash; claimed[a] = true; return true; }
                foreach (PlayerEffectAction a in e.PlayerActions)
                    if (a != null && a.Action != null && !claimed.ContainsKey(a)
                        && (type != null ? a.Action.GetType() == type : Kind(a.Action) == fx)) { a.HashId = hash; a.Action = WithOwnHash(a.Action, hash); claimed[a] = true; return true; }
            }
            return false;
        }


        // Fresh containers down to the actions (the ICardAction objects themselves stay shared; only HashId and list order change).
        private static void OwnCopies(Card card)
        {
            if (card.TriggeredEffects != null)
            {
                List<TriggeredEffect> list = new List<TriggeredEffect>();
                foreach (TriggeredEffect t in card.TriggeredEffects)
                {
                    TriggeredEffect c = Copy(t);
                    if (c != null) c.EffectGroup = CopyGroup(t.EffectGroup);
                    list.Add(c);
                }
                card.TriggeredEffects = list;
            }
            if (card.ActionPlayInformation != null)
            {
                List<PlayCardAbility> list = new List<PlayCardAbility>();
                foreach (PlayCardAbility p in card.ActionPlayInformation)
                {
                    PlayCardAbility c = Copy(p);
                    if (c != null) c.EffectGroup = CopyGroup(p.EffectGroup);
                    list.Add(c);
                }
                card.ActionPlayInformation = list;
            }
            if (card.ActivatedAbility != null && card.ActivatedAbility.AbilityGroups != null)
            {
                ActivatedAbility ab = Copy(card.ActivatedAbility);
                ab.AbilityGroups = new List<AbilityGroup>();
                foreach (AbilityGroup g in card.ActivatedAbility.AbilityGroups)
                {
                    AbilityGroup c = Copy(g);
                    if (c != null) c.EffectGroup = CopyGroup(g.EffectGroup);
                    ab.AbilityGroups.Add(c);
                }
                card.ActivatedAbility = ab;
            }
        }

        private static EffectGroup CopyGroup(EffectGroup g)
        {
            if (g == null) return null;
            EffectGroup c = Copy(g);
            c.Effects = new List<Effect>();
            foreach (Effect e in g.Effects)
            {
                Effect ce = Copy(e);
                if (ce != null)
                {
                    ce.CardActions = new List<CardEffectAction>();
                    foreach (CardEffectAction a in e.CardActions) ce.CardActions.Add(Copy(a));
                    ce.PlayerActions = new List<PlayerEffectAction>();
                    foreach (PlayerEffectAction a in e.PlayerActions) ce.PlayerActions.Add(Copy(a));
                }
                c.Effects.Add(ce);
            }
            return c;
        }
    }
}
