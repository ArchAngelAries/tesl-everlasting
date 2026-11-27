using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OfflineEngine
{
    // GitHub issue 1 (new player stuck after the tutorial's third chapter): the client's WinManager keeps every window in
    // _wins and looks windows up by x.name. A window destroyed elsewhere stays in that list, so every later lookup throws
    // (seen: MainMenu PLAY -> ShowPlayFlowWin -> GetWindow NullReferenceException; PLAY did nothing). Before each lookup the
    // destroyed entries are dropped; the last name each one had is logged so the cause can be traced.
    public static class OfflineWindowList
    {
        private static readonly Dictionary<int, string> lastNames = new Dictionary<int, string>();
        private static System.Reflection.FieldInfo winsField;
        public static int Removed;

        public static void Install(Harmony harmony)
        {
            winsField = AccessTools.Field(typeof(WinManager), "_wins");
            HarmonyMethod prune = new HarmonyMethod(typeof(OfflineWindowList), "Prune");
            foreach (string method in new string[] { "ShowWindow", "DestroyWindow", "HideWindow", "IsWindowActive", "HideAndDestroyWindow", "IsWindowVisible", "GetWindow" })
                harmony.Patch(AccessTools.Method(typeof(WinManager), method), prune, null, null, null, null);
            // Names are also recorded right after a window is shown, so one destroyed before the next lookup is still named.
            harmony.Patch(AccessTools.Method(typeof(WinManager), "ShowWindow"), null, new HarmonyMethod(typeof(OfflineWindowList), "Record"), null, null, null);
        }

        private static void Record(WinManager __instance)
        {
            List<GameObject> wins = winsField == null ? null : winsField.GetValue(__instance) as List<GameObject>;
            if (wins == null) return;
            foreach (GameObject win in wins)
                if ((object)win != null && win != null) lastNames[win.GetInstanceID()] = win.name;
        }

        private static void Prune(WinManager __instance)
        {
            List<GameObject> wins = winsField == null ? null : winsField.GetValue(__instance) as List<GameObject>;
            if (wins == null) return;
            for (int i = wins.Count - 1; i >= 0; i--)
            {
                GameObject win = wins[i];
                if ((object)win != null && win != null) { lastNames[win.GetInstanceID()] = win.name; continue; }
                string name = null;
                if ((object)win != null) lastNames.TryGetValue(win.GetInstanceID(), out name);
                wins.RemoveAt(i);
                Removed++;
                Debug.LogWarning("OFFLINE WINDOWS: dropped a destroyed window from the window list (" + (name ?? "unknown") + ")");
            }
        }
    }

    // The client disconnects every service after an hour with no mouse or key input (ServicesManager.Update, IdleDisconnect)
    // and offers only "Reconnect", which reboots the app. Offline there is no server to protect, and the reboot left the local
    // services unreachable (seen in a hidden run: PLAY and chapter select threw). The idle timer is reset each frame instead.
    public static class OfflineNoIdleKick
    {
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(SparkleServices.ServicesManager), "Update"),
                new HarmonyMethod(typeof(OfflineNoIdleKick), "Reset"), null, null, null, null);
        }

        private static void Reset(SparkleServices.ServicesManager __instance) { __instance.ResetIdleTimer(); }
    }
}
