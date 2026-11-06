using System.Net;

namespace MikroTikManager;

internal enum RouterDnsState { Resolved, Failed, Unverified }
internal sealed record RouterDnsResult(RouterDnsState State, string Detail);

// One instance per router run, shared across package passes and automatic retries.
internal sealed class RouterUpdateRecovery
{
    internal const string UpdateHost = "upgrade.mikrotik.com";
    internal bool RebootUsed { get; private set; }
    internal static bool IsBadHttp(string text) => text.Contains("bad http response", StringComparison.OrdinalIgnoreCase);

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation, Func<Task<RouterDnsResult>> diagnose,
        Func<Task> reboot, Action<string, string> report, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return await operation(); }
            catch (Exception ex) when (ex is not OperationCanceledException && IsBadHttp(ex.Message))
            {
                if (RebootUsed)
                    throw new InvalidOperationException("Bad HTTP response persists after the one recovery reboot. No further automatic reboot; check router connectivity, firewall/proxy and MikroTik update service.", ex);
                report("DNS check", "Bad HTTP response; checking " + UpdateHost + " using the router's resolver");
                var dns = await diagnose();
                report("DNS result", dns.Detail);
                if (dns.State != RouterDnsState.Resolved)
                    throw new InvalidOperationException(dns.Detail + ". Upgrade stopped; recovery reboot was not attempted.", ex);
                ct.ThrowIfCancellationRequested();
                // Consume before sending the command: an uncertain disconnect must not cause a second reboot.
                RebootUsed = true;
                report("HTTP recovery", "DNS resolved. Performing the single recovery reboot, then checking updates again.");
                await reboot();
                ct.ThrowIfCancellationRequested();
                report("HTTP retry", "Router returned; retrying update check and package download");
            }
        }
    }

    internal static RouterDnsResult FromPing(IReadOnlyList<ApiReply> replies)
    {
        var hosts = replies.Where(r => r.Type == "!re")
            .Select(r => r.Attributes.GetValueOrDefault("host") ?? "")
            .Where(h => IPAddress.TryParse(h, out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
            .Distinct().ToArray();
        if (hosts.Length > 0)
            return new(RouterDnsState.Resolved, "Router DNS resolved " + UpdateHost + ": " + string.Join(", ", hosts) + "; ICMP replies are not required");
        string status = string.Join("; ", replies.Select(r => r.Attributes.GetValueOrDefault("status")).Where(s => !string.IsNullOrWhiteSpace(s)));
        return FromError(status.Length > 0 ? status : "Probe returned no resolved IP address");
    }

    internal static RouterDnsResult FromError(string error)
    {
        bool dns = error.Contains("could not resolve", StringComparison.OrdinalIgnoreCase)
            || error.Contains("cannot resolve", StringComparison.OrdinalIgnoreCase)
            || error.Contains("dns name", StringComparison.OrdinalIgnoreCase)
            || error.Contains("dns server", StringComparison.OrdinalIgnoreCase);
        return new(dns ? RouterDnsState.Failed : RouterDnsState.Unverified,
            (dns ? "Router DNS resolution failed for " : "Router DNS could not be verified for ") + UpdateHost + ": " + error);
    }
}
