namespace MikroTikManager;

// These dialogs are constructed entirely in code, in 96-DPI logical units.
// Defer the initial scaling until every child has been added.
public class DpiDialog : Form
{
    public DpiDialog()
    {
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 9F);
        Icon = AppIcon.Current;
    }

    protected override void OnLoad(EventArgs e)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
        PerformAutoScale();
        AppTheme.Apply(this);
        base.OnLoad(e);
    }

    // Keep action buttons outside the scrollable content on small/high-DPI screens.
    protected void SetScrollableContent(Control content, FlowLayoutPanel footer)
    {
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        content.Dock = DockStyle.Top; content.AutoSize = true;
        body.Controls.Add(content);
        footer.Dock = DockStyle.Bottom; footer.AutoSize = true;
        footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        footer.WrapContents = true; footer.Padding = new Padding(12, 8, 12, 12);
        Controls.Add(body); Controls.Add(footer);
        FormBorderStyle = FormBorderStyle.Sizable;
        bool sizing = false;
        void Fit()
        {
            if (sizing || IsDisposed || !IsHandleCreated) return;
            sizing = true;
            try
            {
                var area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 24),
                    Math.Min(MinimumSize.Height, area.Height - 24));
                int width = Math.Min(Width, area.Width - 24);
                Width = Math.Max(280, width);
                PerformLayout();
                int desired = content.GetPreferredSize(new Size(body.ClientSize.Width, 0)).Height
                    + footer.GetPreferredSize(new Size(ClientSize.Width, 0)).Height + Height - ClientSize.Height + 12;
                Height = Math.Min(Math.Max(Height, desired), area.Height - 24);
                Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
                    Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
            }
            finally { sizing = false; }
        }
        Shown += (_, _) => Fit();
        DpiChanged += (_, _) => { if (IsHandleCreated) BeginInvoke((Action)Fit); };
        FontChanged += (_, _) => { if (IsHandleCreated) BeginInvoke((Action)Fit); };
    }
}
