namespace MikroTikManager;

internal static class BackupSchedulePolicy
{
    internal static DateTime Minute(DateTime time) => new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);

    internal static void Validate(UpgradeJob job)
    {
        if (!Enum.IsDefined(job.Kind) || !Enum.IsDefined(job.Recurrence)) throw new ArgumentException("Unknown schedule type.");
        if (job.Kind == ScheduledJobKind.Upgrade && job.Recurrence != BackupRecurrence.Once)
            throw new ArgumentException("Recurring schedules are available for backups only.");
        if (job.Kind != ScheduledJobKind.Backup) return;
        if (job.RetentionDays is < 0 or > 3650) throw new ArgumentException("Retention must be 0–3650 days.");
        if (string.IsNullOrWhiteSpace(job.BackupFolder) || !Path.IsPathFullyQualified(job.BackupFolder)
            || job.BackupFolder.StartsWith(@"\\") || job.BackupFolder.StartsWith("//") || job.BackupFolder.Contains('"')
            || new DriveInfo(Path.GetPathRoot(job.BackupFolder)!).DriveType != DriveType.Fixed)
            throw new ArgumentException("Choose a folder on a local fixed drive. Network shares, mapped drives and removable drives are not supported for scheduled backups.");
    }

    internal static string JobFolder(UpgradeJob job) => Path.Combine(job.BackupFolder, "Schedule-" + job.Id.ToString("N"));

    internal static void Finish(UpgradeJob job, bool success, string result, DateTime now)
    {
        job.CompletedAt = now; job.LastRunResult = result; job.LastRunSuccessful = success;
        if (job.Kind != ScheduledJobKind.Backup || job.Recurrence == BackupRecurrence.Once)
        { job.State = success ? "Completed" : result; return; }
        int days = job.Recurrence == BackupRecurrence.Daily ? 1 : 7;
        DateTime next = job.ScheduledLocalTime;
        // Retain the original local time and weekday, skip missed occurrences.
        int periods = Math.Max(1, (int)Math.Floor((now - next).TotalDays / days) + 1);
        next = next.AddDays((long)periods * days);
        while (next <= now) next = next.AddDays(days);
        job.ScheduledLocalTime = next; job.State = "Scheduled";
    }
}
