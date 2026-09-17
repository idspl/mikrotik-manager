using System.Reflection;
using System.Text.Json;

namespace MikroTikManager;

public static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/idspl/mikrotik-manager/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MikroTikManager/0.2.0");
        using HttpResponseMessage response = await client.GetAsync(LatestReleaseApi, ct);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        string tag = document.RootElement.GetProperty("tag_name").GetString() ?? "";
        string url = document.RootElement.GetProperty("html_url").GetString() ?? "https://github.com/idspl/mikrotik-manager/releases/latest";
        string versionText = tag.Trim().TrimStart('v', 'V');
        Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        bool newer = Version.TryParse(versionText, out Version? latest) && latest > current;
        return new UpdateCheckResult(versionText, url, newer);
    }
}
