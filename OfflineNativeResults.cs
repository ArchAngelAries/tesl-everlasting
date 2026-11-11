using System;
using System.Reflection;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Presentation only: caller owns terminal validation, transport shutdown,
    // menu navigation and replay. Never invoke the remote EndGameState pipeline.
    public static class OfflineNativeResults
    {
        private const string WindowName = "EndGameWin";
        private static bool installed;
        private static bool pending;
        private static int generation;
        private static WinManager owner;
        private static EndGameWin window;
        private static Action completionHandler;

        public static bool IsPending { get { return pending; } }
        public static bool IsVisible { get { return window != null && window.gameObject.activeInHierarchy; } }
        public static EndGameWin Window { get { return window; } }

        public static void Install(Harmony harmony)
        {
            RequireOffline();
            if (installed) return;
            if (harmony == null) throw new ArgumentNullException("harmony");
            // Defensive boundary: even an accidental XP presentation call must
            // not claim rewards or inspect a fabricated remote player profile.
            MethodInfo original = AccessTools.Method(typeof(EndGameWin), "UpdateXpUi", Type.EmptyTypes);
            if (original == null) throw new MissingMethodException(typeof(EndGameWin).FullName, "UpdateXpUi");
            harmony.Patch(original, new HarmonyMethod(typeof(OfflineNativeResults), "AllowOnlineOnly"), null, null, null, null);
            installed = true;
        }

        private static bool AllowOnlineOnly() { return !OfflineBootstrapPlugin.Active; }

        public static void Show(SettingsData.EndGameType result, Action onComplete)
        {
            RequireOffline();
            if (!installed) throw new InvalidOperationException("Install the native offline results helper before use.");
            if (result != SettingsData.EndGameType.Win && result != SettingsData.EndGameType.Lose)
                throw new InvalidOperationException("Offline results require a completed native Win or Lose.");
            if (pending || window != null) throw new InvalidOperationException("An offline result window is already active.");
            WinManager manager = Singleton<WinManager>.Instance;
            if (manager == null) throw new InvalidOperationException("Native WinManager is absent.");
            if (manager.IsWindowActive(WindowName))
                throw new InvalidOperationException("Another native result window is already active.");
            owner = manager;
            pending = true;
            int requestGeneration = ++generation;
            try
            {
                manager.ShowWindow(WindowName, delegate(GameObject go)
                {
                    // Hide may cancel an asynchronous original Resources load.
                    if (requestGeneration != generation)
                    {
                        if (manager != null) manager.DestroyWindow(WindowName);
                        pending = false;
                        owner = null;
                        return;
                    }
                    pending = false;
                    try
                    {
                        if (go == null || go.GetComponent<EndGameWin>() == null)
                            throw new InvalidOperationException("Original EndGameWin failed to load.");
                        window = go.GetComponent<EndGameWin>();
                        window.SetData(result);
                        completionHandler = delegate
                        {
                            Hide();
                            if (onComplete != null) onComplete();
                        };
                        window.OnEndGameWinComplete += completionHandler;
                        window.UpdateUi();
                        window.SetLoadingActive(false);
                        UnityEngine.Debug.Log("OFFLINE RESULTS: original " + result + " window ready; native close control enabled.");
                    }
                    catch
                    {
                        Hide();
                        throw;
                    }
                }, false, true);
            }
            catch
            {
                Hide();
                throw;
            }
        }

        public static void Hide()
        {
            ++generation;
            // Keep pending until the canceled load callback arrives. A new Show
            // must not race WinManager's name-based window registration/removal.
            bool wasPending = pending;
            if (window != null && completionHandler != null)
                window.OnEndGameWinComplete -= completionHandler;
            window = null;
            completionHandler = null;
            if (owner != null && !wasPending) owner.DestroyWindow(WindowName);
            if (!wasPending) owner = null;
        }

        private static void RequireOffline()
        {
            if (!OfflineBootstrapPlugin.Active)
                throw new InvalidOperationException("Native local results require guarded offline startup.");
        }
    }
}
