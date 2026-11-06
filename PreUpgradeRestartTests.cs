namespace MikroTikManager;

internal static class PreUpgradeRestartTests
{
    internal static async Task RunAsync()
    {
        int sends = 0, verifies = 0;
        var restart = new PreUpgradeRestart();
        Task Send() { sends++; return Task.CompletedTask; }
        Task Verify() { verifies++; return Task.CompletedTask; }
        await restart.RunAsync(false, Send, Verify, default);
        if (sends != 0 || verifies != 0) throw new Exception("Restart must be opt-in");
        await restart.RunAsync(true, Send, Verify, default);
        await restart.RunAsync(true, Send, Verify, default);
        if (sends != 1 || verifies != 1) throw new Exception("Automatic retries must not repeat a verified restart");
        restart = new PreUpgradeRestart(); sends = verifies = 0;
        try { await restart.RunAsync(true, Send, () => throw new TimeoutException(), default); }
        catch (TimeoutException) { }
        await restart.RunAsync(true, Send, Verify, default);
        if (sends != 1 || verifies != 1) throw new Exception("Recovery failure must recheck without another reboot");
        restart = new PreUpgradeRestart(); sends = 0;
        try { await restart.RunAsync(true, () => { sends++; throw new IOException("Response lost"); }, Verify, default); }
        catch (IOException) { }
        await restart.RunAsync(true, Send, Verify, default);
        if (sends != 1) throw new Exception("Lost response must not cause duplicate reboot");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await new PreUpgradeRestart().RunAsync(true, Send, Verify, cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        if (sends != 1) throw new Exception("Cancelled restart sent a command");
        var job = new UpgradeJob { RestartBeforeUpgrade = true };
        var restored = System.Text.Json.JsonSerializer.Deserialize<UpgradeJob>(System.Text.Json.JsonSerializer.Serialize(job))!;
        if (!restored.RestartBeforeUpgrade || System.Text.Json.JsonSerializer.Deserialize<UpgradeJob>("{}")!.RestartBeforeUpgrade)
            throw new Exception("Restart preference must persist, with old schedules defaulting off");
    }
}
