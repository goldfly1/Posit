using Posit.Contracts.Core;
using Posit.Core.State;

namespace Posit.Core.Graph;

public interface IDependencyGraphEngine
{
    DependencyGraph Build(PhaseId[] phaseIds, PhaseId[][] dependencies);
    PhaseId? GetNextRunnable(SessionState state);
    bool HasCycles(DependencyGraph graph, out PhaseId[] cycle);
}

public sealed class DependencyGraphEngine : IDependencyGraphEngine
{
    public DependencyGraph Build(PhaseId[] phaseIds, PhaseId[][] dependencies)
    {
        if (phaseIds.Length != dependencies.Length)
            throw new ArgumentException("phaseIds and dependencies must have the same length.");

        var priorities = new int[phaseIds.Length];
        for (var i = 0; i < priorities.Length; i++)
            priorities[i] = ComputePriority(i, phaseIds, dependencies, []);

        return new DependencyGraph
        {
            PhaseIds = phaseIds,
            Adjacency = dependencies,
            Priorities = priorities
        };
    }

    public PhaseId? GetNextRunnable(SessionState state)
    {
        var graph = state.DependencyGraph;
        if (graph is null)
            return null;

        var completed = state.CompletedPhases;
        var current = state.CurrentPhaseId;

        var eligible = graph.PhaseIds
            .Select((id, index) => (id, index))
            .Where(x => !completed.Contains(x.id))
            .Where(x => current is null || x.id != current)
            .Where(x => graph.Adjacency[x.index].All(d => completed.Contains(d)))
            .OrderBy(x => graph.Priorities[x.index])
            .Select(x => x.id)
            .FirstOrDefault();

        return eligible.Value is null ? null : eligible;
    }

    public bool HasCycles(DependencyGraph graph, out PhaseId[] cycle)
    {
        var index = graph.PhaseIds.ToDictionary(id => id, id => graph.PhaseIds.ToList().IndexOf(id));
        var visited = new HashSet<PhaseId>();
        var stack = new HashSet<PhaseId>();
        var path = new List<PhaseId>();

        foreach (var phase in graph.PhaseIds)
        {
            if (!visited.Contains(phase))
            {
                if (DetectCycle(phase, graph, index, visited, stack, path, out cycle))
                    return true;
            }
        }

        cycle = [];
        return false;
    }

    private static bool DetectCycle(
        PhaseId phase,
        DependencyGraph graph,
        Dictionary<PhaseId, int> index,
        HashSet<PhaseId> visited,
        HashSet<PhaseId> stack,
        List<PhaseId> path,
        out PhaseId[] cycle)
    {
        visited.Add(phase);
        stack.Add(phase);
        path.Add(phase);

        var deps = graph.Adjacency[index[phase]];
        foreach (var dep in deps)
        {
            if (!index.TryGetValue(dep, out _))
                continue;

            if (stack.Contains(dep))
            {
                var start = path.IndexOf(dep);
                cycle = path.Skip(start).ToArray();
                return true;
            }

            if (!visited.Contains(dep))
            {
                if (DetectCycle(dep, graph, index, visited, stack, path, out cycle))
                    return true;
            }
        }

        stack.Remove(phase);
        path.RemoveAt(path.Count - 1);
        cycle = [];
        return false;
    }

    private static int ComputePriority(int index, PhaseId[] phaseIds, PhaseId[][] dependencies, HashSet<int> visiting)
    {
        if (visiting.Contains(index))
            return 0;

        visiting.Add(index);
        var max = 0;
        foreach (var dep in dependencies[index])
        {
            var depIndex = Array.IndexOf(phaseIds, dep);
            if (depIndex >= 0)
                max = Math.Max(max, 1 + ComputePriority(depIndex, phaseIds, dependencies, visiting));
        }

        return max;
    }
}