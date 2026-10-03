namespace MikroTikManager;

public sealed partial class MainForm
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr handle);
    private readonly CheckBox _scheduleWindow = new() { Text = "Cutoff", AutoSize = true };
    private readonly DateTimePicker _scheduleEnd = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MM-yyyy HH:mm", Width = 155, Value = DateTime.Now.AddHours(3) };

    private Task<bool> ConfirmGroupContinuationAsync(RouterRecord router, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke((Action)(() =>
        {
            if (ct.IsCancellationRequested) { completion.TrySetCanceled(ct); return; }
            string group = string.IsNullOrWhiteSpace(router.Group) ? "Ungrouped" : router.Group;
            bool approved = MessageBox.Show(this, $"Test router {router.Name} in group '{group}' completed successfully.\n\nCheck its services, then continue the remaining routers in this group?\nNo pauses the queue for later review.", "Approve group rollout", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            completion.TrySetResult(approved);
        }));
        return completion.Task.WaitAsync(ct);
    }

    private void ShowRunSummary(MaintenanceRunResult result, List<RouterRecord> selected)
    {
        using var dialog = new Form { Text = "Maintenance Results", Width = 1000, Height = 520, StartPosition = FormStartPosition.CenterParent };
        var rows = new System.Data.DataTable();
        foreach (string column in new[] { "Router", "Outcome", "RouterOS", "Firmware", "Detail" }) rows.Columns.Add(column);
        foreach (var router in selected)
        {
            var entry = result.Routers.FirstOrDefault(x => x.RouterId == router.Id);
            string outcome = entry is null ? "Skipped / not started" : entry.Result == "Completed"
                ? entry.Message.StartsWith("Already current") ? "Already current" : "Upgraded" : entry.Result;
            rows.Rows.Add(router.Name, outcome, entry?.RouterOsVersion ?? router.RouterOsVersion,
                entry?.FirmwareVersion ?? router.FirmwareVersion, entry?.Message ?? "Queue paused, stopped or maintenance window ended");
        }
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, DataSource = rows, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
        var info = new Label { Dock = DockStyle.Bottom, Height = 45, Text = "Use Retry Failed or Resume Incomplete Queue on Maintenance Progress to continue after reviewing the result." };
        dialog.Controls.Add(grid); dialog.Controls.Add(info); dialog.ShowDialog(this);
    }

    private void StartScheduleMonitor()
    {
        var timer = new System.Windows.Forms.Timer(_components) { Interval = 2000 };
        timer.Tick += async (_, _) =>
        {
            // Native file pickers and ShowDialog disable their owner. Do not start a
            // job in their nested message loops while the user is editing inventory.
            if (_operationInProgress || _updateInProgress || !IsWindowEnabled(Handle)) return;
            var job = _jobs.Where(x => x.State == "Scheduled" && x.ScheduledLocalTime <= DateTime.Now)
                .OrderBy(x => x.ScheduledLocalTime).FirstOrDefault();
            if (job is null) return;
            await RunScheduledInUiAsync(job);
        };
        timer.Start();
    }

    private async Task RunScheduledInUiAsync(UpgradeJob job, bool manualRun = false)
    {
        string previousState = job.State;
        SetBusy(true, "Scheduled " + job.Kind + ": " + job.Name);
        _running = new CancellationTokenSource();
        var selected = job.RouterIds.Select(id => _routers.FirstOrDefault(r => r.Id == id)).OfType<RouterRecord>().ToList();
        job.State = "Running"; job.StartedAt = DateTime.Now;
        try
        {
            SaveAll();
            if (selected.Count != job.RouterIds.Count || selected.Count == 0) throw new InvalidOperationException("Scheduled routers are missing or empty.");
            _lastUpgradeRouterIds = job.Kind == ScheduledJobKind.Upgrade ? selected.Select(x => x.Id).ToList() : [];
            PrepareProgressRows(selected); _tabs.SelectedTab = _maintenancePage;
            if (job.Kind == ScheduledJobKind.Backup)
            {
                var backup = await ScheduledBackupRunner.RunAsync(_store, _store.LoadSettings(), job, selected, AppendLog,
                    update => UpdateProgress(update), _running.Token);
                job.State = backup.Failed == 0 ? "Completed" : $"Backup failed: {backup.Failed} routers";
                job.LastRunResult = $"Backup: {backup.Successful} successful, {backup.Failed} failed; {backup.Folder}";
            }
            else
            {
            var engine = CreateEngine(); _activeUpgrade = engine;
            var result = await Task.Run(() => engine.RunSequentialAsync(selected,
                new UpgradeRunOptions(job.FailureBehavior, job.RetryCount, manualRun ? null : job.WindowEnd), _running.Token));
            await new MaintenanceReportService(_store).WriteAsync(result);
            job.State = result.Failed == 0 && result.Routers.Count == selected.Count ? "Completed"
                : $"Finished: {result.Failed} failed; {selected.Count - result.Routers.Count} not processed";
            }
        }
        catch (OperationCanceledException) { job.State = "Cancelled"; }
        catch (Exception ex) { job.State = "Failed: " + ex.Message; }
        finally
        {
            bool success = job.State == "Completed";
            string result = success && job.Kind == ScheduledJobKind.Backup ? job.LastRunResult : job.State;
            BackupSchedulePolicy.Finish(job, success, result, DateTime.Now, manualRun ? previousState : null); _activeUpgrade = null;
            try { SaveAll(); } catch (Exception ex) { ShowError(ex); }
            _running.Dispose(); _running = null; SetBusy(false, job.Name + ": " + job.LastRunResult);
            _jobGrid.Refresh(); _routerGrid.Refresh();
        }
    }

    private async void ActivateImportedSchedule(object? sender, EventArgs e)
    {
        if (_operationInProgress || _jobGrid.CurrentRow?.DataBoundItem is not UpgradeJob job) return;
        if (job.Kind == ScheduledJobKind.Backup) { await ConfigureBackupScheduleAsync(job); return; }
        if (job.State != "Imported — disabled") { MessageBox.Show("Select an imported disabled schedule. Set its Run at date above before activating."); return; }
        if (_scheduleTime.Value <= DateTime.Now.AddMinutes(1)) { MessageBox.Show("Choose a future Run at time."); return; }
        if (_scheduleWindow.Checked && _scheduleEnd.Value <= _scheduleTime.Value) { MessageBox.Show("Cutoff must be after Run at."); return; }
        if (MessageBox.Show($"Activate '{job.Name}' for {_scheduleTime.Value:g}, containing {job.RouterCount} routers?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        SetBusy(true, "Registering schedule...");
        try
        {
            job.ScheduledLocalTime = _scheduleTime.Value; job.WindowEnd = _scheduleWindow.Checked ? _scheduleEnd.Value : null;
            await TaskSchedulerService.RegisterAsync(job);
            job.State = "Scheduled"; _store.SaveJobs(_jobs.ToList()); _jobGrid.Refresh();
        }
        catch (Exception ex) { job.State = "Imported — disabled"; ShowError(ex); }
        finally { SetBusy(false, "Ready"); }
    }
}
