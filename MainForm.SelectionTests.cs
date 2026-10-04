namespace MikroTikManager;

public sealed partial class MainForm
{
    private void VerifyBackupSelectionAndContinue()
    {
        if (!_backupGrid.MultiSelect) throw new Exception("Backups must allow multi-selection.");
        var inventoryIds = SelectedRouters().Select(r => r.Id).ToArray();
        _backupGrid.ClearSelection();
        _backupGrid.Rows[2].Selected = true; _backupGrid.Rows[5].Selected = true;
        var selected = SelectedBackupRouters().Select(r => r.Id).ToHashSet();
        if (selected.Count != 2 || !selected.Contains(((BackupCoverageRow)_backupGrid.Rows[2].DataBoundItem).Id)
            || !SelectedRouters().Select(r => r.Id).SequenceEqual(inventoryIds))
            throw new Exception("Backup selection must include every selected device without changing inventory selection.");
        _backupGrid.FirstDisplayedScrollingRowIndex = 2;
        int scroll = _backupGrid.FirstDisplayedScrollingRowIndex;
        var refresh = RefreshBackupCoverageAsync();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!refresh.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(10); }
        if (!refresh.IsCompleted) throw new Exception("Backup refresh did not complete.");
        refresh.GetAwaiter().GetResult();
        if (!selected.SetEquals(SelectedBackupRouters().Select(r => r.Id)) || _backupGrid.FirstDisplayedScrollingRowIndex != scroll)
            throw new Exception("Coverage refresh must preserve backup multi-selection and scroll.");
        _backupGrid.SelectAll();
        if (SelectedBackupRouters().Count != _routers.Count) throw new Exception("Backup Select All lost devices.");
        _backupGrid.ClearSelection();
        if (SelectedBackupRouters().Count != 0) throw new Exception("Cleared selection must not fall back to the current row.");

        var previousIds = _lastUpgradeRouterIds;
        var previousRows = _progressRows.ToList();
        try
        {
            _lastUpgradeRouterIds = _routers.Take(3).Select(r => r.Id).ToList();
            _progressRows.Clear();
            _progressRows.Add(new() { RouterId = _lastUpgradeRouterIds[0], Result = "Failed", Stage = "Preflight", Attempt = 1 });
            _progressRows.Add(new() { RouterId = _lastUpgradeRouterIds[1], Result = "Pending", Stage = "Queued", Attempt = 0 });
            _progressRows.Add(new() { RouterId = _lastUpgradeRouterIds[2], Result = "Completed", Stage = "Complete", Attempt = 1 });
            UpdateSkipFailedButton();
            if (!_skipFailedContinue.Enabled || !PendingAfterFailures(_lastUpgradeRouterIds, _progressRows).SequenceEqual([_lastUpgradeRouterIds[1]]))
                throw new Exception("Skip failed must enable for only unstarted upgrade devices.");
            _operationInProgress = true; UpdateSkipFailedButton();
            if (_skipFailedContinue.Enabled) throw new Exception("Continue must not overlap an active job.");
            _operationInProgress = false; _lastUpgradeRouterIds = []; UpdateSkipFailedButton();
            if (_skipFailedContinue.Enabled) throw new Exception("Backup results must not enable upgrade continuation.");
            if (_skipFailedContinue.FlatStyle != FlatStyle.Flat || _skipFailedContinue.MinimumSize.Height < 32
                || _skipFailedContinue.Text.Contains('&')) throw new Exception("Continue button must use the standard readable style.");
        }
        finally
        {
            _operationInProgress = false; _lastUpgradeRouterIds = previousIds;
            _progressRows.Clear(); foreach (var row in previousRows) _progressRows.Add(row);
            UpdateSkipFailedButton();
        }
    }
}
