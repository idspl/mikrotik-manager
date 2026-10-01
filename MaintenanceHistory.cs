using System.Text.Json;

namespace MikroTikManager;

public sealed class MaintenanceHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Started { get; set; } = DateTime.Now;
    public string State { get; set; } = "Interrupted";
    public List<Guid> RouterIds { get; set; } = [];
    public List<Guid> CompletedIds { get; set; } = [];
    public List<HistoryStage> Stages { get; set; } = [];
    public FailureBehavior FailureBehavior { get; set; }
    public int RetryCount { get; set; }

    public void Save(SecureStore store)
    {
        string folder = Path.Combine(store.RootDirectory, "History");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Id + ".dat");
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(this);
        try
        {
            File.WriteAllBytes(path + ".tmp", Dpapi.Protect(clear));
            File.Move(path + ".tmp", path, true);
        }
        finally { Array.Clear(clear); }
    }

    public static List<MaintenanceHistory> Load(SecureStore store)
    {
        string folder = Path.Combine(store.RootDirectory, "History");
        if (!Directory.Exists(folder)) return [];
        var runs = new List<MaintenanceHistory>();
        foreach (string path in Directory.EnumerateFiles(folder, "*.dat"))
        {
            byte[]? clear = null;
            try
            {
                clear = Dpapi.Unprotect(File.ReadAllBytes(path));
                var run = JsonSerializer.Deserialize<MaintenanceHistory>(clear);
                if (run is not null) runs.Add(run);
            }
            catch { /* A damaged entry must not prevent recovery of other runs. */ }
            finally { if (clear is not null) Array.Clear(clear); }
        }
        return runs.OrderByDescending(x => x.Started).ToList();
    }
}

public sealed record HistoryStage(DateTime Time, Guid RouterId, string Router, string Stage,
    int Attempt, string Result, string Message, string RouterOS, string Firmware);
