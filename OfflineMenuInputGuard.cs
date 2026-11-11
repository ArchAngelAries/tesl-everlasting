using System;
using HarmonyLib;
using SPTESL;
using UnityEngine;
using UnityEngine.UI;

namespace OfflineEngine
{
    // While the F10 menu is open, clicks on it must not reach the game behind it (owner 2026-11-06: menu clicks caused
    // misclicks on the board). Only the window's area is blocked, the rest of the screen plays normally.
    //  - The board reads the mouse itself (BoardInputController.CollectRawInput): over the window it gets no press and
    //    no card under the pointer. A drag started on the board keeps going.
    //  - The game's own buttons (uGUI: End Turn, hand, menus) are stopped by an invisible raycast panel over the window.
    public static class OfflineMenuInputGuard
    {
        private static bool installed;
        private static GameObject blocker;
        private static RectTransform panel;

        // The open menu's window in IMGUI coordinates (top-left origin); empty when closed.
        public static Rect Window;
        public static bool Open;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            new Harmony("local.tesl.offline.menu-input").Patch(AccessTools.Method(typeof(BoardInputController), "CollectRawInput"), null,
                new HarmonyMethod(typeof(OfflineMenuInputGuard).GetMethod("RawInputPostfix")), null, null, null);
        }

        public static bool PointerOver(Vector3 screen)
        {
            return Open && Window.Contains(new Vector2(screen.x, Screen.height - screen.y));
        }

        public static void RawInputPostfix(ref BoardInputController.RawInput __result)
        {
            if (!PointerOver(__result.screenPosition)) return;
            if (__result.lastTouchEvent == BoardInputController.TouchEventType.Held) return;   // a board drag in progress
            __result.touchEvent = BoardInputController.TouchEventType.None;
            __result.cardUnderPointer = null;
            __result.doubleCardHalfUnderPointer = null;
            __result.logEntryUnderPointer = null;
            __result.inspectionCardUnderPointer = null;
            __result.avatarCardUnderPointer = null;
            __result.cardUnderPointerId = CardNetUid.GetInvalid();
        }

        // Called every frame by the menu: keeps the invisible panel on the window while it is open.
        public static void Track(bool open, Rect window)
        {
            Open = open; Window = window;
            if (!open) { if (blocker != null && blocker.activeSelf) blocker.SetActive(false); return; }
            if (blocker == null)
            {
                blocker = new GameObject("TESL Everlasting F10 input blocker");
                UnityEngine.Object.DontDestroyOnLoad(blocker);
                Canvas canvas = blocker.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
                blocker.AddComponent<GraphicRaycaster>();
                GameObject child = new GameObject("area");
                child.transform.SetParent(blocker.transform, false);
                Image image = child.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);
                image.raycastTarget = true;
                panel = child.GetComponent<RectTransform>();
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            }
            if (!blocker.activeSelf) blocker.SetActive(true);
            panel.anchoredPosition = new Vector2(window.x, -window.y);
            panel.sizeDelta = new Vector2(window.width, window.height);
        }
    }
}
