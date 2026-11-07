using System.Text.Json;
namespace MikroTikManager;

internal sealed record ServiceActivityState(Guid Session, string Job, DateTime UpdatedUtc, bool Running,
    string State, List<MaintenanceProgressRow> Devices);
internal sealed record ServiceControlRequest(Guid Session, string Action, DateTime CreatedUtc);

// Installed mode's data directory is restricted to SYSTEM and Administrators.
// This companion never reads or writes mutable inventory or scheduler state.
internal sealed class ServiceActivity : IDisposable
{
    private readonly SecureStore _store;
    private readonly object _gate = new();
    private ServiceActivityState _state;
    private readonly CancellationTokenSource _pollStop = new();
    private readonly Task _poll;
    internal Action? StopNewStarts { get; set; }
    internal Action? Cancel { get; set; }
    private static string PathFor(SecureStore store) => Path.Combine(store.RootDirectory, "service-activity.json");
    internal ServiceActivity(SecureStore store, string job, IEnumerable<RouterRecord> routers)
    {
        _store = store;
        _state = new(Guid.NewGuid(), job, DateTime.UtcNow, true, "Starting", routers.Select(r => new MaintenanceProgressRow { RouterId = r.Id, Router = r.Name, Address = r.Host }).ToList());
        Publish(); _poll = Task.Run(PollAsync);
    }
    internal static T? Read<T>(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return JsonSerializer.Deserialize<T>(stream); }
        catch { return default; }
    }
    internal static ServiceActivityState? Read(SecureStore store) => Read<ServiceActivityState>(PathFor(store));
    internal static bool Accept(ServiceControlRequest request, ServiceActivityState state, DateTime now) => state.Running && request.Session == state.Session
        && request.CreatedUtc <= now.AddSeconds(5) && now - request.CreatedUtc <= TimeSpan.FromSeconds(30)
        && request.Action is "Stop new starts" or "Cancel";
    internal static void Request(SecureStore store, ServiceActivityState state, string action)
    {
        if (!InstalledMode.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        string path = Path.Combine(store.RootDirectory, "service-command-" + Guid.NewGuid().ToString("N") + ".json");
        Atomic(path, new ServiceControlRequest(state.Session, action, DateTime.UtcNow));
    }
    private static void Atomic<T>(string path, T value)
    {
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private void Publish() { _state = _state with { UpdatedUtc = DateTime.UtcNow }; Atomic(PathFor(_store), _state); }
    internal void Update(MaintenanceProgressUpdate update)
    {
        lock (_gate)
        {
            var row = _state.Devices.FirstOrDefault(r => r.RouterId == update.RouterId);
            if (row is null) return;
            row.Stage = update.Stage; row.Result = update.Result; row.Progress = update.Progress; row.Attempt = update.Attempt; row.Message = update.Message;
            Publish();
        }
    }
    private async Task PollAsync()
    {
        try
        {
            while (!_pollStop.IsCancellationRequested)
            {
                await Task.Delay(1000, _pollStop.Token);
                lock (_gate)
                {
                    foreach (var path in Directory.EnumerateFiles(_store.RootDirectory, "service-command-*.json").Take(100))
                    {
                        var request = Read<ServiceControlRequest>(path);
                        try { File.Delete(path); } catch { continue; }
                        if (request is null || !Accept(request, _state, DateTime.UtcNow)) continue;
                        if (request.Action == "Cancel") Cancel?.Invoke(); else StopNewStarts?.Invoke();
                        _state = _state with { State = request.Action == "Cancel" ? "Cancellation requested; commands already sent cannot be undone" : "Stop requested; active devices will finish" };
                    }
                    Publish();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SchedulerWindowsService.Log(_store, "Activity monitor: " + ex.Message); }
    }
    internal void Finish(string state) { lock (_gate) { _state = _state with { Running = false, State = state }; Publish(); } }
    public void Dispose() { _pollStop.Cancel(); _poll.GetAwaiter().GetResult(); _pollStop.Dispose(); }
}

internal sealed class ServiceActivityForm : DpiDialog
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    internal ServiceActivityForm(SecureStore store)
    {
        Text = "MikroTik Manager — Background maintenance"; Width = 1180; Height = 700; StartPosition = FormStartPosition.CenterScreen;
        var status = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12) };
        ServiceActivityState? current = null;
        var stop = new Button { Text = "Stop New Starts", AutoSize = true }; var cancel = new Button { Text = "Cancel Job…", AutoSize = true };
        void Request(string action)
        {
            if (current is null) return;
            if (MessageBox.Show(this, action + "? Active reboot/upgrade commands cannot be undone.", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { ServiceActivity.Request(store, current, action); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }
        stop.Click += (_, _) => Request("Stop new starts"); cancel.Click += (_, _) => Request("Cancel");
        var close = new Button { Text = "Close Monitor", AutoSize = true }; close.Click += (_, _) => Close();
        bar.Controls.AddRange([stop, cancel, close]); Controls.Add(grid); Controls.Add(status); Controls.Add(bar);
        void RefreshState()
        {
            var state = ServiceActivity.Read(store); if (state is null) { status.Text = "Waiting for background job details…"; stop.Enabled = cancel.Enabled = false; return; }
            bool fresh = DateTime.UtcNow - state.UpdatedUtc < TimeSpan.FromSeconds(10);
            status.Text = $"{state.Job} • {state.State} • Updated {state.UpdatedUtc.ToLocalTime():T}\n" +
                (!fresh ? "Connection stale — controls disabled. " : "") + "Close this monitor and reopen the app after the job finishes to edit inventory or schedules.";
            stop.Enabled = cancel.Enabled = fresh && state.Running;
            var selected = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (r.DataBoundItem as MaintenanceProgressRow)?.RouterId).ToHashSet();
            int first = grid.FirstDisplayedScrollingRowIndex;
            // Avoid replacing row objects when only the heartbeat changed.
            if (current is null || JsonSerializer.Serialize(current.Devices) != JsonSerializer.Serialize(state.Devices))
            {
                grid.DataSource = state.Devices; grid.Columns[nameof(MaintenanceProgressRow.RouterId)].Visible = false;
                grid.ClearSelection(); foreach (DataGridViewRow row in grid.Rows) if (row.DataBoundItem is MaintenanceProgressRow item) row.Selected = selected.Contains(item.RouterId);
                if (first >= 0 && first < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = first;
            }
            current = state;
        }
        _timer.Tick += (_, _) => RefreshState(); Shown += (_, _) => { RefreshState(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Dispose();
    }
}
