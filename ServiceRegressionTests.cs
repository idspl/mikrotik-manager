namespace MikroTikManager;
internal static class ServiceRegressionTests
{
    internal static async Task RunAsync(string directory)
    {
        var store = new SecureStore(directory);
        var due = new UpgradeJob { ScheduledLocalTime = DateTime.Now.AddMinutes(-1) };
        var future = new UpgradeJob { ScheduledLocalTime = DateTime.Now.AddHours(1) };
        var interrupted = new UpgradeJob { State = "Running", ScheduledLocalTime = DateTime.Now.AddMinutes(-2) };
        store.SaveJobs([due, future, interrupted]);
        string name = "MikroTikManager-test-" + Guid.NewGuid().ToString("N");
        static void Check(bool result, string message) { if (!result) throw new Exception(message); }
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var owner = new Thread(() => { using var gate = new Mutex(true, name); held.Set(); release.Wait(); gate.ReleaseMutex(); });
        owner.Start(); held.Wait();
        try { Check(!SchedulerWindowsService.Tick(store, CancellationToken.None, name, (_, _) => throw new Exception("Duplicate execution")), "Service must defer to desktop owner"); }
        finally { release.Set(); owner.Join(); }
        int calls = 0;
        bool ran = await Task.Run(() => SchedulerWindowsService.Tick(store, CancellationToken.None, name, (id, _) => {
            Check(id == due.Id, "Only due scheduled job can run"); calls++;
            var jobs = store.LoadJobs(); jobs.Single(j => j.Id == id).State = "Completed"; store.SaveJobs(jobs);
        }));
        Check(ran && calls == 1, "Service takes over after desktop releases ownership");
        Check(store.LoadJobs().Single(j => j.Id == interrupted.Id).State.StartsWith("Interrupted"), "Interrupted job must not replay");
        Check(!SchedulerWindowsService.Tick(store, CancellationToken.None, name, (_, _) => calls++), "Completed/future jobs must not run");
        Check(calls == 1, "No duplicate dispatch");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { SchedulerWindowsService.Tick(store, cancel.Token, name); throw new Exception("Cancelled service dispatched"); }
        catch (OperationCanceledException) { }
        using var afterCancel = new Mutex(false, name);
        Check(afterCancel.WaitOne(0), "Cancellation releases scheduler lock"); afterCancel.ReleaseMutex();
    }
}
