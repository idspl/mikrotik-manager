namespace MikroTikManager;

public sealed partial class MainForm
{
    private void VerifyToolbarNavigation(float scale, Action<string> capture)
    {
        static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        foreach (TabPage page in _tabs.TabPages)
        {
            _tabs.SelectedTab = page; PerformLayout(); Application.DoEvents();
            var bar = Descendants(page).OfType<ToolStrip>().Single(s => s.Name == "PageToolbar");
            if (!bar.CanOverflow || bar.Height < bar.Font.Height || bar.Height > bar.Font.Height * 4 + 20)
                throw new Exception("Page toolbar must remain one compact accessible row: " + page.Text + " at " + scale);
            foreach (ToolStripItem item in bar.Items)
                if (item.Placement == ToolStripItemPlacement.Main && item.Bounds.Bottom > bar.ClientSize.Height)
                    throw new Exception("Toolbar item clips: " + page.Text + " / " + item.Text);
        }
        var selected = SelectedRouters().Select(r => r.Id).ToArray();
        var msg = new Message();
        for (int i = 0; i < 8; i++)
        {
            ProcessCmdKey(ref msg, Keys.Control | (Keys)((int)Keys.D1 + i));
            if (_tabs.SelectedIndex != i) throw new Exception("Page shortcut did not select page " + i);
        }
        ProcessCmdKey(ref msg, Keys.Control | Keys.F);
        if (_tabs.SelectedTab != _routerPage || !_routerSearch.Focused) throw new Exception("Ctrl+F must focus device search");
        SetNavigationCollapsed(true);
        if (!_store.LoadSettings().NavigationCollapsed || _navigationButtons.Any(x => !x.Button.Compact || x.Button.AccessibleName != PageTitle(x.Page)))
            throw new Exception("Compact sidebar must persist and preserve accessible names");
        capture("compact-navigation");
        SetNavigationCollapsed(false);
        if (_store.LoadSettings().NavigationCollapsed || !SelectedRouters().Select(r => r.Id).SequenceEqual(selected))
            throw new Exception("Navigation must preserve device selection");
        _tabs.SelectedTab = _dashboardPage;
    }
}
