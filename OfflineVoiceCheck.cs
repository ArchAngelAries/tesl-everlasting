using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Hidden diagnostic (--tesl-offline-native-menu=voicecheck): after mod voice audio is attached, checks that
    //  - every mod card line and shout level resolves to its own decoded clip (file named for the card),
    //  - a sample of mod card lines plays through the client's own PlayCardAudio as the card's clip,
    //  - the original cards those mod cards were made from still play their own sound (no swap),
    //  - every mod avatar emote line loads, and no original avatar package gained mod audio,
    //  - each mod shout card plays its own clip at shout levels 1-3 through PlayFxSfxAudio.
    // Results: exports/voice-check.tsv (one row per check) and a summary line in the log.
    public static class OfflineVoiceCheck
    {
        private static StringBuilder tsv;
        private static Dictionary<string, int[]> tally;

        private static void Row(string cat, string item, string slot, string file, bool ok, string detail)
        {
            int[] t;
            if (!tally.TryGetValue(cat, out t)) tally[cat] = t = new int[2];
            t[ok ? 0 : 1]++;
            tsv.Append(cat).Append('\t').Append(item).Append('\t').Append(slot).Append('\t').Append(file == null ? "" : Path.GetFileName(file))
                .Append('\t').Append(ok ? "ok" : "FAIL").Append('\t').Append((detail ?? "").Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
        }

        public static IEnumerator Run(string exportDirectory)
        {
            float until = Time.realtimeSinceStartup + 900f;
            while (!OfflineAudio.PreloadDone && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(2f);
            GameConfiguration config = GameManager.Instance.Config;
            tsv = new StringBuilder("category\titem\tslot\tfile\tresult\tdetail\n");
            tally = new Dictionary<string, int[]>();
            Dictionary<int, Card> byId = new Dictionary<int, Card>();
            foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards) if (c != null) byId[c.ResourceId.Id] = c;

            // 1. every mod card line resolves to its own decoded clip
            foreach (OfflineAudio.ModCard mc in OfflineAudio.Cards.Values)
                foreach (KeyValuePair<string, string> line in mc.Lines)
                {
                    AudioClip clip = OfflineAudio.ClipFor(line.Value);
                    string name = Path.GetFileNameWithoutExtension(line.Value).ToLowerInvariant();
                    bool own = name.StartsWith(mc.Slug.ToLowerInvariant());
                    Row("card-clip", mc.Slug, line.Key, line.Value, clip != null && clip.length > 0.05f && own,
                        clip == null ? "not decoded" : !own ? "file not named for this card" : clip.length.ToString("0.0") + " s");
                }

            // 2. a sample of mod card lines through the client's own PlayCardAudio, and 3. their original templates untouched
            int played = 0;
            HashSet<int> templatesDone = new HashSet<int>();
            List<OfflineAudio.ModCard> withPlay = new List<OfflineAudio.ModCard>();
            foreach (OfflineAudio.ModCard mc in OfflineAudio.Cards.Values) if (mc.Lines.ContainsKey("Play")) withPlay.Add(mc);
            int step = Math.Max(1, withPlay.Count / 25);
            for (int i = 0; i < withPlay.Count && played < 25; i += step)
            {
                OfflineAudio.ModCard mc = withPlay[i];
                Card card;
                if (!byId.TryGetValue(mc.Id, out card) || string.IsNullOrEmpty(card.PlayCardVO)) continue;
                played++;
                int linesBefore = OfflineAudio.CardLines;
                bool finished = false;
                AudioPlaybackManager.PlayCardAudio(card, card.PlayCardVO, delegate { finished = true; }, null);
                float t0 = Time.realtimeSinceStartup;
                while (!finished && Time.realtimeSinceStartup < t0 + 8f) yield return null;
                Row("card-play", mc.Slug, "Play", mc.Lines["Play"], OfflineAudio.CardLines == linesBefore + 1,
                    "own line used " + (OfflineAudio.CardLines - linesBefore) + "x; " + (OfflineAudio.LastSwap ?? "fallback"));
                Card template = OfflineAudio.Template(mc);
                if (template == null || OfflineAudio.Cards.ContainsKey(template.ResourceId.Id) || string.IsNullOrEmpty(template.PlayCardVO) || !templatesDone.Add(template.ResourceId.Id)) continue;
                OfflineAudio.Recording = new List<string>();
                int swapsBefore = OfflineAudio.Swaps, ownBefore = OfflineAudio.CardLines, fallbacksBefore = OfflineAudio.Fallbacks;
                finished = false;
                AudioPlaybackManager.PlayCardAudio(template, template.PlayCardVO, delegate { finished = true; }, null);
                t0 = Time.realtimeSinceStartup;
                while (!finished && Time.realtimeSinceStartup < t0 + 8f) yield return null;
                bool untouched = OfflineAudio.CardLines == ownBefore && OfflineAudio.Fallbacks == fallbacksBefore
                    && (OfflineAudio.Swaps == swapsBefore || OfflineAudio.Replacements.Count > 0);
                bool modClip = false;
                foreach (string rec in OfflineAudio.Recording) if (rec.Contains("\tmod:")) modClip = true;
                Row("original-untouched", template.Name + " (template of " + mc.Slug + ")", "Play", null, untouched && !modClip,
                    "played " + string.Join(" | ", OfflineAudio.Recording.ToArray()));
                OfflineAudio.Recording = null;
            }

            // 4. mod avatar emote lines load; original avatar packages carry no mod audio
            AvatarCollection col = config.AvatarCollection;
            foreach (AvatarEmotePackage p in OfflineMods.EmotePackages)
                foreach (AvatarEmoteData d in p.EmoteData)
                {
                    if (d == null || string.IsNullOrEmpty(d.EmoteAudio) || !d.EmoteAudio.StartsWith(OfflineEmotes.AudioPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    AudioClip clip = null; string error = null;
                    try { clip = OfflineEmotes.Clip(d.EmoteAudio); } catch (Exception e) { error = e.Message; }
                    Row("avatar-emote", p.ResourceId.UniqueName, d.EmoteType.ToString(), d.EmoteAudio, clip != null && clip.length > 0.05f,
                        clip == null ? (error ?? "not loaded") : clip.length.ToString("0.0") + " s");
                }
            bool originalsClean = true;
            foreach (AvatarEmotePackage p in col.EmotePackages)
            {
                if (p == null || OfflineMods.EmotePackages.Contains(p)) continue;
                int modLines = 0;
                foreach (AvatarEmoteData d in p.EmoteData)
                    if (d != null && !string.IsNullOrEmpty(d.EmoteAudio) && d.EmoteAudio.StartsWith(OfflineEmotes.AudioPrefix, StringComparison.OrdinalIgnoreCase)) modLines++;
                if (modLines > 0) { originalsClean = false; Row("original-avatar-untouched", p.ResourceId.UniqueName, "", null, false, modLines + " mod lines in an original package"); }
            }
            if (originalsClean) Row("original-avatar-untouched", "all original packages", "", null, true, "no mod audio");

            // 5. shout levels through PlayFxSfxAudio
            foreach (OfflineAudio.ModCard mc in OfflineAudio.Cards.Values)
            {
                Card card;
                if (!byId.TryGetValue(mc.Id, out card)) continue;
                for (int level = 1; level <= 3; level++)
                {
                    string file;
                    if (!mc.Lines.TryGetValue("Shout" + level, out file)) continue;
                    int before = OfflineAudio.ShoutSwaps;
                    OfflineAudio.NoteShoutFx(card, "Card_UnrelentingForce_Lvl" + level + "_Runes");
                    AudioPlaybackManager.PlayFxSfxAudio("unrelenting_force_0" + level);
                    bool ok = OfflineAudio.ShoutSwaps == before + 1 && OfflineAudio.LastShout != null && OfflineAudio.LastShout.EndsWith(Path.GetFileName(file));
                    Row("shout-level", mc.Slug, "Shout" + level, file, ok, OfflineAudio.LastShout);
                    AudioClip clip = OfflineAudio.ClipFor(file);
                    yield return new WaitForSeconds(clip == null ? 1f : Math.Max(1f, clip.length + 0.3f));
                }
            }

            File.WriteAllText(Path.Combine(exportDirectory, "voice-check.tsv"), tsv.ToString());
            StringBuilder summary = new StringBuilder("OFFLINE VOICE CHECK:");
            foreach (KeyValuePair<string, int[]> t in tally) summary.Append(' ').Append(t.Key).Append(' ').Append(t.Value[0]).Append(" ok/").Append(t.Value[1]).Append(" fail;");
            Debug.Log(summary.ToString());
        }
    }
}
