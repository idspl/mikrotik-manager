using System.Diagnostics;

namespace IndigoRouterScheduler;

public static class TaskSchedulerService
{
    public static async Task RegisterAsync(UpgradeJob job, CancellationToken ct = default)
    {
        if (job.ScheduledLocalTime <= DateTime.Now.AddMinutes(1))
            throw new ArgumentException("The schedule must be at least one minute in the future.");

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the application executable.");
        string taskName = $"Indigo Router Scheduler\\{job.Id:N}";
        string action = $"\"{exe}\" --run-job {job.Id:D}";
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (string argument in new[]
        {
            "/Create", "/TN", taskName, "/TR", action, "/SC", "ONCE",
            "/SD", job.ScheduledLocalTime.ToString("MM/dd/yyyy"),
            "/ST", job.ScheduledLocalTime.ToString("HH:mm"),
            "/RU", "SYSTEM", "/RL", "HIGHEST", "/F"
        }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows Task Scheduler.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Task Scheduler returned exit code {process.ExitCode}.");
    }
}
