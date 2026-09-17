namespace MikroTikManager;

public sealed class RouterRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Host { get; set; } = "";
    public int ApiPort { get; set; } = 8728;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string BackupPassword { get; set; } = "";
    public string ApiStatus { get; set; } = "Not checked";
    public string LastStatus { get; set; } = "Not checked";
    public string Model { get; set; } = "";
    public string RouterOsVersion { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
}

public sealed class UpgradeJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTime ScheduledLocalTime { get; set; }
    public List<Guid> RouterIds { get; set; } = [];
    public int RouterCount => RouterIds.Count;
    public string State { get; set; } = "Scheduled";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public FailureBehavior FailureBehavior { get; set; } = FailureBehavior.Stop;
    public int RetryCount { get; set; } = 1;
}

public enum FailureBehavior
{
    Stop,
    Skip,
    RetryThenSkip,
    RetryThenStop
}

public sealed class AppSettings
{
    public int DefaultApiPort { get; set; } = 8728;
    public bool UseApiSsl { get; set; }
    public bool AllowInvalidTlsCertificate { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 120;
    public int ReconnectTimeoutMinutes { get; set; } = 3;
    public int StableOnlineSeconds { get; set; } = 10;
    public string UpdateChannel { get; set; } = "stable";
    public FailureBehavior DefaultFailureBehavior { get; set; } = FailureBehavior.RetryThenSkip;
    public int RetryCount { get; set; } = 1;
    public int MinimumFreeDiskMb { get; set; } = 16;
    public int BackupRetentionDays { get; set; } = 30;
    public bool CheckForUpdatesAtStartup { get; set; } = true;
}

public sealed record ApiReply(string Type, IReadOnlyDictionary<string, string> Attributes);
public sealed record RouterSnapshot(string Identity, string Model, string RouterOsVersion, string CurrentFirmware, string UpgradeFirmware);
public sealed record BulkBackupResult(int Successful, int Failed, string Folder, int Verified);
public sealed record RouterHealthCheck(
    RouterRecord Router,
    bool Passed,
    string Summary,
    RouterSnapshot? Snapshot,
    long FreeDiskBytes,
    DateTime CheckedAt);

public sealed record UpgradeRunOptions(FailureBehavior FailureBehavior, int RetryCount);
public sealed record RouterRunResult(
    Guid RouterId,
    string Router,
    string Address,
    string Result,
    int Attempts,
    string RouterOsVersion,
    string FirmwareVersion,
    string Message,
    DateTime StartedAt,
    DateTime CompletedAt);
public sealed record MaintenanceRunResult(DateTime StartedAt, DateTime CompletedAt, IReadOnlyList<RouterRunResult> Routers)
{
    public int Successful => Routers.Count(x => x.Result == "Completed");
    public int Failed => Routers.Count(x => x.Result == "Failed");
    public int Cancelled => Routers.Count(x => x.Result == "Cancelled");
}

public sealed class MaintenanceProgressRow
{
    public Guid RouterId { get; set; }
    public string Router { get; set; } = "";
    public string Address { get; set; } = "";
    public string Stage { get; set; } = "Queued";
    public int Attempt { get; set; }
    public int Progress { get; set; }
    public string Result { get; set; } = "Pending";
    public string Message { get; set; } = "";
}

public sealed record MaintenanceProgressUpdate(
    Guid RouterId,
    string Stage,
    int Attempt,
    int Progress,
    string Result,
    string Message);

public sealed record UpdateCheckResult(string Version, string Url, bool IsNewer);
