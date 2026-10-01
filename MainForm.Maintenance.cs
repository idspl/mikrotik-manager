namespace MikroTikManager;

public sealed partial class MainForm
{
    private UpgradeEngine? _activeUpgrade;
    private bool _updateInProgress;
    private DateTime? _upgradeWindowEnd;
    private bool _canaryPerGroup;

    private void PauseAfterRouter(object? sender, EventArgs e)
    {
        if (_activeUpgrade is null) return;
        _activeUpgrade.PauseRequested = true;
        _status.Text = "Pause requested: finishing the current router, including retries. Use Resume Incomplete Queue afterwards.";
    }

    private async void EditCredentials(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count != 1) { MessageBox.Show("Select exactly one router to edit credentials."); return; }
        RouterRecord router = selected[0];
        using var form = new Form { Text = "Edit Credentials — " + router.Name, Width = 470, Height = 280,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, AutoSize = true };
        var username = new TextBox { Text = router.Username, Width = 260 };
        var password = new TextBox { UseSystemPasswordChar = true, Width = 260, PlaceholderText = "Leave blank to keep saved password" };
        var show = new CheckBox { Text = "Show new password", AutoSize = true };
        show.CheckedChanged += (_, _) => password.UseSystemPasswordChar = !show.Checked;
        var clear = new CheckBox { Text = "Use an empty password", AutoSize = true };
        var result = new Label { AutoSize = true, MaximumSize = new Size(400, 50) };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var test = new Button { Text = "Test API Login", AutoSize = true };
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        string CandidatePassword() => clear.Checked ? "" : password.Text.Length == 0 ? router.Password : password.Text;
        bool testing = false;
        form.FormClosing += (_, e) => { if (testing) e.Cancel = true; };
        test.Click += async (_, _) =>
        {
            testing = true; test.Enabled = save.Enabled = cancel.Enabled = false;
            result.Text = "Testing API login...";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var candidate = new RouterRecord { Host = router.Host, ApiPort = router.ApiPort,
                    Username = username.Text.Trim(), Password = CandidatePassword() };
                await new UpgradeEngine(_store, _store.LoadSettings()).CheckApiAsync(candidate, timeout.Token);
                result.Text = "API login successful. Save to apply these credentials.";
            }
            catch { result.Text = "API login failed or timed out. Check credentials, API port and connectivity."; }
            finally { testing = false; test.Enabled = save.Enabled = cancel.Enabled = true; }
        };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(username.Text)) { result.Text = "Username is required."; return; }
            form.DialogResult = DialogResult.OK;
        };
        AddRow(layout, "Username", username); AddRow(layout, "New password", password);
        AddRow(layout, "", show); AddRow(layout, "", clear); AddRow(layout, "", result);
        buttons.Controls.AddRange([test, save, cancel]); AddRow(layout, "", buttons);
        form.Controls.Add(layout); form.CancelButton = cancel;
        if (form.ShowDialog(this) != DialogResult.OK) return;
        string oldUsername = router.Username, oldPassword = router.Password;
        router.Username = username.Text.Trim(); router.Password = CandidatePassword();
        try { SaveRouters(); router.ApiStatus = "Not checked"; router.LastStatus = "Credentials updated"; _routerGrid.Refresh(); }
        catch (Exception ex) { router.Username = oldUsername; router.Password = oldPassword; ShowError(ex); }
        await Task.CompletedTask;
    }

    private void ShowHistory(object? sender, EventArgs e)
    {
        using var form = new Form { Text = "Maintenance History — stages and results", Width = 1150, Height = 600, StartPosition = FormStartPosition.CenterParent };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
        grid.DataSource = MaintenanceHistory.Load(_store).SelectMany(run => run.Stages.Select(stage => new
        { Run = run.Started, RunState = run.State, stage.Time, stage.Router, stage.Stage, stage.Attempt, stage.Result,
            stage.Message, stage.RouterOS, stage.Firmware })).ToList();
        form.Controls.Add(grid); form.ShowDialog(this);
    }

    private void RestoreIncompleteQueue()
    {
        MaintenanceHistory? run = MaintenanceHistory.Load(_store).FirstOrDefault();
        if (run is null) return;
        _lastUpgradeRouterIds = run.RouterIds;
        _failureBehavior.SelectedItem = run.FailureBehavior;
        _retryCount.Value = Math.Clamp(run.RetryCount, 0, 5);
        PrepareProgressRows(_routers.Where(x => run.RouterIds.Contains(x.Id)));
        foreach (var row in _progressRows)
        {
            HistoryStage? stage = run.Stages.LastOrDefault(x => x.RouterId == row.RouterId);
            row.Result = run.CompletedIds.Contains(row.RouterId) ? "Completed" : "Pending";
            row.Stage = stage?.Stage ?? "Queued";
            row.Message = stage?.Message ?? "Not started";
            row.Attempt = stage?.Attempt ?? 0;
        }
        _progressRows.ResetBindings();
        if (run.RouterIds.Except(run.CompletedIds).Any())
            _status.Text = "Incomplete maintenance found. Review History, then Resume Incomplete Queue to re-check before upgrading.";
    }

    private async Task<bool> PreviewUpgradeAsync(List<RouterRecord> selected, FailureBehavior behavior, int retries)
    {
        SetBusy(true, "Checking upgrade readiness for preview...");
        _running = new CancellationTokenSource();
        try
        {
            var rows = new System.Data.DataTable();
            foreach (string column in new[] { "Order", "Router", "Group", "Address", "RouterOS", "Firmware", "Available firmware", "Ready", "Storage", "Detail" })
                rows.Columns.Add(column);
            var engine = new UpgradeEngine(_store, _store.LoadSettings());
            int order = 0;
            foreach (RouterRecord router in selected)
            {
                RouterHealthCheck health = await engine.PreflightAsync(router, _running.Token);
                rows.Rows.Add(++order, router.Name, router.Group, router.Host,
                    health.Snapshot?.RouterOsVersion ?? "Unknown", health.Snapshot?.CurrentFirmware ?? "Unknown",
                    health.Snapshot?.UpgradeFirmware ?? "Not applicable", health.Passed, router.StorageStatus, health.Summary);
            }
            using var dialog = new Form { Text = "Upgrade Preview — confirm exact queue order", Width = 1100, Height = 550, StartPosition = FormStartPosition.CenterParent };
            var info = new Label { Dock = DockStyle.Top, Height = 100, Padding = new Padding(10), Text =
                $"Channel: {_store.LoadSettings().UpdateChannel} | Failure policy: {FriendlyBehavior(behavior)} | Retries: {retries}\n" +
                "RouterOS target is resolved by the router at execution time; it is not pinned by this preview.\n" +
                "Safety backups are created ON EACH ROUTER (preupgrade-*). Use Backup Selected first for local copies.\n" +
                "Routers run in the order shown, not automatically sorted by group. Failed readiness checks block that router at execution." };
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, DataSource = rows };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft };
            var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, AutoScroll = true };
            var canary = new CheckBox { Text = "Test first router in each group; approve before continuing", AutoSize = true };
            var window = new CheckBox { Text = "Stop starting routers after", AutoSize = true };
            var end = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy HH:mm", Width = 175, Value = DateTime.Now.AddHours(2) };
            controls.Controls.AddRange([canary, window, end]);
            actions.Controls.Add(new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true });
            actions.Controls.Add(new Button { Text = "Start Upgrade", DialogResult = DialogResult.OK, AutoSize = true });
            dialog.Controls.Add(grid); dialog.Controls.Add(info); dialog.Controls.Add(controls); dialog.Controls.Add(actions);
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            if (window.Checked && end.Value <= DateTime.Now) { MessageBox.Show("The maintenance cutoff must be in the future."); return false; }
            _canaryPerGroup = canary.Checked; _upgradeWindowEnd = window.Checked ? end.Value : null;
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { ShowError(ex); return false; }
        finally { _running.Dispose(); _running = null; SetBusy(false, "Ready"); _routerGrid.Refresh(); }
    }
}
