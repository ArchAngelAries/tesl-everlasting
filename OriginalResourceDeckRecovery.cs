using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FullSerializer;
using SPTESL;

namespace OfflineEngine
{
    // The reviewed extraction manifest identifies exact original Resources
    // TextAsset bytes. Read them again from their unchanged original source,
    // verifying whole-file and script hashes before returning copied recipes.
    public static class OriginalResourceDeckRecovery
    {
        public sealed class Recipe { public string AssetName; public int Offset; public Deck Deck; }
        public static List<Recipe> Read(string directory)
        {
            List<Recipe> recipes = new List<Recipe>();
            string manifestPath = Path.Combine(Path.Combine(directory, "original-resource-decks"), "manifest.json");
            if (!File.Exists(manifestPath)) manifestPath = Path.Combine(OfflinePaths.DataNear(directory, "original-resource-decks"), "manifest.json");   // data/original-resource-decks
            if (!File.Exists(manifestPath)) return recipes;
            fsData manifest = fsJsonParser.Parse(File.ReadAllText(manifestPath));
            Keys(manifest, "schema", "source_sha256", "records");
            if (Integer(manifest, "schema") != 1) throw new NotSupportedException("Unknown original resource-deck extraction schema.");
            Dictionary<string, byte[]> sources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> assets = new HashSet<string>(StringComparer.Ordinal);
            foreach (fsData record in manifest.AsDictionary["records"].AsList)
            {
                // Each deck is verified on its own (exact script bytes by SHA-256). One that cannot be verified in this
                // install is skipped with a warning; startup never stops for it (another build's resources.assets differs
                // elsewhere but carries the same decks: Bethesda.net/TESL Reborn installs, reported on Reddit).
                try { Recipe recipe = ReadRecord(record, sources, assets); if (recipe != null) recipes.Add(recipe); }
                catch (Exception e) { UnityEngine.Debug.LogWarning("OFFLINE DECKS: original deck skipped (" + e.Message + ")"); }
            }
            return recipes;
        }

        private static Recipe ReadRecord(fsData record, Dictionary<string, byte[]> sources, HashSet<string> assets)
        {
            {
                Keys(record, "asset_name", "file", "source_file", "script_offset", "script_length", "sha256", "name", "size");
                string sourceFile = record.AsDictionary["source_file"].AsString;   // relative to the game folder (older manifests: absolute)
                string sourcePath = Path.GetFullPath(Path.IsPathRooted(sourceFile) ? sourceFile : Path.Combine(OfflinePaths.GameRoot(), sourceFile));
                if (Path.GetFileName(sourcePath) != "resources.assets") throw new NotSupportedException("Resource-deck manifest must identify the original resources.assets.");
                byte[] source;
                if (!sources.TryGetValue(sourcePath, out source))
                {
                    source = File.ReadAllBytes(sourcePath);
                    sources.Add(sourcePath, source);
                }
                string asset = record.AsDictionary["asset_name"].AsString;
                if (!assets.Add(asset)) throw new NotSupportedException("Duplicate recovered original TextAsset identity.");
                int offset = Integer(record, "script_offset"), length = Integer(record, "script_length");
                string sha = record.AsDictionary["sha256"].AsString;
                if (length < 16 || length > 1048576) throw new NotSupportedException("Original TextAsset script length is outside its audited bounds.");
                byte[] payload = At(source, offset, length, sha);
                // Moved in another build: find the TextAsset by its name; its script follows the 4-aligned name and a length prefix.
                if (payload == null)
                {
                    byte[] name = Encoding.UTF8.GetBytes(asset);
                    for (int i = Find(source, name, 0); i >= 0 && payload == null; i = Find(source, name, i + 1))
                    {
                        int candidate = (i + name.Length + 3) / 4 * 4 + 4;
                        payload = At(source, candidate, length, sha);
                        if (payload != null) offset = candidate;
                    }
                }
                if (payload == null) throw new NotSupportedException(asset + " not found with its audited bytes in this install's resources.assets");
                fsData json = fsJsonParser.Parse(new UTF8Encoding(false, true).GetString(payload));
                Keys(json, "Name", "Type", "AvatarCardId", "Cards");
                Deck deck = new Deck { Name = json.AsDictionary["Name"].AsString,
                    Type = (DeckType)Enum.Parse(typeof(DeckType), json.AsDictionary["Type"].AsString), AvatarCardId = Integer(json, "AvatarCardId") };
                foreach (fsData item in json.AsDictionary["Cards"].AsList)
                { Keys(item, "Id", "Count"); deck.Cards.Add(new DeckCardInfo(Integer(item, "Id"), Integer(item, "Count"))); }
                if (deck.Name != record.AsDictionary["name"].AsString || deck.Size != Integer(record, "size"))
                    throw new NotSupportedException("Original resource-deck name/size differs from extraction receipt.");
                return new Recipe { AssetName = asset, Offset = offset, Deck = deck };
            }
        }

        // The exact script at offset, or null: an audited length prefix and the audited SHA-256 of the bytes.
        private static byte[] At(byte[] source, int offset, int length, string sha)
        {
            if (offset < 4 || (long)offset + length > source.Length || BitConverter.ToInt32(source, offset - 4) != length) return null;
            byte[] payload = new byte[length]; Array.Copy(source, offset, payload, 0, length);
            return Hash(payload) == sha ? payload : null;
        }

        private static int Find(byte[] source, byte[] value, int from)
        {
            for (int i = from; i <= source.Length - value.Length; i++)
            {
                int j = 0;
                while (j < value.Length && source[i + j] == value[j]) j++;
                if (j == value.Length) return i;
            }
            return -1;
        }
        private static string Hash(byte[] value)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", ""); }
        private static int Integer(fsData value, string name)
        { fsData field = value.AsDictionary[name]; if (!field.IsInt64) throw new NotSupportedException("Expected original integer field " + name + "."); return checked((int)field.AsInt64); }
        private static void Keys(fsData value, params string[] names)
        { if (!value.IsDictionary || value.AsDictionary.Count != names.Length) throw new NotSupportedException("Unknown original resource-deck fields."); foreach (string name in names) if (!value.AsDictionary.ContainsKey(name)) throw new NotSupportedException("Missing original resource-deck field " + name + "."); }
    }
}
