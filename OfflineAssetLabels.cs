using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SPTESL;

namespace OfflineEngine
{
    // For the modding suite's labelled asset catalog: every asset path the game configuration names, with the type and
    // the display name of the thing that uses it (avatar, AI opponent, deck, card back, campaign, quest, lane, cinematic...).
    // Cards are left out (the suite names card art from its own card data). Written by the audit to asset-labels.tsv.
    public static class OfflineAssetLabels
    {
        private sealed class ByReference : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        private static readonly string[] Skip = { "CardCollection", "CardDefinitions", "Zones", "PlayerDefinitions", "LaneTriggers", "CommonAiWeights" };
        private static HashSet<object> seen;
        private static StringBuilder rows;
        private static Dictionary<int, string> deckNames;
        public static int Count;

        public static int Write(GameConfiguration config, string file)
        {
            seen = new HashSet<object>(new ByReference());
            rows = new StringBuilder("path\ttype\tfield\tlabel\n");
            Count = 0;
            deckNames = new Dictionary<int, string>();
            foreach (DeckInfo d in config.DeckInfo)
                if (d != null && d.ArtTypeHash != 0 && !deckNames.ContainsKey(d.ArtTypeHash)) deckNames[d.ArtTypeHash] = Text(d.DisplayNameLocKey ?? d.DisplayName) ?? d.TypeName;
            foreach (FieldInfo f in typeof(GameConfiguration).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Array.IndexOf(Skip, f.Name) >= 0 || f.IsNotSerialized) continue;
                try { Walk(f.GetValue(config), "", "GameConfiguration", f.Name, 0); } catch (Exception) { }
            }
            System.IO.File.WriteAllText(file, rows.ToString());
            return Count;
        }

        private static string Text(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try { string t = LocalizationManager.Instance.GetText(key); return string.IsNullOrEmpty(t) || t == key ? null : t; } catch (Exception) { return null; }
        }

        private static string LabelOf(object o, Type t)
        {
            DeckArtData art = o as DeckArtData;
            if (art != null) { string n; if (deckNames.TryGetValue(art.ResourceId.Id, out n)) return n; }
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(LocalizedStringKey)) continue;
                string n = f.Name.ToLowerInvariant();
                if (!n.Contains("name") && !n.Contains("title")) continue;
                string text = Text(((LocalizedStringKey)f.GetValue(o)).RawKey);
                if (text != null) return text;
            }
            foreach (string name in new string[] { "Name", "DisplayName", "Title" })
            {
                FieldInfo f = t.GetField(name);
                if (f != null && f.FieldType == typeof(string)) { string s = (string)f.GetValue(o); if (!string.IsNullOrEmpty(s)) return Text(s) ?? s; }
            }
            FieldInfo id = t.GetField("ResourceId");
            if (id != null && id.FieldType == typeof(UniqueResourceId)) { string u = ((UniqueResourceId)id.GetValue(o)).UniqueName; if (!string.IsNullOrEmpty(u)) return u; }
            return null;
        }

        private static bool LooksLikeAsset(string s)
        {
            return s.Length > 8 && s.Length < 260 && s.IndexOf('/') > 0 && s.IndexOf(' ') != 0
                && (s.StartsWith("ContentPack") || s.StartsWith("AppBase") || s.StartsWith("Common") || s.StartsWith("Assets/"));
        }

        private static void Walk(object o, string owner, string ownerType, string field, int depth)
        {
            if (o == null || depth > 14) return;
            string s = o as string;
            if (s != null)
            {
                if (LooksLikeAsset(s)) { rows.Append(s).Append('\t').Append(ownerType).Append('\t').Append(field).Append('\t').Append(owner.Replace('\t', ' ').Replace('\n', ' ')).Append('\n'); Count++; }
                return;
            }
            Type t = o.GetType();
            if (t.IsPrimitive || t.IsEnum || o is decimal || t == typeof(LocalizedStringKey)) return;
            if (!t.IsValueType && !seen.Add(o)) return;
            IDictionary dict = o as IDictionary;
            if (dict != null) { foreach (object v in dict.Values) Walk(v, owner, ownerType, field, depth + 1); return; }
            IEnumerable list = o as IEnumerable;
            if (list != null) { foreach (object v in list) Walk(v, owner, ownerType, field, depth + 1); return; }
            if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) return;
            string label = LabelOf(o, t);
            string here = string.IsNullOrEmpty(label) ? owner : label;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsNotSerialized) continue;
                object v;
                try { v = f.GetValue(o); } catch (Exception) { continue; }
                Walk(v, here, string.IsNullOrEmpty(label) ? ownerType : t.Name, f.Name, depth + 1);
            }
        }
    }
}
