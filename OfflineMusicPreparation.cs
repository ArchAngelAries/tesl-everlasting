using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AssetBundles;
using DarkTonic.MasterAudio;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OfflineEngine
{
    // Presentation-only diagnostic preparation. Native configured music prefab
    // receipts, playlist registration and routing are reused without asset copies.
    public static class OfflineMusicPreparation
    {
        private sealed class Entry
        {
            public string Role;
            public AudioBgMusicData Data;
            public MusicAssetManager.Receipt Receipt;
            public MusicAssetManager.AudioAsset Asset;
            public MusicSetting Start;
            public AudioClip Clip;
            public string ClipName;
            public int Frequency;
            public int Channels;
            public int Samples;
            public MasterAudio.Playlist RepairedPlaylist;
            public AssetBundleLoadAssetOperation ClipOperation;
            public string ClipAssetPath;
        }
        private static readonly List<Entry> retained = new List<Entry>();
        private static bool preparing;
        private static bool prepared;
        private static MusicAssetManager retainedManager;
        private static bool introHookInstalled;
        private static string boardExportDirectory;
        private static string lifecyclePath;
        private static int retainedManagerId;
        private static int retainedMasterId;
        private static int retainedControllerId;
        public static bool IsPrepared { get { return prepared; } }

        // Before launch, arm preparation at the new board's native intro iterator.
        // Its audio hierarchy replaces the menu hierarchy during scene loading.
        public static IEnumerator Prepare(string exportDirectory)
        {
            RequireDiagnostic();
            Directory.CreateDirectory(exportDirectory);
            boardExportDirectory = exportDirectory;
            if (!introHookInstalled)
            {
                MethodInfo execute = AccessTools.Method(typeof(SequenceMatchIntro), "Execute", new Type[] { typeof(MatchLogAnimationData), typeof(BoardState) });
                if (execute == null) throw new MissingMethodException("Native SequenceMatchIntro.Execute is unavailable.");
                new Harmony("local.tesl.offline.music.intro").Patch(execute, null,
                    new HarmonyMethod(typeof(OfflineMusicPreparation), "WrapNativeIntro"), null, null, null);
                introHookInstalled = true;
            }
            UnityEngine.Debug.Log("OFFLINE MUSIC: board-scene preparation armed before original native intro execution.");
            yield break;
        }

        private static void WrapNativeIntro(ref IEnumerator __result)
        {
            if (!OfflineBootstrapPlugin.Active || !OfflineBoardAdapter.IsBoardMode) return;
            if (boardExportDirectory == null) throw new InvalidOperationException("Board-scene music preparation was not armed.");
            __result = ExecutePreparedNativeIntro(__result, boardExportDirectory);
        }

        private static IEnumerator ExecutePreparedNativeIntro(IEnumerator original, string directory)
        {
            try
            {
                if (SceneManager.GetActiveScene().name != "CDGameBoard")
                    throw new InvalidOperationException("Native diagnostic intro must prepare music in CDGameBoard.");
                float deadline = Time.realtimeSinceStartup + 60f;
                while (Singleton<AudioAssetManager>.Instance == null || Singleton<AudioAssetManager>.Instance.MusicManager == null
                    || MasterAudio.SafeInstance == null || !MasterAudio.SoundsReady || MasterAudio.OnlyPlaylistController == null
                    || !MasterAudio.OnlyPlaylistController.ControllerIsReady)
                { CheckDeadline(deadline, "board-scene native audio Awake/Start"); yield return null; }
                if (prepared && !System.Object.ReferenceEquals(retainedManager, Singleton<AudioAssetManager>.Instance.MusicManager)) Release();
                ClearVerifiedStaleMusicReceipt();
                yield return PrepareBoardMusic(directory);
                // Preserve the exact native iterator's values, timing and cleanup.
                while (original.MoveNext()) yield return original.Current;
                yield return VerifyNativeIntroRouting(directory);
            }
            finally { IDisposable disposable = original as IDisposable; if (disposable != null) disposable.Dispose(); }
        }

        private static void ClearVerifiedStaleMusicReceipt()
        {
            FieldInfo field = AccessTools.Field(typeof(AudioPlaybackManager), "_lastMusicReceipt");
            FieldInfo generation = AccessTools.Field(typeof(MusicAssetManager).BaseType, "_generation");
            if (field == null || generation == null) throw new MissingFieldException("Native music receipt generation fields are unavailable.");
            MusicAssetManager.Receipt last = (MusicAssetManager.Receipt)field.GetValue(null);
            int current = (int)generation.GetValue(null);
            if (last.Handle.Hash != 0 && last.Handle.Generation != current)
            {
                AudioPlaybackManager.StopAllCoroutines();
                field.SetValue(null, default(MusicAssetManager.Receipt));
                AccessTools.Field(typeof(AudioPlaybackManager), "_lastBgMusic").SetValue(null, null);
                UnityEngine.Debug.Log("OFFLINE MUSIC: cleared stale destroyed-scene receipt; old generation=" + last.Handle.Generation + "; current=" + current);
            }
        }

        private static IEnumerator VerifyNativeIntroRouting(string directory)
        {
            AudioBgMusicData intro = GameManager.Instance.Config.AudioPlaybackData.FindBgMusicByTrigger(AudioBgMusicTriggerType.MatchStartIntro);
            FieldInfo lastMusic = AccessTools.Field(typeof(AudioPlaybackManager), "_lastBgMusic");
            PlaylistController controller = MasterAudio.OnlyPlaylistController;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!System.Object.ReferenceEquals(lastMusic.GetValue(null), intro) || controller.CurrentPlaylistClip == null)
            { CheckDeadline(deadline, "original native intro music routing"); yield return null; }
            if (!controller.HasPlaylist || controller.PlaylistName != intro.Playlist || controller.CurrentPlaylistClip.name != intro.StartingClip)
                throw new InvalidOperationException("Original native intro routing did not select its configured playlist/clip.");
            File.WriteAllText(Path.Combine(directory, "audio-music-intro-routing.tsv"),
                "playlist\tclip\tstate\taudio_source_playing\tstatus\n" + Tsv(controller.PlaylistName) + "\t"
                + Tsv(controller.CurrentPlaylistClip.name) + "\t" + controller.PlaylistState + "\t"
                + (controller.CurrentPlaylistSource != null && controller.CurrentPlaylistSource.isPlaying)
                + "\toriginal_intro_trigger_verified\n");
            WriteLifecycle("original_native_intro_route_verified");
            UnityEngine.Debug.Log("OFFLINE MUSIC: original native intro route verified " + controller.PlaylistName + " / " + controller.CurrentPlaylistClip.name);
        }

        private static IEnumerator PrepareBoardMusic(string exportDirectory)
        {
            RequireDiagnostic();
            if (prepared) yield break;
            if (preparing) throw new InvalidOperationException("Native music preparation is already running.");
            if (!AssetCatalog.IsReady() || GameManager.Instance == null || GameManager.Instance.Config == null
                || GameManager.Instance.Config.AudioPlaybackData == null || LocalizationManager.Instance == null
                || LocalizationManager.Instance.IsLoading())
                throw new InvalidOperationException("Native music requires ready catalog/configuration and completed localization.");
            AudioAssetManager audio = Singleton<AudioAssetManager>.Instance;
            if (audio == null || audio.MusicManager == null || MasterAudio.Instance == null || !MasterAudio.SoundsReady)
                throw new InvalidOperationException("Native AudioAssetManager/MusicManager/MasterAudio must already be ready.");
            PlaylistController controller = MasterAudio.OnlyPlaylistController;
            if (controller == null || !controller.ControllerIsReady)
                throw new InvalidOperationException("The original single PlaylistController must complete Awake/Start before preparation.");
            AudioGameData config = GameManager.Instance.Config.AudioPlaybackData;
            List<Entry> entries = new List<Entry>();
            Add(entries, "MatchStartIntro", config.FindBgMusicByTrigger(AudioBgMusicTriggerType.MatchStartIntro));
            Add(entries, "MatchOngoing", config.FindBgMusicByTrigger(AudioBgMusicTriggerType.MatchOngoing));
            Add(entries, "MatchHalfHealth", config.FindBgMusicByTrigger(AudioBgMusicTriggerType.MatchHalfHealth));
            foreach (AudioBgMusicPlaymatData playmat in config.AudioBgMusicPlaymatList)
                if (playmat != null) Add(entries, "Playmat:" + playmat.PlaymatType, playmat.Data);
            Directory.CreateDirectory(exportDirectory);
            preparing = true;
            bool success = false;
            float deadline = Time.realtimeSinceStartup + 60f;
            try
            {
                retainedManager = audio.MusicManager;
                StartLifecycleEvidence(exportDirectory);
                foreach (Entry entry in entries)
                {
                    // AssetManager owns reference counts and instantiation. A
                    // later native intro request shares the existing same-path
                    // asset and never duplicates its DynamicSoundGroupCreator.
                    entry.Receipt = audio.MusicManager.LoadAudio(entry.Data.PrefabPath, null);
                    retained.Add(entry);
                    while (!audio.MusicManager.IsFinishedLoading(entry.Receipt))
                    { CheckDeadline(deadline, entry.Role); yield return null; }
                    entry.Asset = audio.MusicManager.FindLoadedAsset(entry.Receipt);
                    if (entry.Asset == null || entry.Asset.LoadOp == null || entry.Asset.GenericObject == null)
                        throw new InvalidOperationException("Native music prefab failed to instantiate: " + entry.Data.PrefabPath);
                    MasterAudio.Playlist playlist;
                    while ((playlist = MasterAudio.GrabPlaylist(entry.Data.Playlist, false)) == null)
                    { CheckDeadline(deadline, entry.Role + " playlist registration"); yield return null; }
                    // Awake/OnEnable can register a playlist before the prefab's
                    // Start and deferred clip loading have run. Preserve both
                    // original native naming and native audio-location semantics.
                    yield return null;
                    entry.Start = FindConfiguredClip(playlist, entry.Data.StartingClip);
                    WriteLoadingState(Path.Combine(exportDirectory, "audio-music-loading.tsv"), entries);
                    if (entry.Start == null)
                    {
                        // Installed playlists can predate configured soundtrack
                        // changes. Attach only the exact original configured clip
                        // after manifest, native map and bundle-index agreement.
                        entry.ClipAssetPath = FindOriginalClipPath(entry.Data.StartingClip);
                        ValidateOriginalClipMapping(entry.ClipAssetPath);
                        entry.ClipOperation = AssetCatalog.Instance.LoadAssetFromPathAsync(entry.ClipAssetPath, typeof(AudioClip), null);
                        if (entry.ClipOperation == null)
                            throw new InvalidOperationException("Native catalog could not start exact music clip load: " + entry.ClipAssetPath);
                        while (!entry.ClipOperation.IsDone())
                        { CheckDeadline(deadline, entry.Role + " exact original clip asset"); yield return null; }
                        entry.Clip = entry.ClipOperation.GetAsset<AudioClip>();
                        ValidateOriginalClipIndex(entry.ClipAssetPath);
                        if (entry.Clip == null || entry.Clip.name != entry.Data.StartingClip)
                            throw new InvalidOperationException("Exact original music asset name disagrees with configuration: " + entry.ClipAssetPath
                                + " / loaded=" + (entry.Clip == null ? "NULL" : entry.Clip.name));
                        // No alias and no substitution: add the installed clip
                        // itself to the existing native playlist, process-only.
                        entry.Start = new MusicSetting { audLocation = MasterAudio.AudioLocation.Clip, clip = entry.Clip,
                            songName = entry.Clip.name };
                        playlist.MusicSettings.Add(entry.Start);
                        entry.RepairedPlaylist = playlist;
                        WriteLoadingState(Path.Combine(exportDirectory, "audio-music-loading.tsv"), entries);
                    }
                    if (entry.Start.audLocation == MasterAudio.AudioLocation.ResourceFile)
                    {
                        if (string.IsNullOrEmpty(entry.Start.resourceFileName))
                            throw new InvalidOperationException("Native resource music entry has no resourceFileName: " + entry.Role);
                        // This is the exact sync path used by PlaylistController
                        // PlaySong; ResourceFile entries intentionally keep their
                        // MusicSetting.clip null. Do not rewrite that field.
                        entry.Clip = AudioResourceOptimizer.PopulateResourceSongToPlaylistController(controller.ControllerName,
                            entry.Start.resourceFileName, entry.Data.Playlist);
                    }
                    else if (entry.Start.audLocation == MasterAudio.AudioLocation.Clip)
                    {
                        while (entry.Start.clip == null)
                        { CheckDeadline(deadline, entry.Role + " native clip reference"); yield return null; }
                        entry.Clip = entry.Start.clip;
                    }
                    else throw new NotSupportedException("Unsupported native music audio location: " + entry.Role + " / " + entry.Start.audLocation);
                    if (entry.Clip == null)
                        throw new InvalidOperationException("Native music loader returned no exact configured clip: " + entry.Role + " / " + entry.Data.StartingClip);
                    if (entry.Clip.loadState == AudioDataLoadState.Unloaded && !entry.Clip.LoadAudioData())
                        throw new InvalidOperationException("Unity rejected loading original music audio data: " + entry.Role + " / " + entry.Clip.name);
                    while (entry.Clip.loadState == AudioDataLoadState.Loading || entry.Clip.loadState == AudioDataLoadState.Unloaded)
                    { CheckDeadline(deadline, entry.Role + " audio data"); yield return null; }
                    if (entry.Clip.loadState == AudioDataLoadState.Failed || entry.Clip.samples <= 0 || entry.Clip.channels <= 0)
                        throw new InvalidOperationException("Original music data did not load: " + entry.Role + " / " + entry.Clip.name
                            + "; state=" + entry.Clip.loadState + "; samples=" + entry.Clip.samples + "; channels=" + entry.Clip.channels);
                    // Native resource bookkeeping may later release preload
                    // references when playlists change; preserve proof of exact
                    // loaded metadata without modifying MusicSetting references.
                    entry.ClipName = entry.Clip.name; entry.Frequency = entry.Clip.frequency;
                    entry.Channels = entry.Clip.channels; entry.Samples = entry.Clip.samples;
                    WriteLoadingState(Path.Combine(exportDirectory, "audio-music-loading.tsv"), entries);
                }
                WriteRepairs(Path.Combine(exportDirectory, "audio-music-membership-repairs.tsv"), entries);
                Entry intro = entries[0];
                // Bind the real new scene's registered intro playlist without
                // starting playback. Original BeginMatchMusicSequence owns the
                // configured clip trigger and original playmat ending callback.
                if (!controller.HasPlaylist || controller.PlaylistName != intro.Data.Playlist)
                    MasterAudio.ChangePlaylistByName(intro.Data.Playlist, false);
                if (!controller.HasPlaylist || controller.PlaylistName != intro.Data.Playlist)
                    throw new InvalidOperationException("Native board intro playlist selection failed.");
                Directory.CreateDirectory(exportDirectory);
                WriteLifecycle("board_music_ready_before_original_intro");
                WriteEvidence(Path.Combine(exportDirectory, "audio-music-prepared.tsv"), entries, controller);
                prepared = true;
                success = true;
                UnityEngine.Debug.Log("OFFLINE MUSIC: verified " + entries.Count + " original board-scene music routes; selected "
                    + controller.PlaylistName + "; playback remains owned by original BeginMatchMusicSequence.");
            }
            finally
            {
                preparing = false;
                if (!success)
                {
                    try { WriteLoadingState(Path.Combine(exportDirectory, "audio-music-loading.tsv"), entries); }
                    catch (Exception ex) { UnityEngine.Debug.LogWarning("OFFLINE MUSIC: readiness report failed: " + ex.Message); }
                    Release();
                }
            }
        }

        private static string FindOriginalClipPath(string name)
        {
            string manifest = Path.Combine(Application.streamingAssetsPath, "appbase/audio.manifest");
            if (!File.Exists(manifest)) throw new InvalidOperationException("Installed original music manifest is absent.");
            List<string> matches = new List<string>();
            bool inAssets = false;
            string current = null;
            List<string> assets = new List<string>();
            foreach (string line in File.ReadAllLines(manifest))
            {
                if (line == "Assets:") { inAssets = true; continue; }
                if (!inAssets) continue;
                if (line.StartsWith("- ", StringComparison.Ordinal))
                { if (current != null) assets.Add(current); current = line.Substring(2); }
                else if (line.StartsWith("  ", StringComparison.Ordinal) && current != null) current += " " + line.Trim();
                else { if (current != null) assets.Add(current); current = null; break; }
            }
            if (current != null) assets.Add(current);
            foreach (string asset in assets)
                if (asset.StartsWith("Assets/AppBase/Audio/Music/Clips/", StringComparison.Ordinal)
                    && Path.GetFileNameWithoutExtension(asset) == name)
                    matches.Add(asset.Substring("Assets/".Length));
            if (matches.Count != 1)
                throw new InvalidOperationException("Exact configured music clip is not uniquely present in original manifest: " + name + "; matches=" + matches.Count);
            return matches[0];
        }

        private static void StartLifecycleEvidence(string directory)
        {
            retainedManagerId = retainedManager.GetInstanceID();
            retainedMasterId = MasterAudio.Instance.GetInstanceID();
            retainedControllerId = MasterAudio.OnlyPlaylistController.GetInstanceID();
            lifecyclePath = Path.Combine(directory, "audio-music-lifecycle.tsv");
            File.WriteAllText(lifecyclePath, "event\tactive_scene\tmanager_scene\tmanager_id\tcurrent_manager_id\tmaster_id\tcontroller_id\tgeneration\tprepared_instances_alive\tintro_setting_present\texpected_manager_id\texpected_master_id\texpected_controller_id\n");
            WriteLifecycle("new_board_audio_before_preparation");
        }

        private static void WriteLifecycle(string stage)
        {
            try
            {
                AudioAssetManager audio = Singleton<AudioAssetManager>.Instance;
                MusicAssetManager current = audio == null ? null : audio.MusicManager;
                MasterAudio master = MasterAudio.SafeInstance;
                PlaylistController controller = master == null ? null : MasterAudio.OnlyPlaylistController;
                int alive = 0;
                foreach (Entry entry in retained) if (entry.Asset != null && entry.Asset.GenericObject != null) alive++;
                MasterAudio.Playlist intro = master == null ? null : MasterAudio.GrabPlaylist("MatchStartMusic", false);
                bool introPresent = intro != null && FindConfiguredClip(intro, "TESL_MoE_Fanfare_final") != null;
                FieldInfo generation = AccessTools.Field(typeof(MusicAssetManager).BaseType, "_generation");
                string evidence = Tsv(stage) + "\t" + Tsv(SceneManager.GetActiveScene().name) + "\t"
                    + Tsv(retainedManager == null ? "destroyed" : retainedManager.gameObject.scene.name) + "\t"
                    + (retainedManager == null ? 0 : retainedManager.GetInstanceID()) + "\t" + (current == null ? 0 : current.GetInstanceID())
                    + "\t" + (master == null ? 0 : master.GetInstanceID()) + "\t" + (controller == null ? 0 : controller.GetInstanceID())
                    + "\t" + (generation == null ? "unknown" : generation.GetValue(null).ToString()) + "\t" + alive + "\t" + introPresent
                    + "\t" + retainedManagerId + "\t" + retainedMasterId + "\t" + retainedControllerId;
                if (lifecyclePath != null) File.AppendAllText(lifecyclePath, evidence + Environment.NewLine);
                UnityEngine.Debug.Log("OFFLINE MUSIC LIFECYCLE: " + evidence);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("OFFLINE MUSIC: lifecycle evidence failed: " + ex.Message); }
        }

        private static void ValidateOriginalClipMapping(string path)
        {
            Dictionary<int, int> files = AccessTools.Field(typeof(AssetCatalog), "_assetFileMap").GetValue(AssetCatalog.Instance) as Dictionary<int, int>;
            Dictionary<int, string> bundles = AccessTools.Field(typeof(AssetCatalog), "_bundleCatalog").GetValue(AssetCatalog.Instance) as Dictionary<int, string>;
            int bundleHash;
            string bundle;
            if (files == null || bundles == null || !files.TryGetValue(MurmurHash3.Hash(path.ToLowerInvariant()), out bundleHash)
                || !bundles.TryGetValue(bundleHash, out bundle) || bundle != "appbase/audio")
                throw new InvalidOperationException("Exact music clip path lacks its original appbase/audio catalog mapping: " + path);
        }

        private static void ValidateOriginalClipIndex(string path)
        {
            Dictionary<string, LoadedAssetBundle> loaded = AccessTools.Field(typeof(AssetBundleManager), "m_LoadedAssetBundles").GetValue(null)
                as Dictionary<string, LoadedAssetBundle>;
            LoadedAssetBundle bundle;
            if (loaded == null || !loaded.TryGetValue("appbase/audio", out bundle) || bundle.m_AssetBundle == null)
                throw new InvalidOperationException("Native original music bundle is not loaded for index verification.");
            string exact = ("assets/" + path).ToLowerInvariant();
            int matches = 0;
            foreach (string asset in bundle.m_AssetBundle.GetAllAssetNames())
                if (asset.ToLowerInvariant() == exact) matches++;
            if (matches != 1) throw new InvalidOperationException("Original music manifest/native bundle index disagree: " + path + "; matches=" + matches);
        }

        private static void WriteRepairs(string path, List<Entry> entries)
        {
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine("role\tplaylist\tconfigured_clip\toriginal_asset_path\trepair");
                foreach (Entry entry in entries)
                    writer.WriteLine(Tsv(entry.Role) + "\t" + Tsv(entry.Data.Playlist) + "\t" + Tsv(entry.Data.StartingClip) + "\t"
                        + Tsv(entry.ClipAssetPath) + "\t" + (entry.RepairedPlaylist == null ? "original_membership_preserved" : "exact_original_clip_added_process_only"));
            }
        }

        private static void Add(List<Entry> entries, string role, AudioBgMusicData data)
        {
            if (data == null || string.IsNullOrEmpty(data.PrefabPath) || string.IsNullOrEmpty(data.Playlist) || string.IsNullOrEmpty(data.StartingClip))
                throw new NotSupportedException("Native required music route has no configured prefab/playlist/starting clip: " + role);
            entries.Add(new Entry { Role = role, Data = data });
        }

        private static MusicSetting FindConfiguredClip(MasterAudio.Playlist playlist, string name)
        {
            if (playlist.MusicSettings == null) return null;
            MusicSetting found = playlist.MusicSettings.Find(delegate(MusicSetting s) { return s != null && s.alias == name; });
            if (found != null) return found;
            return playlist.MusicSettings.Find(delegate(MusicSetting s) {
                return s != null && (s.audLocation == MasterAudio.AudioLocation.Clip
                    ? s.clip != null && s.clip.name == name : s.resourceFileName == name || s.songName == name); });
        }

        private static void WriteEvidence(string path, List<Entry> entries, PlaylistController controller)
        {
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine("role\tprefab_path\tconfigured_playlist\tconfigured_start\tloaded_clip\tfrequency\tchannels\tsamples\tstatus");
                foreach (Entry entry in entries)
                    writer.WriteLine(Tsv(entry.Role) + "\t" + Tsv(entry.Data.PrefabPath) + "\t" + Tsv(entry.Data.Playlist) + "\t"
                        + Tsv(entry.Data.StartingClip) + "\t" + Tsv(entry.ClipName) + "\t" + entry.Frequency + "\t"
                        + entry.Channels + "\t" + entry.Samples + "\toriginal_prefab_playlist_clip_ready");
                writer.WriteLine("native_board_intro_playlist\t\t" + Tsv(controller.PlaylistName) + "\t\t" + Tsv(controller.CurrentPlaylistClip == null ? null : controller.CurrentPlaylistClip.name)
                    + "\t\t\t\t" + controller.PlaylistState);
                writer.WriteLine("native_audio_source\t\t\t\t\t\t\t\t" + (controller.CurrentPlaylistSource != null && controller.CurrentPlaylistSource.isPlaying
                    ? "playing" : "not_playing_on_current_device"));
                writer.WriteLine("native_music_scene_lifecycle\t\t\t\t\t\t\t\tnew_board_scene_prepared_before_original_intro;native_scene_dump_active");
            }
        }

        private static void WriteLoadingState(string path, List<Entry> entries)
        {
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine("role\tconfigured_playlist\tconfigured_start\tsetting_index\taudio_location\talias\tsong_name\tresource_file\tclip\tload_state\tsamples\tchannels\tselected\tresolved_clip");
                foreach (Entry entry in entries)
                {
                    MasterAudio.Playlist playlist = MasterAudio.GrabPlaylist(entry.Data.Playlist, false);
                    if (playlist == null || playlist.MusicSettings == null)
                    { writer.WriteLine(Tsv(entry.Role) + "\t" + Tsv(entry.Data.Playlist) + "\t" + Tsv(entry.Data.StartingClip) + "\t\t\t\t\t\t\tplaylist_not_registered"); continue; }
                    for (int i = 0; i < playlist.MusicSettings.Count; i++)
                    {
                        MusicSetting setting = playlist.MusicSettings[i];
                        if (setting == null) continue;
                        AudioClip clip = setting.clip;
                        writer.WriteLine(Tsv(entry.Role) + "\t" + Tsv(entry.Data.Playlist) + "\t" + Tsv(entry.Data.StartingClip) + "\t" + i + "\t"
                            + setting.audLocation + "\t" + Tsv(setting.alias) + "\t" + Tsv(setting.songName) + "\t" + Tsv(setting.resourceFileName)
                            + "\t" + Tsv(clip == null ? null : clip.name) + "\t" + (clip == null ? "no_clip_reference" : clip.loadState.ToString())
                            + "\t" + (clip == null ? 0 : clip.samples) + "\t" + (clip == null ? 0 : clip.channels)
                            + "\t" + System.Object.ReferenceEquals(setting, entry.Start) + "\t" + Tsv(entry.ClipName));
                    }
                }
            }
        }

        // Call only after the diagnostic has stopped native music/match routing.
        // These are our extra preload references; native routing owns its receipt.
        public static void Release()
        {
            try
            {
                WriteLifecycle("release_before_owned_references");
                foreach (Entry entry in retained)
                {
                    if (entry.RepairedPlaylist != null && entry.Start != null) entry.RepairedPlaylist.MusicSettings.Remove(entry.Start);
                    AssetBundleLoadAssetOperation.Unload(ref entry.ClipOperation);
                    if (retainedManager != null) retainedManager.UnloadAsset(ref entry.Receipt);
                }
            }
            finally
            {
                retainedManager = null;
                lifecyclePath = null;
                retained.Clear(); prepared = false;
            }
        }
        private static void RequireDiagnostic()
        {
            if (!OfflineBootstrapPlugin.Active || !OfflineBoardAdapter.IsBoardMode)
                throw new InvalidOperationException("Native music preparation requires guarded offline board diagnostics.");
        }
        private static void CheckDeadline(float deadline, string role)
        { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Native music readiness timed out: " + role); }
        private static string Tsv(string value) { return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
