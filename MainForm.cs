using System.ComponentModel;
using System.Diagnostics;

namespace IndigoRouterScheduler;

public sealed class MainForm : Form
{
    private readonly IContainer _components = new Container();
    private readonly SecureStore _store = new();
    private readonly SortableBindingList<RouterRecord> _routers;
    private readonly BindingList<UpgradeJob> _jobs;
    private readonly DataGridView _routerGrid = new();
    private readonly DataGridView _jobGrid = new();
    private readonly RichTextBox _log = new();
    private readonly ToolStripStatusLabel _status = new("Ready");
    private readonly ContextMenuStrip _routerContextMenu;
    private readonly ComboBox _groupSelector = new() { Width = 145, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly DateTimePicker _scheduleTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MM-yyyy HH:mm", Width = 160 };
    private readonly TextBox _jobName = new() { Width = 210, PlaceholderText = "Maintenance job name" };
    private CancellationTokenSource? _running;
    private bool _operationInProgress;
    private bool _suspendRouterSaves;
    private bool _contextMenuRowValid;

    public MainForm()
    {
        _routerContextMenu = new ContextMenuStrip(_components);
        Text = "Indigo Router Scheduler 0.1.10";
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
        Shown += (_, _) => _routerGrid.ClearSelection();
    }

    private void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildRoutersPage());
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
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 118, Padding = new Padding(8), WrapContents = true };
        bar.Controls.Add(Button("Import CDB / WBX", ImportCdb));
        bar.Controls.Add(Button("Add Manually", AddRouterManually));
        bar.Controls.Add(Button("Select All", (_, _) => _routerGrid.SelectAll()));
        bar.Controls.Add(Button("Clear Selection", (_, _) => _routerGrid.ClearSelection()));
        bar.Controls.Add(new Label { Text = "Upgrade group:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        bar.Controls.Add(_groupSelector);
        bar.Controls.Add(Button("Set Group", AssignSelectedGroup));
        bar.Controls.Add(Button("Clear Group", ClearSelectedGroup));
        bar.Controls.Add(Button("Select Group", SelectRouterGroup));
        bar.Controls.Add(Button("Remove Selected", RemoveSelectedRouters));
        bar.Controls.Add(Button("Check API Status", CheckApiStatusSelected));
        bar.Controls.Add(Button("Fetch Current Versions", FetchCurrentVersions));
        bar.Controls.Add(Button("Backup Selected", BackupSelectedRouters));
        bar.Controls.Add(Button("Upgrade Selected", RunNow));
        bar.Controls.Add(Button("Cancel", (_, _) => _running?.Cancel()));
        bar.Controls.Add(new Label { Text = "Tip: Ctrl-click individual rows; Shift-click a range", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 7, 0, 0) });

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
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.Name), "Router", 180));
        DataGridViewTextBoxColumn groupColumn = TextColumn(nameof(RouterRecord.Group), "Upgrade Group", 105);
        groupColumn.MaxInputLength = 80;
        _routerGrid.Columns.Add(groupColumn);
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.Host), "Address", 120));
        _routerGrid.Columns.Add(TextColumn(nameof(RouterRecord.Model), "Model", 105, true));
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
        _groupSelector.DropDown += (_, _) => RefreshGroupChoices();
        RefreshGroupChoices();
        _routerContextMenu.Items.Add("Check API Status", null, CheckApiStatusSelected);
        _routerContextMenu.Items.Add("Fetch Current Versions", null, FetchCurrentVersions);
        _routerContextMenu.Items.Add(new ToolStripSeparator());
        _routerContextMenu.Items.Add("Remove Router", null, RemoveSelectedRouters);
        _routerContextMenu.Opening += (_, e) => e.Cancel = !_contextMenuRowValid || _operationInProgress;
        _routerGrid.ContextMenuStrip = _routerContextMenu;
        page.Controls.Add(_routerGrid);
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
        AddRow(panel, "RouterOS update channel", channel);
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
                UpdateChannel = channel.SelectedItem?.ToString() ?? "stable"
            };
            _store.SaveSettings(value);
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

    private void RouterGridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        _contextMenuRowValid = false;
        if (e.RowIndex < 0 || _operationInProgress)
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
        _contextMenuRowValid = true;
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
                $"Bulk backup completed.\n\nSuccessful: {result.Successful}\nFailed: {result.Failed}\n\nFolder:\n{result.Folder}\n\nThe .rsc exports and BackupManifest.csv contain sensitive information.",
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
        if (MessageBox.Show($"Run the safe upgrade on {selected.Count} router(s), strictly one at a time?\n\n{names}\n\nThe process stops when a router fails.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetBusy(true, "Upgrade running...");
        _running = new CancellationTokenSource();
        try
        {
            CancellationToken token = _running.Token;
            UpgradeEngine engine = CreateEngine();
            await Task.Run(() => engine.RunSequentialAsync(selected, token), token);
            MessageBox.Show("All selected routers completed successfully.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { MessageBox.Show("Upgrade cancelled."); }
        catch (Exception ex) { ShowError(ex); }
        finally { SaveRouters(); _routerGrid.Refresh(); SetBusy(false, "Ready"); _running.Dispose(); _running = null; }
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
            RouterIds = selected.Select(x => x.Id).ToList()
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
        engine.Message += line => BeginInvoke((Action)(() => { _log.AppendText(line + Environment.NewLine); _log.ScrollToCaret(); _status.Text = line; _routerGrid.Refresh(); }));
        return engine;
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => AppendLog(line))); return; }
        _log.AppendText(line + Environment.NewLine);
        _log.ScrollToCaret();
        _status.Text = line;
        _routerGrid.Refresh();
    }

    private List<RouterRecord> SelectedRouters() => _routerGrid.Rows.Cast<DataGridViewRow>()
        .Where(row => row.Selected && row.DataBoundItem is RouterRecord)
        .OrderBy(row => row.Index)
        .Select(row => (RouterRecord)row.DataBoundItem!)
        .ToList();
    private void SaveRouters() { if (!_suspendRouterSaves) _store.SaveRouters(_routers.ToList()); }
    private void SaveAll() { SaveRouters(); _store.SaveJobs(_jobs.ToList()); }
    private void SetBusy(bool busy, string text) { _operationInProgress = busy; _status.Text = text; _routerGrid.Enabled = !busy; }
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
