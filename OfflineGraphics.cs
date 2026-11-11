using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OfflineEngine
{
    // Graphics and performance options for the offline game (F10 menu, saved in .offline-user-data/graphics.json, apart
    // from the player profile). The client caps the frame rate itself (InitializationState: Application.targetFrameRate 60,
    // or 30) and its video-quality setting rewrites QualitySettings, so the chosen values are re-applied whenever they drift.
    [Serializable]
    public sealed class OfflineGraphicsSettings
    {
        public int frameCap = 60;          // -1 = unlimited
        public bool vsync = false;
        public int msaa = 2;               // 2, 4, 8. Never off: the board (CameraMatrixOverride) flips its projection for texture
                                           // targets only when AA > 1, so with AA off a post-processed scene (story chapter 1) draws a black table
        public bool anisotropic = true;
        public bool fullTextures = true;   // QualitySettings.masterTextureLimit 0
        public bool fpsCounter = false;
        public string board = "";       // force one mod board everywhere (OfflineBoards key "mod/slug"); "" = the game's choice
        public string mode = "";        // practice matches play this mod game mode (OfflineModes key "mod/slug"); "" = ordinary practice
        public string opponent = "";    // practice opponent from a mod (OfflineOpponents key "mod/slug"); "" = the game's choice
        public bool modOpponents = false; // random practice may bring mod opponents, and practice AIs may play mod decks
        public string preset = "custom"; // last quality preset chosen: low, medium, high, ultra (custom after a manual change)
        public bool ultra = false;       // Ultra extras beyond the game's High: shadow resolution/distance, lights, detail distance, soft particles
        public string window = "game";  // game (the game's own setting), windowed, maximized, fullscreen
        public bool resizable = true;    // windowed: drag the edges / maximize button (the original build locks the size)
    }

    public sealed class OfflineGraphics : MonoBehaviour
    {
        public static readonly int[] FrameCaps = { 30, 60, 120, 144, 165, 240, -1 };
        public static readonly int[] MsaaLevels = { 2, 4, 8 };
        public static readonly string[] Presets = { "low", "medium", "high", "ultra" };
        public static readonly string[] WindowModes = { "game", "windowed", "maximized", "fullscreen" };
        public static OfflineGraphicsSettings Settings = new OfflineGraphicsSettings();
        private static string path;
        private float nextCheck, smoothed = 1f / 60f;
        private GUIStyle style;

        public static void Install(MonoBehaviour host, string settingsPath)
        {
            path = settingsPath;
            Settings = Load(settingsPath);
            if (host.gameObject.GetComponent<OfflineGraphics>() == null) host.gameObject.AddComponent<OfflineGraphics>();
            Apply();
        }

        public static OfflineGraphicsSettings Load(string file)
        {
            try { if (file != null && File.Exists(file)) return Sanitize(JsonUtility.FromJson<OfflineGraphicsSettings>(File.ReadAllText(file))); }
            catch (Exception e) { Debug.LogWarning("OFFLINE GRAPHICS: settings unreadable, defaults used: " + e.Message); }
            return new OfflineGraphicsSettings();
        }

        public static OfflineGraphicsSettings Sanitize(OfflineGraphicsSettings s)
        {
            if (s == null) s = new OfflineGraphicsSettings();
            if (Array.IndexOf(FrameCaps, s.frameCap) < 0) s.frameCap = 60;
            if (Array.IndexOf(MsaaLevels, s.msaa) < 0) s.msaa = 2;   // also old settings files with 0 (off)
            if (Array.IndexOf(WindowModes, s.window) < 0) s.window = "game";
            if (s.preset != "custom" && Array.IndexOf(Presets, s.preset) < 0) s.preset = "custom";
            return s;
        }

        public static void Save()
        {
            if (path == null) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(Settings, true)); }
            catch (Exception e) { Debug.LogWarning("OFFLINE GRAPHICS: settings not saved: " + e.Message); }
        }

        // Applies the settings; returns how many values had drifted (the client or its quality preset changed them).
        public static int Apply()
        {
            OfflineGraphicsSettings s = Settings;
            int changed = 0;
            int vsync = s.vsync ? 1 : 0;
            if (QualitySettings.vSyncCount != vsync) { QualitySettings.vSyncCount = vsync; changed++; }
            if (Application.targetFrameRate != s.frameCap) { Application.targetFrameRate = s.frameCap; changed++; }
            if (QualitySettings.antiAliasing != s.msaa) { QualitySettings.antiAliasing = s.msaa; changed++; }
            AnisotropicFiltering af = s.anisotropic ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            if (QualitySettings.anisotropicFiltering != af) { QualitySettings.anisotropicFiltering = af; changed++; }
            if (s.fullTextures && QualitySettings.masterTextureLimit != 0) { QualitySettings.masterTextureLimit = 0; changed++; }
            if (s.ultra)
            {
                if (QualitySettings.shadowResolution != ShadowResolution.VeryHigh) { QualitySettings.shadowResolution = ShadowResolution.VeryHigh; changed++; }
                if (QualitySettings.shadowDistance < 120f) { QualitySettings.shadowDistance = 120f; changed++; }
                if (QualitySettings.shadowCascades < 4) { QualitySettings.shadowCascades = 4; changed++; }
                if (QualitySettings.pixelLightCount < 8) { QualitySettings.pixelLightCount = 8; changed++; }
                if (QualitySettings.lodBias < 2f) { QualitySettings.lodBias = 2f; changed++; }
                if (!QualitySettings.softParticles) { QualitySettings.softParticles = true; changed++; }
            }
            return changed;
        }

        public static void Change(Action<OfflineGraphicsSettings> edit)
        {
            string window = Settings.window, board = Settings.board, mode = Settings.mode; bool counter = Settings.fpsCounter;
            int msaa = Settings.msaa, cap = Settings.frameCap; bool vsync = Settings.vsync, af = Settings.anisotropic, tex = Settings.fullTextures;
            edit(Settings);
            if (msaa != Settings.msaa || af != Settings.anisotropic || tex != Settings.fullTextures) { Settings.preset = "custom"; if (Settings.ultra) { Settings.ultra = false; ResetUltra(); } }
            Settings = Sanitize(Settings);
            Apply();
            Save();
        }

        // Quality presets: the game's own quality level (its Settings menu shows it) plus our options.
        public static void ApplyPreset(string preset)
        {
            OfflineGraphicsSettings s = Settings;
            s.preset = preset;
            SettingsData.VideoQuality level = preset == "low" ? SettingsData.VideoQuality.Low : preset == "medium" ? SettingsData.VideoQuality.Medium : SettingsData.VideoQuality.High;
            s.msaa = preset == "low" || preset == "medium" ? 2 : preset == "high" ? 4 : 8;
            s.anisotropic = preset != "low";
            s.fullTextures = preset != "low";
            s.ultra = preset == "ultra";
            if (!s.ultra) ResetUltra();
            SettingsManager manager = Pixelplacement.Singleton<SettingsManager>.Instance;
            if (manager != null)
            {
                manager.SetQuality(level);
                HarmonyLib.AccessTools.Method(typeof(SettingsManager), "UpdateUnityQuality").Invoke(manager, new object[] { level });
                HarmonyLib.AccessTools.Method(typeof(SettingsManager), "SaveSettings") .Invoke(manager, new object[0]);
            }
            Settings = Sanitize(s);
            Apply();
            Save();
            Debug.Log("OFFLINE GRAPHICS: preset " + preset + " (quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + ", MSAA " + s.msaa + ")");
        }

        // Back to the current quality level's own values (after Ultra).
        private static void ResetUltra() { QualitySettings.SetQualityLevel(QualitySettings.GetQualityLevel(), true); }

        public static string PresetText(string preset) { return preset == "custom" ? "custom" : char.ToUpper(preset[0]) + preset.Substring(1); }
        public static string WindowText(string w) { return w == "game" ? "game setting" : w; }
        public static string Next(string[] values, string current) { int i = Array.IndexOf(values, current); return values[(i + 1) % values.Length]; }

        // The graphics and display buttons (offline F10 menu and online F10 menu).
        public static void DrawOptions()
        {
            GUILayout.Label("Graphics (saved; applies at once)");
            OfflineGraphicsSettings g = Settings;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Frame cap: " + CapText(g.frameCap))) Change(delegate(OfflineGraphicsSettings x) { x.frameCap = Next(FrameCaps, x.frameCap); });
            if (GUILayout.Button("VSync: " + (g.vsync ? "on" : "off"))) Change(delegate(OfflineGraphicsSettings x) { x.vsync = !x.vsync; });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Anti-aliasing: " + g.msaa + "x")) Change(delegate(OfflineGraphicsSettings x) { x.msaa = Next(MsaaLevels, x.msaa); });
            if (GUILayout.Button("Texture filtering: " + (g.anisotropic ? "16x" : "default"))) Change(delegate(OfflineGraphicsSettings x) { x.anisotropic = !x.anisotropic; });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Full-res textures: " + (g.fullTextures ? "on" : "game default"))) Change(delegate(OfflineGraphicsSettings x) { x.fullTextures = !x.fullTextures; });
            if (GUILayout.Button("FPS counter: " + (g.fpsCounter ? "on" : "off"))) Change(delegate(OfflineGraphicsSettings x) { x.fpsCounter = !x.fpsCounter; });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Quality preset: " + PresetText(g.preset))) ApplyPreset(Next(Presets, g.preset == "custom" ? "ultra" : g.preset));
            if (GUILayout.Button("Display: " + WindowText(g.window))) Change(delegate(OfflineGraphicsSettings x) { x.window = Next(WindowModes, x.window); });
            GUILayout.EndHorizontal();
            GUILayout.Label("Window: " + WindowState + (g.resizable ? " (drag the edges to resize)" : ""));
        }

        // ---- the window ------------------------------------------------------------------------------------------------
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private const int GWL_STYLE = -16, WS_THICKFRAME = 0x00040000, WS_MAXIMIZEBOX = 0x00010000, SW_MAXIMIZE = 3, SW_RESTORE = 9;
        private const uint SWP_FRAMECHANGED = 0x20, SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4;
        private static IntPtr window;
        private static string windowApplied;
        public static string WindowState = "";

        private static IntPtr Window()
        {
            if (window != IntPtr.Zero) return window;
            // Mono's Process.MainWindowHandle is always zero; the game window is this process's UnityWndClass window.
            int me = System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "UnityWndClass", null)) != IntPtr.Zero)
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (pid == (uint)me) { window = h; Debug.Log("OFFLINE GRAPHICS: game window found"); break; }
            }
            return window;
        }

        // Called every second: keeps the window resizable while windowed, and applies the chosen display mode once.
        public static void ApplyWindow()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer) return;
            IntPtr h = Window();
            if (h == IntPtr.Zero) return;
            OfflineGraphicsSettings s = Settings;
            if (windowApplied != s.window)
            {
                windowApplied = s.window;
                if (s.window == "fullscreen" && !Screen.fullScreen) Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, true);
                else if ((s.window == "windowed" || s.window == "maximized") && Screen.fullScreen)
                {   // back to a window a little smaller than the screen; maximized follows on the next check
                    Screen.SetResolution(Math.Max(1024, Screen.currentResolution.width * 3 / 4), Math.Max(576, Screen.currentResolution.height * 3 / 4), false);
                    windowApplied = null;
                    return;
                }
                else if (s.window == "maximized" && !IsZoomed(h)) { EnsureResizable(h); ShowWindow(h, SW_MAXIMIZE); }
                else if (s.window == "windowed" && IsZoomed(h)) ShowWindow(h, SW_RESTORE);
            }
            if (!Screen.fullScreen && s.resizable) EnsureResizable(h);
            WindowState = (Screen.fullScreen ? "fullscreen " : IsZoomed(h) ? "maximized " : "window ") + Screen.width + "x" + Screen.height;
        }

        private static void EnsureResizable(IntPtr h)
        {
            int style = GetWindowLong(h, GWL_STYLE);
            int wanted = style | WS_THICKFRAME | WS_MAXIMIZEBOX;
            if (style == wanted) return;
            SetWindowLong(h, GWL_STYLE, wanted);
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
            Debug.Log("OFFLINE GRAPHICS: window made resizable (" + Screen.width + "x" + Screen.height + ")");
        }

        public static int Next(int[] values, int current) { int i = Array.IndexOf(values, current); return values[(i + 1) % values.Length]; }
        public static string CapText(int cap) { return cap < 0 ? "unlimited" : cap.ToString(); }

        // Hitch log (with the counter on): every frame over 100 ms, with garbage collections during it and what was tagged
        // (Mark) in that frame or the one before, so test-harness work can be told apart from the game's own stalls.
        private static string mark = ""; private static int markFrame = -10; private static int lastGc0, lastGc2;
        public static void Mark(string what) { mark = what; markFrame = Time.frameCount; }
        // Startup timing: logs any step that takes 30 ms or more (the main thread is blocked meanwhile).
        public static void Timed(string what, Action step)
        {
            System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
            Mark(what);
            try { step(); }
            finally { if (w.ElapsedMilliseconds >= 30) Debug.Log("OFFLINE TIMING: " + what + " " + w.ElapsedMilliseconds + " ms"); }
        }
        private float windowStart = -1f, worst; private int frames;
        private void Update()
        {
            smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime, 0.05f);
            // With the counter on, the log gets a 10-second summary (average fps and the slowest frame).
            if (Settings.fpsCounter)
            {
                if (windowStart < 0f) { windowStart = Time.unscaledTime; frames = 0; worst = 0f; }
                frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                int gc0 = System.GC.CollectionCount(0), gc2 = System.GC.CollectionCount(System.GC.MaxGeneration);
                if (Time.unscaledDeltaTime > 0.1f && lastGc0 > 0)
                    Debug.Log("OFFLINE GRAPHICS: hitch " + (Time.unscaledDeltaTime * 1000f).ToString("0") + " ms at " + Time.realtimeSinceStartup.ToString("0.0")
                        + " s, gc +" + (gc0 - lastGc0) + " (full +" + (gc2 - lastGc2) + "), heap " + (System.GC.GetTotalMemory(false) >> 20) + " MB, during "
                        + (markFrame >= Time.frameCount - 1 ? mark : "-"));
                lastGc0 = gc0; lastGc2 = gc2;
                float span = Time.unscaledTime - windowStart;
                if (span >= 10f)
                {
                    Debug.Log("OFFLINE GRAPHICS: " + (frames / span).ToString("0.0") + " fps average, slowest frame " + (worst * 1000f).ToString("0.0") + " ms, cap "
                        + CapText(Settings.frameCap) + ", vsync " + Settings.vsync + ", MSAA " + Settings.msaa + ", " + Screen.width + "x" + Screen.height);
                    windowStart = Time.unscaledTime; frames = 0; worst = 0f;
                }
            }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            Apply();
            try { ApplyWindow(); } catch (Exception e) { Debug.LogWarning("OFFLINE GRAPHICS: window: " + e.Message); Settings.resizable = false; }
        }

        private void OnGUI()
        {
            if (!Settings.fpsCounter) return;
            if (style == null) { style = new GUIStyle(GUI.skin.label); style.fontSize = 14; style.normal.textColor = Color.white; }
            GUI.depth = -20001;
            float ms = smoothed * 1000f;
            GUI.Label(new Rect(8, 4, 260, 24), Mathf.RoundToInt(1000f / Mathf.Max(ms, 0.01f)) + " fps  " + ms.ToString("0.0") + " ms  (cap " + CapText(Settings.frameCap)
                + (Settings.vsync ? ", vsync" : "") + (Settings.msaa > 0 ? ", " + Settings.msaa + "x MSAA" : "") + ")", style);
        }
    }
}
