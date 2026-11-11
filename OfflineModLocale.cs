using System;
using System.Collections.Generic;
using System.IO;
using FullSerializer;
using HarmonyLib;
using SPTESL;
using SparkleServices;
using UnityEngine;

namespace OfflineEngine
{
    // Translations for mod text: <mod>/locale/<language>.json {"strings": {"<English text or text key>": "translation"}}.
    // <language> is the game's language name (LanguageCode, e.g. "de", "fr"; the names are logged at mod load) or a common
    // alias (german, french, ...). Mod text is stored in English; when the game runs in another language the translation is
    // shown instead, looked up by the text's key first, then by its English text. Later mods win for the same string.
    public static class OfflineModLocale
    {
        private static readonly Dictionary<string, Dictionary<string, string>> byLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "english", "en" }, { "german", "de" }, { "deutsch", "de" }, { "french", "fr" }, { "francais", "fr" }, { "spanish", "es" }, { "espanol", "es" },
            { "italian", "it" }, { "russian", "ru" }, { "polish", "pl" }, { "portuguese", "pt" }, { "brazilian", "pt" }, { "japanese", "ja" },
            { "chinese", "zh" }, { "korean", "ko" } };
        private static int cachedFrame = -1; private static string cachedLanguage;
        public static string Override;   // tests/diagnostics: force a language
        public static int Strings;

        private static string Norm(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return "";
            string a;
            lang = lang.Trim().Replace('_', '-');
            return Aliases.TryGetValue(lang, out a) ? a : lang.ToLowerInvariant();
        }

        public static void Load(string modId, string dir, List<string> problems)
        {
            string folder = Path.Combine(dir, "locale");
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    Dictionary<string, fsData> root = fsJsonParser.Parse(File.ReadAllText(file)).AsDictionary;
                    if (!root.ContainsKey("strings") || !root["strings"].IsDictionary) { problems.Add(modId + "/locale/" + Path.GetFileName(file) + ": no \"strings\""); continue; }
                    string lang = Norm(Path.GetFileNameWithoutExtension(file));
                    Dictionary<string, string> table;
                    if (!byLanguage.TryGetValue(lang, out table)) byLanguage[lang] = table = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, fsData> s in root["strings"].AsDictionary)
                        if (s.Value.IsString && !string.IsNullOrEmpty(s.Value.AsString)) { table[s.Key] = s.Value.AsString; Strings++; }
                }
                catch (Exception e) { problems.Add(modId + "/locale/" + Path.GetFileName(file) + ": unreadable (" + e.Message + ")"); }
            }
        }

        // The game's current language, normalized (cached per frame: text lookups are frequent).
        public static string Current()
        {
            if (Override != null) return Norm(Override);
            int frame = Time.frameCount;
            if (frame == cachedFrame && cachedLanguage != null) return cachedLanguage;
            cachedFrame = frame;
            try
            {
                object manager = UnityEngine.Object.FindObjectOfType(typeof(ServicesManager));
                object code = manager == null ? null : AccessTools.Field(typeof(ServicesManager), "_currentLanguage").GetValue(manager);
                cachedLanguage = Norm(code == null ? "en" : code.ToString());
            }
            catch (Exception) { cachedLanguage = "en"; }
            return cachedLanguage;
        }

        public static string LanguageNames()
        {
            try { return string.Join(",", Enum.GetNames(AccessTools.Field(typeof(ServicesManager), "_currentLanguage").FieldType)); }
            catch (Exception e) { return "? (" + e.Message + ")"; }
        }

        // English text -> the current language's translation, or null.
        public static string Translate(string key, string english)
        {
            if (byLanguage.Count == 0) return null;
            string lang = Current();
            Dictionary<string, string> table;
            if (!byLanguage.TryGetValue(lang, out table))
            {
                int dash = lang.IndexOf('-');
                if (dash < 0 || !byLanguage.TryGetValue(lang.Substring(0, dash), out table)) return null;
            }
            string t;
            if (key != null && table.TryGetValue(key, out t)) return t;
            if (english != null && table.TryGetValue(english, out t)) return t;
            return null;
        }

        public static void Clear() { byLanguage.Clear(); Strings = 0; }
        public static int Languages { get { return byLanguage.Count; } }
    }
}
