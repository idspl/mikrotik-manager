namespace MikroTikManager;

public sealed partial class MainForm
{
    internal void RenderSmokeViews()
    {
        // Called only by --self-test with an isolated temporary SecureStore.
        _suspendRouterSaves = true; _routers.RaiseListChangedEvents = false;
        for (int i = 0; i < 1000; i++) _routers.Add(new RouterRecord {
            Name = $"Client-{i + 1:0000}", Host = $"192.0.{2 + i / 250}.{1 + i % 250}", Site = i % 2 == 0 ? "Davanagere" : "Shivamogga",
            Group = "Client-end", Tags = "Customer", Model = "RB750Gr3", ApiStatus = i % 7 == 0 ? "Failed" : "Online", RouterOsVersion = "7.24.4", FirmwareVersion = "7.24.4", LastStatus = "Snapshot fixture" });
        _routers.RaiseListChangedEvents = true; _routers.ResetBindings();
        RefreshDashboard();
        Show(); Application.DoEvents();
        void Capture(string name)
        {
            PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
            using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText("ui-" + name + ".base64", Convert.ToBase64String(stream.ToArray()));
        }
        Capture("dashboard");
        _tabs.SelectedTab = _routerPage; _routerGrid.ClearSelection(); _routerGrid.Rows[20].Selected = true;
        _routerGrid.FirstDisplayedScrollingRowIndex = 12;
        int scroll = _routerGrid.FirstDisplayedScrollingRowIndex;
        var selected = _routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToArray();
        PrepareProgressRows(_routers.Take(10));
        for (int i = 0; i < 1000; i++) UpdateProgress(new(_routers[i % 10].Id, "Stability hold", 1, 90, "Running", "UI load fixture"));
        FlushUiUpdates(); Application.DoEvents();
        if (_routerGrid.FirstDisplayedScrollingRowIndex != scroll || !_routerGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).SequenceEqual(selected))
            throw new Exception("Progress changed inventory scroll or selection.");
        Capture("devices");
        _tabs.SelectedTab = _maintenancePage; Capture("progress");
        _tabs.SelectedTab = _schedulesPage; Capture("schedules");
        Hide();
    }
}
