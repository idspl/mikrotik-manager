namespace MikroTikManager;

public sealed partial class MainForm
{
    private void ShowUpgradeScheduleDialog(object? sender, EventArgs e)
    {
        if (_operationInProgress || _updateInProgress) return;
        if (SelectedRouters().Count == 0) { MessageBox.Show("Select devices in Devices first."); return; }
        using var dialog = new Form { Text = "Create Upgrade Schedule", Width = 530, Height = 380, StartPosition = FormStartPosition.CenterParent };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18) };
        AddRow(layout, "Name", _jobName); AddRow(layout, "Run at", _scheduleTime);
        AddRow(layout, "Concurrent (0 = all)", _scheduleConcurrency);
        AddRow(layout, "", _scheduleWindow); AddRow(layout, "Cutoff", _scheduleEnd);
        AddRow(layout, "", new Label { Text = "Parallel upgrades require independent devices. Failure policy and retries use the Devices screen settings.", AutoSize = true, MaximumSize = new Size(280, 70) });
        AddRow(layout, "", new Button { Text = "Create Schedule", AutoSize = true, DialogResult = DialogResult.OK }); dialog.Controls.Add(layout);
        var result = dialog.ShowDialog(this);
        // These fields belong to MainForm and must outlive the temporary dialog.
        foreach (Control control in new Control[] { _jobName, _scheduleTime, _scheduleConcurrency, _scheduleWindow, _scheduleEnd }) layout.Controls.Remove(control);
        if (result == DialogResult.OK) CreateSchedule(sender, e);
    }
    private void BuildScheduleContextMenu()
    {
        var menu = new ContextMenuStrip(_components);
        menu.Items.Add("Run Job Now…", null, RunJobNow);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete Job / Schedule…", null, DeleteSchedule);
        bool rowValid = false;
        _jobGrid.CellMouseDown += (_, e) => {
            if (e.Button != MouseButtons.Right) return;
            rowValid = e.RowIndex >= 0 && e.ColumnIndex >= 0;
            if (!rowValid) return;
            _jobGrid.ClearSelection();
            _jobGrid.CurrentCell = _jobGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            _jobGrid.Rows[e.RowIndex].Selected = true;
        };
        menu.Opening += (_, e) => e.Cancel = !rowValid || _operationInProgress || _updateInProgress
            || _jobGrid.CurrentRow?.DataBoundItem is not UpgradeJob job || job.State == "Running";
        _jobGrid.ContextMenuStrip = menu;
    }

    private async void RunJobNow(object? sender, EventArgs e)
    {
        if (_operationInProgress || _updateInProgress) return;
        if (_jobGrid.CurrentRow?.DataBoundItem is not UpgradeJob job) { MessageBox.Show("Select a job first."); return; }
        if (job.State == "Running") { MessageBox.Show("This job is already running."); return; }
        var routers = job.RouterIds.Select(id => _routers.FirstOrDefault(r => r.Id == id)).OfType<RouterRecord>().ToList();
        if (routers.Count == 0 || routers.Count != job.RouterIds.Count) { MessageBox.Show("Some scheduled routers are missing. Recreate the job with the current inventory."); return; }
        try { BackupSchedulePolicy.Validate(job); }
        catch (Exception ex) { ShowError(ex); return; }
        string effect = job.Kind == ScheduledJobKind.Backup && job.Recurrence != BackupRecurrence.Once
            ? job.State == "Scheduled" ? "A future scheduled run keeps its date. If already due, this run advances it to the next occurrence."
                : "This runs once now and does not enable the recurring schedule."
            : "This executes the one-time job now. On success it is marked Completed and will not run again at its scheduled time.";
        if (job.Kind == ScheduledJobKind.Upgrade) effect += "\nThe saved cutoff is ignored for this manual run. Routers may reboot and interrupt service.";
        string names = string.Join("\n", routers.Take(12).Select(r => "• " + r.Name + " (" + r.Host + ")"));
        if (routers.Count > 12) names += $"\n…and {routers.Count - 12} more";
        if (MessageBox.Show(this, $"Run '{job.Name}' now?\nType: {job.Kind} | Routers: {routers.Count}\n\n{names}\n\n{effect}", "Run Job Now", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await RunScheduledInUiAsync(job, manualRun: true);
        RefreshDashboard();
    }

    private async void DeleteSchedule(object? sender, EventArgs e)
    {
        if (_operationInProgress || _updateInProgress) return;
        if (_jobGrid.CurrentRow?.DataBoundItem is not UpgradeJob job) { MessageBox.Show("Select a job first."); return; }
        if (job.State == "Running") { MessageBox.Show("Wait for the running job to finish before deleting it."); return; }
        if (MessageBox.Show(this, $"Delete '{job.Name}' and its Windows scheduled task?\n\nDownloaded backups, routers and maintenance history will be kept. Windows may request administrator approval.", "Delete Job / Schedule", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetBusy(true, "Deleting schedule...");
        string previous = job.State;
        bool taskRemoved = false;
        try
        {
            job.State = "Deleting"; _store.SaveJobs(_jobs.ToList());
            await TaskSchedulerService.DeleteAsync(job.Id); taskRemoved = true;
            _store.SaveJobs(_jobs.Where(j => j.Id != job.Id).ToList());
            _jobs.Remove(job);
        }
        catch (Exception ex)
        {
            job.State = taskRemoved ? "Disabled" : previous;
            try { _store.SaveJobs(_jobs.ToList()); } catch { }
            ShowError(ex);
        }
        finally { SetBusy(false, "Ready"); _jobGrid.Refresh(); RefreshDashboard(); }
    }
}
