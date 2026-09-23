using System.ComponentModel;
using System.Diagnostics;

namespace MikroTikManager;

public sealed class MainForm : Form
{
    private readonly IContainer _components = new Container();
    private readonly SecureStore _store = new();
    private readonly SortableBindingList<RouterRecord> _routers;
    private readonly BindingList<UpgradeJob> _jobs;
    private readonly DataGridView _routerGrid = new();
    private readonly DataGridView _jobGrid = new();
    private readonly DataGridView _progressGrid = new();
    private readonly BindingList<MaintenanceProgressRow> _progressRows = [];
    private readonly RichTextBox _log = new();
    private readonly ToolStripStatusLabel _status = new("Ready");
    private readonly ContextMenuStrip _routerContextMenu;
    private readonly ComboBox _groupSelector = new() { Width = 145, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly DateTimePicker _scheduleTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MM-yyyy HH:mm", Width = 160 };
    private readonly TextBox _jobName = new() { Width = 210, PlaceholderText = "Maintenance job name" };
    private readonly TextBox _routerSearch = new() { Width = 180, PlaceholderText = "Search routers..." };
    private readonly ComboBox _statusFilter = new() { Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _failureBehavior = new() { Width = 135, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _retryCount = new() { Width = 52, Minimum = 0, Maximum = 5, Value = 1 };
    private CancellationTokenSource? _running;
    private bool _operationInProgress;
    private bool _suspendRouterSaves;
    private bool _contextMenuRowValid;
    private List<Guid> _lastUpgradeRouterIds = [];

    public MainForm()
    {
        _routerContextMenu = new ContextMenuStrip(_components);
        Text = "MikroTik Manager 0.2.2";
        Icon = AppIcon.Current;
        Width = 1280;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);
        AllowDrop = true;
        _routers = new SortableBindingList<RouterRecord>(_store.LoadRouters());
        _jobs = new BindingList<UpgradeJob>(_store.LoadJobs());
        BuildUi();
        DragEnter += MainFormDragEnter;
        DragDrop += MainFormDragDrop;
        FormClosing += (_, _) => SaveAll();
        Shown += async (_, _) =>
        {
            _routerGrid.ClearSelection();
            if (_store.LoadSettings().CheckForUpdatesAtStartup) await CheckForUpdatesAsync(false);
        };
    }

    private void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildRoutersPage());
        tabs.TabPages.Add(BuildMaintenancePage());
        tabs.TabPages.Add(BuildSchedulesPage());
        tabs.TabPages.Add(BuildLogsPage());
        tabs.TabPages.Add(BuildSettingsPage());
        var status = new StatusStrip();
        status.Items.Add(_status);
        Controls.Add(tabs);
        Controls.Add(status);
    }

    private TabPage BuildRoutersPage()
    {
        var page = new TabPage("Routers");
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 72,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.FromArgb(245, 247, 250),
            Padding = new Padding(4, 3, 4, 3)
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var actions = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            BackColor = Color.FromArgb(245, 247, 250),
            Padding = new Padding(3, 1, 3, 1)
        };
        var inventoryMenu = new ToolStripDropDownButton("Inventory");
        inventoryMenu.DropDownItems.Add("Import WinBox CDB / WBX...", null, ImportCdb);
        inventoryMenu.DropDownItems.Add("Add Router Manually...", null, AddRouterManually);
        inventoryMenu.DropDownItems.Add(new ToolStripSeparator());
        inventoryMenu.DropDownItems.Add("Remove Selected Routers...", null, RemoveSelectedRouters);

        var selectionMenu = new ToolStripDropDownButton("Selection");
        selectionMenu.DropDownItems.Add("Select All Visible Routers", null, (_, _) => SelectVisibleRouters());
        selectionMenu.DropDownItems.Add("Clear Selection", null, (_, _) => _routerGrid.ClearSelection());

        var groupMenu = new ToolStripDropDownButton("Groups");
        groupMenu.DropDownItems.Add("Assign Selected to Group", null, AssignSelectedGroup);
        groupMenu.DropDownItems.Add("Clear Group from Selected", null, ClearSelectedGroup);
        groupMenu.DropDownItems.Add("Select Entire Group", null, SelectRouterGroup);

        var maintenanceMenu = new ToolStripDropDownButton("Maintenance");
        maintenanceMenu.DropDownItems.Add("Check API Status", null, CheckApiStatusSelected);
        maintenanceMenu.DropDownItems.Add("Pre-Upgrade Check", null, RunPreflightSelected);
        maintenanceMenu.DropDownItems.Add("Fetch Current Versions", null, FetchCurrentVersions);
        maintenanceMenu.DropDownItems.Add(new ToolStripSeparator());
        maintenanceMenu.DropDownItems.Add("Backup Selected Routers...", null, BackupSelectedRouters);

        var upgradeMenu = new ToolStripDropDownButton("Upgrade");
        upgradeMenu.DropDownItems.Add("Upgrade Selected Routers...", null, RunNow);
        upgradeMenu.DropDownItems.Add("Resume Incomplete Queue", null, ResumeIncomplete);
        upgradeMenu.DropDownItems.Add(new ToolStripSeparator());
        upgradeMenu.DropDownItems.Add("Cancel Current Operation", null, (_, _) => _running?.Cancel());

        var updatesMenu = new ToolStripDropDownButton("Help") { Alignment = ToolStripItemAlignment.Right };
        updatesMenu.DropDownItems.Add("Check for Updates", null, async (_, _) => await CheckForUpdatesAsync(true));
        updatesMenu.DropDownItems.Add(new ToolStripSeparator());
        updatesMenu.DropDownItems.Add("About MikroTik Manager 0.2.2", null, (_, _) =>
            MessageBox.Show("MikroTik Manager 0.2.2\n\nMade for MikroTik\nIndependent open-source software by Indigo Data Services Pvt Ltd.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information));
        actions.Items.AddRange([inventoryMenu, selectionMenu, groupMenu, maintenanceMenu, upgradeMenu, updatesMenu]);

        var filters = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            BackColor = Color.White,
            Padding = new Padding(5, 2, 5, 2)
        };
        _statusFilter.Items.AddRange(["All statuses", "Online", "Failed", "Needs attention"]);
        _statusFilter.SelectedIndex = 0;
        _failureBehavior.DataSource = Enum.GetValues<FailureBehavior>();
        AppSettings saved = _store.LoadSettings();
        _failureBehavior.SelectedItem = saved.DefaultFailureBehavior;
        _retryCount.Value = Math.Clamp(saved.RetryCount, (int)_retryCount.Minimum, (int)_retryCount.Maximum);
        filters.Items.Add(new ToolStripLabel("Search"));
        filters.Items.Add(new ToolStripControlHost(_routerSearch) { AutoSize = false, Width = 190 });
        filters.Items.Add(new ToolStripLabel("Status"));
        filters.Items.Add(new ToolStripControlHost(_statusFilter) { AutoSize = false, Width = 125 });
        filters.Items.Add(new ToolStripSeparator());
        filters.Items.Add(new ToolStripLabel("Group"));
        filters.Items.Add(new ToolStripControlHost(_groupSelector) { AutoSize = false, Width = 150 });
        filters.Items.Add(new ToolStripSeparator());
        filters.Items.Add(new ToolStripLabel("On failure"));
        filters.Items.Add(new ToolStripControlHost(_failureBehavior) { AutoSize = false, Width = 135 });
        filters.Items.Add(new ToolStripLabel("Retries"));
        filters.Items.Add(new ToolStripControlHost(_retryCount) { AutoSize = false, Width = 55 });
        filters.Items.Add(new ToolStripLabel("Select rows with Ctrl or Shift") { ForeColor = Color.DimGray });

        header.Controls.Add(actions, 0, 0);
        header.Controls.Add(filters, 0, 1);

        _routerGrid.Dock = DockStyle.Fill;
        _routerGrid.AutoGenerateColumns = false;
        _routerGrid.AllowUserToAddRows = false;
        _routerGrid.AllowUserToDeleteRows = false;
        _routerGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _routerGrid.MultiSelect = true;
        _routerGrid.RowHeadersVisible = false;
        _routerGrid.AllowDrop = true;
        _routerGrid.DragEnter += MainFormDragEnter;
        _routerGrid.DragDrop += MainFormDragDrop;
        _routerGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _routerGrid.BorderStyle = BorderStyle.None;
        _routerGrid.BackgroundColor = Color.White;
        _routerGrid.GridColor = Color.FromArgb(225, 229, 235);
        _routerGrid.RowTemplate.Height = 28;
        _routerGrid.EnableHeadersVisualStyles = false;
        _routerGrid.ColumnHeadersHeight = 32;
        _routerGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 49, 78);
        _routerGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _routerGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _routerGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 252);
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.Name), "Router", 180));
        DataGridViewTextBoxColumn groupColumn = TextColumn(nameof(RouterRecord.Group), "Upgrade Group", 105);
        groupColumn.MaxInputLength = 80;
        _routerGrid.Columns.Add(groupColumn);
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.Host), "Address", 120));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.Model), "Model", 105, true));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.StorageStatus), "Storage", 125, true));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.ApiPort), "API Port", 72));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.Username), "Username", 85));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.ApiStatus), "API Status", 120, true));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.RouterOsVersion), "RouterOS", 85, true));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.FirmwareVersion), "Firmware", 85, true));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.LastStatus), "Status", 190, true));
        _routerGrid.DataSource = _routers;
        _routerGrid.CellValueChanged += (_, e) =>
        {
            SaveRouters();
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _routerGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(RouterRecord.Group))
                RefreshGroupChoices();
        };
        _routerGrid.CellMouseDown += RouterGridCellMouseDown;
        _routerSearch.TextChanged += (_, _) => ApplyRouterFilter();
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyRouterFilter();
        _groupSelector.DropDown += (_, _) => RefreshGroupChoices();
        RefreshGroupChoices();
        _routerContextMenu.Items.Add("Check API Status", null, CheckApiStatusSelected);
        _routerContextMenu.Items.Add("Pre-Upgrade Check", null, RunPreflightSelected);
        _routerContextMenu.Items.Add("Fetch Current Versions", null, FetchCurrentVersions);
        _routerContextMenu.Items.Add("Backup Router...", null, BackupSelectedRouters);
        _routerContextMenu.Items.Add(new ToolStripSeparator());
        _routerContextMenu.Items.Add("Remove Router", null, RemoveSelectedRouters);
        _routerContextMenu.Opening += (_, e) => e.Cancel = !_contextMenuRowValid || _operationInProgress;
        _routerGrid.ContextMenuStrip = _routerContextMenu;
        page.Controls.Add(_routerGrid);
        page.Controls.Add(header);
        return page;
    }

    private TabPage BuildMaintenancePage()
    {
        var page = new TabPage("Maintenance Progress");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), WrapContents = false };
        bar.Controls.Add(Button("Retry Failed", RetryFailed));
        bar.Controls.Add(Button("Open Reports Folder", OpenReportsFolder));
        bar.Controls.Add(Button("Cancel Current Job", (_, _) => _running?.Cancel()));
        bar.Controls.Add(new Label { Text = "Live progress remains interactive while maintenance is running.", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 7, 0, 0) });
        _progressGrid.Dock = DockStyle.Fill;
        _progressGrid.ReadOnly = true;
        _progressGrid.AllowUserToAddRows = false;
        _progressGrid.AllowUserToDeleteRows = false;
        _progressGrid.AutoGenerateColumns = false;
        _progressGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _progressGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _progressGrid.Columns.Add(TextColumn(nameof(MaintenanceProgressRow.Router), "Router", 150, true));
        _progressGrid.Columns.Add(TextColumn(nameof(MaintenanceProgressRow.Address), "Address", 110, true));
        _progressGrid.Columns.Add(TextColumn(nameof(MaintenanceProgressRow.Stage), "Stage", 125, true));
        _progressGrid.Columns.Add(TextColumn(nameof(MaintenanceProgressRow.Attempt), "Attempt", 55, true));
        _progressGrid.Columns.Add(TextColumn(nameof(MaintenanceProgressRow.Progress), "Progress %", 65, true));
        _progressGrid.Columns.Add(TextColumn(nameof(MaintenanceProgressRow.Result), "Result", 80, true));
        _progressGrid.Columns.Add(TextColumn(nameof(MaintenanceProgressRow.Message), "Message", 260, true));
        _progressGrid.DataSource = _progressRows;
        page.Controls.Add(_progressGrid);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildSchedulesPage()
    {
        var page = new TabPage("Schedules");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), WrapContents = false };
        _scheduleTime.Value = DateTime.Now.AddHours(1);
        bar.Controls.Add(new Label { Text = "Job:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        bar.Controls.Add(_jobName);
        bar.Controls.Add(new Label { Text = "Run at:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        bar.Controls.Add(_scheduleTime);
        bar.Controls.Add(Button("Schedule Selected Routers", CreateSchedule));
        _jobGrid.Dock = DockStyle.Fill;
        _jobGrid.ReadOnly = true;
        _jobGrid.AllowUserToAddRows = false;
        _jobGrid.AllowUserToDeleteRows = false;
        _jobGrid.AutoGenerateColumns = false;
        _jobGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _jobGrid.Columns.Add(TextColumn(nameof(UpgradeJob.Name), "Job", 180, true));
        _jobGrid.Columns.Add(TextColumn(nameof(UpgradeJob.ScheduledLocalTime), "Scheduled", 120, true));
        _jobGrid.Columns.Add(TextColumn(nameof(UpgradeJob.RouterCount), "Routers", 60, true));
        _jobGrid.Columns.Add(TextColumn(nameof(UpgradeJob.State), "State", 220, true));
        _jobGrid.Columns.Add(TextColumn(nameof(UpgradeJob.StartedAt), "Started", 120, true));
        _jobGrid.Columns.Add(TextColumn(nameof(UpgradeJob.CompletedAt), "Completed", 120, true));
        _jobGrid.DataSource = _jobs;
        page.Controls.Add(_jobGrid);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildLogsPage()
    {
        var page = new TabPage("Live Log");
        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.Font = new Font("Consolas", 9);
        page.Controls.Add(_log);
        return page;
    }

    private TabPage BuildSettingsPage()
    {
        AppSettings current = _store.LoadSettings();
        var page = new TabPage("Settings");
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(18), ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        var apiPort = Numeric(current.DefaultApiPort, 1, 65535);
        var ssl = new CheckBox { Checked = current.UseApiSsl, Text = "Use API-SSL", AutoSize = true };
        var invalid = new CheckBox { Checked = current.AllowInvalidTlsCertificate, Text = "Allow self-signed certificate", AutoSize = true };
        var connect = Numeric(current.ConnectTimeoutSeconds, 3, 120);
        var reconnect = Numeric(current.ReconnectTimeoutMinutes, 1, 120);
        var stable = Numeric(current.StableOnlineSeconds, 0, 300);
        var minimumDisk = Numeric(current.MinimumFreeDiskMb, 0, 65535);
        var retention = Numeric(current.BackupRetentionDays, 0, 3650);
        var defaultFailure = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, DataSource = Enum.GetValues<FailureBehavior>() };
        defaultFailure.SelectedItem = current.DefaultFailureBehavior;
        var retries = Numeric(current.RetryCount, 0, 5);
        var updateChecks = new CheckBox { Checked = current.CheckForUpdatesAtStartup, Text = "Check GitHub releases at startup", AutoSize = true };
        var channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        channel.Items.AddRange(["stable", "long-term", "testing", "development"]);
        channel.SelectedItem = current.UpdateChannel;
        if (channel.SelectedIndex < 0) channel.SelectedIndex = 0;
        AddRow(panel, "Default RouterOS API port", apiPort);
        AddRow(panel, "Encrypted API connection", ssl);
        AddRow(panel, "TLS certificate handling", invalid);
        AddRow(panel, "Connection timeout (seconds)", connect);
        AddRow(panel, "Reboot reconnect timeout (minutes)", reconnect);
        AddRow(panel, "Stability wait after reconnect (seconds)", stable);
        AddRow(panel, "Fallback free storage requirement (MB)", minimumDisk);
        AddRow(panel, "Backup retention (days; 0 keeps all)", retention);
        AddRow(panel, "Default failure behavior", defaultFailure);
        AddRow(panel, "Default retry count", retries);
        AddRow(panel, "Application updates", updateChecks);
        AddRow(panel, "RouterOS update channel", channel);
        AddRow(panel, "Trademark notice", new Label
        {
            Text = "MikroTik, RouterOS, RouterBOARD and WinBox are trademarks of MikroTikls SIA. This independent Indigo project is not affiliated with or endorsed by MikroTikls SIA.",
            AutoSize = true,
            MaximumSize = new Size(420, 0)
        });
        var save = Button("Save Settings", (_, _) =>
        {
            var value = new AppSettings
            {
                DefaultApiPort = (int)apiPort.Value,
                UseApiSsl = ssl.Checked,
                AllowInvalidTlsCertificate = invalid.Checked,
                ConnectTimeoutSeconds = (int)connect.Value,
                ReconnectTimeoutMinutes = (int)reconnect.Value,
                StableOnlineSeconds = (int)stable.Value,
                UpdateChannel = channel.SelectedItem?.ToString() ?? "stable",
                MinimumFreeDiskMb = (int)minimumDisk.Value,
                BackupRetentionDays = (int)retention.Value,
                DefaultFailureBehavior = (FailureBehavior)(defaultFailure.SelectedItem ?? FailureBehavior.RetryThenSkip),
                RetryCount = (int)retries.Value,
                CheckForUpdatesAtStartup = updateChecks.Checked
            };
            _store.SaveSettings(value);
            _failureBehavior.SelectedItem = value.DefaultFailureBehavior;
            _retryCount.Value = value.RetryCount;
            foreach (RouterRecord router in _routers.Where(x => x.ApiPort is 8728 or 8729)) router.ApiPort = value.DefaultApiPort;
            SaveRouters();
            MessageBox.Show("Settings saved.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        AddRow(panel, "", save);
        page.Controls.Add(panel);
        return page;
    }

    private async void ImportCdb(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        TraceImport("Import button clicked; opening internal path dialog");
        using var dialog = new ImportPathDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await ImportFileAsync(dialog.SelectedPath);
    }

    private async Task ImportFileAsync(string filePath)
    {
        if (_operationInProgress) return;
        var elapsed = Stopwatch.StartNew();
        TraceImport("Import requested", filePath);
        SetBusy(true, $"Reading {Path.GetFileName(filePath)}...");
        UseWaitCursor = true;
        try
        {
            AppSettings settings = _store.LoadSettings();
            IReadOnlyList<RouterRecord> imported = await Task.Run(() =>
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException("The selected WinBox address file was not found.", filePath);
                return WinboxAddressImporter.Parse(filePath, settings.DefaultApiPort);
            });
            TraceImport($"Parsing completed: {imported.Count} records in {elapsed.ElapsedMilliseconds} ms", filePath);
            int added = 0, updated = 0;
            _suspendRouterSaves = true;
            _routers.RaiseListChangedEvents = false;
            try
            {
                foreach (RouterRecord item in imported)
                {
                    RouterRecord? existing = _routers.FirstOrDefault(x => x.Host.Equals(item.Host, StringComparison.OrdinalIgnoreCase) && x.Username.Equals(item.Username, StringComparison.Ordinal));
                    if (existing is null) { _routers.Add(item); added++; }
                    else
                    {
                        existing.Name = item.Name;
                        existing.Password = item.Password;
                        if (string.IsNullOrEmpty(existing.BackupPassword)) existing.BackupPassword = item.BackupPassword;
                        updated++;
                    }
                }
            }
            finally
            {
                _routers.RaiseListChangedEvents = true;
                try { _routers.RefreshSort(); }
                finally { _suspendRouterSaves = false; }
            }

            TraceImport($"Bulk merge completed: {added} added, {updated} updated", filePath);
            List<RouterRecord> snapshot = _routers.ToList();
            _status.Text = $"Encrypting and saving {snapshot.Count} routers...";
            await Task.Run(() => _store.SaveRouters(snapshot));
            TraceImport($"Encrypted save completed in {elapsed.ElapsedMilliseconds} ms", filePath);
            RefreshGroupChoices();
            ApplyRouterFilter();
            string format = Path.GetExtension(filePath).Equals(".wbx", StringComparison.OrdinalIgnoreCase) ? "WBX" : "CDB";
            MessageBox.Show($"Imported {imported.Count} {format} records: {added} added and {updated} updated.\n\nSaved credentials are protected with Windows machine encryption.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            TraceImport("FAILED: " + ex, filePath);
            ShowError(ex);
        }
        finally
        {
            UseWaitCursor = false;
            SetBusy(false, "Ready");
        }
    }

    private void MainFormDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = GetDroppedWinboxFile(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
    }

    private async void MainFormDragDrop(object? sender, DragEventArgs e)
    {
        string? path = GetDroppedWinboxFile(e);
        if (path is not null) await ImportFileAsync(path);
    }

    private static string? GetDroppedWinboxFile(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1) return null;
        string extension = Path.GetExtension(files[0]);
        return extension.Equals(".cdb", StringComparison.OrdinalIgnoreCase) || extension.Equals(".wbx", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;
    }

    private void TraceImport(string message, string? path = null)
    {
        try
        {
            string suffix = path is null ? "" : $" | {path}";
            File.AppendAllText(Path.Combine(_store.LogDirectory, "import.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{suffix}{Environment.NewLine}");
        }
        catch { }
    }

    private void AddRouterManually(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        using var dialog = new ManualRouterDialog(_store.LoadSettings().DefaultApiPort);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Router is null) return;
        RouterRecord item = dialog.Router;
        RouterRecord? existing = _routers.FirstOrDefault(x =>
            x.Host.Equals(item.Host, StringComparison.OrdinalIgnoreCase) &&
            x.Username.Equals(item.Username, StringComparison.Ordinal));
        if (existing is not null && MessageBox.Show(
            $"{item.Host} with username {item.Username} already exists. Update it?",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _suspendRouterSaves = true;
        try
        {
            if (existing is null) _routers.Add(item);
            else
            {
                existing.Name = item.Name;
                existing.Group = item.Group;
                existing.ApiPort = item.ApiPort;
                existing.Password = item.Password;
                existing.BackupPassword = item.BackupPassword;
            }
            _routers.RefreshSort();
        }
        finally { _suspendRouterSaves = false; }
        SaveRouters();
        RefreshGroupChoices();
        ApplyRouterFilter();
    }

    private void AssignSelectedGroup(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        string group = _groupSelector.Text.Trim();
        if (string.IsNullOrWhiteSpace(group))
        {
            MessageBox.Show("Enter or choose an upgrade group name first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (group.Length > 80)
        {
            MessageBox.Show("The upgrade group name must be 80 characters or fewer.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (RouterRecord router in selected) router.Group = group;
        SaveRouters();
        RefreshGroupChoices();
        _routerGrid.Refresh();
        _status.Text = $"Assigned {selected.Count} router(s) to upgrade group '{group}'.";
    }

    private void ClearSelectedGroup(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        foreach (RouterRecord router in selected) router.Group = "";
        SaveRouters();
        RefreshGroupChoices();
        _routerGrid.Refresh();
        _status.Text = $"Cleared the upgrade group from {selected.Count} router(s).";
    }

    private void SelectRouterGroup(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        string group = _groupSelector.Text.Trim();
        if (string.IsNullOrWhiteSpace(group))
        {
            MessageBox.Show("Choose an upgrade group first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _routerGrid.ClearSelection();
        DataGridViewRow? first = null;
        int count = 0;
        foreach (DataGridViewRow row in _routerGrid.Rows)
        {
            if (row.DataBoundItem is not RouterRecord router ||
                !string.Equals(router.Group, group, StringComparison.OrdinalIgnoreCase)) continue;
            row.Selected = true;
            first ??= row;
            count++;
        }
        if (first is not null) _routerGrid.CurrentCell = first.Cells[0];
        _status.Text = count == 0 ? $"No routers found in group '{group}'." : $"Selected {count} router(s) in group '{group}'.";
    }

    private void RefreshGroupChoices()
    {
        string current = _groupSelector.Text;
        string[] groups = _routers.Select(router => router.Group?.Trim() ?? "")
            .Where(group => group.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _groupSelector.BeginUpdate();
        try
        {
            _groupSelector.Items.Clear();
            _groupSelector.Items.AddRange(groups);
        }
        finally { _groupSelector.EndUpdate(); }
        _groupSelector.Text = current;
    }

    private void ApplyRouterFilter()
    {
        if (_routerGrid.Rows.Count == 0) return;
        string search = _routerSearch.Text.Trim();
        string status = _statusFilter.SelectedItem?.ToString() ?? "All statuses";
        try
        {
            _routerGrid.CurrentCell = null;
            foreach (DataGridViewRow row in _routerGrid.Rows)
            {
                if (row.DataBoundItem is not RouterRecord router) continue;
                bool textMatch = search.Length == 0 || new[] { router.Name, router.Group, router.Host, router.Model, router.StorageStatus, router.Username, router.RouterOsVersion, router.FirmwareVersion }
                    .Any(value => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
                bool statusMatch = status switch
                {
                    "Online" => router.ApiStatus.Equals("Online", StringComparison.OrdinalIgnoreCase),
                    "Failed" => router.ApiStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase) || router.LastStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase),
                    "Needs attention" => !router.ApiStatus.Equals("Online", StringComparison.OrdinalIgnoreCase) || router.LastStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase),
                    _ => true
                };
                row.Visible = textMatch && statusMatch;
            }
        }
        catch (InvalidOperationException) { }
    }

    private void SelectVisibleRouters()
    {
        foreach (DataGridViewRow row in _routerGrid.Rows) row.Selected = row.Visible;
    }

    private void RouterGridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        _contextMenuRowValid = false;
        if (e.RowIndex < 0)
        {
            _routerGrid.ClearSelection();
            return;
        }

        _routerGrid.ClearSelection();
        DataGridViewRow row = _routerGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not RouterRecord) return;
        row.Selected = true;
        int columnIndex = e.ColumnIndex >= 0 ? e.ColumnIndex : 0;
        _routerGrid.CurrentCell = row.Cells[columnIndex];
        _contextMenuRowValid = !_operationInProgress;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _components.Dispose();
        base.Dispose(disposing);
    }

    private void RemoveSelectedRouters(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        string target = selected.Count == 1 ? $"'{selected[0].Name}'" : $"the {selected.Count} selected routers";
        if (MessageBox.Show($"Remove {target} from the application?\n\nThey can be restored later by importing the CDB/WBX file again.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        var ids = selected.Select(x => x.Id).ToHashSet();
        _suspendRouterSaves = true;
        try
        {
            foreach (RouterRecord router in selected) _routers.Remove(router);
            foreach (UpgradeJob job in _jobs) job.RouterIds.RemoveAll(ids.Contains);
        }
        finally { _suspendRouterSaves = false; }
        SaveAll();
        RefreshGroupChoices();
        _routerGrid.ClearSelection();
        _routerGrid.Refresh();
        _jobGrid.Refresh();
        ApplyRouterFilter();
    }

    private async void FetchCurrentVersions(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        SetBusy(true, "Fetching current RouterOS and firmware versions...");
        _running = new CancellationTokenSource();
        try
        {
            var engine = CreateEngine();
            foreach (RouterRecord router in selected)
            {
                router.LastStatus = "Fetching versions"; _routerGrid.Refresh();
                try
                {
                    RouterSnapshot result = await engine.TestAndReadAsync(router, _running.Token);
                    router.ApiStatus = "Online";
                    router.Model = result.Model;
                    router.RouterOsVersion = result.RouterOsVersion;
                    router.FirmwareVersion = result.CurrentFirmware;
                    router.LastStatus = "Versions fetched";
                }
                catch (Exception ex) { router.ApiStatus = "Failed"; router.LastStatus = "Test failed: " + ex.Message; }
                _routerGrid.Refresh(); SaveRouters();
            }
        }
        finally { SetBusy(false, "Ready"); _running.Dispose(); _running = null; }
    }

    private async void CheckApiStatusSelected(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> routers = SelectedRouters();
        if (routers.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        SetBusy(true, $"Checking API status on {routers.Count} router(s)...");
        _running = new CancellationTokenSource();
        int online = 0, failed = 0;
        try
        {
            var engine = CreateEngine();
            int completed = 0;
            foreach (RouterRecord[] batch in routers.Chunk(8))
            {
                await Task.WhenAll(batch.Select(async router =>
                {
                    router.ApiStatus = "Checking";
                    router.LastStatus = "Checking API";
                    try
                    {
                        await engine.CheckApiAsync(router, _running.Token);
                        router.ApiStatus = "Online";
                        router.LastStatus = "API online";
                        online++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        router.ApiStatus = "Failed";
                        router.LastStatus = "API failed: " + ex.Message;
                        failed++;
                    }
                    completed++;
                }));
                _status.Text = $"API check: {completed} of {routers.Count} completed";
                _routerGrid.Refresh();
                SaveRouters();
            }
            MessageBox.Show($"API check completed.\n\nOnline: {online}\nFailed: {failed}", Text,
                MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { MessageBox.Show("API check cancelled."); }
        finally { SetBusy(false, "Ready"); _running.Dispose(); _running = null; }
    }

    private async void RunPreflightSelected(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        PrepareProgressRows(selected);
        SetBusy(true, $"Running preflight on {selected.Count} router(s)...");
        _running = new CancellationTokenSource();
        int passed = 0, failed = 0;
        try
        {
            UpgradeEngine engine = CreateEngine();
            foreach (RouterRecord router in selected)
            {
                UpdateProgress(new MaintenanceProgressUpdate(router.Id, "Preflight", 1, 5, "Running", "Connecting"));
                RouterHealthCheck result = await engine.PreflightAsync(router, _running.Token);
                if (result.Passed) passed++; else failed++;
                router.LastStatus = result.Passed ? "Preflight passed: " + result.Summary : "Preflight failed: " + result.Summary;
                UpdateProgress(new MaintenanceProgressUpdate(router.Id, "Preflight", 1, 100,
                    result.Passed ? "Ready" : "Failed", result.Summary));
                _routerGrid.Refresh();
            }
            SaveRouters();
            ApplyRouterFilter();
            MessageBox.Show($"Preflight completed.\n\nReady: {passed}\nFailed: {failed}", Text,
                MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { MessageBox.Show("Preflight cancelled."); }
        finally { SetBusy(false, "Ready"); _running.Dispose(); _running = null; }
    }

    private async void BackupSelectedRouters(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        using var dialog = new BackupFolderDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true, $"Backing up {selected.Count} selected router(s) through the API...");
        _running = new CancellationTokenSource();
        try
        {
            var service = new RouterBackupService(_store.LoadSettings());
            service.Message += AppendLog;
            BulkBackupResult result = await service.BackupAllAsync(selected, dialog.SelectedPath, _running.Token);
            SaveRouters();
            _routerGrid.Refresh();
            MessageBox.Show(
                $"Bulk backup completed.\n\nSuccessful: {result.Successful}\nVerified: {result.Verified}\nFailed: {result.Failed}\n\nFolder:\n{result.Folder}\n\nThe manifest records file sizes and SHA-256 hashes. The .rsc exports and BackupManifest.csv contain sensitive information.",
                Text, MessageBoxButtons.OK, result.Failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{result.Folder}\"") { UseShellExecute = true }); } catch { }
        }
        catch (OperationCanceledException) { MessageBox.Show("Bulk backup cancelled. Completed files remain in the selected folder."); }
        catch (Exception ex) { ShowError(ex); }
        finally { SaveRouters(); _routerGrid.Refresh(); SetBusy(false, "Ready"); _running.Dispose(); _running = null; }
    }

    private async void RunNow(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select at least one router."); return; }
        string names = string.Join(Environment.NewLine, selected.Take(8).Select(x => "• " + x.Name));
        if (selected.Count > 8) names += $"\n• and {selected.Count - 8} more";
        FailureBehavior behavior = (FailureBehavior)(_failureBehavior.SelectedItem ?? FailureBehavior.RetryThenSkip);
        if (MessageBox.Show($"Run the safe upgrade on {selected.Count} router(s), strictly one at a time?\n\n{names}\n\nFailure behavior: {FriendlyBehavior(behavior)}\nRetries: {(int)_retryCount.Value}\n\nAn integrated preflight will block unsafe routers.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await RunUpgradeAsync(selected, behavior, (int)_retryCount.Value);
    }

    private async Task RunUpgradeAsync(List<RouterRecord> selected, FailureBehavior behavior, int retries)
    {
        _lastUpgradeRouterIds = selected.Select(x => x.Id).ToList();
        PrepareProgressRows(selected);
        SetBusy(true, "Upgrade running...");
        _running = new CancellationTokenSource();
        DateTime started = DateTime.Now;
        try
        {
            CancellationToken token = _running.Token;
            UpgradeEngine engine = CreateEngine();
            MaintenanceRunResult result = await Task.Run(
                () => engine.RunSequentialAsync(selected, new UpgradeRunOptions(behavior, retries), token), token);
            MaintenanceReportFiles reports = await new MaintenanceReportService(_store).WriteAsync(result, token);
            string summary = $"Maintenance completed.\n\nSuccessful: {result.Successful}\nFailed: {result.Failed}\nNot processed: {selected.Count - result.Routers.Count}\n\nReports:\n{reports.CsvPath}\n{reports.PdfPath}";
            MessageBox.Show(summary, Text, MessageBoxButtons.OK,
                result.Failed == 0 && result.Routers.Count == selected.Count ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            MaintenanceRunResult cancelled = BuildProgressResult(started, "Cancelled");
            await new MaintenanceReportService(_store).WriteAsync(cancelled, CancellationToken.None);
            MessageBox.Show("Upgrade cancelled. A report was saved for completed and attempted routers.");
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            SaveRouters(); _routerGrid.Refresh(); ApplyRouterFilter(); SetBusy(false, "Ready");
            _running.Dispose(); _running = null;
        }
    }

    private async void RetryFailed(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        HashSet<Guid> ids = _progressRows.Where(x => x.Result == "Failed").Select(x => x.RouterId).ToHashSet();
        List<RouterRecord> routers = _routers.Where(x => ids.Contains(x.Id)).ToList();
        if (routers.Count == 0) { MessageBox.Show("There are no failed routers to retry."); return; }
        await RunUpgradeAsync(routers, (FailureBehavior)(_failureBehavior.SelectedItem ?? FailureBehavior.RetryThenSkip), (int)_retryCount.Value);
    }

    private async void ResumeIncomplete(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        HashSet<Guid> completed = _progressRows.Where(x => x.Result == "Completed").Select(x => x.RouterId).ToHashSet();
        List<RouterRecord> routers = _routers.Where(x => _lastUpgradeRouterIds.Contains(x.Id) && !completed.Contains(x.Id)).ToList();
        if (routers.Count == 0) { MessageBox.Show("There is no incomplete upgrade queue to resume."); return; }
        await RunUpgradeAsync(routers, (FailureBehavior)(_failureBehavior.SelectedItem ?? FailureBehavior.RetryThenSkip), (int)_retryCount.Value);
    }

    private async void CreateSchedule(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        List<RouterRecord> selected = SelectedRouters();
        if (selected.Count == 0) { MessageBox.Show("Select the routers on the Routers tab first."); return; }
        var job = new UpgradeJob
        {
            Name = string.IsNullOrWhiteSpace(_jobName.Text) ? $"Maintenance {_scheduleTime.Value:dd-MM-yyyy HH:mm}" : _jobName.Text.Trim(),
            ScheduledLocalTime = _scheduleTime.Value,
            RouterIds = selected.Select(x => x.Id).ToList(),
            FailureBehavior = (FailureBehavior)(_failureBehavior.SelectedItem ?? FailureBehavior.RetryThenSkip),
            RetryCount = (int)_retryCount.Value
        };
        try
        {
            _jobs.Add(job); _store.SaveJobs(_jobs.ToList());
            await TaskSchedulerService.RegisterAsync(job);
            _jobGrid.Refresh();
            MessageBox.Show("Schedule created. Windows may request administrator approval so the task can run while nobody is logged in.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _jobs.Remove(job); _store.SaveJobs(_jobs.ToList()); ShowError(ex);
        }
    }

    private UpgradeEngine CreateEngine()
    {
        var engine = new UpgradeEngine(_store, _store.LoadSettings());
        engine.Message += line =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(() => { _log.AppendText(line + Environment.NewLine); _log.ScrollToCaret(); _status.Text = line; _routerGrid.Refresh(); }));
        };
        engine.Progress += update =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(() => UpdateProgress(update)));
        };
        return engine;
    }

    private void PrepareProgressRows(IEnumerable<RouterRecord> routers)
    {
        _progressRows.Clear();
        foreach (RouterRecord router in routers)
        {
            _progressRows.Add(new MaintenanceProgressRow
            {
                RouterId = router.Id,
                Router = router.Name,
                Address = router.Host,
                Stage = "Queued",
                Result = "Pending"
            });
        }
    }

    private void UpdateProgress(MaintenanceProgressUpdate update)
    {
        MaintenanceProgressRow? row = _progressRows.FirstOrDefault(x => x.RouterId == update.RouterId);
        if (row is null) return;
        row.Stage = update.Stage;
        row.Attempt = update.Attempt;
        row.Progress = update.Progress;
        row.Result = update.Result;
        row.Message = update.Message;
        _progressRows.ResetItem(_progressRows.IndexOf(row));
    }

    private MaintenanceRunResult BuildProgressResult(DateTime started, string pendingResult)
    {
        DateTime completed = DateTime.Now;
        List<RouterRunResult> rows = _progressRows.Select(row =>
        {
            RouterRecord? router = _routers.FirstOrDefault(x => x.Id == row.RouterId);
            string result = row.Result is "Completed" or "Failed" ? row.Result : pendingResult;
            return new RouterRunResult(row.RouterId, row.Router, row.Address, result, Math.Max(1, row.Attempt),
                router?.RouterOsVersion ?? "", router?.FirmwareVersion ?? "", row.Message, started, completed);
        }).ToList();
        return new MaintenanceRunResult(started, completed, rows);
    }

    private void OpenReportsFolder(object? sender, EventArgs e)
    {
        string folder = Path.Combine(_store.RootDirectory, "Reports");
        Directory.CreateDirectory(folder);
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task CheckForUpdatesAsync(bool showCurrent)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            UpdateCheckResult result = await UpdateChecker.CheckAsync(timeout.Token);
            if (result.IsNewer)
            {
                if (MessageBox.Show($"MikroTik Manager {result.Version} is available.\n\nOpen the GitHub release page?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(result.Url) { UseShellExecute = true });
            }
            else if (showCurrent)
            {
                MessageBox.Show("You are running the latest published release.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex) when (!showCurrent && ex is not OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show("Could not check GitHub releases: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static string FriendlyBehavior(FailureBehavior behavior) => behavior switch
    {
        FailureBehavior.Stop => "Stop at first failure",
        FailureBehavior.Skip => "Skip failures",
        FailureBehavior.RetryThenStop => "Retry, then stop",
        _ => "Retry, then skip"
    };

    private void AppendLog(string line)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => AppendLog(line))); return; }
        _log.AppendText(line + Environment.NewLine);
        _log.ScrollToCaret();
        _status.Text = line;
        _routerGrid.Refresh();
    }

    private List<RouterRecord> SelectedRouters() => _routerGrid.Rows.Cast<DataGridViewRow>()
        .Where(row => row.Visible && row.Selected && row.DataBoundItem is RouterRecord)
        .OrderBy(row => row.Index)
        .Select(row => (RouterRecord)row.DataBoundItem!)
        .ToList();
    private void SaveRouters() { if (!_suspendRouterSaves) _store.SaveRouters(_routers.ToList()); }
    private void SaveAll() { SaveRouters(); _store.SaveJobs(_jobs.ToList()); }
    private void SetBusy(bool busy, string text)
    {
        _operationInProgress = busy;
        _status.Text = text;

        // Keep the grid enabled so operators can scroll and select rows while
        // status updates arrive. Only editable connection fields are locked.
        foreach (DataGridViewColumn column in _routerGrid.Columns)
        {
            if (column.DataPropertyName is nameof(RouterRecord.Name)
                or nameof(RouterRecord.Group)
                or nameof(RouterRecord.Host)
                or nameof(RouterRecord.ApiPort)
                or nameof(RouterRecord.Username))
                column.ReadOnly = busy;
        }
    }
    private void ShowError(Exception ex) => MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static Button Button(string text, EventHandler click) { var b = new Button { Text = text, AutoSize = true, Height = 28 }; b.Click += click; return b; }
    private static NumericUpDown Numeric(int value, int min, int max)
    {
        // Minimum and Maximum must be assigned before Value. NumericUpDown's
        // default maximum is 100, while the default RouterOS API port is 8728.
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 120
        };
    }
    private static DataGridViewTextBoxColumn TextColumn(string property, string header, float weight, bool readOnly = false) => new() { DataPropertyName = property, HeaderText = header, FillWeight = weight, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.Automatic };
    private static void AddRow(TableLayoutPanel panel, string label, Control control) { int row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 0, 8) }, 0, row); panel.Controls.Add(control, 1, row); }
}
