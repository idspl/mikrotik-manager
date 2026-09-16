using System.Drawing.Drawing2D;

namespace IndigoRouterScheduler;

public sealed class SplashForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    public SplashForm()
    {
        Icon = AppIcon.Current;
        Width = 640;
        Height = 330;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(7, 22, 43);
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Shown += (_, _) => _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(7, 22, 43), Color.FromArgb(16, 71, 121), 25f);
        e.Graphics.FillRectangle(background, ClientRectangle);

        using var accent = new SolidBrush(Color.FromArgb(36, 156, 255));
        e.Graphics.FillRectangle(accent, 0, Height - 8, Width, 8);
        using var indigoFont = new Font("Segoe UI", 34, FontStyle.Bold);
        using var productFont = new Font("Segoe UI", 21, FontStyle.Regular);
        using var detailFont = new Font("Segoe UI", 10, FontStyle.Regular);
        using var white = new SolidBrush(Color.White);
        using var soft = new SolidBrush(Color.FromArgb(205, 225, 242));

        e.Graphics.DrawString("INDIGO", indigoFont, white, 54, 68);
        e.Graphics.DrawString("Router Scheduler", productFont, white, 57, 132);
        e.Graphics.DrawString("API Management  •  Backup  •  Upgrade", detailFont, soft, 60, 188);
        e.Graphics.DrawString("Version 0.1.10", detailFont, soft, 60, 252);
    }
}
