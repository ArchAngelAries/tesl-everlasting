using System;
using System.Collections.Generic;
using System.IO;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Puzzle starting conditions. The installed puzzle scenarios keep their boards, hands and
    // decks, but the per-puzzle player overrides (magicka, avatar health) are NonSerialized. Values
    // come from UESP per-puzzle pages (data/puzzle-setups.tsv, compiled 2026-10-21 into
    // (research notes: puzzle-setups.tsv). Applied right after native registration:
    // both avatars' health, and the player's max magicka as a native max-magicka buff of
    // magicka - 1 (the first turn start adds the last point). Runes are not published; puzzles run
    // with none (see Apply). Reconstruction from fan data, not server data.
    public static class OfflinePuzzleSetups
    {
        public sealed class Setup { public string Name; public int Magicka, PlayerHealth, OpponentHealth; }
        private static Dictionary<string, Setup> setups;

        private static string Key(string name)
        {
            System.Text.StringBuilder key = new System.Text.StringBuilder();
            foreach (char c in name ?? "") if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
            return key.ToString();
        }

        public static void Load(string path)
        {
            Dictionary<string, Setup> loaded = new Dictionary<string, Setup>();
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] f = line.Split('\t');
                    int magicka, player, opponent;
                    if (f.Length < 6 || f[0] == "set" || !int.TryParse(f[3], out magicka) || !int.TryParse(f[4], out player) || !int.TryParse(f[5], out opponent)) continue;
                    loaded[Key(f[1])] = new Setup { Name = f[1], Magicka = magicka, PlayerHealth = player, OpponentHealth = opponent };
                }
            setups = loaded;
            Debug.Log("OFFLINE PUZZLES: loaded " + loaded.Count + " puzzle setups from " + path);
        }

        // Scenario "PUZ_<giver>_<PuzzleName>" -> its setup (null when not a known puzzle).
        public static Setup Find(string scenarioName)
        {
            if (setups == null || scenarioName == null || !scenarioName.StartsWith("PUZ_", StringComparison.Ordinal)) return null;
            int split = scenarioName.IndexOf('_', 4);
            Setup setup;
            return split > 0 && setups.TryGetValue(Key(scenarioName.Substring(split + 1)), out setup) ? setup : null;
        }

        public static void Apply(Simulation sim, Setup setup)
        {
            ServerEffectUtilities.TriggerGameState gs = new ServerEffectUtilities.TriggerGameState(sim.HistoryLog, sim.RandomGenerator, sim.CardCatalog, sim.Players,
                new ServerEffectUtilities.TriggerQueues(), sim.ActiveQuests, new List<CustomDisplayCard>(), sim._scenarioId);
            foreach (PlayerState player in sim.Players)
            {
                // At registration the avatar is still in its AvatarDeck zone (it moves to AvatarBoard at game start).
                ServerCard avatar = sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarDeck)
                    ?? sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarBoard);
                if (avatar == null) { List<ServerCard> any = sim.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.OwnerNetId == player.NetId && c.HasSubtype(CardSubtype.Avatar); }); avatar = any.Count > 0 ? any[0] : null; }
                Debug.Log("OFFLINE PUZZLES: " + setup.Name + " player " + player.NetId.Id + " avatar " + (avatar == null ? "absent" : avatar.CurrentGameZoneName.ToString()));
                if (avatar == null) continue;
                bool human = player.PlayerType == (int)PlayerType.Normal;
                // No runes: puzzle health sits below every rune threshold, so runes would all break at
                // game start and draw from the puzzle's empty decks (out-of-cards damage kills the player).
                player.SetAttribute(AttributeType.NumRunes, 0); player.SetAttribute(AttributeType.NumStartingRunes, 0);
                int health = human ? setup.PlayerHealth : setup.OpponentHealth;
                // The magicka buff recalculates the avatar's enchantments, which re-derives health from the
                // definition: apply it first, then the puzzle health.
                if (human && setup.Magicka > 1) avatar.AddMaxManaBuff(gs, setup.Magicka - 1);
                if (health > 0) { avatar.SetAttribute(AttributeType.StartingHealth, health); avatar.SetAvatarHealthPreGame(health); }
            }
        }
    }
}
