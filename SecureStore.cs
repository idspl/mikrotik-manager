using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MikroTikManager;

public sealed class SecureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string RootDirectory { get; }
    public string LogDirectory => Path.Combine(RootDirectory, "Logs");
    private string RoutersPath => Path.Combine(RootDirectory, "routers.dat");
    private string JobsPath => Path.Combine(RootDirectory, "jobs.dat");
    private string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public SecureStore()
    {
        string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        RootDirectory = Path.Combine(commonData, "MikroTik Manager");
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
        TryMigrateLegacyData(Path.Combine(commonData, "Indigo Router Scheduler"));
    }

    private void TryMigrateLegacyData(string legacyDirectory)
    {
        if (!Directory.Exists(legacyDirectory)) return;
        try
        {
            foreach (string source in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(legacyDirectory, source);
                string destination = Path.Combine(RootDirectory, relative);
                if (File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(
                Path.Combine(LogDirectory, "migration.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tCould not migrate all legacy data: {ex.Message}{Environment.NewLine}");
        }
    }

    public List<RouterRecord> LoadRouters() => LoadProtected<List<RouterRecord>>(RoutersPath) ?? [];
    public void SaveRouters(List<RouterRecord> routers) => SaveProtected(RoutersPath, routers);
    public List<UpgradeJob> LoadJobs() => LoadProtected<List<UpgradeJob>>(JobsPath) ?? [];
    public void SaveJobs(List<UpgradeJob> jobs) => SaveProtected(JobsPath, jobs);

    public AppSettings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new(); }
        catch { return new(); }
    }

    public void SaveSettings(AppSettings settings) => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));

    private static T? LoadProtected<T>(string path)
    {
        if (!File.Exists(path)) return default;
        byte[] clear = Dpapi.Unprotect(File.ReadAllBytes(path));
        try { return JsonSerializer.Deserialize<T>(clear, JsonOptions); }
        finally { Array.Clear(clear, 0, clear.Length); }
    }

    private static void SaveProtected<T>(string path, T value)
    {
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try { File.WriteAllBytes(path, Dpapi.Protect(clear)); }
        finally { Array.Clear(clear, 0, clear.Length); }
    }
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LocalFree(IntPtr value);
    private const int LocalMachine = 0x4;

    public static byte[] Protect(byte[] clear) => Transform(clear, true);
    public static byte[] Unprotect(byte[] cipher) => Transform(cipher, false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        IntPtr ptr = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, ptr, input.Length);
            var source = new DataBlob { Size = input.Length, Data = ptr };
            DataBlob target;
            bool ok = protect
                ? CryptProtectData(ref source, "MikroTik Manager", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, LocalMachine, out target)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, LocalMachine, out target);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                byte[] result = new byte[target.Size];
                Marshal.Copy(target.Data, result, 0, target.Size);
                return result;
            }
            finally { LocalFree(target.Data); }
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
