namespace MikroTikManager;

internal static class Release080Tests
{
    internal static async Task RunAsync()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception("0.8.0: " + message); }
        static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected safety rejection"); }
        static RouterRunResult Result(RouterRecord r, string result = "Completed") => new(r.Id, r.Name, r.Host, result, 1, "", "", "", DateTime.Now, DateTime.Now);
        var a = new RouterRecord { Name = "leaf" }; var b = new RouterRecord { Name = "core", UpgradeAfter = [a.Id] }; var c = new RouterRecord { Name = "independent" };
        var order = new List<Guid>();
        var gate = new TaskCompletionSource<RouterRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = BatchUpgradeQueue.RunAsync([b, a, c], 3, 0, () => false, r => { order.Add(r.Id); return r == a ? gate.Task : Task.FromResult(Result(r)); }, CancellationToken.None);
        await Task.Delay(30); Check(!order.Contains(b.Id) && order.Contains(c.Id), "Prerequisites prevent concurrent upstream work but allow independent devices");
        gate.SetResult(Result(a)); await running; Check(order.IndexOf(b.Id) > order.IndexOf(a.Id), "Dependent starts after prerequisite completion");
        var resumed = await BatchUpgradeQueue.RunAsync([b], 2, 0, () => false, r => Task.FromResult(Result(r)), CancellationToken.None, new HashSet<Guid> { a.Id });
        Check(resumed.Count == 1, "Resume accepts completed prerequisites without rerunning them");
        order.Clear();
        var failed = await BatchUpgradeQueue.RunAsync([b, a, c], 3, 0, () => false, r => { order.Add(r.Id); return Task.FromResult(Result(r, r == a ? "Failed" : "Completed")); }, CancellationToken.None);
        Check(!order.Contains(b.Id) && failed.Count == 2, "Failed prerequisite blocks dependent even under skip policy");
        Reject(() => DependencyPlan.Validate([b])); a.UpgradeAfter = [b.Id]; Reject(() => DependencyPlan.Validate([a, b])); a.UpgradeAfter.Clear();
        var state = new ServiceActivityState(Guid.NewGuid(), "test", DateTime.UtcNow, true, "Running", []);
        Check(ServiceActivity.Accept(new(state.Session, "Cancel", DateTime.UtcNow), state, DateTime.UtcNow), "Valid control accepted");
        Check(!ServiceActivity.Accept(new(Guid.NewGuid(), "Cancel", DateTime.UtcNow), state, DateTime.UtcNow), "Old session rejected");
        Check(!ServiceActivity.Accept(new(state.Session, "Cancel", DateTime.UtcNow.AddMinutes(-1)), state, DateTime.UtcNow), "Expired request rejected");
        Check(!ServiceActivity.Accept(new(state.Session, "Delete", DateTime.UtcNow), state, DateTime.UtcNow), "Unsupported action rejected");
        var root = Path.Combine(Path.GetTempPath(), "mm-080-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var router = new RouterRecord();
            for (int i = 0; i < 3; i++)
            {
                string dir = Path.Combine(root, "local", "MikroTik-Manager-Backups_" + i); Directory.CreateDirectory(dir);
                string backup = Path.Combine(dir, "router.backup"), export = Path.Combine(dir, "router.rsc");
                await File.WriteAllTextAsync(backup, "encrypted fixture " + i); await File.WriteAllTextAsync(export, "export " + i);
                await File.WriteAllTextAsync(Path.Combine(dir, "BackupManifest.csv"), "fixture manifest");
                router.Backups.Add(new(DateTime.Now.AddMinutes(i), backup, export, await RecoveryFiles.HashAsync(backup, default), await RecoveryFiles.HashAsync(export, default), "Verified") { Model = "model", RouterOsVersion = "7.25", SerialNumber = "serial" });
            }
            string newest = Path.GetDirectoryName(router.Backups[^1].BackupPath)!;
            await RecoveryFiles.CopyOffsiteAsync(newest, Path.Combine(root, "offsite"), default);
            Check(File.Exists(Path.Combine(root, "offsite", Path.GetFileName(newest), "router.backup")), "Verified offsite copy published");
            string unrelated = Path.Combine(root, "local", "MikroTik-Manager-Backups_untracked"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "keep"), "keep");
            await RecoveryFiles.PruneAsync(router, Path.Combine(root, "local"), 1, 0, default);
            Check(Directory.Exists(newest) && Directory.Exists(unrelated) && !Directory.Exists(Path.GetDirectoryName(router.Backups[0].BackupPath)), "Retention preserves latest and untracked directories");
            var backupEntry = router.Backups[^1]; GuidedRestore.CheckIdentity(backupEntry, "model", "7.25", "serial");
            Reject(() => GuidedRestore.CheckIdentity(backupEntry, "model", "7.25", "different"));
            Reject(() => GuidedRestore.CheckIdentity(backupEntry, "model", "7.24", "serial"));
            await File.AppendAllTextAsync(backupEntry.BackupPath, "corruption");
            bool rejected = false; try { await RecoveryFiles.VerifyAsync(backupEntry, default); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Corrupted backup rejected");
            var settings = new AppSettings();
            router.Health = new(DateTime.Now, 95, 1, 100, 1, 100, "1d", "cpu-temperature: 85 °C");
            HealthHistory.Record(router, settings); HealthHistory.Record(router, settings);
            Check(router.HealthHistory.Count == 1 && HealthHistory.Warnings(router.Health, settings).Contains("High temperature"), "History deduplicates samples and evaluates thresholds");
            var store = new SecureStore(Path.Combine(root, "store")); settings.MaintenanceProfiles.Add(new() { Name = "Production", RestartBeforeUpgrade = true });
            store.SaveHealthSamples(router); router.HealthHistory.Clear(); router.HealthHistory = store.LoadHealthSamples(router.Id); Check(router.HealthHistory.Count == 1, "Encrypted time series survive separately from inventory");
            store.SaveSettings(settings); Check(store.LoadSettings().MaintenanceProfiles[0].RestartBeforeUpgrade, "Profiles survive save/load");
            using (var activity = new ServiceActivity(store, "fixture", [router]))
            { activity.Update(new(router.Id, "Backup", 1, 20, "Running", "fixture")); Check(ServiceActivity.Read(store)?.Devices.Single().Progress == 20, "Service progress published"); activity.Finish("Completed"); }
            Check(ServiceActivity.Read(store)?.Running == false, "Service completion published");
        }
        finally { Directory.Delete(root, true); }
    }
}
