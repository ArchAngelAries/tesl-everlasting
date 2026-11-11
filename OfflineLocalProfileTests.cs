using System;
using System.IO;

namespace OfflineEngine
{
    public static class OfflineLocalProfileTests
    {
        // Isolated per-case workspaces; never opens the real user profile.
        public static void Run(string outputDirectory, Action<string, Action> test)
        {
            string root = Path.Combine(outputDirectory, "profile-tests-" + Guid.NewGuid().ToString("N"));
            test("local-profile-identity-name-and-ledger-roundtrip", delegate {
                string workspace = Path.Combine(root, "roundtrip");
                OfflineLocalProfile profile = OfflineLocalProfile.Open(workspace);
                string identity = profile.Identity;
                profile.SetUsername("  Dovahkiin \u2605  ");
                profile.RecordResult(OfflineLocalProfile.CreateMatchId(), true);
                profile.RecordResult(OfflineLocalProfile.CreateMatchId(), false);
                OfflineLocalProfile loaded = OfflineLocalProfile.Open(workspace);
                Require(loaded.Identity == identity && loaded.Username == "Dovahkiin \u2605", "Identity/name did not roundtrip.");
                Require(loaded.MatchesCompleted == 2 && loaded.MatchesWon == 1 && loaded.MatchesLost == 1, "Terminal counters did not roundtrip.");
            });
            test("local-profile-idempotent-results-and-stale-instance-writes", delegate {
                string workspace = Path.Combine(root, "idempotence");
                OfflineLocalProfile first = OfflineLocalProfile.Open(workspace), stale = OfflineLocalProfile.Open(workspace);
                string match = OfflineLocalProfile.CreateMatchId();
                Require(first.RecordResult(match, true) && !stale.RecordResult(match, true), "Duplicate completion was counted.");
                Reject(delegate { stale.RecordResult(match, false); });
                stale.SetUsername("Updated Player");
                first.RecordResult(OfflineLocalProfile.CreateMatchId(), false);
                OfflineLocalProfile loaded = OfflineLocalProfile.Open(workspace);
                Require(loaded.Username == "Updated Player" && loaded.MatchesCompleted == 2, "Stale writer lost committed data.");
                Require(!loaded.RecordResult(match, true), "Restart lost idempotence ledger.");
            });
            test("local-profile-corrupt-primary-recovers-backup", delegate {
                string workspace = Path.Combine(root, "recovery");
                OfflineLocalProfile profile = OfflineLocalProfile.Open(workspace);
                string identity = profile.Identity;
                profile.SetUsername("Recovered Name");
                profile.RecordResult(OfflineLocalProfile.CreateMatchId(), true);
                File.WriteAllText(profile.StoragePath, "{broken");
                OfflineLocalProfile recovered = OfflineLocalProfile.Open(workspace);
                Require(recovered.Identity == identity && recovered.Username == "Recovered Name" && recovered.MatchesCompleted == 0, "Valid previous generation was not recovered.");
                Require(File.Exists(profile.StoragePath + ".bak"), "Recovery destroyed backup.");
                Require(Directory.GetFiles(Path.GetDirectoryName(profile.StoragePath), "profile.json.corrupt-*").Length == 1, "Damaged primary was not preserved.");
                Require(OfflineLocalProfile.Open(workspace).Identity == identity, "Recovered primary is invalid.");
            });
            test("local-profile-missing-primary-recovers-backup", delegate {
                string workspace = Path.Combine(root, "missing");
                OfflineLocalProfile profile = OfflineLocalProfile.Open(workspace);
                string identity = profile.Identity; profile.SetUsername("Second Generation");
                File.Delete(profile.StoragePath);
                Require(OfflineLocalProfile.Open(workspace).Identity == identity, "Missing primary regenerated identity.");
            });
            test("local-profile-invalid-data-is-preserved", delegate {
                string workspace = Path.Combine(root, "invalid");
                OfflineLocalProfile profile = OfflineLocalProfile.Open(workspace);
                profile.SetUsername("Valid");
                File.WriteAllText(profile.StoragePath, "broken primary");
                File.WriteAllText(profile.StoragePath + ".bak", "broken backup");
                Reject(delegate { OfflineLocalProfile.Open(workspace); });
                Require(File.ReadAllText(profile.StoragePath) == "broken primary" && File.ReadAllText(profile.StoragePath + ".bak") == "broken backup", "Unrecoverable data was overwritten.");
            });
            test("local-profile-future-schema-is-preserved", delegate {
                string workspace = Path.Combine(root, "future");
                OfflineLocalProfile profile = OfflineLocalProfile.Open(workspace); profile.SetUsername("Valid");
                string future = File.ReadAllText(profile.StoragePath).Replace("\"schema\":1", "\"schema\":2");
                Require(future.IndexOf("\"schema\":2") >= 0, "Schema fixture was not modified.");
                File.WriteAllText(profile.StoragePath, future);
                Reject(delegate { OfflineLocalProfile.Open(workspace); });
                Require(File.ReadAllText(profile.StoragePath) == future, "Future schema was rolled back.");
            });
            test("local-profile-name-and-match-id-validation", delegate {
                string workspace = Path.Combine(root, "validation");
                OfflineLocalProfile profile = OfflineLocalProfile.Open(workspace);
                string[] invalid = { null, " ", new string('a', 33), "Line\nBreak", "<b>Name</b>", "\ud800" };
                foreach (string value in invalid) { string captured = value; Reject(delegate { profile.SetUsername(captured); }); }
                Reject(delegate { profile.RecordResult("remote-id", true); });
                Require(OfflineLocalProfile.Open(workspace).Username == "Offline Player" && profile.MatchesCompleted == 0, "Invalid updates changed stored data.");
            });
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Reject(Action action)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            catch (InvalidOperationException) { return; }
            catch (IOException) { return; }
            catch (NotSupportedException) { return; }
            throw new InvalidOperationException("Invalid operation was accepted.");
        }
    }
}
