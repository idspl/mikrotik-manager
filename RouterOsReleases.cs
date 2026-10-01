using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MikroTikManager;

internal sealed record RouterOsReleases(string Stable, string LongTerm, string Testing)
{
    internal const string Source = "https://mikrotik.com/download/routeros";

    internal static async Task<RouterOsReleases> FetchAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MikroTikManager");
        string html = await client.GetStringAsync(Source).ConfigureAwait(false);
        return Parse(html);
    }

    internal static RouterOsReleases Parse(string html)
    {
        // Read the structured state of the RouterOS component, not CHR or changelog history.
        foreach (Match match in Regex.Matches(html, "wire:snapshot=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
            var root = doc.RootElement;
            if (!root.TryGetProperty("memo", out var memo) || !memo.TryGetProperty("name", out var name)
                || name.GetString() != "components.software.router-OS") continue;
            var data = root.GetProperty("data");
            string ReadVersion(string key)
            {
                string value = data.GetProperty(key).GetString() ?? "";
                if (value.Length == 0) return "Not listed";
                if (!Regex.IsMatch(value, @"\A\d+\.\d+(?:\.\d+)?(?:(?:beta|rc)\d+)?\z"))
                    throw new FormatException("Unexpected RouterOS version format.");
                return value;
            }
            var longTerm = new List<(Version Number, string Text)>();
            foreach (var entry in data.GetProperty("releases")[0].EnumerateArray())
            {
                var release = entry[0];
                if (release.GetProperty("archived").GetBoolean()
                    || !release.GetProperty("channels")[0].GetProperty("longTerm").GetBoolean()) continue;
                string value = release.GetProperty("version").GetString() ?? "";
                if (Version.TryParse(value, out var number)) longTerm.Add((number, value));
            }
            return new(ReadVersion("latestStableVersion"),
                longTerm.OrderByDescending(x => x.Number).Select(x => x.Text).FirstOrDefault() ?? "Not listed",
                ReadVersion("latestTestingVersion"));
        }
        throw new FormatException("RouterOS release information was not found on the MikroTik website.");
    }
}
