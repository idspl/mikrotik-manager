namespace MikroTikManager;

public sealed record StorageRequirement(int RequiredFreeMb, string Source);

public static class StoragePolicy
{
    private const long MiB = 1024L * 1024L;

    public static StorageRequirement Resolve(string model, long totalBytes, int fallbackMb)
    {
        int fallback = Math.Max(0, fallbackMb);
        string name = model?.Trim() ?? "";

        // CCR devices should keep a larger safety margin even when a legacy
        // resource response reports an unexpectedly small or rounded disk.
        if (name.StartsWith("CCR", StringComparison.OrdinalIgnoreCase))
            return new StorageRequirement(Math.Max(16, fallback), "CCR safety rule");

        if (totalBytes > 0)
        {
            double totalMb = ToMb(totalBytes);
            if (totalMb <= 20)
                return new StorageRequirement(3, "16 MB storage tier");
            if (totalMb <= 64)
                return new StorageRequirement(8, "21-64 MB storage tier");
            return new StorageRequirement(16, "65 MB+ storage tier");
        }

        if (name.Contains("hEX", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("RB750", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("RB760", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("RB960", StringComparison.OrdinalIgnoreCase))
            return new StorageRequirement(3, "hEX family fallback");

        return new StorageRequirement(fallback, "fallback setting");
    }

    public static string FormatStatus(long freeBytes, long totalBytes, int requiredMb)
    {
        if (freeBytes <= 0 && totalBytes <= 0) return $"Unknown (req {requiredMb} MB)";
        if (totalBytes <= 0) return $"{ToMb(freeBytes):0.0} MB free (req {requiredMb})";
        return $"{ToMb(freeBytes):0.0}/{ToMb(totalBytes):0.0} MB (req {requiredMb})";
    }

    public static double ToMb(long bytes) => bytes <= 0 ? 0 : bytes / (double)MiB;
}
