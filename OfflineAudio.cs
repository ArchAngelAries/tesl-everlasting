using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DarkTonic.MasterAudio;
using FullSerializer;
using HarmonyLib;
using Pixelplacement;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mod audio for sound effects and voices (music is OfflineMusic).
    //  - Replacements (<mod>/audio/audio.json "replace"): every game sound plays through a MasterAudio sound group (the
    //    "event", e.g. "fiery_imp_attack", "ui_button_click") whose variation holds the clip. Just before a variation
    //    starts (SoundGroupVariation.FinishSetupToPlay) its clip is swapped for the mod's file when the group name or the
    //    original clip name is replaced, and swapped back otherwise. Buses, volume settings, 3D placement and "finished"
    //    callbacks all stay the game's. Card voices, emotes, UI and effect sounds are covered alike.
    //  - Mod card voices (card json "audio": {"Play": "audio/cards/x-play.ogg", ...}): a mod card's own sound files don't
    //    exist in the game's bundles, so PlayCardAudio loads its template card's audio (the voice it borrows) and plays
    //    the event with the mod's own line swapped in where it has one. Lines with no game sound group (the template has
    //    no such line) play on a plain audio source at the game's master volume.
    public static class OfflineAudio
    {
        public static readonly string[] Slots = { "Play", "Attack", "Death", "Hit", "Success", "Failure" };
        // A shout card's own clip per shout level. Official shouts play one sound per level from the level's effect
        // (Card_UnrelentingForce_Lvl2_Runes plays unrelenting_force_02); a mod shout card borrowing that effect plays its
        // own ShoutN clip instead, for its own cast only.
        public static readonly string[] ShoutSlots = { "Shout1", "Shout2", "Shout3" };
        private static ModCard shoutCard;
        private static int shoutLevel;
        private static float shoutUntil;
        public static int ShoutSwaps;
        public static string LastShout;
        public sealed class Replacement { public string Target, ModId; public List<string> Files = new List<string>(); public float Volume = 1f; }
        public sealed class ModCard { public int Id, TemplateId; public string ModId, Slug; public Dictionary<string, string> Lines = new Dictionary<string, string>(); }

        public static readonly Dictionary<string, Replacement> Replacements = new Dictionary<string, Replacement>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<int, ModCard> Cards = new Dictionary<int, ModCard>();
        public static int Swaps, CardLines, Fallbacks;
        public static string LastSwap;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<AudioClip> ours = new HashSet<AudioClip>();
        private static readonly Dictionary<SoundGroupVariation, AudioClip> originals = new Dictionary<SoundGroupVariation, AudioClip>();
        private static readonly System.Random rng = new System.Random();
        private static string pendingGroup;
        private static AudioClip pendingClip;
        private static MonoBehaviour host;
        private static GameConfiguration config;
        private static System.Reflection.MethodInfo playEvent;

        public static void Load(string modId, string dir, List<string> problems)
        {
            string manifest = Path.Combine(Path.Combine(dir, "audio"), "audio.json");
            if (!File.Exists(manifest)) return;
            Dictionary<string, fsData> root = fsJsonParser.Parse(File.ReadAllText(manifest)).AsDictionary;
            if (!root.ContainsKey("replace")) return;
            foreach (fsData e in root["replace"].AsList)
            {
                Dictionary<string, fsData> r = e.AsDictionary;
                Replacement rep = new Replacement { Target = r["target"].AsString, ModId = modId, Volume = r.ContainsKey("volume") ? (float)r["volume"].AsDouble : 1f };
                foreach (fsData f in r["files"].AsList)
                {
                    string file = Path.Combine(Path.Combine(dir, "audio"), f.AsString);
                    if (File.Exists(file)) rep.Files.Add(file); else problems.Add(modId + ": audio " + rep.Target + " file missing: " + f.AsString);
                }
                if (rep.Files.Count > 0) Replacements[rep.Target] = rep;      // a later mod wins
            }
            EnsurePreload();
        }

        // From OfflineMods for each mod card: the template it was made from and its own lines.
        public static void RegisterCard(string modId, string dir, Card card, Dictionary<string, fsData> json, GameConfiguration gameConfig, List<string> problems)
        {
            config = gameConfig;
            ModCard mc = new ModCard { Id = card.ResourceId.Id, ModId = modId, Slug = card.ResourceId.UniqueName,
                TemplateId = 0 };
            Dictionary<string, fsData> meta = json.ContainsKey("_mod") && json["_mod"].IsDictionary ? json["_mod"].AsDictionary : json;   // the suite's own notes
            if (meta.ContainsKey("template") && meta["template"].IsInt64) mc.TemplateId = (int)meta["template"].AsInt64;
            if (json.ContainsKey("audio") && json["audio"].IsDictionary)
                foreach (KeyValuePair<string, fsData> line in json["audio"].AsDictionary)
                {
                    string file = Path.Combine(dir, line.Value.AsString);
                    if (Array.IndexOf(Slots, line.Key) < 0 && Array.IndexOf(ShoutSlots, line.Key) < 0) problems.Add(modId + ": card " + mc.Slug + " unknown voice line " + line.Key);
                    else if (!File.Exists(file)) problems.Add(modId + ": card " + mc.Slug + " " + line.Key + " file missing");
                    else mc.Lines[line.Key] = file;
                }
            Cards[mc.Id] = mc;
            EnsurePreload();
        }

        public static void Install(Harmony harmony, MonoBehaviour coroutineHost)
        {
            host = coroutineHost;
            playEvent = AccessTools.Method(typeof(AudioPlaybackManager), "PlayEvent");
            harmony.Patch(AccessTools.Method(typeof(SoundGroupVariation), "FinishSetupToPlay"), new HarmonyMethod(typeof(OfflineAudio).GetMethod("FinishSetupPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(AudioUtil), "UnloadNonPreloadedAudioData"), new HarmonyMethod(typeof(OfflineAudio).GetMethod("UnloadPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(AudioPlaybackManager), "PlayCardAudio"), new HarmonyMethod(typeof(OfflineAudio).GetMethod("PlayCardAudioPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(SequenceFxUtils), "LoadCardFx", new Type[] { typeof(BoardState), typeof(CardFxDefinition), typeof(ClientCard), typeof(ClientCard),
                typeof(FxVariantTestOverrides), typeof(MatchFxTracker.RepeatableFxInstanceHandle), typeof(SequenceDataLookup.SequenceFxDelegate) }),
                new HarmonyMethod(typeof(OfflineAudio).GetMethod("LoadCardFxPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(AudioPlaybackManager), "PlayFxSfxAudio"), new HarmonyMethod(typeof(OfflineAudio).GetMethod("PlayFxSfxAudioPrefix")), null, null, null, null);
            EnsurePreload();
        }

        private static bool preloading;
        public static bool PreloadDone;   // every mod clip decoded (or failed) once
        // Mods are read after Install (when the game configuration arrives), so either side may start the loading.
        public static void EnsurePreload()
        {
            if (preloading || host == null) return;
            bool any = Replacements.Count > 0;
            foreach (ModCard c in Cards.Values) if (c.Lines.Count > 0) any = true;
            if (extraFiles.Count > 0) any = true;
            if (!any) return;
            preloading = true;
            host.StartCoroutine(Preload());
        }

        // OGG decodes asynchronously, so every mod file is decoded at start-up and ready when a sound plays.
        private static IEnumerator Preload()
        {
            yield return null;                                               // the rest of the mods register in the same frame
            List<string> files = new List<string>();
            foreach (Replacement r in Replacements.Values) files.AddRange(r.Files);
            foreach (ModCard c in Cards.Values) files.AddRange(c.Lines.Values);
            files.AddRange(extraFiles);
            int ok = 0;
            foreach (string file in files)
            {
                if (clips.ContainsKey(file)) continue;
                AudioClip clip = null;
                if (file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    try { clip = OfflineEmotes.LoadWav(file, Path.GetFileNameWithoutExtension(file)); } catch (Exception e) { Debug.LogWarning("OFFLINE AUDIO: " + file + ": " + e.Message); }
                }
                else
                {
                    WWW www = new WWW(new Uri(file).AbsoluteUri);
                    yield return www;
                    if (string.IsNullOrEmpty(www.error))
                    {
                        clip = www.GetAudioClip(false, false, AudioType.OGGVORBIS);
                        while (clip.loadState == AudioDataLoadState.Loading || clip.loadState == AudioDataLoadState.Unloaded) yield return null;
                        if (clip.loadState != AudioDataLoadState.Loaded) clip = null;
                    }
                    if (clip == null) Debug.LogWarning("OFFLINE AUDIO: " + file + " did not decode " + www.error);
                }
                if (clip == null) continue;
                clip.name = "mod:" + Path.GetFileNameWithoutExtension(file);
                clips[file] = clip; ours.Add(clip); ok++;
            }
            Debug.Log("OFFLINE AUDIO: " + ok + " clips ready; " + Replacements.Count + " replaced sounds, " + Cards.Count + " mod cards.");
            PreloadDone = true;
        }

        // Other mod files to decode at start-up (story voice lines).
        private static readonly List<string> extraFiles = new List<string>();
        public static void AddPreload(string file) { if (!extraFiles.Contains(file)) extraFiles.Add(file); EnsurePreload(); }
        public static MonoBehaviour Host { get { return host; } }
        public static AudioClip ClipFor(string file) { AudioClip c; return file != null && clips.TryGetValue(file, out c) ? c : null; }

        public static AudioClip Pick(Replacement r)
        {
            List<AudioClip> ready = new List<AudioClip>();
            foreach (string f in r.Files) { AudioClip c = ClipFor(f); if (c != null) ready.Add(c); }
            return ready.Count == 0 ? null : ready[rng.Next(ready.Count)];
        }

        // The replacement for a sound group, or for the original clip it holds.
        public static Replacement Find(string group, AudioClip original)
        {
            Replacement r;
            if (group != null && Replacements.TryGetValue(group, out r)) return r;
            if (original != null && Replacements.TryGetValue(original.name, out r)) return r;
            return null;
        }

        public static void FinishSetupPrefix(SoundGroupVariation __instance)
        {
            try
            {
                AudioSource src = __instance.VarAudio;
                if (src == null) return;
                if (src.clip != null && !ours.Contains(src.clip)) originals[__instance] = src.clip;
                AudioClip original;
                originals.TryGetValue(__instance, out original);
                string group = __instance.SoundGroupName;
                AudioClip swap = null;
                if (pendingClip != null && group == pendingGroup) swap = pendingClip;
                else
                {
                    Replacement r = Replacements.Count == 0 ? null : Find(group, original);
                    if (r != null) { swap = Pick(r); if (swap != null && r.Volume < 0.999f) __instance.VarAudio.volume *= r.Volume; }
                }
                if (swap != null) { src.clip = swap; Swaps++; LastSwap = group + " -> " + swap.name; }
                else if (src.clip != null && ours.Contains(src.clip) && original != null) src.clip = original;
                if (Recording != null) Recording.Add(group + "\t" + (original != null ? original.name : "") + "\t" + (src.clip != null ? src.clip.name : ""));
            }
            catch (Exception e) { Debug.LogWarning("OFFLINE AUDIO: swap failed: " + e.Message); }
        }

        public static bool UnloadPrefix(AudioClip clip) { return clip == null || !ours.Contains(clip); }

        public static string SlotOf(Card card, string eventName)
        {
            if (eventName == card.PlayCardVO) return "Play";
            if (eventName == card.AttackCardVO) return "Attack";
            if (eventName == card.DeathCardVO) return "Death";
            if (eventName == card.HitCardVO) return "Hit";
            if (eventName == card.SuccessCardVO) return "Success";
            if (eventName == card.FailureCardVO) return "Failure";
            return null;
        }

        public static Card Template(ModCard mc)
        {
            if (config == null) return null;
            if (mc.TemplateId != 0)
            {
                foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards) if (c != null && c.ResourceId.Id == mc.TemplateId) return c;
                return null;
            }
            // Older mod cards don't name their template: the original card whose voice events they copied.
            Card self = null;
            foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards) if (c != null && c.ResourceId.Id == mc.Id) self = c;
            if (self == null) return null;
            foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards)
            {
                if (c == null || Cards.ContainsKey(c.ResourceId.Id)) continue;
                if ((!string.IsNullOrEmpty(self.PlayCardVO) && !self.PlayCardVO.StartsWith("mod_") && c.PlayCardVO == self.PlayCardVO)
                    || (!string.IsNullOrEmpty(self.AttackCardVO) && !self.AttackCardVO.StartsWith("mod_") && c.AttackCardVO == self.AttackCardVO))
                { mc.TemplateId = c.ResourceId.Id; return c; }
            }
            return null;
        }

        public static bool PlayCardAudioPrefix(Card card, string eventName, Action callback, Transform followTransform)
        {
            ModCard mc;
            if (card == null || string.IsNullOrEmpty(eventName) || !Cards.TryGetValue(card.ResourceId.Id, out mc)) return true;
            if (MasterAudio.Instance == null || !MasterAudio.SoundsReady || host == null || playEvent == null) return true;
            string slot = SlotOf(card, eventName), file;
            AudioClip own = slot != null && mc.Lines.TryGetValue(slot, out file) ? ClipFor(file) : null;
            Card template = Template(mc);
            if (own != null) CardLines++;
            if (template == null)
            {
                if (own == null) return true;
                Fallback(own, callback); return false;
            }
            Singleton<AudioAssetManager>.Instance.CardAudioManager.LoadAudio(template, delegate(CardAudioAssetManager.Receipt receipt, CardAudioAssetManager.AudioAsset asset)
            {
                CardAudioAssetManager.Receipt held = receipt;
                Action done = delegate
                {
                    Singleton<AudioAssetManager>.Instance.CardAudioManager.UnloadAsset(ref held);
                    if (callback != null) callback();
                };
                if (!MasterAudio.SoundGroupExists(eventName))
                {
                    if (own != null) Fallback(own, done); else done();
                    return;
                }
                pendingGroup = eventName; pendingClip = own;
                try { playEvent.Invoke(null, new object[] { eventName, done, followTransform }); }
                finally { pendingGroup = null; pendingClip = null; }
            });
            return false;
        }

        // ---- shout levels ----------------------------------------------------------------------------------------
        // A card effect is about to load for its source card: a shout level effect (name with Lvl1/2/3) of a mod card that
        // has its own clip for that level arms the swap; any other shout level effect (an original shout) disarms it.
        public static void LoadCardFxPrefix(CardFxDefinition fxDefinition, ClientCard source)
        {
            try { if (fxDefinition != null) NoteShoutFx(source == null ? null : source.BaseCard, fxDefinition.Name); }
            catch (Exception e) { Debug.LogWarning("OFFLINE AUDIO: shout effect check failed: " + e.Message); }
        }

        public static void NoteShoutFx(Card baseCard, string fxName)
        {
            int level = ShoutLevelOf(fxName);
            if (level == 0) return;
            ModCard mc;
            if (baseCard != null && Cards.TryGetValue(baseCard.ResourceId.Id, out mc) && mc.Lines.ContainsKey("Shout" + level))
            { shoutCard = mc; shoutLevel = level; shoutUntil = Time.realtimeSinceStartup + 8f; }
            else shoutCard = null;
        }

        public static int ShoutLevelOf(string fxName)
        {
            string n = (fxName ?? "").ToLowerInvariant();
            for (int level = 1; level <= 3; level++) if (n.Contains("lvl" + level)) return level;
            return 0;
        }

        // The armed card's clip for this effect sound: the level's own sound (name ending _01/_02/_03) within 8 s.
        public static string ShoutClipFor(string eventName)
        {
            if (shoutCard == null || Time.realtimeSinceStartup > shoutUntil || string.IsNullOrEmpty(eventName)) return null;
            if (!eventName.EndsWith("_0" + shoutLevel, StringComparison.OrdinalIgnoreCase)) return null;
            string file;
            return shoutCard.Lines.TryGetValue("Shout" + shoutLevel, out file) ? file : null;
        }

        public static bool PlayFxSfxAudioPrefix(string eventName)
        {
            try
            {
                string file = ShoutClipFor(eventName);
                AudioClip clip = file == null ? null : ClipFor(file);
                if (clip == null || host == null) return true;
                LastShout = shoutCard.Slug + ": " + eventName + " -> " + Path.GetFileName(file);
                shoutCard = null; ShoutSwaps++;
                Fallback(clip, null);
                return false;
            }
            catch (Exception e) { Debug.LogWarning("OFFLINE AUDIO: shout swap failed: " + e.Message); return true; }
        }

        public static float SfxVolume() { return MasterAudio.MixerMuted ? 0f : MasterAudio.MasterVolumeLevel; }

        private static AudioSource fallback;
        public static void Fallback(AudioClip clip, Action done)
        {
            Fallbacks++;
            if (fallback == null) { fallback = host.gameObject.AddComponent<AudioSource>(); fallback.playOnAwake = false; fallback.spatialBlend = 0f; }
            fallback.PlayOneShot(clip, SfxVolume());
            if (done != null) host.StartCoroutine(After(clip.length, done));
        }

        // Diagnostic (--tesl-offline-native-menu=audio): a mod card's own line and a borrowed line, and a replaced original
        // card sound, all through the client's own PlayCardAudio; what MasterAudio actually played is written to audio.tsv.
        public static List<string> Recording;
        public static IEnumerator Diagnose(string exportDirectory)
        {
            Recording = new List<string>();
            float until = Time.realtimeSinceStartup + 20f;
            while ((!MasterAudio.SoundsReady || clips.Count == 0) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(2f);
            System.Text.StringBuilder log = new System.Text.StringBuilder("step\tdetail\n");
            List<Card> plays = new List<Card>();
            foreach (ModCard mc in Cards.Values)
            {
                Card card = null;
                foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards) if (c != null && c.ResourceId.Id == mc.Id) card = c;
                if (card == null) continue;
                foreach (string slot in new string[] { "Play", "Attack", "Death" })
                {
                    string ev = slot == "Play" ? card.PlayCardVO : slot == "Attack" ? card.AttackCardVO : card.DeathCardVO;
                    if (string.IsNullOrEmpty(ev) || (slot == "Death" && mc.Lines.Count == 0)) continue;
                    bool finished = false;
                    log.Append("card\t" + mc.Slug + " " + slot + " (" + ev + ") own=" + mc.Lines.ContainsKey(slot) + "\n");
                    AudioPlaybackManager.PlayCardAudio(card, ev, delegate { finished = true; }, null);
                    float t0 = Time.realtimeSinceStartup;
                    while (!finished && Time.realtimeSinceStartup < t0 + 8f) yield return null;
                    log.Append("done\t" + finished + " after " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s\n");
                }
                if (mc.Lines.Count > 0) break;
            }
            foreach (Replacement r in Replacements.Values)
            {
                Card owner = null;
                foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards)
                    if (owner == null && c != null && c.AttackCardVO != null && r.Target.StartsWith(c.AttackCardVO, StringComparison.OrdinalIgnoreCase)) owner = c;
                if (owner == null) continue;
                bool finished = false;
                log.Append("replaced\t" + r.Target + " via " + owner.ResourceId.UniqueName + " attack\n");
                AudioPlaybackManager.PlayCardAudio(owner, owner.AttackCardVO, delegate { finished = true; }, null);
                float t0 = Time.realtimeSinceStartup;
                while (!finished && Time.realtimeSinceStartup < t0 + 8f) yield return null;
                log.Append("done\t" + finished + "\n");
                break;
            }
            // shout levels: a mod card with its own level clip (or, for the check, a borrowed Play line) through PlayFxSfxAudio
            foreach (ModCard mc in Cards.Values)
            {
                bool borrowed = !mc.Lines.ContainsKey("Shout2");
                if (borrowed && !mc.Lines.ContainsKey("Play")) continue;
                if (borrowed) mc.Lines["Shout2"] = mc.Lines["Play"];
                Card shoutCardData = null;
                foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards) if (c != null && c.ResourceId.Id == mc.Id) shoutCardData = c;
                int swapsBefore = ShoutSwaps;
                NoteShoutFx(shoutCardData, "Card_UnrelentingForce_Lvl2_Runes");
                AudioPlaybackManager.PlayFxSfxAudio("unrelenting_force_02");
                log.Append("shout\t" + mc.Slug + " level 2 " + (borrowed ? "(test clip) " : "") + "swapped=" + (ShoutSwaps == swapsBefore + 1) + " " + LastShout + "\n");
                if (borrowed) mc.Lines.Remove("Shout2");
                yield return new WaitForSeconds(3f);
                break;
            }
            foreach (string line in Recording) log.Append("played\t" + line + "\n");
            log.Append("counts\tswaps " + Swaps + ", own card lines " + CardLines + ", fallbacks " + Fallbacks + "\n");
            File.WriteAllText(Path.Combine(exportDirectory, "audio.tsv"), log.ToString());
            Debug.Log("OFFLINE AUDIO: diagnostic done; swaps " + Swaps + ", own card lines " + CardLines + ", fallbacks " + Fallbacks);
            Recording = null;
        }

        private static IEnumerator After(float seconds, Action done) { yield return new WaitForSeconds(seconds); done(); }
    }
}
