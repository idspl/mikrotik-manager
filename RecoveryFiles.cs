using System.Security.Cryptography;
namespace MikroTikManager;

internal static class RecoveryFiles
{
    internal static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length == 0) throw new InvalidDataException("Empty recovery file.");
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
    internal static async Task VerifyAsync(BackupHistoryEntry entry, CancellationToken ct)
    {
        foreach (var pair in new[] { (entry.BackupPath, entry.BackupHash), (entry.ExportPath, entry.ExportHash) })
            if (!string.Equals(await HashAsync(pair.Item1, ct), pair.Item2, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup integrity check failed: " + Path.GetFileName(pair.Item1));
    }
    internal static bool IsLinked(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    internal static void NoLinks(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Recovery folders must not contain symbolic links or junctions.");
    }
    internal static async Task<string> CopyOffsiteAsync(string source, string destinationRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot)) return "Offsite copy disabled";
        if (!Path.IsPathFullyQualified(destinationRoot)) throw new IOException("Offsite folder must be an absolute local or UNC path.");
        NoLinks(source); NoLinks(destinationRoot);
        string destination = Path.Combine(destinationRoot, Path.GetFileName(source));
        string fullSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((Path.GetFullPath(destination) + Path.DirectorySeparatorChar).StartsWith(fullSource, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Offsite destination must be outside the source backup folder.");
        Directory.CreateDirectory(destinationRoot);
        string temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (string file in Directory.EnumerateFiles(source))
            {
                ct.ThrowIfCancellationRequested();
                if (IsLinked(file)) throw new IOException("Linked recovery file rejected.");
                string name = Path.GetFileName(file);
                if (!(name.EndsWith(".backup") || name.EndsWith(".rsc") || name == "BackupManifest.csv")) continue;
                string copy = Path.Combine(temporary, name);
                await using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
                await using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    await input.CopyToAsync(output, ct);
                if (await HashAsync(file, ct) != await HashAsync(copy, ct)) throw new IOException("Offsite checksum mismatch: " + name);
            }
            // Never replace a previous copy or present a partial directory as complete.
            Directory.Move(temporary, destination);
            return "Verified offsite copy: " + destination;
        }
        catch { try { Directory.Delete(temporary, true); } catch { } throw; }
    }
    // Only remove previously recorded, complete copies belonging to this router.
    // Latest verified copy is always preserved, even if it exceeds the budget.
    internal static async Task<string> PruneAsync(RouterRecord router, string root, int keep, int budgetMb, CancellationToken ct)
    {
        if (keep <= 0 && budgetMb <= 0) return "Pre-upgrade cleanup disabled";
        NoLinks(root);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var valid = new List<(BackupHistoryEntry Entry, string Folder, long Bytes)>();
        foreach (var entry in router.Backups.OrderByDescending(b => b.CreatedAt))
        {
            string? folder = Path.GetDirectoryName(entry.BackupPath);
            if (folder is null || !Directory.Exists(folder) || !Path.GetFullPath(folder).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            // Direct children only: never accept a history entry aimed at an arbitrary folder.
            if (!string.Equals(Path.GetFullPath(Path.GetDirectoryName(folder)!) + Path.DirectorySeparatorChar, prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetDirectoryName(entry.ExportPath) != folder || !Path.GetFileName(folder).StartsWith("MikroTik-Manager-Backups_")) continue;
            NoLinks(folder);
            var files = Directory.GetFiles(folder);
            if (Directory.GetDirectories(folder).Length != 0 || files.Any(IsLinked)) continue;
            var expected = new HashSet<string>([entry.BackupPath, entry.ExportPath, Path.Combine(folder, "BackupManifest.csv")], StringComparer.OrdinalIgnoreCase);
            if (files.Length != 3 || files.Any(f => !expected.Contains(f))) continue;
            try { await VerifyAsync(entry, ct); } catch (IOException) { continue; }
            valid.Add((entry, folder, files.Sum(f => new FileInfo(f).Length)));
        }
        long total = valid.Sum(v => v.Bytes), budget = budgetMb * 1048576L;
        int remaining = valid.Count, removed = 0;
        foreach (var candidate in valid.Skip(1).Reverse())
        {
            ct.ThrowIfCancellationRequested();
            if (!(keep > 0 && remaining > keep || budget > 0 && total > budget)) break;
            Directory.Delete(candidate.Folder, true); total -= candidate.Bytes; remaining--; removed++;
        }
        return $"Removed {removed} old recovery copies" + (budget > 0 && total > budget ? "; newest copy retained above budget" : "");
    }
}
