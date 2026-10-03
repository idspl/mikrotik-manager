namespace MikroTikManager;

// These dialogs are constructed entirely in code, in 96-DPI logical units.
// Defer the initial scaling until every child has been added.
internal class DpiDialog : Form
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
        base.OnLoad(e);
    }
}
