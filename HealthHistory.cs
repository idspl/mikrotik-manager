using System.Globalization;
using System.Text.RegularExpressions;
namespace MikroTikManager;

public sealed record OpticalSample(DateTime CheckedAt, string Port, string RxPower, string TxPower, string Temperature);
internal static class HealthHistory
{
    internal static double? Temperature(string text)
    {
        var numbers = Regex.Matches(text, @"(?:^|:\s*)(-?\d+(?:\.\d+)?)\s*(?:°?C)?(?:;|$|\s)")
            .Select(m => double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (double?)n : null).Where(n => n.HasValue).ToList();
        return numbers.Count == 0 ? null : numbers.Max();
    }
    internal static double? OpticalNumber(string text) => double.TryParse(text.Replace("dBm", "").Replace("°C", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : null;
    internal static double? Memory(DeviceHealth h) => h.TotalMemory > 0 && h.FreeMemory is not null ? 100d * (h.TotalMemory - h.FreeMemory) / h.TotalMemory : null;
    internal static void Record(RouterRecord router, AppSettings settings)
    {
        if (router.Health is not { } sample) return;
        router.HealthHistory.RemoveAll(h => h.CheckedAt < DateTime.Now.AddDays(-Math.Clamp(settings.HealthHistoryDays, 1, 90)));
        if (router.HealthHistory.LastOrDefault()?.CheckedAt != sample.CheckedAt) router.HealthHistory.Add(sample);
        if (router.HealthHistory.Count > 2016) router.HealthHistory.RemoveRange(0, router.HealthHistory.Count - 2016);
    }
    internal static string Warnings(DeviceHealth? h, AppSettings s)
    {
        if (h is null) return "No readings";
        var warnings = new List<string>();
        if (h.CpuPercent >= s.CpuWarningPercent) warnings.Add("High CPU");
        if (Memory(h) >= s.MemoryWarningPercent) warnings.Add("High memory");
        if (Temperature(h.Temperature) >= s.TemperatureWarningC) warnings.Add("High temperature");
        return warnings.Count == 0 ? "No threshold exceeded" : string.Join(" • ", warnings);
    }
}

internal sealed class HistoryPlot : Control
{
    internal List<(DateTime Time, double? Value)> Points { get; set; } = [];
    internal string Unit { get; set; } = "%";
    internal HistoryPlot() { DoubleBuffered = true; Dock = DockStyle.Fill; ResizeRedraw = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(AppTheme.Surface);
        var bounds = new Rectangle(70, 30, Math.Max(1, Width - 100), Math.Max(1, Height - 95));
        using var pen = new Pen(Color.CornflowerBlue, 2); using var text = new SolidBrush(AppTheme.IsDark ? Color.WhiteSmoke : Color.Black);
        var valid = Points.Where(p => p.Value.HasValue).ToList();
        if (valid.Count == 0) { e.Graphics.DrawString("No retained readings for this metric.", Font, text, 24, 24); return; }
        double min = valid.Min(p => p.Value!.Value), max = valid.Max(p => p.Value!.Value); if (max - min < 1) { min -= 1; max += 1; }
        long first = Points[0].Time.Ticks, range = Math.Max(1, Points[^1].Time.Ticks - first);
        using var grid = new Pen(Color.Gray);
        e.Graphics.DrawRectangle(grid, bounds);
        e.Graphics.DrawString($"{max:0.#} {Unit}", Font, text, 3, bounds.Top);
        e.Graphics.DrawString($"{min:0.#} {Unit}", Font, text, 3, bounds.Bottom - Font.Height);
        e.Graphics.DrawString(Points[0].Time.ToString("dd MMM HH:mm"), Font, text, bounds.Left, bounds.Bottom + 10);
        e.Graphics.DrawString(Points[^1].Time.ToString("dd MMM HH:mm"), Font, text, Math.Max(bounds.Left, bounds.Right - 150), bounds.Bottom + 10);
        PointF? previous = null;
        foreach (var point in Points)
        {
            if (point.Value is not double value) { previous = null; continue; }
            var at = new PointF(bounds.Left + (float)((point.Time.Ticks - first) / (double)range * bounds.Width), bounds.Bottom - (float)((value - min) / (max - min) * bounds.Height));
            if (previous is { } prior) e.Graphics.DrawLine(pen, prior, at);
            e.Graphics.FillEllipse(Brushes.CornflowerBlue, at.X - 2, at.Y - 2, 4, 4); previous = at;
        }
    }
}

public sealed partial class MainForm
{
    private async void ShowHealthHistory()
    {
        if (_operationInProgress) return;
        if (_healthGrid.CurrentRow?.DataBoundItem is not HealthRow row || _routers.FirstOrDefault(r => r.Id == row.Id) is not RouterRecord router) return;
        try { await Task.Run(() => { router.HealthHistory = _store.LoadHealthSamples(router.Id); router.OpticalHistory = _store.LoadOpticalSamples(router.Id); }); }
        catch (Exception ex) { ShowError(ex); return; }
        if (IsDisposed) return;
        using var dialog = new DpiDialog { Text = "Health history — " + router.Name, Width = 1100, Height = 700, StartPosition = FormStartPosition.CenterParent };
        var selector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        selector.Items.AddRange(["CPU %", "Memory used %", "Temperature °C"]);
        foreach (string port in router.OpticalHistory.Select(h => h.Port).Distinct().Order()) { selector.Items.Add("RX • " + port); selector.Items.Add("TX • " + port); }
        var plot = new HistoryPlot(); var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }; split.Panel1.Controls.Add(plot); split.Panel2.Controls.Add(grid);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) }; bar.Controls.Add(selector);
        bar.Controls.Add(new Label { AutoSize = true, Text = "Health is sampled while open; optics are retained when a port is read. No notifications." });
        selector.SelectedIndexChanged += (_, _) => {
            string metric = selector.Text;
            if (selector.SelectedIndex < 3) plot.Points = router.HealthHistory.Select(h => (h.CheckedAt, selector.SelectedIndex switch { 0 => h.CpuPercent, 1 => HealthHistory.Memory(h), _ => HealthHistory.Temperature(h.Temperature) })).ToList();
            else plot.Points = router.OpticalHistory.Where(h => h.Port == metric[5..]).Select(h => (h.CheckedAt, HealthHistory.OpticalNumber(metric.StartsWith("RX") ? h.RxPower : h.TxPower))).ToList();
            plot.Unit = selector.SelectedIndex < 2 ? "%" : selector.SelectedIndex == 2 ? "°C" : "dBm";
            grid.DataSource = plot.Points.AsEnumerable().Reverse().Select(p => new { Time = p.Time, Value = p.Value, Unit = plot.Unit }).ToList(); plot.Invalidate();
        };
        dialog.Controls.Add(split); dialog.Controls.Add(bar); selector.SelectedIndex = 0;
        dialog.Shown += (_, _) => split.SplitterDistance = split.Height * 2 / 3; dialog.ShowDialog(this);
    }
}
