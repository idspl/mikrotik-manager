namespace MikroTikManager;

public sealed partial class MainForm
{
    private void VerifyNewMaintenanceDialogs(float scale)
    {
        void Capture(Form form, string name)
        {
            ApplyLayoutTestScale(form, scale); Application.DoEvents();
            form.Size = new Size(Math.Min(form.Width, Screen.FromControl(form).WorkingArea.Width - 24), Math.Min(form.Height, Screen.FromControl(form).WorkingArea.Height - 24));
            form.PerformLayout(); Application.DoEvents();
            foreach (var footer in form.Controls.OfType<FlowLayoutPanel>().Where(p => p.Dock == DockStyle.Bottom))
                foreach (Control button in footer.Controls)
                    if (!button.Visible || form.PointToClient(button.PointToScreen(new Point(button.Width, button.Height))).Y > form.ClientSize.Height)
                        throw new Exception("New dialog footer clips: " + name + " / " + scale);
            if (scale == 1.5F)
            {
                using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText("ui-150-" + name + ".base64", Convert.ToBase64String(stream.ToArray()));
            }
        }
        void Modal(Action show, string name)
        {
            Exception? error = null; bool found = false;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) => {
                var dialog = Application.OpenForms.OfType<DpiDialog>().FirstOrDefault(); if (dialog is null) return;
                timer.Stop(); found = true;
                try { Capture(dialog, name); } catch (Exception ex) { error = ex; }
                finally { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
            };
            timer.Start(); show(); timer.Stop();
            if (error is not null) throw error;
            if (!found) throw new Exception("New dialog did not open: " + name);
        }
        Modal(() => ConfigureRecovery(null, EventArgs.Empty), "recovery-settings");
        Modal(() => EditProfiles(null, EventArgs.Empty), "maintenance-profiles");
        using var activity = new ServiceActivity(_store, "Scheduled maintenance fixture", _routers.Take(3));
        activity.Update(new(_routers[0].Id, "RouterOS packages", 1, 55, "Running", "Downloading update"));
        using var monitor = new ServiceActivityForm(_store);
        monitor.Show(this); Capture(monitor, "service-monitor"); monitor.Close(); activity.Finish("Fixture complete");
        var backupRow = _backupGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        if (backupRow?.DataBoundItem is BackupCoverageRow row)
        {
            var router = _routers.Single(r => r.Id == row.Id); _backupGrid.ClearSelection(); backupRow.Selected = true;
            router.Backups.Add(new(DateTime.Now, "fixture.backup", "fixture.rsc", "hash", "hash", "fixture") { Model = "RB750Gr3", RouterOsVersion = "7.24.4", SerialNumber = "fixture" });
            Modal(() => ShowGuidedRestore(null, EventArgs.Empty), "guided-restore"); router.Backups.RemoveAt(router.Backups.Count - 1);
        }
    }
}
