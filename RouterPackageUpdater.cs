namespace MikroTikManager;

internal sealed record PackagePreparation(string Version, bool RequiresReboot);

// Download separately so an HTTP failure cannot masquerade as an expected installation reboot.
internal sealed class RouterPackageUpdater(
    Func<string, CancellationToken, string[], Task<IReadOnlyList<ApiReply>>> execute,
    Action<string, string> report)
{
    internal TimeSpan CheckTimeout { get; init; } = TimeSpan.FromMinutes(2);
    internal TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(10);
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    internal async Task<PackagePreparation> PrepareAsync(string channel, CancellationToken ct)
    {
        var update = await RunStageAsync("RouterOS check", "/system/package/update/check-for-updates", CheckTimeout,
            status => status.Contains("up to date", StringComparison.OrdinalIgnoreCase)
                || status.Contains("new version is available", StringComparison.OrdinalIgnoreCase), ct, channel);
        string latest = update.GetValueOrDefault("latest-version") ?? "";
        string current = update.GetValueOrDefault("installed-version") ?? "";
        if (update.GetValueOrDefault("status", "").Contains("up to date", StringComparison.OrdinalIgnoreCase))
            return new(current, false);
        if (string.IsNullOrWhiteSpace(latest)) throw new InvalidOperationException("RouterOS update check returned no target version.");
        await RunStageAsync("RouterOS download", "/system/package/update/download", DownloadTimeout, DownloadReady, ct);
        return new(latest, true);
    }

    private async Task<IReadOnlyDictionary<string, string>> RunStageAsync(string stage, string command, TimeSpan limit,
        Func<string, bool> ready, CancellationToken ct, string? channel = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        var token = timeout.Token;
        try
        {
            report(stage, command.EndsWith("download") ? "Downloading packages; waiting for RouterOS to confirm completion" : "Checking selected update channel");
            if (channel is not null) await execute("/system/package/update/set", token, ["channel=" + channel]);
            var response = await execute(command, token, []);
            foreach (var reply in response) ThrowIfError(reply.Attributes.GetValueOrDefault("status") ?? "");
            string previous = "";
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var replies = await execute("/system/package/update/print", token, []);
                var row = replies.FirstOrDefault(r => r.Type == "!re")?.Attributes ?? new Dictionary<string, string>();
                string status = row.GetValueOrDefault("status") ?? "";
                ThrowIfError(status); // Inspect error even if a stale latest-version is still present.
                if (status != previous) { report(stage, status); previous = status; }
                if (ready(status)) return row;
                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException(stage + " timed out. No installation reboot was requested."); }
    }

    internal static bool DownloadReady(string status) => status.TrimStart().StartsWith("Downloaded", StringComparison.OrdinalIgnoreCase)
        && status.Contains("reboot", StringComparison.OrdinalIgnoreCase);

    internal static void ThrowIfError(string status)
    {
        if (RouterUpdateRecovery.IsBadHttp(status) || status.Contains("error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("failed", StringComparison.OrdinalIgnoreCase) || status.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || status.Contains("cannot", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RouterOS update: " + status);
    }
}
