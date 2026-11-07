namespace MikroTikManager;

internal static class BatchUpgradeQueue
{
    internal static async Task<List<RouterRunResult>> RunAsync(IReadOnlyList<RouterRecord> routers, int concurrency,
        int failureLimit, Func<bool> shouldStop, Func<RouterRecord, Task<RouterRunResult>> run, CancellationToken ct, IReadOnlySet<Guid>? completed = null)
    {
        if (concurrency < 0 || failureLimit < 0) throw new ArgumentOutOfRangeException(nameof(concurrency));
        int slots = concurrency == 0 ? Math.Max(1, routers.Count) : Math.Max(1, concurrency);
        var active = new List<Task<RouterRunResult>>(); var results = new List<RouterRunResult>();
        var pending = routers.ToList(); int failures = 0;
        DependencyPlan.Validate(routers, completed);
        try
        {
            while (pending.Count > 0 || active.Count > 0)
            {
                // Drain all finished tasks before opening another slot: observe failures first.
                foreach (var finished in active.Where(t => t.IsCompleted).ToArray())
                {
                    active.Remove(finished); var result = await finished; results.Add(result);
                    if (result.Result == "Failed") failures++;
                }
                bool stopped = ct.IsCancellationRequested || shouldStop() || failureLimit > 0 && failures >= failureLimit;
                while (!stopped && pending.Count > 0 && active.Count < slots)
                {
                    ct.ThrowIfCancellationRequested();
                    var next = pending.FirstOrDefault(r => r.UpgradeAfter.All(id => (completed?.Contains(id) == true && routers.All(candidate => candidate.Id != id)) || results.Any(x => x.RouterId == id && x.Result == "Completed")));
                    if (next is null) break;
                    pending.Remove(next);
                    active.Add(run(next));
                    if (active.Any(t => t.IsCompleted)) break;
                    stopped = shouldStop();
                }
                if (active.Count == 0) break;
                await Task.WhenAny(active);
            }
            ct.ThrowIfCancellationRequested();
            return results;
        }
        finally
        {
            // Never release UI/store ownership while another device is still running.
            try { await Task.WhenAll(active); } catch { }
        }
    }
}
