using System.Text.RegularExpressions;

namespace MikroTikManager;

internal static class ConfigDiff
{
    internal sealed record Line(string Kind, string Text);
    internal static string[] Normalize(string text) => text.Replace("\r\n", "\n").Split('\n')
        .Select(l => Regex.Replace(l, @"^# (?:\d{4}-\d{2}-\d{2}|[a-zA-Z]{3}/\d{1,2}/\d{4})\s+\d{2}:\d{2}:\d{2}\s+by RouterOS", "# by RouterOS")).ToArray();
    internal static List<Line> Compare(string before, string after)
    {
        string[] a = Normalize(before), b = Normalize(after); var output = new List<Line>();
        if (a.Length > 20000 || b.Length > 20000) throw new InvalidOperationException("Export is over 20,000 lines; compare it in an external editor.");
        int prefix = 0; while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) { output.Add(new(" ", a[prefix])); prefix++; }
        int ae = a.Length, be = b.Length;
        while (ae > prefix && be > prefix && a[ae - 1] == b[be - 1]) { ae--; be--; }
        int n = ae - prefix, m = be - prefix;
        if ((long)n * m > 2_000_000)
        {
            // Bound memory/time for large exports; unmatched middle is displayed as replacement.
            for (int i = prefix; i < ae; i++) output.Add(new("−", a[i]));
            for (int j = prefix; j < be; j++) output.Add(new("+", b[j]));
        }
        else
        {
            var lengths = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--) for (int j = m - 1; j >= 0; j--)
                lengths[i, j] = a[prefix + i] == b[prefix + j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[prefix + x] == b[prefix + y]) { output.Add(new(" ", a[prefix + x++])); y++; }
                else if (x < n && (y == m || lengths[x + 1, y] >= lengths[x, y + 1])) output.Add(new("−", a[prefix + x++]));
                else output.Add(new("+", b[prefix + y++]));
            }
        }
        for (int i = ae; i < a.Length; i++) output.Add(new(" ", a[i]));
        return output;
    }
    internal static string Display(string line, bool reveal) => reveal || string.IsNullOrWhiteSpace(line) ? line : "[configuration content hidden]";
}
