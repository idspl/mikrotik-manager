namespace MikroTikManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 1 && args[0] == "--self-test")
        {
            try { RegressionTests.RunAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { File.WriteAllText("self-test-failure.log", ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 5 && args[0] == "--apply-update") { SelfUpdater.Apply(args); return; }
        using var instance = new Mutex(false, SelfUpdater.InstanceName);
        bool owned;
        try { owned = instance.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            if (args.Length == 2 && args[0] == "--run-job" && Guid.TryParse(args[1], out Guid queuedJob))
            {
                var outcome = ScheduledJobCoordinator.Wait(instance, new SecureStore(), queuedJob, TimeSpan.FromHours(24));
                owned = outcome == ScheduledOwnership.Acquired;
                if (!owned) { Environment.ExitCode = outcome == ScheduledOwnership.CompletedByDesktop ? 0 : 2; return; }
            }
            else { MessageBox.Show("MikroTik Manager or a scheduled job is already running."); return; }
        }
        try
        {
            new SecureStore().CompletePendingImport();
            if (args.Length == 2 && args[0].Equals("--run-job", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(args[1], out Guid jobId))
            {
                Task.Run(() => RunScheduledJobAsync(jobId)).GetAwaiter().GetResult();
                return;
            }
            Application.Run(new SplashForm());
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MikroTik Manager", "Logs");
            try
            {
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "startup-crash.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{ex}\r\n\r\n");
            }
            catch { }
            MessageBox.Show(
                $"MikroTik Manager could not start.\n\n{ex.Message}\n\nDetails were written to:\n{root}\\startup-crash.log",
                "MikroTik Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally { instance.ReleaseMutex(); }
    }

    private static async Task RunScheduledJobAsync(Guid jobId)
    {
        var store = new SecureStore();
        List<UpgradeJob> jobs = store.LoadJobs();
        UpgradeJob? job = jobs.FirstOrDefault(x => x.Id == jobId);
        if (job is null || job.State != "Scheduled") { Environment.ExitCode = job?.State == "Completed" ? 0 : 2; return; }
        if (job.ScheduledLocalTime > DateTime.Now)
        { Environment.ExitCode = job.Kind == ScheduledJobKind.Backup && job.CompletedAt is not null && job.LastRunSuccessful ? 0 : 2; return; }
        string? backupSummary = null;
        job.State = "Running";
        job.StartedAt = DateTime.Now;
        store.SaveJobs(jobs);
        List<RouterRecord> routers = store.LoadRouters();
        var selected = job.RouterIds.Select(id => routers.FirstOrDefault(x => x.Id == id)).Where(x => x is not null).Cast<RouterRecord>().ToList();
        try
        {
            if (selected.Count == 0 || selected.Count != job.RouterIds.Count) throw new InvalidOperationException("Scheduled routers are missing or empty.");
            if (job.Kind == ScheduledJobKind.Backup)
            {
                var backup = await ScheduledBackupRunner.RunAsync(store, store.LoadSettings(), job, selected, null, null, CancellationToken.None);
                job.State = backup.Failed == 0 ? "Completed" : $"Completed with {backup.Failed} backup failures";
                backupSummary = job.LastRunResult = $"Backup: {backup.Successful} successful, {backup.Failed} failed; {backup.Folder}";
            }
            else
            {
            var engine = new UpgradeEngine(store, store.LoadSettings());
            MaintenanceRunResult result = await engine.RunSequentialAsync(selected,
                new UpgradeRunOptions(job.FailureBehavior, job.RetryCount, job.WindowEnd, MaxConcurrency: job.MaxConcurrency, FailureLimit: job.FailureLimit), CancellationToken.None);
            await new MaintenanceReportService(store).WriteAsync(result);
            job.State = result.Failed == 0 && result.Routers.Count == selected.Count
                ? "Completed"
                : $"Completed with {result.Failed} failed and {selected.Count - result.Routers.Count} not processed";
            }
        }
        catch (Exception ex)
        {
            job.State = "Failed: " + ex.Message;
        }
        finally
        {
            bool success = job.State == "Completed";
            string result = backupSummary ?? job.State;
            BackupSchedulePolicy.Finish(job, success, result, DateTime.Now);
            store.SaveRouters(routers);
            store.SaveJobs(jobs);
            Environment.ExitCode = success ? 0 : 2;
        }
    }
}
