using System.Text.RegularExpressions;

namespace MikroTikManager;

public sealed class UpgradeEngine(SecureStore store, AppSettings settings)
{
    private readonly SecureStore _store = store;
    private readonly AppSettings _settings = settings;
    public event Action<string>? Message;

    public async Task RunSequentialAsync(IEnumerable<RouterRecord> routers, CancellationToken cancellationToken)
    {
        foreach (RouterRecord router in routers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                router.LastStatus = "Running";
                await RunRouterAsync(router, cancellationToken);
                router.LastStatus = "Completed";
            }
            catch (OperationCanceledException) { router.LastStatus = "Cancelled"; throw; }
            catch (Exception ex)
            {
                router.LastStatus = "Failed: " + ex.Message;
                Log(router, "FAILED", ex.Message);
                throw new UpgradeStoppedException(router, ex);
            }
        }
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

    private async Task RunRouterAsync(RouterRecord router, CancellationToken ct)
    {
        Log(router, "START", "Safe upgrade started");
        RouterOsApiClient api = await RouterOsApiClient.ConnectAsync(router, _settings, ct);
        try
        {
            RouterSnapshot before = await GetSnapshotAsync(api, ct);
            router.Model = before.Model;
            Log(router, "PRECHECK", $"Identity={before.Identity}; model={before.Model}; RouterOS={before.RouterOsVersion}; firmware={before.CurrentFirmware}/{before.UpgradeFirmware}");

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
                string updateStatus = update.GetValueOrDefault("status") ?? "";
                if (string.IsNullOrWhiteSpace(latest))
                {
                    if (updateStatus.Contains("up to date", StringComparison.OrdinalIgnoreCase)) break;
                    throw new InvalidOperationException("RouterOS update check did not return a latest version: " + updateStatus);
                }
                if (VersionMatches(current, latest)) break;

                Log(router, "PACKAGE", "Installing RouterOS update; disconnect/reboot expected");
                await api.SendExpectedDisconnectAsync("/system/package/update/install", ct);
                await api.DisposeAsync();
                api = await WaitForRouterAsync(router, "RouterOS reboot", ct);
                RouterSnapshot afterPackage = await GetSnapshotAsync(api, ct);
                if (!VersionMatches(afterPackage.RouterOsVersion, latest))
                    throw new InvalidOperationException($"RouterOS verification failed: expected {latest}, found {afterPackage.RouterOsVersion}.");
                Log(router, "VERIFY", "RouterOS updated to " + afterPackage.RouterOsVersion);
                if (pass == maximumPackagePasses)
                    throw new InvalidOperationException("RouterOS still offers another intermediate update after four passes; stopped for manual review.");
            }

            RouterSnapshot firmware = await GetSnapshotAsync(api, ct);
            if (!string.IsNullOrWhiteSpace(firmware.UpgradeFirmware) && !VersionMatches(firmware.CurrentFirmware, firmware.UpgradeFirmware))
            {
                Log(router, "FIRMWARE", $"Upgrading RouterBOARD {firmware.CurrentFirmware} -> {firmware.UpgradeFirmware}");
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
            }
            else
            {
                router.RouterOsVersion = firmware.RouterOsVersion;
                router.FirmwareVersion = firmware.CurrentFirmware;
                router.Model = firmware.Model;
                Log(router, "FIRMWARE", "RouterBOARD firmware is already current or not applicable");
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
