using System;
using System.Collections.Generic;
using SparkleServices;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    public static class OfflineGoldStoreTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            if (OfflineCampaignService.Content == null) OfflineCampaignService.PublishContent(config.Campaigns);
            test("gold-store-every-official-card-back-has-a-store-image", delegate {
                // owner 2026-11-07: 52 official backs had blank store tiles (prefab backs; textures under other names)
                List<string> missing = new List<string>();
                foreach (string key in OfflineCosmetics.CardBacks)
                {
                    if (key == OfflineCosmetics.DefaultCardBack) continue;
                    string art = OfflineGoldStore.CardBackArt(config, key);
                    if (art == null) { missing.Add(key + " (none)"); continue; }
                    if (art.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && !System.IO.File.Exists(new Uri(art).LocalPath)) missing.Add(key + " (" + art + " absent)");
                }
                Require(missing.Count == 0, "card backs without a store image: " + string.Join(", ", missing.ToArray()));
            });
            test("gold-store-decks-and-card-backs", delegate {
                OfflinePlayerStateData d = new OfflinePlayerStateData { gold = 100000 };
                List<OfflineGoldStore.Offer> offers = OfflineGoldStore.Offers(d);
                OfflineGoldStore.Offer deck = offers.Find(delegate(OfflineGoldStore.Offer o) { return o.Kind == "deck"; });
                OfflineGoldStore.Offer back = offers.Find(delegate(OfflineGoldStore.Offer o) { return o.Kind == "cardback"; });
                Require(deck != null && deck.Key == "deck_" + deck.Deck.TypeName && deck.Tags.Contains("Special") && deck.Tags.Contains("Deck") && deck.Gold >= 250,
                    "No well-formed deck offer.");
                Require(!offers.Exists(delegate(OfflineGoldStore.Offer o) { return o.Kind == "deck" && o.Deck.TypeName.StartsWith("mod.", StringComparison.Ordinal); }), "Mod decks are offered for gold.");
                Require(back != null && back.CardBack != OfflineCosmetics.DefaultCardBack, "No card back offer.");
                List<Shop.CatalogItem> items = OfflineGoldStore.CatalogItems(d, 10);
                Require(items.Count == offers.Count && items[0].index == 10 && items.TrueForAll(delegate(Shop.CatalogItem i) { return i.priceGold > 0 && !string.IsNullOrEmpty(i.summaryTitle); }),
                    "Catalog items malformed.");
                OfflinePlayerStateData poor = new OfflinePlayerStateData { gold = deck.Gold - 1 };
                Require(!OfflineGoldStore.Buy(poor, deck.Key) && poor.gold == deck.Gold - 1 && poor.decks.Count == 0, "Bought a deck without enough gold.");
                int gold = d.gold;
                Require(OfflineGoldStore.Buy(d, deck.Key) && d.gold == gold - deck.Gold && d.decks.Exists(delegate(Inventory.Deck x) { return x.deckTypeHash == deck.Deck.TypeNameHash; }),
                    "Deck purchase did not grant the deck and charge its price.");
                Require(!OfflineGoldStore.Offers(d).Exists(delegate(OfflineGoldStore.Offer o) { return o.Key == deck.Key; }) && !OfflineGoldStore.Buy(d, deck.Key), "Owned deck still for sale.");
                gold = d.gold;
                Require(OfflineGoldStore.Buy(d, back.Key) && d.ownedCardBacks.Contains(back.CardBack) && d.gold == gold - back.Gold, "Card back purchase failed.");
                Require(!OfflineGoldStore.Buy(d, "offline_nonsense") && !OfflineGoldStore.Buy(d, "deck_nonsense"), "Unknown offer bought.");
            });
            test("graphics-settings-apply-and-sanitize", delegate {
                OfflineGraphicsSettings saved = OfflineGraphics.Settings;
                int fps = Application.targetFrameRate, aa = QualitySettings.antiAliasing, vs = QualitySettings.vSyncCount;
                try
                {
                    OfflineGraphics.Settings = new OfflineGraphicsSettings { frameCap = 144, msaa = 4, vsync = false, anisotropic = true };
                    OfflineGraphics.Apply();
                    Require(Application.targetFrameRate == 144 && QualitySettings.antiAliasing == 4 && QualitySettings.vSyncCount == 0
                        && QualitySettings.anisotropicFiltering == AnisotropicFiltering.ForceEnable, "Graphics settings not applied.");
                    Application.targetFrameRate = 60;   // what the client does at startup
                    Require(OfflineGraphics.Apply() == 1 && Application.targetFrameRate == 144, "Drifted frame cap not re-applied.");
                    OfflineGraphicsSettings bad = OfflineGraphics.Sanitize(new OfflineGraphicsSettings { frameCap = 77, msaa = 3 });
                    Require(bad.frameCap == 60 && bad.msaa == 2, "Invalid graphics values kept.");
                    Require(OfflineGraphics.Sanitize(new OfflineGraphicsSettings { msaa = 0 }).msaa == 2, "Anti-aliasing off must become 2x (black board with AA off).");
                    Require(OfflineGraphics.Next(OfflineGraphics.FrameCaps, -1) == 30 && OfflineGraphics.Load("Z:/no/such/graphics.json").frameCap == 60, "Cycling or defaults wrong.");
                }
                finally { OfflineGraphics.Settings = saved; Application.targetFrameRate = fps; QualitySettings.antiAliasing = aa; QualitySettings.vSyncCount = vs; }
            });
            test("graphics-presets-and-window", delegate {
                OfflineGraphicsSettings saved = OfflineGraphics.Settings;
                int level = QualitySettings.GetQualityLevel(), aa = QualitySettings.antiAliasing;
                System.Reflection.FieldInfo pathField = typeof(OfflineGraphics).GetField("path", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                object savedPath = pathField.GetValue(null); pathField.SetValue(null, null);   // never write the player's graphics.json
                try
                {
                    OfflineGraphicsSettings bad = OfflineGraphics.Sanitize(new OfflineGraphicsSettings { window = "huge", preset = "insane" });
                    Require(bad.window == "game" && bad.preset == "custom" && bad.resizable, "Bad window/preset values kept.");
                    OfflineGraphics.Settings = new OfflineGraphicsSettings();
                    OfflineGraphics.ApplyPreset("ultra");
                    Require(OfflineGraphics.Settings.msaa == 8 && OfflineGraphics.Settings.ultra && QualitySettings.shadowResolution == ShadowResolution.VeryHigh
                        && QualitySettings.pixelLightCount >= 8, "Ultra preset not applied.");
                    OfflineGraphics.ApplyPreset("low");
                    Require(OfflineGraphics.Settings.msaa == 2 && !OfflineGraphics.Settings.ultra && !OfflineGraphics.Settings.anisotropic, "Low preset not applied.");
                    OfflineGraphics.Change(delegate(OfflineGraphicsSettings x) { x.msaa = 4; });
                    Require(OfflineGraphics.Settings.preset == "custom", "A manual change kept the preset name.");
                }
                finally { OfflineGraphics.Settings = saved; QualitySettings.SetQualityLevel(level, true); QualitySettings.antiAliasing = aa; pathField.SetValue(null, savedPath); }
            });
            test("gold-store-stories-optional", delegate {
                OfflinePlayerStateData d = new OfflinePlayerStateData { gold = 5000 };
                OfflineCampaignService.EnsureStarted(d);
                List<OfflineGoldStore.Offer> stories = OfflineGoldStore.Offers(d).FindAll(delegate(OfflineGoldStore.Offer o) { return o.Kind == "story"; });
                Require(stories.Count > 0 && stories.TrueForAll(delegate(OfflineGoldStore.Offer o) { return o.Owned; }), "Free stories not shown as owned (" + stories.Count + ").");
                int relocked = OfflineGoldStore.SetStoriesForGold(d, true);
                stories = OfflineGoldStore.Offers(d).FindAll(delegate(OfflineGoldStore.Offer o) { return o.Kind == "story"; });
                Require(relocked == stories.Count && stories.TrueForAll(delegate(OfflineGoldStore.Offer o) { return !o.Owned; }), "Unplayed acts not re-locked: " + relocked + "/" + stories.Count);
                Require(d.campaigns.Exists(delegate(SparkleServices.Campaign.IUserCampaignData c) { return c.campaignId == OfflineCampaignService.ForgottenHeroCampaignId; }), "The Forgotten Hero was re-locked.");
                OfflineGoldStore.Offer act = stories.Find(delegate(OfflineGoldStore.Offer o) { return o.Tags.Contains("DarkBrotherhood"); }) ?? stories[0];
                Require(OfflineGoldStore.Buy(d, act.Key) && d.gold == 5000 - act.Gold, "Act purchase failed.");
                SparkleServices.Campaign.IUserCampaignData user = d.campaigns.Find(delegate(SparkleServices.Campaign.IUserCampaignData c) { return c.campaignId == act.Campaign.ResourceId.Id; });
                Require(user != null && Array.IndexOf(user.ownedActs, act.Act.ResourceId.Id) >= 0 && Array.Exists(user.actData, delegate(SparkleServices.Campaign.IUserActData a) { return a.actId == act.Act.ResourceId.Id; }),
                    "Bought act not owned with its first chapter.");
                OfflinePlayerStateData fresh = new OfflinePlayerStateData { storiesForGold = true };
                OfflineCampaignService.EnsureStarted(fresh);
                // Original stories are bought; The Forgotten Hero and mod stories (OfflineStories) stay free.
                Require(fresh.campaigns.TrueForAll(delegate(SparkleServices.Campaign.IUserCampaignData c) { return c.campaignId == OfflineCampaignService.ForgottenHeroCampaignId || OfflineStories.CampaignIds.Contains(c.campaignId); })
                    && fresh.campaigns.Exists(delegate(SparkleServices.Campaign.IUserCampaignData c) { return c.campaignId == OfflineCampaignService.ForgottenHeroCampaignId; }), "New gold-mode profile got free original stories.");
                int regranted = OfflineGoldStore.SetStoriesForGold(d, false);
                Require(regranted == stories.Count - 1 && OfflineGoldStore.Offers(d).FindAll(delegate(OfflineGoldStore.Offer o) { return o.Kind == "story" && !o.Owned; }).Count == 0,
                    "Turning stories free again did not own every act (" + regranted + ").");
            });
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
