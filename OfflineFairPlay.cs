using System;
using System.Net;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Fair play (owner rule 2026-11-04): cheats, mods and any future AI-agent player are for offline play against the
    // in-game AI only. Everything offline runs against loopback services and a loopback match server, so an offline session
    // can never meet a human. This is the tripwire behind that design: if a client that has the offline engine loaded ever
    // starts a match connection to a non-loopback host, the game is force-closed before the connection is made.
    // Normal online launches (without --tesl-offline-native) never load the offline engine, so they are unaffected.
    public static class OfflineFairPlay
    {
        public static int Blocked;

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameNetworkManager), "AttemptConnection"), new HarmonyMethod(typeof(OfflineFairPlay).GetMethod("AttemptConnectionPrefix")), null, null, null, null);
        }

        // Online launches: the online-safe mode never installs offline patches; if any are present anyway (a modified setup)
        // when a remote match starts, the game closes before connecting.
        public static void InstallOnlineCheck(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameNetworkManager), "AttemptConnection"), new HarmonyMethod(typeof(OfflineFairPlay).GetMethod("OnlineConnectionPrefix")), null, null, null, null);
        }

        public static string OfflinePatchOwner()
        {
            foreach (System.Reflection.MethodBase m in Harmony.GetAllPatchedMethods())
            {
                Patches info = Harmony.GetPatchInfo(m);
                if (info == null) continue;
                foreach (string owner in info.Owners) if (owner == "local.tesl.offline" || owner.StartsWith("local.tesl.offline.")) return owner + " on " + m.Name;
            }
            return null;
        }

        public static bool OnlineConnectionPrefix(GameNetworkManager __instance)
        {
            string host = AccessTools.Field(typeof(GameNetworkManager), "_hostServer").GetValue(__instance) as string;
            if (IsLocal(host)) return true;
            string offline = OfflinePatchOwner();
            if (offline == null) return true;
            Blocked++;
            Debug.LogError("OFFLINE FAIR PLAY: offline-mode code (" + offline + ") is active while joining a remote match; the game is closing.");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return false;
        }

        public static bool IsLocal(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress ip;
            return IPAddress.TryParse(host, out ip) && IPAddress.IsLoopback(ip);
        }

        public static bool AttemptConnectionPrefix(GameNetworkManager __instance)
        {
            string host = AccessTools.Field(typeof(GameNetworkManager), "_hostServer").GetValue(__instance) as string;
            if (IsLocal(host)) return true;
            Blocked++;
            Debug.LogError("OFFLINE FAIR PLAY: a remote match server (" + host + ") was requested while the offline engine (cheats, mods) is loaded. "
                + "Offline features are for play against the in-game AI only; the game is closing.");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return false;
        }
    }
}
