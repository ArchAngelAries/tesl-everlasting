using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AssetBundles;
using Pixelplacement;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Card previews for TESL Forge, drawn by the game itself: `--tesl-render-cards=<folder>` (with --tesl-offline-native,
    // usually -batchmode and --tesl-mods-list=<list>). <folder>/cards.txt lists card ids, one per line, or "mod" for every
    // loaded mod card. As soon as the game's card factory is ready (before any menu, so no player profile is used), each card
    // is built like the pack-opening screen does (CardFactory.CloneCard + Card3D.SetCardAndRefreshVisuals), drawn by its own
    // camera far from the scene onto a transparent texture, and saved as <folder>/<id>.png. Then <folder>/done.txt is written
    // and the process ends.
    // cards.txt may also name other mod content (drawn after the cards):
    //   back:<card back key> or "backs"    -> back_<key>.png   a card's back face with that card back (as in packs and decks)
    //   deck:<deck type name> or "decks"   -> deck_<name>.png  the deck's banner: the game's own deck tile (name, attributes, art)
    //   board:<board key> or "boards"      -> board_<key>.png  the board prefab of its base mat with the mod image, seen from above
    //   tile:<card id> or "tiles"          -> tile_<id>.png    the card's row in deck lists (small deck card, uses ListImage* framing)
    public static class OfflineCardRender
    {
        public const string Flag = "--tesl-render-cards=";
        private const int Layer = 30, Height = 1024;

        public static string Folder()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(Flag, StringComparison.Ordinal)) return arg.Substring(Flag.Length).Trim('"');
            return null;
        }

        public static IEnumerator Run(string folder)
        {
            List<string> log = new List<string>();
            try { Directory.CreateDirectory(folder); } catch (Exception) { }
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 240f && (Singleton<CardFactory>.Instance == null || !Singleton<CardFactory>.Instance.IsReady()
                   || GameManager.Instance == null || GameManager.Instance.Config == null || !OfflineMods.Ready))
                yield return null;
            if (Singleton<CardFactory>.Instance == null || !OfflineMods.Ready) { Finish(folder, "error: the game's card factory or mods did not load", log); yield break; }
            // The card prefabs load when the game first needs them (FXFactory.Initialize); load them now.
            while (Time.realtimeSinceStartup - start < 240f && !AssetCatalog.IsReady()) yield return null;
            yield return Singleton<FXFactory>.Instance.Initialize();
            while (Time.realtimeSinceStartup - start < 240f && Singleton<FXFactory>.Instance.IsLoading()) yield return null;
            List<int> ids = new List<int>();
            List<string> backs = new List<string>(), decks = new List<string>(), boards = new List<string>();
            List<int> tiles = new List<int>();
            string list = Path.Combine(folder, "cards.txt");
            foreach (string line in File.Exists(list) ? File.ReadAllLines(list) : new string[] { "mod" })
            {
                string l = line.Trim(); int id;
                if (l == "mod") ids.AddRange(OfflineMods.CardIds);
                else if (l == "backs") backs.AddRange(OfflineMods.CardBackKeys);
                else if (l == "decks") foreach (DeckInfo d in OfflineMods.Decks) decks.Add(d.TypeName);
                else if (l == "boards") foreach (OfflineBoards.Board b in OfflineBoards.Boards) boards.Add(b.Key);
                else if (l.StartsWith("back:", StringComparison.Ordinal)) backs.Add(l.Substring(5));
                else if (l.StartsWith("deck:", StringComparison.Ordinal)) decks.Add(l.Substring(5));
                else if (l.StartsWith("board:", StringComparison.Ordinal)) boards.Add(l.Substring(6));
                else if (l == "tiles") tiles.AddRange(OfflineMods.CardIds);
                else if (l.StartsWith("tile:", StringComparison.Ordinal)) { int t; if (int.TryParse(l.Substring(5), out t)) tiles.Add(t); }
                else if (int.TryParse(l, out id)) ids.Add(id);
            }
            Camera cam = new GameObject("TESL Forge card camera").AddComponent<Camera>();
            cam.enabled = false; cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0); cam.cullingMask = 1 << Layer; cam.nearClipPlane = 0.01f; cam.farClipPlane = 1000f;
            Light light = new GameObject("TESL Forge card light").AddComponent<Light>();
            light.type = LightType.Directional; light.cullingMask = 1 << Layer; light.intensity = 1f;
            int n = 0;
            // The first card drawn in a process misses late-loading parts (cost, stats): it is drawn to a throwaway file and drawn again at the end.
            int warmups = Math.Min(1, ids.Count);   // the first build is a warm-up (drawn and thrown away); those cards are drawn again last
            for (int w = 0; w < warmups; w++) ids.Add(ids[w]);
            int others = 0;
            int warm = warmups;
            foreach (int id in ids)
            {
                Card def = GameManager.Instance.GetCard(id);
                if (def == null) { log.Add(id + "\tunknown card"); continue; }
                Card3D card = Singleton<CardFactory>.Instance.CloneCard();
                if (card == null) { log.Add(id + "\tno card instance"); continue; }
                card.transform.position = new Vector3(5000f, 5000f, 5000f);
                card.gameObject.SetActive(true);
                // The game's own layout, facing this camera, before the card's data (which then hides the parts the card lacks).
                try { HarmonyLib.AccessTools.Field(typeof(Card3D), "_facingCamera").SetValue(card, cam); } catch (Exception e) { log.Add(id + "\tcamera: " + e.Message); }
                Exception failed = null;
                Coroutine set = null;
                try { set = card.SetCardAndRefreshVisuals(new ClientCard(def), null); } catch (Exception e) { failed = e; }
                if (failed != null) { log.Add(id + "\t" + failed.Message); UnityEngine.Object.Destroy(card.gameObject); continue; }
                if (set != null) yield return set;
                float wait = Time.realtimeSinceStartup;
                while (card.CardUI != null && card.CardUI.IsLoading() && Time.realtimeSinceStartup - wait < 15f) yield return null;
                try { card.SetLayoutStyle(CardVisualLayoutType.Default); } catch (Exception e) { log.Add(id + "\tlayout: " + e.Message); }
                try { set = card.SetCardAndRefreshVisuals(card.ClientCard, null); } catch (Exception e) { log.Add(id + "\trefresh: " + e.Message); set = null; }
                if (set != null) yield return set;
                float settle = Time.realtimeSinceStartup;   // the art fades in after it loads
                while (Time.realtimeSinceStartup - settle < 2.5f) yield return null;
                if (warm > 0)
                {   // a full draw into a throwaway file
                    warm--;
                    try { string tmp = Path.Combine(folder, "warmup.tmp.png"); Save(card, cam, light, tmp); File.Delete(tmp); } catch (Exception) { }
                    UnityEngine.Object.Destroy(card.gameObject); for (int f = 0; f < 10; f++) yield return null; continue;
                }
                try { Save(card, cam, light, Path.Combine(folder, id + ".png")); n++; log.Add(id + "\tok\t" + def.Name + "\t" + Describe(card)); }
                catch (Exception e) { log.Add(id + "\t" + e.Message); }
                UnityEngine.Object.Destroy(card.gameObject);
                yield return null;
            }
            int[] counts = new int[1];
            if (backs.Count > 0) yield return RenderBacks(folder, backs, cam, light, log, counts);
            if (decks.Count > 0) yield return RenderDecks(folder, decks, cam, light, log, counts);
            if (boards.Count > 0) yield return RenderBoards(folder, boards, cam, light, log, counts);
            if (tiles.Count > 0) yield return RenderTiles(folder, tiles, cam, light, log, counts);
            others = backs.Count + decks.Count + boards.Count + tiles.Count;
            Finish(folder, "rendered " + (n + counts[0]) + " of " + (ids.Count - warmups + others), log);
        }

        private static string FileSafe(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return key;
        }

        // Card backs: a card built as for the pack screen, given the card back, drawn from behind (only its back mesh).
        private static IEnumerator RenderBacks(string folder, List<string> keys, Camera cam, Light light, List<string> log, int[] counts)
        {
            Card def = null;
            def = GameManager.Instance.GetCard(NativeKeywordRules.LurkingCrocodileId);   // any original creature: only its back is drawn
            foreach (string key in keys)
            {
                CardBackData data = GameManager.Instance.Config.CardSharedGraphics.FindCardBackData(key);
                if (data == null || def == null) { log.Add("back:" + key + "\tunknown card back"); continue; }
                Card3D card = Singleton<CardFactory>.Instance.CloneCard();
                if (card == null) { log.Add("back:" + key + "\tno card instance"); continue; }
                card.transform.position = new Vector3(5000f, 5000f, 5000f);
                card.gameObject.SetActive(true);
                try { HarmonyLib.AccessTools.Field(typeof(Card3D), "_facingCamera").SetValue(card, cam); } catch (Exception) { }
                Coroutine set = null; Exception failed = null;
                try { set = card.SetCardAndRefreshVisuals(new ClientCard(def), null); } catch (Exception e) { failed = e; }
                if (set != null) yield return set;
                MeshRenderer back = card.CardUI == null ? null : card.CardUI.CardBackMeshRenderer;
                if (failed != null || back == null) { log.Add("back:" + key + "\t" + (failed != null ? failed.Message : "card has no back mesh")); UnityEngine.Object.Destroy(card.gameObject); continue; }
                try { card.SetCardBackData(data); } catch (Exception e) { log.Add("back:" + key + "\tset: " + e.Message); }
                float settle = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - settle < 2.5f) yield return null;
                try
                {
                    back.gameObject.layer = Layer;
                    Bounds b = back.bounds; Vector3 s = b.size;
                    Vector3 normal = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
                    if (Vector3.Dot(card.transform.TransformDirection(Vector3.up), normal) > 0) normal = -normal;   // the side away from the face
                    float w, h; Vector3 up;
                    if (normal == Vector3.up || normal == -Vector3.up) { w = s.x; h = s.z; up = Vector3.forward; if (w > h) { float t = w; w = h; h = t; up = Vector3.right; } }
                    else if (normal == Vector3.right || normal == -Vector3.right) { w = s.z; h = s.y; up = Vector3.up; }
                    else { w = s.x; h = s.y; up = Vector3.up; }
                    Shoot(cam, light, b.center, normal, up, w, h, Height, Path.Combine(folder, "back_" + FileSafe(key) + ".png"));
                    counts[0]++; log.Add("back:" + key + "\tok\t" + (OfflineMods.CardBackName(key) ?? key) + "\tmaterial " + (back.sharedMaterial == null ? "none" : back.sharedMaterial.name));
                }
                catch (Exception e) { log.Add("back:" + key + "\t" + e.Message); }
                UnityEngine.Object.Destroy(card.gameObject);
                yield return null;
            }
        }

        // Deck-list rows: the game's own small deck card (CardFactory.CloneDeckCardSmall, as in the deck builder), framed by the card's ListImage* fields.
        private static IEnumerator RenderTiles(string folder, List<int> ids, Camera cam, Light light, List<string> log, int[] counts)
        {
            if (Singleton<CardFactory>.Instance == null) { foreach (int id in ids) log.Add("tile:" + id + "\tno card factory"); yield break; }
            GameObject holder = new GameObject("TESL Forge tile canvas");
            holder.transform.position = new Vector3(-5000f, -5000f, 5000f);
            Canvas canvas = holder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam;
            foreach (int id in ids)
            {
                Card def = GameManager.Instance.GetCard(id);
                if (def == null) { log.Add("tile:" + id + "\tunknown card"); continue; }
                DeckCardSmall small = Singleton<CardFactory>.Instance.CloneDeckCardSmall(holder.transform);
                if (small == null) { log.Add("tile:" + id + "\tno small deck card"); continue; }
                GameObject row = small.gameObject;
                row.SetActive(true);
                Exception failed = null;
                try { small.SetData(def, 1, false, false, null); small.UpdateCardArt(); } catch (Exception e) { failed = e; }
                if (failed != null) { log.Add("tile:" + id + "\t" + failed.Message); UnityEngine.Object.Destroy(row); continue; }
                float settle = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - settle < 2f) yield return null;
                try
                {
                    SetLayer(holder.transform);
                    RectTransform rect = (RectTransform)row.transform;
                    float pw = UnityEngine.UI.LayoutUtility.GetPreferredWidth(rect), ph = UnityEngine.UI.LayoutUtility.GetPreferredHeight(rect);
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                    if (rect.rect.width < 2f || rect.rect.height < 2f) rect.sizeDelta = new Vector2(pw > 1f ? pw : 300f, ph > 1f ? ph : 48f);
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    Canvas.ForceUpdateCanvases();
                    Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    Vector3 center = (corners[0] + corners[2]) * 0.5f;
                    float w = (corners[3] - corners[0]).magnitude, h = (corners[1] - corners[0]).magnitude;
                    if (w <= 0f || h <= 0f) throw new InvalidOperationException("tile has no size");
                    Shoot(cam, light, center, -holder.transform.forward, holder.transform.up, w, h, Mathf.Clamp(Mathf.RoundToInt(1024f * h / w), 32, 1024),
                        Path.Combine(folder, "tile_" + id + ".png"));
                    counts[0]++; log.Add("tile:" + id + "\tok\t" + def.Name + "\tsize " + w.ToString("F1") + "x" + h.ToString("F1"));
                }
                catch (Exception e) { log.Add("tile:" + id + "\t" + e.Message); }
                UnityEngine.Object.Destroy(row);
                yield return null;
            }
            UnityEngine.Object.Destroy(holder);
        }

        // Deck banners: the game's own deck tile (UIDeckTile, as in deck pickers) on a world-space canvas.
        private static IEnumerator RenderDecks(string folder, List<string> names, Camera cam, Light light, List<string> log, int[] counts)
        {
            DeckTile template = Resources.Load<DeckTile>("UIDeckTile");
            if (template == null) { foreach (string name in names) log.Add("deck:" + name + "\tno deck tile prefab"); yield break; }
            GameObject holder = new GameObject("TESL Forge deck canvas");
            holder.transform.position = new Vector3(-5000f, 5000f, 5000f);
            Canvas canvas = holder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam;
            foreach (string name in names)
            {
                DeckInfo deck = GameManager.Instance.Config.DeckInfo == null ? null
                    : GameManager.Instance.Config.DeckInfo.Find(delegate(DeckInfo d) { return d.TypeName == name; });
                if (deck == null) { log.Add("deck:" + name + "\tunknown deck"); continue; }
                DeckTile tile = UnityEngine.Object.Instantiate(template);
                tile.transform.SetParent(holder.transform, false);
                tile.gameObject.SetActive(true);
                Exception failed = null;
                try { tile.SetData(deck); } catch (Exception e) { failed = e; }
                if (failed != null) { log.Add("deck:" + name + "\t" + failed.Message); UnityEngine.Object.Destroy(tile.gameObject); continue; }
                float settle = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - settle < 3f) yield return null;
                try
                {
                    SetLayer(holder.transform);
                    RectTransform rect = (RectTransform)tile.transform;
                    // In the game a list layout sizes the tile; here it gets its own preferred size (or the 4:1 art banner).
                    float pw = UnityEngine.UI.LayoutUtility.GetPreferredWidth(rect), ph = UnityEngine.UI.LayoutUtility.GetPreferredHeight(rect);
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(pw > 1f ? pw : 512f, ph > 1f ? ph : 128f);
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    Canvas.ForceUpdateCanvases();
                    Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    Vector3 center = (corners[0] + corners[2]) * 0.5f;
                    float w = (corners[3] - corners[0]).magnitude, h = (corners[1] - corners[0]).magnitude;
                    if (w <= 0f || h <= 0f) throw new InvalidOperationException("deck tile has no size");
                    Shoot(cam, light, center, -holder.transform.forward, holder.transform.up, w, h, Mathf.Clamp(Mathf.RoundToInt(1024f * h / w), 64, 1024),
                        Path.Combine(folder, "deck_" + FileSafe(name) + ".png"));
                    counts[0]++; log.Add("deck:" + name + "\tok\t" + tile.DeckNameText.text + "\tsize " + w.ToString("F1") + "x" + h.ToString("F1"));
                }
                catch (Exception e) { log.Add("deck:" + name + "\t" + e.Message); }
                UnityEngine.Object.Destroy(tile.gameObject);
                yield return null;
            }
            UnityEngine.Object.Destroy(holder);
        }

        // Boards: the base mat's own prefab (from the game's PlaymatSelector), skinned with the mod image, seen from above.
        private static IEnumerator RenderBoards(string folder, List<string> keys, Camera cam, Light light, List<string> log, int[] counts)
        {
            // The board selector lives in the match scene (CDGameBoard, [GameBoard]); before a match it is not loaded, so this
            // throwaway process loads that scene alongside the menu (nothing in it is shown or played; the process ends after).
            PlaymatSelector selector = FindSelector();
            float waited = Time.realtimeSinceStartup;
            if (selector == null)
            {
                AsyncOperation load = null;
                try { load = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("CDGameBoard", UnityEngine.SceneManagement.LoadSceneMode.Additive); }
                catch (Exception e) { log.Add("boards\tscene load: " + e.Message); }
                while (load != null && !load.isDone && Time.realtimeSinceStartup - waited < 120f) yield return null;
                for (int f = 0; f < 10 && selector == null; f++) { yield return null; selector = FindSelector(); }
            }
            // Lighting (ambient, baked lightmaps) comes from the active scene: make it the board's.
            if (selector != null) try { UnityEngine.SceneManagement.SceneManager.SetActiveScene(selector.gameObject.scene); } catch (Exception e) { log.Add("boards	active scene: " + e.Message); }
            log.Add("boards\tscene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " selector " + (selector != null) + " after " + (Time.realtimeSinceStartup - waited).ToString("0") + " s");
            List<PlaymatSelector.PlaymatReference> refs = selector == null ? null
                : (List<PlaymatSelector.PlaymatReference>)HarmonyLib.AccessTools.Field(typeof(PlaymatSelector), "playmatPrefabs").GetValue(selector);
            if (refs == null) { foreach (string key in keys) log.Add("board:" + key + "\tthe game's board list is not loaded before a match"); yield break; }
            foreach (string key in keys)
            {
                OfflineBoards.Board board = OfflineBoards.Find(key);
                PlaymatType plain;   // an original board by its type name (a control picture, no mod image)
                if (board == null && Enum.IsDefined(typeof(PlaymatType), key)) { plain = (PlaymatType)Enum.Parse(typeof(PlaymatType), key); board = new OfflineBoards.Board { Key = key, Name = key, Base = plain }; }
                PlaymatSelector.PlaymatReference r = board == null ? null : refs.Find(delegate(PlaymatSelector.PlaymatReference x) { return x.playmatType == board.Base; });
                if (board == null || r == null || r.prefab == null) { log.Add("board:" + key + "\t" + (board == null ? "unknown board" : "no prefab for " + board.Base)); continue; }
                r.prefab.Load();
                float wait = Time.realtimeSinceStartup;
                while (r.prefab.IsLoading() && Time.realtimeSinceStartup - wait < 60f) yield return null;
                // Placed where the game places a mat (the selector's playmatParent), lit by the board scene's lights and
                // drawn through its own cameras: the player's view of an empty board.
                Transform mount = (Transform)HarmonyLib.AccessTools.Field(typeof(PlaymatSelector), "playmatParent").GetValue(selector);
                foreach (Transform old in mount) old.gameObject.SetActive(false);
                GameObject parent = new GameObject("TESL Forge board");
                parent.transform.SetParent(mount, false);
                GameObject instance = null;
                try { instance = r.prefab.CreateNewInstance<GameObject>(parent.transform); } catch (Exception e) { log.Add("board:" + key + "\t" + e.Message); }
                if (instance == null) { UnityEngine.Object.Destroy(parent); continue; }
                for (int f = 0; f < 5; f++) yield return null;
                int skinned = 0;
                // the mod's 3D edits (moved, hidden, recoloured and added objects), as in a match
                if (OfflineBoards.Find(key) != null) try { OfflineBoardEdits.Apply(instance, board); } catch (Exception e) { log.Add("board:" + key + "	edits: " + e.Message); }
                if (OfflineBoards.Find(key) != null) try { skinned = OfflineBoards.Skin(instance, board); } catch (Exception e) { log.Add("board:" + key + "\tskin: " + e.Message); }
                float settle = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - settle < 2f) yield return null;
                try
                {
                    if (Environment.GetEnvironmentVariable("TESL_RENDER_DEBUG") == "1")
                    {
                        foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
                            log.Add("camera	" + c.name + " tag " + c.tag + " scene " + c.gameObject.scene.name + " enabled " + c.enabled + "/" + c.gameObject.activeInHierarchy
                                + " depth " + c.depth + " mask " + c.cullingMask + " clear " + c.clearFlags + " rt " + (c.targetTexture != null) + " pos " + c.transform.position);
                        foreach (Renderer x in instance.GetComponentsInChildren<Renderer>(true))
                            if (x.name.Contains("Plane")) log.Add("plane	" + x.name + " layer " + x.gameObject.layer + " enabled " + x.enabled + " pos " + x.transform.position + " bounds " + x.bounds.size);
                    }
                    string used = ShootScene(selector.gameObject.scene, 1600, 900, Path.Combine(folder, "board_" + FileSafe(key) + ".png"));
                    counts[0]++; log.Add("board:" + key + "\tok\t" + board.Name + "\tbase " + board.Base + " skinned " + skinned + " cameras " + used);
                }
                catch (Exception e) { log.Add("board:" + key + "\t" + e.Message); }
                UnityEngine.Object.Destroy(parent);
                yield return null;
            }
        }

        // Every enabled camera of a scene, in depth order, into one opaque picture (as the screen would show it).
        private static string ShootScene(UnityEngine.SceneManagement.Scene scene, int width, int height, string file)
        {
            List<Camera> cams = new List<Camera>();
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
                // the board camera only: the UI, targeting and drag cameras draw nothing without a match (the UI one clears to black)
                if (c.gameObject.scene == scene && c.enabled && c.gameObject.activeInHierarchy && c.targetTexture == null && c.name == "Camera_Main") cams.Add(c);
            if (cams.Count == 0) throw new InvalidOperationException("the board scene has no camera");
            cams.Sort(delegate(Camera a, Camera b) { return a.depth.CompareTo(b.depth); });
            // The mat's shader (Board/GameBoard) projects through CameraMatrixOverride's camera, found by tag at its Start;
            // with the menu scene also loaded that can be another scene's camera: use the board camera.
            // Cameras that draw into textures the board's shaders read (the rune wave input) are drawn first.
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
                if (c.gameObject.scene == scene && c.targetTexture != null) try { c.Render(); } catch (Exception) { }
            // CameraMatrixOverride flips its projection for texture targets only when anti-aliasing is on (as in the game's
            // windows); without it the mat is drawn upside down and culled. On for this picture, then restored.
            int aa = QualitySettings.antiAliasing;
            QualitySettings.antiAliasing = 2;
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 2);
            RenderTexture previous = RenderTexture.active;
            Texture2D shot = new Texture2D(width, height, TextureFormat.ARGB32, false);
            List<string> names = new List<string>();
            // only the board scene: the menu scene's own objects (its Elder Scroll, windows) are hidden for the picture
            List<GameObject> hidden = new List<GameObject>();
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                UnityEngine.SceneManagement.Scene other = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (other == scene || !other.isLoaded) continue;
                foreach (GameObject root in other.GetRootGameObjects())
                    if (root.activeSelf && root.GetComponentInChildren<Camera>(true) == null) { root.SetActive(false); hidden.Add(root); }
            }
            try
            {
                RenderTexture.active = rt; GL.Clear(true, true, Color.black);
                foreach (Camera c in cams)
                {
                    float aspect = c.aspect;
                    c.targetTexture = rt; c.aspect = (float)width / height;
                    try { c.Render(); } finally { c.targetTexture = null; c.aspect = aspect; c.ResetAspect(); }
                    names.Add(c.name + "@" + c.depth);
                    if (Environment.GetEnvironmentVariable("TESL_RENDER_DEBUG") == "1")
                    {
                        shot.ReadPixels(new Rect(0, 0, width, height), 0, 0); shot.Apply();
                        File.WriteAllBytes(file + "." + names.Count + "-" + c.name + ".png", shot.EncodeToPNG());
                    }
                }
                RenderTexture.active = rt;
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                Color32[] px = shot.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i].a = 255;
                shot.SetPixels32(px); shot.Apply();
                File.WriteAllBytes(file, shot.EncodeToPNG());
            }
            finally { foreach (GameObject g in hidden) if (g != null) g.SetActive(true); QualitySettings.antiAliasing = aa; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.Destroy(shot); }
            return string.Join(",", names.ToArray());
        }

        private static PlaymatSelector FindSelector()
        {
            foreach (PlaymatSelector p in Resources.FindObjectsOfTypeAll<PlaymatSelector>()) if (p.gameObject.scene.IsValid()) return p;
            return null;
        }

        // An orthographic picture of the area (w x h, centred, looked at from `normal`'s side) at the given pixel height.
        private static void Shoot(Camera cam, Light light, Vector3 center, Vector3 normal, Vector3 up, float w, float h, int height, string file, bool opaque = false)
        {
            cam.transform.position = center + normal * 100f;
            cam.transform.rotation = Quaternion.LookRotation(-normal, up);
            light.transform.rotation = cam.transform.rotation;
            cam.orthographicSize = h * 0.51f;
            int width = Mathf.Max(64, Mathf.RoundToInt(height * w / h));
            cam.aspect = (float)width / height;
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D shot = new Texture2D(width, height, TextureFormat.ARGB32, false);
            try
            {
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                if (opaque)
                {   // board shaders write no alpha: a board picture is a full opaque frame
                    Color32[] px = shot.GetPixels32();
                    for (int i = 0; i < px.Length; i++) px[i].a = 255;
                    shot.SetPixels32(px);
                }
                shot.Apply();
                File.WriteAllBytes(file, shot.EncodeToPNG());
            }
            finally { cam.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.Destroy(shot); }
        }

        public static string LastInfo = "";
        private static string Describe(Card3D card)
        {
            int all = 0, on = 0;
            System.Text.StringBuilder names = new System.Text.StringBuilder();
            foreach (Renderer r in card.GetComponentsInChildren<Renderer>(true))
            {
                all++;
                if (r.enabled && r.gameObject.activeInHierarchy) { on++; if (names.Length < 900) names.Append(r.GetType().Name).Append(':').Append(r.name).Append(' ').Append(r.bounds.size.ToString("F2")).Append(' '); }
            }
            return "renderers " + on + "/" + all + " active " + card.gameObject.activeInHierarchy + " scale " + card.transform.lossyScale.ToString("F2") + " " + LastInfo + " | " + names;
        }

        private static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            foreach (Transform c in t) SetLayer(c);
        }

        // The card's thinnest extent is its face normal; the camera looks at its face with the card's long side up.
        private static void Save(Card3D card, Camera cam, Light light, string file)
        {
            SetLayer(card.transform);
            Bounds b = new Bounds(card.transform.position, Vector3.zero); bool any = false;
            foreach (Renderer r in card.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                // empty text meshes report zero-size bounds at the world origin: leave them out
                // ... and screen-sized effect planes (a card's CardEffect layer can measure 3840 x 2160): far larger than a card
                if (r.bounds.size.sqrMagnitude < 1e-6f || (r.bounds.center - card.transform.position).magnitude > 50f || r.bounds.size.magnitude > 50f) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) throw new InvalidOperationException("nothing to draw");
            Vector3 s = b.size;
            Vector3 normal = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
            Vector3 face = card.transform.TransformDirection(Vector3.up);
            if (Vector3.Dot(face, normal) < 0) normal = -normal;   // look at the side the card's own "up" points to
            float w, h; Vector3 up;
            if (normal == Vector3.up || normal == -Vector3.up) { w = s.x; h = s.z; up = Vector3.forward; if (w > h) { float t = w; w = h; h = t; up = Vector3.right; } }
            else if (normal == Vector3.right || normal == -Vector3.right) { w = s.z; h = s.y; up = Vector3.up; }
            else { w = s.x; h = s.y; up = Vector3.up; }
            LastInfo = "bounds " + b.center.ToString("F1") + " " + b.size.ToString("F3") + " normal " + normal;
            Shoot(cam, light, b.center, normal, up, w, h, Height, file);
        }

        private static void Finish(string folder, string result, List<string> log)
        {
            try
            {
                File.WriteAllLines(Path.Combine(folder, "render-log.tsv"), log.ToArray());
                File.WriteAllText(Path.Combine(folder, "done.txt"), result);
            }
            catch (Exception) { }
            Debug.Log("OFFLINE CARD RENDER: " + result);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
