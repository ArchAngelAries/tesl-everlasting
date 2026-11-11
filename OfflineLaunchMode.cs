using System;
using System.Diagnostics;
using System.IO;

namespace OfflineEngine
{
    // Online or offline without a special launcher (owner direction 2026-11-04). The choice is kept in
    // <game>/.offline-user-data/launch.json {"mode": "offline" | "online"}; flags override it (--tesl-offline-native = offline,
    // --tesl-online = online). With no choice saved, the game starts offline when TESL-Reborn is not installed (there is no
    // online service without it) and online when it is. The F10 menu switches mode by saving the choice and restarting the game.
    // OfflineAccessPatcher (a separate preloader assembly) reads the same file with the same rules.
    public static class OfflineLaunchMode
    {
        public const string OnlineFlag = "--tesl-online", OfflineFlag = "--tesl-offline-native", BenchFlag = "--tesl-offline";

        public static string GameRoot()
        {
            return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        }

        public static string SettingsPath() { return Path.Combine(Path.Combine(GameRoot(), ".offline-user-data"), "launch.json"); }

        public static bool RebornInstalled()
        {
            return Directory.Exists(Path.Combine(Path.Combine(Path.Combine(GameRoot(), "BepInEx"), "plugins"), "TeslRebornPlugin"));
        }

        private static bool HasFlag(string flag)
        {
            foreach (string a in Environment.GetCommandLineArgs()) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The saved choice: "offline", "online" or null.
        public static string Saved()
        {
            try
            {
                string file = SettingsPath();
                if (!File.Exists(file)) return null;
                string text = File.ReadAllText(file);
                if (text.Contains("\"offline\"")) return "offline";
                if (text.Contains("\"online\"")) return "online";
            }
            catch (Exception) { }
            return null;
        }

        public static bool Bench { get { return HasFlag(BenchFlag); } }

        public static bool WantsOffline()
        {
            if (Bench) return false;                      // the old engine test bench has its own flag
            if (HasFlag(OfflineFlag)) return true;
            if (HasFlag(OnlineFlag)) return false;
            string saved = Saved();
            // a saved "online" needs TESL-Reborn; without it the game starts offline (online services would be unreachable)
            if (saved != null) return saved == "offline" || !RebornInstalled();
            return !RebornInstalled();
        }

        public static string Current { get { return WantsOffline() ? "offline" : "online"; } }

        // Saves the choice and restarts the game in that mode (offline/online flags of this run are left out).
        public static void SwitchTo(string mode)
        {
            string file = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, "{\n \"mode\": \"" + mode + "\"\n}\n");
            System.Collections.Generic.List<string> args = new System.Collections.Generic.List<string>();
            string[] all = Environment.GetCommandLineArgs();
            for (int i = 1; i < all.Length; i++)
            {
                string a = all[i];
                if (a.StartsWith("--tesl-", StringComparison.OrdinalIgnoreCase)) continue;
                args.Add(a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a);
            }
            ProcessStartInfo start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, string.Join(" ", args.ToArray()));
            start.WorkingDirectory = GameRoot();
            start.UseShellExecute = false;
            UnityEngine.Debug.Log("OFFLINE LAUNCH: switching to " + mode + " mode; restarting");
            Process.Start(start);
            Process.GetCurrentProcess().Kill();
        }
    }
}
