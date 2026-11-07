namespace MikroTikManager;

// Shared by every upgrade entry point through UpgradeEngine. No UI or user-profile paths.
internal sealed class UpgradeBackupService(SecureStore store, AppSettings settings, object saveLock)
{
    internal Func<RouterBackupService>? ServiceFactory { get; init; }
    internal static string RootFolder(SecureStore store) => Path.Combine(store.RootDirectory, "Backups", "PreUpgrade");

    internal async Task<string> SaveAsync(RouterRecord router, CancellationToken ct, Action<string>? message = null)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(router.BackupPassword))
            router.BackupPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
        Persist(router); // Recovery secret must survive termination during the download.
        var service = ServiceFactory?.Invoke() ?? new RouterBackupService(settings, retentionDays: 0);
        if (message is not null) service.Message += message;
        try
        {
            var result = await service.BackupAllAsync([router], Path.Combine(RootFolder(store), router.Id.ToString("N")), ct);
            ct.ThrowIfCancellationRequested();
            if (result.Successful != 1 || result.Failed != 0 || result.Verified != 1)
                throw new IOException("Upgrade blocked: local recovery backup was not verified. " + router.LastBackupError);
            string cleanup = router.OffsiteStatus.StartsWith("Offsite copy failed:") ? "Cleanup skipped: offsite transfer failed" : await RecoveryFiles.PruneAsync(router, Path.Combine(RootFolder(store), router.Id.ToString("N")),
                settings.PreUpgradeKeepCopies, settings.PreUpgradeBudgetMb, ct);
            message?.Invoke(cleanup);
            return result.Folder;
        }
        finally { Persist(router); }
    }

    private void Persist(RouterRecord router)
    {
        // Parallel upgrades must merge their own backup metadata, never overwrite other routers.
        lock (saveLock)
        {
            var saved = store.LoadRouters();
            var target = saved.FirstOrDefault(r => r.Id == router.Id);
            if (target is null) throw new InvalidOperationException("Router is no longer in the saved inventory; upgrade stopped.");
            target.BackupPassword = router.BackupPassword;
            target.Backups = router.Backups.ToList();
            target.LastBackupFolder = router.LastBackupFolder;
            target.LastBackupAttemptAt = router.LastBackupAttemptAt;
            target.LastBackupError = router.LastBackupError;
            target.BackupChange = router.BackupChange;
            target.BackupChangeAt = router.BackupChangeAt;
            target.OffsiteStatus = router.OffsiteStatus;
            store.SaveRouters(saved);
        }
    }
}
