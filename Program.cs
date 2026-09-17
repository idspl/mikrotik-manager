namespace MikroTikManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
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
    }

    private static async Task RunScheduledJobAsync(Guid jobId)
    {
        var store = new SecureStore();
        List<UpgradeJob> jobs = store.LoadJobs();
        UpgradeJob? job = jobs.FirstOrDefault(x => x.Id == jobId);
        if (job is null) return;
        job.State = "Running";
        job.StartedAt = DateTime.Now;
        store.SaveJobs(jobs);
        List<RouterRecord> routers = store.LoadRouters();
        var selected = job.RouterIds.Select(id => routers.FirstOrDefault(x => x.Id == id)).Where(x => x is not null).Cast<RouterRecord>().ToList();
        try
        {
            var engine = new UpgradeEngine(store, store.LoadSettings());
            MaintenanceRunResult result = await engine.RunSequentialAsync(selected,
                new UpgradeRunOptions(job.FailureBehavior, job.RetryCount), CancellationToken.None);
            await new MaintenanceReportService(store).WriteAsync(result);
            job.State = result.Failed == 0 && result.Routers.Count == selected.Count
                ? "Completed"
                : $"Completed with {result.Failed} failed and {selected.Count - result.Routers.Count} not processed";
        }
        catch (Exception ex)
        {
            job.State = "Failed: " + ex.Message;
        }
        finally
        {
            job.CompletedAt = DateTime.Now;
            store.SaveRouters(routers);
            store.SaveJobs(jobs);
        }
    }
}
