namespace MikroTikManager;

public sealed partial class MainForm
{
    private readonly SmoothGrid _backupGrid = new();
    private readonly Label _backupCoverage = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(15), Text = "Refresh coverage to check local backup files and schedules." };
    private bool _readingCoverage;
    private sealed record BackupCoverageRow(Guid Id, string Device, string Site, string Coverage, DateTime? LastBackup, DateTime? LastAttempt, string Error);

    private TabPage BuildBackupsPage()
    {
        var page = new TabPage("Backups");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        bar.Controls.Add(Button("Refresh Coverage", async (_, _) => await RefreshBackupCoverageAsync()));
        bar.Controls.Add(Button("Compare Exports", CompareBackups));
        bar.Controls.Add(Button("Backup History", (_, _) => { SelectBackupDevice(); ShowBackupHistory(this, EventArgs.Empty); }));
        bar.Controls.Add(Button("Backup Selected Device", (_, _) => { SelectBackupDevice(); BackupSelectedRouters(this, EventArgs.Empty); }));
        bar.Controls.Add(Button("Manage Schedules", (_, _) => _tabs.SelectedTab = _schedulesPage));
        _backupGrid.Dock = DockStyle.Fill; _backupGrid.ReadOnly = true; _backupGrid.AllowUserToAddRows = false;
        _backupGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _backupGrid.MultiSelect = false;
        _backupGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; StyleGrid(_backupGrid);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.Dock = DockStyle.Fill; bar.Margin = Padding.Empty; _backupCoverage.Margin = Padding.Empty; _backupGrid.Margin = Padding.Empty;
        layout.Controls.Add(bar, 0, 0); layout.Controls.Add(_backupCoverage, 0, 1); layout.Controls.Add(_backupGrid, 0, 2);
        page.Controls.Add(layout);
        void SizeSummary()
        {
            int width = Math.Max(100, layout.ClientSize.Width - _backupCoverage.Padding.Horizontal);
            int height = TextRenderer.MeasureText(_backupCoverage.Text, _backupCoverage.Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + _backupCoverage.Padding.Vertical + 8;
            if (Math.Abs(layout.RowStyles[1].Height - height) > 1) layout.RowStyles[1].Height = height;
        }
        layout.SizeChanged += (_, _) => SizeSummary(); _backupCoverage.TextChanged += (_, _) => SizeSummary();
        _backupCoverage.FontChanged += (_, _) => SizeSummary(); SizeSummary();
        _tabs.SelectedIndexChanged += async (_, _) => { if (_tabs.SelectedTab == page) await RefreshBackupCoverageAsync(); };
        return page;
    }
    private void SelectBackupDevice()
    {
        _routerGrid.ClearSelection();
        if (_backupGrid.CurrentRow?.DataBoundItem is not BackupCoverageRow item) return;
        _routerSearch.Clear(); _statusFilter.SelectedIndex = 0;
        foreach (DataGridViewRow row in _routerGrid.Rows) if (row.DataBoundItem is RouterRecord r && r.Id == item.Id) row.Selected = true;
    }
    private async Task RefreshBackupCoverageAsync()
    {
        if (_readingCoverage) return; _readingCoverage = true;
        try
        {
            // Snapshot mutable inventory on the UI thread; file probes run in the background.
            var snapshots = _routers.Select(r => new { r.Id, r.Name, r.Site, Last = r.Backups.LastOrDefault(), r.LastBackupAttemptAt, r.LastBackupError }).ToList();
            var schedules = _jobs.Where(j => j.Kind == ScheduledJobKind.Backup && j.State is "Scheduled" or "Running").Select(j => new { Ids = j.RouterIds.ToArray(), j.ScheduledLocalTime, j.State }).ToList();
            var rows = await Task.Run(() => snapshots.Select(r => {
                var issues = new List<string>(); var jobs = schedules.Where(j => j.Ids.Contains(r.Id)).ToList();
                if (r.Last is null) issues.Add("Never backed up");
                else if (!File.Exists(r.Last.BackupPath) || !File.Exists(r.Last.ExportPath)) issues.Add("Files missing");
                if (jobs.Count == 0) issues.Add("No active schedule");
                if (jobs.Any(j => j.State == "Scheduled" && j.ScheduledLocalTime < DateTime.Now)) issues.Add("Overdue");
                if (!string.IsNullOrEmpty(r.LastBackupError)) issues.Add("Last backup failed");
                return new BackupCoverageRow(r.Id, r.Name, r.Site, issues.Count == 0 ? "Covered" : string.Join("; ", issues), r.Last?.CreatedAt, r.LastBackupAttemptAt, r.LastBackupError);
            }).ToList());
            if (IsDisposed) return;
            _backupGrid.DataSource = rows;
            if (_backupGrid.Columns.Contains("Id")) _backupGrid.Columns["Id"].Visible = false;
            foreach (DataGridViewColumn column in _backupGrid.Columns)
            {
                column.HeaderText = column.Name switch { "LastBackup" => "Last backup", "LastAttempt" => "Last attempt", _ => column.Name };
                column.MinimumWidth = column.Name == "Coverage" ? 240 : column.Name == "Device" ? 200 : 100;
                column.FillWeight = column.Name == "Coverage" ? 240 : column.Name == "Device" ? 200 : 100;
                if (column.Name is "LastBackup" or "LastAttempt") column.DefaultCellStyle.Format = "dd MMM yyyy HH:mm";
            }
            _backupCoverage.Text = $"{rows.Count} devices   •   {rows.Count(r => r.Coverage == "Covered")} covered   •   {rows.Count(r => r.Coverage.Contains("Never"))} never backed up   •   {rows.Count(r => r.Coverage.Contains("No active"))} unscheduled\n{rows.Count(r => r.Coverage.Contains("Overdue"))} overdue   •   {rows.Count(r => r.Coverage.Contains("failed"))} failed   •   {rows.Count(r => r.Coverage.Contains("missing"))} missing files   |   Checked {DateTime.Now:T}";
        }
        catch (Exception ex) { if (!IsDisposed) _backupCoverage.Text = ex.Message; }
        finally { _readingCoverage = false; }
    }
    private void CompareBackups(object? sender, EventArgs e)
    {
        if (_backupGrid.CurrentRow?.DataBoundItem is not BackupCoverageRow row) { MessageBox.Show("Select a device in Backups first."); return; }
        var router = _routers.FirstOrDefault(r => r.Id == row.Id); if (router is null) return;
        var entries = router.Backups.OrderByDescending(b => b.CreatedAt).ToList();
        if (entries.Count < 2) { MessageBox.Show("This device needs two recorded exports to compare."); return; }
        using var dialog = new DpiDialog { Text = "Compare backups — " + router.Name, Width = 1150, Height = 720, StartPosition = FormStartPosition.CenterParent };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var before = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        var after = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var entry in entries) { string label = entry.CreatedAt.ToString("dd MMM yyyy HH:mm:ss"); before.Items.Add(label); after.Items.Add(label); }
        before.SelectedIndex = 1; after.SelectedIndex = 0;
        var reveal = new CheckBox { Text = "Reveal configuration / secrets", AutoSize = true };
        var summary = new Label { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8), Text = "Export timestamps are ignored. Contents are hidden by default. Large changes use a bounded replacement comparison." };
        var grid = new SmoothGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill }; StyleGrid(grid);
        List<ConfigDiff.Line> diff = [];
        void Render() { grid.DataSource = diff.Select((l, i) => new { Line = i + 1, Change = l.Kind, Content = ConfigDiff.Display(l.Text, reveal.Checked) }).ToList(); if (grid.Columns.Count > 0) { grid.Columns[0].FillWeight = 8; grid.Columns[1].FillWeight = 8; grid.Columns[2].FillWeight = 84; } }
        var compare = Button("Compare", async (_, _) => {
            bar.Enabled = false;
            try
            {
                string a = entries[before.SelectedIndex].ExportPath, b = entries[after.SelectedIndex].ExportPath;
                diff = await Task.Run(() => {
                    if (new FileInfo(a).Length > 4 * 1024 * 1024 || new FileInfo(b).Length > 4 * 1024 * 1024) throw new InvalidOperationException("Export is over 4 MB; compare it in an external editor.");
                    return ConfigDiff.Compare(File.ReadAllText(a), File.ReadAllText(b));
                });
                if (dialog.IsDisposed) return; Render(); summary.Text = $"{diff.Count(l => l.Kind == "+")} added • {diff.Count(l => l.Kind == "−")} removed • Timestamp-only changes ignored. Values hidden unless revealed.";
            }
            catch (Exception ex) { if (!dialog.IsDisposed) summary.Text = ex.Message; }
            finally { if (!dialog.IsDisposed) bar.Enabled = true; }
        });
        reveal.CheckedChanged += (_, _) => Render();
        grid.CellFormatting += (_, args) => { if (args.RowIndex < 0 || args.RowIndex >= diff.Count) return; args.CellStyle.BackColor = diff[args.RowIndex].Kind switch { "+" => Color.FromArgb(226, 245, 233), "−" => Color.FromArgb(255, 234, 234), _ => Color.White }; };
        bar.Controls.AddRange([new Label { Text = "Before", AutoSize = true }, before, new Label { Text = "After", AutoSize = true }, after, compare, reveal]);
        dialog.Controls.Add(grid); dialog.Controls.Add(summary); dialog.Controls.Add(bar); dialog.ShowDialog(this);
    }
}
