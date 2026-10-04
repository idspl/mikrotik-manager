namespace MikroTikManager;

public sealed partial class MainForm
{
    private async void CreateBackupSchedule(object? sender, EventArgs e) => await ConfigureBackupScheduleAsync(null);

    private async void EditBackupSchedule(object? sender, EventArgs e)
    {
        if (_jobGrid.CurrentRow?.DataBoundItem is not UpgradeJob job || job.Kind != ScheduledJobKind.Backup)
        { MessageBox.Show("Select a backup schedule to edit."); return; }
        await ConfigureBackupScheduleAsync(job);
    }

    private async Task ConfigureBackupScheduleAsync(UpgradeJob? existing)
    {
        if (_operationInProgress || _updateInProgress) return;
        var routers = existing is null ? SelectedRouters() : existing.RouterIds.Select(id => _routers.FirstOrDefault(r => r.Id == id)).OfType<RouterRecord>().ToList();
        if (routers.Count == 0) { MessageBox.Show("Select routers on the Routers tab first."); return; }
        using var dialog = new DpiDialog { Text = existing is null ? "Schedule Local Backups" : "Edit Backup Schedule", Width = 650, Height = 540,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var name = new TextBox { Width = 400, Text = existing?.Name ?? "Daily router backups" };
        var start = new DateTimePicker { Width = 220, Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy HH:mm",
            Value = existing?.ScheduledLocalTime > DateTime.Now.AddMinutes(2) ? existing.ScheduledLocalTime : DateTime.Now.AddHours(1) };
        var repeat = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        repeat.Items.AddRange(Enum.GetValues<BackupRecurrence>().Cast<object>().ToArray());
        repeat.SelectedItem = existing?.Recurrence ?? BackupRecurrence.Daily;
        var folder = new TextBox { Width = 330, Text = existing?.BackupFolder ?? @"C:\MikroTikBackups" };
        var retention = Numeric(existing?.RetentionDays ?? 30, 0, 3650);
        var browse = Button("Browse…", (_, _) => { using var pick = new FolderBrowserDialog { SelectedPath = folder.Text }; if (pick.ShowDialog(dialog) == DialogResult.OK) folder.Text = pick.SelectedPath; });
        AddRow(panel, "Schedule name", name); AddRow(panel, "First / next run", start); AddRow(panel, "Repeat", repeat);
        AddRow(panel, "Local folder", folder); AddRow(panel, "", browse); AddRow(panel, "Keep days (0 = all)", retention);
        AddRow(panel, "Routers", new Label { Text = $"{routers.Count} routers: " + string.Join(", ", routers.Take(4).Select(r => r.Name)), AutoSize = true, MaximumSize = new Size(400, 45) });
        AddRow(panel, "", new Label { Text = "Backups run in parallel and continue after device failures. Stores sensitive exports in a separate folder. Use a local fixed drive writable by Windows SYSTEM and your desktop user.", AutoSize = true, MaximumSize = new Size(400, 65) });
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(new Button { Text = "Save Schedule", DialogResult = DialogResult.OK, AutoSize = true });
        buttons.Controls.Add(new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true }); AddRow(panel, "", buttons);
        dialog.Controls.Add(panel);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var job = new UpgradeJob { Id = existing?.Id ?? Guid.NewGuid(), Name = name.Text.Trim(), Kind = ScheduledJobKind.Backup,
            ScheduledLocalTime = BackupSchedulePolicy.Minute(start.Value), Recurrence = (BackupRecurrence)repeat.SelectedItem!,
            BackupFolder = folder.Text.Trim(), RetentionDays = (int)retention.Value, RouterIds = routers.Select(r => r.Id).ToList(), State = "Registering",
            LastRunResult = existing?.LastRunResult ?? "", LastRunSuccessful = existing?.LastRunSuccessful ?? false, CompletedAt = existing?.CompletedAt };
        SetBusy(true, "Registering backup schedule...");
        int index = existing is null ? -1 : _jobs.IndexOf(existing);
        bool inserted = false;
        try
        {
            if (job.Name.Length == 0) throw new ArgumentException("Schedule name is required.");
            BackupSchedulePolicy.Validate(job);
            if (job.ScheduledLocalTime <= DateTime.Now.AddMinutes(1)) throw new ArgumentException("Choose a start time at least two minutes in the future.");
            Directory.CreateDirectory(BackupSchedulePolicy.JobFolder(job));
            if (index < 0) _jobs.Add(job); else _jobs[index] = job;
            inserted = true; _store.SaveJobs(_jobs.ToList());
            await TaskSchedulerService.RegisterAsync(job);
            job.State = "Scheduled"; _store.SaveJobs(_jobs.ToList()); _jobGrid.Refresh();
            _tabs.SelectedTab = _schedulesPage;
        }
        catch (Exception ex)
        {
            if (inserted) { if (index < 0) _jobs.Remove(job); else _jobs[index] = existing!; _store.SaveJobs(_jobs.ToList()); }
            ShowError(ex);
        }
        finally { SetBusy(false, "Ready"); RefreshDashboard(); }
    }

    private void DisableBackupSchedule(object? sender, EventArgs e)
    {
        if (_operationInProgress || _jobGrid.CurrentRow?.DataBoundItem is not UpgradeJob job || job.Kind != ScheduledJobKind.Backup) return;
        if (MessageBox.Show($"Disable '{job.Name}'? Use Edit Backup Schedule to set a future date and enable it again.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        job.State = "Disabled"; _store.SaveJobs(_jobs.ToList()); _jobGrid.Refresh(); RefreshDashboard();
    }

    private void SetSelectedChannel(string channel)
    {
        if (_operationInProgress || _updateInProgress) return;
        var selected = SelectedRouters(); if (selected.Count == 0) { MessageBox.Show("Select routers first."); return; }
        foreach (var router in selected) router.UpdateChannel = channel;
        SaveRouters(); RequestInventoryPaint(); RefreshDashboard();
    }
}
