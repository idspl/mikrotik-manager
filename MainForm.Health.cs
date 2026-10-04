namespace MikroTikManager;

public sealed partial class MainForm
{
    private readonly SmoothGrid _healthGrid = new();
    private readonly Label _healthSummary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Text = "Refresh to collect a resource snapshot. Saved readings are stale until checked." };
    private bool _healthRefreshing;
    private DateTime _lastHealthPoll = DateTime.MinValue;
    private TabPage _healthPage = null!;
    private sealed record HealthRow(Guid Id, string Device, string Status, string CPU, string MemoryUsed, string FreeStorage, string Uptime, string Temperature,
        DateTime? LastCheck, DateTime? ReadingAt, string Detail);

    private TabPage BuildHealthPage()
    {
        var page = new TabPage("Health"); _healthPage = page;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        actions.Controls.Add(Button("Refresh All Health", async (_, _) => await RefreshHealthAsync(_routers.ToList())));
        actions.Controls.Add(Button("Refresh Selected", async (_, _) => {
            var ids = _healthGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<HealthRow>().Select(r => r.Id).ToHashSet();
            await RefreshHealthAsync(_routers.Where(r => ids.Contains(r.Id)).ToList());
        }));
        actions.Controls.Add(Button("Device / Interfaces", (_, _) => {
            if (_healthGrid.CurrentRow?.DataBoundItem is HealthRow row && _routers.FirstOrDefault(r => r.Id == row.Id) is RouterRecord router) ShowDeviceDetails(router);
        }));
        actions.Controls.Add(Button("Cancel Health Refresh", (_, _) => { if (_healthRefreshing) _running?.Cancel(); }));
        actions.Controls.Add(Button("Monitoring Settings", (_, _) => _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "Settings")));
        _healthGrid.Dock = DockStyle.Fill; _healthGrid.ReadOnly = true; _healthGrid.AllowUserToAddRows = false;
        _healthGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _healthGrid.MultiSelect = true;
        _healthGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; StyleGrid(_healthGrid);
        page.Controls.Add(_healthGrid); page.Controls.Add(_healthSummary); page.Controls.Add(actions);
        page.SizeChanged += (_, _) => _healthSummary.MaximumSize = new Size(Math.Max(100, page.ClientSize.Width), 0);
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedTab == page) RefreshHealthView(); };
        return page;
    }
    private void StartHealthMonitor()
    {
        var timer = new System.Windows.Forms.Timer(_components) { Interval = 15000 };
        timer.Tick += async (_, _) =>
        {
            if (_tabs.SelectedTab == _healthPage) RefreshHealthView();
            int interval = _store.LoadSettings().HealthPollMinutes;
            if (interval <= 0 || DateTime.Now - _lastHealthPoll < TimeSpan.FromMinutes(interval)
                || _operationInProgress || _updateInProgress || !IsWindowEnabled(Handle)
                || _jobs.Any(j => j.State == "Scheduled" && j.ScheduledLocalTime <= DateTime.Now)) return;
            await RefreshHealthAsync(_routers.ToList());
        };
        timer.Start();
    }
    private async Task RefreshHealthAsync(List<RouterRecord> routers)
    {
        if (_operationInProgress || _updateInProgress || routers.Count == 0) return;
        _healthRefreshing = true; SetBusy(true, "Reading device health..."); _running = new CancellationTokenSource();
        int completed = 0;
        try
        {
            await HealthMonitor.PollAsync(routers, _store.LoadSettings(), () => {
                _status.Text = $"Health: {++completed} / {routers.Count} checked"; RequestInventoryPaint();
            }, _running.Token);
        }
        catch (OperationCanceledException) { _status.Text = "Health refresh cancelled"; }
        catch (Exception ex) { AppendLog("Health refresh: " + ex.Message); }
        finally
        {
            _lastHealthPoll = DateTime.Now;
            try { SaveRouters(); } catch (Exception ex) { ShowError(ex); }
            RefreshHealthView(); RefreshDashboard(); RequestInventoryPaint();
            _running.Dispose(); _running = null; _healthRefreshing = false; SetBusy(false, $"Health refresh finished: {completed} / {routers.Count}");
        }
    }
    private void RefreshHealthView()
    {
        if (IsDisposed) return;
        int interval = _store.LoadSettings().HealthPollMinutes;
        var rows = _routers.Select(r => {
            var h = r.Health;
            string memory = h?.TotalMemory is > 0 && h.FreeMemory is not null ? $"{100d * (h.TotalMemory - h.FreeMemory) / h.TotalMemory:0.0}%" : "Unavailable";
            return new HealthRow(r.Id, r.Name, HealthMonitor.Status(r, DateTime.Now, interval), h?.CpuPercent is double cpu ? $"{cpu:0.#}%" : "Unavailable",
                memory, HealthMonitor.Size(h?.FreeStorage), h?.Uptime ?? "Unavailable", h?.Temperature ?? "Unavailable", r.LastHealthCheckAt, h?.CheckedAt, r.LastHealthError);
        }).ToList();
        _healthSummary.Text = $"{rows.Count(r => r.Status == "Online")} online • {rows.Count(r => r.Status == "Offline")} offline • {rows.Count(r => r.Status == "Stale")} stale • {rows.Count(r => r.Status == "Not checked")} not checked\n" +
            (interval == 0 ? "Automatic polling off" : $"Checks every {interval} minutes while the app is open; deferred during maintenance") + ". Reading time identifies retained values after a failed check.";
        if (_healthGrid.DataSource is List<HealthRow> old && old.SequenceEqual(rows)) return;
        var selected = _healthGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<HealthRow>().Select(r => r.Id).ToHashSet();
        int first = _healthGrid.FirstDisplayedScrollingRowIndex;
        _healthGrid.DataSource = rows; _healthGrid.Columns["Id"].Visible = false;
        foreach (DataGridViewColumn column in _healthGrid.Columns)
        {
            column.HeaderText = column.Name switch { "MemoryUsed" => "Memory used", "FreeStorage" => "Free storage", "LastCheck" => "Last check", "ReadingAt" => "Reading time", _ => column.Name };
            column.MinimumWidth = column.Name is "Device" or "Temperature" or "Detail" ? 170 : 100;
            if (column.Name is "LastCheck" or "ReadingAt") { column.DefaultCellStyle.Format = "dd MMM HH:mm:ss"; column.MinimumWidth = 140; }
        }
        _healthGrid.ClearSelection(); foreach (DataGridViewRow row in _healthGrid.Rows) if (row.DataBoundItem is HealthRow item) row.Selected = selected.Contains(item.Id);
        if (first >= 0 && rows.Count > 0) _healthGrid.FirstDisplayedScrollingRowIndex = Math.Min(first, rows.Count - 1);
    }
}
