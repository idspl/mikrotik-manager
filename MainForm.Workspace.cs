using System.Collections.Concurrent;
using System.Text.Json;

namespace MikroTikManager;

internal sealed class SmoothGrid : DataGridView
{
    public SmoothGrid() { DoubleBuffered = true; AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; }
}

public sealed partial class MainForm
{
    private readonly ConcurrentDictionary<Guid, MaintenanceProgressUpdate> _pendingProgress = new();
    private readonly ConcurrentQueue<string> _pendingLog = new();
    private readonly Label _selectionLabel = new() { Text = "Select devices to begin", AutoSize = true, Padding = new Padding(6, 8, 18, 0) };
    private readonly Label _runTotals = new() { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(12, 8, 0, 0), BackColor = Color.FromArgb(232, 241, 252) };
    private readonly ProgressBar _runOverall = new() { Dock = DockStyle.Bottom, Height = 7, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly NumericUpDown _scheduleConcurrency = new() { Minimum = 0, Maximum = 10000, Value = 1, Width = 65 };
    private int _batchConcurrency = 5, _batchFailureLimit;
    private bool _inventoryDirty;

    private void UpdateProgress(MaintenanceProgressUpdate update) => _pendingProgress[update.RouterId] = update;
    private void RequestInventoryPaint() => _inventoryDirty = true;
    private void StartUiPump()
    {
        _maintenancePage.Controls.Add(_runTotals);
        _maintenancePage.Controls.Add(_runOverall);
        var timer = new System.Windows.Forms.Timer(_components) { Interval = 200 };
        timer.Tick += (_, _) => FlushUiUpdates(); timer.Start();
    }
    private void FlushUiUpdates()
    {
        if (IsDisposed) return;
        foreach (var id in _pendingProgress.Keys)
            if (_pendingProgress.TryRemove(id, out var update)) ApplyProgress(update);
        if (_inventoryDirty)
        {
            _inventoryDirty = false;
            int first = _routerGrid.FirstDisplayedScrollingRowIndex;
            if (first >= 0)
                for (int i = first; i < _routerGrid.Rows.Count; i++)
                {
                    var row = _routerGrid.Rows[i]; if (!row.Visible) continue;
                    if (!row.Displayed) break;
                    _routerGrid.InvalidateRow(i);
                }
        }
        if (!_pendingLog.IsEmpty)
        {
            bool follow = _log.SelectionStart + _log.SelectionLength >= _log.TextLength - 1;
            int selection = _log.SelectionStart, length = _log.SelectionLength;
            var lines = new List<string>();
            while (lines.Count < 250 && _pendingLog.TryDequeue(out var line)) lines.Add(line);
            if (_log.TextLength > 250000 && follow) _log.Clear();
            _log.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine);
            if (follow) { _log.SelectionStart = _log.TextLength; _log.ScrollToCaret(); }
            else _log.Select(selection, length);
        }
        int done = _progressRows.Count(r => r.Result == "Completed"), failed = _progressRows.Count(r => r.Result == "Failed"), queued = _progressRows.Count(r => r.Result == "Pending");
        int percent = _progressRows.Count == 0 ? 0 : (int)_progressRows.Average(r => r.Result is "Completed" or "Failed" or "Cancelled" ? 100 : Math.Clamp(r.Progress, 0, 100));
        _runOverall.Value = percent;
        _runTotals.Text = $"{percent}% (stage estimate)   •   {queued} queued   •   {_progressRows.Count(r => r.Result is "Running" or "Retrying")} running   •   {done} completed   •   {failed} failed";
    }

    private Control BuildNavigation()
    {
        var side = new Panel { Name = "Navigation", Dock = DockStyle.Left, Width = 180, BackColor = Color.FromArgb(23, 39, 62), Padding = new Padding(12) };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var list = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = Padding.Empty, Margin = Padding.Empty };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        list.Controls.Add(new Label { Text = "INDIGO\nMikroTik\nManager", ForeColor = Color.White, Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16), Padding = new Padding(4, 12, 0, 8) });
        var buttons = new List<(Button Button, TabPage Page)>();
        foreach (TabPage page in _tabs.TabPages)
        {
            string title = page.Text == "Maintenance Progress" ? "Maintenance" : page.Text;
            var button = new Button { Text = title, Dock = DockStyle.Fill, AutoSize = true, MinimumSize = new Size(0, 42), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 8, 8, 8), ForeColor = Color.White, BackColor = side.BackColor, Margin = new Padding(0, 3, 0, 3), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => _tabs.SelectedTab = page;
            list.Controls.Add(button); buttons.Add((button, page));
        }
        void Highlight() { foreach (var item in buttons) item.Button.BackColor = item.Page == _tabs.SelectedTab ? Color.FromArgb(45, 99, 170) : side.BackColor; }
        _tabs.SelectedIndexChanged += (_, _) => Highlight(); Highlight();
        scroll.Controls.Add(list); side.Controls.Add(scroll);
        side.Controls.Add(new Label { Text = "Made for MikroTik\nv0.5.3 • Open source", Dock = DockStyle.Bottom, AutoSize = true, ForeColor = Color.LightSteelBlue, Padding = new Padding(4, 8, 0, 0) });
        return side;
    }

    private Control BuildSelectionBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), BackColor = Color.FromArgb(232, 241, 252) };
        bar.Controls.Add(_selectionLabel);
        bar.Controls.Add(Button("Upgrade Selected", RunNow)); bar.Controls.Add(Button("Backup", BackupSelectedRouters));
        bar.Controls.Add(Button("Check API", CheckApiStatusSelected)); bar.Controls.Add(Button("Site / Tags", SetSiteTags));
        _routerGrid.SelectionChanged += (_, _) => _selectionLabel.Text = $"{_routerGrid.SelectedRows.Count} device(s) selected";
        return bar;
    }

    private string ColumnLayoutPath => Path.Combine(_store.RootDirectory, "inventory-columns.json");
    private sealed record ColumnLayout(string Property, int Width, bool Visible);
    private void RestoreColumns()
    {
        foreach (DataGridViewColumn c in _routerGrid.Columns)
            c.Visible = c.DataPropertyName is not (nameof(RouterRecord.ApiPort) or nameof(RouterRecord.Username) or nameof(RouterRecord.LastCheckedAt) or nameof(RouterRecord.LastBackupAt) or nameof(RouterRecord.Tags));
        try
        {
            if (!File.Exists(ColumnLayoutPath)) return;
            var saved = JsonSerializer.Deserialize<List<ColumnLayout>>(File.ReadAllText(ColumnLayoutPath)) ?? [];
            foreach (var entry in saved)
                foreach (DataGridViewColumn c in _routerGrid.Columns)
                    if (c.DataPropertyName == entry.Property) { c.FillWeight = Math.Clamp(entry.Width, 55, 1200); c.Visible = entry.Visible; }
        }
        catch { }
    }
    private void SaveColumnLayout()
    {
        try { File.WriteAllText(ColumnLayoutPath, JsonSerializer.Serialize(_routerGrid.Columns.Cast<DataGridViewColumn>().Select(c => new ColumnLayout(c.DataPropertyName, c.Width, c.Visible)))); } catch { }
    }
    private void ChooseColumns(object? sender, EventArgs e)
    {
        using var dialog = new DpiDialog { Text = "Inventory columns", Width = 360, Height = 490, StartPosition = FormStartPosition.CenterParent };
        var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        foreach (DataGridViewColumn c in _routerGrid.Columns) choices.Items.Add(c.HeaderText, c.Visible);
        var save = new Button { Text = "Apply", Dock = DockStyle.Bottom, Height = 38, DialogResult = DialogResult.OK };
        dialog.Controls.Add(choices); dialog.Controls.Add(save);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        for (int i = 0; i < choices.Items.Count; i++) _routerGrid.Columns[i].Visible = choices.GetItemChecked(i) || i == 0;
        SaveColumnLayout();
    }
    private void SetSiteTags(object? sender, EventArgs e)
    {
        if (_operationInProgress || _updateInProgress) return;
        var selected = SelectedRouters(); if (selected.Count == 0) { MessageBox.Show("Select devices first."); return; }
        using var dialog = new DpiDialog { Text = $"Site and tags — {selected.Count} devices", Width = 500, Height = 260, StartPosition = FormStartPosition.CenterParent };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2 };
        var site = new TextBox { Width = 290, Text = selected.Count == 1 ? selected[0].Site : "" };
        var tags = new TextBox { Width = 290, Text = selected.Count == 1 ? selected[0].Tags : "", PlaceholderText = "Client-end, Critical, Switch" };
        AddRow(layout, "Site", site); AddRow(layout, "Tags", tags);
        AddRow(layout, "", new Label { Text = "Replaces site/tags on selected devices. Blank clears the field. Upgrade groups are separate.", AutoSize = true, MaximumSize = new Size(320, 60) });
        AddRow(layout, "", new Button { Text = "Apply", DialogResult = DialogResult.OK }); dialog.Controls.Add(layout);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var router in selected) { router.Site = site.Text.Trim(); router.Tags = tags.Text.Trim(); }
        SaveRouters(); RequestInventoryPaint();
    }
}
