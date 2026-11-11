using System;
using System.Collections.Generic;
using System.IO;
using SparkleServices;
using SPTESL;

namespace OfflineEngine
{
    // The extension interface (EverlastingHost) that expansions such as TESL: Aetherius build on.
    public static class EverlastingHostTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test, string output)
        {
            test("everlasting-host-versioned-registration-and-dependencies", delegate {
                EverlastingHost.Manifest future = new EverlastingHost.Manifest { Id = "test.future", Name = "Future Pack", Version = "1.0.0", RequiresApi = 99 };
                string refused = EverlastingHost.Register(future);
                Require(refused != null && refused.Contains("99") && !EverlastingHost.IsActive("test.future"), "an unsupported API version was accepted: " + refused);
                Require(EverlastingHost.Register(new EverlastingHost.Manifest { Id = "test.core", Name = "Core", Version = "1.2.0" }) == null, "a supported extension was refused");
                EverlastingHost.Register(new EverlastingHost.Manifest { Id = "test.pack", Name = "Pack", Version = "1.0.0", Requires = new[] { "test.core>=1.1", "test.absent" } });
                List<string> missing = EverlastingHost.MissingDependencies("test.pack");
                Require(missing.Count == 1 && missing[0] == "test.absent", "dependency check: " + string.Join(", ", missing.ToArray()));
                EverlastingHost.Register(new EverlastingHost.Manifest { Id = "test.pack2", Name = "Pack 2", Version = "1.0.0", Requires = new[] { "test.core>=2.0" } });
                Require(EverlastingHost.MissingDependencies("test.pack2").Count == 1, "a too-old dependency passed");
                object provider = new object();
                EverlastingHost.Provide("test-objectives", 2, provider);
                Require(EverlastingHost.Providers("test-objectives", 1).Contains(provider) && EverlastingHost.Providers("test-objectives", 3).Count == 0, "capability versions");
            });
            test("everlasting-host-extension-bundles-reach-the-native-reward-flow", delegate {
                int card = 0;
                foreach (EverlastingHost.CardInfo c in EverlastingHost.Cards()) if (c.Collectible && c.Mod == null) { card = c.Id; break; }
                Require(card != 0, "no official collectible card listed");
                EverlastingHost.RegisterBundle("test.pack.reward", new[] { "gold:25", "gems:3", "cardid:" + card + "*2" });
                OfflinePlayerStateData d = new OfflinePlayerStateData();
                Rewards.RewardDescription r = EverlastingHost.QueueReward(d, "test.pack.reward", Rewards.RewardSource.Game);
                Require(d.rewards.Count == 1 && r.rewardPasses[0].bundleKey == "test.pack.reward", "reward not queued");
                Rewards.AcceptRewardResponse.AcceptRewardResult got = OfflineRewardsService.Apply(d, r.rewardPasses[0]);
                int owned = d.cardIds.IndexOf(card) >= 0 ? d.cardCounts[d.cardIds.IndexOf(card)] : 0;
                Require(d.gold == 25 && d.gems == 3 && owned == 2 && got.items.Count == 1, "applied: gold " + d.gold + ", gems " + d.gems + ", copies " + owned);
            });
            test("everlasting-host-content-lists-mods-in-load-order", delegate {
                List<EverlastingHost.ModInfo> mods = EverlastingHost.Mods();
                Require(mods.Count == OfflineMods.Loaded.Count, "mods listed " + mods.Count + " of " + OfflineMods.Loaded.Count);
                if (OfflineMods.CardIds.Count > 0)
                {
                    int modCard = OfflineMods.CardIds[0];
                    EverlastingHost.CardInfo info = EverlastingHost.Cards().Find(delegate(EverlastingHost.CardInfo c) { return c.Id == modCard; });
                    Require(info != null && info.Mod != null && OfflineMods.Loaded.Contains(info.Mod), "a mod card is not attributed to its mod");
                    EverlastingHost.ModInfo m = mods.Find(delegate(EverlastingHost.ModInfo x) { return x.Id == info.Mod; });
                    Require(m != null && m.Folder != null && File.Exists(Path.Combine(m.Folder, "mod.json")), "mod folder unknown");
                }
            });
            test("everlasting-host-match-ended-and-data-folder", delegate {
                EverlastingHost.MatchResult seen = null;
                EverlastingHost.OnMatchEnded(delegate(EverlastingHost.MatchResult m) { if (m.Scenario == "test-scenario") seen = m; return m.Scenario == "test-scenario"; });
                bool rewarded = EverlastingHost.RaiseMatchEnded(new EverlastingHost.MatchResult { Mode = "Practice", Won = true, Scenario = "test-scenario" });
                Require(seen != null && seen.Won && rewarded, "match-ended handler not called");
                string dir = EverlastingHost.DataFolder("test.pack", "offline");
                string file = Path.Combine(dir, "state.json");
                EverlastingHost.WriteAtomic(file, "{\"a\":1}");
                EverlastingHost.WriteAtomic(file, "{\"a\":2}");
                Require(File.ReadAllText(file) == "{\"a\":2}" && File.Exists(file + ".bak"), "atomic write");
                Require(dir.Replace('\\', '/').Contains(".offline-user-data/extensions/test.pack/offline"), "data folder " + dir);
                Directory.Delete(Path.GetDirectoryName(dir), true);
            });
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
