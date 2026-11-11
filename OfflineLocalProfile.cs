using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FullSerializer;

namespace OfflineEngine
{
    // Local-only identity and terminal match ledger. Never consumes AuthData or remote ids.
    public sealed class OfflineLocalProfile
    {
        private sealed class State
        {
            public string Identity, Username;
            public Dictionary<string, bool> Results = new Dictionary<string, bool>(StringComparer.Ordinal);
        }
        private readonly string path;
        private State state;
        private const int MaxBytes = 16777216;
        public string StoragePath { get { return path; } }
        public string Identity { get { return state.Identity; } }
        public string Username { get { return state.Username; } }
        public int MatchesCompleted { get { return state.Results.Count; } }
        public int MatchesWon { get { int count = 0; foreach (bool won in state.Results.Values) if (won) count++; return count; } }
        public int MatchesLost { get { return MatchesCompleted - MatchesWon; } }
        private OfflineLocalProfile(string value) { path = value; }

        public static OfflineLocalProfile Open(string workspaceRoot)
        {
            if (string.IsNullOrEmpty(workspaceRoot)) throw new ArgumentException("A workspace root is required.");
            string directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".offline-user-data");
            Directory.CreateDirectory(directory);
            OfflineLocalProfile profile = new OfflineLocalProfile(Path.Combine(directory, "profile.json"));
            using (profile.Lock())
            {
                profile.state = profile.Load();
                if (profile.state == null)
                {
                    profile.state = new State { Identity = Guid.NewGuid().ToString("D"), Username = "Offline Player" };
                    profile.Save(profile.state);
                }
            }
            return profile;
        }

        public static string CreateMatchId() { return Guid.NewGuid().ToString("D"); }
        public static string ValidateUsername(string value)
        {
            if (value == null) throw new ArgumentException("Enter a player name.");
            value = value.Trim();
            if (value.Length < 1 || value.Length > 32) throw new ArgumentException("Player name must contain 1–32 characters.");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsControl(c) || c == '<' || c == '>') throw new ArgumentException("Player name cannot contain control characters or angle brackets.");
                if (char.IsHighSurrogate(c)) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw new ArgumentException("Player name contains invalid Unicode."); }
                else if (char.IsLowSurrogate(c)) throw new ArgumentException("Player name contains invalid Unicode.");
            }
            return value;
        }
        public void SetUsername(string value)
        {
            value = ValidateUsername(value);
            using (Lock()) { State next = RequireExisting(); next.Username = value; Save(next); state = next; }
        }
        // Returns false for an already committed result. Conflicting repeats are rejected.
        // Caller decides eligibility: completed native Results Win/Lose, excluding Retry.
        public bool RecordResult(string matchId, bool won)
        {
            RequireGuid(matchId);
            using (Lock())
            {
                State next = RequireExisting(); bool previous;
                if (next.Results.TryGetValue(matchId, out previous))
                {
                    if (previous != won) throw new InvalidOperationException("A local match already has a different committed result.");
                    state = next; return false;
                }
                next.Results.Add(matchId, won); Save(next); state = next; return true;
            }
        }
        private FileStream Lock() { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        private State RequireExisting() { State result = Load(); if (result == null) throw new IOException("Local profile disappeared; refusing to replace its identity."); return result; }
        private State Load()
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            Exception failure;
            try { return Read(path); }
            catch (NotSupportedException) { throw; } // Future schema must never be rolled back silently.
            catch (Exception ex) { if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is KeyNotFoundException || ex is OverflowException)) throw; failure = ex; }
            State recovered;
            try { recovered = Read(path + ".bak"); }
            catch (Exception ex) { throw new IOException("Local profile and backup cannot be loaded. Preserve both files for recovery. Primary: " + failure.Message, ex); }
            string temporary = WriteTemporary(recovered);
            try
            {
                if (File.Exists(path)) File.Replace(temporary, path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return recovered;
        }
        private static State Read(string file)
        {
            // The shipped Mono FileStream reports a missing file as an
            // IsolatedStorageException. Normalize this known missing-file case.
            if (!File.Exists(file)) throw new FileNotFoundException("Local profile file is absent.", file);
            using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxBytes) throw new IOException("Local profile exceeds supported size.");
                string json; using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true))) json = reader.ReadToEnd();
                fsData root;
                if (!fsJsonParser.Parse(json, out root).Succeeded) throw new FormatException("Local profile JSON is malformed.");
                if (!root.IsDictionary) throw new FormatException("Local profile must be an object.");
                Dictionary<string, fsData> fields = root.AsDictionary;
                if (!fields.ContainsKey("schema") || !fields["schema"].IsInt64) throw new FormatException("Local profile schema is absent.");
                if (fields["schema"].AsInt64 != 1) throw new NotSupportedException("Unsupported local profile schema; data was preserved.");
                if (fields.Count != 4) throw new FormatException("Unexpected local profile fields.");
                State result = new State { Identity = fields["identity"].AsString, Username = fields["username"].AsString };
                RequireGuid(result.Identity);
                if (ValidateUsername(result.Username) != result.Username) throw new FormatException("Stored player name is not normalized.");
                if (!fields["results"].IsDictionary) throw new FormatException("Invalid local match ledger.");
                foreach (KeyValuePair<string, fsData> entry in fields["results"].AsDictionary)
                { RequireGuid(entry.Key); if (!entry.Value.IsBool) throw new FormatException("Invalid match outcome."); result.Results.Add(entry.Key, entry.Value.AsBool); }
                return result;
            }
        }
        private static void RequireGuid(string value)
        {
            try
            {
                if (value == null || new Guid(value).ToString("D") != value)
                    throw new ArgumentException("Local identity must be a canonical GUID.");
            }
            catch (FormatException ex) { throw new ArgumentException("Local identity must be a canonical GUID.", ex); }
        }
        private string WriteTemporary(State value)
        {
            Dictionary<string, fsData> results = new Dictionary<string, fsData>();
            foreach (KeyValuePair<string, bool> entry in value.Results) results.Add(entry.Key, new fsData(entry.Value));
            Dictionary<string, fsData> fields = new Dictionary<string, fsData>();
            fields.Add("schema", new fsData((long)1)); fields.Add("identity", new fsData(value.Identity));
            fields.Add("username", new fsData(value.Username)); fields.Add("results", new fsData(results));
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(fsJsonPrinter.CompressedJson(new fsData(fields)));
            if (bytes.Length > MaxBytes) throw new IOException("Local profile exceeds supported size.");
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try { using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(); } }
            catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
            return temporary;
        }
        private void Save(State value)
        {
            string temporary = WriteTemporary(value);
            try { if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
