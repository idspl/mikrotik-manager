using System.Text;

namespace MikroTikManager;

internal static class RegressionTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MikroTikManager-tests-" + Guid.NewGuid().ToString("N"));
        var store = new SecureStore(directory);
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var router = new RouterRecord { Name = "Fixture", Username = "test", Password = "fixture-password" };
        store.SaveRouters([router]);
        Check(store.LoadRouters().Single().Password == router.Password, "DPAPI credentials roundtrip");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "routers.dat"))).Contains(router.Password), "Credentials must not be plaintext");
        var history = new MaintenanceHistory { RouterIds = [router.Id], CompletedIds = [] };
        history.Stages.Add(new HistoryStage(DateTime.Now, router.Id, router.Name, "Safety backup", 1, "Running", "Creating backup", "7.1", "7.1"));
        history.Save(store);
        Check(MaintenanceHistory.Load(store).Single().Stages.Single().Stage == "Safety backup", "History survives reload");
        history.CompletedIds.Add(router.Id); history.State = "Finished"; history.Save(store);
        Check(MaintenanceHistory.Load(store).Single().CompletedIds.Contains(router.Id), "Atomic history replacement");
        // Pause before the first router must never attempt an API connection.
        var engine = new UpgradeEngine(store, new AppSettings()) { PauseRequested = true };
        var result = await engine.RunSequentialAsync([router], CancellationToken.None);
        Check(result.Routers.Count == 0, "Pause must prevent starting next router");
        Check(MaintenanceHistory.Load(store).First().State == "Paused", "Pause state persisted");
        string file = Path.Combine(directory, "hash-fixture");
        File.WriteAllText(file, "abc", new UTF8Encoding(false));
        Check(SelfUpdater.Hash(file) == "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", "SHA256 known vector");
        // Files are isolated test fixtures. Retain them for CI diagnostics until runner cleanup.
    }
}
