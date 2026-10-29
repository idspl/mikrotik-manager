using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;

namespace MikroTikManager;

internal static class InstalledMode
{
    internal static bool Enabled => File.Exists(Path.Combine(AppContext.BaseDirectory, "installed.mode"));
    internal static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal static bool ElevateIfNeeded(string[] args)
    {
        if (!Enabled || IsAdministrator) return false;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        try { Process.Start(start); } catch (Exception ex) { MessageBox.Show("Installed mode requires administrator access to the shared scheduler and credentials. " + ex.Message); }
        return true;
    }
    internal static string Status()
    {
        if (!Enabled) return "Portable mode — Windows Task Scheduler";
        try { using var service = new ServiceController(SchedulerWindowsService.Name); return "Windows service: " + service.Status; }
        catch { return "Windows service unavailable — run Setup to repair"; }
    }
    internal static void RequireRunning()
    {
        using var service = new ServiceController(SchedulerWindowsService.Name);
        if (service.Status != ServiceControllerStatus.Running)
            throw new InvalidOperationException("The scheduler service is not running. Start Indigo MikroTik Scheduler in Windows Services before activating this schedule.");
    }
    internal static void PrepareDataDirectory()
    {
        if (!IsAdministrator) throw new UnauthorizedAccessException("Administrator access required.");
        var store = new SecureStore();
        // Machine-scope DPAPI requires access control on its encrypted files as well.
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(store.RootDirectory).SetAccessControl(security);
        foreach (string file in Directory.EnumerateFiles(store.RootDirectory))
        {
            var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(file).SetAccessControl(acl);
        }
    }
}
