namespace IndigoRouterScheduler;

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
}

public sealed class AppSettings
{
    public int DefaultApiPort { get; set; } = 8728;
    public bool UseApiSsl { get; set; }
    public bool AllowInvalidTlsCertificate { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int ReconnectTimeoutMinutes { get; set; } = 15;
    public int StableOnlineSeconds { get; set; } = 20;
    public string UpdateChannel { get; set; } = "stable";
}

public sealed record ApiReply(string Type, IReadOnlyDictionary<string, string> Attributes);
public sealed record RouterSnapshot(string Identity, string Model, string RouterOsVersion, string CurrentFirmware, string UpgradeFirmware);
public sealed record BulkBackupResult(int Successful, int Failed, string Folder);
