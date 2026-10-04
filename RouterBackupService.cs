using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MikroTikManager;

public sealed class RouterBackupService(AppSettings settings, int? retentionDays = null)
{
    private readonly AppSettings _settings = settings;
    internal Func<RouterRecord, string, string, CancellationToken, Task>? DownloadOverride { get; init; }
    public event Action<string>? Message;
    public event Action<MaintenanceProgressUpdate>? Progress;

    public async Task<BulkBackupResult> BackupAllAsync(IReadOnlyList<RouterRecord> routers, string parentFolder, CancellationToken cancellationToken)
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        string outputFolder = Path.Combine(parentFolder, "MikroTik-Manager-Backups_" + stamp + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outputFolder);
        string manifestPath = Path.Combine(outputFolder, "BackupManifest.csv");
        await File.WriteAllTextAsync(manifestPath, "Router,Address,API Port,Backup File,Backup Bytes,Backup SHA-256,Export File,Export Bytes,Export SHA-256,Backup Password,Verification,Result\r\n", new UTF8Encoding(true), cancellationToken);

        int successful = 0, failed = 0, verified = 0;
        using var manifestLock = new SemaphoreSlim(1, 1);
        async Task BackupOneAsync(RouterRecord router)
        {
            cancellationToken.ThrowIfCancellationRequested();
            router.LastBackupAttemptAt = DateTime.Now; router.LastBackupError = "";
            Progress?.Invoke(new(router.Id, "Backup", 1, 10, "Running", "Creating and downloading local backup"));
            string safeName = SafeName(router.Name);
            string idSuffix = router.Id.ToString("N")[..8];
            string localStem = $"{safeName}_{SafeName(router.Host)}_{router.ApiPort}_{stamp}_{idSuffix}";
            string remoteStem = "indigo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + idSuffix;
            string localBackup = Path.Combine(outputFolder, localStem + ".backup");
            string localExport = Path.Combine(outputFolder, localStem + ".rsc");
            string result;
            string verification = "Not verified";
            long backupBytes = 0, exportBytes = 0;
            string backupHash = "", exportHash = "";

            if (string.IsNullOrEmpty(router.BackupPassword))
                router.BackupPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(10));
                if (DownloadOverride is not null)
                    await DownloadOverride(router, localBackup, localExport, timeout.Token);
                else
                    await DownloadAsync(router, remoteStem, localBackup, localExport, timeout.Token);
                (backupBytes, backupHash) = await VerifyFileAsync(localBackup, timeout.Token);
                (exportBytes, exportHash) = await VerifyFileAsync(localExport, timeout.Token);
                verification = "Verified size and SHA-256";
                router.Backups.Add(new BackupHistoryEntry(DateTime.Now, localBackup, localExport,
                    backupHash, exportHash, verification));
                Interlocked.Increment(ref verified);
                router.LastStatus = "Backup downloaded";
                result = "Success";
                Interlocked.Increment(ref successful);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                router.LastBackupError = "Cancelled"; router.LastStatus = "Backup cancelled";
                result = "Cancelled";
                TryDeleteLocal(localBackup); TryDeleteLocal(localExport);
            }
            catch (Exception ex)
            {
                router.ApiStatus = "Failed";
                string error = ex is OperationCanceledException ? "Device backup timed out" : ex.Message;
                router.LastBackupError = error;
                router.LastStatus = "Backup failed: " + error;
                result = "Failed: " + error;
                Interlocked.Increment(ref failed);
                Log(router, result);
                TryDeleteLocal(localBackup);
                TryDeleteLocal(localExport);
            }

            string row = string.Join(',', Csv(router.Name), Csv(router.Host), router.ApiPort.ToString(CultureInfo.InvariantCulture),
                Csv(Path.GetFileName(localBackup)), backupBytes.ToString(CultureInfo.InvariantCulture), Csv(backupHash),
                Csv(Path.GetFileName(localExport)), exportBytes.ToString(CultureInfo.InvariantCulture), Csv(exportHash),
                Csv(router.BackupPassword), Csv(verification), Csv(result)) + "\r\n";
            // One writer prevents parallel CSV rows from interleaving. Preserve completed results on cancellation.
            await manifestLock.WaitAsync();
            try { await File.AppendAllTextAsync(manifestPath, row, Encoding.UTF8); }
            finally { manifestLock.Release(); }
            Progress?.Invoke(new(router.Id, "Backup", 1, 100,
                result == "Success" ? "Completed" : result == "Cancelled" ? "Cancelled" : "Failed", result));
        }
        // Backup-only jobs always attempt all devices; upgrade queue failure rules do not apply.
        await Task.WhenAll(routers.DistinctBy(r => r.Id).Select(BackupOneAsync));
        cancellationToken.ThrowIfCancellationRequested();
        // Do not delete earlier recovery copies when the replacement run failed.
        if (failed == 0 && successful > 0) await Task.Run(() => CleanupExpiredBackups(parentFolder));
        return new BulkBackupResult(successful, failed, outputFolder, verified);
    }

    private async Task DownloadAsync(RouterRecord router, string remoteStem, string localBackup, string localExport, CancellationToken ct)
    {
        router.LastStatus = "Creating API backup";
        Log(router, "Connecting to API");
        await using RouterOsApiClient api = await RouterOsApiClient.ConnectAsync(router, _settings, ct);
        router.ApiStatus = "Online"; router.LastSeenAt = DateTime.Now;
        try
        {
            try { await api.ExecuteAsync("/system/backup/save", ct, $"name={remoteStem}", $"password={router.BackupPassword}", "encryption=aes-sha256"); }
            catch (RouterOsApiException) { await api.ExecuteAsync("/system/backup/save", ct, $"name={remoteStem}", $"password={router.BackupPassword}"); }
            try { await api.ExecuteAsync("/export", ct, $"file={remoteStem}", "show-sensitive=yes"); }
            catch (RouterOsApiException) { await api.ExecuteAsync("/export", ct, $"file={remoteStem}", "hide-sensitive=no"); }
            router.LastStatus = "Downloading backup files";
            Log(router, "Downloading encrypted .backup");
            await api.DownloadFileAsync(remoteStem + ".backup", localBackup, ct);
            Log(router, "Downloading show-sensitive .rsc");
            await api.DownloadFileAsync(remoteStem + ".rsc", localExport, ct);
        }
        finally
        {
            // A disconnected router must not leave the batch waiting forever for cleanup.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await TryDeleteAsync(api, remoteStem + ".backup", cleanup.Token);
            await TryDeleteAsync(api, remoteStem + ".rsc", cleanup.Token);
        }
    }

    private async Task<(long Size, string Sha256)> VerifyFileAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0) throw new InvalidDataException($"Downloaded file is empty: {Path.GetFileName(path)}");
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return (info.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private void CleanupExpiredBackups(string parentFolder)
    {
        int days = retentionDays ?? _settings.BackupRetentionDays;
        if (days <= 0 || !Directory.Exists(parentFolder)) return;
        DateTime cutoff = DateTime.Now.AddDays(-days);
        foreach (string folder in Directory.EnumerateDirectories(parentFolder, "MikroTik-Manager-Backups_*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (Directory.GetCreationTime(folder) < cutoff) Directory.Delete(folder, true);
            }
            catch { }
        }
    }

    private static async Task TryDeleteAsync(RouterOsApiClient api, string remoteFile, CancellationToken cancellationToken)
    {
        try { await api.DeleteFileAsync(remoteFile, cancellationToken); } catch { }
    }

    private static void TryDeleteLocal(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        try { if (File.Exists(path + ".part")) File.Delete(path + ".part"); } catch { }
    }

    private void Log(RouterRecord router, string message)
        => Message?.Invoke($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{router.Name}\t{router.Host}\tBACKUP\t{message}");

    private static string SafeName(string value)
    {
        string clean = string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is ':' or '/' or '\\' ? '_' : c));
        return string.IsNullOrWhiteSpace(clean) ? "router" : clean.Trim();
    }

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
}
