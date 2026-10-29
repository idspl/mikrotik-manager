using System.ServiceProcess;

namespace MikroTikManager;

// SCM owns the service; a dedicated OS thread owns the application mutex.
internal sealed class SchedulerWindowsService : ServiceBase
{
    internal const string Name = "IndigoMikroTikScheduler";
    private readonly CancellationTokenSource _stop = new();
    private Thread? _worker;
    internal SchedulerWindowsService() { ServiceName = Name; CanStop = true; CanShutdown = true; AutoLog = true; }
    protected override void OnStart(string[] args)
    {
        _worker = new Thread(Loop) { IsBackground = true, Name = "Scheduled maintenance" };
        _worker.Start();
    }
    private void Loop()
    {
        var store = new SecureStore();
        while (!_stop.IsCancellationRequested)
        {
            try { Tick(store, _stop.Token); }
            catch (Exception ex) { Log(store, "Scheduler error: " + ex.Message); }
            _stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
        }
    }
    internal static bool Tick(SecureStore store, CancellationToken ct, string? mutexName = null,
        Action<Guid, CancellationToken>? execute = null)
    {
        using var instance = new Mutex(false, mutexName ?? SelfUpdater.InstanceName);
        bool owned;
        try { owned = instance.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) return false; // The desktop runs its own due jobs while open.
        try
        {
            ct.ThrowIfCancellationRequested();
            store.CompletePendingImport();
            var jobs = store.LoadJobs();
            bool changed = false;
            foreach (var interrupted in jobs.Where(j => j.State == "Running"))
            {
                interrupted.State = "Interrupted — review before running again";
                interrupted.LastRunResult = "Previous process stopped during execution. Automatic replay disabled.";
                changed = true;
            }
            if (changed) store.SaveJobs(jobs);
            var due = jobs.Where(j => j.State == "Scheduled" && j.ScheduledLocalTime <= DateTime.Now)
                .OrderBy(j => j.ScheduledLocalTime).FirstOrDefault();
            if (due is null) return false;
            Log(store, "Starting " + due.Id + " / " + due.Kind);
            if (execute is not null) execute(due.Id, ct);
            else Program.RunScheduledJobAsync(due.Id, ct, store).GetAwaiter().GetResult();
            Log(store, "Finished " + due.Id);
            return true;
        }
        finally { instance.ReleaseMutex(); }
    }
    internal static void Log(SecureStore store, string message)
    {
        try { File.AppendAllText(Path.Combine(store.LogDirectory, "scheduler-service.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); } catch { }
    }
    protected override void OnStop()
    {
        _stop.Cancel();
        // Drain API/file work before SCM reports stopped; installer never force-kills jobs.
        while (_worker is { IsAlive: true }) { RequestAdditionalTime(15000); _worker.Join(1000); }
    }
    protected override void OnShutdown() => OnStop();
}
