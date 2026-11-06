namespace MikroTikManager;

internal static class HttpRecoveryRegressionTests
{
    internal static async Task RunAsync()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static ApiReply Row(string status, string? host = null) => new("!re", new Dictionary<string, string> {
            ["status"] = status, ["host"] = host ?? "", ["installed-version"] = "7.20", ["latest-version"] = "7.21" });
        var resolved = RouterUpdateRecovery.FromPing([Row("timeout", "192.0.2.1")]);
        Check(resolved.State == RouterDnsState.Resolved, "Resolved IP plus ICMP timeout is not DNS failure");
        Check(RouterUpdateRecovery.FromPing([Row("", "2001:db8::1")]).State == RouterDnsState.Resolved, "IPv6 DNS result accepted");
        Check(RouterUpdateRecovery.FromError("could not resolve dns name").State == RouterDnsState.Failed, "DNS error classified");
        Check(RouterUpdateRecovery.FromError("not enough permissions").State == RouterDnsState.Unverified, "Permissions do not prove DNS failure");
        Check(RouterUpdateRecovery.FromPing([Row("timeout")]).State == RouterDnsState.Unverified, "No resolved address cannot authorize reboot");

        int calls = 0, boots = 0, probes = 0;
        var recovery = new RouterUpdateRecovery();
        var stages = new List<string>();
        int answer = await recovery.RunAsync(() => ++calls == 1
                ? Task.FromException<int>(new InvalidOperationException("ERROR: bad HTTP response")) : Task.FromResult(42),
            () => { probes++; return Task.FromResult(resolved); },
            () => { boots++; return Task.CompletedTask; }, (s, _) => stages.Add(s), CancellationToken.None);
        Check(answer == 42 && calls == 2 && boots == 1 && probes == 1 && stages.Contains("HTTP retry"), "Resolve then one reboot then recheck");
        // Same object is retained by UpgradeEngine across automatic retry attempts.
        bool blocked = false;
        try { await recovery.RunAsync(() => Task.FromException<int>(new RouterOsApiException("bad http response")),
            () => Task.FromResult(resolved), () => { boots++; return Task.CompletedTask; }, (_, _) => { }, CancellationToken.None); }
        catch (InvalidOperationException ex) { blocked = ex.Message.Contains("persists"); }
        Check(blocked && boots == 1, "Automatic retries cannot reset the recovery reboot budget");

        foreach (var state in new[] { RouterDnsState.Failed, RouterDnsState.Unverified })
        {
            boots = 0; blocked = false;
            try { await new RouterUpdateRecovery().RunAsync(() => Task.FromException<int>(new InvalidOperationException("bad http response")),
                () => Task.FromResult(new RouterDnsResult(state, "DNS fixture")),
                () => { boots++; return Task.CompletedTask; }, (_, _) => { }, CancellationToken.None); }
            catch (InvalidOperationException ex) { blocked = ex.Message.Contains("not attempted"); }
            Check(blocked && boots == 0, "Failed or unverified DNS must not reboot");
        }
        using var cancelled = new CancellationTokenSource(); boots = 0;
        try { await new RouterUpdateRecovery().RunAsync(() => Task.FromException<int>(new InvalidOperationException("bad http response")),
            () => { cancelled.Cancel(); return Task.FromResult(resolved); },
            () => { boots++; return Task.CompletedTask; }, (_, _) => { }, cancelled.Token); }
        catch (OperationCanceledException) { }
        Check(boots == 0, "Cancellation after DNS prevents recovery reboot");

        // Exercise actual update-check/download polling with scripted API replies and recovery.
        string phase = ""; bool recovered = false; boots = 0;
        var commands = new List<string>();
        var updater = new RouterPackageUpdater((command, ct, args) => {
            commands.Add(command); ct.ThrowIfCancellationRequested();
            if (command.EndsWith("check-for-updates")) phase = "check";
            if (command.EndsWith("download")) phase = "download";
            IReadOnlyList<ApiReply> rows = command.EndsWith("print")
                ? [Row(phase == "check" ? "New version is available" : recovered ? "Downloaded, please reboot router to upgrade it" : "ERROR: bad http response")]
                : [];
            return Task.FromResult(rows);
        }, (_, _) => { }) { PollInterval = TimeSpan.FromMilliseconds(1) };
        var plan = await new RouterUpdateRecovery().RunAsync(() => updater.PrepareAsync("stable", CancellationToken.None),
            () => Task.FromResult(resolved), () => { boots++; recovered = true; return Task.CompletedTask; }, (_, _) => { }, CancellationToken.None);
        Check(plan.RequiresReboot && plan.Version == "7.21" && boots == 1 && commands.Count(c => c.EndsWith("download")) == 2,
            "HTTP download error requires DNS/recovery and successful re-download before installation");
        Check(!commands.Any(c => c.EndsWith("install") || c.EndsWith("reboot")), "Preparation cannot request installation or reboot itself");
        Check(!RouterPackageUpdater.DownloadReady("Downloading...") && !RouterPackageUpdater.DownloadReady("ERROR: downloaded package is corrupt"),
            "Only positive download completion permits installation reboot");
        blocked = false;
        try { RouterPackageUpdater.ThrowIfError("ERROR: bad http response"); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "Error overrides stale latest-version field");
        var stuck = new RouterPackageUpdater((_, _, _) => Task.FromResult<IReadOnlyList<ApiReply>>([Row("finding out latest version...")]), (_, _) => { })
            { CheckTimeout = TimeSpan.FromMilliseconds(30), PollInterval = TimeSpan.FromMilliseconds(1) };
        blocked = false;
        try { await stuck.PrepareAsync("stable", CancellationToken.None); } catch (TimeoutException) { blocked = true; }
        Check(blocked, "Update polling is bounded and fails without reboot");
    }
}
