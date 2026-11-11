using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AssetBundles;
using DarkTonic.MasterAudio;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Diagnostic only. Native bundle names/dependencies remain authoritative;
    // this repairs absent file-catalog entries without replacing audio assets.
    public static class OfflineAudioAssets
    {
        private sealed class Mapping
        {
            public string Bundle;
            public string Asset;
        }

        private static void RequireDiagnostic()
        {
            if (!OfflineBootstrapPlugin.Active || !OfflineBoardAdapter.IsBoardMode)
                throw new InvalidOperationException("Audio recovery requires guarded offline board diagnostics.");
            if (!AssetCatalog.IsReady()) throw new InvalidOperationException("Native asset catalog must be initialized.");
        }

        // Run after AssetCatalog.Initialize, before OfflineBoardAdapter.Prepare.
        // GetAllAssetNames reads indices only: no sound assets are instantiated.
        public static IEnumerator InspectAndInstall(string exportDirectory, bool installMissingMappings)
        {
            RequireDiagnostic();
            string root = Path.GetFullPath(Application.streamingAssetsPath);
            Directory.CreateDirectory(exportDirectory);
            List<string> manifests = new List<string>();
            string[] folders = { "contentpack000/en_us", "contentpack000/sfx" };
            foreach (string folder in folders)
            {
                string path = Path.Combine(root, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string manifest in Directory.GetFiles(path, "*.manifest")) manifests.Add(manifest);
            }
            manifests.Add(Path.Combine(root, "appbase/audioprefabs_0.manifest"));
            manifests.Add(Path.Combine(root, "appbase/audioprefabs_1.manifest"));
            manifests.Sort(StringComparer.Ordinal);
            HashSet<string> nativeBundles = new HashSet<string>(AssetBundleManager.GetBundleList(), StringComparer.Ordinal);
            FieldInfo loadedField = AccessTools.Field(typeof(AssetBundleManager), "m_LoadedAssetBundles");
            Dictionary<string, LoadedAssetBundle> loaded = loadedField == null ? null :
                loadedField.GetValue(null) as Dictionary<string, LoadedAssetBundle>;
            if (loaded == null) throw new MissingFieldException("Native loaded bundle dictionary unavailable.");
            List<Mapping> candidates = new List<Mapping>();
            int examined = 0;
            using (StreamWriter writer = new StreamWriter(Path.Combine(exportDirectory, "audio-local-assets.tsv")))
            {
                writer.WriteLine("bundle\tnative_asset_name\tcatalog_asset_path\tmanifest_agreement\tstatus");
                foreach (string manifest in manifests)
                {
                    if (!File.Exists(manifest)) continue;
                    List<string> expected = ReadAssets(manifest);
                    if (!expected.Exists(delegate(string value) { return value.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase); })) continue;
                    string file = manifest.Substring(0, manifest.Length - ".manifest".Length);
                    string bundleName = file.Substring(root.Length + 1).Replace('\\', '/').ToLowerInvariant();
                    if (!nativeBundles.Contains(bundleName) || !File.Exists(file))
                        throw new InvalidOperationException("Audio prefab bundle is absent from native manifest/local disk: " + bundleName);
                    HashSet<string> expectedPaths = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string asset in expected) expectedPaths.Add(asset.ToLowerInvariant());
                    AssetBundle bundle = null;
                    bool owned = false;
                    LoadedAssetBundle borrowed;
                    if (loaded.TryGetValue(bundleName, out borrowed)) bundle = borrowed.m_AssetBundle;
                    else { bundle = AssetBundle.LoadFromFile(file); owned = true; }
                    if (bundle == null) throw new InvalidOperationException("Native audio index could not open " + bundleName);
                    try
                    {
                        string[] names = bundle.GetAllAssetNames();
                        Array.Sort(names, StringComparer.Ordinal);
                        foreach (string fullName in names)
                        {
                            string full = fullName.ToLowerInvariant();
                            if (!full.StartsWith("assets/", StringComparison.Ordinal) ||
                                !full.EndsWith(".prefab", StringComparison.Ordinal)) continue;
                            string asset = full.Substring("assets/".Length);
                            bool agreement = expectedPaths.Contains(full);
                            writer.WriteLine(Tsv(bundleName) + "\t" + Tsv(fullName) + "\t" + Tsv(asset) + "\t" + agreement + "\tindex_verified");
                            if (!agreement) throw new InvalidOperationException("Audio bundle index disagrees with its original text manifest: " + fullName);
                            candidates.Add(new Mapping { Bundle = bundleName, Asset = asset });
                            expectedPaths.Remove(full);
                        }
                        foreach (string remaining in expectedPaths)
                            if (remaining.EndsWith(".prefab", StringComparison.Ordinal))
                                throw new InvalidOperationException("Original audio manifest prefab missing from native bundle index: " + remaining);
                    }
                    finally { if (owned && bundle != null) bundle.Unload(false); }
                    examined++;
                    yield return null;
                }
            }
            // Validate the complete patch before touching either native dictionary.
            Dictionary<int, int> files = GetCatalogDictionary<int>("_assetFileMap");
            Dictionary<int, string> bundles = GetCatalogDictionary<string>("_bundleCatalog");
            Dictionary<int, Mapping> byHash = new Dictionary<int, Mapping>();
            Dictionary<int, string> bundleHashes = new Dictionary<int, string>();
            int missing = 0;
            foreach (Mapping entry in candidates)
            {
                int hash = MurmurHash3.Hash(entry.Asset), bundleHash = MurmurHash3.Hash(entry.Bundle);
                Mapping previous;
                if (byHash.TryGetValue(hash, out previous) &&
                    (previous.Asset != entry.Asset || previous.Bundle != entry.Bundle))
                    throw new InvalidOperationException("Ambiguous original audio asset hash: " + entry.Asset);
                byHash[hash] = entry;
                string name;
                if ((bundles.TryGetValue(bundleHash, out name) || bundleHashes.TryGetValue(bundleHash, out name)) && name != entry.Bundle)
                    throw new InvalidOperationException("Original audio bundle hash collision: " + entry.Bundle);
                bundleHashes[bundleHash] = entry.Bundle;
                int existing;
                if (files.TryGetValue(hash, out existing))
                {
                    if (existing != bundleHash) throw new InvalidOperationException("Existing audio mapping differs; preserved original: " + entry.Asset);
                }
                else missing++;
            }
            using (StreamWriter writer = new StreamWriter(Path.Combine(exportDirectory, "audio-local-mappings.tsv")))
            {
                writer.WriteLine("catalog_asset_path\tbundle\tasset_hash\tbundle_hash\tstatus");
                foreach (Mapping entry in candidates)
                {
                    int hash = MurmurHash3.Hash(entry.Asset), bundleHash = MurmurHash3.Hash(entry.Bundle);
                    bool absent = !files.ContainsKey(hash);
                    writer.WriteLine(Tsv(entry.Asset) + "\t" + Tsv(entry.Bundle) + "\t" + hash + "\t" + bundleHash + "\t" +
                        (absent ? (installMissingMappings ? "verified_install_candidate" : "verified_missing") : "original_present"));
                }
            }
            // Finish report I/O first, so an unwritable report never leaves a
            // partly applied mapping patch. This commit runs without yielding.
            if (installMissingMappings)
                foreach (Mapping entry in candidates)
                {
                    int hash = MurmurHash3.Hash(entry.Asset), bundleHash = MurmurHash3.Hash(entry.Bundle);
                    if (!bundles.ContainsKey(bundleHash)) bundles.Add(bundleHash, entry.Bundle);
                    if (!files.ContainsKey(hash)) files.Add(hash, bundleHash);
                }
            WriteMusicState(Path.Combine(exportDirectory, "audio-music-before-board.tsv"));
            UnityEngine.Debug.Log("OFFLINE AUDIO: verified " + candidates.Count + " original prefab paths in " + examined +
                " local bundle indices; missing=" + missing + "; installed=" + installMissingMappings + ". Scope: cp000 English/SFX and original music. No absent audio aliased.");
        }

        private static Dictionary<int, T> GetCatalogDictionary<T>(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(AssetCatalog), name);
            Dictionary<int, T> result = field == null ? null : field.GetValue(AssetCatalog.Instance) as Dictionary<int, T>;
            if (result == null) throw new MissingFieldException(typeof(AssetCatalog).FullName, name);
            return result;
        }

        // Unity's generated text manifests fold long YAML scalar asset names onto
        // indented continuation lines. Retain the single separating space exactly.
        private static List<string> ReadAssets(string manifest)
        {
            List<string> result = new List<string>();
            bool inAssets = false;
            string current = null;
            foreach (string line in File.ReadAllLines(manifest))
            {
                if (line == "Assets:") { inAssets = true; continue; }
                if (!inAssets) continue;
                if (line == "Dependencies:" || (!line.StartsWith("- ", StringComparison.Ordinal) && !line.StartsWith("  ", StringComparison.Ordinal)))
                { if (current != null) result.Add(current); current = null; break; }
                if (line.StartsWith("- ", StringComparison.Ordinal))
                { if (current != null) result.Add(current); current = line.Substring(2); }
                else if (current != null) current += " " + line.Trim();
            }
            if (current != null) result.Add(current);
            return result;
        }

        public static void WriteMusicState(string path)
        {
            RequireDiagnostic();
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine("kind\tidentifier\tprefab_path\tplaylist\tstarting_clip\tambient\tstate");
                if (GameManager.Instance != null && GameManager.Instance.Config != null && GameManager.Instance.Config.AudioPlaybackData != null)
                {
                    AudioGameData data = GameManager.Instance.Config.AudioPlaybackData;
                    foreach (AudioBgMusicTriggerData entry in data.AudioBgMusicTriggerList)
                        if (entry != null) WriteMusic(writer, "trigger", entry.Trigger.ToString(), entry.Data);
                    foreach (AudioBgMusicPlaymatData entry in data.AudioBgMusicPlaymatList)
                        if (entry != null) WriteMusic(writer, "playmat", entry.PlaymatType.ToString(), entry.Data);
                }
                PlaylistController controller = MasterAudio.OnlyPlaylistController;
                writer.WriteLine("runtime\tPlaylistController\t\t" + Tsv(controller == null || !controller.HasPlaylist ? null : controller.PlaylistName) + "\t" +
                    Tsv(controller == null || controller.CurrentPlaylistClip == null ? null : controller.CurrentPlaylistClip.name) + "\t\t" +
                    (controller == null ? "absent" : controller.PlaylistState.ToString()));
                MasterAudio master = MasterAudio.Instance;
                FieldInfo playlistsField = AccessTools.Field(typeof(MasterAudio), "musicPlaylists");
                IList playlists = master == null || playlistsField == null ? null : playlistsField.GetValue(master) as IList;
                if (playlists != null)
                    foreach (object playlist in playlists)
                    {
                        if (playlist == null) continue;
                        FieldInfo nameField = AccessTools.Field(playlist.GetType(), "playlistName");
                        writer.WriteLine("runtime_playlist\t" + Tsv(nameField == null ? playlist.GetType().FullName : Convert.ToString(nameField.GetValue(playlist))) + "\t\t\t\t\tpresent");
                    }
            }
        }

        private static void WriteMusic(StreamWriter writer, string kind, string id, AudioBgMusicData data)
        {
            if (data == null) { writer.WriteLine(kind + "\t" + Tsv(id) + "\t\t\t\t\tabsent_data"); return; }
            string asset = (data.PrefabPath + ".prefab").ToLowerInvariant();
            Dictionary<int, int> files = GetCatalogDictionary<int>("_assetFileMap");
            writer.WriteLine(kind + "\t" + Tsv(id) + "\t" + Tsv(data.PrefabPath) + "\t" + Tsv(data.Playlist) + "\t" + Tsv(data.StartingClip) +
                "\t" + Tsv(data.Ambient) + "\t" + (files.ContainsKey(MurmurHash3.Hash(asset)) ? "native_mapping_present" : "native_mapping_absent"));
        }

        private static string Tsv(string value) { return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
