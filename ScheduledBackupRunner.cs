namespace MikroTikManager;

internal static class ScheduledBackupRunner
{
    internal static async Task<BulkBackupResult> RunAsync(SecureStore store, AppSettings settings, UpgradeJob job,
        List<RouterRecord> selected, Action<string>? message, Action<MaintenanceProgressUpdate>? progress, CancellationToken ct, Func<RouterBackupService>? serviceFactory = null)
    {
        BackupSchedulePolicy.Validate(job);
        // Persist encryption passwords before remote backups are created.
        foreach (var router in selected)
            if (string.IsNullOrEmpty(router.BackupPassword)) router.BackupPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
        void SaveSelected()
        {
            var saved = store.LoadRouters();
            foreach (var router in selected)
            {
                int index = saved.FindIndex(r => r.Id == router.Id);
                if (index >= 0) saved[index] = router;
            }
            store.SaveRouters(saved);
        }
        SaveSelected();
        var service = serviceFactory?.Invoke() ?? new RouterBackupService(settings, job.RetentionDays);
        if (message is not null) service.Message += message;
        var saveLock = new object();
        service.Progress += update =>
        {
            if (update.Result is "Completed" or "Failed" or "Cancelled")
            {
                // Persist only this finished router, never enumerate histories still being changed by other tasks.
                lock (saveLock)
                {
                    var saved = store.LoadRouters();
                    int index = saved.FindIndex(r => r.Id == update.RouterId);
                    if (index >= 0) saved[index] = selected.Single(r => r.Id == update.RouterId);
                    store.SaveRouters(saved);
                }
            }
            progress?.Invoke(update);
        };
        try { return await service.BackupAllAsync(selected, BackupSchedulePolicy.JobFolder(job), ct); }
        finally { SaveSelected(); } // BackupAllAsync drains every active task before returning or throwing.
    }
}
