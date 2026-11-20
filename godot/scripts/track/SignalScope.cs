using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record SignalScope(SignalKey Signal, int[] Edges, SignalKey[] Boundaries);
public sealed record SignalPath(SignalKey Entry, SignalKey? Exit, int[] Edges, bool Conflict = false);
public sealed record SignalPaths(SignalPath[] Selected, SignalPath[] Others, bool Truncated);

public sealed partial class RailInterlocking
{
    public SignalPaths InspectPaths(SignalKey key)
    {
        if (_dirty) Rebuild();
        if (!_network.Signals.ContainsKey(key)) return new(System.Array.Empty<SignalPath>(), System.Array.Empty<SignalPath>(), false);
        const int maxPaths = 512, maxStates = 8192, maxComparisons = 100000;
        int states = 0, comparisons = 0; bool truncated = false;
        SignalPath[] Walk(SignalKey entry, HashSet<int>? blocks = null)
        {
            var result = new List<SignalPath>();
            var pending = new Stack<(TrackEdge Edge, int From, int[] Path)>();
            foreach (var edge in Outgoing(entry))
                if (blocks == null || blocks.Contains(_block[edge.Id])) pending.Push((edge, entry.Node, System.Array.Empty<int>()));
            while (pending.TryPop(out var leg))
            {
                if (++states > maxStates || result.Count >= maxPaths) { truncated = true; break; }
                if (leg.Path.Length >= 128) { truncated = true; continue; }
                if (leg.Path.Contains(leg.Edge.Id)) continue;
                var path = leg.Path.Append(leg.Edge.Id).ToArray();
                int node = leg.Edge.A == leg.From ? leg.Edge.B : leg.Edge.A;
                var exit = new SignalKey(node, -TrackNetwork.SideAt(leg.Edge, node));
                if (_network.Signals.ContainsKey(new(node, 1)) || _network.Signals.ContainsKey(new(node, -1)))
                { result.Add(new(entry, _network.Signals.ContainsKey(exit) ? exit : null, path)); continue; }
                var next = Outgoing(exit).Where(e => e.Gauge == leg.Edge.Gauge).ToArray();
                if (next.Length == 0) result.Add(new(entry, null, path));
                foreach (var edge in next) pending.Push((edge, node, path));
            }
            return result.ToArray();
        }
        var selected = Walk(key); var selectedEdges = selected.SelectMany(p => p.Edges).ToHashSet();
        var blocks = Outgoing(key).Select(e => _block[e.Id]).ToHashSet();
        bool Conflict(IEnumerable<int> edges)
        {
            foreach (int a in edges)
                foreach (int b in selectedEdges)
                {
                    if (++comparisons > maxComparisons) { truncated = true; return true; }
                    if (Conflicts(a, b)) return true;
                }
            return false;
        }
        // Include nearby geometric crossings even when their rails have no common graph node.
        var candidates = new HashSet<int>(blocks);
        var boxes = selectedEdges.Select(id => Bounds(_network.Edges[id])).ToArray();
        double minX = boxes.Length == 0 ? 0 : boxes.Min(b => b.Min.X) - 5, maxX = boxes.Length == 0 ? 0 : boxes.Max(b => b.Max.X) + 5;
        double minZ = boxes.Length == 0 ? 0 : boxes.Min(b => b.Min.Z) - 5, maxZ = boxes.Length == 0 ? 0 : boxes.Max(b => b.Max.Z) + 5;
        double minY = boxes.Length == 0 ? 0 : boxes.Min(b => b.Min.Y) - 4.5, maxY = boxes.Length == 0 ? 0 : boxes.Max(b => b.Max.Y) + 4.5;
        bool Nearby(IEnumerable<int> ids)
        {
            if (selectedEdges.Count == 0) return false;
            return ids.Any(id => { var b = Bounds(_network.Edges[id]); return b.Min.X <= maxX && b.Max.X >= minX
                && b.Min.Z <= maxZ && b.Max.Z >= minZ && b.Min.Y <= maxY && b.Max.Y >= minY; });
        }
        foreach (var pair in _blocks)
        {
            if (comparisons >= maxComparisons) break;
            if (!candidates.Contains(pair.Key) && Nearby(pair.Value) && Conflict(pair.Value)) candidates.Add(pair.Key);
        }
        var others = new List<SignalPath>();
        foreach (var entry in _network.Signals.Keys.OrderBy(k => k.Node).ThenBy(k => k.Side))
        {
            if (entry == key || !Outgoing(entry).Any(e => candidates.Contains(_block[e.Id]))) continue;
            foreach (var path in Walk(entry, candidates))
            {
                if (others.Count + selected.Length >= maxPaths) { truncated = true; break; }
                others.Add(path with { Conflict = Conflict(path.Edges) });
            }
            if (states >= maxStates || comparisons >= maxComparisons || others.Count + selected.Length >= maxPaths) { truncated = true; break; }
        }
        return new(selected, others.ToArray(), truncated);
    }
    /// <summary>Inspection uses the reservation topology, never acquires leases or changes signal aspects.</summary>
    public SignalScope InspectScope(SignalKey key)
    {
        if (_dirty) Rebuild();
        var edges = new HashSet<int>(); var boundaries = new HashSet<SignalKey>();
        var pending = new Queue<SignalKey>(); var visited = new HashSet<SignalKey>(); pending.Enqueue(key);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current) || !_network.Signals.TryGetValue(current, out var signal)) continue;
            foreach (var edge in Outgoing(current))
            {
                edges.UnionWith(_blocks[_block[edge.Id]]);
                foreach (var next in NextSignals(edge, current.Node))
                    if (next is { } boundary)
                    {
                        boundaries.Add(boundary);
                        // The normal exit's receiving block is included: chain admission reserves it too.
                        if (signal.Kind != RailSignalKind.Normal) pending.Enqueue(boundary);
                    }
            }
        }
        return new(key, edges.Order().ToArray(), boundaries.OrderBy(s => s.Node).ThenBy(s => s.Side).ToArray());
    }
}
