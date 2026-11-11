using System;
using System.Collections;
using HarmonyLib;
using SparkleServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OfflineEngine
{
    // In-game player name entry. The shipped client takes the name from the Bethesda account (AuthData.nickname)
    // and has no rename screen, so offline the name is chosen in a client popup (WinManager's pooled UIPopupWindow)
    // holding a TextMeshPro input in the popup's own font:
    //  - once at the first main menu (player state nameChosen), and
    //  - any time by clicking the player name on Profile > Overview (PlayerProfileLandingWin).
    // Saving validates with OfflineLocalProfile, then updates AuthData.nickname and refreshes the client profile.
    public static class OfflineNameEntry
    {
        public const int MaxLength = 32;
        private static OfflinePlayerState state;
        private static GameObject content;
        private static TMP_InputField input;
        private static TextMeshProUGUI prompt;
        private static bool showing;
        private static UIPopupWindow current;

        public static void Install(Harmony harmony, MonoBehaviour host, OfflinePlayerState playerState)
        {
            state = playerState;
            harmony.Patch(AccessTools.Method(typeof(PlayerProfileLandingWin), "UpdateUi"), null, new HarmonyMethod(typeof(OfflineNameEntry).GetMethod("LandingPostfix")), null, null, null);
            host.StartCoroutine(FirstRunPrompt());
        }

        // Profile > Overview: the player name becomes a button that opens the name entry.
        public static void LandingPostfix(PlayerProfileLandingWin __instance)
        {
            TextMeshProUGUI name = AccessTools.Field(typeof(PlayerProfileLandingWin), "_playerNameText").GetValue(__instance) as TextMeshProUGUI;
            if (name == null || name.GetComponent<Button>() != null) return;
            name.raycastTarget = true;
            Button button = name.gameObject.AddComponent<Button>();
            button.onClick.AddListener(delegate { Show(false); });
        }

        private static IEnumerator FirstRunPrompt()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);
                if (state == null || state.Snapshot().nameChosen) yield break;
                if (Pixelplacement.Singleton<WinManager>.Instance == null || UnityEngine.Object.FindObjectOfType<SPTESL.MainMenu>() == null) continue;
                Pixelplacement.State current = null;
                foreach (Pixelplacement.StateMachine machine in UnityEngine.Object.FindObjectsOfType<Pixelplacement.StateMachine>())
                    if (machine.currentState != null) { current = machine.currentState.GetComponent<Pixelplacement.State>(); break; }
                if (!(current is SPTESL.MainMenu) || Pixelplacement.Singleton<WinManager>.Instance.PopupCount > 0) continue;
                yield return new WaitForSeconds(1.5f); // let the menu's own onboarding popups claim the stack first
                if (Pixelplacement.Singleton<WinManager>.Instance.PopupCount > 0) continue;
                Show(true);
                yield break;
            }
        }

        public static void Show(bool firstRun)
        {
            if (showing) return;
            UIPopupWindow popup = Pixelplacement.Singleton<WinManager>.Instance.Popup;
            current = popup;
            Build(popup);
            popup.AddChildContent(content);
            input.text = OfflineNativeFlow.Profile.Username;
            prompt.text = firstRun
                ? "Choose the name shown on your profile and to your opponents. You can change it later from your Profile."
                : "Choose the name shown on your profile and to your opponents.";
            popup.SetTitleText("PLAYER NAME");
            popup.SetConfirmButtonText("OK");
            popup.SetCancelButtonText("CANCEL");
            popup.SetHideOnButtonPress(false);
            popup.SetConfirmCallback(delegate { if (Save(popup)) { popup.Hide(); } });
            popup.SetCancelCallback(delegate { MarkChosen(); popup.Hide(); });
            popup.SetHideCallback(delegate { showing = false; current = null; });
            popup.Show(content, false, true, !firstRun, false, input.gameObject);
            Pixelplacement.Singleton<WinManager>.Instance.SetWindowAsLastSibling(popup.gameObject); // above menu windows (Daily Quests)
            showing = true;
            input.ActivateInputField();
        }

        public static bool IsShowing { get { return showing; } }

        // Diagnostic only: type a name and press the popup's own OK button.
        public static void DiagnosticSubmit(string name)
        {
            if (!showing || current == null) return;
            input.text = name;
            Button ok = AccessTools.Field(typeof(UIPopupWindow), "_buttonConfirm").GetValue(current) as Button;
            if (ok != null) ok.onClick.Invoke();
        }

        private static bool Save(UIPopupWindow popup)
        {
            string value = input.text == null ? "" : input.text.Trim();
            if (value.Length == 0) value = OfflineNativeFlow.Profile.Username;
            try { value = OfflineLocalProfile.ValidateUsername(value); }
            catch (ArgumentException e) { prompt.text = "<color=#ff6060>" + e.Message + "</color>"; return false; }
            if (value != OfflineNativeFlow.Profile.Username) OfflineNativeFlow.Profile.SetUsername(value);
            ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
            if (services != null && services.AuthData != null) services.AuthData.nickname = value;
            if (services != null && services.Profile != null) services.Profile.Refresh();
            MarkChosen();
            Debug.Log("OFFLINE NATIVE: player name set to '" + value + "'.");
            return true;
        }

        private static void MarkChosen() { if (state != null) state.Mutate(delegate(OfflinePlayerStateData d) { d.nameChosen = true; }); }

        // Content built once, styled from the popup's own body text (font, colour), and moved between pooled popups.
        private static void Build(UIPopupWindow popup)
        {
            if (content != null) return;
            TextMeshProUGUI body = AccessTools.Field(typeof(UIPopupWindow), "_genericBodyText").GetValue(popup) as TextMeshProUGUI;
            content = new GameObject("OfflineNameEntry", typeof(RectTransform));
            ((RectTransform)content.transform).sizeDelta = new Vector2(680f, 200f); // the popup container does not size its children
            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18f; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(30, 30, 10, 10);
            LayoutElement contentSize = content.AddComponent<LayoutElement>();
            contentSize.preferredWidth = 680f; contentSize.minWidth = 680f; contentSize.preferredHeight = 200f; contentSize.minHeight = 200f;

            GameObject promptGo = new GameObject("Prompt", typeof(RectTransform));
            promptGo.transform.SetParent(content.transform, false);
            prompt = promptGo.AddComponent<TextMeshProUGUI>();
            Style(prompt, body, 26f);
            prompt.alignment = TextAlignmentOptions.Center;
            promptGo.AddComponent<LayoutElement>().preferredHeight = 70f;

            GameObject field = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            field.name = "NameInput";
            field.transform.SetParent(content.transform, false);
            field.AddComponent<LayoutElement>().preferredHeight = 64f;
            Image frame = field.GetComponent<Image>();
            if (frame != null) frame.color = new Color(0.06f, 0.06f, 0.07f, 0.95f);
            Outline outline = field.AddComponent<Outline>();
            outline.effectColor = new Color(0.78f, 0.64f, 0.33f, 0.9f); outline.effectDistance = new Vector2(2f, -2f);
            input = field.GetComponent<TMP_InputField>();
            input.characterLimit = MaxLength;
            input.lineType = TMP_InputField.LineType.SingleLine;
            TextMeshProUGUI text = input.textComponent as TextMeshProUGUI;
            if (text != null) { Style(text, body, 32f); text.alignment = TextAlignmentOptions.Center; }
            TextMeshProUGUI placeholder = input.placeholder as TextMeshProUGUI;
            if (placeholder != null) { Style(placeholder, body, 30f); placeholder.text = "Enter a name"; placeholder.alignment = TextAlignmentOptions.Center; placeholder.color = new Color(1f, 1f, 1f, 0.35f); }
            input.onSubmit.AddListener(delegate { /* Enter confirms through the popup's own submit handling */ });
            content.SetActive(false);
        }

        private static void Style(TextMeshProUGUI text, TextMeshProUGUI source, float size)
        {
            if (source != null) { text.font = source.font; text.fontSharedMaterial = source.fontSharedMaterial; text.color = source.color; }
            text.fontSize = size; text.enableWordWrapping = true;
        }
    }
}
