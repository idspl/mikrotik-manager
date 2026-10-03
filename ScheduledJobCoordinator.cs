namespace MikroTikManager;

internal enum ScheduledOwnership { Acquired, CompletedByDesktop, Failed }

internal static class ScheduledJobCoordinator
{
    // Synchronous by design: mutex ownership must remain on the calling OS thread.
    internal static ScheduledOwnership Wait(Mutex instance, SecureStore store, Guid jobId, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var job = store.LoadJobs().FirstOrDefault(x => x.Id == jobId);
            if (job is null) return ScheduledOwnership.Failed;
            // A recurring desktop run advances its due date instead of becoming terminal.
            if (job.Kind == ScheduledJobKind.Backup && job.State == "Scheduled"
                && job.ScheduledLocalTime > DateTime.Now && job.CompletedAt is not null)
                return job.LastRunSuccessful ? ScheduledOwnership.CompletedByDesktop : ScheduledOwnership.Failed;
            if (job.State != "Scheduled" && job.State != "Running")
                return job.State == "Completed" ? ScheduledOwnership.CompletedByDesktop : ScheduledOwnership.Failed;
            try { if (instance.WaitOne(TimeSpan.FromMilliseconds(Math.Min(2000, Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds))))) return ScheduledOwnership.Acquired; }
            catch (AbandonedMutexException) { return ScheduledOwnership.Acquired; }
        }
        return ScheduledOwnership.Failed;
    }
}
