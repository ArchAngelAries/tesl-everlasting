using System;
using System.Collections.Generic;
using HarmonyLib;
using SPTESL;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OfflineEngine
{
    // Extra emotes beyond the game's eight. An extra is an AvatarEmoteData in the avatar's emote package with a type number
    // past the built-in ones (ExtraBase + n); the client passes emote numbers straight through (UserEmotesWin ->
    // BoardInputController -> BoardConductor.ShowUserEmote looks the type up in the speaker's package), so text and audio
    // play like the originals. The in-match panel gets a "More" button that flips its eight buttons through pages of the
    // player's extras. An extra may be marked "like" one of the eight: the AI then mixes it into that emote's uses, and a
    // human's extra is answered as that emote.
    public static class OfflineExtraEmotes
    {
        public const int ExtraBase = 100;
        public sealed class Extra { public UserEmoteType Type; public string Label; public UserEmoteType? Like; }
        public static readonly Dictionary<int, List<Extra>> ByPackage = new Dictionary<int, List<Extra>>();
        public static int Page, PagesShown;
        private static Button more;
        private static UserEmotesWin moreOwner;
        private static string[] standardTexts;
        private static readonly System.Random rng = new System.Random();

        public static UserEmoteType? ParseLike(string like)
        {
            if (string.IsNullOrEmpty(like)) return null;
            try { UserEmoteType t = (UserEmoteType)Enum.Parse(typeof(UserEmoteType), like.Replace(" ", ""), true); return (int)t < 8 ? (UserEmoteType?)t : null; }
            catch (ArgumentException) { return null; }
        }

        public static Extra Add(AvatarEmotePackage package, AvatarEmoteData data, string label, UserEmoteType? like)
        {
            List<Extra> list;
            if (!ByPackage.TryGetValue(package.ResourceId.Id, out list)) ByPackage[package.ResourceId.Id] = list = new List<Extra>();
            data.EmoteType = (UserEmoteType)(ExtraBase + list.Count);
            package.EmoteData.Add(data);
            Extra e = new Extra { Type = data.EmoteType, Label = label, Like = like };
            list.Add(e);
            return e;
        }

        public static List<Extra> For(AvatarEmotePackage p)
        {
            List<Extra> list;
            return p != null && ByPackage.TryGetValue(p.ResourceId.Id, out list) ? list : new List<Extra>();
        }

        public static Extra Find(AvatarEmotePackage p, UserEmoteType type)
        {
            return For(p).Find(delegate(Extra e) { return e.Type == type; });
        }

        // The AI's line for an emote: the standard one or one of the extras marked like it.
        public static UserEmoteType Vary(AvatarEmotePackage p, UserEmoteType type)
        {
            List<Extra> alts = For(p).FindAll(delegate(Extra e) { return e.Like == type; });
            if (alts.Count == 0) return type;
            int pick = rng.Next(alts.Count + 1);
            return pick == alts.Count ? type : alts[pick].Type;
        }

        public static AvatarEmotePackage PlayerPackage()
        {
            BoardConductor bc = UnityEngine.Object.FindObjectOfType<BoardConductor>();
            MatchPlayerData data = bc == null || bc.BoardState == null ? null : bc.BoardState.FindMatchPlayerData(PlayerControllerType.Player);
            if (data == null) return null;
            AvatarCollection col = GameManager.Instance.Config.AvatarCollection;
            foreach (string name in new string[] { data.AvatarUniqueVoName, data.AvatarUniqueName })
            {
                if (string.IsNullOrEmpty(name)) continue;
                AvatarData a = col.Avatars.Find(delegate(AvatarData x) { return x != null && x.ResourceId.UniqueName == name; });
                if (a != null) return OfflineEmotes.Package(col, a.EmotePackage.resourceId);
            }
            return null;
        }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(UserEmotesWin), "EmoteUi_Show"), null, new HarmonyMethod(typeof(OfflineExtraEmotes).GetMethod("ShowPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(UserEmotesWin), "FindButtonEmoteType"), new HarmonyMethod(typeof(OfflineExtraEmotes).GetMethod("FindTypePrefix")), null, null, null, null);
        }

        private static Button[] Buttons(UserEmotesWin w) { return (Button[])AccessTools.Field(typeof(UserEmotesWin), "EmoteButtons").GetValue(w); }

        public static void ShowPostfix(UserEmotesWin __instance, bool show)
        {
            if (!show) return;
            try
            {
                List<Extra> extras = For(PlayerPackage());
                if (extras.Count == 0 && more == null) return;
                Button[] buttons = Buttons(__instance);
                if (standardTexts == null || moreOwner != __instance)
                {
                    standardTexts = new string[buttons.Length];
                    for (int i = 0; i < buttons.Length; i++) { TMP_Text t = buttons[i] == null ? null : buttons[i].GetComponentInChildren<TMP_Text>(true); standardTexts[i] = t == null ? "" : t.text; }
                }
                if ((more == null || moreOwner != __instance) && extras.Count > 0)
                {
                    Button template = buttons[buttons.Length - 1];
                    GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
                    clone.name = "OfflineMoreEmotes";
                    foreach (LocalizeTextMeshProUGUI loc in clone.GetComponentsInChildren<LocalizeTextMeshProUGUI>(true)) UnityEngine.Object.Destroy(loc);
                    more = clone.GetComponent<Button>();
                    more.onClick = new Button.ButtonClickedEvent();
                    // The columns keep their buttons where they were laid out; below them is the hand, so "More" goes one row
                    // above the right column's top button (Oops).
                    LayoutElement le = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
                    RectTransform top = (RectTransform)buttons[4].transform, next = (RectTransform)buttons[5].transform, dst = (RectTransform)clone.transform;
                    dst.anchorMin = top.anchorMin; dst.anchorMax = top.anchorMax; dst.pivot = top.pivot; dst.sizeDelta = top.rect.size;
                    dst.anchoredPosition = top.anchoredPosition + (top.anchoredPosition - next.anchoredPosition);
                    UserEmotesWin owner = __instance;
                    more.onClick.AddListener(delegate { Turn(owner, 1); });
                    moreOwner = __instance;
                    Page = 0;
                }
                if (more != null) more.gameObject.SetActive(extras.Count > 0);
                if (extras.Count == 0) Page = 0;
                Apply(__instance, extras);
            }
            catch (Exception e) { Debug.LogWarning("OFFLINE EMOTES: extra emote page failed: " + e.Message); }
        }

        public static int Pages(int extras) { return 1 + (extras + 7) / 8; }

        public static void Turn(UserEmotesWin w, int step)
        {
            List<Extra> extras = For(PlayerPackage());
            Page = extras.Count == 0 ? 0 : (Page + step) % Pages(extras.Count);
            PagesShown++;
            Apply(w, extras);
        }

        private static void Apply(UserEmotesWin w, List<Extra> extras)
        {
            Button[] buttons = Buttons(w);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                TMP_Text text = buttons[i].GetComponentInChildren<TMP_Text>(true);
                LocalizeTextMeshProUGUI loc = buttons[i].GetComponentInChildren<LocalizeTextMeshProUGUI>(true);
                if (Page == 0)
                {
                    buttons[i].gameObject.SetActive(true);
                    if (loc != null) loc.enabled = true;
                    if (text != null && standardTexts != null) text.text = standardTexts[i];
                    continue;
                }
                int n = (Page - 1) * 8 + i;
                buttons[i].gameObject.SetActive(n < extras.Count);
                if (n >= extras.Count) continue;
                if (loc != null) loc.enabled = false;
                if (text != null) text.text = extras[n].Label;
            }
            if (more != null)
            {
                TMP_Text t = more.GetComponentInChildren<TMP_Text>(true);
                int pages = Pages(extras.Count);
                if (t != null) t.text = Page == pages - 1 ? "Back" : pages > 2 ? "More (" + (Page + 1) + "/" + (pages - 1) + ")" : "More...";
                more.transform.SetAsLastSibling();
            }
        }

        public static bool FindTypePrefix(UserEmotesWin __instance, Button button, ref UserEmoteType __result)
        {
            if (Page == 0) return true;
            int i = Array.IndexOf(Buttons(__instance), button);
            List<Extra> extras = For(PlayerPackage());
            int n = (Page - 1) * 8 + i;
            if (i < 0 || n >= extras.Count) return true;
            __result = extras[n].Type;
            return false;
        }
    }
}
