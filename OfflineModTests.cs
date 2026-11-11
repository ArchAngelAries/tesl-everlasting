using System;
using System.Collections.Generic;
using AssetBundles;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // The fixture mod (fixtures/mods/sample-skyrim, authored with the TESL Modding Suite CLI): three new cards built from
    // templates (Renowned Legate's, Haunting Spirit's and Crushing Blow's rules) and a 51-card deck (the client requires 50+).
    public static class OfflineModTests
    {
        private const string Mod = "sample-skyrim";

        public static void Run(GameConfiguration config, Action<string, Action> test, string output)
        {
            test("mod-avatars-picker-pages-and-images", delegate {
                // Original catalog for the suite (voice names, round-image paths): exports/avatar-catalog.tsv.
                AvatarCollection col = config.AvatarCollection;
                System.Text.StringBuilder tsv = new System.Text.StringBuilder("unique_name\tid\trace\timage\tcircular_image\temote_package\n");
                foreach (AvatarData a in col.Avatars)
                {
                    AvatarRaceData race = col.Races.Find(delegate(AvatarRaceData r) { return r.ResourceId.Id == a.RaceId.resourceId; });
                    tsv.Append(a.ResourceId.UniqueName).Append('\t').Append(a.ResourceId.Id).Append('\t').Append(race == null ? "?" : race.ResourceId.UniqueName)
                       .Append('\t').Append(a.Image).Append('\t').Append(a.CircularImage).Append('\t').Append(a.EmotePackage.resourceId).Append('\n');
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "avatar-catalog.tsv"), tsv.ToString());
                Require(OfflineMods.Avatars.Count == 3 && OfflineMods.AvatarPages.Count == 1, "Expected 3 mod avatars on 1 page, got "
                    + OfflineMods.Avatars.Count + " on " + OfflineMods.AvatarPages.Count);
                AvatarRaceData page = OfflineMods.AvatarPages[0];
                Require(col.Races.Contains(page) && OfflineMods.Text(page.Name.RawKey) == "Skyrim Heroes", "Avatar page missing or unnamed.");
                AvatarData housecarl = OfflineMods.Avatars[0], breton = col.Avatars.Find(delegate(AvatarData a) { return a.ResourceId.UniqueName == "AvatarBreton1"; });
                Require(housecarl.RaceId.resourceId == page.ResourceId.Id && col.Avatars.Contains(housecarl), "Mod avatar not on its page.");
                // Housecarl has its own emote lines on top of AvatarBreton1's voice (checked in mod-emotes-...); Draugr only borrows.
                AvatarData draugr = OfflineMods.Avatars[1];
                AvatarData fallback = col.Avatars.Find(delegate(AvatarData a) { return a.ResourceId.Id == col.FallbackAvatar.resourceId; });
                Require(breton != null && housecarl.EmotePackage.resourceId != 0 && fallback != null && draugr.EmotePackage.resourceId == fallback.EmotePackage.resourceId,
                    "Mod avatar voices wrong (Draugr should borrow the fallback avatar's voice).");
                AssetBundleLoadAssetOperation op = null;
                Require(!OfflineMods.LoadAssetPrefix(housecarl.Image, typeof(Sprite), ref op) && op != null, "Avatar portrait not served.");
                Sprite s = op.GetAsset<Sprite>();
                Require(s.texture.width == 572 && Mathf.Abs(s.bounds.size.x - 286f / 94.70199f) < 0.01f, "Avatar portrait not at the original size: "
                    + s.texture.width + "px, " + s.bounds.size.x + " units");
                AssetBundleLoadAssetOperation circle = null;
                Require(!OfflineMods.LoadAssetPrefix(housecarl.CircularImage, typeof(Sprite), ref circle) && circle.GetAsset<Sprite>().texture.width == 512,
                    "Round avatar image not served.");
                HashSet<int> ids = new HashSet<int>();
                foreach (AvatarData a in col.Avatars) Require(ids.Add(a.ResourceId.Id), "Duplicate avatar id " + a.ResourceId.Id);
            });
            test("mod-emotes-lines-voice-and-ai-voice", delegate {
                AvatarCollection col = config.AvatarCollection;
                AvatarData housecarl = OfflineMods.Avatars.Find(delegate(AvatarData a) { return a.ResourceId.UniqueName == "mod.sample-skyrim.avatar.housecarl"; });
                AvatarData breton = col.Avatars.Find(delegate(AvatarData a) { return a.ResourceId.UniqueName == "AvatarBreton1"; });
                AvatarEmotePackage own = OfflineEmotes.Package(col, housecarl.EmotePackage.resourceId), voice = OfflineEmotes.Package(col, breton.EmotePackage.resourceId);
                Require(own != null && own != voice && OfflineMods.EmotePackages.Contains(own), "Housecarl has no emote package of its own.");
                AvatarEmoteData hello = OfflineEmotes.LineFor(own, UserEmoteType.Hello), taunt = OfflineEmotes.LineFor(own, UserEmoteType.Taunt);
                Require(hello != null && OfflineMods.Text(hello.EmoteString.RawKey) == "My Jarl's hall is open to you." && OfflineEmotes.HasAudio(hello.EmoteAudio),
                    "Housecarl Hello line or voice missing.");
                Require(taunt != null && taunt.EmoteAudio == "" && OfflineMods.Text(taunt.EmoteString.RawKey).StartsWith("I am sworn"), "Text-only Taunt line wrong.");
                AudioClip clip = OfflineEmotes.Clip(hello.EmoteAudio);
                Require(clip != null && Mathf.Abs(clip.length - 0.8f) < 0.01f && clip.frequency == 22050, "Mod voice clip not decoded: " + (clip == null ? "null" : clip.length + " s"));
                AvatarEmoteData sorry = OfflineEmotes.LineFor(own, UserEmoteType.Sorry), bretonSorry = OfflineEmotes.LineFor(voice, UserEmoteType.Sorry);
                Require(bretonSorry == null || sorry != null && sorry.EmoteString.RawKey == bretonSorry.EmoteString.RawKey && sorry.EmoteAudio == bretonSorry.EmoteAudio,
                    "Unset emote did not keep the borrowed voice's line.");
                // AI voice: its avatar's package when it plays a known avatar (mod avatars included), else a stable original voice.
                Require(OfflineEmotes.VoiceFor(new MatchPlayerData { isAIPlayer = true, AvatarUniqueName = housecarl.ResourceId.UniqueName }) == own, "AI with a mod avatar did not use its lines.");
                AvatarEmotePackage a1 = OfflineEmotes.VoiceFor(new MatchPlayerData { isAIPlayer = true, AvatarUniqueName = "AiOpponent_Unknown", Name = "Ulfric" });
                AvatarEmotePackage a2 = OfflineEmotes.VoiceFor(new MatchPlayerData { isAIPlayer = true, AvatarUniqueName = "AiOpponent_Unknown", Name = "Ulfric" });
                for (int n = 0; n < 400; n++)
                {   // an opponent without a player avatar never speaks a mod avatar's lines
                    AvatarEmotePackage any = OfflineEmotes.VoiceFor(new MatchPlayerData { isAIPlayer = true, AvatarUniqueName = "AiOpponent_Unknown", Name = "Foe " + n });
                    Require(any != null && !OfflineMods.EmotePackages.Contains(any), "an unknown opponent used mod avatar lines: " + (any == null ? "none" : any.ResourceId.UniqueName));
                }
                Require(a1 != null && a1 == a2 && a1.EmoteData.Count > 0, "AI without a player avatar got no stable voice.");
                // an AiOpponent whose portrait is an original avatar's picture speaks with that avatar's voice; one named for a race uses that race
                List<AiOpponent> opps = GameManager.Instance.Config.AiOpponents;
                AvatarData khajiit = col.Avatars.Find(delegate(AvatarData x) { return x != null && x.ResourceId.UniqueName == "AvatarKhajiit2"; });
                AiOpponent pictured = khajiit == null || opps == null ? null : opps.Find(delegate(AiOpponent x) { return x != null && !string.IsNullOrEmpty(x.Image) && (x.Image.EndsWith("/" + khajiit.CircularImage.Substring(khajiit.CircularImage.LastIndexOf('/') + 1)) || x.Image == khajiit.CircularImage); });
                Require(pictured != null && OfflineEmotes.VoiceFor(new MatchPlayerData { isAIPlayer = true, AvatarUniqueName = pictured.ResourceId.UniqueName, Name = "x" }) == OfflineEmotes.Package(col, khajiit.EmotePackage.resourceId),
                    "an AI showing a Khajiit avatar's portrait did not use that avatar's voice");
                int raced = 0, raceRight = 0;
                foreach (AiOpponent o in opps)
                {
                    if (o == null || o.ResourceId.UniqueName == null) continue;
                    string lower = ((o.Image ?? "") + " " + o.ResourceId.UniqueName).ToLowerInvariant();
                    if (!lower.Contains("nord") || lower.Contains("arena_nord")) continue;
                    raced++;
                    AvatarEmotePackage v = OfflineEmotes.VoiceFor(new MatchPlayerData { isAIPlayer = true, AvatarUniqueName = o.ResourceId.UniqueName, Name = "y" });
                    if (col.Avatars.Exists(delegate(AvatarData a) { return a != null && a.ResourceId.UniqueName.StartsWith("AvatarNord") && OfflineEmotes.Package(col, a.EmotePackage.resourceId) == v; })) raceRight++;
                }
                Require(raced == 0 || raceRight == raced, "Nord-named AI opponents with non-Nord voices: " + (raced - raceRight) + " of " + raced);
                int portrait = 0, byRace = 0, none = 0;
                foreach (AiOpponent o in opps)
                {
                    if (o == null || o.ResourceId.UniqueName == null) continue;
                    MatchPlayerData md = new MatchPlayerData { isAIPlayer = true, AvatarUniqueName = o.ResourceId.UniqueName, Name = "z" };
                    AvatarEmotePackage v = OfflineEmotes.VoiceForPortrait(col, md, 0);
                    if (v == null) none++; else if (col.Avatars.Exists(delegate(AvatarData a) { return a != null && !string.IsNullOrEmpty(o.Image) && (a.CircularImage == o.Image || a.Image == o.Image || (a.CircularImage ?? "").EndsWith("/" + o.Image.Substring(o.Image.LastIndexOf('/') + 1))); })) portrait++; else byRace++;
                }
                Debug.Log("OFFLINE EMOTES: AI opponent voices - " + portrait + " by portrait, " + byRace + " by race, " + none + " without a race (name-picked original voice)");
            });
            test("mod-cardbacks-registered-owned-and-sold", delegate {
                const string free = "mod.sample-skyrim.cardback.skyrim-heroes", paid = "mod.sample-skyrim.cardback.greybeard-shout";
                Require(OfflineMods.CardBackKeys.Contains(free) && OfflineMods.CardBackKeys.Contains(paid), "Mod card backs not loaded.");
                CardBackData freeData = config.CardSharedGraphics.FindCardBackData(free), paidData = config.CardSharedGraphics.FindCardBackData(paid);
                Require(freeData != null && paidData != null && (int)freeData.Type >= 100000 && freeData.Type != paidData.Type && freeData.VisualAsset.StartsWith("Mods/"),
                    "Mod card backs not in the client's card back lookup.");
                Require(OfflineMods.Text(paidData.NameTextKey.RawKey) == "Greybeard Shout", "Card back name text missing.");
                AssetBundles.AssetBundleLoadAssetOperation op = null;
                Require(!OfflineMods.LoadAssetPrefix(freeData.VisualAsset, typeof(Material), ref op) && op is OfflineMods.ModCardBackOperation, "Card back material not served.");
                OfflinePlayerStateData d = new OfflinePlayerStateData { gold = 1000 };
                SparkleServices.GenericInventory.CategoryData backs = OfflineCosmetics.Categories(d).Find(delegate(SparkleServices.GenericInventory.CategoryData c) { return c.category == "cardback"; });
                Require(backs.available.Contains(free) && backs.locked.Contains(paid) && OfflineCosmetics.CanSelect(d, "cardback", free) && !OfflineCosmetics.CanSelect(d, "cardback", paid),
                    "Free mod card back not available or paid one not locked.");
                OfflineGoldStore.Offer offer = OfflineGoldStore.Offers(d).Find(delegate(OfflineGoldStore.Offer o) { return o.CardBack == paid; });
                Require(offer != null && offer.Gold == 400 && offer.Image == paidData.VisualAsset + "-store" && OfflineMods.HasArt(offer.Image)
                    && !OfflineGoldStore.Offers(d).Exists(delegate(OfflineGoldStore.Offer o) { return o.CardBack == free; }), "Mod card back store offer wrong.");
                Require(OfflineGoldStore.Buy(d, offer.Key) && d.gold == 600 && OfflineCosmetics.CanSelect(d, "cardback", paid), "Mod card back purchase failed.");
            });
            test("mod-rule-editor-composed-card-plays", delegate {
                // Whiterun Wolfcaller (suite rule editor): Guard; Fiery Imp's attack trigger at 3 damage; Pack Wolf's Summon at 2 tokens.
                const int Wolfcaller = -219964603, YoungWolf = 858815917;
                Card card = config.FindCardById(Wolfcaller);
                Require(card != null && card.TriggeredEffects.Count == 2, "Composed card missing or without its two abilities.");
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, 620729069))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard wolfcaller = f.Create(Wolfcaller, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me); f.PlayCreatureLane(wolfcaller, 0, me);
                    int wolves = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == YoungWolf; }, s.Players[me].NetId, GameZoneName.CreatureBoard).Count;
                    Require(wolfcaller.CurrentGameZoneName == GameZoneName.CreatureBoard && wolves == 2, "Summon did not make two Young Wolves (" + wolves + ").");
                    Require(wolfcaller.QueryAttribute(AttributeType.Taunt).Value == 1, "Composed card lost Guard.");
                }
            });
            test("mod-boards-loaded", delegate {
                OfflineBoards.Board board = OfflineBoards.Find("sample-skyrim/greenwood-table");
                Require(board != null && board.Base == PlaymatType.Original && !board.Replace && System.IO.File.Exists(board.Image), "Sample mod board not loaded.");
                Require(OfflineBoards.MainTexture[board.Base] == "Playmat_01_D" && OfflineBoards.Find("nope/none") == null, "Board lookup wrong.");
            });
            test("mod-audio-loaded", delegate {
                OfflineAudio.ModCard wolf = null;
                foreach (OfflineAudio.ModCard c in OfflineAudio.Cards.Values) if (c.Slug == "whiterun-wolfcaller") wolf = c;
                Require(wolf != null && wolf.TemplateId == 775516546 && wolf.Lines.Count == 2 && wolf.Lines.ContainsKey("Play") && wolf.Lines.ContainsKey("Attack"),
                    "Mod card voice lines not registered.");
                Require(OfflineAudio.Template(wolf) != null, "Mod card template not found.");
                // shout levels: a mod card's own level clip replaces the level sound of the borrowed effect, for its cast only
                Card wolfCard = null, original = null;
                foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards)
                {
                    if (c != null && c.ResourceId.Id == wolf.Id) wolfCard = c;
                    if (c != null && c.ResourceId.Id == wolf.TemplateId) original = c;
                }
                string clipFile = wolf.Lines["Play"];
                wolf.Lines["Shout2"] = clipFile;
                try
                {
                    OfflineAudio.NoteShoutFx(wolfCard, "Card_UnrelentingForce_Lvl2_Runes");
                    Require(OfflineAudio.ShoutClipFor("unrelenting_force_02") == clipFile, "Mod shout level 2 clip not chosen.");
                    Require(OfflineAudio.ShoutClipFor("unrelenting_force_01") == null && OfflineAudio.ShoutClipFor("shout_explosion") == null, "Wrong sound swapped for a mod shout.");
                    if (OfflineAudio.ClipFor(clipFile) != null && OfflineAudio.Host != null)
                    {   // through the client's own effect-sound call: the swap plays our clip instead
                        int before = OfflineAudio.ShoutSwaps;
                        AudioPlaybackManager.PlayFxSfxAudio("unrelenting_force_02");
                        Require(OfflineAudio.ShoutSwaps == before + 1, "PlayFxSfxAudio did not swap the mod shout sound.");
                        Debug.Log("OFFLINE AUDIO: shout swap through PlayFxSfxAudio verified (" + OfflineAudio.LastShout + ")");
                        OfflineAudio.NoteShoutFx(wolfCard, "Card_UnrelentingForce_Lvl2_Runes");
                    }
                    else Debug.Log("OFFLINE AUDIO: shout live swap skipped (clip not loaded yet)");
                    OfflineAudio.NoteShoutFx(wolfCard, "Card_UnrelentingForce_Lvl3_Runes");
                    Require(OfflineAudio.ShoutClipFor("unrelenting_force_03") == null, "A level without its own clip was swapped.");
                    OfflineAudio.NoteShoutFx(wolfCard, "Card_UnrelentingForce_Lvl2_Runes");
                    OfflineAudio.NoteShoutFx(original, "Card_UnrelentingForce_Lvl2_Runes");
                    Require(OfflineAudio.ShoutClipFor("unrelenting_force_02") == null, "An original shout played a mod card's clip.");
                    OfflineAudio.NoteShoutFx(wolfCard, "Card_Something_Else");
                    Require(OfflineAudio.ShoutLevelOf("Card_UnrelentingForce_Lvl1_Runes") == 1 && OfflineAudio.ShoutLevelOf("Card_UnrelentingForce_Breath") == 0, "Shout level parse wrong.");
                }
                finally { wolf.Lines.Remove("Shout2"); OfflineAudio.NoteShoutFx(null, "lvl1"); }
                Require(OfflineAudio.Replacements.ContainsKey("vile_imp_attack_02") && OfflineAudio.Find("vile_imp_attack", null) != null
                    && OfflineAudio.Find("vile_imp_hit", null) == null, "Sound replacement lookup wrong.");
            });
            test("mod-game-modes", delegate {
                OfflineModes.Mode m = OfflineModes.Find("sample-skyrim/skyrim-brawl");
                Require(m != null && m.LaneTypes.Count == 2 && m.LaneTypes[0] == "Plunder" && m.LaneTypes[1] == "Windy", "Sample mode lanes not loaded.");
                Require(m.Player.Health == 25 && m.Opponent.Magicka == 3 && m.Player.Runes == 3 && m.DrawPerTurn == 1 && m.RandomCreatureEvery == 3 && m.RewardGold == 50
                    && m.First == "player" && m.Player.Cards["lane1"][0] == "Whiterun Wolfcaller", "Sample mode settings wrong.");
                Require(Array.IndexOf(OfflineModes.LaneChoices(), CardLaneType.Champions_Arena) >= 0 && OfflineModes.LaneChoices().Length == 50, "Arena lanes missing from mode lane choices (" + OfflineModes.LaneChoices().Length + ").");
                Require(OfflineModes.FindCard(config, "Whiterun Wolfcaller") != null && OfflineModes.FindCard(config, "Bleak Falls Draugr") != null, "Mode cards not found.");
                List<int> random = OfflineModes.RandomDeck(config, 30);
                Require(random.Count == 30, "Random deck has " + random.Count + " cards.");
                MatchConfiguration.StartInfo info = new MatchConfiguration.StartInfo();
                OfflineModes.AddLanes(info, m);
                Require(info.OptionalCreatureBoardOverride[0] != null && info.OptionalCreatureBoardOverride[0].LaneOverrides.Count == 2
                    && info.OptionalCreatureBoardOverride[1].LaneOverrides.Count == 2, "Mode lanes not built.");
            });
            test("fair-play-loopback-only", delegate {
                Require(OfflineFairPlay.IsLocal("127.0.0.1") && OfflineFairPlay.IsLocal("localhost") && OfflineFairPlay.IsLocal("::1")
                    && !OfflineFairPlay.IsLocal("10.0.0.5") && !OfflineFairPlay.IsLocal("game.example.com"), "Loopback check wrong.");
            });
            test("mod-compat-plan", delegate {
                string root = System.IO.Path.Combine(output, "compat-test");
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
                Func<string, string, string> mod = delegate(string folder, string json)
                {
                    string d = System.IO.Path.Combine(root, folder); System.IO.Directory.CreateDirectory(d);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(d, "mod.json"), json); return d;
                };
                List<string> dirs = new List<string> {
                    mod("b", "{\"id\":\"b\",\"version\":\"1.0.0\",\"dependencies\":[{\"id\":\"a\",\"version\":\">=1.2\"}]}"),
                    mod("a", "{\"id\":\"a\",\"version\":\"1.4.0\"}"),
                    mod("a-copy", "{\"id\":\"a\",\"version\":\"9.0.0\"}"),
                    mod("future", "{\"id\":\"future\",\"requires_engine\":999}"),
                    mod("c", "{\"id\":\"c\",\"conflicts\":[\"a\"]}"),
                    mod("d", "{\"id\":\"d\",\"dependencies\":[\"missing\"]}"),
                    mod("e", "{\"id\":\"e\",\"dependencies\":[{\"id\":\"a\",\"version\":\"^2.0\"}]}"),
                    mod("f", "{\"id\":\"f\",\"dependencies\":[\"d\"]}"),
                    mod("g", "{\"id\":\"g\",\"load_before\":[\"b\"]}") };
                List<string> problems = new List<string>();
                List<OfflineCompat.Manifest> plan = OfflineCompat.Plan(dirs, problems);
                string order = string.Join(",", plan.ConvertAll(delegate(OfflineCompat.Manifest m) { return m.Id; }).ToArray());
                Require(order == "a,g,b", "Load plan wrong: " + order + " | " + string.Join(" | ", problems.ToArray()));
                Require(problems.Count == 6, "Expected 6 problems (copy, engine, conflict, missing, version, chained), got " + problems.Count + ": " + string.Join(" | ", problems.ToArray()));
                Require(OfflineCompat.Satisfies("1.4.0", ">=1.2 <2") && !OfflineCompat.Satisfies("2.0", "<2") && OfflineCompat.Satisfies("1.9.3", "^1.2")
                    && !OfflineCompat.Satisfies("1.1", "^1.2") && OfflineCompat.Satisfies("1.2.7", "~1.2") && OfflineCompat.Satisfies("1.2.7", "1.2")
                    && !OfflineCompat.Satisfies("1.3", "=1.2") && OfflineCompat.Satisfies("0.1", "*"), "Version ranges wrong.");
                System.IO.Directory.Delete(root, true);
            });
            test("mod-opponents", delegate {
                OfflineOpponents.Opponent elder = OfflineOpponents.Find("sample-skyrim/greybeard-elder"), draugr = OfflineOpponents.Find("sample-skyrim/draugr-warlord");
                Require(elder != null && draugr != null && elder.Difficulty == "Adept" && elder.Temperament == "Stoic", "Sample opponents not loaded.");
                AiOpponent ai = config.AiOpponents.Find(delegate(AiOpponent a) { return a.ResourceId.Id == elder.AiOpponentId; });
                Require(ai != null && ai.ResourceId.UniqueName == elder.UniqueName && OfflineMods.Text(ai.DisplayNameKey.RawKey) == "Greybeard Elder" && OfflineMods.HasArt(ai.Image), "Opponent AiOpponent entry wrong.");
                AiOpponent dai = config.AiOpponents.Find(delegate(AiOpponent a) { return a.ResourceId.Id == draugr.AiOpponentId; });
                Require(dai != null && dai.Image == OfflineMods.ImagePrefix + "sample-skyrim/avatar-draugr-circle", "avatar: portrait not resolved to the round image.");
                List<int> cards = OfflineOpponents.DeckCards(config, elder);
                Require(cards != null && cards.Count >= 30, "Opponent deck not resolved.");
                Require(OfflineEmotes.TemperamentFor(new MatchPlayerData { Name = "Draugr Warlord" }) == OfflineEmotes.Temperament.Cocky, "Opponent personality ignored.");
                OfflineModes.Mode brawl = OfflineModes.Find("sample-skyrim/skyrim-brawl");
                Require(brawl != null && brawl.AiRef == "sample-skyrim/draugr-warlord" && OfflineOpponents.Find(brawl.AiRef) == draugr, "Mode AI opponent link wrong.");
                OfflineGraphicsSettings g = OfflineGraphics.Settings;
                string oldOpp = g.opponent; bool oldMod = g.modOpponents;
                try {
                    g.opponent = elder.Key; g.modOpponents = false;
                    Require(OfflineOpponents.PickForPractice() == elder, "F10 practice opponent not used.");
                    g.opponent = ""; Require(OfflineOpponents.PickForPractice() == null && OfflineOpponents.RandomModDeckForAi() == null, "Mod opponents used while off.");
                    g.modOpponents = true; bool seen = false;
                    for (int i = 0; i < 200 && !seen; i++) seen = OfflineOpponents.PickForPractice() != null;
                    Require(seen, "Random practice never picks a mod opponent.");
                    Require(OfflineOpponents.Next("") == OfflineOpponents.All[0].Key && OfflineOpponents.Next(OfflineOpponents.All[OfflineOpponents.All.Count - 1].Key) == "", "F10 cycle wrong.");
                } finally { g.opponent = oldOpp; g.modOpponents = oldMod; }
            });
            test("mod-packs", delegate {
                OfflineModPacks.Pack heroes = OfflineModPacks.ForItem("modpack:sample-skyrim/skyrim-heroes-pack"), recruits = OfflineModPacks.ForBundle("cardpack_mod_sample-skyrim_nord-recruits");
                Require(heroes != null && recruits != null && heroes.Gold == 150 && heroes.Cards == 3 && OfflineMods.HasArt(heroes.Image), "Sample packs not loaded.");
                Require(heroes.Pool[0].Count == 1 && heroes.Pool[1].Count == 2 && heroes.Pool[2].Count == 1 && recruits.Weighted.Count == 3, "Pack pools wrong.");
                Require(Math.Abs(heroes.LastOdds[1] - 0.85) < 0.001 && heroes.Odds[3] == 0, "Pack odds not normalized.");
                Require(OfflinePackService.CanonicalBundle(recruits.Bundle) == recruits.Bundle, "Mod pack bundle renamed.");
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                List<int> all = new List<int>(); foreach (List<int> l in heroes.Pool) all.AddRange(l);
                for (int i = 0; i < 20; i++)
                {
                    List<KeyValuePair<int, bool>> got = OfflineModPacks.Open(d, heroes);
                    Require(got.Count == 3 && got.TrueForAll(delegate(KeyValuePair<int, bool> c) { return all.Contains(c.Key); }), "Heroes pack gave a card outside its pool.");
                    SPTESL.Card last = GameManager.Instance.GetCard(got[2].Key);
                    Require(last != null && (int)last.Rarity >= 1, "Last slot was not Rare or better.");
                }
                Dictionary<int, int> seen = new Dictionary<int, int>();
                OfflinePlayerStateData e = new OfflinePlayerStateData();
                for (int i = 0; i < 200; i++) foreach (KeyValuePair<int, bool> c in OfflineModPacks.Open(new OfflinePlayerStateData(), recruits)) { int n; seen.TryGetValue(c.Key, out n); seen[c.Key] = n + 1; }
                int wolf = OfflineModes.FindCard(config, "Whiterun Wolfcaller").ResourceId.Id, carl = OfflineModes.FindCard(config, "Jarl's Housecarl").ResourceId.Id;
                Require(seen.Count == 3 && seen[wolf] > seen[carl] * 3, "Weighted drops wrong.");
            });
            test("mod-translations", delegate {
                SPTESL.Card wolf = OfflineModes.FindCard(config, "Whiterun Wolfcaller");
                Require(wolf != null && OfflineModLocale.Languages >= 1, "Sample translations not loaded.");
                Debug.Log("OFFLINE MODS: game language names: " + OfflineModLocale.LanguageNames() + "; current " + OfflineModLocale.Current());
                string old = OfflineModLocale.Override;
                // The LocalizationManager.GetText hook (installed in the game, not on this bench) is OfflineMods.GetTextPrefix.
                System.Func<string, string> shown = delegate(string key) { string r = null; return OfflineMods.GetTextPrefix(key, ref r) ? null : r; };
                try {
                    OfflineModLocale.Override = "german";
                    Require(shown(wolf.NameKey.RawKey) == "Weisslauf-Wolfsrufer", "German name not shown: " + shown(wolf.NameKey.RawKey));
                    OfflineModLocale.Override = "de-at";
                    Require(shown(wolf.NameKey.RawKey) == "Weisslauf-Wolfsrufer", "Regional language should fall back to de.");
                    OfflineModLocale.Override = "fr";
                    Require(shown(wolf.NameKey.RawKey) == "Whiterun Wolfcaller", "Untranslated language should show English.");
                    OfflineModLocale.Override = "en";
                    Require(shown(wolf.NameKey.RawKey) == "Whiterun Wolfcaller", "English changed.");
                } finally { OfflineModLocale.Override = old; }
            });
            test("mod-stories", delegate {
                SPTESL.Campaign saga = config.Campaigns.Find(delegate(SPTESL.Campaign c) { return c != null && c.ResourceId.UniqueName == "MOD:sample-skyrim/greenwood-saga"; });
                Require(saga != null && saga.Acts.Count == 1 && saga.Acts[0].Chapters.Count == 2 && !saga.IsLinear, "Sample story not built.");
                SPTESL.Chapter call = saga.Acts[0].Chapters[0], barrow = saga.Acts[0].Chapters[1];
                Require(call.ChapterEvents.Count == 3 && call.ChapterEventsToComplete.Count == 2 && barrow.ChapterEvents[0].GatedScenarios.Count == 2, "Story chapters wrong.");
                SPTESL.ChapterEvent house = call.ChapterEvents[0], draugr = call.ChapterEvents[1], bandits = call.ChapterEvents[2];
                Require(house.GatedScenarios.Count == 0 && house.IntroModal.DialogueDataEvents.Count == 2 && house.IntroModal.DialogueDataEvents[1].Option2.GoldChoice == 50
                    && house.IntroModal.DialogueDataEvents[1].Option1.CardChoice.resourceId == OfflineModes.FindCard(config, "Whiterun Wolfcaller").ResourceId.Id
                    && house.IntroModal.DialogueDataEvents[0].DialogueAudio.StartsWith(OfflineStories.VoicePrefix), "Dialogue, choices or voice wrong.");
                Require(OfflineMods.Text(house.IntroModal.DialogueDataEvents[0].DialogueTextKey.RawKey).Contains("Draugr stir") && OfflineMods.HasArt(house.CoinImage), "Dialogue text or coin art not registered.");
                SPTESL.Scenario fight = GameManager.Instance.FindScenario(draugr.GatedScenarios[0].Scenario.resourceId);
                OfflineStories.Battle battle = fight == null ? null : OfflineStories.FindBattle(fight.ResourceId.UniqueName);
                Require(battle != null && battle.Mode.Opponent.Health == 20 && battle.Mode.LaneTypes[1] == "Plunder" && !battle.FixedDeck && fight.ScenarioAllowsUserDeckSelection()
                    && fight.MatchEndDialogues.Count == 1 && !string.IsNullOrEmpty(fight.IntroModalData.OverviewSection.TextKey.RawKey), "Battle scenario wrong.");
                SPTESL.Scenario masterFight = GameManager.Instance.FindScenario(draugr.GatedScenarios[0].MasterScenario.resourceId);
                OfflineStories.Battle master = masterFight == null ? null : OfflineStories.FindBattle(masterFight.ResourceId.UniqueName);
                Require(master != null && master.Mode.Difficulty == "Expert" && master.Mode.Opponent.Health == 20 && !string.IsNullOrEmpty(draugr.MasterRewardsBundleKey),
                    "Master battle or rewards missing.");
                AiOpponent ai = config.AiOpponents.Find(delegate(AiOpponent a) { return a.ResourceId.Id == fight.AiOpponent.resourceId; });
                Require(ai != null && OfflineMods.Text(ai.DisplayNameKey.RawKey) == "Draugr Deathlord" && OfflineMods.HasArt(ai.Image), "Battle opponent wrong.");
                Require(draugr.RewardsBundleKey.Split(',').Length == 2 && barrow.ChapterEvents[0].GatedScenarios[1].RequiredOutcomes[0].ModalChoiceIndex == 1, "Rewards or branch outcome wrong.");
                // Progress: events appear as their requirements are met; the choice reveals the optional fight.
                List<SPTESL.Campaign> old = OfflineCampaignService.Content;
                try {
                    OfflineCampaignService.PublishContent(config.Campaigns);
                    OfflinePlayerStateData d = new OfflinePlayerStateData();
                    OfflineCampaignService.EnsureStarted(d);
                    SparkleServices.Campaign.IUserCampaignData user = d.campaigns.Find(delegate(SparkleServices.Campaign.IUserCampaignData u) { return u.campaignId == saga.ResourceId.Id; });
                    Require(user != null && user.actData.Length == 1 && user.actData[0].chapterData[0].chapterEventData.Length == 1, "Story should start with only the first event.");
                    SparkleServices.Campaign.IUserChapterEventData first = user.actData[0].chapterData[0].chapterEventData[0];
                    first.introChoice = SparkleServices.Campaign.IUserCampaignChoice.Second; first.isComplete = true;
                    OfflineCampaignService.Advance(d, saga.ResourceId.Id, false);
                    Require(user.actData[0].chapterData[0].chapterEventData.Length == 3, "Choice did not reveal the battle and the branch (" + user.actData[0].chapterData[0].chapterEventData.Length + ").");
                    Require(OfflineCampaignService.CompleteEvent(d, draugr.ResourceId.Id, false) && user.actData[0].chapterData.Length == 2 && user.actData[0].chapterData[0].isComplete,
                        "Winning the required battle did not complete the chapter.");
                    Require(OfflineRewardsService.GrantEvent(d, draugr.ResourceId.Id, false) && d.rewards[d.rewards.Count - 1].rewardPasses.Count == 2, "Battle rewards not granted.");
                    int gold = d.gold;
                    foreach (SparkleServices.Rewards.RewardPassDescription pass in d.rewards[d.rewards.Count - 1].rewardPasses) OfflineRewardsService.Apply(d, pass);
                    Require(d.gold == gold + 100 && d.TotalCopies(OfflineModes.FindCard(config, "Bleak Falls Draugr").ResourceId.Id) >= 1, "Reward passes not applied.");
                } finally { OfflineCampaignService.PublishContent(old); }
            });
            test("mod-board-edits-and-models", delegate {
                OfflineBoards.Board board = OfflineBoards.Find("sample-skyrim/greenwood-table");
                Require(board != null && board.Edits != null && board.Edits["add"].AsList.Count == 6 && board.Edits.ContainsKey("objects"), "Sample board edits not loaded.");
                string dir = board.Dir;
                GameObject crate = OfflineModels.Load(System.IO.Path.Combine(dir, "greenwood-table/models/crate/crate.obj"), null);
                GameObject gem = OfflineModels.Load(System.IO.Path.Combine(dir, "greenwood-table/models/soul-gem/soul-gem.glb"), null);
                GameObject stone = OfflineModels.Load(System.IO.Path.Combine(dir, "greenwood-table/models/runestone/runestone.glb"), null);
                GameObject rug = OfflineModels.ImagePlane(System.IO.Path.Combine(dir, "greenwood-table/images/emblem.png"), 9f);
                try
                {
                    Mesh cm = crate.GetComponent<MeshFilter>().sharedMesh;
                    Require(cm.triangles.Length == 36 && cm.vertexCount >= 8 && crate.GetComponent<MeshRenderer>().sharedMaterial.mainTexture != null,
                        "OBJ crate wrong (" + cm.triangles.Length / 3 + " triangles, " + cm.vertexCount + " vertices, texture " + (crate.GetComponent<MeshRenderer>().sharedMaterial.mainTexture != null) + ").");
                    Require(gem.GetComponentsInChildren<MeshFilter>().Length == 1 && gem.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount > 10, "GLB gem wrong.");
                    Require(stone.GetComponentsInChildren<MeshRenderer>().Length == 2, "Converted runestone should have stone and rune meshes.");
                    int tris = rug.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                    Require(tris > 20 && tris < 4000, "Image plane cut-out has " + tris + " triangles.");
                }
                finally { UnityEngine.Object.Destroy(crate); UnityEngine.Object.Destroy(gem); UnityEngine.Object.Destroy(stone); UnityEngine.Object.Destroy(rug); }
            });
            test("asset-labels-export", delegate {
                int n = OfflineAssetLabels.Write(config, System.IO.Path.Combine(output, "asset-labels.tsv"));
                Require(n > 500, "Only " + n + " labelled asset paths found.");
            });
            test("mod-extra-emotes", delegate {
                AvatarCollection col = GameManager.Instance.Config.AvatarCollection;
                AvatarData housecarl = col.Avatars.Find(delegate(AvatarData a) { return a != null && a.ResourceId.UniqueName == "mod.sample-skyrim.avatar.housecarl"; });
                AvatarEmotePackage own = housecarl == null ? null : OfflineEmotes.Package(col, housecarl.EmotePackage.resourceId);
                List<OfflineExtraEmotes.Extra> mine = OfflineExtraEmotes.For(own);
                Require(mine.Count == 3 && mine.Exists(delegate(OfflineExtraEmotes.Extra x) { return x.Label == "Skol!" && x.Like == UserEmoteType.Taunt; }),
                    "Housecarl extras wrong (" + mine.Count + ").");
                AvatarData original = col.Avatars.Find(delegate(AvatarData a) { return a != null && a.ResourceId.UniqueName == "AvatarBreton1"; });
                AvatarEmotePackage p = OfflineEmotes.Package(col, original.EmotePackage.resourceId);
                Require(OfflineExtraEmotes.For(p).Count == 2 && OfflineEmotes.LineFor(p, OfflineExtraEmotes.For(p)[0].Type) != null, "Mod-wide extras missing on an original avatar.");
                Require((int)OfflineExtraEmotes.Vary(own, UserEmoteType.Hello) == (int)UserEmoteType.Hello, "Vary changed an emote with no variants.");
            });
            test("mod-music-contexts", delegate {
                OfflineMusic.Track track = OfflineMusic.Tracks.Find(delegate(OfflineMusic.Track t) { return t.Key == "sample-skyrim/hearth-theme"; });
                Require(track != null && !track.Rotation && System.IO.File.Exists(track.File), "Sample mod track not loaded.");
                Require(OfflineMusic.For("MainMenuState").Contains(track) && OfflineMusic.For("StoreState").Count == 0, "Moment lookup wrong.");
                Require(OfflineMusic.For("Board:sample-skyrim/greenwood-table|Board:Original").Contains(track) && OfflineMusic.For("Board:Original").Count == 0,
                    "Board lookup wrong (mod board first, base board none).");
            });
            test("ai-emote-temperaments", delegate {
                MatchPlayerData foe = new MatchPlayerData { isAIPlayer = true, Name = "Ulfric", AvatarUniqueName = "AiOpponent_Unknown" };
                Require(OfflineEmotes.TemperamentFor(foe) == OfflineEmotes.TemperamentFor(foe), "Temperament not stable per opponent.");
                OfflineEmotes.AiPersonalityOverride = "cocky";
                try { Require(OfflineEmotes.TemperamentFor(foe) == OfflineEmotes.Temperament.Cocky, "Temperament override ignored."); }
                finally { OfflineEmotes.AiPersonalityOverride = null; }
                int cockyTaunts = 0, friendlyTaunts = 0, stoicSilent = 0, friendlySilent = 0;
                for (int i = 0; i < 400; i++)
                {
                    if (OfflineEmotes.Pick(OfflineEmotes.Temperament.Cocky, "opening") == UserEmoteType.Taunt) cockyTaunts++;
                    foreach (string evt in new string[] { "board-wiped", "human-near-death", "big-hit-dealt", "won" })
                        if (OfflineEmotes.Pick(OfflineEmotes.Temperament.Friendly, evt) == UserEmoteType.Taunt) friendlyTaunts++;
                    if (OfflineEmotes.Pick(OfflineEmotes.Temperament.Stoic, "big-hit-dealt") == null) stoicSilent++;
                    if (OfflineEmotes.Pick(OfflineEmotes.Temperament.Friendly, "opening") == null) friendlySilent++;
                    Require(OfflineEmotes.Reply(OfflineEmotes.Temperament.Friendly, UserEmoteType.Hello) == UserEmoteType.Hello, "Friendly did not answer Hello.");
                }
                Require(cockyTaunts > 150 && friendlyTaunts == 0 && stoicSilent > 300 && friendlySilent < 60,
                    "Temperaments off: cocky taunts " + cockyTaunts + ", friendly taunts " + friendlyTaunts + ", stoic silent " + stoicSilent + ", friendly silent " + friendlySilent);
                Require(OfflineEmotes.Pick(OfflineEmotes.Temperament.Cocky, "no-such-event") == null, "Unknown event spoke.");
            });
            test("mod-sample-cards-deck-text-and-art-load", delegate {
                Require(OfflineMods.Loaded.Contains(Mod), "Fixture mod not loaded: " + string.Join(" | ", OfflineMods.Problems.ToArray()));
                Require(OfflineMods.Problems.Count == 0, "Mod problems: " + string.Join(" | ", OfflineMods.Problems.ToArray()));
                Require(OfflineMods.CardIds.Count == 4, "Expected 4 mod cards, got " + OfflineMods.CardIds.Count);
                Card housecarl = Find(config, "Jarl's Housecarl");
                Require(housecarl.TriggeredEffects.Count == 1 && housecarl.TriggeredEffects[0].TriggerType == EffectTriggerType.Summon,
                    "Housecarl did not keep its template's (Renowned Legate) Summon graph.");
                Require(Stat(housecarl, AttributeType.ManaCost) == 5 && Stat(housecarl, AttributeType.Attack) == 4 && Stat(housecarl, AttributeType.MaxHealth) == 4,
                    "Housecarl stats are not the authored 5-cost 4/4.");
                Require(OfflineMods.Text(housecarl.NameKey.RawKey) == "Jarl's Housecarl" && (OfflineMods.Text(housecarl.DescriptionKey.RawKey) ?? "").Contains("Imperial Grunt"),
                    "Housecarl name/text keys do not resolve.");
                Card thuum = Find(config, "Greybeard Thu'um");
                Require(thuum.ActionPlayInformation.Count == 1 && Stat(thuum, AttributeType.ManaCost) == 2, "Thu'um did not take Crushing Blow's action graph at cost 2.");
                Require(GameManager.Instance.GetCard(housecarl.ResourceId.Id) == housecarl, "GameManager.GetCard does not find the mod card.");
                Require(OfflineMods.HasArt(housecarl.Image), "Housecarl art not registered for " + housecarl.Image);
                AssetBundleLoadAssetOperation op = null;
                Require(!OfflineMods.LoadAssetPrefix(housecarl.Image, typeof(Sprite), ref op) && op != null && op.IsDone(), "Art request was not served.");
                Sprite sprite = op.GetAsset<Sprite>();
                Require(sprite != null && sprite.texture.width == 1024 && sprite.texture.height == 1024, "Mod art sprite is not the imported 1024x1024 image.");
                Require(OfflineMods.Decks.Count == 1, "Mod deck not registered.");
                DeckInfo deck = OfflineMods.Decks[0];
                int total = 0; foreach (DeckInfo.CardInfo c in deck.Cards) total += c.Count;
                Require(total == 51 && config.DeckInfo.Contains(deck) && OfflineMods.Text(deck.DisplayName) == "Skyrim Heroes", "Mod deck wrong: " + total + " cards.");
                DeckArtData art = config.DeckArt.Find(delegate(DeckArtData x) { return x.ResourceId.Id == deck.ArtTypeHash; });
                Require(art != null && OfflineMods.HasArt(art.CroppedImagePath) && OfflineMods.HasArt(art.LargeImagePath), "Mod deck art not registered.");
                AssetBundleLoadAssetOperation tileOp = null;
                OfflineMods.LoadAssetPrefix(art.CroppedImagePath, typeof(Sprite), ref tileOp);
                Sprite tile = tileOp == null ? null : tileOp.GetAsset<Sprite>();
                Require(tile != null && tile.texture.width == 512 && tile.texture.height == 128, "Deck tile art is not the original 512x128 crop.");
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                int granted = OfflineMods.GrantOwnership(d);
                Require(granted == 1 + OfflineMods.CardIds.Count && d.TotalCopies(housecarl.ResourceId.Id) == 3 && d.decks.Count == 1 && OfflineMods.GrantOwnership(d) == 0,
                    "Ownership grant wrong or repeated (" + granted + ").");
            });
            test("mod-sample-cards-real-paid-play-and-smoke", delegate {
                List<string> failures = new List<string>();
                foreach (int id in OfflineMods.CardIds)
                {
                    Card card = config.FindCardById(id);
                    try { OriginalSpecTests.PaidPlay(config, card); } catch (Exception e) { failures.Add(card.Name + " paid play: " + e.Message); }
                    try { OriginalSpecTests.Smoke(config, card); } catch (Exception e) { if (!(e is InvalidOperationException) || e.Message.IndexOf("no legal", StringComparison.Ordinal) < 0) failures.Add(card.Name + " smoke: " + e.Message); }
                }
                Require(failures.Count == 0, string.Join(" | ", failures.ToArray()));
            });
            test("mod-image-replacement-original-size", delegate {
                // The fixture replaces Fiery Imp's original 512x512 art (100 px/unit) with a 1024x1024 remaster.
                Card imp = config.FindCardById(1193892832);
                Require(imp != null && OfflineMods.Replaced.Contains(imp.Image), "Fiery Imp art replacement not loaded (" + (imp == null ? "no card" : imp.Image) + ").");
                AssetBundleLoadAssetOperation op = null;
                Require(!OfflineMods.LoadAssetPrefix(imp.Image, typeof(Sprite), ref op) && op != null, "Replacement not served as a sprite.");
                Sprite sprite = op.GetAsset<Sprite>();
                Require(sprite.texture.width == 1024 && Mathf.Abs(sprite.bounds.size.x - 5.12f) < 0.01f && Mathf.Abs(sprite.pivot.x - 512f) < 0.5f,
                    "Replacement sprite not at the original size: " + sprite.texture.width + "px, " + sprite.bounds.size.x + " units, pivot " + sprite.pivot);
                AssetBundleLoadAssetOperation tex = null;
                Require(!OfflineMods.LoadAssetExtPrefix(imp.Image, typeof(Texture), "png", ref tex) && tex.GetAsset<Texture>().width == 1024,
                    "Replacement not served through the extension overload.");
                AssetBundleLoadAssetOperation other = null;
                Require(OfflineMods.LoadAssetPrefix("ContentPack000/Cards/Visuals/scheme_weaver/scheme_weaver", typeof(Sprite), ref other) && other == null,
                    "An unreplaced original was intercepted.");
                Require(OfflineMods.LoadAssetPrefix(imp.Image, typeof(Material), ref other), "A non-image load of a replaced path was intercepted.");
            });
        }

        private static int Stat(Card card, AttributeType type)
        {
            foreach (SPTESL.Attribute a in card.EntityAttributes) { IntAttribute i = a as IntAttribute; if (i != null && a.Name == type) return i.Value; }
            return int.MinValue;
        }

        private static Card Find(GameConfiguration config, string name)
        {
            foreach (int id in OfflineMods.CardIds) { Card c = config.FindCardById(id); if (c != null && c.Name == name) return c; }
            throw new InvalidOperationException("Mod card missing: " + name);
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
