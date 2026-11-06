namespace MikroTikManager;

internal static class UpgradeBackupRegressionTests
{
    internal static async Task RunAsync(string directory)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        const long mb = 1024L * 1024L;
        Check(StoragePolicy.Resolve("RB750Gr3", 16 * mb, 16).RequiredFreeMb == 2, "16 MB flash requires 2 MB, independent of fallback");
        Check(StoragePolicy.Resolve("unknown", 16 * mb, 16).RequiredFreeMb == 2, "Reported storage determines small tier");
        Check(StoragePolicy.Resolve("RB760iGS", 0, 16).RequiredFreeMb == 2, "Small hEX fallback uses 2 MB");
        Check(StoragePolicy.Resolve("hEX", 128 * mb, 16).RequiredFreeMb == 16, "Larger hEX storage is not treated as 16 MB flash");
        Check(StoragePolicy.Resolve("CCR2116", 128 * mb, 16).RequiredFreeMb == 16, "CCR requirement retained");
        Check(StoragePolicy.Resolve("other", 32 * mb, 16).RequiredFreeMb == 8, "Medium storage requirement retained");
        var store = new SecureStore(directory);
        var routers = Enumerable.Range(0, 3).Select(i => new RouterRecord { Name = "Upgrade fixture " + i, Host = "192.0.2." + (i + 1) }).ToList();
        store.SaveRouters(routers);
        var settings = new AppSettings();
        var saveLock = new object();
        var service = new UpgradeBackupService(store, settings, saveLock) { ServiceFactory = () => new RouterBackupService(settings, 0) {
            DownloadOverride = async (router, backup, export, ct) => {
                Check(store.LoadRouters().Single(r => r.Id == router.Id).BackupPassword == router.BackupPassword,
                    "Password must be durable before router backup starts");
                await File.WriteAllTextAsync(backup, "encrypted fixture", ct);
                await File.WriteAllTextAsync(export, "/system identity set name=test", ct);
            }
        } };
        string[] folders = await Task.WhenAll(routers.Select(r => service.SaveAsync(r, CancellationToken.None)));
        Check(folders.Distinct().Count() == 3 && folders.All(f => f.StartsWith(UpgradeBackupService.RootFolder(store))), "Parallel upgrade backups use separate local folders");
        Check(store.LoadRouters().All(r => r.Backups.Count == 1 && r.Backups[0].BackupHash.Length == 64), "Parallel backup histories survive persistence");
        Check(folders.All(f => File.Exists(Path.Combine(f, "BackupManifest.csv"))), "Recovery manifest saved with each backup");
        // A missing export must block the upgrade gate and retain the previous verified recovery copy.
        var failed = new UpgradeBackupService(store, settings, saveLock) { ServiceFactory = () => new RouterBackupService(settings, 0) {
            DownloadOverride = async (_, backup, export, ct) => await File.WriteAllTextAsync(backup, "partial fixture", ct)
        } };
        bool blocked = false;
        try { await failed.SaveAsync(routers[0], CancellationToken.None); } catch (IOException) { blocked = true; }
        Check(blocked && routers[0].Backups.Count == 1 && File.Exists(routers[0].Backups[0].BackupPath), "Missing export blocks upgrade without deleting previous recovery copy");
        Check(store.LoadRouters()[0].LastBackupError.Length > 0, "Backup failure persisted for review");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        blocked = false;
        try { await service.SaveAsync(routers[1], cancelled.Token); } catch (OperationCanceledException) { blocked = true; }
        Check(blocked && routers[1].Backups.Count == 1, "Cancellation cannot pass the upgrade backup gate");
        // A local path error also prevents a false successful gate.
        var badStore = new SecureStore(Path.Combine(directory, "blocked-path")); badStore.SaveRouters([routers[2]]);
        await File.WriteAllTextAsync(Path.Combine(badStore.RootDirectory, "Backups"), "not a directory");
        blocked = false;
        try { await new UpgradeBackupService(badStore, settings, saveLock).SaveAsync(routers[2], CancellationToken.None); }
        catch (IOException) { blocked = true; }
        Check(blocked, "Unwritable local destination blocks upgrade before network work");
    }
}
