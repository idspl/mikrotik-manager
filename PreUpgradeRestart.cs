namespace MikroTikManager;

// One state per router run, shared by automatic retry attempts.
internal sealed class PreUpgradeRestart
{
    private bool _requested, _verified;

    internal async Task RunAsync(bool enabled, Func<Task> requestRestart, Func<Task> verifyRecovery, CancellationToken ct)
    {
        if (!enabled || _verified) return;
        ct.ThrowIfCancellationRequested();
        if (!_requested)
        {
            // The command may reach the router even if the response is lost. Never resend on automatic retry.
            _requested = true;
            await requestRestart();
        }
        ct.ThrowIfCancellationRequested();
        await verifyRecovery();
        _verified = true;
    }
}
