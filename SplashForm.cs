namespace MikroTikManager;

public sealed class SplashForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    internal SplashForm(bool preview) : this() { if (preview) _timer.Interval = 60000; }
    public SplashForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 10F);
        Icon = AppIcon.Current; ClientSize = new Size(600, 330);
        StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
        BackColor = Color.FromArgb(17, 34, 56); ForeColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(32, 28, 32, 22) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Label Line(string text, float size, Color color) => new() { Text = text, AutoSize = true, Dock = DockStyle.Fill, ForeColor = color,
            Font = new Font("Segoe UI", size, text == "INDIGO" ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(Line("INDIGO", 28, Color.White), 0, 0);
        layout.Controls.Add(Line("One Partner. Every Possibility.", 11, Color.LightSteelBlue), 0, 1);
        layout.Controls.Add(Line("MikroTik Manager", 20, Color.White), 0, 2);
        layout.Controls.Add(new Panel(), 0, 3);
        layout.Controls.Add(Line("Version 0.7.0", 10, Color.LightSteelBlue), 0, 4);
        layout.Controls.Add(Line("MikroTik is a trademark of MikroTikls SIA. Independent Indigo project.", 8, Color.LightSteelBlue), 0, 5);
        Controls.Add(layout);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 4, BackColor = Color.FromArgb(54, 155, 242) });
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Shown += (_, _) => _timer.Start(); FormClosed += (_, _) => _timer.Dispose();
    }
    protected override void OnLoad(EventArgs e)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        PerformAutoScale(); base.OnLoad(e);
    }
}
