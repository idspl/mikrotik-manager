namespace MikroTikManager;

public sealed partial class MainForm
{
    internal void RenderSmokeViews(float scale = 1F)
    {
        File.AppendAllText("ui-layout-check.log", $"Starting scale {scale}\n");
        // Called only by --self-test with an isolated temporary SecureStore.
        if (_failureBehavior.SelectedItem is not FailureBehavior policy || policy != _store.LoadSettings().DefaultFailureBehavior) throw new Exception("Failure policy default was not selected.");
        _suspendRouterSaves = true; _routers.RaiseListChangedEvents = false;
        for (int i = 0; i < (scale == 1F ? 1000 : 57); i++) _routers.Add(new RouterRecord {
            Name = $"Client-{i + 1:0000}", Host = $"192.0.{2 + i / 250}.{1 + i % 250}", Site = i % 2 == 0 ? "Davanagere" : "Shivamogga",
            Group = "Client-end", Tags = "Customer", Model = "RB750Gr3", ApiStatus = i % 7 == 0 ? "Failed" : "Online", RouterOsVersion = "7.24.4", FirmwareVersion = "7.24.4", AvailableFirmware = i % 2 == 0 ? "7.24.4" : "7.24.5", LastStatus = "Snapshot fixture" });
        _routers.RaiseListChangedEvents = true; _routers.ResetBindings();
        _publishedReleases = new RouterOsReleases("7.24.5", "7.24.4", "7.25beta1");
        _routers[0].UpdateChannel = "long-term";
        if (PublishedVersionStatus(_routers[0], "7.24.4").Tone != ReadingTone.Good || PublishedVersionStatus(_routers[1], "7.24.4").Tone != ReadingTone.Warning)
            throw new Exception("Inventory colors must use channel releases, not bundled firmware");
        RefreshDashboard();
        Show(); Application.DoEvents();
        ApplyLayoutTestScale(this, scale);
        MaximumSize = new Size(4096, 2160);
        Size = new Size(1920, 1040); PerformLayout(); Application.DoEvents();
        void Capture(string name)
        {
            File.AppendAllText("ui-layout-check.log", $"Capture {scale} {name}\n");
            PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
            using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText("ui-" + (int)(scale * 100) + "-" + name + ".base64", Convert.ToBase64String(stream.ToArray()));
        }
        foreach (var label in _dashboardCounts.Values)
        {
            if (label.Height < label.PreferredHeight) throw new Exception($"Dashboard count clips at {scale}: {label.Size}, {label.PreferredSize}");
            var caption = label.Parent!.Controls.OfType<Label>().Single(l => l != label);
            if (caption.Bounds.IntersectsWith(label.Bounds) || caption.Bottom > label.Parent.ClientSize.Height - label.Parent.Padding.Bottom)
                throw new Exception("Dashboard card overlaps at " + scale);
        }
        Capture("dashboard");
        if (_dashboardJobs.Parent is ScrollableControl dashboardScroll)
        {
            dashboardScroll.ScrollControlIntoView(_dashboardJobs); Capture("dashboard-tables");
            dashboardScroll.AutoScrollPosition = Point.Empty;
        }
        _tabs.SelectedTab = _healthPage;
        _routers[0].Health = new DeviceHealth(DateTime.Now, 23, 67108864, 268435456, 67108864, 134217728, "4d 03:12:00", "cpu-temperature: 42 °C");
        _routers[0].LastHealthCheckAt = DateTime.Now; _routers[0].HealthObservedThisSession = true;
        RefreshHealthView(); Capture("health");
        _tabs.SelectedTab = _routerPage; _routerGrid.ClearSelection(); _routerGrid.Rows[20].Selected = true;
        _routerGrid.FirstDisplayedScrollingRowIndex = 12;
        int scroll = _routerGrid.FirstDisplayedScrollingRowIndex;
        var selected = _routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToArray();
        PrepareProgressRows(_routers.Take(10));
        for (int i = 0; i < 1000; i++) UpdateProgress(new(_routers[i % 10].Id, "Stability hold", 1, 90, "Running", "UI load fixture"));
        FlushUiUpdates(); Application.DoEvents();
        if (_routerGrid.FirstDisplayedScrollingRowIndex != scroll || !_routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).SequenceEqual(selected))
            throw new Exception("Progress changed inventory scroll or selection.");
        if (_routerGrid.Rows.Cast<DataGridViewRow>().Any(r => r.Height < _routerGrid.Font.Height + 8))
            throw new Exception("Inventory row text lacks vertical padding at " + scale);
        Capture("devices");
        using (var splash = new SplashForm(preview: true))
        {
            splash.Show(); ApplyLayoutTestScale(splash, scale); Application.DoEvents();
            var panel = splash.Controls.OfType<TableLayoutPanel>().Single();
            var labels = panel.Controls.OfType<Label>().ToList();
            foreach (var label in labels)
            {
                if (label.Height < label.GetPreferredSize(new Size(label.Width, 0)).Height || label.Bottom > panel.ClientSize.Height - panel.Padding.Bottom)
                    throw new Exception("Splash text clips at " + scale);
                if (labels.Any(other => other != label && other.Bounds.IntersectsWith(label.Bounds))) throw new Exception("Splash text overlaps");
            }
            using var bitmap = new Bitmap(splash.Width, splash.Height); splash.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText($"ui-{(int)(scale * 100)}-splash.base64", Convert.ToBase64String(stream.ToArray()));
            splash.Close();
        }
        _tabs.SelectedTab = _maintenancePage; Capture("progress");
        _tabs.SelectedTab = _schedulesPage; Capture("schedules");
        _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Backups");
        var coverageWait = System.Diagnostics.Stopwatch.StartNew();
        while (_readingCoverage && coverageWait.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(10); }
        if (_readingCoverage || _backupGrid.Rows.Count != _routers.Count) throw new Exception("Backup coverage did not populate");
        if (scale == 1F) VerifyBackupSelectionAndContinue();
        Application.DoEvents(); Capture("backups");
        if (_backupGrid.Bottom > _backupGrid.Parent!.ClientSize.Height || _backupGrid.Height < 100 || _backupCoverage.Height < _backupCoverage.GetPreferredSize(new Size(_backupCoverage.Width, 0)).Height || _backupGrid.Top < _backupCoverage.Bottom)
            throw new Exception("Backup summary overlaps table at " + scale);
        using (var dialog = BuildUpgradeScheduleDialog(out _, out var date, out _, out _, out _))
        {
            dialog.Show(this); Application.DoEvents(); ApplyLayoutTestScale(dialog, scale); Application.DoEvents();
            var button = (Button)dialog.AcceptButton!;
            var bottom = dialog.PointToClient(button.PointToScreen(new Point(0, button.Height)));
            if (bottom.Y > dialog.ClientSize.Height || !button.Visible) throw new Exception("Schedule footer is clipped at " + scale);
            int requiredDateWidth = TextRenderer.MeasureText(date.Value.ToString("dd MMM yyyy  HH:mm"), date.Font).Width + (int)(28 * scale);
            if (date.Width < requiredDateWidth) throw new Exception($"Schedule date clips at {scale}: {date.Width} < {requiredDateWidth}");
            using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText($"ui-{(int)(scale * 100)}-schedule-dialog.base64", Convert.ToBase64String(stream.ToArray()));
            dialog.Close();
        }
        if (scale == 1.5F)
        {
            VerifyThemeSwitch(Capture);
            using var preview = new DpiDialog { Text = "Reading colors — fixture", Width = 900, Height = 460 };
            var grid = new SmoothGrid { Dock = DockStyle.Fill }; ConfigureDashboardGrid(grid, ["Reading", "Value"]); preview.Controls.Add(grid);
            var rows = new[] { new[] { "Firmware: up to date", "7.20" }, new[] { "Firmware: update available", "7.19" }, new[] { "Temperature: normal advisory", "42 °C" }, new[] { "Temperature: warm advisory", "75 °C" }, new[] { "Temperature: hot advisory", "90 °C" }, new[] { "Fiber: no loss reported", "-12dBm" }, new[] { "Fiber: loss reported", "-40dBm" }, new[] { "Fiber: status unknown", "-12dBm" }, new[] { "Temperature: unavailable", "Unavailable" } };
            var assessments = new[] { ReadingColors.Firmware("7.20", "7.20"), ReadingColors.Firmware("7.19", "7.20"), ReadingColors.Temperature("42 C"), ReadingColors.Temperature("75 C"), ReadingColors.Temperature("90 C"), ReadingColors.Fiber("-12dBm", "no"), ReadingColors.Fiber("-40dBm", "yes"), ReadingColors.Fiber("-12dBm", null), ReadingColors.Temperature("Unavailable") };
            grid.CellFormatting += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 1) ReadingColors.Apply(grid, e, assessments[e.RowIndex]); };
            ReplaceItems(grid, rows); preview.Show(this); Application.DoEvents();
            foreach (string theme in new[] { "Dark", "Light" })
            {
                AppTheme.Set(theme); AppTheme.Apply(preview); grid.Rows[3].Selected = true; Application.DoEvents();
                if (grid.Rows[3].Cells[1].InheritedStyle is null) throw new Exception("Missing cell style");
                using var bitmap = new Bitmap(preview.Width, preview.Height); preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText($"ui-150-colors-{theme}.base64", Convert.ToBase64String(stream.ToArray()));
            }
            preview.Close();
        }
        Hide();
    }
    // Geometry + typography stress fixtures; these do not emulate monitor switching.
    private static void ApplyLayoutTestScale(Form form, float scale)
    {
        if (scale == 1F) return;
        var fonts = new List<(Control Control, Font Font)>();
        void Collect(Control parent) { fonts.Add((parent, parent.Font)); foreach (Control c in parent.Controls) Collect(c); }
        Collect(form); form.SuspendLayout(); form.AutoScaleMode = AutoScaleMode.None;
        form.Scale(new SizeF(scale, scale));
        foreach (var item in fonts) item.Control.Font = new Font(item.Font.FontFamily, item.Font.SizeInPoints * scale, item.Font.Style);
        form.ResumeLayout(true); form.PerformLayout(); Application.DoEvents();
    }
}
