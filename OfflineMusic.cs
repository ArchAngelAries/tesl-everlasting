using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DarkTonic.MasterAudio;
using FullSerializer;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mod soundtracks. The client asks for background music by moment (AudioBgMusicTriggerType: MainMenuState, StoreState,
    // MatchStartIntro, MatchOngoing, MatchHalfHealth, EndGameWin, ...) or by board (FindBgMusicByPlaymat), and plays the
    // entry through AudioPlaybackManager.TriggerBgMusicChange(AudioBgMusicData, callback) on a MasterAudio playlist.
    // A mod track names the contexts it plays in ("MainMenuState", "Board:Elsweyr", "Board:<mod>/<board>"):
    //   mode "replace": the mod's track(s) instead of the game's; "rotation": the game's music is one of the choices.
    // The game's playlist is faded out and the track plays on an AudioSource that follows the game's music volume and mute.
    // A context asked with an end callback (the match intro, then board music) plays once and then runs the callback.
    public static class OfflineMusic
    {
        public sealed class Track { public string Key, Name, File; public List<string> Contexts = new List<string>(); public bool Rotation; public float Volume = 1f; }
        public static readonly List<Track> Tracks = new List<Track>();
        public static string Playing, LastContext;
        public static int Started;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<AudioBgMusicData, string> contexts = new Dictionary<AudioBgMusicData, string>();
        private static MonoBehaviour host;
        private static AudioSource source;
        private static Track current;
        private static Coroutine routine;
        private static readonly System.Random rng = new System.Random();

        public static void Load(string modId, string dir, List<string> problems)
        {
            string manifest = Path.Combine(dir, "music.json");
            if (!System.IO.File.Exists(manifest)) return;
            foreach (fsData e in fsJsonParser.Parse(System.IO.File.ReadAllText(manifest)).AsDictionary["tracks"].AsList)
            {
                Dictionary<string, fsData> t = e.AsDictionary;
                string slug = t["slug"].AsString, file = Path.Combine(dir, t["file"].AsString);
                if (!System.IO.File.Exists(file)) { problems.Add(modId + ": music " + slug + " file missing"); continue; }
                Track track = new Track { Key = modId + "/" + slug, Name = t.ContainsKey("name") ? t["name"].AsString : slug, File = file,
                    Rotation = t.ContainsKey("mode") && t["mode"].AsString == "rotation", Volume = t.ContainsKey("volume") ? (float)t["volume"].AsDouble : 1f };
                if (t.ContainsKey("contexts")) foreach (fsData c in t["contexts"].AsList) track.Contexts.Add(c.AsString);
                Tracks.Add(track);
            }
        }

        public static void Install(Harmony harmony, MonoBehaviour coroutineHost)
        {
            host = coroutineHost;
            harmony.Patch(AccessTools.Method(typeof(AudioGameData), "FindBgMusicByPlaymat"), null, new HarmonyMethod(typeof(OfflineMusic).GetMethod("PlaymatPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(AudioGameData), "FindBgMusicByTrigger"), null, new HarmonyMethod(typeof(OfflineMusic).GetMethod("TriggerPostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(AudioPlaybackManager), "TriggerBgMusicChange", new Type[] { typeof(AudioBgMusicData), typeof(Action) }),
                new HarmonyMethod(typeof(OfflineMusic).GetMethod("ChangePrefix")), null, null, null, null);
        }

        public static void PlaymatPostfix(PlaymatType playmatType, AudioBgMusicData __result)
        {
            if (__result == null) return;
            OfflineBoards.Board board = OfflineBoards.Current;
            contexts[__result] = board != null && board.Base == playmatType ? "Board:" + board.Key + "|Board:" + playmatType : "Board:" + playmatType;
        }

        public static void TriggerPostfix(AudioBgMusicTriggerType triggerType, AudioBgMusicData __result)
        {
            if (__result != null && !contexts.ContainsKey(__result)) contexts[__result] = triggerType.ToString();
        }

        // The tracks for a context, most specific first ("Board:mod/board" before "Board:Elsweyr").
        public static List<Track> For(string context)
        {
            foreach (string c in context.Split('|'))
            {
                List<Track> found = Tracks.FindAll(delegate(Track t) { return t.Contexts.Contains(c); });
                if (found.Count > 0) return found;
            }
            return new List<Track>();
        }

        public static bool ChangePrefix(AudioBgMusicData musicData, Action playlistEndCallback)
        {
            if (Tracks.Count == 0 || musicData == null || host == null) return true;
            string context;
            if (!contexts.TryGetValue(musicData, out context)) context = "";
            if (OfflineModes.Active != null && (context.StartsWith("Match") || context.StartsWith("Board:"))) context = "Mode:" + OfflineModes.Active.Key + "|" + context;
            LastContext = context;
            List<Track> choices = For(context);
            if (choices.Count == 0) { StopMod(); return true; }
            bool rotation = choices.TrueForAll(delegate(Track t) { return t.Rotation; });
            int pick = rng.Next(choices.Count + (rotation ? 1 : 0));
            if (pick >= choices.Count) { StopMod(); return true; }       // the game's own music this time
            Track track = choices[pick];
            if (current == track && source != null && source.isPlaying && playlistEndCallback == null) return false;
            AudioPlaybackManager.FadeBgMusicVolumeOut(0.5f, null);
            if (routine != null) host.StopCoroutine(routine);
            routine = host.StartCoroutine(Play(track, playlistEndCallback));
            return false;
        }

        public static void StopMod()
        {
            if (routine != null && host != null) host.StopCoroutine(routine);
            routine = null; current = null; Playing = null;
            if (source != null && source.isPlaying) host.StartCoroutine(FadeOut(source, 0.5f));
        }

        private static float GameMusicVolume()
        {
            if (PersistentAudioSettings.MusicMuted.HasValue && PersistentAudioSettings.MusicMuted.Value) return 0f;
            return PersistentAudioSettings.MusicVolume.HasValue ? PersistentAudioSettings.MusicVolume.Value : 1f;
        }

        private static IEnumerator Play(Track track, Action endCallback)
        {
            AudioClip clip;
            if (!clips.TryGetValue(track.Key, out clip) || clip == null)
            {
                WWW www = new WWW(new Uri(track.File).AbsoluteUri);
                yield return www;
                if (!string.IsNullOrEmpty(www.error)) { Debug.LogWarning("OFFLINE MUSIC: " + track.Key + ": " + www.error); if (endCallback != null) endCallback(); yield break; }
                AudioType type = track.File.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS;
                clip = www.GetAudioClip(false, false, type);
                while (clip.loadState == AudioDataLoadState.Loading || clip.loadState == AudioDataLoadState.Unloaded) yield return null;
                if (clip.loadState != AudioDataLoadState.Loaded) { Debug.LogWarning("OFFLINE MUSIC: " + track.Key + " did not decode"); if (endCallback != null) endCallback(); yield break; }
                clip.name = track.Key;
                clips[track.Key] = clip;
            }
            if (source == null) { source = host.gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0f; source.priority = 0; }
            current = track; Playing = track.Key; Started++;
            source.Stop(); source.clip = clip; source.loop = endCallback == null; source.volume = 0f; source.Play();
            Debug.Log("OFFLINE MUSIC: playing " + track.Key + " for " + LastContext + " (" + clip.length.ToString("0") + " s)");
            float fade = 0f;
            while (source != null && source.clip == clip && (source.isPlaying || source.loop))
            {
                fade = Mathf.Min(1f, fade + Time.unscaledDeltaTime / 1.5f);
                source.volume = GameMusicVolume() * track.Volume * fade;
                if (!source.loop && !source.isPlaying) break;
                yield return null;
            }
            if (endCallback != null && current == track) { current = null; Playing = null; endCallback(); }
        }

        private static IEnumerator FadeOut(AudioSource s, float seconds)
        {
            float start = s.volume;
            for (float t = 0; t < seconds && s != null; t += Time.unscaledDeltaTime) { s.volume = start * (1f - t / seconds); yield return null; }
            if (s != null && current == null) s.Stop();
        }
    }
}
