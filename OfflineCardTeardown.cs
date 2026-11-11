using System;
using System.Reflection;
using HarmonyLib;
using SPTESL;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OfflineEngine
{
    public static class OfflineCardTeardown
    {
        [ThreadStatic] private static Card3D destroying;

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Card3D), "OnDestroy"),
                new HarmonyMethod(typeof(OfflineCardTeardown), "EnterDestroy"), null, null,
                new HarmonyMethod(typeof(OfflineCardTeardown), "LeaveDestroy"), null);
            harmony.Patch(AccessTools.Method(typeof(Card3D), "UpdateCardDragSettings"),
                new HarmonyMethod(typeof(OfflineCardTeardown), "AllowDragSetup"), null, null, null, null);
        }

        private static void EnterDestroy(Card3D __instance, out Card3D __state)
        {
            __state = destroying;
            if (OfflineBootstrapPlugin.Active && OfflineBoardAdapter.IsBoardMode) destroying = __instance;
        }

        private static Exception LeaveDestroy(Exception __exception, Card3D __state)
        {
            destroying = __state;
            return __exception;
        }

        private static bool AllowDragSetup(Card3D __instance)
        {
            // Original ResetState also serves pooling, where this method is required.
            // Only destruction skips drag/collider/shadow/animator reconfiguration:
            // its AddComponent<CardMomentum> cannot succeed on a dying GameObject.
            return !object.ReferenceEquals(destroying, __instance);
        }

        // Scene unload destroys the audio manager before some Card3D objects.
        // Release through the original method while all managers are still live.
        // Include pooled/inactive views, and stop their independent load coroutine
        // so it cannot acquire another receipt after this point.
        public static void BeforeSceneUnload()
        {
            if (!OfflineBootstrapPlugin.Active || !OfflineBoardAdapter.IsBoardMode)
                throw new InvalidOperationException("Card teardown requires guarded offline board mode.");
            Scene scene = SceneManager.GetActiveScene();
            MethodInfo stop = AccessTools.Method(typeof(CardFullView), "StopLoadRoutines", Type.EmptyTypes);
            if (stop == null) throw new MissingMethodException("CardFullView", "StopLoadRoutines");
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CardFullView view in root.GetComponentsInChildren<CardFullView>(true))
                {
                    stop.Invoke(view, null);
                    view.DisposeAssets();
                    count++;
                }
            Debug.Log("OFFLINE CARD TEARDOWN: released original assets for " + count + " outgoing views before " + scene.name + " unload.");
        }
    }
}
