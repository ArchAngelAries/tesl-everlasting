using System;
using System.Collections.Generic;
using System.IO;
using FullSerializer;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mod AI opponents (<mod>/opponents/opponents.json, portraits beside it): an in-game AI character with a name, a title line,
    // a square head-and-shoulders portrait like the game's AIPortraits (an AiOpponent entry with its Image; "avatar:<slug>" uses
    // that mod avatar's round image), a deck ("practice" =
    // the game's practice AI decks, "random", or any mod/original deck by name), a difficulty and an emote personality
    // (Friendly, Cocky, Playful, Stoic). Opponents play:
    //   - in Practice: chosen in F10 ("Practice opponent"), or - with "Mod opponents and decks in random Practice" on - now
    //     and then instead of the game's practice opponents (a practice AI may also play a random mod deck);
    //   - in game modes and story battles that name them ("ai": "<mod>/<slug>"), which then default to their name, portrait,
    //     deck, difficulty and personality.
    public static class OfflineOpponents
    {
        public sealed class Opponent
        {
            public string Key, Mod, Slug, Name, Title, Deck = "practice", Difficulty = "chosen", Temperament; public int RandomSize = 30;
            public bool Practice = true; public int AiOpponentId; public string UniqueName;
        }
        public static readonly List<Opponent> All = new List<Opponent>();
        private static readonly Dictionary<string, string> temperamentByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Random rng = new System.Random();
        public static Opponent Current;   // the opponent of the match being started/played (for logs and tests)
        public static string ForcedKey;   // diagnostics only: practice always uses this opponent

        private static string S(Dictionary<string, fsData> d, string k, string def) { return d != null && d.ContainsKey(k) && d[k].IsString ? d[k].AsString : def; }

        public static void Load(GameConfiguration config, string modId, string dir, List<string> problems)
        {
            string file = Path.Combine(Path.Combine(dir, "opponents"), "opponents.json");
            if (!File.Exists(file)) return;
            foreach (fsData e in fsJsonParser.Parse(File.ReadAllText(file)).AsDictionary["opponents"].AsList)
            {
                Dictionary<string, fsData> d = e.AsDictionary;
                Opponent o = new Opponent { Mod = modId, Slug = S(d, "slug", "opponent"), Name = S(d, "name", "Opponent"), Title = S(d, "title", ""),
                    Deck = S(d, "deck", "practice"), Difficulty = S(d, "difficulty", "chosen"), Temperament = S(d, "temperament", null) };
                o.Key = modId + "/" + o.Slug;
                if (d.ContainsKey("random_size") && d["random_size"].IsInt64) o.RandomSize = (int)d["random_size"].AsInt64;
                o.Practice = !d.ContainsKey("practice") || !d["practice"].IsBool || d["practice"].AsBool;
                string portrait = S(d, "portrait", null), image = null;
                if (!string.IsNullOrEmpty(portrait))
                {
                    if (portrait.StartsWith("asset:")) image = portrait.Substring(6);
                    else if (portrait.StartsWith("avatar:"))
                    {   // one of this mod's avatars (its slug or name)
                        string want = portrait.Substring(7);
                        AvatarData a = OfflineMods.Avatars.Find(delegate(AvatarData x) { return x.ResourceId.UniqueName == "mod." + modId + ".avatar." + want; });
                        if (a != null) image = a.CircularImage; else problems.Add(o.Key + ": no avatar " + want + " in this mod");
                    }
                    else
                    {
                        string png = Path.Combine(Path.Combine(dir, "opponents"), portrait);
                        if (File.Exists(png)) { image = OfflineMods.ImagePrefix + modId + "/opponents/" + Path.GetFileNameWithoutExtension(portrait); OfflineMods.AddArt(image, png); }
                        else problems.Add(o.Key + ": portrait missing: " + portrait);
                    }
                }
                AiOpponent ai = new AiOpponent { ResourceId = new UniqueResourceId { Id = OfflineMods.StableId("opponent:" + o.Key), UniqueName = "MOD:opponent:" + o.Key },
                    ContentPackIndex = 0, TitleRef = new UniqueResourceReference() };
                ai.DisplayNameKey = new LocalizedStringKey { RawKey = "mod.opponent." + o.Key + ".name" };
                OfflineMods.SetText(ai.DisplayNameKey.RawKey, o.Name);
                ai.Image = image ?? (config.AiOpponents.Count > 0 ? config.AiOpponents[0].Image : null);
                config.AiOpponents.RemoveAll(delegate(AiOpponent x) { return x.ResourceId.Id == ai.ResourceId.Id; });
                config.AiOpponents.Add(ai);
                o.AiOpponentId = ai.ResourceId.Id; o.UniqueName = ai.ResourceId.UniqueName;
                if (!string.IsNullOrEmpty(o.Temperament)) temperamentByName[o.Name] = o.Temperament;
                All.RemoveAll(delegate(Opponent x) { return x.Key == o.Key; });
                All.Add(o);
            }
        }

        public static Opponent Find(string key) { return string.IsNullOrEmpty(key) ? null : All.Find(delegate(Opponent o) { return o.Key == key || string.Equals(o.Name, key, StringComparison.OrdinalIgnoreCase); }); }

        // Practice: the opponent chosen in F10, else (with the random option on) sometimes one of the mods' practice opponents.
        public static Opponent PickForPractice()
        {
            Opponent chosen = Find(ForcedKey ?? OfflineGraphics.Settings.opponent);
            if (chosen != null) return chosen;
            if (!OfflineGraphics.Settings.modOpponents) return null;
            List<Opponent> pool = All.FindAll(delegate(Opponent o) { return o.Practice; });
            if (pool.Count == 0 || rng.NextDouble() >= pool.Count / (pool.Count + 3.0)) return null;
            return pool[rng.Next(pool.Count)];
        }

        // With the random option on, a practice AI sometimes plays one of the mods' decks.
        public static DeckInfo RandomModDeckForAi()
        {
            if (!OfflineGraphics.Settings.modOpponents || OfflineMods.Decks.Count == 0 || rng.NextDouble() >= 0.35) return null;
            return OfflineMods.Decks[rng.Next(OfflineMods.Decks.Count)];
        }

        public static string TemperamentOf(string playerName)
        {
            string t;
            return playerName != null && temperamentByName.TryGetValue(playerName, out t) ? t : null;
        }

        public static List<int> DeckCards(GameConfiguration config, Opponent o)
        {
            OfflineModes.Side side = new OfflineModes.Side { Deck = o.Deck, RandomSize = o.RandomSize };
            return OfflineModes.DeckCards(config, side);
        }

        public static string Next(string current)
        {
            List<string> keys = new List<string> { "" };
            foreach (Opponent o in All) keys.Add(o.Key);
            int i = keys.IndexOf(current ?? "");
            return keys[(i + 1) % keys.Count];
        }
    }
}
