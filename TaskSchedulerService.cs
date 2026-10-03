using System.Diagnostics;

namespace MikroTikManager;

public static class TaskSchedulerService
{
    public static async Task DeleteAsync(Guid jobId)
    {
        // Only the application's exact GUID task is removed. Missing tasks are already deleted.
        string script = "$ErrorActionPreference='Stop'; try { " +
            "Get-ScheduledTask -ErrorAction Stop | Where-Object { $_.TaskPath -eq '\\MikroTik Manager\\' -and $_.TaskName -eq '" + jobId.ToString("N") +
            "' } | Unregister-ScheduledTask -Confirm:$false -ErrorAction Stop; exit 0 } catch { exit 1 }";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows Task Scheduler cleanup.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows could not remove the scheduled task. The schedule has been retained. Check Task Scheduler access and try again.");
    }

    public static async Task RegisterAsync(UpgradeJob job, CancellationToken ct = default)
    {
        BackupSchedulePolicy.Validate(job);
        job.ScheduledLocalTime = BackupSchedulePolicy.Minute(job.ScheduledLocalTime);
        if (job.ScheduledLocalTime <= DateTime.Now.AddMinutes(1))
            throw new ArgumentException("The schedule must be at least one minute in the future.");

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the application executable.");
        string taskName = $"MikroTik Manager\\{job.Id:N}";
        string action = $"\"{exe}\" --run-job {job.Id:D}";
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (string argument in new[]
        {
            "/Create", "/TN", taskName, "/TR", action, "/SC", job.Recurrence switch { BackupRecurrence.Daily => "DAILY", BackupRecurrence.Weekly => "WEEKLY", _ => "ONCE" },
            "/SD", job.ScheduledLocalTime.ToString("MM/dd/yyyy"),
            "/ST", job.ScheduledLocalTime.ToString("HH:mm"),
            "/RU", "SYSTEM", "/RL", "HIGHEST", "/F"
        }) start.ArgumentList.Add(argument);
        if (job.Recurrence == BackupRecurrence.Weekly)
        {
            start.ArgumentList.Add("/D");
            start.ArgumentList.Add(job.ScheduledLocalTime.DayOfWeek.ToString()[..3].ToUpperInvariant());
        }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows Task Scheduler.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Task Scheduler returned exit code {process.ExitCode}.");
    }
}
