using System.Text;

namespace MikroTikManager;

internal static class RegressionTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MikroTikManager-tests-" + Guid.NewGuid().ToString("N"));
        var store = new SecureStore(directory);
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        // Fake device operations exercise dispatch without contacting a router.
        var fleet = Enumerable.Range(0, 6).Select(i => new RouterRecord { Name = "Batch " + i }).ToList();
        static RouterRunResult Result(RouterRecord r, string outcome = "Completed") => new(r.Id, r.Name, r.Host, outcome, 1, "", "", "fixture", DateTime.Now, DateTime.Now);
        int activeCount = 0, peak = 0;
        var parallel = await BatchUpgradeQueue.RunAsync(fleet, 2, 0, () => false, async r => {
            int now = Interlocked.Increment(ref activeCount); int old; do { old = peak; } while (now > old && Interlocked.CompareExchange(ref peak, now, old) != old); await Task.Delay(10); Interlocked.Decrement(ref activeCount); return Result(r);
        }, CancellationToken.None);
        Check(parallel.Count == 6 && peak == 2 && activeCount == 0, "Parallel dispatcher enforces concurrency and waits for completion");
        TaskCompletionSource<RouterRunResult> releaseA = new(), releaseB = new TaskCompletionSource<RouterRunResult>();
        int dispatched = 0;
        var limited = BatchUpgradeQueue.RunAsync(fleet, 2, 1, () => false, r => ++dispatched == 1 ? releaseA.Task : releaseB.Task, CancellationToken.None);
        releaseA.SetResult(Result(fleet[0], "Failed"));
        await Task.Delay(20);
        Check(dispatched == 2 && !limited.IsCompleted, "Failure stops new starts while active router continues");
        releaseB.SetResult(Result(fleet[1]));
        Check((await limited).Count == 2, "Failure limit leaves unstarted routers pending");
        bool paused = false; dispatched = 0;
        await BatchUpgradeQueue.RunAsync(fleet, 1, 0, () => paused, r => { dispatched++; paused = true; return Task.FromResult(Result(r)); }, CancellationToken.None);
        Check(dispatched == 1, "Pause blocks new starts");
        var allGate = new TaskCompletionSource<bool>(); dispatched = 0;
        var allAtOnce = BatchUpgradeQueue.RunAsync(fleet, 0, 0, () => false, async r => { dispatched++; await allGate.Task; return Result(r); }, CancellationToken.None);
        Check(dispatched == fleet.Count, "All-at-once mode opens every selected slot"); allGate.SetResult(true); await allAtOnce;
        using (var cancelQueue = new CancellationTokenSource())
        {
            var gate = new TaskCompletionSource<bool>(); dispatched = 0;
            var cancelledQueue = BatchUpgradeQueue.RunAsync(fleet, 2, 0, () => false, async r => { dispatched++; await gate.Task; return Result(r); }, cancelQueue.Token);
            cancelQueue.Cancel();
            Check(!cancelledQueue.IsCompleted && dispatched == 2, "Cancellation retains ownership until active operations finish");
            gate.SetResult(true); bool cancelled = false;
            try { await cancelledQueue; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && dispatched == 2, "Cancellation does not launch pending devices");
        }
        var diff = ConfigDiff.Compare("# 2026-10-01 10:00:00 by RouterOS 7.24\n/interface\nadd name=a", "# 2026-10-03 11:00:00 by RouterOS 7.24\n/interface\nadd name=b");
        Check(diff.Count(x => x.Kind == "+") == 1 && diff.Count(x => x.Kind == "−") == 1, "Diff ignores only export timestamps and retains configuration changes");
        Check(!ConfigDiff.Display("password=secret", false).Contains("secret") && ConfigDiff.Display("password=secret", true).Contains("secret"), "Compare masks content unless explicitly revealed");
        // Construct every tab on the STA entry thread without showing the form or contacting routers.
        // Run form construction on its own STA thread after async dispatcher tests.
        Exception? uiFailure = null;
        var uiThread = new Thread(() => { try { foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F }) { using var form = new MainForm(new SecureStore(Path.Combine(directory, "ui-" + scale)), smokeTest: true); form.RenderSmokeViews(scale); Check(form.Text.Contains("0.5.1"), "Dashboard and menus construct on Windows"); } } catch (Exception ex) { uiFailure = ex; } });
        uiThread.IsBackground = true; uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start();
        Check(uiThread.Join(TimeSpan.FromMinutes(3)), "UI layout checks timed out; see ui-layout-check.log");
        Check(uiFailure is null, "Windows workspace construction: " + uiFailure);
        var settings = new AppSettings { UpdateChannel = "long-term" };
        var channelRouter = new RouterRecord();
        Check(RouterChannels.Resolve(channelRouter, settings) == "long-term", "Default channel inherits settings");
        channelRouter.UpdateChannel = "testing";
        Check(RouterChannels.Resolve(channelRouter, settings) == "testing", "Router channel overrides settings");
        bool invalidChannel = false;
        try { RouterChannels.Resolve(new RouterRecord { UpdateChannel = "invalid" }, settings); } catch (InvalidOperationException) { invalidChannel = true; }
        Check(invalidChannel, "Unknown channel rejected");
        var due = new DateTime(2026, 10, 3, 2, 30, 0);
        var daily = new UpgradeJob { Kind = ScheduledJobKind.Backup, Recurrence = BackupRecurrence.Daily, ScheduledLocalTime = due, BackupFolder = directory };
        BackupSchedulePolicy.Validate(daily);
        BackupSchedulePolicy.Finish(daily, false, "One router failed", due.AddDays(3).AddHours(1));
        Check(daily.State == "Scheduled" && daily.ScheduledLocalTime == due.AddDays(4) && !daily.LastRunSuccessful && daily.LastRunResult == "One router failed", "Daily failure advances past missed runs and retains outcome");
        var weekly = new UpgradeJob { Kind = ScheduledJobKind.Backup, Recurrence = BackupRecurrence.Weekly, ScheduledLocalTime = due, BackupFolder = directory };
        BackupSchedulePolicy.Finish(weekly, true, "Done", due.AddDays(16));
        Check(weekly.ScheduledLocalTime == due.AddDays(21), "Weekly catchup preserves weekday and local time");
        var once = new UpgradeJob { Kind = ScheduledJobKind.Backup, ScheduledLocalTime = due };
        BackupSchedulePolicy.Finish(once, true, "Done", due.AddMinutes(2));
        Check(once.State == "Completed" && once.ScheduledLocalTime == due, "One-time backup never repeats");
        var manual = new UpgradeJob { Kind = ScheduledJobKind.Backup, Recurrence = BackupRecurrence.Daily, ScheduledLocalTime = due.AddDays(1) };
        BackupSchedulePolicy.Finish(manual, true, "Manual backup done", due, "Scheduled");
        Check(manual.State == "Scheduled" && manual.ScheduledLocalTime == due.AddDays(1), "Run Now preserves a future recurring backup");
        BackupSchedulePolicy.Finish(manual, false, "Manual backup failed", due, "Disabled");
        Check(manual.State == "Disabled" && !manual.LastRunSuccessful && manual.ScheduledLocalTime == due.AddDays(1), "Manual backup cannot enable a disabled schedule");
        BackupSchedulePolicy.Finish(manual, true, "Manual backup done", due.AddDays(3), "Imported — disabled");
        Check(manual.State == "Imported — disabled" && manual.ScheduledLocalTime == due.AddDays(1), "Manual imported backup remains disabled even when overdue");
        BackupSchedulePolicy.Finish(manual, true, "Manual backup done", due.AddDays(3), "Scheduled");
        Check(manual.State == "Scheduled" && manual.ScheduledLocalTime == due.AddDays(4), "Run Now consumes an overdue recurring occurrence once");
        var manualUpgrade = new UpgradeJob { ScheduledLocalTime = due.AddDays(1) };
        BackupSchedulePolicy.Finish(manualUpgrade, true, "Upgrade done", due, "Scheduled");
        Check(manualUpgrade.State == "Completed", "Run Now consumes a one-time upgrade instead of replaying later");
        Check(BackupSchedulePolicy.JobFolder(daily) != BackupSchedulePolicy.JobFolder(weekly), "Retention directories isolated per schedule");
        bool recurringUpgrade = false;
        try { BackupSchedulePolicy.Validate(new UpgradeJob { Recurrence = BackupRecurrence.Daily }); } catch (ArgumentException) { recurringUpgrade = true; }
        Check(recurringUpgrade, "Upgrade schedules cannot repeat implicitly");
        Check(BackupSchedulePolicy.Minute(due.AddSeconds(57)) == due, "Scheduler time matches Windows minute precision");
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
        router.UpdateChannel = "long-term";
        daily.RouterIds = [router.Id];
        var v2 = new ConfigurationBundle(2, [router], [daily], new AppSettings());
        var restored = ConfigurationArchive.Decrypt(ConfigurationArchive.Encrypt(v2, "test-export-password"), "test-export-password");
        Check(restored.Version == 2 && restored.Routers[0].UpdateChannel == "long-term" && restored.Jobs[0].Kind == ScheduledJobKind.Backup && restored.Jobs[0].Recurrence == BackupRecurrence.Daily, "Version 2 archive preserves backup and channel settings");
        daily.Recurrence = (BackupRecurrence)99;
        bool badRecurrence = false;
        try { ConfigurationArchive.Decrypt(ConfigurationArchive.Encrypt(v2, "test-export-password"), "test-export-password"); } catch (InvalidDataException) { badRecurrence = true; }
        Check(badRecurrence, "Unknown recurrence rejected on import");
        daily.Recurrence = BackupRecurrence.Daily;
        var legacy = System.Text.Json.JsonSerializer.Deserialize<UpgradeJob>("{\"Name\":\"Old upgrade\"}")!;
        Check(legacy.Kind == ScheduledJobKind.Upgrade && legacy.Recurrence == BackupRecurrence.Once, "Legacy schedules remain one-time upgrades");
        Check(legacy.MaxConcurrency == 1, "Existing schedules remain sequential");
        router.Site = "Davanagere"; router.Tags = "Client-end, Critical"; daily.MaxConcurrency = 5;
        var v3 = new ConfigurationBundle(3, [router], [daily], new AppSettings());
        var migrated = ConfigurationArchive.Decrypt(ConfigurationArchive.Encrypt(v3, "test-export-password"), "test-export-password");
        Check(migrated.Routers[0].Site == "Davanagere" && migrated.Routers[0].Tags.Contains("Client-end") && migrated.Jobs[0].MaxConcurrency == 5, "Schema 3 retains sites, tags and batch controls");
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
            daily.ScheduledLocalTime = DateTime.Now.AddDays(1); daily.LastRunSuccessful = true; store.SaveJobs([daily]);
            var recurringDone = Task.Run(() => {
                using var contender = new Mutex(false, mutexName);
                return ScheduledJobCoordinator.Wait(contender, store, daily.Id, TimeSpan.FromSeconds(1));
            }).GetAwaiter().GetResult();
            Check(recurringDone == ScheduledOwnership.CompletedByDesktop, "Recurring desktop completion does not replay backup");
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
