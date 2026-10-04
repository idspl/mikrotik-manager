namespace MikroTikManager;

internal static class BackupChangeDetector
{
    internal static async Task<string> CompareAsync(string? previous, string current, CancellationToken ct)
    {
        if (previous is null) return "Baseline created";
        if (!File.Exists(previous)) return "Previous export missing";
        if (new FileInfo(previous).Length > 4 * 1024 * 1024 || new FileInfo(current).Length > 4 * 1024 * 1024) return "Comparison skipped (>4 MiB)";
        string a = await File.ReadAllTextAsync(previous, ct), b = await File.ReadAllTextAsync(current, ct);
        return ConfigDiff.Normalize(a).SequenceEqual(ConfigDiff.Normalize(b)) ? "Unchanged" : "Changed";
    }
}
