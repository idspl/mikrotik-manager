using System.Text;

namespace MikroTikManager;

internal static class RegressionTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MikroTikManager-tests-" + Guid.NewGuid().ToString("N"));
        var store = new SecureStore(directory);
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Guid done = Guid.NewGuid(), failedRouter = Guid.NewGuid(), pendingA = Guid.NewGuid(), pendingB = Guid.NewGuid(), interrupted = Guid.NewGuid();
        MaintenanceProgressRow[] queueRows = [
            new() { RouterId = pendingB }, new() { RouterId = done, Result = "Completed" },
            new() { RouterId = failedRouter, Result = "Failed", Attempt = 1 },
            new() { RouterId = interrupted, Result = "Interrupted", Stage = "Firmware upgrade", Attempt = 1 },
            new() { RouterId = pendingA }];
        Check(MainForm.PendingAfterFailures([done, failedRouter, pendingA, interrupted, pendingB], queueRows)
            .SequenceEqual(new[] { pendingA, pendingB }), "Skip failures continues only unstarted routers in original queue order");
        Check(MainForm.PendingAfterFailures([done, failedRouter], queueRows).Count == 0, "No remaining routers means no continuation");
        Check(MainForm.PendingAfterFailures([pendingA], [new() { RouterId = pendingA }]).Count == 0, "Skip-failed action requires an actual failure");
        // Match the website's Livewire array wrappers and exclude archived releases.
        const string releaseState = """
            {"memo":{"name":"components.software.router-OS"},"data":{
            "latestStableVersion":"7.24.5","latestTestingVersion":"7.24rc4",
            "releases":[[
            [{"version":"6.49.22","archived":false,"channels":[{"longTerm":true},{}]},{}],
            [{"version":"7.23.7","archived":false,"channels":[{"longTerm":true},{}]},{}],
            [{"version":"7.24.5","archived":false,"channels":[{"longTerm":false},{}]},{}],
            [{"version":"8.0","archived":true,"channels":[{"longTerm":true},{}]},{}]
            ],{}]}}
            """;
        var releases = RouterOsReleases.Parse("<div wire:snapshot=\"" + System.Net.WebUtility.HtmlEncode(releaseState) + "\"></div>");
        Check(releases == new RouterOsReleases("7.24.5", "7.23.7", "7.24rc4"), "Website channels and highest nonarchived long-term version");
        bool invalidReleasePage = false;
        try { RouterOsReleases.Parse("<html>Maintenance</html>"); }
        catch (FormatException) { invalidReleasePage = true; }
        Check(invalidReleasePage, "Website changes must not display fabricated versions");
        var router = new RouterRecord { Name = "Fixture", Username = "test", Password = "fixture-password" };
        router.Host = "192.0.2.1";
        var bundle = new ConfigurationBundle(1, [router], [], new AppSettings());
        byte[] archive = ConfigurationArchive.Encrypt(bundle, "test-export-password");
        Check(!Encoding.UTF8.GetString(archive).Contains(router.Password), "Archive secrets encrypted");
        Check(ConfigurationArchive.Decrypt(archive, "test-export-password").Routers[0].Password == router.Password, "Portable archive roundtrip");
        bool wrongPassword = false;
        try { ConfigurationArchive.Decrypt(archive, "wrong-password"); } catch (System.Security.Cryptography.CryptographicException) { wrongPassword = true; }
        Check(wrongPassword, "Reject incorrect archive password");
        archive[^1] ^= 1;
        bool tamperRejected = false;
        try { ConfigurationArchive.Decrypt(archive, "test-export-password"); } catch (System.Security.Cryptography.CryptographicException) { tamperRejected = true; }
        Check(tamperRejected, "Reject modified archive");
        store.ImportConfiguration(bundle);
        Check(store.LoadRouters()[0].Id == router.Id && !File.Exists(Path.Combine(directory, "import-pending.dat")), "Import transaction completed");
        Check(ReleaseVersion.IsNewerSameMajor("7.24.5", "7.24.4 (stable)"), "Version comparison accepts RouterOS suffix");
        Check(!ReleaseVersion.IsNewerSameMajor("7.24.5", "6.49.22"), "No automatic major upgrade selection");
        Check(!ReleaseVersion.IsNewerSameMajor("7.24rc4", "7.24"), "Do not select release candidate below stable");
        Check(ReleaseVersion.IsNewerSameMajor("7.24rc4", "7.24beta9"), "Release candidate ranks after beta");
        var scheduled = new UpgradeJob { State = "Scheduled", RouterIds = [router.Id] };
        store.SaveJobs([scheduled]);
        string mutexName = "MikroTikManager-test-" + Guid.NewGuid().ToString("N");
        using (var owner = new Mutex(true, mutexName))
        {
            // A competing launcher must not own the lock while the desktop is active.
            var blocked = Task.Run(() =>
            {
                using var contender = new Mutex(false, mutexName);
                return ScheduledJobCoordinator.Wait(contender, store, scheduled.Id, TimeSpan.FromMilliseconds(100));
            }).GetAwaiter().GetResult();
            Check(blocked == ScheduledOwnership.Failed, "Competing launcher cannot acquire active desktop lock");
            scheduled.State = "Completed"; store.SaveJobs([scheduled]);
            var completedByDesktop = Task.Run(() =>
            {
                using var contender = new Mutex(false, mutexName);
                return ScheduledJobCoordinator.Wait(contender, store, scheduled.Id, TimeSpan.FromSeconds(1));
            }).GetAwaiter().GetResult();
            Check(completedByDesktop == ScheduledOwnership.CompletedByDesktop, "Launcher sees desktop completion without replay");
            scheduled.State = "Scheduled"; store.SaveJobs([scheduled]); owner.ReleaseMutex();
            var takeover = Task.Run(() =>
            {
                using var contender = new Mutex(false, mutexName);
                var state = ScheduledJobCoordinator.Wait(contender, store, scheduled.Id, TimeSpan.FromSeconds(1));
                if (state == ScheduledOwnership.Acquired) contender.ReleaseMutex();
                return state;
            }).GetAwaiter().GetResult();
            Check(takeover == ScheduledOwnership.Acquired, "Launcher takes ownership after desktop closes");
        }
        store.SaveRouters([router]);
        Check(store.LoadRouters().Single().Password == router.Password, "DPAPI credentials roundtrip");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "routers.dat"))).Contains(router.Password), "Credentials must not be plaintext");
        var history = new MaintenanceHistory { RouterIds = [router.Id], CompletedIds = [] };
        history.Stages.Add(new HistoryStage(DateTime.Now, router.Id, router.Name, "Safety backup", 1, "Running", "Creating backup", "7.1", "7.1"));
        history.Save(store);
        Check(MaintenanceHistory.Load(store).Single().Stages.Single().Stage == "Safety backup", "History survives reload");
        history.CompletedIds.Add(router.Id); history.State = "Finished"; history.Save(store);
        Check(MaintenanceHistory.Load(store).Single().CompletedIds.Contains(router.Id), "Atomic history replacement");
        // Pause before the first router must never attempt an API connection.
        var engine = new UpgradeEngine(store, new AppSettings()) { PauseRequested = true };
        var expired = await new UpgradeEngine(store, new AppSettings()).RunSequentialAsync([router],
            new UpgradeRunOptions(FailureBehavior.Stop, 0, DateTime.Now.AddMinutes(-1)), CancellationToken.None);
        Check(expired.Routers.Count == 0, "Expired maintenance window must not connect to routers");
        var result = await engine.RunSequentialAsync([router], CancellationToken.None);
        Check(result.Routers.Count == 0, "Pause must prevent starting next router");
        Check(MaintenanceHistory.Load(store).First().State == "Paused", "Pause state persisted");
        string file = Path.Combine(directory, "hash-fixture");
        File.WriteAllText(file, "abc", new UTF8Encoding(false));
        Check(SelfUpdater.Hash(file) == "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", "SHA256 known vector");
        string target = Path.Combine(directory, "installed-fixture");
        File.WriteAllText(target, "old executable fixture");
        bool rejected = false;
        try { SelfUpdater.ReplaceVerified(file, target, new string('0', 64), target + ".new", target + ".previous"); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && File.ReadAllText(target) == "old executable fixture", "Checksum mismatch preserves installed file");
        SelfUpdater.ReplaceVerified(file, target, SelfUpdater.Hash(file), target + ".new", target + ".previous");
        Check(File.ReadAllText(target) == "abc", "Updater installs verified bytes at same path");
        Check(File.ReadAllText(target + ".previous") == "old executable fixture", "Updater retains recovery copy");
        // Also exercise the real message formatting (including TimeSpan formatting and redaction).
        var report = typeof(UpgradeEngine).GetMethod("Report", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        MaintenanceProgressUpdate? last = null;
        engine.Progress += update => last = update;
        report.Invoke(engine, [router, "Stability hold", 1, 90, "Running", "secret=" + router.Password]);
        Check(last is not null && last.Message.Contains("elapsed") && !last.Message.Contains(router.Password), "Progress formatting and secret redaction");
        // Files are isolated test fixtures. Retain them for CI diagnostics until runner cleanup.
    }
}
