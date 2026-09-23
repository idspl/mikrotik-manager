using System.Drawing.Drawing2D;

namespace MikroTikManager;

public sealed class SplashForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    public SplashForm()
    {
        Icon = AppIcon.Current;
        Width = 640;
        Height = 350;
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
        using var brandFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var detailFont = new Font("Segoe UI", 10, FontStyle.Regular);
        using var legalFont = new Font("Segoe UI", 8, FontStyle.Regular);
        using var white = new SolidBrush(Color.White);
        using var soft = new SolidBrush(Color.FromArgb(205, 225, 242));

        e.Graphics.DrawString("INDIGO", indigoFont, white, 54, 53);
        e.Graphics.DrawString("MikroTik Manager", productFont, white, 57, 116);
        e.Graphics.DrawString("MADE FOR MIKROTIK", brandFont, accent, 60, 162);
        e.Graphics.DrawString("API Management  •  Backup  •  Upgrade", detailFont, soft, 60, 198);
        e.Graphics.DrawString("Version 0.2.1", detailFont, soft, 60, 247);
        e.Graphics.DrawString("MikroTik is a trademark of MikroTikls SIA. Independent Indigo project.", legalFont, soft, 60, 286);
    }
}
