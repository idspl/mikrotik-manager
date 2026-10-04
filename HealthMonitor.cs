using System.Globalization;
namespace MikroTikManager;

public sealed record DeviceHealth(DateTime CheckedAt, double? CpuPercent, long? FreeMemory, long? TotalMemory,
    long? FreeStorage, long? TotalStorage, string Uptime, string Temperature);

internal static class HealthMonitor
{
    internal static string Status(RouterRecord router, DateTime now, int intervalMinutes)
    {
        if (router.LastHealthCheckAt is null) return "Not checked";
        if (!router.HealthObservedThisSession || now - router.LastHealthCheckAt > TimeSpan.FromMinutes(Math.Max(2, intervalMinutes * 2))) return "Stale";
        return router.LastHealthError.Length == 0 ? "Online" : "Offline";
    }
    internal static double? Number(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= 0 && n <= 100 ? n : null;
    internal static long? Bytes(IReadOnlyDictionary<string, string> values, string key) => long.TryParse(values.GetValueOrDefault(key), out var n) && n >= 0 ? n : null;
    internal static string Size(long? bytes) => bytes is null ? "Unavailable" : $"{bytes / 1048576d:0.0} MiB";
    internal static DeviceHealth Parse(IReadOnlyDictionary<string, string> resource, IEnumerable<IReadOnlyDictionary<string, string>> sensors, DateTime time)
    {
        var temperatures = new List<string>();
        foreach (var sensor in sensors)
        {
            if (sensor.TryGetValue("name", out var name))
            {
                if (name.Contains("temperature", StringComparison.OrdinalIgnoreCase) && sensor.TryGetValue("value", out var value)) temperatures.Add(name + ": " + value + " °C");
            }
            else foreach (var pair in sensor.Where(p => p.Key.Contains("temperature", StringComparison.OrdinalIgnoreCase))) temperatures.Add(pair.Key + ": " + pair.Value + " °C");
        }
        return new(time, Number(resource.GetValueOrDefault("cpu-load")), Bytes(resource, "free-memory"), Bytes(resource, "total-memory"),
            Bytes(resource, "free-hdd-space"), Bytes(resource, "total-hdd-space"), resource.GetValueOrDefault("uptime") ?? "Unavailable",
            temperatures.Count == 0 ? "Unavailable" : string.Join("; ", temperatures));
    }
    internal static async Task<DeviceHealth> ReadAsync(RouterRecord router, AppSettings settings, CancellationToken ct)
    {
        await using var api = await RouterOsApiClient.ConnectAsync(router, settings, ct);
        var resource = (await api.ExecuteAsync("/system/resource/print", ct)).FirstOrDefault(r => r.Type == "!re")?.Attributes
            ?? throw new InvalidDataException("No resource information returned.");
        var sensors = new List<IReadOnlyDictionary<string, string>>();
        try { sensors.AddRange((await api.ExecuteAsync("/system/health/print", ct)).Where(r => r.Type == "!re").Select(r => r.Attributes)); }
        catch (RouterOsApiException) { /* Sensors are optional and depend on model / permissions. */ }
        return Parse(resource, sensors, DateTime.Now);
    }
    internal static async Task PollAsync(IReadOnlyList<RouterRecord> routers, AppSettings settings, Action? completed, CancellationToken ct,
        Func<RouterRecord, AppSettings, CancellationToken, Task<DeviceHealth>>? read = null)
    {
        using var slots = new SemaphoreSlim(8);
        await Task.WhenAll(routers.Select(async router =>
        {
            await slots.WaitAsync(ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds + 30));
                try
                {
                    router.Health = await (read ?? ReadAsync)(router, settings, timeout.Token);
                    router.LastHealthError = ""; router.ApiStatus = "Online"; router.LastSeenAt = DateTime.Now;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { router.LastHealthError = ex is OperationCanceledException ? "Health check timed out" : ex.Message; router.ApiStatus = "Failed"; }
                router.LastHealthCheckAt = router.LastApiCheckAt = DateTime.Now;
                router.HealthObservedThisSession = true;
                completed?.Invoke();
            }
            finally { slots.Release(); }
        }));
    }
}
