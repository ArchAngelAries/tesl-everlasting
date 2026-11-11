using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Online launches (owner's multiplayer table, BACKLOG top): only what the player alone sees or hears is allowed online.
    // This mode installs nothing that touches services, matches, rules or other players: image replacements (remasters),
    // sound replacements, music, board skins and board edits from the enabled mods (their "local" content only; cards, decks,
    // avatars, card backs, emotes, game modes and stories stay offline), plus the graphics and display options. There are no
    // cheats in this mode. F10 shows these options and a switch to offline mode (restart).
    // Fair play: a remote match connection while any offline-mode patch is present closes the game (OfflineFairPlay).
    // Everything here fails safe: an error disables the online-safe features and the game carries on unmodded.
    public sealed class OfflineOnlineSafe : MonoBehaviour
    {
        public const string HarmonyId = "local.tesl.online-safe";
        public static bool Active;
        private static string status = "loading local mods";
        private bool show;
        private Rect window = new Rect(80, 80, 470, 520);

        public static void Install(MonoBehaviour host)
        {
            try
            {
                Harmony harmony = new Harmony(HarmonyId);
                OfflineMods.LocalOnly = true;
                OfflineMods.Install(harmony);
                OfflineAudio.Install(harmony, host);
                OfflineMusic.Install(harmony, host);
                OfflineBoards.Install(harmony);
                OfflineFairPlay.InstallOnlineCheck(harmony);
                OfflineGraphics.Install(host, Path.Combine(Path.Combine(OfflineLaunchMode.GameRoot(), ".offline-user-data"), "graphics.json"));
                host.gameObject.AddComponent<OfflineOnlineSafe>();
                Active = true;
                Debug.Log("OFFLINE ONLINE-SAFE: local visual/audio mods and graphics options; no cheats or gameplay mods");
            }
            catch (Exception e) { status = "not available: " + e.Message; Debug.LogError("OFFLINE ONLINE-SAFE: not installed: " + e); }
        }

        private IEnumerator Start()
        {
            yield return null;   // local content (images, sounds, music, boards) needs no game data: applied at start, before sign-in
            try
            {
                OfflineMods.EnsureApplied(null, null);
                status = OfflineMods.Loaded.Count + " mods: " + OfflineMods.Replaced.Count + " images, " + OfflineAudio.Replacements.Count + " sounds, "
                    + OfflineMusic.Tracks.Count + " music tracks, " + OfflineBoards.Boards.Count + " boards" + (OfflineMods.Problems.Count > 0 ? "; " + OfflineMods.Problems.Count + " problems" : "");
            }
            catch (Exception e) { status = "mods not loaded: " + e.Message; Debug.LogError("OFFLINE ONLINE-SAFE: " + e); }
            Debug.Log("OFFLINE ONLINE-SAFE: " + status);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10)) show = !show;
        }

        private void OnGUI()
        {
            if (!show) return;
            GUI.depth = -20000;
            window = GUILayout.Window(0x7E52, window, Draw, "TESL: Everlasting - online (F10)");
        }

        private void Draw(int id)
        {
            GUILayout.Label("Online: only local visual and audio mods are used here (what you alone see and hear).");
            GUILayout.Label("Cheats, mod cards, decks, avatars, card backs, emotes, game modes and stories are offline only.");
            GUILayout.Label("Local mods: " + status);
            GUILayout.Space(6);
            OfflineGraphics.DrawOptions();
            GUILayout.Space(6);
            if (GUILayout.Button("Switch to offline mode (restarts the game)")) OfflineLaunchMode.SwitchTo("offline");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Support TESL: Everlasting:");
            if (GUILayout.Button("Ko-fi")) Application.OpenURL("https://ko-fi.com/archangelaries");
            if (GUILayout.Button("Patreon")) Application.OpenURL("https://www.patreon.com/c/ArchAngelAries");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Close")) show = false;
            GUI.DragWindow();
        }
    }
}
