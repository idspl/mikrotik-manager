namespace MikroTikManager;

public sealed partial class MainForm
{
    internal void RenderSmokeViews(float scale = 1F)
    {
        // Called only by --self-test with an isolated temporary SecureStore.
        if (_failureBehavior.SelectedItem is not FailureBehavior policy || policy != _store.LoadSettings().DefaultFailureBehavior) throw new Exception("Failure policy default was not selected.");
        _suspendRouterSaves = true; _routers.RaiseListChangedEvents = false;
        for (int i = 0; i < 1000; i++) _routers.Add(new RouterRecord {
            Name = $"Client-{i + 1:0000}", Host = $"192.0.{2 + i / 250}.{1 + i % 250}", Site = i % 2 == 0 ? "Davanagere" : "Shivamogga",
            Group = "Client-end", Tags = "Customer", Model = "RB750Gr3", ApiStatus = i % 7 == 0 ? "Failed" : "Online", RouterOsVersion = "7.24.4", FirmwareVersion = "7.24.4", LastStatus = "Snapshot fixture" });
        _routers.RaiseListChangedEvents = true; _routers.ResetBindings();
        RefreshDashboard();
        Show(); Application.DoEvents();
        ApplyLayoutTestScale(this, scale);
        Size = new Size(1920, 1040); PerformLayout(); Application.DoEvents();
        void Capture(string name)
        {
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
        _tabs.SelectedTab = _routerPage; _routerGrid.ClearSelection(); _routerGrid.Rows[20].Selected = true;
        _routerGrid.FirstDisplayedScrollingRowIndex = 12;
        int scroll = _routerGrid.FirstDisplayedScrollingRowIndex;
        var selected = _routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToArray();
        PrepareProgressRows(_routers.Take(10));
        for (int i = 0; i < 1000; i++) UpdateProgress(new(_routers[i % 10].Id, "Stability hold", 1, 90, "Running", "UI load fixture"));
        FlushUiUpdates(); Application.DoEvents();
        if (_routerGrid.FirstDisplayedScrollingRowIndex != scroll || !_routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).SequenceEqual(selected))
            throw new Exception("Progress changed inventory scroll or selection.");
        Capture("devices");
        _tabs.SelectedTab = _maintenancePage; Capture("progress");
        _tabs.SelectedTab = _schedulesPage; Capture("schedules");
        _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Backups");
        _backupCoverage.Text = "57 devices • 0 covered • 57 never backed up • 57 unscheduled\n0 overdue • 0 failed • 0 missing files | Checked 22:42:00";
        Application.DoEvents(); Capture("backups");
        if (_backupCoverage.Height < _backupCoverage.GetPreferredSize(new Size(_backupCoverage.Width, 0)).Height || _backupGrid.Top < _backupCoverage.Bottom)
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
