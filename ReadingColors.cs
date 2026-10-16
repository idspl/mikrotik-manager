using System.Globalization;
using System.Text.RegularExpressions;

namespace MikroTikManager;

internal enum ReadingTone { Unknown, Good, Warning, Critical, Info }
internal sealed record ReadingAssessment(ReadingTone Tone, string Detail);
internal static class ReadingColors
{
    internal static ReadingAssessment Firmware(string current, string latest, string channel = "stable")
    {
        static string? Normalize(string text)
        {
            var m = Regex.Match(text.Trim(), @"^(\d+)\.(\d+)(?:\.(\d+))?((?:beta|rc)\d+)?(?:\s+\([^)]*\))?$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return $"{m.Groups[1].Value}.{m.Groups[2].Value}.{(m.Groups[3].Success ? m.Groups[3].Value : "0")}{m.Groups[4].Value.ToLowerInvariant()}";
        }
        string? installed = Normalize(current), target = Normalize(latest);
        if (installed is null || target is null) return new(ReadingTone.Unknown, $"Latest {channel} release or installed version unavailable; refresh MikroTik releases and device versions");
        return installed == target
            ? new(ReadingTone.Good, $"Matches latest published {channel} release: {latest}")
            : new(ReadingTone.Warning, $"Does not match latest published {channel} release: {latest}. Installed: {current}. Review upgrade compatibility before changing versions.");
    }
    internal static string ChannelTarget(RouterOsReleases? releases, string channel) => channel switch {
        "stable" => releases?.Stable ?? "", "long-term" => releases?.LongTerm ?? "", "testing" => releases?.Testing ?? "", _ => "" };
    internal static ReadingAssessment Temperature(string value)
    {
        // Parse values after sensor names, never digits embedded in names (e.g. cpu2).
        var readings = value.Split(';').Select(part => part.Contains(':') ? part[(part.LastIndexOf(':') + 1)..] : part)
            .Select(part => Regex.Match(part.Trim(), @"^([+-]?\d+(?:\.\d+)?)\s*(?:°?C)?$", RegexOptions.IgnoreCase))
            .Where(m => m.Success).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        if (readings.Count == 0) return new(ReadingTone.Unknown, "Temperature unavailable");
        double hottest = readings.Max();
        return new(hottest >= 85 ? ReadingTone.Critical : hottest >= 70 ? ReadingTone.Warning : ReadingTone.Good,
            "Advisory temperature bands: below 70°C green, 70–84.9°C amber, 85°C+ red. Not model-specific safety limits; highest reported sensor determines color.");
    }
    internal static ReadingAssessment Fiber(string value, string? fault)
    {
        if (fault is "yes" or "true") return new(ReadingTone.Critical, "Router reports signal loss / transmitter fault");
        if (string.IsNullOrWhiteSpace(value) || value == "Unavailable") return new(ReadingTone.Unknown, "Optical reading unavailable");
        if (fault is "no" or "false") return new(ReadingTone.Good, "No signal-loss / transmitter-fault flag. Power margin is module-specific; this is not a power-limit check.");
        return new(ReadingTone.Info, "Power reading available; fault status unknown. Check module-specific power limits.");
    }
    internal static void Apply(DataGridView grid, DataGridViewCellFormattingEventArgs e, ReadingAssessment assessment)
    {
        var colors = assessment.Tone switch {
            ReadingTone.Good => (AppTheme.SuccessBack, AppTheme.SuccessText),
            ReadingTone.Warning => (AppTheme.WarningBack, AppTheme.WarningText),
            ReadingTone.Critical => (AppTheme.FailureBack, AppTheme.FailureText),
            ReadingTone.Info => (AppTheme.RunningBack, AppTheme.RunningText),
            _ => (AppTheme.Surface, AppTheme.Muted) };
        e.CellStyle.BackColor = colors.Item1; e.CellStyle.ForeColor = colors.Item2;
        // Keep indicators readable when a row is selected; other cells retain the selection highlight.
        e.CellStyle.SelectionBackColor = colors.Item1; e.CellStyle.SelectionForeColor = colors.Item2;
        grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = assessment.Detail;
    }
}
