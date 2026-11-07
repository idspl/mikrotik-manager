using System.Drawing.Drawing2D;
namespace MikroTikManager;

internal static class ActionIcons
{
    private static readonly Dictionary<string, Image> Cache = new();
    internal static Image For(string text)
    {
        string kind = text.Contains("Refresh") || text.Contains("Retry") ? "refresh" : text.Contains("Backup") || text.Contains("Folder") ? "backup"
            : text.Contains("History") || text.Contains("Schedule") ? "clock" : text.Contains("Upgrade") ? "upgrade" : text.Contains("Cancel") || text.Contains("Delete") ? "close" : "action";
        if (Cache.TryGetValue(kind, out var cached)) return cached;
        var bitmap = new Bitmap(32, 32); using var g = Graphics.FromImage(bitmap); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = new Pen(Color.FromArgb(91, 161, 240), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (kind)
        {
            case "refresh": g.DrawArc(p, 6, 6, 20, 20, 35, 290); g.DrawLines(p, new Point[] {new Point(25, 5), new Point(25, 12), new Point(18, 12)}); break;
            case "backup": g.DrawRectangle(p, 6, 11, 20, 15); g.DrawRectangle(p, 4, 6, 24, 5); g.DrawLine(p, 12, 17, 20, 17); break;
            case "clock": g.DrawEllipse(p, 5, 5, 22, 22); g.DrawLines(p, new Point[] {new Point(16, 9), new Point(16, 17), new Point(22, 20)}); break;
            case "upgrade": g.DrawLine(p, 16, 25, 16, 6); g.DrawLines(p, new Point[] {new Point(8, 14), new Point(16, 6), new Point(24, 14)}); break;
            case "close": g.DrawLine(p, 8, 8, 24, 24); g.DrawLine(p, 8, 24, 24, 8); break;
            default: g.DrawRectangle(p, 6, 6, 20, 20); g.DrawLine(p, 11, 12, 21, 12); g.DrawLine(p, 11, 19, 21, 19); break;
        }
        Cache[kind] = bitmap; return bitmap;
    }
}
