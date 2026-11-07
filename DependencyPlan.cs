namespace MikroTikManager;

internal static class DependencyPlan
{
    internal static void Validate(IReadOnlyList<RouterRecord> routers)
    {
        var byId = routers.ToDictionary(r => r.Id);
        var visiting = new HashSet<Guid>(); var visited = new HashSet<Guid>();
        void Visit(Guid id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException("Upgrade dependency cycle detected. Review device upgrade order.");
            foreach (var dependency in byId[id].UpgradeAfter.Distinct())
            {
                if (!byId.ContainsKey(dependency)) throw new InvalidOperationException($"{byId[id].Name}: include every 'Upgrade after' device in this job, or remove the dependency.");
                Visit(dependency);
            }
            visiting.Remove(id); visited.Add(id);
        }
        foreach (var router in routers) Visit(router.Id);
    }
}
