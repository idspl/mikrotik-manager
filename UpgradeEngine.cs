using System.Text.RegularExpressions;

namespace MikroTikManager;

public sealed class UpgradeEngine(SecureStore store, AppSettings settings)
{
    private readonly SecureStore _store = store;
    private readonly AppSettings _settings = settings;
    public event Action<string>? Message;
    public event Action<MaintenanceProgressUpdate>? Progress;

    public Task<MaintenanceRunResult> RunSequentialAsync(IEnumerable<RouterRecord> routers, CancellationToken cancellationToken)
        => RunSequentialAsync(routers, new UpgradeRunOptions(FailureBehavior.Stop, 0), cancellationToken);

    public async Task<MaintenanceRunResult> RunSequentialAsync(
        IEnumerable<RouterRecord> routers,
        UpgradeRunOptions options,
        CancellationToken cancellationToken)
    {
        DateTime runStarted = DateTime.Now;
        var results = new List<RouterRunResult>();
        foreach (RouterRecord router in routers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime started = DateTime.Now;
            int maximumAttempts = options.FailureBehavior is FailureBehavior.RetryThenSkip or FailureBehavior.RetryThenStop
                ? Math.Max(1, options.RetryCount + 1)
                : 1;
            Exception? failure = null;
            bool completed = false;
            int attempts = 0;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                attempts = attempt;
                try
                {
                    router.LastStatus = attempt == 1 ? "Running preflight" : $"Retry {attempt - 1} of {maximumAttempts - 1}";
                    Report(router, "Preflight", attempt, 5, "Running", "Checking API, identity, version and free storage");
                    RouterHealthCheck health = await PreflightAsync(router, cancellationToken);
                    if (!health.Passed) throw new InvalidOperationException(health.Summary);
                    await RunRouterAsync(router, attempt, cancellationToken);
                    router.LastStatus = "Completed";
                    Report(router, "Completed", attempt, 100, "Completed", "Upgrade and verification completed");
                    completed = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    router.LastStatus = "Cancelled";
                    Report(router, "Cancelled", attempt, 0, "Cancelled", "Operation cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    router.LastStatus = "Failed: " + ex.Message;
                    Log(router, "FAILED", ex.Message);
                    Report(router, "Failed", attempt, 0, "Failed", ex.Message);
                    if (attempt < maximumAttempts)
                    {
                        Report(router, "Retry wait", attempt, 0, "Retrying", "Retrying in 2 seconds");
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                }
            }

            results.Add(new RouterRunResult(
                router.Id, router.Name, router.Host, completed ? "Completed" : "Failed", attempts,
                router.RouterOsVersion, router.FirmwareVersion, failure?.Message ?? "Completed", started, DateTime.Now));

            if (!completed && options.FailureBehavior is FailureBehavior.Stop or FailureBehavior.RetryThenStop)
            {
                break;
            }
        }
        return new MaintenanceRunResult(runStarted, DateTime.Now, results);
    }

    public async Task<RouterSnapshot> TestAndReadAsync(RouterRecord router, CancellationToken ct)
    {
        await using RouterOsApiClient api = await RouterOsApiClient.ConnectAsync(router, _settings, ct);
        return await GetSnapshotAsync(api, ct);
    }

    public async Task CheckApiAsync(RouterRecord router, CancellationToken ct)
    {
        await using RouterOsApiClient api = await RouterOsApiClient.ConnectAsync(router, _settings, ct);
    }

    public async Task<RouterHealthCheck> PreflightAsync(RouterRecord router, CancellationToken ct)
    {
        try
        {
            await using RouterOsApiClient api = await RouterOsApiClient.ConnectAsync(router, _settings, ct);
            RouterSnapshot snapshot = await GetSnapshotAsync(api, ct);
            IReadOnlyDictionary<string, string> resource = await ReadOneAsync(api, "/system/resource/print", ct);
            long.TryParse(resource.GetValueOrDefault("free-hdd-space"), out long freeBytes);
            long.TryParse(resource.GetValueOrDefault("total-hdd-space"), out long totalBytes);
            StorageRequirement storage = StoragePolicy.Resolve(snapshot.Model, totalBytes, _settings.MinimumFreeDiskMb);
            router.ApiStatus = "Online";
            router.Model = snapshot.Model;
            router.RouterOsVersion = snapshot.RouterOsVersion;
            router.FirmwareVersion = snapshot.CurrentFirmware;
            router.FreeDiskBytes = freeBytes;
            router.TotalDiskBytes = totalBytes;
            router.RequiredFreeDiskMb = storage.RequiredFreeMb;
            router.StorageStatus = StoragePolicy.FormatStatus(freeBytes, totalBytes, storage.RequiredFreeMb);
            long required = storage.RequiredFreeMb * 1024L * 1024L;
            if (string.IsNullOrWhiteSpace(snapshot.RouterOsVersion))
                return new RouterHealthCheck(router, false, "RouterOS version was not returned.", snapshot,
                    freeBytes, totalBytes, storage.RequiredFreeMb, storage.Source, DateTime.Now);
            if (freeBytes > 0 && freeBytes < required)
                return new RouterHealthCheck(router, false,
                    $"Only {StoragePolicy.ToMb(freeBytes):0.0} MB free; {storage.RequiredFreeMb} MB is required ({storage.Source}).",
                    snapshot, freeBytes, totalBytes, storage.RequiredFreeMb, storage.Source, DateTime.Now);
            return new RouterHealthCheck(router, true,
                freeBytes > 0
                    ? $"Ready; {StoragePolicy.ToMb(freeBytes):0.0} MB free of {StoragePolicy.ToMb(totalBytes):0.0} MB; requires {storage.RequiredFreeMb} MB ({storage.Source})"
                    : $"Ready; free storage not reported; using {storage.RequiredFreeMb} MB requirement ({storage.Source})",
                snapshot, freeBytes, totalBytes, storage.RequiredFreeMb, storage.Source, DateTime.Now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            router.ApiStatus = "Failed";
            return new RouterHealthCheck(router, false, ex.Message, null, 0, 0, _settings.MinimumFreeDiskMb, "fallback setting", DateTime.Now);
        }
    }

    private async Task RunRouterAsync(RouterRecord router, int attempt, CancellationToken ct)
    {
        Log(router, "START", "Safe upgrade started");
        Report(router, "Connect", attempt, 10, "Running", "Connected to RouterOS API");
        RouterOsApiClient api = await RouterOsApiClient.ConnectAsync(router, _settings, ct);
        try
        {
            RouterSnapshot before = await GetSnapshotAsync(api, ct);
            router.Model = before.Model;
            Log(router, "PRECHECK", $"Identity={before.Identity}; model={before.Model}; RouterOS={before.RouterOsVersion}; firmware={before.CurrentFirmware}/{before.UpgradeFirmware}");
            Report(router, "Preflight", attempt, 20, "Running", $"{before.Model}; RouterOS {before.RouterOsVersion}");

            string stem = SafeName($"preupgrade-{router.Name}-{DateTime.Now:yyyyMMdd-HHmm}");
            if (string.IsNullOrEmpty(router.BackupPassword))
                router.BackupPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
            try
            {
                await api.ExecuteAsync("/system/backup/save", ct, $"name={stem}", $"password={router.BackupPassword}", "encryption=aes-sha256");
            }
            catch (RouterOsApiException)
            {
                // Compatibility fallback for RouterOS releases predating the encryption selector.
                await api.ExecuteAsync("/system/backup/save", ct, $"name={stem}", $"password={router.BackupPassword}");
            }
            Log(router, "BACKUP", stem + ".backup created with encryption");
            Report(router, "Safety backup", attempt, 30, "Running", stem + ".backup created");
            await api.ExecuteAsync("/export", ct, $"file={stem}");
            Log(router, "EXPORT", stem + ".rsc created; sensitive values omitted by RouterOS default");

            const int maximumPackagePasses = 4;
            for (int pass = 1; pass <= maximumPackagePasses; pass++)
            {
                await api.ExecuteAsync("/system/package/update/set", ct, $"channel={_settings.UpdateChannel}");
                await api.ExecuteAsync("/system/package/update/check-for-updates", ct);
                IReadOnlyDictionary<string, string> update = await ReadOneAsync(api, "/system/package/update/print", ct);
                string current = update.GetValueOrDefault("installed-version") ?? (await GetSnapshotAsync(api, ct)).RouterOsVersion;
                string latest = update.GetValueOrDefault("latest-version") ?? "";
                Log(router, "PACKAGE", $"Pass={pass}; installed={current}; latest={latest}; status={update.GetValueOrDefault("status") ?? "unknown"}");
                Report(router, "RouterOS check", attempt, 35 + (pass * 5), "Running", $"Installed {current}; latest {latest}");
                string updateStatus = update.GetValueOrDefault("status") ?? "";
                if (string.IsNullOrWhiteSpace(latest))
                {
                    if (updateStatus.Contains("up to date", StringComparison.OrdinalIgnoreCase)) break;
                    throw new InvalidOperationException("RouterOS update check did not return a latest version: " + updateStatus);
                }
                if (VersionMatches(current, latest)) break;

                Log(router, "PACKAGE", "Installing RouterOS update; disconnect/reboot expected");
                Report(router, "RouterOS upgrade", attempt, 55, "Running", $"Installing {latest}; reboot expected");
                await api.SendExpectedDisconnectAsync("/system/package/update/install", ct);
                await api.DisposeAsync();
                api = await WaitForRouterAsync(router, "RouterOS reboot", ct);
                RouterSnapshot afterPackage = await GetSnapshotAsync(api, ct);
                if (!VersionMatches(afterPackage.RouterOsVersion, latest))
                    throw new InvalidOperationException($"RouterOS verification failed: expected {latest}, found {afterPackage.RouterOsVersion}.");
                Log(router, "VERIFY", "RouterOS updated to " + afterPackage.RouterOsVersion);
                Report(router, "RouterOS verified", attempt, 75, "Running", afterPackage.RouterOsVersion);
                if (pass == maximumPackagePasses)
                    throw new InvalidOperationException("RouterOS still offers another intermediate update after four passes; stopped for manual review.");
            }

            RouterSnapshot firmware = await GetSnapshotAsync(api, ct);
            if (!string.IsNullOrWhiteSpace(firmware.UpgradeFirmware) && !VersionMatches(firmware.CurrentFirmware, firmware.UpgradeFirmware))
            {
                Log(router, "FIRMWARE", $"Upgrading RouterBOARD {firmware.CurrentFirmware} -> {firmware.UpgradeFirmware}");
                Report(router, "Firmware upgrade", attempt, 82, "Running", $"{firmware.CurrentFirmware} -> {firmware.UpgradeFirmware}");
                await api.ExecuteAsync("/system/routerboard/upgrade", ct);
                await api.SendExpectedDisconnectAsync("/system/reboot", ct);
                await api.DisposeAsync();
                api = await WaitForRouterAsync(router, "firmware reboot", ct);
                RouterSnapshot final = await GetSnapshotAsync(api, ct);
                if (!VersionMatches(final.CurrentFirmware, final.UpgradeFirmware))
                    throw new InvalidOperationException($"Firmware verification failed: current {final.CurrentFirmware}, expected {final.UpgradeFirmware}.");
                router.RouterOsVersion = final.RouterOsVersion;
                router.FirmwareVersion = final.CurrentFirmware;
                router.Model = final.Model;
                Log(router, "VERIFY", $"RouterOS={final.RouterOsVersion}; firmware={final.CurrentFirmware}");
                Report(router, "Final verification", attempt, 95, "Running", $"RouterOS {final.RouterOsVersion}; firmware {final.CurrentFirmware}");
            }
            else
            {
                router.RouterOsVersion = firmware.RouterOsVersion;
                router.FirmwareVersion = firmware.CurrentFirmware;
                router.Model = firmware.Model;
                Log(router, "FIRMWARE", "RouterBOARD firmware is already current or not applicable");
                Report(router, "Final verification", attempt, 95, "Running", "Firmware already current or not applicable");
            }
        }
        finally { await api.DisposeAsync(); }
        Log(router, "DONE", "Upgrade and verification completed");
    }

    private async Task<RouterOsApiClient> WaitForRouterAsync(RouterRecord router, string phase, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(_settings.ReconnectTimeoutMinutes);
        Exception? last = null;
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                RouterOsApiClient client = await RouterOsApiClient.ConnectAsync(router, _settings, ct);
                Log(router, "ONLINE", $"Connected after {phase}; stability wait {_settings.StableOnlineSeconds}s");
                await Task.Delay(TimeSpan.FromSeconds(_settings.StableOnlineSeconds), ct);
                return client;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
        throw new TimeoutException($"Router did not return after {phase}. Last error: {last?.Message}");
    }

    private static async Task<RouterSnapshot> GetSnapshotAsync(RouterOsApiClient api, CancellationToken ct)
    {
        var identity = await ReadOneAsync(api, "/system/identity/print", ct);
        var resource = await ReadOneAsync(api, "/system/resource/print", ct);
        IReadOnlyDictionary<string, string> board;
        try { board = await ReadOneAsync(api, "/system/routerboard/print", ct); }
        catch (RouterOsApiException) { board = new Dictionary<string, string>(); }
        return new RouterSnapshot(
            identity.GetValueOrDefault("name") ?? "",
            FirstNonEmpty(board.GetValueOrDefault("model"), resource.GetValueOrDefault("board-name"), resource.GetValueOrDefault("platform")),
            resource.GetValueOrDefault("version") ?? "",
            board.GetValueOrDefault("current-firmware") ?? "",
            board.GetValueOrDefault("upgrade-firmware") ?? "");
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadOneAsync(RouterOsApiClient api, string command, CancellationToken ct)
    {
        IReadOnlyList<ApiReply> replies = await api.ExecuteAsync(command, ct);
        return replies.FirstOrDefault(x => x.Type == "!re")?.Attributes ?? new Dictionary<string, string>();
    }

    private void Log(RouterRecord router, string stage, string message)
    {
        string safeMessage = string.IsNullOrEmpty(router.Password)
            ? message
            : message.Replace(router.Password, "<redacted>", StringComparison.Ordinal);
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{router.Name}\t{router.Host}\t{stage}\t{safeMessage}";
        File.AppendAllText(Path.Combine(_store.LogDirectory, $"upgrade-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
        Message?.Invoke(line);
    }

    private void Report(RouterRecord router, string stage, int attempt, int progress, string result, string message)
        => Progress?.Invoke(new MaintenanceProgressUpdate(router.Id, stage, attempt, progress, result, message));

    private static string SafeName(string value) => Regex.Replace(value, "[^A-Za-z0-9._-]", "_");
    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    private static bool VersionMatches(string a, string b) => NormalizeVersion(a).Equals(NormalizeVersion(b), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeVersion(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
}

public sealed class UpgradeStoppedException(RouterRecord router, Exception inner)
    : Exception($"Processing stopped at {router.Name} ({router.Host}): {inner.Message}", inner)
{
    public RouterRecord Router { get; } = router;
}
