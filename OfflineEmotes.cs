using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Emotes offline.
    //  - Mod emote lines: a mod avatar's "emotes" (avatars.json) become its own AvatarEmotePackage (text, and optional WAV
    //    voice); missing emote types keep the borrowed voice's lines. Mod audio names start with "Mods/" and are played by a
    //    prefix on EmotesWin.LoadAndStartAudio (the original path loads an audio-creator prefab for MasterAudio).
    //  - The AI opponent emotes: solo matches never carry emote input events, so the AI's emotes are handed to the board
    //    in-process (BoardInputController.ReceiveInputInstantMessage, as the network path does for a human opponent). The
    //    client leaves AI emote text empty (its AI branch in BoardConductor.ShowUserEmote fills nothing), so a prefix fills
    //    it from the AI's avatar voice, or a voice picked from its name.
    //  - Behaviour (simple, replaceable by a future AI-player mod through AiEmote): greets on its first turn, answers
    //    Hello/Good Game/Nice Play/Thank You/Taunt, once per kind per match, after a short pause.
    public static class OfflineEmotes
    {
        public const string AudioPrefix = "Mods/";
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> clipFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static bool AiEmotesEnabled = true;
        // Avatar whose emote lines every AI opponent uses (null: its own avatar's, else one picked from its name).
        public static string AiVoiceOverride;
        public static int AiEmotesSent;
        private static object currentBoard;
        private static readonly HashSet<string> spokenThisMatch = new HashSet<string>();
        private static MonoBehaviour host;

        public static void Install(Harmony harmony, MonoBehaviour coroutineHost)
        {
            host = coroutineHost;
            harmony.Patch(AccessTools.Method(typeof(EmotesWin), "LoadAndStartAudio"), new HarmonyMethod(typeof(OfflineEmotes).GetMethod("LoadAudioPrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(BoardConductor), "ShowUserEmote"), new HarmonyMethod(typeof(OfflineEmotes).GetMethod("ShowUserEmotePrefix")), null, null, null, null);
            harmony.Patch(AccessTools.Method(typeof(BoardInputController), "HandleOnUserEmote"), null, new HarmonyMethod(typeof(OfflineEmotes).GetMethod("HumanEmotePostfix")), null, null, null);
            harmony.Patch(AccessTools.Method(typeof(BoardInputController), "ReceiveOpponentCardCommandState"), null, new HarmonyMethod(typeof(OfflineEmotes).GetMethod("OpponentTurnPostfix")), null, null, null);
            OfflineExtraEmotes.Install(harmony);
        }

        public static void RegisterAudio(string name, string wavFile) { clipFiles[name] = wavFile; clips.Remove(name); }
        public static bool HasAudio(string name) { return name != null && clipFiles.ContainsKey(name); }

        public static AudioClip Clip(string name)
        {
            AudioClip clip;
            if (clips.TryGetValue(name, out clip) && clip != null) return clip;
            string file;
            if (!clipFiles.TryGetValue(name, out file)) return null;
            clip = LoadWav(file, name);
            clips[name] = clip;
            return clip;
        }

        // ---- mod voice audio -----------------------------------------------------------------------
        public static bool LoadAudioPrefix(EmotesWin __instance, string audioName, uint contentPackIndex, Action<float> OnAudioReadyToPlay, ref IEnumerator __result)
        {
            if (audioName == null || !audioName.StartsWith(AudioPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            __result = PlayModAudio(__instance, audioName, OnAudioReadyToPlay);
            return false;
        }

        private static IEnumerator PlayModAudio(EmotesWin win, string audioName, Action<float> ready)
        {
            AudioClip clip = null;
            try { clip = Clip(audioName); } catch (Exception e) { Debug.LogWarning("OFFLINE EMOTES: " + audioName + ": " + e.Message); }
            if (clip == null) { ready(5f); yield break; }
            AudioSource source = win.gameObject.GetComponent<AudioSource>() ?? win.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0f;
            source.Stop(); source.clip = clip; source.Play();
            ready(clip.length);
        }

        // Diagnostic: the emote panel's buttons (path, position, size, label) for laying out extra pages.
        public static string DescribeWheel(UserEmotesWin wheel)
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder("index\tpath\tanchored\tsize\tlabel\n");
            UnityEngine.UI.Button[] buttons = (UnityEngine.UI.Button[])AccessTools.Field(typeof(UserEmotesWin), "EmoteButtons").GetValue(wheel);
            for (int i = 0; i < buttons.Length; i++)
            {
                UnityEngine.UI.Button b = buttons[i];
                if (b == null) { s.Append(i).Append("\tnull\n"); continue; }
                RectTransform r = (RectTransform)b.transform;
                string path = b.name; for (Transform t = b.transform.parent; t != null && t != wheel.transform; t = t.parent) path = t.name + "/" + path;
                TMPro.TMP_Text text = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                s.Append(i).Append('\t').Append(path).Append('\t').Append(r.anchoredPosition).Append('\t').Append(r.rect.size).Append('\t')
                    .Append(text != null ? text.text : "").Append('\n');
            }
            foreach (Transform t in wheel.GetComponentsInChildren<Transform>(true))
            {
                string path = t.name; for (Transform u = t.parent; u != null && u != wheel.transform; u = u.parent) path = u.name + "/" + path;
                Component[] cs = t.GetComponents<Component>();
                string kinds = ""; foreach (Component c in cs) if (c != null) kinds += c.GetType().Name + ",";
                s.Append("node\t").Append(path).Append('\t').Append(kinds).Append('\n');
            }
            return s.ToString();
        }

        // PCM WAV (8/16-bit, mono or stereo) without Unity's WWW, so it loads synchronously on first use.
        public static AudioClip LoadWav(string file, string name)
        {
            byte[] b = File.ReadAllBytes(file);
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A') throw new InvalidOperationException("not a WAV file: " + file);
            int pos = 12, channels = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ")
                {
                    if (BitConverter.ToInt16(b, pos + 8) != 1) throw new InvalidOperationException("only PCM WAV is supported: " + file);
                    channels = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22);
                }
                else if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataAt < 0 || channels < 1 || rate < 1 || (bits != 8 && bits != 16)) throw new InvalidOperationException("unsupported WAV layout: " + file);
            int bytes = bits / 8, count = dataLen / bytes;
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
                samples[i] = bits == 16 ? BitConverter.ToInt16(b, dataAt + i * 2) / 32768f : (b[dataAt + i] - 128) / 128f;
            AudioClip clip = AudioClip.Create(name, count / channels, channels, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // ---- AI emote text and voice ---------------------------------------------------------------
        public static void ShowUserEmotePrefix(BoardConductor __instance, ref UserEmoteData emoteData)
        {
            try
            {
                if (emoteData.PlayerType != PlayerControllerType.Opponent || !string.IsNullOrEmpty(emoteData.EmoteKey.RawKey)) return;
                MatchPlayerData data = __instance.BoardState == null ? null : __instance.BoardState.FindMatchPlayerData(emoteData.PlayerType);
                if (data == null || !data.isAIPlayer) return;
                AvatarEmotePackage voice = VoiceFor(data);
                if ((int)emoteData.EmoteType < 8) emoteData.EmoteType = OfflineExtraEmotes.Vary(voice, emoteData.EmoteType);
                AvatarEmoteData line = LineFor(voice, emoteData.EmoteType);
                if (line == null) return;
                emoteData.EmoteKey = line.EmoteString; emoteData.AudioPath = line.EmoteAudio; emoteData.ContentPackIndex = line.ContentPackIndex;
                Debug.Log("OFFLINE EMOTES: AI " + emoteData.EmoteType + " -> " + line.EmoteString.RawKey + " audio '" + line.EmoteAudio + "'");
            }
            catch (Exception e) { Debug.LogWarning("OFFLINE EMOTES: AI emote text: " + e.Message); }
        }

        // The AI's avatar voice if its avatar is a player avatar (original or mod), else an original voice chosen from its name.
        // Mod avatars' lines belong to those avatars only, so the name-based pick never lands on them.
        public static AvatarEmotePackage VoiceFor(MatchPlayerData data)
        {
            AvatarCollection col = GameManager.Instance.Config.AvatarCollection;
            int h = OfflineMods.StableId((data.AvatarUniqueName ?? "") + "|" + (data.Name ?? ""));
            if (string.IsNullOrEmpty(AiVoiceOverride) && string.IsNullOrEmpty(data.AvatarUniqueVoName))
            {   // what the player sees (an AiOpponent portrait) decides the voice before the entry's name does
                AvatarEmotePackage shown = VoiceForPortrait(col, data, h);
                if (shown != null) return shown;
            }
            foreach (string name in new string[] { AiVoiceOverride, data.AvatarUniqueVoName, data.AvatarUniqueName })
            {
                if (string.IsNullOrEmpty(name)) continue;
                AvatarData a = col.Avatars.Find(delegate(AvatarData x) { return x != null && x.ResourceId.UniqueName == name; });
                if (a != null) { AvatarEmotePackage p = Package(col, a.EmotePackage.resourceId); if (p != null) return p; }
            }
            List<AvatarEmotePackage> voiced = col.EmotePackages.FindAll(delegate(AvatarEmotePackage p) { return p != null && p.EmoteData.Count > 0 && !OfflineMods.EmotePackages.Contains(p); });
            if (voiced.Count == 0) return null;
            return voiced[(h & 0x7fffffff) % voiced.Count];
        }

        // Practice, event and story opponents are AiOpponent entries (a portrait and a name, no voice). Their voice follows
        // what is shown: the original avatar whose picture the portrait is, else an original voice of the race (and gender,
        // where the name says it) that the name or portrait names. Opponents with no race (beasts, groups) return null.
        private static readonly string[][] RaceWords = {
            new[] { "Argonian", "argonian", "black marsh", "blackmarsh", "shadowscale", "saxhleel" },
            new[] { "Breton", "breton", "high rock", "wayrest", "daggerfall", "reachmen", "reach " },
            new[] { "DarkElf", "darkelf", "dark elf", "dunmer", "dres ", "telvanni", "hlaalu", "redoran", "indoril", "morrowind", "mournhold", "ashlander" },
            new[] { "HighElf", "highelf", "high elf", "altmer", "summerset", "skywatch", "alinor", "thalmor", "artaeum", "crystal tower" },
            new[] { "Imperial", "imperial", "legion", "cyrodiil", "cheydinhal", "kvatch", "bruma", "chorrol", "anvil", "skingrad", "penitus" },
            new[] { "Khajiit", "khajiit", "elsweyr", "skooma", "rimmen", "riverhold", "senche", "mane " },
            new[] { "Nord", "nord", "skyrim", "stormcloak", "windhelm", "whiterun", "solitude", "rikke", "companion", "winterhold", "dragon guard", "falkreath", "riften" },
            new[] { "Orc", "orc", "orsimer", "orsinium", "clan", "stronghold", "malacath" },
            new[] { "Redguard", "redguard", "hammerfell", "rihad", "alik'r", "sentinel", "yokuda", "crown ", "forebear" },
            new[] { "WoodElf", "woodelf", "wood elf", "bosmer", "valenwood", "greenheart", "elden root", "falinesti" } };
        private static readonly string[] FemaleWords = { "queen", "lady", "mistress", "matron", "priestess", "sister", "mother", "witch", "huntress", "sorceress", "princess", "baroness", "countess", "duchess", "maiden", "daughter", "wife", "widow", "she ", "her ", "heroine", "enchantress", "seductress", "empress", "matriarch" };
        private static readonly string[] MaleWords = { "king", " lord", "baron ", "brother", "father", "prince ", "count ", "duke", "son ", "husband", "he ", "his ", "emperor", "patriarch", "sir ", "monk" };
        private static Dictionary<string, AvatarData> portraitAvatars;

        public static AvatarEmotePackage VoiceForPortrait(AvatarCollection col, MatchPlayerData data, int h)
        {
            List<AiOpponent> opponents = GameManager.Instance.Config.AiOpponents;
            string name = data.AvatarUniqueName;
            AiOpponent ai = opponents == null || string.IsNullOrEmpty(name) ? null : opponents.Find(delegate(AiOpponent x) { return x != null && x.ResourceId.UniqueName == name; });
            if (ai == null) return null;
            if (portraitAvatars == null)
            {
                Dictionary<string, AvatarData> map = new Dictionary<string, AvatarData>(StringComparer.OrdinalIgnoreCase);
                foreach (AvatarData a in col.Avatars)
                    if (a != null && !OfflineMods.Avatars.Contains(a))
                        foreach (string img in new[] { a.CircularImage, a.Image })
                            if (!string.IsNullOrEmpty(img) && !map.ContainsKey(Base(img))) map[Base(img)] = a;
                portraitAvatars = map;
            }
            AvatarData pictured;
            if (!string.IsNullOrEmpty(ai.Image) && portraitAvatars.TryGetValue(Base(ai.Image), out pictured))
            {
                AvatarEmotePackage p = Package(col, pictured.EmotePackage.resourceId);
                if (p != null && p.EmoteData.Count > 0) return p;
            }
            string words = OpponentWords(ai, data.Name);
            string race = OpponentRace(ai, data.Name);
            if (race == null) return null;
            string gender = null;
            foreach (string w in FemaleWords) if (words.Contains(w.StartsWith(" ") ? w : " " + w)) { gender = "female"; break; }
            if (gender == null) foreach (string w in MaleWords) if (words.Contains(w.StartsWith(" ") ? w : " " + w)) { gender = "male"; break; }
            List<AvatarEmotePackage> ofRace = new List<AvatarEmotePackage>();
            foreach (AvatarData a in col.Avatars)
            {
                if (a == null || OfflineMods.Avatars.Contains(a) || !a.ResourceId.UniqueName.StartsWith("Avatar" + race, StringComparison.Ordinal)) continue;
                AvatarEmotePackage p = Package(col, a.EmotePackage.resourceId);
                if (p != null && p.EmoteData.Count > 0 && !ofRace.Contains(p)) ofRace.Add(p);
            }
            if (gender != null)
            {
                List<AvatarEmotePackage> g = ofRace.FindAll(delegate(AvatarEmotePackage p) { return Gender(p) == gender; });
                if (g.Count > 0) ofRace = g;
            }
            return ofRace.Count == 0 ? null : ofRace[(h & 0x7fffffff) % ofRace.Count];
        }

        // The opponent's shown name (localized), the match name, its id and portrait file, as lower-case words.
        public static string OpponentDisplayName(AiOpponent ai)
        {
            string text;
            if (ai != null && !string.IsNullOrEmpty(ai.DisplayNameKey.RawKey) && LocalizationManager.Instance != null && LocalizationManager.Instance.TryGetText(ai.DisplayNameKey.RawKey, out text)) return text;
            return "";
        }
        private static string OpponentWords(AiOpponent ai, string playerName)
        {
            return " " + (OpponentDisplayName(ai) + " " + playerName + " " + ai.ResourceId.UniqueName + " " + Base(ai.Image ?? "")).Replace('_', ' ').ToLowerInvariant() + " ";
        }

        // The race an AiOpponent shows (Argonian, Breton, DarkElf, HighElf, Imperial, Khajiit, Nord, Orc, Redguard, WoodElf):
        // the race of the card whose art the portrait is, else race words in its name or portrait; null when none.
        public static string OpponentRace(AiOpponent ai, string playerName)
        {
            if (ai == null) return null;
            string race = CardRace(ai.Image);   // a portrait made from a card's art: that card's race
            if (race != null) return race;
            string words = OpponentWords(ai, playerName);
            foreach (string[] r in RaceWords)
                for (int i = 1; i < r.Length; i++) if (words.Contains(" " + r[i])) return r[0];
            return null;
        }

        // Card art reused as AI portraits (e.g. AIPortraits/.../varanis_courier, Cards/Visuals/X/X): the race subtype of the
        // original card with that art file or that name. Mod cards are left out.
        private static Dictionary<string, string> cardRaces;
        private static readonly string[][] SubtypeRaces = {
            new[] { "Argonian", "Argonian" }, new[] { "Breton", "Breton" }, new[] { "Dark_Elf", "DarkElf" }, new[] { "High_Elf", "HighElf" },
            new[] { "Imperial", "Imperial" }, new[] { "Khajiit", "Khajiit" }, new[] { "Nord", "Nord" }, new[] { "Orc", "Orc" },
            new[] { "Redguard", "Redguard" }, new[] { "Wood_Elf", "WoodElf" } };
        public static string CardRace(string portrait)
        {
            if (string.IsNullOrEmpty(portrait)) return null;
            if (cardRaces == null)
            {
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> clash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                GameConfiguration config = GameManager.Instance == null ? null : GameManager.Instance.Config;
                if (config == null) return null;
                foreach (CardSet set in config.CardCollection)
                    foreach (Card c in set.Cards)
                    {
                        if (c == null || c.Subtypes == null || OfflineMods.ModOfCard(c.ResourceId.Id) != null) continue;
                        string race = null;
                        foreach (CardSubtype t in c.Subtypes)
                            foreach (string[] r in SubtypeRaces) if (t.ToString() == r[0]) race = r[1];
                        if (race == null) continue;
                        foreach (string key in new[] { Key(c.Name), Key(Base(c.Image ?? "")) })
                        {
                            if (key.Length < 4 || clash.Contains(key)) continue;
                            string had;
                            if (map.TryGetValue(key, out had) && had != race) { map.Remove(key); clash.Add(key); }
                            else map[key] = race;
                        }
                    }
                cardRaces = map;
            }
            string found;
            return cardRaces.TryGetValue(Key(Base(portrait)), out found) ? found : null;
        }

        // Letters and digits only, lower case: "Varanis Courier" and "varanis_courier" give the same key.
        private static string Key(string text)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (char ch in text ?? "") if (char.IsLetterOrDigit(ch)) b.Append(char.ToLowerInvariant(ch));
            string k = b.ToString();
            return k.EndsWith("png") ? k.Substring(0, k.Length - 3) : k;
        }

        // "male" or "female" from the package's line keys (e.g. wood_elf_male_b_hello), else null.
        public static string Gender(AvatarEmotePackage p)
        {
            foreach (AvatarEmoteData d in p.EmoteData)
            {
                string k = d == null ? null : (d.EmoteString.RawKey ?? "").ToLowerInvariant();
                if (string.IsNullOrEmpty(k)) continue;
                if (k.Contains("female")) return "female";
                if (k.Contains("male")) return "male";
            }
            return null;
        }

        private static string Base(string path) { int i = path.LastIndexOfAny(new[] { '/', '\\' }); return i >= 0 ? path.Substring(i + 1) : path; }

        public static AvatarEmotePackage Package(AvatarCollection col, int id)
        { return col.EmotePackages.Find(delegate(AvatarEmotePackage p) { return p != null && p.ResourceId.Id == id; }); }

        public static AvatarEmoteData LineFor(AvatarEmotePackage p, UserEmoteType type)
        { return p == null ? null : p.EmoteData.Find(delegate(AvatarEmoteData x) { return x != null && x.EmoteType == type; }); }

        // ---- AI behaviour --------------------------------------------------------------------------
        // The AI says this emote now (also the entry point for a future AI-player mod).
        public static bool AiEmote(UserEmoteType type)
        {
            BoardInputController board = UnityEngine.Object.FindObjectOfType<BoardInputController>();
            if (board == null) return false;
            if (OpponentIsCreature(board)) return false;   // beasts, monsters and constructs have no voice of their own
            board.ReceiveInputInstantMessage(PlayerControllerType.Opponent, CcgRpc.HistoryType.InputInstantEmote,
                new CcgRpc.HistoryInputVisualization { EmoteType = (int)type });
            AiEmotesSent++;
            return true;
        }

        // An AiOpponent with no race that its name or portrait shows to be a creature (2026-11-13: 302 of 718 opponents have no
        // race; beasts and monsters used to emote with a random person's voice). Groups of people keep a voice.
        private static readonly string[] CreatureWords = { "wolf", "wolves", "bear", "spider", "troll", "mammoth", "skeever", "mudcrab",
            "dragon", "skeleton", "zombie", "draugr", "atronach", "centurion", "sphere", "automaton", "fabricant", "netch", "kwama",
            "guar", "horker", "giant ", "frostbite", "slaughterfish", "chaurus", "falmer", "hagraven", "wisp", "spriggan", "beast",
            "sabre cat", "sabrecat", "senche-tiger", "crocodile", "ogre", "imp ", "scamp", "clannfear", "daedroth", "harpy",
            "nix-hound", "cliff racer", "dreugh", "lurker", "seeker", "gargoyle", "golem", "construct", "crab", "rat ", "goat",
            "mudcrabs", "snake", "serpent", "wamasu", "hackwing", "durzog", "echatere", "minotaur", "colossus", "hound" };

        public static bool IsCreatureOpponent(MatchPlayerData data)
        {
            if (data == null || GameManager.Instance == null || GameManager.Instance.Config == null) return false;
            List<AiOpponent> opponents = GameManager.Instance.Config.AiOpponents;
            AiOpponent ai = opponents == null || string.IsNullOrEmpty(data.AvatarUniqueName) ? null
                : opponents.Find(delegate(AiOpponent x) { return x != null && x.ResourceId.UniqueName == data.AvatarUniqueName; });
            if (ai == null) return false;
            if (VoiceForPortrait(GameManager.Instance.Config.AvatarCollection, data, 0) != null) return false;   // a race or a pictured avatar
            string words = OpponentWords(ai, data.Name);
            // whole words only ("guar" is not "guards"); people named after a creature (a Dragon Priest) keep their voice
            HashSet<string> tokens = new HashSet<string>(System.Text.RegularExpressions.Regex.Split(words, "[^a-z'-]+"));
            foreach (string person in new[] { "priest", "cultist", "caller", "beastcaller", "herder", "heder", "scout", "hunter", "tamer", "rider", "keeper", "master", "handler", "trainer" })
                if (tokens.Contains(person) || tokens.Contains(person + "s")) return false;
            foreach (string raw in CreatureWords)
            {
                string w = raw.Trim();
                if (w.Contains(" ") ? words.Contains(" " + w + " ") || words.Contains(" " + w + "s ") : tokens.Contains(w) || tokens.Contains(w + "s") || tokens.Contains(w + "es"))
                    return true;
            }
            return false;
        }

        private static bool OpponentIsCreature(BoardInputController board)
        {
            BoardConductor conductor = (BoardConductor)AccessTools.Field(typeof(BoardInputController), "_boardConductor").GetValue(board);
            MatchPlayerData data = conductor == null || conductor.BoardState == null ? null : conductor.BoardState.FindMatchPlayerData(PlayerControllerType.Opponent);
            return data != null && data.isAIPlayer && IsCreatureOpponent(data);
        }

        private static bool OpponentIsAi(BoardInputController board)
        {
            BoardConductor conductor = (BoardConductor)AccessTools.Field(typeof(BoardInputController), "_boardConductor").GetValue(board);
            MatchPlayerData data = conductor == null || conductor.BoardState == null ? null : conductor.BoardState.FindMatchPlayerData(PlayerControllerType.Opponent);
            return data != null && data.isAIPlayer;
        }

        // ---- AI personality --------------------------------------------------------------------------
        // Each AI opponent gets a temperament (stable per opponent name, or AiPersonalityOverride) that decides what it says
        // on its own (opening, big hits either way, a wiped board, near death, a slow opponent, the result) and how it
        // answers the human's emotes. Paced: at least MinGapSeconds between its own emotes, at most MaxPerMatch per match.
        public enum Temperament { Friendly, Cocky, Playful, Stoic }
        public static string AiPersonalityOverride;           // "Friendly", "Cocky", "Playful", "Stoic" or null
        public static float MinGapSeconds = 10f; public static int MaxPerMatch = 8;
        public static Temperament CurrentTemperament;
        private static float lastAiEmote = -100f; private static int matchEmotes;
        private static readonly System.Random rng = new System.Random();
        private static readonly List<float> humanEmoteTimes = new List<float>();

        public static Temperament TemperamentFor(MatchPlayerData data)
        {
            if (!string.IsNullOrEmpty(AiPersonalityOverride))
                try { return (Temperament)Enum.Parse(typeof(Temperament), AiPersonalityOverride, true); } catch (ArgumentException) { }
            string own = OfflineOpponents.TemperamentOf(data == null ? null : data.Name);   // a mod opponent's own personality
            if (!string.IsNullOrEmpty(own))
                try { return (Temperament)Enum.Parse(typeof(Temperament), own, true); } catch (ArgumentException) { }
            int h = OfflineMods.StableId("temper|" + (data == null ? "" : (data.Name ?? "") + "|" + (data.AvatarUniqueName ?? "")));
            return (Temperament)((h & 0x7fffffff) % 4);
        }

        private static UserEmoteType[] O(params UserEmoteType[] options) { return options; }

        // What the temperament says for an event (null: stays quiet).
        public static UserEmoteType? Pick(Temperament t, string evt)
        {
            UserEmoteType[] options; double chance;
            switch (evt)
            {
                case "opening":
                    chance = t == Temperament.Stoic ? 0.5 : 0.95;
                    options = t == Temperament.Cocky ? O(UserEmoteType.Taunt, UserEmoteType.Hello, UserEmoteType.Taunt)
                        : t == Temperament.Playful ? O(UserEmoteType.Hello, UserEmoteType.Taunt) : O(UserEmoteType.Hello); break;
                case "big-hit-dealt":
                    chance = t == Temperament.Stoic ? 0.15 : t == Temperament.Friendly ? 0.3 : 0.7;
                    options = t == Temperament.Friendly ? O(UserEmoteType.Sorry) : t == Temperament.Playful ? O(UserEmoteType.Taunt, UserEmoteType.Oops) : O(UserEmoteType.Taunt); break;
                case "big-hit-taken":
                    chance = t == Temperament.Stoic ? 0.25 : 0.65;
                    options = t == Temperament.Cocky ? O(UserEmoteType.Implore, UserEmoteType.Oops) : t == Temperament.Playful ? O(UserEmoteType.Oops, UserEmoteType.NicePlay) : O(UserEmoteType.NicePlay); break;
                case "board-wiped":
                    chance = t == Temperament.Friendly ? 0.4 : 0.75;
                    options = t == Temperament.Friendly ? O(UserEmoteType.Sorry) : O(UserEmoteType.Taunt); break;
                case "own-board-wiped":
                    chance = 0.6;
                    options = t == Temperament.Cocky ? O(UserEmoteType.Implore) : t == Temperament.Friendly ? O(UserEmoteType.NicePlay) : O(UserEmoteType.Oops); break;
                case "ai-near-death":
                    chance = 0.7;
                    options = t == Temperament.Cocky ? O(UserEmoteType.Taunt) : t == Temperament.Playful ? O(UserEmoteType.Oops, UserEmoteType.Implore)
                        : t == Temperament.Friendly ? O(UserEmoteType.NicePlay) : O(UserEmoteType.Implore); break;
                case "human-near-death":
                    chance = t == Temperament.Stoic ? 0.2 : 0.7;
                    options = t == Temperament.Friendly ? O(UserEmoteType.Sorry) : O(UserEmoteType.Taunt); break;
                case "slow-human":
                    chance = t == Temperament.Friendly || t == Temperament.Stoic ? 0.2 : 0.8;
                    options = O(UserEmoteType.Implore); break;
                case "won":
                    chance = 0.9;
                    options = t == Temperament.Cocky ? O(UserEmoteType.Taunt, UserEmoteType.GoodGame) : O(UserEmoteType.GoodGame); break;
                case "lost":
                    chance = t == Temperament.Cocky ? 0.5 : 0.95;
                    options = t == Temperament.Cocky ? O(UserEmoteType.Sorry) : O(UserEmoteType.GoodGame); break;
                case "spam":
                    chance = t == Temperament.Cocky || t == Temperament.Playful ? 0.8 : 0.1;
                    options = t == Temperament.Cocky ? O(UserEmoteType.Taunt) : O(UserEmoteType.Oops, UserEmoteType.Taunt); break;
                default: return null;
            }
            if (rng.NextDouble() > chance) return null;
            return options[rng.Next(options.Length)];
        }

        // The reply to a human emote, by temperament (null: no reply).
        public static UserEmoteType? Reply(Temperament t, UserEmoteType human)
        {
            bool coin = rng.NextDouble() < 0.5;
            switch (human)
            {
                case UserEmoteType.Hello: return t == Temperament.Cocky && coin ? UserEmoteType.Taunt : UserEmoteType.Hello;
                case UserEmoteType.GoodGame: return t == Temperament.Cocky && rng.NextDouble() < 0.3 ? UserEmoteType.Taunt : UserEmoteType.GoodGame;
                case UserEmoteType.NicePlay: return t == Temperament.Cocky && rng.NextDouble() < 0.6 ? UserEmoteType.Taunt : UserEmoteType.ThankYou;
                case UserEmoteType.ThankYou:
                    if (t == Temperament.Stoic) return null;
                    return t == Temperament.Cocky ? UserEmoteType.Taunt : UserEmoteType.NicePlay;
                case UserEmoteType.Taunt:
                case UserEmoteType.Oops:
                    if (t == Temperament.Stoic) return null;
                    return t == Temperament.Friendly ? UserEmoteType.Sorry : UserEmoteType.Taunt;
                case UserEmoteType.Sorry:
                    if (t == Temperament.Cocky) return UserEmoteType.Taunt;
                    if (t == Temperament.Friendly) return UserEmoteType.ThankYou;
                    return null;
                case UserEmoteType.Implore:
                    if (t == Temperament.Playful) return UserEmoteType.Oops;
                    if (t == Temperament.Cocky) return UserEmoteType.Taunt;
                    return null;
                default: return null;
            }
        }

        private static bool CanSpeak() { return matchEmotes < MaxPerMatch && Time.realtimeSinceStartup - lastAiEmote >= MinGapSeconds; }

        private static void Say(UserEmoteType? type, float delay)
        {
            if (type == null || host == null || !CanSpeak()) return;
            lastAiEmote = Time.realtimeSinceStartup + delay; matchEmotes++;
            host.StartCoroutine(Later(delay, type.Value));
        }

        private static void NewMatch(BoardInputController board)
        {
            currentBoard = board; spokenThisMatch.Clear(); humanEmoteTimes.Clear(); matchEmotes = 0; lastAiEmote = -100f;
            BoardConductor conductor = (BoardConductor)AccessTools.Field(typeof(BoardInputController), "_boardConductor").GetValue(board);
            CurrentTemperament = TemperamentFor(conductor == null || conductor.BoardState == null ? null : conductor.BoardState.FindMatchPlayerData(PlayerControllerType.Opponent));
            Debug.Log("OFFLINE EMOTES: AI temperament " + CurrentTemperament);
            if (host != null) host.StartCoroutine(Watch(board));
        }

        public static void OpponentTurnPostfix(BoardInputController __instance)
        {
            if (!AiEmotesEnabled || host == null || !OpponentIsAi(__instance)) return;
            if (!ReferenceEquals(currentBoard, __instance)) NewMatch(__instance);
            if (spokenThisMatch.Add("opening")) Say(Pick(CurrentTemperament, "opening"), 1.5f);
        }

        public static void HumanEmotePostfix(BoardInputController __instance, UserEmoteType emoteType)
        {
            if (!AiEmotesEnabled || host == null || !OpponentIsAi(__instance)) return;
            if (!ReferenceEquals(currentBoard, __instance)) NewMatch(__instance);
            float now = Time.realtimeSinceStartup;
            humanEmoteTimes.Add(now); humanEmoteTimes.RemoveAll(delegate(float x) { return now - x > 8f; });
            if (humanEmoteTimes.Count >= 3 && spokenThisMatch.Add("spam")) { Say(Pick(CurrentTemperament, "spam"), 1.5f); return; }
            if ((int)emoteType >= OfflineExtraEmotes.ExtraBase)
            {   // an extra emote is answered as the emote it is marked like; unmarked extras get no reply
                OfflineExtraEmotes.Extra extra = OfflineExtraEmotes.Find(OfflineExtraEmotes.PlayerPackage(), emoteType);
                if (extra == null || extra.Like == null) return;
                emoteType = extra.Like.Value;
            }
            if (emoteType == UserEmoteType.Hello) spokenThisMatch.Add("opening");
            // Replies skip the gap (an answer is expected) but still count toward the match limit.
            if (matchEmotes >= MaxPerMatch) return;
            UserEmoteType? reply = Reply(CurrentTemperament, emoteType);
            if (reply == null) return;
            matchEmotes++; lastAiEmote = now + 2f;
            host.StartCoroutine(Later(1.5f + (float)rng.NextDouble(), reply.Value));
        }

        // Samples the match once a second and turns changes into events.
        private static IEnumerator Watch(BoardInputController board)
        {
            int lastHuman = int.MinValue, lastAi = int.MinValue, lastHumanBoard = 0, lastAiBoard = 0;
            float humanTurnSince = -1f;
            while (board != null && ReferenceEquals(currentBoard, board))
            {
                yield return new WaitForSeconds(1f);
                Simulation s = OfflineNativeMatch.CurrentSimulation;
                if (s == null || s._turnStateStack.Count == 0) continue;
                PlayerState human = s.Players.Find(delegate(PlayerState p) { return p.PlayerType == (int)PlayerType.Normal; });
                PlayerState ai = s.Players.Find(delegate(PlayerState p) { return p != human; });
                if (human == null || ai == null) continue;
                int hh = Health(s, human), ah = Health(s, ai);
                int hb = s.CardCatalog.FindCardCount(GameZoneName.CreatureBoard, human.NetId), ab = s.CardCatalog.FindCardCount(GameZoneName.CreatureBoard, ai.NetId);
                if (hh <= 0 || ah <= 0)
                {
                    if (spokenThisMatch.Add("result")) { lastAiEmote = -100f; Say(Pick(CurrentTemperament, ah <= 0 ? "lost" : "won"), 0.5f); }
                    continue;
                }
                if (lastHuman != int.MinValue)
                {
                    if (lastHuman - hh >= 6) Say(Pick(CurrentTemperament, "big-hit-dealt"), 1f);
                    else if (lastAi - ah >= 6) Say(Pick(CurrentTemperament, "big-hit-taken"), 1f);
                    else if (lastHumanBoard >= 3 && hb == 0) Say(Pick(CurrentTemperament, "board-wiped"), 1f);
                    else if (lastAiBoard >= 3 && ab == 0) Say(Pick(CurrentTemperament, "own-board-wiped"), 1f);
                    if (ah <= 5 && spokenThisMatch.Add("ai-near-death")) Say(Pick(CurrentTemperament, "ai-near-death"), 1.5f);
                    if (hh <= 5 && spokenThisMatch.Add("human-near-death")) Say(Pick(CurrentTemperament, "human-near-death"), 1.5f);
                }
                bool humanTurn = s.Players[s._turnStateStack.Peek().ActivePlayerIndex] == human;
                if (!humanTurn) humanTurnSince = -1f;
                else if (humanTurnSince < 0f) humanTurnSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - humanTurnSince > 50f && spokenThisMatch.Add("slow-" + human.QueryAttribute(AttributeType.TurnCount).Value))
                    Say(Pick(CurrentTemperament, "slow-human"), 0f);
                lastHuman = hh; lastAi = ah; lastHumanBoard = hb; lastAiBoard = ab;
            }
        }

        private static int Health(Simulation s, PlayerState p)
        {
            ServerCard avatar = s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, p.NetId, GameZoneName.AvatarBoard);
            return avatar == null ? 30 : avatar.CurrentHealth;
        }

        private static IEnumerator Later(float seconds, UserEmoteType type)
        {
            yield return new WaitForSeconds(seconds);
            try { AiEmote(type); } catch (Exception e) { Debug.LogWarning("OFFLINE EMOTES: AI emote: " + e.Message); }
        }
    }
}
