namespace MikroTikManager;

public sealed partial class MainForm
{
    private TabPage _dashboardPage = null!, _routerPage = null!, _schedulesPage = null!;
    private readonly Dictionary<string, Label> _dashboardCounts = [];
    private readonly Label _dashboardNote = new() { AutoSize = true, MaximumSize = new Size(1030, 0), ForeColor = Color.DimGray, Margin = new Padding(8, 10, 8, 16) };
    private readonly ListView _dashboardJobs = new() { View = View.Details, FullRowSelect = true, Height = 210, Width = 1060 };
    private readonly ListView _dashboardGroups = new() { View = View.Details, FullRowSelect = true, Height = 185, Width = 1060 };

    private TabPage BuildDashboardPage()
    {
        var page = new TabPage("Dashboard") { BackColor = Color.FromArgb(245, 247, 251) };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(20) };
        layout.Controls.Add(new Label { Text = "Inventory overview", AutoSize = true, Font = new Font("Segoe UI", 21, FontStyle.Bold), ForeColor = Color.FromArgb(28, 49, 78) });
        layout.Controls.Add(_dashboardNote);
        var cards = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 1080, MaximumSize = new Size(1080, 0) };
        foreach (string title in new[] { "Routers", "API online", "API failed", "Not checked", "Updates available", "Backup >7d / never" })
        {
            var card = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 2, MinimumSize = new Size(166, 0), BackColor = Color.White,
                Margin = new Padding(0, 0, 12, 12), Padding = new Padding(14) };
            var count = new Label { Text = "—", AutoSize = true, Margin = new Padding(0, 0, 0, 6), Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Color.FromArgb(28, 76, 128) };
            card.Controls.Add(count, 0, 0);
            card.Controls.Add(new Label { Text = title, AutoSize = true, Margin = Padding.Empty, ForeColor = Color.FromArgb(65, 75, 90) }, 0, 1);
            cards.Controls.Add(card); _dashboardCounts[title] = count;
        }
        layout.Controls.Add(cards);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 14) };
        actions.Controls.Add(Button("Open Inventory", (_, _) => _tabs.SelectedTab = _routerPage));
        actions.Controls.Add(Button("Refresh All Router Versions", async (_, _) => { if (!_operationInProgress && !_updateInProgress) await FetchVersionsAsync(_routers.ToList()); RefreshDashboard(); }));
        actions.Controls.Add(Button("Manage Schedules", (_, _) => _tabs.SelectedTab = _schedulesPage));
        layout.Controls.Add(actions);
        layout.Controls.Add(new Label { Text = "Schedules — double-click to manage", AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) });
        _dashboardJobs.Columns.Add("Job", 230); _dashboardJobs.Columns.Add("Type / repeat", 135);
        _dashboardJobs.Columns.Add("Next / planned run", 155); _dashboardJobs.Columns.Add("State", 160); _dashboardJobs.Columns.Add("Last result", 350);
        _dashboardJobs.DoubleClick += (_, _) => _tabs.SelectedTab = _schedulesPage;
        layout.Controls.Add(_dashboardJobs);
        layout.Controls.Add(new Label { Text = "Upgrade groups", AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = new Padding(3, 16, 3, 4) });
        _dashboardGroups.Columns.Add("Group", 330); _dashboardGroups.Columns.Add("Routers", 120);
        _dashboardGroups.Columns.Add("API online", 140); _dashboardGroups.Columns.Add("Backup >7d / never", 210);
        layout.Controls.Add(_dashboardGroups);
        var notice = new Label { Text = "Made for MikroTik • Independent software by Indigo Data Services. MikroTik trademarks belong to MikroTikls SIA.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 16, 3, 16) };
        layout.Controls.Add(notice);
        page.Controls.Add(layout);
        page.Resize += (_, _) => {
            int width = Math.Max(240, layout.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);
            _dashboardNote.MaximumSize = new Size(width - 16, 0);
            notice.MaximumSize = new Size(width - 16, 0);
            cards.MaximumSize = new Size(width, 0); cards.Width = width;
            actions.MaximumSize = new Size(width, 0); actions.Width = width;
            _dashboardJobs.Width = width; _dashboardGroups.Width = width;
        };
        return page;
    }

    private void RefreshDashboard()
    {
        if (_dashboardCounts.Count == 0 || IsDisposed) return;
        var settings = _store.LoadSettings();
        DateTime old = DateTime.Now.AddDays(-7);
        bool NeedsBackup(RouterRecord r) => r.LastBackupAt is null || r.LastBackupAt < old;
        _dashboardCounts["Routers"].Text = _routers.Count.ToString();
        _dashboardCounts["API online"].Text = _routers.Count(r => r.ApiStatus == "Online").ToString();
        _dashboardCounts["API failed"].Text = _routers.Count(r => r.ApiStatus == "Failed").ToString();
        _dashboardCounts["Not checked"].Text = _routers.Count(r => r.ApiStatus != "Online" && r.ApiStatus != "Failed").ToString();
        _dashboardCounts["Backup >7d / never"].Text = _routers.Count(NeedsBackup).ToString();
        _dashboardCounts["Updates available"].Text = _publishedReleases is null ? "—" : _routers.Count(r => {
            string channel = string.IsNullOrWhiteSpace(r.UpdateChannel) ? settings.UpdateChannel : r.UpdateChannel;
            string? latest = channel switch { "stable" => _publishedReleases.Stable, "long-term" => _publishedReleases.LongTerm, "testing" => _publishedReleases.Testing, _ => null };
            return latest is not null && ReleaseVersion.IsNewerSameMajor(latest, r.RouterOsVersion);
        }).ToString();
        _dashboardNote.Text = "Last-known observations; refresh to verify live status. Update count compares the same RouterOS major version by router channel (excludes development).";
        ReplaceItems(_dashboardJobs, _jobs.OrderBy(j => j.State == "Scheduled" ? 0 : 1).ThenBy(j => j.ScheduledLocalTime)
            .Select(j => new[] { j.Name, j.Kind + " / " + j.Recurrence, j.ScheduledLocalTime.ToString("dd MMM yyyy HH:mm"),
                j.State == "Scheduled" && j.ScheduledLocalTime <= DateTime.Now ? "Due / awaiting runner" : j.State, j.LastRunResult }).ToArray());
        ReplaceItems(_dashboardGroups, _routers.GroupBy(r => string.IsNullOrWhiteSpace(r.Group) ? "Ungrouped" : r.Group)
            .OrderBy(g => g.Key).Select(g => new[] { g.Key, g.Count().ToString(), g.Count(r => r.ApiStatus == "Online").ToString(), g.Count(NeedsBackup).ToString() }).ToArray());
    }

    private static void ReplaceItems(ListView view, string[][] rows)
    {
        if (view.Items.Count == rows.Length && rows.Select((r, i) => r.SequenceEqual(view.Items[i].SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text))).All(x => x)) return;
        view.BeginUpdate();
        try { view.Items.Clear(); view.Items.AddRange(rows.Select(r => new ListViewItem(r)).ToArray()); }
        finally { view.EndUpdate(); }
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.RowHeadersVisible = false; grid.BorderStyle = BorderStyle.None; grid.BackgroundColor = Color.White;
        grid.GridColor = Color.FromArgb(225, 229, 235); grid.RowTemplate.Height = 30;
        grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersHeight = 36; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 49, 78);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 252);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 233, 250);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 40, 65);
        grid.CellFormatting += (_, e) => {
            if (e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].DataPropertyName is not ("ApiStatus" or "Result" or "State")) return;
            string state = e.Value?.ToString() ?? "";
            if (state is "Online" or "Completed") { e.CellStyle.ForeColor = Color.FromArgb(24, 112, 73); e.CellStyle.BackColor = Color.FromArgb(231, 247, 238); }
            else if (state.Contains("Failed", StringComparison.OrdinalIgnoreCase)) { e.CellStyle.ForeColor = Color.FromArgb(160, 36, 36); e.CellStyle.BackColor = Color.FromArgb(255, 235, 235); }
            else if (state is "Running" or "Retrying") { e.CellStyle.ForeColor = Color.FromArgb(32, 83, 148); e.CellStyle.BackColor = Color.FromArgb(229, 239, 255); }
        };
        foreach (DataGridViewColumn column in grid.Columns)
            if (column.DataPropertyName.EndsWith("At") || column.DataPropertyName == nameof(UpgradeJob.ScheduledLocalTime)) column.DefaultCellStyle.Format = "dd MMM yyyy HH:mm";
    }
}
