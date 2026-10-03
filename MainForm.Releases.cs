using System.Diagnostics;

namespace MikroTikManager;

public sealed partial class MainForm
{
    private readonly ToolStripLabel _latestStable = new("Latest Stable: —");
    private readonly ToolStripLabel _latestLongTerm = new("Latest Long-term: —");
    private readonly ToolStripLabel _latestTesting = new("Latest Testing: —");
    private readonly ToolStripLabel _releaseChecked = new("Not checked") { ForeColor = Color.DimGray };
    private readonly ToolStripButton _refreshReleases = new("Refresh");
    private RouterOsReleases? _publishedReleases;
    private bool _refreshingReleases;
    private DateTime? _releaseCheckedAt;

    private ToolStrip BuildRouterOsReleaseStrip()
    {
        var strip = new ToolStrip { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Color.FromArgb(235, 242, 250), ShowItemToolTips = true };
        var source = new ToolStripButton("MikroTik releases");
        source.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(RouterOsReleases.Source) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        };
        _refreshReleases.Click += async (_, _) => await RefreshRouterOsReleasesAsync();
        strip.Items.AddRange([_latestStable, new ToolStripSeparator(), _latestLongTerm,
            new ToolStripSeparator(), _latestTesting, new ToolStripSeparator(), _refreshReleases, source, _releaseChecked]);
        foreach (var label in new[] { _latestStable, _latestLongTerm, _latestTesting })
            label.ToolTipText = "Published on mikrotik.com. Informational; the router checks its own applicable update at upgrade time.";
        return strip;
    }

    private async Task RefreshRouterOsReleasesAsync()
    {
        if (_refreshingReleases || IsDisposed) return;
        _refreshingReleases = true;
        _refreshReleases.Enabled = false;
        _releaseChecked.Text = "Checking MikroTik…";
        try
        {
            RouterOsReleases releases = await RouterOsReleases.FetchAsync();
            if (IsDisposed || Disposing) return;
            _publishedReleases = releases;
            _latestStable.Text = "Latest Stable: " + releases.Stable;
            _latestLongTerm.Text = "Latest Long-term: " + releases.LongTerm;
            _latestTesting.Text = "Latest Testing: " + releases.Testing;
            _releaseCheckedAt = DateTime.Now;
            _releaseChecked.Text = $"Checked {_releaseCheckedAt:dd MMM HH:mm}";
        }
        catch (Exception)
        {
            if (IsDisposed || Disposing) return;
            _releaseChecked.Text = _releaseCheckedAt is null ? "Unavailable — retry Refresh" : $"Stale — last checked {_releaseCheckedAt:dd MMM HH:mm}";
        }
        finally
        {
            _refreshingReleases = false;
            if (!IsDisposed && !Disposing) { _refreshReleases.Enabled = true; RefreshDashboard(); }
        }
    }
}

