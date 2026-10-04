namespace MikroTikManager;

// WinForms' native disabled-button text is too dark on our dark surfaces.
internal sealed class ThemedButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Enabled || !AppTheme.IsDark || FlatStyle != FlatStyle.Flat) return;
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(fill, ClientRectangle);
        using var border = new Pen(FlatAppearance.BorderColor);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
        var textBounds = Rectangle.FromLTRB(Padding.Left, Padding.Top,
            Math.Max(Padding.Left, ClientSize.Width - Padding.Right), Math.Max(Padding.Top, ClientSize.Height - Padding.Bottom));
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, AppTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | (UseMnemonic ? TextFormatFlags.Default : TextFormatFlags.NoPrefix));
    }
}
