using System;
using System.Collections.Generic;
using System.IO;
using FullSerializer;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mod boards. The client has five playmats (PlaymatType: Original, IsleOfMadness, AllianceWar, Elsweyr,
    // RealmsOfOblivion), each a prefab chosen by PlaymatSelector: by the scenario in single-player story/puzzle matches,
    // otherwise from the match id. A mod board re-skins one base playmat: its painted table (the base's main texture, 2048x1024)
    // is replaced by the mod's image on the instance's own materials once the prefab is created; props stay.
    //   mode "rotation": joins the random choice; "replace": always shown instead of that base.
    // OfflineGraphicsSettings.board forces one board everywhere ("" = the game's choice).
    public static class OfflineBoards
    {
        public sealed class Board { public string Key, Mod, Slug, Name, Image, Dir; public PlaymatType Base; public bool Replace; public Dictionary<string, fsData> Edits; }
        public static readonly List<Board> Boards = new List<Board>();
        public static readonly Dictionary<PlaymatType, string> MainTexture = new Dictionary<PlaymatType, string> {
            { PlaymatType.Original, "Playmat_01_D" }, { PlaymatType.IsleOfMadness, "IOM_Playmat_01_D" }, { PlaymatType.AllianceWar, "Playmat_AW_01_D" },
            { PlaymatType.Elsweyr, "ELS_Playmat_01_D" }, { PlaymatType.RealmsOfOblivion, "OC_Playmat_01" } };
        private static readonly string[] TextureProperties = { "_BoardImage", "_MainTex", "_Diffuse", "_DiffuseTexture", "_BaseMap", "_Albedo" };   // per-board shader names (Original, IOM/ELS, AW, OC)
        private static Board pending;            // the rotation's pick for the next playmat instance
        private static bool choosing;            // inside RandomPrefix's own ChangePlaymat call
        private static int skinnedInstance; private static string skinnedKey;
        public static int SkinsApplied;

        public static void Load(string modId, string dir, List<string> problems)
        {
            string manifest = Path.Combine(dir, "boards.json");
            if (!File.Exists(manifest)) return;
            foreach (fsData e in fsJsonParser.Parse(File.ReadAllText(manifest)).AsDictionary["boards"].AsList)
            {
                Dictionary<string, fsData> b = e.AsDictionary;
                string slug = b["slug"].AsString, png = b.ContainsKey("image") && b["image"].IsString && b["image"].AsString.Length > 0 ? Path.Combine(dir, b["image"].AsString) : null;
                PlaymatType type;
                try { type = (PlaymatType)Enum.Parse(typeof(PlaymatType), b.ContainsKey("base") ? b["base"].AsString : "Original", true); }
                catch (ArgumentException) { problems.Add(modId + ": board " + slug + " has an unknown base"); continue; }
                if (!MainTexture.ContainsKey(type)) { problems.Add(modId + ": board " + slug + " base " + type + " cannot be re-skinned"); continue; }
                if (png != null && !File.Exists(png)) { problems.Add(modId + ": board " + slug + " image missing"); png = null; }
                Dictionary<string, fsData> edits = b.ContainsKey("edits") && b["edits"].IsDictionary ? b["edits"].AsDictionary : null;
                if (png == null && edits == null) { problems.Add(modId + ": board " + slug + " has neither an image nor edits"); continue; }
                Boards.Add(new Board { Key = modId + "/" + slug, Mod = modId, Slug = slug, Name = b.ContainsKey("name") ? b["name"].AsString : slug, Image = png,
                    Dir = dir, Edits = edits, Base = type, Replace = b.ContainsKey("mode") && b["mode"].AsString == "replace" });
            }
        }

        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(PlaymatSelector), "SetRandomPlaymat"), new HarmonyMethod(typeof(OfflineBoards).GetMethod("RandomPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(PlaymatSelector), "ChangePlaymat"), new HarmonyMethod(typeof(OfflineBoards).GetMethod("ChangePrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(PlaymatSelector), "Update"), null, new HarmonyMethod(typeof(OfflineBoards).GetMethod("UpdatePostfix")), null, null, null);
        }

        // The board shown now (or about to be): music can follow it (OfflineMusic "Board:<mod>/<board>").
        public static Board Current { get { return pending ?? Find(skinnedKey); } }

        public static Board Find(string key) { return string.IsNullOrEmpty(key) ? null : Boards.Find(delegate(Board b) { return b.Key == key; }); }

        // The match's random board: the original production mats plus rotation boards, picked from the match id as the client does.
        public static bool RandomPrefix(PlaymatSelector __instance, BoardState boardState)
        {
            if (OfflineModes.Active != null && OfflineModes.Active.Board != null && Find(OfflineModes.Active.Board) == null)
            {   // the mode names one of the five game boards
                try
                {
                    PlaymatType type = (PlaymatType)Enum.Parse(typeof(PlaymatType), OfflineModes.Active.Board, true);
                    choosing = true;
                    try { AccessTools.Method(typeof(PlaymatSelector), "ChangePlaymat").Invoke(__instance, new object[] { type, boardState }); }
                    finally { choosing = false; }
                    return false;
                }
                catch (ArgumentException) { }
            }
            Board forced = Find(OfflineModes.Active != null && OfflineModes.Active.Board != null ? OfflineModes.Active.Board : OfflineGraphics.Settings.board);
            List<Board> rotation = Boards.FindAll(delegate(Board b) { return !b.Replace; });
            if (forced == null && rotation.Count == 0) return true;
            Board pick = forced;
            if (pick == null)
            {
                PlaymatType[] production = (PlaymatType[])AccessTools.Field(typeof(PlaymatSelector), "productionMatTypes").GetValue(__instance);
                int originals = production == null ? 0 : production.Length;
                string id = GameNetworkManager.Instance == null ? "invalid" : (GameNetworkManager.Instance.MatchId ?? "invalid");
                int slot = (int)((uint)MurmurHash3.Hash(id + "|mods") % (uint)(originals + rotation.Count));
                if (slot < originals) return true;           // an original mat: the client's own choice
                pick = rotation[slot - originals];
            }
            pending = pick; choosing = true;
            try { AccessTools.Method(typeof(PlaymatSelector), "ChangePlaymat").Invoke(__instance, new object[] { pick.Base, boardState }); }
            finally { choosing = false; }
            skinnedInstance = 0;                             // re-skin even when the base mat stays loaded
            return false;
        }

        // Scenario-chosen mats: a forced board overrides, a "replace" board re-skins its base.
        public static void ChangePrefix(ref PlaymatType matType)
        {
            if (choosing) return;
            Board forced = Find(OfflineModes.Active != null && OfflineModes.Active.Board != null ? OfflineModes.Active.Board : OfflineGraphics.Settings.board);
            pending = forced;                                // a scenario's own mat never keeps an earlier rotation pick
            if (forced != null) { matType = forced.Base; skinnedInstance = 0; }
        }

        public static void UpdatePostfix(PlaymatSelector __instance)
        {
            GameObject instance = (GameObject)AccessTools.Field(typeof(PlaymatSelector), "playmatInstance").GetValue(__instance);
            if (instance == null) return;
            PlaymatType type = (PlaymatType)AccessTools.Field(typeof(PlaymatSelector), "currentMatType").GetValue(__instance);
            Board board = pending != null && pending.Base == type ? pending
                : Boards.FindLast(delegate(Board b) { return b.Replace && b.Base == type; });   // later mod in the load order wins
            string key = board == null ? "" : board.Key;
            if (instance.GetInstanceID() == skinnedInstance && key == skinnedKey) return;
            skinnedInstance = instance.GetInstanceID(); skinnedKey = key;
            OfflineBoardEdits.Undo();                        // the previous board's edits on this instance
            if (board == null || board.Image == null)
            {
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        foreach (string prop in TextureProperties)
                            if (m != null && m.HasProperty(prop)) Restore(m.GetTexture(prop) as Texture2D);
            }
            else
            {
                try { Debug.Log("OFFLINE BOARDS: " + board.Key + " on " + type + ": " + Skin(instance, board) + " materials re-skinned"); }
                catch (Exception e) { Debug.LogWarning("OFFLINE BOARDS: " + board.Key + ": " + e.Message); }
            }
            if (board != null && board.Edits != null)
            {
                try { OfflineBoardEdits.Apply(instance, board); }
                catch (Exception e) { Debug.LogWarning("OFFLINE BOARDS: " + board.Key + " edits: " + e); }
            }
        }

        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        // Boards are seen at an angle and at a distance: full mip chain, trilinear, clamped (the originals are mipmapped).
        private static Texture2D BoardTexture(Board board, Texture2D like)
        {
            Texture2D tex;
            string key = board.Key + "|" + like.width + "x" + like.height + "|" + like.format;
            if (textures.TryGetValue(key, out tex) && tex != null) return tex;
            Texture2D raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(raw, File.ReadAllBytes(board.Image), false)) throw new InvalidOperationException("board image unreadable: " + board.Image);
            if (raw.width != like.width || raw.height != like.height) raw = Resized(raw, like.width, like.height);
            tex = new Texture2D(like.width, like.height, like.format == TextureFormat.DXT1 ? TextureFormat.RGB24 : TextureFormat.RGBA32, like.mipmapCount > 1);   // RGB compresses to DXT1, RGBA to DXT5
            tex.SetPixels32(raw.GetPixels32());
            tex.Apply(like.mipmapCount > 1, false);
            if (like.format == TextureFormat.DXT1 || like.format == TextureFormat.DXT5) tex.Compress(true);
            if (tex.format != like.format || tex.mipmapCount != like.mipmapCount)
                throw new InvalidOperationException("board texture is " + tex.format + "/" + tex.mipmapCount + ", the original " + like.format + "/" + like.mipmapCount);
            tex.name = "mod-board-" + board.Slug;
            textures[key] = tex;
            return tex;
        }

        private static Texture2D Resized(Texture2D src, int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            Texture2D outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0); outTex.Apply(false, false);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            return outTex;
        }

        public static int Skin(GameObject instance, Board board)
        {
            string wanted = MainTexture[board.Base];
            int n = 0;
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    foreach (string prop in TextureProperties)
                    {
                        if (m == null || !m.HasProperty(prop)) continue;
                        Texture2D original = m.GetTexture(prop) as Texture2D;
                        if (original == null || (original.name != wanted && !original.name.StartsWith("mod-board", StringComparison.Ordinal))) continue;
                        Swap(original, board);
                        n++;
                    }
            if (n > 0) SkinsApplied++;
            return n;
        }

        // The original texture keeps its identity (every material, animation and script that holds it keeps working); only its
        // pixels change: the mod image compressed to the same format and mip count, then copied on the GPU. A copy of the
        // original pixels is kept so a later match on that base without a mod board gets them back.
        private static readonly Dictionary<int, Texture2D> backups = new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, string> showing = new Dictionary<int, string>();

        private static void Swap(Texture2D original, Board board)
        {
            int id = original.GetInstanceID();
            string current;
            if (showing.TryGetValue(id, out current) && current == board.Key) return;
            if (!backups.ContainsKey(id))
            {
                Texture2D keep = new Texture2D(original.width, original.height, original.format, original.mipmapCount > 1);
                Graphics.CopyTexture(original, keep);
                backups[id] = keep;
            }
            Texture2D src = BoardTexture(board, original);
            Graphics.CopyTexture(src, original);
            showing[id] = board.Key;
            Debug.Log("OFFLINE BOARDS: " + board.Key + " copied into " + original.name + " (" + original.width + "x" + original.height + " " + original.format + ", " + original.mipmapCount + " mips)");
        }

        // Diagnostic: the mean colour of a texture as the GPU sees it (blit to a small render texture, read back).
        public static string Average(Texture t)
        {
            RenderTexture rt = RenderTexture.GetTemporary(16, 8, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            Texture2D read = new Texture2D(16, 8, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, 16, 8), 0, 0); read.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            Color32[] px = read.GetPixels32(); long r = 0, g = 0, b = 0;
            foreach (Color32 c in px) { r += c.r; g += c.g; b += c.b; }
            UnityEngine.Object.Destroy(read);
            return "(" + r / px.Length + "," + g / px.Length + "," + b / px.Length + ")";
        }

        public static void Restore(Texture2D original)
        {
            Texture2D keep;
            if (original != null && backups.TryGetValue(original.GetInstanceID(), out keep)) { Graphics.CopyTexture(keep, original); showing.Remove(original.GetInstanceID()); }
        }
    }
}
