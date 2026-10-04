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
        var versionText = new Label { Dock = DockStyle.Fill, Padding = new Padding(24), Text = $"Saved RouterOS: {router.RouterOsVersion}\n\nSaved firmware: {router.FirmwareVersion}\n\nUpgrade channel: {RouterChannels.Resolve(router, _store.LoadSettings())}\n\nLast version check: {router.LastCheckedAt:g}", Font = new Font("Segoe UI", 12) }; versions.Controls.Add(versionText);
        var history = new SmoothGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, DataSource = router.Backups.OrderByDescending(b => b.CreatedAt).ToList(), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells }; StyleGrid(history); backups.Controls.Add(history);
        var ports = new SmoothGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill }; StyleGrid(ports);
        var sample = new TextBox { Dock = DockStyle.Bottom, Height = 140, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = "Select a port, then Read Traffic / Optics. Missing sensor readings are shown as unavailable." };
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
                versionText.Text = string.Join("\n\n", data.fields.Where(f => f[0] is "version" or "current-firmware" or "upgrade-firmware").Select(f => f[0] + ": " + f[1])) + "\n\nUpgrade channel: " + RouterChannels.Resolve(router, _store.LoadSettings());
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
                    var output = new List<string>();
                    foreach (var r in await api.ExecuteAsync("/interface/monitor-traffic", cancellation.Token, "interface=" + port.Name, "once="))
                        if (r.Type == "!re") foreach (string key in new[] { "rx-bits-per-second", "tx-bits-per-second" }) output.Add(key + ": " + (r.Attributes.GetValueOrDefault(key) ?? "Unavailable"));
                    if (port.Type == "ether")
                        try { foreach (var r in await api.ExecuteAsync("/interface/ethernet/monitor", cancellation.Token, "numbers=" + port.Name, "once=")) if (r.Type == "!re") foreach (string key in new[] { "status", "rate", "full-duplex", "sfp-rx-power", "sfp-tx-power", "sfp-temperature", "sfp-supply-voltage" }) output.Add(key + ": " + (r.Attributes.GetValueOrDefault(key) ?? "Unavailable")); } catch (RouterOsApiException) { output.Add("Ethernet optics unavailable / permission denied"); }
                    return string.Join(Environment.NewLine, output);
                }, cancellation.Token);
                if (!dialog.IsDisposed) sample.Text = port.Name + " • " + DateTime.Now.ToString("T") + Environment.NewLine + values;
            }
            catch (Exception ex) { if (!dialog.IsDisposed) sample.Text = ex.Message; }
            finally { busy = false; if (!dialog.IsDisposed) refresh.Enabled = readPort.Enabled = true; }
        };
        dialog.FormClosing += (_, _) => cancellation.Cancel();
        dialog.Shown += async (_, _) => await RefreshAsync();
        dialog.ShowDialog(this);
    }
    private sealed record PortDetail(string Name, string Type, string Link, string State, string RxBytes, string TxBytes, string RxErrors, string TxErrors, string QueueDrops, string Comment);
}
