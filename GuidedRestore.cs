namespace MikroTikManager;

internal static class GuidedRestore
{
    internal static void CheckIdentity(BackupHistoryEntry backup, string model, string version, string serial)
    {
        if (string.IsNullOrWhiteSpace(backup.SerialNumber) || string.IsNullOrWhiteSpace(backup.Model) || string.IsNullOrWhiteSpace(backup.RouterOsVersion))
            throw new InvalidOperationException("This backup lacks verified device identity. Automatic restore is blocked; use the vendor's manual recovery procedure.");
        if (backup.SerialNumber != serial || backup.Model != model || backup.RouterOsVersion != version)
            throw new InvalidOperationException("Restore blocked: device serial, model and RouterOS version must match the backup exactly. No automatic downgrade or cross-device restore.");
    }
    internal static async Task VerifyRemoteAsync(RouterOsApiClient api, BackupHistoryEntry backup, string remoteName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(remoteName) || !remoteName.EndsWith(".backup", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Enter the exact uploaded .backup filename on the router.");
        await RecoveryFiles.VerifyAsync(backup, ct);
        var resource = (await api.ExecuteAsync("/system/resource/print", ct)).First(r => r.Type == "!re").Attributes;
        var board = (await api.ExecuteAsync("/system/routerboard/print", ct)).First(r => r.Type == "!re").Attributes;
        CheckIdentity(backup, resource.GetValueOrDefault("board-name") ?? "", resource.GetValueOrDefault("version") ?? "", board.GetValueOrDefault("serial-number") ?? "");
        // Verify the already-staged remote bytes, not just a filename or file size.
        string temporary = Path.Combine(Path.GetTempPath(), "mm-restore-" + Guid.NewGuid().ToString("N") + ".backup");
        try
        {
            await api.DownloadFileAsync(remoteName, temporary, ct);
            if (!string.Equals(await RecoveryFiles.HashAsync(temporary, ct), backup.BackupHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The router's staged file does not match the selected verified backup.");
        }
        finally { try { File.Delete(temporary); } catch { } }
    }
}

public sealed partial class MainForm
{
    private async void ShowGuidedRestore(object? sender, EventArgs args)
    {
        if (_operationInProgress || _updateInProgress) return;
        var selected = SelectedBackupRouters();
        if (selected.Count != 1) { MessageBox.Show("Select one device in Backups for guided restore."); return; }
        var router = selected[0]; var entries = router.Backups.OrderByDescending(b => b.CreatedAt).ToList();
        if (entries.Count == 0) { MessageBox.Show("No recorded backups for this device."); return; }
        using var dialog = new DpiDialog { Text = "Guided restore — " + router.Name, Width = 900, Height = 650, StartPosition = FormStartPosition.CenterParent };
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(20) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        var choice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var entry in entries) choice.Items.Add($"{entry.CreatedAt:g} • {entry.Model} • {entry.RouterOsVersion}"); choice.SelectedIndex = 0;
        var remote = new TextBox { Dock = DockStyle.Fill, Text = Path.GetFileName(entries[0].BackupPath) };
        choice.SelectedIndexChanged += (_, _) => remote.Text = Path.GetFileName(entries[choice.SelectedIndex].BackupPath);
        var password = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Text = router.BackupPassword };
        var typed = new TextBox { Dock = DockStyle.Fill };
        AddRow(panel, "Recovery copy", choice); AddRow(panel, "Staged router filename", remote); AddRow(panel, "Backup password", password);
        AddRow(panel, "Type device name to confirm", typed);
        var note = new Label { AutoSize = true, MaximumSize = new Size(510, 0), Text = "First upload the selected encrypted .backup to this same router using your existing file-transfer tool. This app uses API to verify and restore a staged file; it does not enable FTP, SSH or WinBox services.\n\nBoth local files and the router copy are checked. Device serial, model and RouterOS version must match. Older backups without identity metadata cannot be restored here.\n\nRestore replaces configuration and restarts the router. Management address and credentials may change. Keep console access available. A new local recovery backup is required before sending the restore command. No automatic retry." };
        AddRow(panel, "Procedure", note);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12) };
        var accept = new Button { Text = "Verify and Restore…", AutoSize = true, DialogResult = DialogResult.OK };
        footer.Controls.Add(accept); var close = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel }; footer.Controls.Add(close);
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; body.Controls.Add(panel); dialog.Controls.Add(body); dialog.Controls.Add(footer);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (typed.Text != router.Name || string.IsNullOrEmpty(password.Text)) { MessageBox.Show("Device name confirmation and backup password are required."); return; }
        var backup = entries[choice.SelectedIndex]; string remoteName = remote.Text.Trim(); string restorePassword = password.Text;
        SetBusy(true, "Verifying restore files and device identity…"); _running = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        bool restoreRequested = false;
        try
        {
            await using var api = await RouterOsApiClient.ConnectAsync(router, _store.LoadSettings(), _running.Token);
            await GuidedRestore.VerifyRemoteAsync(api, backup, remoteName, _running.Token);
            if (MessageBox.Show(this, $"Verified {router.Name} ({router.Host}), backup {backup.CreatedAt:g}.\n\nCreate a new recovery copy, then replace this router's configuration and reboot?", "Confirm restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (string.IsNullOrEmpty(router.BackupPassword)) router.BackupPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
            SaveRouters();
            var safety = new RouterBackupService(_store.LoadSettings(), 0);
            var result = await safety.BackupAllAsync([router], Path.Combine(_store.RootDirectory, "Backups", "BeforeRestore", router.Id.ToString("N")), _running.Token);
            SaveRouters();
            if (result.Verified != 1 || result.Failed != 0) throw new IOException("New recovery backup failed; restore blocked.");
            // Re-check the staged bytes and identity after the operator confirmation and safety backup.
            await GuidedRestore.VerifyRemoteAsync(api, backup, remoteName, _running.Token);
            _status.Text = "Sending restore request — do not repeat";
            AppendLog($"Restore requested: {router.Name}; recovery {backup.CreatedAt:O}");
            restoreRequested = true;
            await api.SendExpectedDisconnectAsync("/system/backup/load", _running.Token, "name=" + remoteName[..^7], "password=" + restorePassword);
            router.ApiStatus = "Not checked"; router.LastStatus = "Restore requested — verify manually";
            MessageBox.Show(this, "Restore request sent. A disconnect is expected; completion is not verified. Reconnect using the restored configuration and verify the router before any further maintenance.");
        }
        catch (Exception ex) {
            if (restoreRequested) { router.LastStatus = "Restore outcome uncertain — verify manually"; MessageBox.Show(this, "Restore outcome is uncertain. Do not repeat the request until you have checked the router. " + ex.Message); }
            else ShowError(ex);
        }
        finally { SaveRouters(); _running.Dispose(); _running = null; SetBusy(false, "Ready"); }
    }
}
