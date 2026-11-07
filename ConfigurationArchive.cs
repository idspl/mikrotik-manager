using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MikroTikManager;

internal sealed record ConfigurationBundle(int Version, List<RouterRecord> Routers, List<UpgradeJob> Jobs, AppSettings Settings);

internal static class ConfigurationArchive
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MTMCONF1");
    internal static byte[] Encrypt(ConfigurationBundle bundle, string password)
    {
        if (password.Length < 12) throw new ArgumentException("Use an export password of at least 12 characters.");
        byte[] salt = RandomNumberGenerator.GetBytes(16), nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(bundle), cipher = new byte[clear.Length], tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, clear, cipher, tag, Magic);
            return Magic.Concat(salt).Concat(nonce).Concat(tag).Concat(cipher).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(clear); }
    }
    internal static ConfigurationBundle Decrypt(byte[] bytes, string password)
    {
        if (bytes.Length < 53 || bytes.Length > 32 * 1024 * 1024 || !bytes.Take(8).SequenceEqual(Magic))
            throw new InvalidDataException("Not a supported configuration archive.");
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, bytes.AsSpan(8, 16), 600000, HashAlgorithmName.SHA256, 32);
        byte[] clear = new byte[bytes.Length - 52];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(bytes.AsSpan(24, 12), bytes.AsSpan(52), bytes.AsSpan(36, 16), clear, Magic);
            var bundle = JsonSerializer.Deserialize<ConfigurationBundle>(clear) ?? throw new InvalidDataException("Empty archive.");
            if (bundle.Version is not (1 or 2 or 3) || bundle.Routers is null || bundle.Jobs is null || bundle.Settings is null
                || bundle.Routers.Any(x => x.ApiPort < 1 || x.ApiPort > 65535 || string.IsNullOrWhiteSpace(x.Host))
                || bundle.Routers.Select(x => x.Id).Distinct().Count() != bundle.Routers.Count
                || bundle.Routers.Any(x => x.Id == Guid.Empty || x.Backups is null || x.CriticalInterfaces is null || x.Site is null || x.Tags is null || x.UpgradeAfter is null || x.HealthHistory is null || x.OpticalHistory is null)
                || bundle.Routers.Any(x => x.UpdateChannel != "" && !RouterChannels.Allowed.Contains(x.UpdateChannel))
                || bundle.Jobs.Any(x => x.MaxConcurrency is < 0 or > 10000 || x.FailureLimit is < 0 or > 10000 || !Enum.IsDefined(x.Kind) || !Enum.IsDefined(x.Recurrence) || x.RetentionDays is < 0 or > 3650
                    || x.Kind == ScheduledJobKind.Upgrade && x.Recurrence != BackupRecurrence.Once
                    || x.Kind == ScheduledJobKind.Backup && (string.IsNullOrWhiteSpace(x.BackupFolder) || !Path.IsPathFullyQualified(x.BackupFolder)))
                || bundle.Settings.MaintenanceProfiles is null
                || bundle.Settings.MaintenanceProfiles.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.MaxConcurrency is < 0 or > 10000 || p.RetryCount is < 0 or > 5 || p.StabilitySeconds is < 0 or > 300 || p.FailureLimit is < 0 or > 10000 || !Enum.IsDefined(p.FailureBehavior) || p.Channel is null || p.Channel != "" && !RouterChannels.Allowed.Contains(p.Channel))
                || bundle.Settings.PreUpgradeKeepCopies is < 0 or > 1000 || bundle.Settings.PreUpgradeBudgetMb is < 0 or > 1000000
                || bundle.Settings.HealthHistoryDays is < 1 or > 90 || bundle.Settings.CpuWarningPercent is < 1 or > 100 || bundle.Settings.MemoryWarningPercent is < 1 or > 100 || bundle.Settings.TemperatureWarningC is < 1 or > 150
                || bundle.Settings.OffsiteFolder is null || bundle.Settings.OffsiteFolder != "" && !Path.IsPathFullyQualified(bundle.Settings.OffsiteFolder)
                || bundle.Jobs.Any(j => j.StabilitySeconds is < 0 or > 300 || j.ChannelOverride is null || j.ChannelOverride != "" && !RouterChannels.Allowed.Contains(j.ChannelOverride))
                || bundle.Settings.Theme is not ("Light" or "Dark")
                || bundle.Settings.DefaultApiPort is < 1 or > 65535
                || bundle.Settings.ConnectTimeoutSeconds is < 3 or > 120
                || bundle.Settings.ReconnectTimeoutMinutes is < 1 or > 120
                || bundle.Settings.StableOnlineSeconds is < 0 or > 300
                || bundle.Settings.HealthPollMinutes is < 0 or > 1440
                || bundle.Settings.InterfaceRecoverySeconds is < 0 or > 600
                || bundle.Settings.RetryCount is < 0 or > 5
                || bundle.Settings.MinimumFreeDiskMb is < 0 or > 65535
                || bundle.Settings.BackupRetentionDays is < 0 or > 3650
                || !new[] { "stable", "long-term", "testing", "development" }.Contains(bundle.Settings.UpdateChannel)
                || !Enum.IsDefined(bundle.Settings.DefaultFailureBehavior)
                || bundle.Jobs.Any(x => x.RouterIds is null || x.RetryCount is < 0 or > 5 || !Enum.IsDefined(x.FailureBehavior))
                || bundle.Jobs.Any(x => x.RouterIds.Any(id => !bundle.Routers.Any(r => r.Id == id))))
                throw new InvalidDataException("Invalid configuration archive.");
            try { DependencyPlan.Validate(bundle.Routers); }
            catch (InvalidOperationException ex) { throw new InvalidDataException("Invalid dependency graph in archive.", ex); }
            return bundle;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(clear); }
    }
}
