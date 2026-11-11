using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FullSerializer;
using SPTESL;

namespace OfflineEngine
{
    // Original card rule graphs (data/original-rules/SOURCE.md). The shipped binary configuration strips twelve
    // [NonSerialized] Card fields; this file restores them from the original JSON form, decoded by the game's own
    // FullSerializer (as GameConfiguration.LoadJSONString<T>, but with a config that reads [NonSerialized] members).
    //  - Cards none of our rule batches support get every stripped field from the original ("original-graph").
    //  - Cards with no original rules and no rules of ours are confirmed vanilla ("original-vanilla").
    //  - Cards we already support keep our graphs unless listed in prefer-original.tsv; the stripped non-graph fields
    //    (AI PlayWeight, HuntTypes, UpgradesInto, MobilizeRecruitId, ArenaDraftSubtypes) are filled where ours are empty.
    // Cards that fail our tests with the original graph are listed in excluded.tsv and stay as they were.
    public static class OriginalGraphRules
    {
        public const string Folder = "data/original-rules";
        private static Dictionary<int, Card> originals = new Dictionary<int, Card>();
        public static readonly List<int> ImportedIds = new List<int>();
        public static readonly List<int> VanillaIds = new List<int>();
        public static readonly List<int> OverlapIds = new List<int>();
        public static readonly List<int> PreferredIds = new List<int>();
        public static int FxAdded { get; private set; }

        public static bool HasOriginal(int id) { return originals.ContainsKey(id); }
        // Text-compiled cards whose original graph replaces the compiled spec (prefer-original.tsv, read in Load).
        private static Dictionary<int, string> preferred = new Dictionary<int, string>();
        public static bool Prefers(int id) { return preferred.ContainsKey(id) && originals.ContainsKey(id); }
        public static Card Original(int id) { Card c; return originals.TryGetValue(id, out c) ? c : null; }

        public static string Root(string exportDirectory)
        {
            return OfflinePaths.DataNear(exportDirectory, "original-rules");
        }

        // One object in the original JSON form (also used for mod cards): the game's FullSerializer with a config that
        // reads [NonSerialized] members, after priming fsTypeCache. Top-level keys starting with '_' are authoring
        // metadata (e.g. the suite's "_mod") and are dropped before decoding.
        public static T Decode<T>(string json) where T : class
        {
            PrimeTypes(json);
            fsData data = fsJsonParser.Parse(json);
            if (data.IsDictionary)
            {
                List<string> drop = new List<string>();
                foreach (string key in data.AsDictionary.Keys) if (key.StartsWith("_", StringComparison.Ordinal)) drop.Add(key);
                foreach (string key in drop) data.AsDictionary.Remove(key);
            }
            fsSerializer serializer = new fsSerializer();
            serializer.Config = new fsConfig();
            serializer.Config.IgnoreSerializeAttributes = new Type[] { typeof(fsIgnoreAttribute) };
            object result = null;
            fsResult status = serializer.TryDeserialize(data, typeof(T), ref result);
            if (status.Failed) throw new InvalidOperationException("JSON did not decode as " + typeof(T).Name + ": " + status.FormattedMessages);
            return (T)result;
        }

        // Decode every original card (main thread, before the rule batches commit).
        // Decoding all original graphs takes ~6 s on the main thread (measured 2026-11-04: a 7.7 s freeze at the main menu).
        // Predecode spreads it over frames (a few ms each) right after the configuration loads; Load then reuses the result.
        // If a match needs the engine first, Load decodes the rest synchronously (never a half-filled table).
        private static Dictionary<int, Card> predecoded;
        private static bool predecodeAbandoned;

        public static IEnumerator Predecode(string exportDirectory, float budgetMs)
        {
            string path = Path.Combine(Root(exportDirectory), "card-rule-graphs.jsonl.gz");
            if (!File.Exists(path) || predecoded != null) yield break;
            predecodeAbandoned = false;
            Dictionary<int, Card> result = new Dictionary<int, Card>();
            fsSerializer serializer = NewSerializer();
            System.Diagnostics.Stopwatch slice = System.Diagnostics.Stopwatch.StartNew(), total = System.Diagnostics.Stopwatch.StartNew();
            int frames = 0;
            // Line by line (one card set per line), so reading the 50 MB file is spread over frames too.
            List<string> lines = new List<string>();
            using (FileStream stream = File.OpenRead(path))
            using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
            {
                string read;
                while ((read = reader.ReadLine()) != null)
                {
                    lines.Add(read);
                    if (slice.ElapsedMilliseconds > budgetMs) { yield return null; frames++; slice = System.Diagnostics.Stopwatch.StartNew(); if (predecodeAbandoned) yield break; }
                }
            }
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                PrimeTypes(line);
                fsData set = fsJsonParser.Parse(line);
                if (slice.ElapsedMilliseconds > budgetMs) { yield return null; frames++; slice = System.Diagnostics.Stopwatch.StartNew(); if (predecodeAbandoned) yield break; }
                if (!set.IsDictionary || !set.AsDictionary.ContainsKey("Cards")) continue;
                foreach (fsData entry in set.AsDictionary["Cards"].AsList)
                {
                    object card = null;
                    fsResult status = serializer.TryDeserialize(entry, typeof(Card), ref card);
                    if (status.Failed) throw new InvalidOperationException("Original card did not decode: " + status.FormattedMessages);
                    Card c = (Card)card;
                    if (c != null && c.ResourceId.Id != 0 && !result.ContainsKey(c.ResourceId.Id)) result.Add(c.ResourceId.Id, c);
                    if (slice.ElapsedMilliseconds > budgetMs)
                    {
                        yield return null; frames++; slice = System.Diagnostics.Stopwatch.StartNew();
                        if (predecodeAbandoned) yield break;
                    }
                }
            }
            if (predecodeAbandoned) yield break;
            predecoded = result;
            UnityEngine.Debug.Log("ORIGINAL GRAPHS: predecoded " + result.Count + " cards over " + frames + " frames (" + total.ElapsedMilliseconds + " ms).");
        }

        private static fsSerializer NewSerializer()
        {
            // The game's default fsConfig ignores [NonSerialized] members, which are exactly the stripped rule fields.
            // A private config without that attribute reads them; fsMetaType caches per config, so the game's own
            // serializer instances are unaffected.
            fsSerializer serializer = new fsSerializer();
            serializer.Config = new fsConfig();
            serializer.Config.IgnoreSerializeAttributes = new Type[] { typeof(fsIgnoreAttribute) };
            return serializer;
        }

        public static int Load(GameConfiguration config, string exportDirectory)
        {
            originals = new Dictionary<int, Card>();
            ImportedIds.Clear(); VanillaIds.Clear(); OverlapIds.Clear(); PreferredIds.Clear();
            string root = Root(exportDirectory), path = Path.Combine(root, "card-rule-graphs.jsonl.gz");
            preferred = ReadIds(Path.Combine(root, "prefer-original.tsv"));
            if (!File.Exists(path)) { UnityEngine.Debug.LogWarning("ORIGINAL GRAPHS: data absent: " + path); return 0; }
            if (predecoded != null)
            {
                originals = predecoded; predecoded = null;
                FxAdded = RestoreFxLookups(config, Path.Combine(root, "card-fx-lookups.tsv"));
                UnityEngine.Debug.Log("ORIGINAL GRAPHS: using " + originals.Count + " predecoded cards, " + FxAdded + " FX lookups added.");
                return originals.Count;
            }
            predecodeAbandoned = true;
            // The game's default fsConfig ignores [NonSerialized] members, which are exactly the stripped rule fields.
            // A private config without that attribute reads them; fsMetaType caches per config, so the game's own
            // serializer instances are unaffected.
            fsSerializer serializer = new fsSerializer();
            serializer.Config = new fsConfig();
            serializer.Config.IgnoreSerializeAttributes = new Type[] { typeof(fsIgnoreAttribute) };
            using (FileStream stream = File.OpenRead(path))
            using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    PrimeTypes(line);
                    object result = null;
                    fsResult status = serializer.TryDeserialize(fsJsonParser.Parse(line), typeof(CardSet), ref result);
                    if (status.Failed) throw new InvalidOperationException("Original card set did not decode: " + status.FormattedMessages);
                    CardSet set = (CardSet)result;
                    foreach (Card card in set.Cards)
                    {
                        if (card == null || card.ResourceId.Id == 0) continue;
                        if (originals.ContainsKey(card.ResourceId.Id)) throw new InvalidOperationException("Duplicate original card " + card.ResourceId.Id);
                        originals.Add(card.ResourceId.Id, card);
                    }
                }
            }
            FxAdded = RestoreFxLookups(config, Path.Combine(root, "card-fx-lookups.tsv"));
            UnityEngine.Debug.Log("ORIGINAL GRAPHS: decoded " + originals.Count + " cards, " + FxAdded + " FX lookups added.");
            return originals.Count;
        }

        // FullSerializer resolves "$type" names by trying every loaded assembly and enumerating GetTypes() on misses; a
        // BepInEx plugin assembly that cannot be enumerated throws ReflectionTypeLoadException there. The original data only
        // names game types, so fill fsTypeCache's private cache with the exact Assembly-CSharp types before decoding.
        private static readonly System.Reflection.FieldInfo TypeCacheField =
            typeof(FullSerializer.Internal.fsTypeCache).GetField("_cachedTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly Regex TypeName = new Regex(@"""\$type""\s*:\s*""([^""]+)""");

        private static void PrimeTypes(string json)
        {
            Dictionary<string, Type> cache = TypeCacheField == null ? null : TypeCacheField.GetValue(null) as Dictionary<string, Type>;
            if (cache == null) throw new InvalidOperationException("FullSerializer type cache not found");
            System.Reflection.Assembly game = typeof(Card).Assembly;
            foreach (Match m in TypeName.Matches(json))
            {
                string name = m.Groups[1].Value;
                if (cache.ContainsKey(name)) continue;
                Type t = game.GetType(name, false);
                if (t == null) throw new InvalidOperationException("Original data names a type absent from the game: " + name);
                cache[name] = t;
            }
        }

        public static bool HasRules(Card c)
        {
            return (c.TriggeredEffects != null && c.TriggeredEffects.Count > 0) || (c.Auras != null && c.Auras.Count > 0)
                || (c.ActionPlayInformation != null && c.ActionPlayInformation.Count > 0)
                || (c.ActivatedAbility != null && c.ActivatedAbility.AbilityGroups != null && c.ActivatedAbility.AbilityGroups.Count > 0);
        }

        // Every stripped field from the original (references are shared with the decoded original; it is not reused).
        public static void ApplyAll(Card dst, Card src)
        {
            dst.TriggeredEffects = src.TriggeredEffects ?? new List<TriggeredEffect>();
            dst.Auras = src.Auras ?? new List<Aura>();
            dst.ActionPlayInformation = src.ActionPlayInformation ?? new List<PlayCardAbility>();
            dst.ActivatedAbility = src.ActivatedAbility ?? new ActivatedAbility();
            dst.PlayRequirements = src.PlayRequirements ?? new PlayCardRequirements();
            dst.BetrayRequirements = src.BetrayRequirements ?? new PlayCardRequirements();
            dst.StrongHintRequirements = src.StrongHintRequirements ?? new StrongHintRequirements();
            ApplyMeta(dst, src, true);
        }

        // Non-graph stripped fields; with force false only where ours are empty.
        public static void ApplyMeta(Card dst, Card src, bool force)
        {
            if (src.PlayWeight != null && (force || dst.PlayWeight == null || Empty(dst.PlayWeight))) dst.PlayWeight = src.PlayWeight;
            if (src.HuntTypes != null && src.HuntTypes.Count > 0 && (force || dst.HuntTypes == null || dst.HuntTypes.Count == 0)) dst.HuntTypes = src.HuntTypes;
            if (src.UpgradesInto != null && src.UpgradesInto.Count > 0 && (force || dst.UpgradesInto == null || dst.UpgradesInto.Count == 0)) dst.UpgradesInto = src.UpgradesInto;
            if (src.MobilizeRecruitId != null && src.MobilizeRecruitId.CardIdReference != 0 && (force || dst.MobilizeRecruitId == null || dst.MobilizeRecruitId.CardIdReference == 0))
                dst.MobilizeRecruitId = src.MobilizeRecruitId;
            if (src.ArenaDraftSubtypes != null && src.ArenaDraftSubtypes.Count > 0 && (force || dst.ArenaDraftSubtypes == null || dst.ArenaDraftSubtypes.Count == 0))
                dst.ArenaDraftSubtypes = src.ArenaDraftSubtypes;
        }

        private static bool Empty(FullAiWeight w)
        {
            return (w.CommonWeights == null || w.CommonWeights.Count == 0) && (w.LocalWeights == null || w.LocalWeights.Count == 0);
        }

        // After our batches: originals for unsupported cards, metadata fill for the rest. Returns newly supported ids.
        public static List<int> Restore(GameConfiguration config, string exportDirectory, Func<int, bool> supported, StringBuilder report)
        {
            List<int> added = new List<int>();
            if (originals.Count == 0) return added;
            string root = Root(exportDirectory);
            Dictionary<int, string> excluded = ReadIds(Path.Combine(root, "excluded.tsv"));
            Dictionary<int, string> prefer = ReadIds(Path.Combine(root, "prefer-original.tsv"));
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    if (card == null) continue;
                    Card src;
                    int id = card.ResourceId.Id;
                    if (!originals.TryGetValue(id, out src)) continue;
                    if (src.Name != card.Name) throw new InvalidOperationException("Original card identity differs: " + id + " " + card.Name + " / " + src.Name);
                    bool ours = supported(id);
                    if (excluded.ContainsKey(id))
                    {
                        report.AppendLine(id + "\t" + card.Name + "\toriginal-graph-excluded\t" + excluded[id]);
                        continue;
                    }
                    if (ours)
                    {
                        if (HasRules(src)) OverlapIds.Add(id);
                        if (prefer.ContainsKey(id) && HasRules(src))
                        {
                            ApplyAll(card, src); PreferredIds.Add(id);
                            report.AppendLine(id + "\t" + card.Name + "\toriginal-graph-preferred\t" + prefer[id]);
                        }
                        // Our hand-written batches validate their cards' exact fields, so ours stay untouched.
                        continue;
                    }
                    ApplyAll(card, src);
                    if (HasRules(src) && preferred.ContainsKey(id))
                    {
                        ImportedIds.Add(id); PreferredIds.Add(id);
                        report.AppendLine(id + "\t" + card.Name + "\toriginal-graph-replaces-spec\toriginal developer rule graph replaces the text-compiled spec");
                    }
                    else if (HasRules(src))
                    {
                        ImportedIds.Add(id);
                        report.AppendLine(id + "\t" + card.Name + "\toriginal-graph\toriginal developer rule graph (data/original-rules)");
                    }
                    else
                    {
                        VanillaIds.Add(id);
                        report.AppendLine(id + "\t" + card.Name + "\toriginal-vanilla\toriginal data has no rule graph; native attributes are the whole card");
                    }
                    added.Add(id);
                }
            UnityEngine.Debug.Log("ORIGINAL GRAPHS: imported " + ImportedIds.Count + ", vanilla " + VanillaIds.Count + ", overlap " + OverlapIds.Count
                + ", preferred " + PreferredIds.Count + ", excluded " + excluded.Count + ".");
            return added;
        }

        private static Dictionary<int, string> ReadIds(string path)
        {
            Dictionary<int, string> ids = new Dictionary<int, string>();
            if (!File.Exists(path)) return ids;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("card_id", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] f = line.Split('\t');
                int id;
                if (int.TryParse(f[0], out id)) ids[id] = f.Length > 2 ? f[2] : "";
            }
            return ids;
        }

        private static int RestoreFxLookups(GameConfiguration config, string path)
        {
            if (!File.Exists(path) || config.CardFxDefinitionLookups == null) return 0;
            Dictionary<string, bool> names = new Dictionary<string, bool>();
            foreach (CardFxDefinition d in config.CardFxDefinitions) if (d != null && d.Name != null) names[d.Name] = true;
            int added = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                string[] f = raw.TrimEnd('\r').Split('\t');
                int hash;
                if (f.Length != 2 || !int.TryParse(f[0], out hash)) continue;
                if (!names.ContainsKey(f[1]) || config.CardFxDefinitionLookups.ContainsKey(hash)) continue;
                config.CardFxDefinitionLookups.Add(hash, f[1]); added++;
            }
            return added;
        }

        // Swap helpers for the differential test: capture and restore one card's stripped fields.
        public sealed class Fields
        {
            public List<TriggeredEffect> Triggers; public List<Aura> Auras; public List<PlayCardAbility> Actions; public ActivatedAbility Ability;
            public PlayCardRequirements Play, Betray; public StrongHintRequirements Hint; public FullAiWeight Weight;
            public List<TreasureHuntType> Hunts; public List<UniqueResourceReference> Upgrades; public SimpleCardReference Mobilize; public List<CardSubtype> Draft;
        }

        public static Fields Capture(Card c)
        {
            return new Fields { Triggers = c.TriggeredEffects, Auras = c.Auras, Actions = c.ActionPlayInformation, Ability = c.ActivatedAbility,
                Play = c.PlayRequirements, Betray = c.BetrayRequirements, Hint = c.StrongHintRequirements, Weight = c.PlayWeight,
                Hunts = c.HuntTypes, Upgrades = c.UpgradesInto, Mobilize = c.MobilizeRecruitId, Draft = c.ArenaDraftSubtypes };
        }

        public static void Put(Card c, Fields f)
        {
            c.TriggeredEffects = f.Triggers; c.Auras = f.Auras; c.ActionPlayInformation = f.Actions; c.ActivatedAbility = f.Ability;
            c.PlayRequirements = f.Play; c.BetrayRequirements = f.Betray; c.StrongHintRequirements = f.Hint; c.PlayWeight = f.Weight;
            c.HuntTypes = f.Hunts; c.UpgradesInto = f.Upgrades; c.MobilizeRecruitId = f.Mobilize; c.ArenaDraftSubtypes = f.Draft;
        }
    }
}
