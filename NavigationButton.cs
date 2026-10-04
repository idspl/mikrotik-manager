using System.Drawing.Drawing2D;
namespace MikroTikManager;

internal sealed class NavigationButton : Button
{
    public string Section { get; init; } = "";
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float size = 18 * DeviceDpi / 96f;
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TranslateTransform(10 * DeviceDpi / 96f, (Height - size) / 2);
        e.Graphics.ScaleTransform(size / 18, size / 18);
        using var pen = new Pen(ForeColor, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (Section)
        {
            case "Dashboard":
                foreach (var point in new[] { new Point(1, 1), new Point(10, 1), new Point(1, 10), new Point(10, 10) }) e.Graphics.DrawRectangle(pen, point.X, point.Y, 6, 6);
                break;
            case "Devices":
                e.Graphics.DrawRectangle(pen, 1, 2, 16, 10); e.Graphics.DrawLine(pen, 9, 12, 9, 16); e.Graphics.DrawLine(pen, 5, 16, 13, 16); break;
            case "Health":
                e.Graphics.DrawLines(pen, new Point[] { new(0, 9), new(4, 9), new(7, 2), new(11, 16), new(14, 9), new(18, 9) }); break;
            case "Maintenance Progress":
                e.Graphics.DrawEllipse(pen, 1, 1, 16, 16); e.Graphics.DrawLine(pen, 9, 4, 9, 13); e.Graphics.DrawLines(pen, new Point[] { new(5, 8), new(9, 4), new(13, 8) }); break;
            case "Schedules":
                e.Graphics.DrawRectangle(pen, 1, 3, 16, 14); e.Graphics.DrawLine(pen, 1, 7, 17, 7); e.Graphics.DrawLine(pen, 5, 1, 5, 5); e.Graphics.DrawLine(pen, 13, 1, 13, 5); e.Graphics.DrawLine(pen, 5, 11, 8, 11); break;
            case "Backups":
                e.Graphics.DrawRectangle(pen, 1, 5, 16, 12); e.Graphics.DrawLine(pen, 3, 2, 15, 2); e.Graphics.DrawLine(pen, 7, 9, 11, 9); break;
            case "Live Log":
                e.Graphics.DrawRectangle(pen, 2, 1, 14, 16); foreach (int y in new[] { 5, 9, 13 }) e.Graphics.DrawLine(pen, 5, y, 13, y); break;
            default:
                e.Graphics.DrawEllipse(pen, 4, 4, 10, 10); e.Graphics.DrawEllipse(pen, 7, 7, 4, 4);
                foreach (int angle in new[] { 0, 45, 90, 135 }) { var saved = e.Graphics.Save(); e.Graphics.TranslateTransform(9, 9); e.Graphics.RotateTransform(angle); e.Graphics.DrawLine(pen, -8, 0, -5, 0); e.Graphics.DrawLine(pen, 5, 0, 8, 0); e.Graphics.Restore(saved); } break;
        }
        e.Graphics.Restore(state);
    }
}
