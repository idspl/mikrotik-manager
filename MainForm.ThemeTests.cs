namespace MikroTikManager;

public sealed partial class MainForm
{
    private void VerifyThemeSwitch(Action<string> capture)
    {
        _tabs.SelectedTab = _routerPage;
        int scroll = _routerGrid.FirstDisplayedScrollingRowIndex;
        var selected = _routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToArray();
        var before = _store.LoadSettings();
        ChangeTheme("Light");
        ChangeTheme("Dark");
        var after = _store.LoadSettings();
        if (!AppTheme.IsDark || after.Theme != "Dark" || after.DefaultApiPort != before.DefaultApiPort || after.UseApiSsl != before.UseApiSsl)
            throw new Exception("Theme preference was not saved independently of connection settings.");
        if (_routerGrid.BackgroundColor != AppTheme.Surface || _routerGrid.DefaultCellStyle.ForeColor != AppTheme.Text
            || _routerContextMenu.BackColor != AppTheme.Surface || _log.BackColor != AppTheme.Surface)
            throw new Exception("Dark grid, menu or log theme was not applied.");
        if (_routerGrid.FirstDisplayedScrollingRowIndex != scroll || !_routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).SequenceEqual(selected))
            throw new Exception("Theme switching disturbed scroll or selection.");
        capture("dark-devices");
        _tabs.SelectedTab = _dashboardPage; capture("dark-dashboard");
        _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Settings"); capture("dark-settings");
        using (var dialog = BuildUpgradeScheduleDialog(out _, out var date, out _, out _, out _))
        {
            dialog.Show(this); Application.DoEvents(); ApplyLayoutTestScale(dialog, 1.5F); Application.DoEvents();
            if (dialog.BackColor != AppTheme.Background || date.BackColor != AppTheme.Surface)
                throw new Exception("New application dialog did not inherit dark theme.");
            using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText("ui-150-dark-dialog.base64", Convert.ToBase64String(stream.ToArray()));
            dialog.Close();
        }
        // Restart construction must restore the saved setting without a theme-change click.
        using (var reopened = new MainForm(_store, smokeTest: true))
            if (!AppTheme.IsDark || reopened._themeChoice.SelectedItem?.ToString() != "Dark")
                throw new Exception("Saved theme was not restored on startup.");
        ChangeTheme("Light");
        if (AppTheme.IsDark || _store.LoadSettings().Theme != "Light" || _routerGrid.BackgroundColor != Color.White)
            throw new Exception("Switching back to light failed.");
    }
}
