namespace MikroTikManager;

internal static class BackupRegressionTests
{
    internal static async Task RunAsync(string directory)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var store = new SecureStore(Path.Combine(directory, "store"));
        var routers = Enumerable.Range(0, 8).Select(i => new RouterRecord { Name = "Backup fixture " + i, Host = "192.0.2." + (i + 1) }).ToList();
        store.SaveRouters(routers);
        var settings = new AppSettings { BackupRetentionDays = 0 };
        // Old saved backups defaulted to Stop and concurrency 1. Neither must constrain backup-only runs.
        var job = new UpgradeJob { Kind = ScheduledJobKind.Backup, BackupFolder = directory,
            FailureBehavior = FailureBehavior.Stop, MaxConcurrency = 1, FailureLimit = 1, RetentionDays = 0,
            RouterIds = routers.Select(r => r.Id).ToList() };
        var allStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0;
        var terminal = new System.Collections.Concurrent.ConcurrentDictionary<Guid, string>();
        var service = new RouterBackupService(settings) { DownloadOverride = async (router, backup, export, ct) =>
        {
            if (Interlocked.Increment(ref started) == routers.Count) allStarted.SetResult(true);
            await release.Task.WaitAsync(ct);
            if (router.Id == routers[0].Id) throw new OperationCanceledException("Simulated connection timeout");
            if (router.Id == routers[1].Id) throw new IOException("Simulated authentication failure");
            await File.WriteAllTextAsync(backup, "binary backup fixture " + router.Id, ct);
            await File.WriteAllTextAsync(export, "/system identity set name=fixture", ct);
        } };
        var run = ScheduledBackupRunner.RunAsync(store, settings, job, routers, null,
            update => { if (update.Result != "Running") terminal[update.RouterId] = update.Result; }, CancellationToken.None, () => service);
        try { await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.TrySetResult(true); }
        var result = await run;
        Check(started == 8 && result.Successful == 6 && result.Failed == 2 && result.Verified == 6,
            "Backup-only scheduling must start all devices and continue after timeout/ordinary failure");
        Check(terminal.Count == 8 && terminal.Values.Count(v => v == "Completed") == 6 && terminal.Values.Count(v => v == "Failed") == 2,
            "Every device must have an independent terminal result");
        var rows = await File.ReadAllLinesAsync(Path.Combine(result.Folder, "BackupManifest.csv"));
        Check(rows.Length == 9 && rows.Count(r => r.Contains("\"Success\"")) == 6 && rows.Count(r => r.Contains("\"Failed:")) == 2,
            "Parallel manifest must retain all complete result rows");
        var persisted = store.LoadRouters();
        Check(persisted.Sum(r => r.Backups.Count) == 6 && persisted.Count(r => r.LastBackupError.Length > 0) == 2
            && persisted.All(r => r.BackupPassword.Length > 0), "Concurrent scheduled completion must persist every history and password");
        Check(persisted.Single(r => r.Id == routers[0].Id).LastBackupError.Contains("timed out"), "Connection cancellation is a device timeout, not batch cancellation");

        // User cancellation must keep ownership until all in-flight cleanup finishes, with partial files removed.
        using var cancel = new CancellationTokenSource();
        var cancelRouters = Enumerable.Range(0, 3).Select(i => new RouterRecord { Name = "Cancellation " + i }).ToList();
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaning = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, cleaningCount = 0;
        var cancelService = new RouterBackupService(settings) { DownloadOverride = async (_, backup, export, ct) =>
        {
            await File.WriteAllTextAsync(backup, "partial file", ct);
            if (Interlocked.Increment(ref active) == 3) ready.SetResult(true);
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally
            {
                if (Interlocked.Increment(ref cleaningCount) == 3) cleaning.SetResult(true);
                await finishCleanup.Task;
                Interlocked.Decrement(ref active);
            }
        } };
        var cancelledRun = cancelService.BackupAllAsync(cancelRouters, Path.Combine(directory, "cancelled"), cancel.Token);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!cancelledRun.IsCompleted, "Cancellation must drain active backups before relinquishing job ownership");
        finishCleanup.SetResult(true);
        bool wasCancelled = false;
        try { await cancelledRun; } catch (OperationCanceledException) { wasCancelled = true; }
        Check(wasCancelled && active == 0 && cancelRouters.All(r => r.LastBackupError == "Cancelled"), "Explicit cancellation reaches all active devices");
        Check(!Directory.EnumerateFiles(Path.Combine(directory, "cancelled"), "*.backup", SearchOption.AllDirectories).Any(),
            "Cancelled partial downloads must be removed");
    }
}
