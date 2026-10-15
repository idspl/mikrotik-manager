using System.Globalization;
using System.Text.RegularExpressions;

namespace MikroTikManager;

internal enum ReadingTone { Unknown, Good, Warning, Critical, Info }
internal sealed record ReadingAssessment(ReadingTone Tone, string Detail);
internal static class ReadingColors
{
    internal static ReadingAssessment Firmware(string current, string available)
    {
        static string VersionText(string value) => value.Trim().Split(' ')[0];
        string a = VersionText(current), b = VersionText(available);
        if (!Version.TryParse(a, out _) || !Version.TryParse(b, out _)) return new(ReadingTone.Unknown, "Firmware not checked or not supported");
        if (a == b) return new(ReadingTone.Good, "Up to date with firmware bundled in installed RouterOS");
        return ReleaseVersion.IsNewerSameMajor(b, a)
            ? new(ReadingTone.Warning, "Firmware update available: " + available)
            : new(ReadingTone.Info, "Firmware differs from available version: " + available + "; review before upgrading");
    }
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
