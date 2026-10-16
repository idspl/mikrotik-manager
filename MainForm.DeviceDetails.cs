namespace MikroTikManager;

public sealed partial class MainForm
{
    private void ShowDeviceDetails(object? sender, EventArgs e)
    {
        var selected = SelectedRouters();
        if (selected.Count != 1) { MessageBox.Show("Select one device to open its details."); return; }
        ShowDeviceDetails(selected[0]);
    }

    private void ShowDeviceDetails(RouterRecord router)
    {
        using var dialog = new DpiDialog { Text = router.Name + " — Device Details", Width = 1060, Height = 740, MinimumSize = new Size(760, 520), StartPosition = FormStartPosition.CenterParent, Font = Font };
        using var cancellation = new CancellationTokenSource();
        var tabs = new TabControl { Dock = DockStyle.Fill };
        TabPage overview = new TabPage("Overview"), interfaces = new TabPage("Interfaces"), versions = new TabPage("Versions"), backups = new TabPage("Backup History");
        tabs.TabPages.AddRange([overview, interfaces, versions, backups]);
        var metrics = new SmoothGrid { Dock = DockStyle.Fill }; ConfigureDashboardGrid(metrics, ["Property", "Value"]); overview.Controls.Add(metrics);
        var versionGrid = new SmoothGrid { Dock = DockStyle.Fill }; ConfigureDashboardGrid(versionGrid, ["Property", "Value"]); versions.Controls.Add(versionGrid);
        ReplaceItems(versionGrid, [["RouterOS", router.RouterOsVersion], ["current-firmware", router.FirmwareVersion], ["upgrade-firmware", router.AvailableFirmware], ["Last version check", $"{router.LastCheckedAt:g}"]]);
        void ColorMetrics(DataGridView grid, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            string key = grid.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "";
            if (key.Contains("temperature", StringComparison.OrdinalIgnoreCase)) ReadingColors.Apply(grid, e, ReadingColors.Temperature(e.Value?.ToString() ?? ""));
            if (key is "current-firmware" or "version" or "RouterOS")
            {
                ReadingColors.Apply(grid, e, PublishedVersionStatus(router, e.Value?.ToString() ?? ""));
            }
        }
        metrics.CellFormatting += (_, e) => ColorMetrics(metrics, e);
        versionGrid.CellFormatting += (_, e) => ColorMetrics(versionGrid, e);
        var history = new SmoothGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, DataSource = router.Backups.OrderByDescending(b => b.CreatedAt).ToList(), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells }; StyleGrid(history); backups.Controls.Add(history);
        var ports = new SmoothGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill }; StyleGrid(ports);
        var sample = new SmoothGrid { Dock = DockStyle.Bottom, Height = 220 }; ConfigureDashboardGrid(sample, ["Reading", "Value"]);
        ReplaceItems(sample, [["Traffic / optics", "Select a port and read its sensors"]]);
        sample.CellFormatting += (_, e) => {
            ColorMetrics(sample, e);
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            string key = sample.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "";
            if (key is not ("sfp-rx-power" or "sfp-tx-power")) return;
            string flag = key == "sfp-rx-power" ? "sfp-rx-loss" : "sfp-tx-fault";
            string? fault = sample.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Cells[0].Value?.ToString() == flag)?.Cells[1].Value?.ToString();
            ReadingColors.Apply(sample, e, ReadingColors.Fiber(e.Value?.ToString() ?? "", fault));
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        var status = new Label { AutoSize = true, Padding = new Padding(10, 8, 0, 0), Text = "Not checked" };
        var refresh = new Button { Text = "Refresh Device", AutoSize = true };
        var readPort = new Button { Text = "Read Selected Port Traffic / Optics", Dock = DockStyle.Bottom, Height = 36 };
        interfaces.Controls.Add(ports); interfaces.Controls.Add(sample); interfaces.Controls.Add(readPort);
        bar.Controls.Add(refresh); bar.Controls.Add(status); dialog.Controls.Add(tabs); dialog.Controls.Add(bar);
        bool busy = false;
        async Task RefreshAsync()
        {
            if (busy) return; busy = true; refresh.Enabled = readPort.Enabled = false; status.Text = "Reading RouterOS API…";
            DateTime checkedAt = DateTime.Now;
            try
            {
                var data = await Task.Run(async () => {
                    await using var api = await RouterOsApiClient.ConnectAsync(router, _store.LoadSettings(), cancellation.Token);
                    async Task<List<IReadOnlyDictionary<string, string>>> Read(string command) => (await api.ExecuteAsync(command, cancellation.Token)).Where(r => r.Type == "!re").Select(r => r.Attributes).ToList();
                    var resource = (await Read("/system/resource/print")).FirstOrDefault() ?? new Dictionary<string, string>();
                    var fields = new List<string[]> { new[] { "Device", router.Name }, new[] { "Address", router.Host }, new[] { "Site", router.Site }, new[] { "Tags", router.Tags } };
                    foreach (string key in new[] { "board-name", "architecture-name", "cpu-load", "free-memory", "total-memory", "free-hdd-space", "total-hdd-space", "uptime", "version" }) fields.Add([key, resource.GetValueOrDefault(key) ?? "Unavailable"]);
                    try { foreach (var b in await Read("/system/routerboard/print")) foreach (string key in new[] { "model", "serial-number", "current-firmware", "upgrade-firmware" }) fields.Add([key, b.GetValueOrDefault(key) ?? "Unavailable"]); } catch (RouterOsApiException) { fields.Add(["RouterBOARD", "Unavailable / permission denied"]); }
                    try { foreach (var h in await Read("/system/health/print")) { if (h.TryGetValue("name", out var name)) fields.Add([name, h.GetValueOrDefault("value") + " " + h.GetValueOrDefault("type")]); else foreach (var pair in h.Where(p => p.Key != ".id")) fields.Add([pair.Key, pair.Value]); } } catch (RouterOsApiException) { fields.Add(["Health sensors", "Unavailable / permission denied"]); }
                    var list = (await api.ExecuteAsync("/interface/print", cancellation.Token, "stats=")).Where(r => r.Type == "!re").Select(r => r.Attributes).Select(p => new PortDetail(p.GetValueOrDefault("name") ?? "", p.GetValueOrDefault("type") ?? "", p.GetValueOrDefault("running") == "true" ? "Up" : "Down", p.GetValueOrDefault("disabled") == "true" ? "Disabled" : "Enabled", p.GetValueOrDefault("rx-byte") ?? "—", p.GetValueOrDefault("tx-byte") ?? "—", p.GetValueOrDefault("rx-error") ?? "Unavailable", p.GetValueOrDefault("tx-error") ?? "Unavailable", p.GetValueOrDefault("tx-queue-drop") ?? "Unavailable", p.GetValueOrDefault("comment") ?? "")).ToList();
                    return (fields, list);
                }, cancellation.Token);
                if (dialog.IsDisposed) return;
                if (!_operationInProgress) { router.LastSeenAt = checkedAt; SaveRouters(); }
                data.fields.Add(["Last checked", checkedAt.ToString("g")]); data.fields.Add(["Last successfully seen", checkedAt.ToString("g")]);
                ReplaceItems(metrics, data.fields.ToArray()); ports.DataSource = data.list;
                ReplaceItems(versionGrid, data.fields.Where(f => f[0] is "version" or "current-firmware" or "upgrade-firmware").Concat(new[] { new[] { "Upgrade channel", RouterChannels.Resolve(router, _store.LoadSettings()) }, new[] { "Checked", checkedAt.ToString("g") } }).ToArray());
                status.Text = $"Snapshot at {checkedAt:T} • CPU %; memory/storage in bytes";
            }
            catch (Exception ex) { if (!dialog.IsDisposed) status.Text = $"Check failed at {checkedAt:T}; last seen {router.LastSeenAt:g}: {ex.Message}"; }
            finally { busy = false; if (!dialog.IsDisposed) refresh.Enabled = readPort.Enabled = true; }
        }
        refresh.Click += async (_, _) => await RefreshAsync();
        readPort.Click += async (_, _) => {
            if (busy || ports.CurrentRow?.DataBoundItem is not PortDetail port) return;
            busy = true; refresh.Enabled = readPort.Enabled = false;
            try
            {
                var values = await Task.Run(async () => {
                    await using var api = await RouterOsApiClient.ConnectAsync(router, _store.LoadSettings(), cancellation.Token);
                    var output = new List<string[]>();
                    foreach (var r in await api.ExecuteAsync("/interface/monitor-traffic", cancellation.Token, "interface=" + port.Name, "once="))
                        if (r.Type == "!re") foreach (string key in new[] { "rx-bits-per-second", "tx-bits-per-second" }) output.Add([key, r.Attributes.GetValueOrDefault(key) ?? "Unavailable"]);
                    if (port.Type == "ether")
                        try { foreach (var r in await api.ExecuteAsync("/interface/ethernet/monitor", cancellation.Token, "numbers=" + port.Name, "once=")) if (r.Type == "!re") foreach (string key in new[] { "status", "rate", "full-duplex", "sfp-rx-power", "sfp-tx-power", "sfp-temperature", "sfp-supply-voltage", "sfp-rx-loss", "sfp-tx-fault" }) output.Add([key, r.Attributes.GetValueOrDefault(key) ?? "Unavailable"]); } catch (RouterOsApiException) { output.Add(["Optics", "Unavailable / permission denied"]); }
                    return output.ToArray();
                }, cancellation.Token);
                if (!dialog.IsDisposed) ReplaceItems(sample, new[] { new[] { "Port / checked", port.Name + " • " + DateTime.Now.ToString("T") } }.Concat(values).ToArray());
            }
            catch (Exception ex) { if (!dialog.IsDisposed) ReplaceItems(sample, [["Read failed", ex.Message]]); }
            finally { busy = false; if (!dialog.IsDisposed) refresh.Enabled = readPort.Enabled = true; }
        };
        dialog.FormClosing += (_, _) => cancellation.Cancel();
        dialog.Shown += async (_, _) => await RefreshAsync();
        dialog.ShowDialog(this);
    }
    private sealed record PortDetail(string Name, string Type, string Link, string State, string RxBytes, string TxBytes, string RxErrors, string TxErrors, string QueueDrops, string Comment);
}
