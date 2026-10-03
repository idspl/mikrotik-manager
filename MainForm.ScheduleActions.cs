namespace MikroTikManager;

public sealed partial class MainForm
{
    private void ShowUpgradeScheduleDialog(object? sender, EventArgs e)
    {
        if (_operationInProgress || _updateInProgress) return;
        if (SelectedRouters().Count == 0) { MessageBox.Show("Select devices in Devices first."); return; }
        using var dialog = BuildUpgradeScheduleDialog(out var name, out var start, out var concurrent, out var cutoffEnabled, out var cutoff);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _jobName.Text = name.Text; _scheduleTime.Value = start.Value;
        _scheduleConcurrency.Value = concurrent.Value; _scheduleWindow.Checked = cutoffEnabled.Checked; _scheduleEnd.Value = cutoff.Value;
        CreateSchedule(sender, e);
    }

    private DpiDialog BuildUpgradeScheduleDialog(out TextBox name, out DateTimePicker start,
        out NumericUpDown concurrent, out CheckBox cutoffEnabled, out DateTimePicker cutoff)
    {
        var dialog = new DpiDialog { Text = "Create Upgrade Schedule", ClientSize = new Size(570, 390),
            MinimumSize = new Size(480, 360), StartPosition = FormStartPosition.CenterParent, MaximizeBox = false };
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(18) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        name = new TextBox { Text = _jobName.Text, Dock = DockStyle.Fill };
        start = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy  HH:mm", Dock = DockStyle.Fill, Value = _scheduleTime.Value };
        concurrent = new NumericUpDown { Minimum = 0, Maximum = 10000, Value = _scheduleConcurrency.Value, Width = 100 };
        cutoffEnabled = new CheckBox { Text = "Stop starting devices after cutoff", Checked = _scheduleWindow.Checked, AutoSize = true };
        cutoff = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy  HH:mm", Dock = DockStyle.Fill, Value = _scheduleEnd.Value, Enabled = cutoffEnabled.Checked };
        var check = cutoffEnabled; var end = cutoff;
        check.CheckedChanged += (_, _) => end.Enabled = check.Checked;
        AddRow(layout, "Name", name); AddRow(layout, "Run at", start); AddRow(layout, "Concurrent (0 = all)", concurrent);
        AddRow(layout, "", cutoffEnabled); AddRow(layout, "Cutoff", cutoff);
        var note = new Label { Text = "Parallel upgrades require independent devices. Failure policy and retries use the Upgrade menu settings.", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 12, 3, 12) };
        int row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(note, 0, row); layout.SetColumnSpan(note, 2);
        scroll.Controls.Add(layout);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), BackColor = Color.FromArgb(232, 241, 252) };
        var create = Button("Create Schedule", (_, _) => { }); create.DialogResult = DialogResult.OK;
        var cancel = Button("Cancel", (_, _) => { }); cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(create); footer.Controls.Add(cancel); dialog.AcceptButton = create; dialog.CancelButton = cancel;
        shell.Controls.Add(scroll, 0, 0); shell.Controls.Add(footer, 0, 1); dialog.Controls.Add(shell);
        return dialog;
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
        if (MessageBox.Show(this, $"Run '{job.Name}' now?\nType: {job.Kind} | Routers: {routers.Count} | Concurrent: {(job.MaxConcurrency == 0 ? "All" : job.MaxConcurrency.ToString())}\n\n{names}\n\n{effect}", "Run Job Now", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
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
