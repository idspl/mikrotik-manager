using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MikroTikManager;

public sealed class RouterBackupService(AppSettings settings)
{
    private readonly AppSettings _settings = settings;
    public event Action<string>? Message;

    public async Task<BulkBackupResult> BackupAllAsync(IReadOnlyList<RouterRecord> routers, string parentFolder, CancellationToken cancellationToken)
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        CleanupExpiredBackups(parentFolder);
        string outputFolder = Path.Combine(parentFolder, "MikroTik-Manager-Backups_" + stamp);
        Directory.CreateDirectory(outputFolder);
        string manifestPath = Path.Combine(outputFolder, "BackupManifest.csv");
        await File.WriteAllTextAsync(manifestPath, "Router,Address,API Port,Backup File,Backup Bytes,Backup SHA-256,Export File,Export Bytes,Export SHA-256,Backup Password,Verification,Result\r\n", new UTF8Encoding(true), cancellationToken);

        int successful = 0, failed = 0, verified = 0;
        foreach (RouterRecord router in routers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string safeName = SafeName(router.Name);
            string idSuffix = router.Id.ToString("N")[..8];
            string localStem = $"{safeName}_{SafeName(router.Host)}_{router.ApiPort}_{stamp}_{idSuffix}";
            string remoteStem = "indigo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + idSuffix;
            string remoteBackup = remoteStem + ".backup";
            string remoteExport = remoteStem + ".rsc";
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
                router.LastStatus = "Creating API backup";
                Log(router, "Connecting to API");
                await using RouterOsApiClient api = await RouterOsApiClient.ConnectAsync(router, _settings, cancellationToken);
                router.ApiStatus = "Online";
                try
                {
                    try
                    {
                        await api.ExecuteAsync("/system/backup/save", cancellationToken,
                            $"name={remoteStem}", $"password={router.BackupPassword}", "encryption=aes-sha256");
                    }
                    catch (RouterOsApiException)
                    {
                        await api.ExecuteAsync("/system/backup/save", cancellationToken,
                            $"name={remoteStem}", $"password={router.BackupPassword}");
                    }

                    try
                    {
                        await api.ExecuteAsync("/export", cancellationToken, $"file={remoteStem}", "show-sensitive=yes");
                    }
                    catch (RouterOsApiException)
                    {
                        await api.ExecuteAsync("/export", cancellationToken, $"file={remoteStem}", "hide-sensitive=no");
                    }

                    router.LastStatus = "Downloading backup files";
                    Log(router, "Downloading encrypted .backup");
                    await api.DownloadFileAsync(remoteBackup, localBackup, cancellationToken);
                    Log(router, "Downloading show-sensitive .rsc");
                    await api.DownloadFileAsync(remoteExport, localExport, cancellationToken);
                    (backupBytes, backupHash) = await VerifyFileAsync(localBackup, cancellationToken);
                    (exportBytes, exportHash) = await VerifyFileAsync(localExport, cancellationToken);
                    verification = "Verified size and SHA-256";
                    verified++;
                    router.LastStatus = "Backup downloaded";
                    result = "Success";
                    successful++;
                }
                finally
                {
                    await TryDeleteAsync(api, remoteBackup, CancellationToken.None);
                    await TryDeleteAsync(api, remoteExport, CancellationToken.None);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                router.ApiStatus = "Failed";
                router.LastStatus = "Backup failed: " + ex.Message;
                result = "Failed: " + ex.Message;
                failed++;
                Log(router, result);
                TryDeleteLocal(localBackup);
                TryDeleteLocal(localExport);
            }

            string row = string.Join(',', Csv(router.Name), Csv(router.Host), router.ApiPort.ToString(CultureInfo.InvariantCulture),
                Csv(Path.GetFileName(localBackup)), backupBytes.ToString(CultureInfo.InvariantCulture), Csv(backupHash),
                Csv(Path.GetFileName(localExport)), exportBytes.ToString(CultureInfo.InvariantCulture), Csv(exportHash),
                Csv(router.BackupPassword), Csv(verification), Csv(result)) + "\r\n";
            await File.AppendAllTextAsync(manifestPath, row, Encoding.UTF8, cancellationToken);
        }
        return new BulkBackupResult(successful, failed, outputFolder, verified);
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
        int days = _settings.BackupRetentionDays;
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
