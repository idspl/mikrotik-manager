using System.Runtime.CompilerServices;

namespace MikroTikManager;

internal static class AppTheme
{
    internal static bool IsDark { get; private set; }
    internal static void Set(string? theme) => IsDark = theme == "Dark";
    internal static Color Background => IsDark ? Color.FromArgb(19, 25, 34) : Color.FromArgb(245, 247, 251);
    internal static Color Surface => IsDark ? Color.FromArgb(29, 37, 49) : Color.White;
    internal static Color Text => IsDark ? Color.FromArgb(230, 236, 244) : Color.FromArgb(20, 40, 65);
    internal static Color Muted => IsDark ? Color.FromArgb(163, 177, 196) : Color.DimGray;
    internal static Color Border => IsDark ? Color.FromArgb(64, 78, 98) : Color.FromArgb(225, 229, 235);
    internal static Color Selection => IsDark ? Color.FromArgb(42, 77, 118) : Color.FromArgb(218, 233, 250);
    internal static Color Alternate => IsDark ? Color.FromArgb(24, 32, 43) : Color.FromArgb(247, 249, 252);
    internal static Color SuccessBack => IsDark ? Color.FromArgb(25, 65, 49) : Color.FromArgb(231, 247, 238);
    internal static Color SuccessText => IsDark ? Color.FromArgb(133, 225, 174) : Color.FromArgb(24, 112, 73);
    internal static Color FailureBack => IsDark ? Color.FromArgb(78, 35, 44) : Color.FromArgb(255, 235, 235);
    internal static Color FailureText => IsDark ? Color.FromArgb(255, 165, 173) : Color.FromArgb(160, 36, 36);
    internal static Color RunningBack => IsDark ? Color.FromArgb(30, 54, 86) : Color.FromArgb(229, 239, 255);
    internal static Color RunningText => IsDark ? Color.FromArgb(150, 194, 255) : Color.FromArgb(32, 83, 148);

    private sealed record Original(Color Back, Color Fore);
    private static readonly ConditionalWeakTable<Control, Original> Originals = new();
    private static readonly ConditionalWeakTable<Control, object> OwnerDrawn = new();

    internal static void Apply(Control root)
    {
        CaptureOriginals(root);
        ApplyCore(root);
    }

    private static void CaptureOriginals(Control root)
    {
        if (root.Name == "Navigation") return;
        Originals.GetValue(root, c => new Original(c.BackColor, c.ForeColor));
        foreach (Control child in root.Controls) CaptureOriginals(child);
        if (root is ToolStrip strip)
            foreach (ToolStripItem item in strip.Items)
                if (item is ToolStripControlHost host) CaptureOriginals(host.Control);
    }

    private static void ApplyCore(Control root)
    {
        // Navigation already has a dark, accessible palette in both themes.
        if (root.Name == "Navigation") return;
        var original = Originals.GetValue(root, c => new Original(c.BackColor, c.ForeColor));
        bool input = root is TextBoxBase or ListBox or ComboBox or UpDownBase or DateTimePicker or ListView;
        root.BackColor = IsDark ? (input || original.Back.ToArgb() == Color.White.ToArgb() ? Surface : Background) : original.Back;
        root.ForeColor = IsDark ? DarkText(original.Fore) : original.Fore;
        if (root is Button button)
        {
            bool primary = original.Back.B > original.Back.R + 50 && original.Back.GetBrightness() < .6F;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? original.Back : Surface;
            button.ForeColor = primary ? Color.White : Text;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Selection;
        }
        if (root is TabPage page) page.UseVisualStyleBackColor = false;
        if (root is CheckBox check) check.UseVisualStyleBackColor = false;
        if (root is RadioButton radio) radio.UseVisualStyleBackColor = false;
        if (root is LinkLabel link) { link.LinkColor = RunningText; link.ActiveLinkColor = RunningText; }
        if (root is ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            if (!OwnerDrawn.TryGetValue(combo, out _))
            {
                OwnerDrawn.Add(combo, new object()); combo.DrawMode = DrawMode.OwnerDrawFixed;
                void SizeItems() => combo.ItemHeight = Math.Max(18, combo.Font.Height + 6);
                combo.FontChanged += (_, _) => SizeItems(); SizeItems();
                combo.DrawItem += (_, e) => {
                    if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
                    Color back = (e.State & DrawItemState.Selected) != 0 ? Selection : Surface;
                    Color fore = combo.Enabled ? Text : Muted;
                    using var brush = new SolidBrush(back); e.Graphics.FillRectangle(brush, e.Bounds);
                    string value = e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
                    TextRenderer.DrawText(e.Graphics, value, combo.Font, Rectangle.Inflate(e.Bounds, -3, 0), fore,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, fore, back);
                };
            }
        }
        if (root is DataGridView grid) ApplyGrid(grid);
        if (root is ToolStrip strip) ApplyStrip(strip);
        if (root.ContextMenuStrip is { } menu) ApplyStrip(menu);
        if (root is ListView list && !OwnerDrawn.TryGetValue(list, out _))
        {
            OwnerDrawn.Add(list, new object()); list.OwnerDraw = true;
            list.DrawColumnHeader += (_, e) => {
                using var brush = new SolidBrush(Surface); e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", list.Font, Rectangle.Inflate(e.Bounds, -5, 0), Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.DrawItem += (_, e) => { if (list.View != View.Details) e.DrawDefault = true; };
            list.DrawSubItem += (_, e) => e.DrawDefault = true;
        }
        if (root is TabControl tabs && tabs.ItemSize.Height > 1 && !OwnerDrawn.TryGetValue(tabs, out _))
        {
            OwnerDrawn.Add(tabs, new object()); tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += (_, e) => {
                using var brush = new SolidBrush(e.Index == tabs.SelectedIndex ? Selection : Surface);
                e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }
        foreach (Control child in root.Controls) ApplyCore(child);
        root.Invalidate();
    }

    private static Color DarkText(Color color)
    {
        if (color.ToArgb() == Color.DimGray.ToArgb() || color.ToArgb() == SystemColors.GrayText.ToArgb()) return Muted;
        if (color.R > color.G + 40 && color.R > color.B + 40) return FailureText;
        if (color.B > color.R + 20) return RunningText;
        return Text;
    }

    internal static void ApplyGrid(DataGridView grid)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = Surface; grid.GridColor = Border;
        grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Selection; grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Alternate;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
        grid.RowsDefaultCellStyle.BackColor = Color.Empty; grid.RowsDefaultCellStyle.ForeColor = Color.Empty;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 49, 78);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(28, 49, 78);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowHeadersDefaultCellStyle.BackColor = Surface; grid.RowHeadersDefaultCellStyle.ForeColor = Text;
        grid.Invalidate();
    }

    private static void ApplyStrip(ToolStrip strip)
    {
        strip.Renderer = new ThemeRenderer();
        strip.BackColor = Surface; strip.ForeColor = Text;
        foreach (ToolStripItem item in strip.Items)
        {
            item.ForeColor = Text; item.BackColor = Surface;
            if (item is ToolStripControlHost host) Apply(host.Control);
            if (item is ToolStripDropDownItem drop) ApplyStrip(drop.DropDown);
        }
    }

    private sealed class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) { }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        { e.ArrowColor = e.Item.Enabled ? Text : Muted; base.OnRenderArrow(e); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        { e.TextColor = e.Item.Enabled ? Text : Muted; base.OnRenderItemText(e); }
    }

    private sealed class ThemeColors : ProfessionalColorTable
    {
        public ThemeColors() { UseSystemColors = false; }
        public override Color CheckBackground => Selection;
        public override Color CheckSelectedBackground => Selection;
        public override Color CheckPressedBackground => Selection;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Selection;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color MenuItemSelectedGradientBegin => Selection;
        public override Color MenuItemSelectedGradientEnd => Selection;
        public override Color MenuItemPressedGradientBegin => Selection;
        public override Color MenuItemPressedGradientMiddle => Selection;
        public override Color MenuItemPressedGradientEnd => Selection;
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color StatusStripGradientBegin => Surface;
        public override Color StatusStripGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color ButtonSelectedHighlight => Selection;
        public override Color ButtonSelectedGradientBegin => Selection;
        public override Color ButtonSelectedGradientMiddle => Selection;
        public override Color ButtonSelectedGradientEnd => Selection;
    }
}
