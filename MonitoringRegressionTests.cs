namespace MikroTikManager;

internal static class MonitoringRegressionTests
{
    internal static async Task RunAsync(string folder)
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Directory.CreateDirectory(folder);
        var time = DateTime.Now;
        var resource = new Dictionary<string, string> { ["cpu-load"] = "12.5", ["free-memory"] = "1048576", ["total-memory"] = "4194304", ["uptime"] = "3d" };
        var modern = HealthMonitor.Parse(resource, [new Dictionary<string, string> { ["name"] = "cpu-temperature", ["value"] = "48" }], time);
        Check(modern.CpuPercent == 12.5 && modern.FreeMemory == 1048576 && modern.FreeStorage is null && modern.Temperature.Contains("48"), "Resource/sensor parsing handles missing storage");
        Check(HealthMonitor.Parse(resource, [new Dictionary<string, string> { ["temperature"] = "41" }], time).Temperature.Contains("41"), "Legacy temperature shape is supported");
        Check(HealthMonitor.Parse(new Dictionary<string, string>(), [], time).Temperature == "Unavailable", "Unsupported sensors must not be shown as zero");
        var router = new RouterRecord { Health = modern, LastHealthCheckAt = time };
        Check(HealthMonitor.Status(router, time, 5) == "Stale", "Saved readings must be stale until observed this session");
        router.HealthObservedThisSession = true;
        Check(HealthMonitor.Status(router, time, 5) == "Online" && HealthMonitor.Status(router, time.AddMinutes(11), 5) == "Stale", "Fresh readings expire");
        router.LastHealthError = "Timeout";
        Check(HealthMonitor.Status(router, time, 5) == "Offline", "Failed current checks are offline");
        var json = System.Text.Json.JsonSerializer.Serialize(router);
        Check(!System.Text.Json.JsonSerializer.Deserialize<RouterRecord>(json)!.HealthObservedThisSession, "Session freshness must not persist");
        var fleet = Enumerable.Range(0, 20).Select(i => new RouterRecord { Name = "Health " + i }).ToList();
        int active = 0, peak = 0, completed = 0;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var poll = HealthMonitor.PollAsync(fleet, new(), () => Interlocked.Increment(ref completed), CancellationToken.None, async (r, _, ct) => {
            int current = Interlocked.Increment(ref active); int old;
            do { old = peak; } while (current > old && Interlocked.CompareExchange(ref peak, current, old) != old);
            if (current == 8) entered.TrySetResult(true);
            try { await gate.Task.WaitAsync(ct); if (r == fleet[0]) throw new OperationCanceledException("device timeout"); return modern; }
            finally { Interlocked.Decrement(ref active); }
        });
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); } finally { gate.TrySetResult(true); }
        await poll;
        Check(peak == 8 && active == 0 && completed == 20 && fleet.Count(r => r.LastHealthError.Length > 0) == 1, "Health polling bounds concurrency and isolates timeout failures");
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = HealthMonitor.PollAsync(fleet, new(), null, cancel.Token, async (_, _, ct) => { started.TrySetResult(true); await Task.Delay(Timeout.Infinite, ct); return modern; });
        await started.Task; cancel.Cancel(); bool stopped = false;
        try { await cancelled; } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "Explicit polling cancellation propagates");
        string before = Path.Combine(folder, "before.rsc"), after = Path.Combine(folder, "after.rsc");
        await File.WriteAllTextAsync(before, "# 2026-10-01 10:00:00 by RouterOS 7\n/interface\nadd name=a");
        await File.WriteAllTextAsync(after, "# 2026-10-04 10:00:00 by RouterOS 7\n/interface\nadd name=a");
        Check(await BackupChangeDetector.CompareAsync(before, after, CancellationToken.None) == "Unchanged", "Backup timestamp-only changes are ignored");
        await File.AppendAllTextAsync(after, "\nadd name=b");
        Check(await BackupChangeDetector.CompareAsync(before, after, CancellationToken.None) == "Changed", "Configuration differences are detected");
        Check(await BackupChangeDetector.CompareAsync(null, after, CancellationToken.None) == "Baseline created", "First backup creates a baseline");
        Check(await BackupChangeDetector.CompareAsync(before + "missing", after, CancellationToken.None) == "Previous export missing", "Missing files are not treated as unchanged");
        Check(new UpgradeJob().TypeLabel == "Backup + upgrade" && new UpgradeJob { Kind = ScheduledJobKind.Backup }.TypeLabel == "Backup only", "Job type describes actual action, not its name");
    }
}
