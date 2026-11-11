using System;
using System.IO;
using UnityEngine;

namespace OfflineEngine
{
    // Where TESL: Everlasting reads its data and writes its logs, in a development checkout and in a player's install.
    //  - Development: <game>/.analysis/offline-engine/data and .../exports (when that folder exists).
    //  - Release: data beside the installed plugin (BepInEx/plugins/TESLOffline/data); logs and reports in
    //    <game>/.offline-user-data/logs.
    // TESL Forge's location: the TESL_MODDING_SUITE variable, else the pointer Forge writes to
    // %APPDATA%/TESL Forge/location.txt (outside the game folder), else a "TESL-Modding-Suite" or "TESL Forge" folder beside
    // the game folder.
    public static class OfflinePaths
    {
        public static string GameRoot() { return Path.GetDirectoryName(Application.dataPath); }

        private static string DevRoot()
        {
            // TESL_EVERLASTING_RELEASE_LAYOUT=1: behave like a player's install even in a development checkout (release tests)
            if (Environment.GetEnvironmentVariable("TESL_EVERLASTING_RELEASE_LAYOUT") == "1") return null;
            string dev = Path.Combine(Path.Combine(GameRoot(), ".analysis"), "offline-engine");
            return Directory.Exists(Path.Combine(dev, "data")) ? dev : null;
        }

        public static bool Development { get { return DevRoot() != null; } }

        public static string PluginFolder()
        {
            try { return Path.GetDirectoryName(typeof(OfflinePaths).Assembly.Location); }
            catch (Exception) { return Path.Combine(Path.Combine(Path.Combine(GameRoot(), "BepInEx"), "plugins"), "TESLOffline"); }
        }

        // Folder holding data/ (story-reward-bundles.tsv, original-rules/, store-art/ ...).
        public static string EngineRoot() { return DevRoot() ?? PluginFolder(); }
        public static string Data(string name) { return Path.Combine(Path.Combine(EngineRoot(), "data"), name); }

        // A data file next to a given report folder (engine tests and benches pass their own), else the installed data.
        public static string DataNear(string reportFolder, string name)
        {
            try
            {
                string near = Path.Combine(Path.Combine(Path.GetDirectoryName(reportFolder.TrimEnd('/', '\\')), "data"), name);
                if (File.Exists(near) || Directory.Exists(near)) return near;
            }
            catch (Exception) { }
            return Data(name);
        }

        // Reports and logs (created on demand). Exports' parent is EngineRoot in development; in a release it is the user data.
        public static string Exports()
        {
            string dev = DevRoot();
            string dir = dev != null ? Path.Combine(dev, "exports") : Path.Combine(Path.Combine(GameRoot(), ".offline-user-data"), "logs");
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            return dir;
        }

        public static string SuiteRoot()
        {
            string env = Environment.GetEnvironmentVariable("TESL_MODDING_SUITE");
            string pointed = !string.IsNullOrEmpty(env) && Directory.Exists(env) ? env : null;
            if (pointed == null)
            try
            {
                string pointer = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TESL Forge"), "location.txt");
                if (File.Exists(pointer))
                {
                    string where = File.ReadAllText(pointer).Trim();
                    if (where.Length > 0 && Directory.Exists(where)) pointed = where;
                }
            }
            catch (Exception) { }
            string parent = Path.GetDirectoryName(GameRoot());
            string beside = null;
            foreach (string name in new string[] { "TESL-Modding-Suite", "TESL Forge" })
                if (beside == null && Directory.Exists(Path.Combine(parent, name))) beside = Path.Combine(parent, name);
            // The pointer follows the last Forge that started; a second copy (an installed Forge tried once, a test run) can
            // leave it at a folder with no mods enabled (owner 2026-11-07: mods missing after a launch). A folder with an
            // active list wins over one without.
            if (pointed != null && (HasActiveList(pointed) || beside == null || !HasActiveList(beside))) return pointed;
            return beside ?? Path.Combine(parent, "TESL-Modding-Suite");
        }

        private static bool HasActiveList(string suite)
        {
            try { return File.Exists(Path.Combine(Path.Combine(suite, "deploy"), "active-mods.json")); }
            catch (Exception) { return false; }
        }
    }
}
