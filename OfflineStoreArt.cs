using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AssetBundles;
using HarmonyLib;
using UnityEngine;

namespace OfflineEngine
{
    // Store images shipped inside the client: StoreImageUtils falls back to the AssetCatalog path
    // "AppBase/UI/Textures/Store/img_<MurmurHash3(imageUrl)>_2" when the CDN fails. The names are hashes of the
    // original CDN URLs, so this diagnostic lists the bundled store textures and saves each as a PNG for review.
    public static class OfflineStoreArt
    {
        // Official single-pack store images shipped in appbase/uistoreimages_0 (identified from the dump).
        public static readonly Dictionary<string, string> BundledPackArt = new Dictionary<string, string> {
            { "core", "AppBase/UI/Textures/Store/img_464553517_2" },
            { "morrowind", "AppBase/UI/Textures/Store/img_-1377594561_2" },
            { "skyrim", "AppBase/UI/Textures/Store/img_782026716_2" },
        };

        // Store images: an AssetCatalog path (official bundled art, as the Reborn service also serves it) or a local
        // file URL (UESP pack renders). Runs last among prefixes, so it decides even with the TESL Reborn plugin loaded.
        public static void Install(Harmony harmony)
        {
            HarmonyMethod prefix = new HarmonyMethod(typeof(OfflineStoreArt).GetMethod("LoadStoreImagePrefix"));
            prefix.priority = Priority.Last;
            harmony.Patch(AccessTools.Method(typeof(StoreImageUtils), "LoadStoreImageCoroutine"), prefix, null, null, null, null);
        }

        public static bool LoadStoreImagePrefix(string imageUrl, Action<Sprite> callback, ref IEnumerator __result)
        {
            if (string.IsNullOrEmpty(imageUrl)) return true;
            __result = LoadImage(imageUrl, callback);
            return false;
        }

        private static IEnumerator LoadImage(string image, Action<Sprite> callback)
        {
            if (image.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string path = new Uri(image).LocalPath;
                if (!File.Exists(path)) { Debug.LogWarning("OFFLINE STORE ART: missing " + path); yield break; }
                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!texture.LoadImage(File.ReadAllBytes(path))) yield break;
                callback(Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f)));
                yield break;
            }
            // a path with its own extension (.png/.tga): the asset catalog hashes the full name, so load that exact file
            string ext = null;
            if (image.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || image.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            { ext = image.Substring(image.Length - 3).ToLowerInvariant(); image = image.Substring(0, image.Length - 4); }
            AssetBundleLoadAssetOperation operation = AssetCatalog.Instance.LoadAssetFromPathAsync(image, typeof(Sprite), ext ?? "png");
            if (operation != null)
            {
                while (!operation.IsDone()) yield return operation;
                Sprite sprite = operation.GetAsset<Sprite>();
                if (sprite != null) { callback(sprite); yield break; }
            }
            // A bare texture (card back diffuse maps are .tga): wrap it in a sprite.
            operation = AssetCatalog.Instance.LoadAssetFromPathAsync(image, typeof(Texture2D), ext ?? "tga");
            if (operation == null) { Debug.LogWarning("OFFLINE STORE ART: no image at " + image); yield break; }
            while (!operation.IsDone()) yield return operation;
            Texture2D tex = operation.GetAsset<Texture2D>();
            if (tex != null) callback(Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f)));
            else Debug.LogWarning("OFFLINE STORE ART: texture did not load: " + image);
        }
        // Diagnostic: every official card back's store image loaded exactly as the store loads it, saved as PNG with a
        // report (card back, image path, loaded size or why not).
        public static IEnumerator DumpCardBackStoreArt(SPTESL.GameConfiguration config, string directory)
        {
            Directory.CreateDirectory(directory);
            StringBuilder report = new StringBuilder("card_back\timage\tresult\n");
            int ok = 0, bad = 0;
            foreach (string key in OfflineCosmetics.CardBacks)
            {
                if (key == OfflineCosmetics.DefaultCardBack) continue;
                string image = OfflineGoldStore.CardBackArt(config, key);
                Sprite got = null;
                if (image != null) yield return LoadImage(image, delegate(Sprite s) { got = s; });
                string result;
                if (got == null || got.texture == null) { result = image == null ? "no image path" : "did not load"; bad++; }
                else
                {
                    Texture2D png = Crop(Readable(got.texture), UvBounds(got));
                    File.WriteAllBytes(Path.Combine(directory, key + ".png"), png.EncodeToPNG());
                    result = "ok " + png.width + "x" + png.height; ok++;
                }
                report.Append(key).Append('\t').Append(image ?? "").Append('\t').Append(result).Append('\n');
            }
            File.WriteAllText(Path.Combine(directory, "cardback-store-art.tsv"), report.ToString());
            Debug.Log("OFFLINE STORE ART: card back store images " + ok + " loaded, " + bad + " missing (" + directory + ")");
        }

        public static IEnumerator Dump(string directory)
        {
            Directory.CreateDirectory(directory);
            StringBuilder report = new StringBuilder("bundle\tasset\twidth\theight\tfile\n");
            List<string> bundles = new List<string>();
            foreach (string name in AssetBundleManager.GetBundleList())
                if (name != null && name.ToLowerInvariant().Contains("store")) bundles.Add(name);
            Debug.Log("OFFLINE STORE ART: candidate bundles " + string.Join(", ", bundles.ToArray()));
            foreach (string bundle in bundles)
            {
                AssetBundleLoadAssetOperation op = AssetBundleManager.LoadAssetAsync(bundle, "offline-store-art-probe", typeof(Sprite), -1);
                if (op != null) { float start = Time.realtimeSinceStartup; while (!op.IsDone() && Time.realtimeSinceStartup - start < 30f) yield return null; }
                string error;
                LoadedAssetBundle loaded = AssetBundleManager.GetLoadedAssetBundle(bundle, out error);
                if (loaded == null || loaded.m_AssetBundle == null) { report.Append(bundle).Append("\t(not loaded: ").Append(error).Append(")\n"); continue; }
                foreach (string asset in loaded.m_AssetBundle.GetAllAssetNames())
                {
                    if (!asset.EndsWith(".png")) continue;
                    Sprite sprite = loaded.m_AssetBundle.LoadAsset<Sprite>(asset);
                    if (sprite == null || sprite.texture == null) { report.Append(bundle).Append('\t').Append(asset).Append("\t-\t-\t(no sprite)\n"); continue; }
                    Texture2D image = Crop(Readable(sprite.texture), UvBounds(sprite));
                    string file = Path.GetFileNameWithoutExtension(asset) + ".png";
                    File.WriteAllBytes(Path.Combine(directory, file), image.EncodeToPNG());
                    report.Append(bundle).Append('\t').Append(asset).Append('\t').Append(image.width).Append('\t').Append(image.height).Append('\t').Append(file).Append('\n');
                    yield return null;
                }
            }
            File.WriteAllText(Path.Combine(directory, "store-art.tsv"), report.ToString());
            Debug.Log("OFFLINE STORE ART: dumped to " + directory);
        }

        // Tight-packed atlas sprites have no textureRect: use the bounding box of their UVs.
        public static Rect UvBounds(Sprite sprite)
        {
            Vector2 min = new Vector2(1f, 1f), max = new Vector2(0f, 0f);
            foreach (Vector2 uv in sprite.uv) { min = Vector2.Min(min, uv); max = Vector2.Max(max, uv); }
            float w = sprite.texture.width, h = sprite.texture.height;
            return new Rect(min.x * w, min.y * h, (max.x - min.x) * w, (max.y - min.y) * h);
        }

        public static Texture2D Crop(Texture2D atlas, Rect rect)
        {
            int x = Mathf.RoundToInt(rect.x), y = Mathf.RoundToInt(rect.y), w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            if (x == 0 && y == 0 && w == atlas.width && h == atlas.height) return atlas;
            Texture2D image = new Texture2D(w, h, TextureFormat.ARGB32, false);
            image.SetPixels(atlas.GetPixels(x, y, w, h));
            image.Apply();
            return image;
        }

        // Bundle textures are not CPU-readable: copy through a RenderTexture.
        public static Texture2D Readable(Texture2D source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
