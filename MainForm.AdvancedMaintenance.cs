namespace MikroTikManager;

public sealed partial class MainForm
{
    private MaintenanceProfile? _chosenProfile;
    private MaintenanceProfile? _scheduleProfile;
    private void EditDependencies(object? sender, EventArgs args)
    {
        if (_operationInProgress) return;
        var selected = SelectedRouters();
        if (selected.Count != 1) { MessageBox.Show("Select one device to configure its upgrade prerequisites."); return; }
        var router = selected[0];
        using var dialog = new DpiDialog { Text = "Upgrade after — " + router.Name, Width = 720, Height = 600, StartPosition = FormStartPosition.CenterParent };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, DisplayMember = "Name" };
        var others = _routers.Where(r => r.Id != router.Id).OrderBy(r => r.Name).ToList();
        foreach (var r in others) list.Items.Add(r, router.UpgradeAfter.Contains(r.Id));
        var note = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Text = "This device starts only after all checked devices finish successfully.\nInclude those devices in the same upgrade job. No automatic topology detection.\nFor downstream-first maintenance, configure the upstream device to run after its downstream devices." };
        var save = new Button { Text = "Save Upgrade Order", Dock = DockStyle.Bottom, AutoSize = true };
        save.Click += (_, _) => {
            var previous = router.UpgradeAfter;
            router.UpgradeAfter = list.CheckedItems.Cast<RouterRecord>().Select(r => r.Id).ToList();
            try { DependencyPlan.Validate(_routers.ToList()); SaveRouters(); dialog.DialogResult = DialogResult.OK; }
            catch (Exception ex) { router.UpgradeAfter = previous; MessageBox.Show(dialog, ex.Message); }
        };
        dialog.Controls.Add(list); dialog.Controls.Add(note); dialog.Controls.Add(save); dialog.ShowDialog(this);
    }
    private ComboBox ProfileChoice()
    {
        var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        choice.Items.Add("Custom / current settings");
        foreach (var profile in _store.LoadSettings().MaintenanceProfiles) choice.Items.Add(profile);
        choice.SelectedIndex = 0; return choice;
    }
    private void EditProfiles(object? sender, EventArgs args)
    {
        if (_operationInProgress) return;
        using var dialog = new DpiDialog { Text = "Maintenance profiles", Width = 900, Height = 650, StartPosition = FormStartPosition.CenterParent };
        var settings = _store.LoadSettings();
        var items = new System.ComponentModel.BindingList<MaintenanceProfile>(settings.MaintenanceProfiles);
        var list = new ListBox { Dock = DockStyle.Left, Width = 260, DataSource = items, DisplayMember = "Name" };
        var editor = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true };
        list.SelectedIndexChanged += (_, _) => editor.SelectedObject = list.SelectedItem;
        editor.SelectedObject = list.SelectedItem;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12) };
        bar.Controls.Add(Button("Add Profile", (_, _) => { var p = new MaintenanceProfile { Name = "Profile " + (items.Count + 1) }; items.Add(p); list.SelectedItem = p; }));
        bar.Controls.Add(Button("Remove Profile", (_, _) => { if (list.SelectedItem is MaintenanceProfile p) items.Remove(p); }));
        bar.Controls.Add(Button("Save Profiles", (_, _) => {
            if (items.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.MaxConcurrency < 0 || p.MaxConcurrency > 10000 || p.FailureLimit < 0 || p.RetryCount < 0 || p.RetryCount > 5 || p.StabilitySeconds < 0 || p.StabilitySeconds > 300 || p.Channel.Length > 0 && !RouterChannels.Allowed.Contains(p.Channel)) || items.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            { MessageBox.Show("Use unique profile names, valid channels (or blank), 0–5 retries, 0–300 stability seconds, and non-negative limits."); return; }
            try { var latest = _store.LoadSettings(); latest.MaintenanceProfiles = items.ToList(); _store.SaveSettings(latest); dialog.Close(); } catch (Exception ex) { ShowError(ex); }
        }));
        var note = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Text = "Choose a saved profile in Upgrade Preview or Create Upgrade Schedule.\nBlank channel uses the app default. Explicit per-device channels always take precedence.\nSchedules retain a copy of profile settings; editing a profile does not change existing jobs." };
        dialog.Controls.Add(editor); dialog.Controls.Add(list); dialog.Controls.Add(note); dialog.Controls.Add(bar); dialog.ShowDialog(this);
    }
    private void ConfigureRecovery(object? sender, EventArgs args)
    {
        if (_operationInProgress) return;
        using var dialog = new DpiDialog { Text = "Recovery storage and health history", Width = 850, Height = 650, StartPosition = FormStartPosition.CenterParent };
        var settings = _store.LoadSettings();
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(20) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        var copies = Numeric(settings.PreUpgradeKeepCopies, 0, 1000); var budget = Numeric(settings.PreUpgradeBudgetMb, 0, 1000000);
        var offsite = new TextBox { Text = settings.OffsiteFolder, Dock = DockStyle.Fill };
        var days = Numeric(settings.HealthHistoryDays, 1, 90); var cpu = Numeric(settings.CpuWarningPercent, 1, 100);
        var memory = Numeric(settings.MemoryWarningPercent, 1, 100); var temperature = Numeric(settings.TemperatureWarningC, 1, 150);
        AddRow(panel, "Pre-upgrade copies per device (0 = unlimited)", copies);
        AddRow(panel, "Pre-upgrade budget MB per device (0 = unlimited)", budget);
        AddRow(panel, "Offsite copy folder (blank = disabled)", offsite);
        AddRow(panel, "Health history retention days", days); AddRow(panel, "CPU warning %", cpu);
        AddRow(panel, "Memory warning %", memory); AddRow(panel, "Temperature warning °C", temperature);
        AddRow(panel, "Storage policy", new Label { AutoSize = true, MaximumSize = new Size(330, 0), Text = "Cleanup runs after a verified replacement; the newest recovery copy is always retained. Offsite accepts an absolute folder or UNC share. Protect access: exports and manifests contain secrets. Windows service uses SYSTEM, not your mapped drives. Transfer failures are reported separately and keep local copies." });
        var save = Button("Save", (_, _) => {
            if (offsite.Text.Trim().Length > 0 && !Path.IsPathFullyQualified(offsite.Text.Trim())) { MessageBox.Show("Use an absolute local or UNC folder path."); return; }
            try {
                var latest = _store.LoadSettings(); latest.PreUpgradeKeepCopies = (int)copies.Value; latest.PreUpgradeBudgetMb = (int)budget.Value;
                latest.OffsiteFolder = offsite.Text.Trim(); latest.HealthHistoryDays = (int)days.Value;
                latest.CpuWarningPercent = (int)cpu.Value; latest.MemoryWarningPercent = (int)memory.Value; latest.TemperatureWarningC = (int)temperature.Value;
                _store.SaveSettings(latest); dialog.Close();
            } catch (Exception ex) { ShowError(ex); }
        });
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; body.Controls.Add(panel);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12) }; footer.Controls.Add(save);
        dialog.Controls.Add(body); dialog.Controls.Add(footer); dialog.ShowDialog(this);
    }
}
