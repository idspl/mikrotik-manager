using System.Diagnostics;
using System.Security.Cryptography;

namespace MikroTikManager;

public sealed partial class MainForm
{
    private void EditRouter(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        var selected = SelectedRouters();
        if (selected.Count != 1) { MessageBox.Show("Select one router to edit."); return; }
        var router = selected[0];
        using var dialog = new ManualRouterDialog(router.ApiPort, router);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Router is not { } edited) return;
        edited.Site = router.Site; edited.Tags = router.Tags; edited.LastSeenAt = router.LastSeenAt;
        edited.LastBackupAttemptAt = router.LastBackupAttemptAt; edited.LastBackupError = router.LastBackupError;
        edited.Id = router.Id; edited.BackupPassword = router.BackupPassword; edited.Backups = router.Backups;
        if (edited.Host == router.Host && edited.ApiPort == router.ApiPort)
        {
            edited.Model = router.Model; edited.RouterOsVersion = router.RouterOsVersion;
            edited.FirmwareVersion = router.FirmwareVersion; edited.AvailableFirmware = router.AvailableFirmware;
            edited.LastCheckedAt = router.LastCheckedAt;
        }
        int index = _routers.IndexOf(router);
        _routers[index] = edited;
        SaveRouters(); RefreshGroupChoices(); ApplyRouterFilter();
    }

    private async void SelectOutdatedRouters(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        SetBusy(true, "Checking published RouterOS versions...");
        try
        {
            var releases = await RouterOsReleases.FetchAsync();
            var settings = _store.LoadSettings();
            _routerGrid.ClearSelection();
            int count = 0;
            foreach (DataGridViewRow row in _routerGrid.Rows)
            {
                if (row.DataBoundItem is not RouterRecord router || router.ApiStatus != "Online" || router.LastCheckedAt is null) continue;
                string channel = RouterChannels.Resolve(router, settings);
                string target = channel switch { "stable" => releases.Stable, "long-term" => releases.LongTerm, "testing" => releases.Testing, _ => "" };
                bool os = ReleaseVersion.IsNewerSameMajor(target, router.RouterOsVersion);
                bool firmware = ReleaseVersion.IsNewerSameMajor(router.AvailableFirmware, router.FirmwareVersion);
                if (os || firmware) { row.Selected = true; count++; }
            }
            MessageBox.Show($"Selected {count} visible router(s) with a newer published version for their configured channel in the same major branch or newer available firmware.\n\nFetch Current Versions first for current inventory. Offline, unknown and cross-major upgrades are not selected automatically. The router resolves its applicable target during upgrade.");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false, "Ready"); }
    }

    private void ShowBackupHistory(object? sender, EventArgs e)
    {
        var selected = SelectedRouters();
        var routers = selected.Count > 0 ? selected : _routers.ToList();
        using var dialog = new Form { Text = "Local Backup History", Width = 1100, Height = 550, StartPosition = FormStartPosition.CenterParent };
        var entries = routers.SelectMany(r => r.Backups.Select(b => new { Router = r.Name, Backup = b })).OrderByDescending(x => x.Backup.CreatedAt).ToList();
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            DataSource = entries.Select(x => new { x.Router, x.Backup.CreatedAt, x.Backup.Verification,
                Files = File.Exists(x.Backup.BackupPath) && File.Exists(x.Backup.ExportPath) ? "Present" : "Missing / moved / retention removed",
                x.Backup.BackupPath, x.Backup.ExportPath, x.Backup.BackupHash, x.Backup.ExportHash }).ToList() };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var open = Button("Open Folder", (_, _) =>
        {
            if (grid.CurrentRow is null) return;
            string? folder = Path.GetDirectoryName(entries[grid.CurrentRow.Index].Backup.BackupPath);
            if (!Directory.Exists(folder)) { MessageBox.Show("The backup folder is no longer present."); return; }
            try { Process.Start(new ProcessStartInfo("explorer.exe", folder!) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); }
        });
        var verify = Button("Verify Selected Files", async (_, _) =>
        {
            if (grid.CurrentRow is null) return;
            var backup = entries[grid.CurrentRow.Index].Backup;
            bar.Enabled = false;
            try
            {
                bool valid = await Task.Run(() => SelfUpdater.Hash(backup.BackupPath).Equals(backup.BackupHash, StringComparison.OrdinalIgnoreCase)
                    && SelfUpdater.Hash(backup.ExportPath).Equals(backup.ExportHash, StringComparison.OrdinalIgnoreCase));
                MessageBox.Show(valid ? "Both files match their saved SHA-256 hashes." : "Verification failed: file contents have changed.");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { if (!bar.IsDisposed) bar.Enabled = true; }
        });
        bar.Controls.AddRange([open, verify]); dialog.Controls.Add(grid); dialog.Controls.Add(bar); dialog.ShowDialog(this);
    }

    private string? AskArchivePassword(bool exporting)
    {
        using var dialog = new Form { Text = exporting ? "Encrypt configuration export" : "Decrypt configuration import", Width = 440, Height = 200, StartPosition = FormStartPosition.CenterParent };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown };
        panel.Controls.Add(new Label { AutoSize = true, Text = "Archive password (at least 12 characters for export)" });
        var password = new TextBox { Width = 370, UseSystemPasswordChar = true }; panel.Controls.Add(password);
        panel.Controls.Add(new Button { Text = "Continue", DialogResult = DialogResult.OK, AutoSize = true });
        dialog.Controls.Add(panel); return dialog.ShowDialog(this) == DialogResult.OK ? password.Text : null;
    }
    private async void ExportConfiguration(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        using var dialog = new SaveFileDialog { Filter = "Encrypted configuration|*.mtmconfig", FileName = "MikroTikManager-configuration.mtmconfig" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string? password = AskArchivePassword(true); if (password is null) return;
        SetBusy(true, "Encrypting configuration...");
        try
        {
            var bundle = new ConfigurationBundle(3, _routers.ToList(), _jobs.ToList(), _store.LoadSettings());
            byte[] encrypted = await Task.Run(() => ConfigurationArchive.Encrypt(bundle, password));
            await File.WriteAllBytesAsync(dialog.FileName, encrypted);
            MessageBox.Show("Encrypted configuration exported. Keep its password separately. Local backup files are not included.");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false, "Ready"); }
    }
    private async void ImportConfiguration(object? sender, EventArgs e)
    {
        if (_operationInProgress) return;
        using var dialog = new OpenFileDialog { Filter = "Encrypted configuration|*.mtmconfig" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string? password = AskArchivePassword(false); if (password is null) return;
        SetBusy(true, "Validating configuration...");
        try
        {
            if (new FileInfo(dialog.FileName).Length > 32 * 1024 * 1024) throw new InvalidDataException("Archive exceeds 32 MB.");
            byte[] bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var bundle = await Task.Run(() => ConfigurationArchive.Decrypt(bytes, password));
            if (MessageBox.Show($"Replace inventory and settings with {bundle.Routers.Count} routers and {bundle.Jobs.Count} schedule definitions?\n\nImported schedules remain disabled until activated in Schedules. Backup file paths refer to the original PC. The app will close; reopen it to load the imported settings.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (var router in bundle.Routers) { router.ApiStatus = "Not checked"; router.LastStatus = "Imported; not checked"; }
            foreach (var job in bundle.Jobs) { job.Id = Guid.NewGuid(); job.State = "Imported — disabled"; job.StartedAt = job.CompletedAt = null; }
            _store.ImportConfiguration(bundle);
            // Prevent the closing handler from writing the old in-memory inventory over the import.
            _suspendRouterSaves = true; _routers.Clear(); foreach (var router in bundle.Routers) _routers.Add(router);
            _jobs.Clear(); foreach (var job in bundle.Jobs) _jobs.Add(job); _suspendRouterSaves = false;
            SetBusy(false, "Imported"); Close();
        }
        catch (CryptographicException) { MessageBox.Show("Incorrect password or damaged archive. Nothing was imported."); }
        catch (Exception ex) { ShowError(ex); }
        finally { if (!IsDisposed) SetBusy(false, "Ready"); }
    }
}

internal static class ReleaseVersion
{
    internal static bool IsNewerSameMajor(string latest, string installed)
    {
        static (Version? Version, int Rank, int Revision) Parse(string text)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+\.\d+(?:\.\d+)?)(?:(beta|rc)(\d+))?(?:\s|$)");
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) return (null, 0, 0);
            return (new Version(version.Major, version.Minor, Math.Max(0, version.Build)), match.Groups[2].Value switch { "beta" => 0, "rc" => 1, _ => 2 }, int.TryParse(match.Groups[3].Value, out int revision) ? revision : 0);
        }
        var a = Parse(latest); var b = Parse(installed);
        if (a.Version is null || b.Version is null || a.Version.Major != b.Version.Major) return false;
        int compare = a.Version.CompareTo(b.Version);
        return compare > 0 || compare == 0 && (a.Rank > b.Rank || a.Rank == b.Rank && a.Revision > b.Revision);
    }
}
