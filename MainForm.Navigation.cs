namespace MikroTikManager;

public sealed partial class MainForm
{
    private Panel? _navigationPanel;
    private Label? _navigationBrand, _navigationFooter;
    private Button? _navigationToggle;
    private bool _navigationCollapsed;
    private readonly List<(NavigationButton Button, TabPage Page)> _navigationButtons = [];

    private ToolStrip PageToolbar(string title, params ToolStripItem[] actions)
    {
        var strip = new ToolStrip { Name = "PageToolbar", Dock = DockStyle.Top, AutoSize = true,
            CanOverflow = true, GripStyle = ToolStripGripStyle.Hidden, Font = Font,
            Padding = new Padding(8, 5, 8, 5), Stretch = true, TabStop = true,
            AccessibleName = title + " actions" };
        strip.ItemAdded += (_, e) => { if (e.Item is not ToolStripLabel and not ToolStripSeparator) e.Item.Padding = new Padding(6, 3, 6, 3); };
        strip.Items.Add(new ToolStripLabel(title) { Padding = new Padding(0, 0, 10, 0), Overflow = ToolStripItemOverflow.Never });
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.AddRange(actions);
        foreach (ToolStripItem item in actions) item.Padding = new Padding(6, 3, 6, 3);
        return strip;
    }

    private static ToolStripButton ToolbarAction(string text, EventHandler handler)
        => new(text, null, handler) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = text };

    private static ToolStripDropDownButton ToolbarMenu(string text, params ToolStripItem[] items)
    {
        var menu = new ToolStripDropDownButton(text);
        menu.DropDownItems.AddRange(items);
        return menu;
    }
    private static ToolStripMenuItem MenuAction(string text, EventHandler handler) => new(text, null, handler);

    private void SetNavigationCollapsed(bool collapsed, bool save = true)
    {
        _navigationCollapsed = collapsed;
        if (_navigationPanel is null) return;
        float factor = Math.Max(DeviceDpi / 96f, Font.SizeInPoints / 9f);
        _navigationPanel.SuspendLayout();
        _navigationPanel.Width = (int)((collapsed ? 60 : 182) * factor);
        _navigationPanel.Padding = new Padding((int)((collapsed ? 6 : 10) * factor));
        if (_navigationBrand is not null) _navigationBrand.Visible = !collapsed;
        if (_navigationFooter is not null) _navigationFooter.Visible = !collapsed;
        if (_navigationToggle is not null) { _navigationToggle.Text = collapsed ? "›" : "‹  Collapse menu"; _navigationToggle.Height = (int)(30 * factor); }
        foreach (var (button, page) in _navigationButtons)
        {
            button.Compact = collapsed;
            button.Text = collapsed ? "" : PageTitle(page);
            button.Padding = collapsed ? Padding.Empty : new Padding((int)(34 * factor), 0, 4, 0);
            button.Invalidate();
        }
        _navigationPanel.ResumeLayout(true);
        if (save)
        {
            try { var settings = _store.LoadSettings(); settings.NavigationCollapsed = collapsed; _store.SaveSettings(settings); }
            catch (Exception ex) { ShowError(ex); }
        }
    }

    private static string PageTitle(TabPage page) => page.Text == "Maintenance Progress" ? "Maintenance" : page.Text;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.Modifiers) == Keys.Control && (keyData & Keys.KeyCode) >= Keys.D1 && (keyData & Keys.KeyCode) <= Keys.D8)
        { _tabs.SelectedIndex = (int)(keyData & Keys.KeyCode) - (int)Keys.D1; return true; }
        if (keyData == (Keys.Control | Keys.F))
        { _tabs.SelectedTab = _routerPage; _routerSearch.Focus(); _routerSearch.SelectAll(); return true; }
        if (keyData == (Keys.Control | Keys.B))
        { SetNavigationCollapsed(!_navigationCollapsed); return true; }
        if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        { int direction = keyData.HasFlag(Keys.Shift) ? -1 : 1; _tabs.SelectedIndex = (_tabs.SelectedIndex + direction + _tabs.TabCount) % _tabs.TabCount; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowNavigationHelp(object? sender, EventArgs e) => MessageBox.Show(this,
        "Ctrl+1…8  Open sidebar pages in order\nCtrl+Tab / Ctrl+Shift+Tab  Next / previous page\nCtrl+F  Find a device\nCtrl+B  Collapse / expand sidebar\n\nUse the toolbar overflow arrow when space is limited.\nCtrl-click selects individual rows; Shift-click selects a range.\nSelections remain when moving between pages.",
        "Navigation shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
