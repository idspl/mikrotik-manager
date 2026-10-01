using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MikroTikManager;

internal static class SelfUpdater
{
    internal const string InstanceName = @"Global\Indigo.MikroTikManager.Application";
    private const string Repository = "https://github.com/idspl/mikrotik-manager/";

    public static async Task<string> DownloadAsync(string expectedVersion, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MikroTikManager-Updater/0.2.5");
        string json = await client.GetStringAsync("https://api.github.com/repos/idspl/mikrotik-manager/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (tag.TrimStart('v', 'V') != expectedVersion || root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidOperationException("The release changed. Check for updates again.");
        string Asset(string name)
        {
            var asset = root.GetProperty("assets").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            string prefix = Repository + "releases/download/" + tag + "/";
            if (!url.StartsWith(prefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out _))
                throw new InvalidOperationException("Unexpected release download address.");
            return url;
        }
        string checksum = await client.GetStringAsync(Asset("MikroTikManager.exe.sha256.txt"), ct);
        string hash = checksum.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z")) throw new InvalidOperationException("Invalid release checksum.");
        string directory = Path.Combine(Path.GetTempPath(), "MikroTikManager-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "MikroTikManager.exe");
        using var response = await client.GetAsync(Asset("MikroTikManager.exe"), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using (var output = File.Create(path))
        { await response.Content.CopyToAsync(output, ct); await output.FlushAsync(ct); }
        if (!Hash(path).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Checksum mismatch. The downloaded executable will not be installed.");
        // Helper is the currently running (known) build, not unverified code fetched separately.
        File.Copy(Environment.ProcessPath!, Path.Combine(directory, "UpdateHelper.exe"));
        return path;
    }

    internal static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    internal static void ReplaceVerified(string download, string target, string expectedHash, string stage, string backup)
    {
        if (target.Equals(download, StringComparison.OrdinalIgnoreCase) || !Hash(download).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update validation failed.");
        File.Copy(download, stage, false);
        if (!Hash(stage).Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Staged checksum mismatch.");
        File.Replace(stage, target, backup, true);
    }

    public static void StartReplacement(string downloaded)
    {
        string target = Environment.ProcessPath ?? throw new InvalidOperationException("Application path unavailable.");
        // Verify write permission before closing the working application. Do not silently elevate.
        string probe = Path.Combine(Path.GetDirectoryName(target)!, ".mikrotik-update-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { }
        File.Delete(probe);
        var start = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(downloaded)!, "UpdateHelper.exe")) { UseShellExecute = false };
        foreach (string value in new[] { "--apply-update", target, downloaded, Hash(downloaded), Environment.ProcessId.ToString() })
            start.ArgumentList.Add(value);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Could not start update helper.");
    }

    public static void Apply(string[] args)
    {
        string target = Path.GetFullPath(args[1]), download = Path.GetFullPath(args[2]);
        string backup = target + ".previous";
        string stage = target + ".new-" + Guid.NewGuid().ToString("N");
        bool replaced = false;
        using var mutex = new Mutex(false, InstanceName);
        bool held = false;
        try
        {
            try
            {
                using var parent = Process.GetProcessById(int.Parse(args[4]));
                if (!parent.WaitForExit(120000)) throw new InvalidOperationException("The application has not closed. Update aborted.");
            }
            catch (ArgumentException) { /* Parent already exited. */ }
            try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new InvalidOperationException("Another application or scheduled job is running. Update aborted.");
            ReplaceVerified(download, target, args[3], stage, backup);
            replaced = true;
            mutex.ReleaseMutex(); held = false;
            _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target)! })
                ?? throw new InvalidOperationException("Updated application could not be launched.");
        }
        catch (Exception ex)
        {
            // Do not overwrite files after releasing the lock: a scheduled job may have started.
            string recovery = replaced ? $"Previous executable retained at:\n{backup}" : "The original executable is unchanged.";
            MessageBox.Show($"Update did not finish: {ex.Message}\n\n{recovery}", "MikroTik Manager update", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (held) mutex.ReleaseMutex();
            try { if (File.Exists(stage)) File.Delete(stage); } catch { }
        }
    }
}
