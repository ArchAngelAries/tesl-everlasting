using System;
using System.Collections.Generic;
using System.IO;
using FullSerializer;

namespace OfflineEngine
{
    // Mod compatibility (owner direction 2026-11-04): mods that change the same thing must not break each other or the game.
    // The enabled list (<suite>/deploy/active-mods.json) is the load order; where two mods change the same thing the later
    // one wins (image and sound replacements, a base board's replacement). Before anything loads, every manifest (mod.json)
    // is read and the plan decides what loads and in which order:
    //   - a mod enabled twice (same id) loads once (the first);
    //   - "requires_engine": a mod needing a newer engine (ApiLevel) is skipped;
    //   - "dependencies": [{"id", "version"}]: a mod whose dependency is missing, disabled, skipped or of the wrong version is
    //     skipped; dependencies load before the mods that need them;
    //   - "conflicts": [ids]: when both are enabled, the later one in the list is skipped;
    //   - "load_after" / "load_before": [ids] order hints (dependencies imply load_after).
    // Every skip is a Problem (shown in the suite and the F10 menu); the game always continues.
    public static class OfflineCompat
    {
        // Engine feature level: the checkpoint that added the newest mod feature (56: card back, deck banner and board previews;
        // 50: window/quality presets; 49: Master stories).
        public const int ApiLevel = 56;

        public sealed class Manifest
        {
            public string Id, Name, Version = "1.0.0", Dir; public int RequiresEngine;
            public readonly List<KeyValuePair<string, string>> Dependencies = new List<KeyValuePair<string, string>>();
            public readonly List<string> Conflicts = new List<string>(), LoadAfter = new List<string>(), LoadBefore = new List<string>();
        }

        private static List<string> Strings(Dictionary<string, fsData> d, string key)
        {
            List<string> list = new List<string>();
            if (d.ContainsKey(key) && d[key].IsList) foreach (fsData e in d[key].AsList) if (e.IsString) list.Add(e.AsString);
            return list;
        }

        public static Manifest Read(string dir)
        {
            Dictionary<string, fsData> d = fsJsonParser.Parse(File.ReadAllText(Path.Combine(dir, "mod.json"))).AsDictionary;
            Manifest m = new Manifest { Dir = dir, Id = d["id"].AsString };
            m.Name = d.ContainsKey("name") && d["name"].IsString ? d["name"].AsString : m.Id;
            if (d.ContainsKey("version") && d["version"].IsString) m.Version = d["version"].AsString;
            if (d.ContainsKey("requires_engine") && d["requires_engine"].IsInt64) m.RequiresEngine = (int)d["requires_engine"].AsInt64;
            if (d.ContainsKey("dependencies") && d["dependencies"].IsList)
                foreach (fsData e in d["dependencies"].AsList)
                {
                    if (e.IsString) m.Dependencies.Add(new KeyValuePair<string, string>(e.AsString, "*"));
                    else if (e.IsDictionary && e.AsDictionary.ContainsKey("id"))
                        m.Dependencies.Add(new KeyValuePair<string, string>(e.AsDictionary["id"].AsString,
                            e.AsDictionary.ContainsKey("version") && e.AsDictionary["version"].IsString ? e.AsDictionary["version"].AsString : "*"));
                }
            m.Conflicts.AddRange(Strings(d, "conflicts"));
            m.LoadAfter.AddRange(Strings(d, "load_after"));
            m.LoadBefore.AddRange(Strings(d, "load_before"));
            return m;
        }

        // The mods to load, in load order. Problems explain every skipped mod and broken hint.
        public static List<Manifest> Plan(List<string> dirs, List<string> problems)
        {
            List<Manifest> kept = new List<Manifest>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                Manifest m;
                try { m = Read(dir); }
                catch (Exception e) { problems.Add(dir + ": mod.json unreadable, mod skipped (" + e.Message + ")"); continue; }
                if (!seen.Add(m.Id)) { problems.Add(m.Id + ": enabled twice (" + dir + "); only the first copy loads"); continue; }
                if (m.RequiresEngine > ApiLevel) { problems.Add(m.Id + ": needs a newer TESL: Everlasting (level " + m.RequiresEngine + ", this is " + ApiLevel + "); skipped"); continue; }
                kept.Add(m);
            }
            // Declared conflicts: the later of two conflicting mods is skipped.
            for (int i = 0; i < kept.Count; i++)
                for (int j = 0; j < i; j++)
                    if (Contains(kept[i].Conflicts, kept[j].Id) || Contains(kept[j].Conflicts, kept[i].Id))
                    {
                        problems.Add(kept[i].Id + ": conflicts with " + kept[j].Id + " (enabled earlier); skipped");
                        kept.RemoveAt(i); i--; break;
                    }
            // Dependencies, until stable (a skipped mod can break the mods that need it).
            for (bool changed = true; changed; )
            {
                changed = false;
                for (int i = 0; i < kept.Count; i++)
                    foreach (KeyValuePair<string, string> dep in kept[i].Dependencies)
                    {
                        Manifest found = kept.Find(delegate(Manifest x) { return string.Equals(x.Id, dep.Key, StringComparison.OrdinalIgnoreCase); });
                        string why = found == null ? "needs " + dep.Key + ", which is not enabled" : !Satisfies(found.Version, dep.Value) ? "needs " + dep.Key + " " + dep.Value + " (enabled: " + found.Version + ")" : null;
                        if (why == null) continue;
                        problems.Add(kept[i].Id + ": " + why + "; skipped");
                        kept.RemoveAt(i); i--; changed = true; break;
                    }
            }
            return Order(kept, problems);
        }

        private static bool Contains(List<string> list, string id) { return list.Exists(delegate(string x) { return string.Equals(x, id, StringComparison.OrdinalIgnoreCase); }); }

        // Stable order: the enabled-list order, moved only as far as dependencies and load_after/load_before require.
        private static List<Manifest> Order(List<Manifest> mods, List<string> problems)
        {
            Dictionary<string, List<string>> before = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);   // id -> ids that must load first
            foreach (Manifest m in mods) before[m.Id] = new List<string>();
            foreach (Manifest m in mods)
            {
                foreach (KeyValuePair<string, string> d in m.Dependencies) if (before.ContainsKey(d.Key)) before[m.Id].Add(d.Key);
                foreach (string a in m.LoadAfter) if (before.ContainsKey(a)) before[m.Id].Add(a);
                foreach (string b in m.LoadBefore) if (before.ContainsKey(b)) before[b].Add(m.Id);
            }
            List<Manifest> result = new List<Manifest>(), left = new List<Manifest>(mods);
            HashSet<string> placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (left.Count > 0)
            {
                Manifest next = left.Find(delegate(Manifest m) { return before[m.Id].TrueForAll(delegate(string x) { return placed.Contains(x); }); });
                if (next == null)
                {
                    next = left[0];
                    problems.Add(next.Id + ": load order hints form a loop (" + string.Join(", ", before[next.Id].ToArray()) + "); kept in list order");
                }
                result.Add(next); placed.Add(next.Id); left.Remove(next);
            }
            return result;
        }

        // Versions "major.minor.patch" (missing parts are 0). Ranges: "*", "1.2" / "=1.2.3" (exact), ">=1.2", ">1.2", "<=2", "<2.0",
        // "^1.2" (same major, at least 1.2), "~1.2" (same major.minor, at least), several separated by spaces (all must hold).
        public static bool Satisfies(string version, string range)
        {
            if (string.IsNullOrEmpty(range) || range.Trim() == "*") return true;
            foreach (string raw in range.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string r = raw.Trim(); int[] v = Parse(version);
                if (r.StartsWith(">=")) { if (Compare(v, Parse(r.Substring(2))) < 0) return false; }
                else if (r.StartsWith("<=")) { if (Compare(v, Parse(r.Substring(2))) > 0) return false; }
                else if (r.StartsWith(">")) { if (Compare(v, Parse(r.Substring(1))) <= 0) return false; }
                else if (r.StartsWith("<")) { if (Compare(v, Parse(r.Substring(1))) >= 0) return false; }
                else if (r.StartsWith("^")) { int[] w = Parse(r.Substring(1)); if (v[0] != w[0] || Compare(v, w) < 0) return false; }
                else if (r.StartsWith("~")) { int[] w = Parse(r.Substring(1)); if (v[0] != w[0] || v[1] != w[1] || Compare(v, w) < 0) return false; }
                else { int[] w = Parse(r.TrimStart('=')); int parts = r.TrimStart('=').Split('.').Length; for (int i = 0; i < parts && i < 3; i++) if (v[i] != w[i]) return false; }
            }
            return true;
        }

        public static int[] Parse(string version)
        {
            int[] v = new int[3];
            string[] parts = (version ?? "0").Trim().Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++)
            {
                string digits = "";
                foreach (char c in parts[i]) { if (char.IsDigit(c)) digits += c; else break; }
                int n; v[i] = int.TryParse(digits, out n) ? n : 0;
            }
            return v;
        }

        public static int Compare(int[] a, int[] b) { for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1; return 0; }
    }
}
