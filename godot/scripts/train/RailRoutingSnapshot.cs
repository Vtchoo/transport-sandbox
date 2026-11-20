using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TransportSandbox;

/// <summary>Physical gauge-connected components own independent immutable graphs and destination caches.</summary>
public sealed class RailRoutingSnapshot
{
    private static readonly RailRoutingGraph Empty = RailRoutingGraph.Build(Array.Empty<RailRoutingEdge>());
    private readonly Dictionary<int, RailRoutingEdge> _edges;
    private readonly Dictionary<int, RailRoutingGraph> _byEdge;
    private readonly Dictionary<int, RailRoutingGraph> _components;
    private readonly HashSet<RailRoutingGraph> _graphs;
    public readonly int[] ChangedEdges;
    public int IntegratedEdges { get; }
    public int ComponentCount => _components.Count;
    public int ReusedComponents { get; }
    public bool Contains(RailRoutingGraph graph) => graph == Empty || _graphs.Contains(graph);
    public int ConflictCount(int edge) => _edges.TryGetValue(edge, out var value) ? value.Conflicts : 0;
    public RailRoutingGraph ForEdge(int edge) => edge == 0 ? _components.Values.FirstOrDefault() ?? Empty : _byEdge.GetValueOrDefault(edge, Empty);
    private RailRoutingSnapshot(Dictionary<int, RailRoutingEdge> edges, Dictionary<int, RailRoutingGraph> byEdge,
        Dictionary<int, RailRoutingGraph> components, int[] changed, int integrated, int reused)
    { _edges = edges; _byEdge = byEdge; _components = components; _graphs = components.Values.ToHashSet(); ChangedEdges = changed; IntegratedEdges = integrated; ReusedComponents = reused; }
    public static RailRoutingSnapshot Build(RailRoutingEdge[] source, RailRoutingSnapshot? previous, CancellationToken token)
    {
        // Compute across components: a geometric crossing need not have a connecting node.
        bool same = previous != null && source.Length == previous._edges.Count
            && source.All(e => previous._edges.TryGetValue(e.Id, out var old) && RailConflictCosts.SameGeometry(e, old));
        source = same ? source.Select(e => e with { Conflicts = previous!.ConflictCount(e.Id) }).ToArray()
            : RailConflictCosts.Build(source, token);
        var parent = Enumerable.Range(0, source.Length).ToArray();
        var rank = new byte[source.Length];
        int Root(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b)
        {
            a = Root(a); b = Root(b); if (a == b) return;
            if (rank[a] < rank[b]) (a, b) = (b, a);
            parent[b] = a; if (rank[a] == rank[b]) rank[a]++;
        }
        var first = new Dictionary<(int Node, int Gauge), int>();
        var edges = new Dictionary<int, RailRoutingEdge>(source.Length);
        var affectedNodes = new HashSet<(int Node, int Gauge)>();
        var changed = new HashSet<int>();
        void Mark(RailRoutingEdge edge) { changed.Add(edge.Id); affectedNodes.Add((edge.A, edge.Gauge)); affectedNodes.Add((edge.B, edge.Gauge)); }
        for (int i = 0; i < source.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var edge = source[i]; edges.Add(edge.Id, edge);
            foreach (int node in new[] { edge.A, edge.B })
            {
                var key = (node, edge.Gauge);
                if (first.TryGetValue(key, out int other)) Union(i, other); else first.Add(key, i);
            }
            if (previous == null || !previous._edges.TryGetValue(edge.Id, out var old)) Mark(edge);
            else if (!RailRoutingGraph.Equivalent(edge, old)) { Mark(old); Mark(edge); }
        }
        if (previous != null)
            foreach (var edge in previous._edges.Values) if (!edges.ContainsKey(edge.Id)) Mark(edge);
        // A new/deleted connection can change station run ends and legal choices on adjacent edges.
        foreach (var edge in edges.Values)
            if (affectedNodes.Contains((edge.A, edge.Gauge)) || affectedNodes.Contains((edge.B, edge.Gauge))) changed.Add(edge.Id);
        if (previous != null)
            foreach (var edge in previous._edges.Values)
                if (affectedNodes.Contains((edge.A, edge.Gauge)) || affectedNodes.Contains((edge.B, edge.Gauge))) changed.Add(edge.Id);
        var groups = new Dictionary<int, List<RailRoutingEdge>>();
        for (int i = 0; i < source.Length; i++)
        {
            int root = Root(i); if (!groups.TryGetValue(root, out var group)) groups.Add(root, group = new()); group.Add(source[i]);
        }
        int integrated = 0, reused = 0;
        var byEdge = new Dictionary<int, RailRoutingGraph>(source.Length);
        var components = new Dictionary<int, RailRoutingGraph>();
        foreach (var group in groups.Values.OrderBy(group => group.Min(edge => edge.Id)))
        {
            token.ThrowIfCancellationRequested(); var input = group.ToArray(); int key = input.Min(edge => edge.Id);
            RailRoutingGraph? graph = null;
            if (previous != null && previous._components.TryGetValue(key, out var old) && old.Matches(input)) { graph = old; reused++; }
            if (graph == null)
            {
                for (int i = 0; i < input.Length; i++)
                    if (input[i].KnownLength < 0 && previous != null && previous._byEdge.TryGetValue(input[i].Id, out var oldGraph))
                        input[i] = input[i] with { KnownLength = oldGraph.ReusableLength(input[i]) };
                graph = RailRoutingGraph.Build(input, token); integrated += graph.IntegratedEdges;
            }
            components.Add(key, graph); foreach (var edge in input) byEdge.Add(edge.Id, graph);
        }
        token.ThrowIfCancellationRequested();
        return new(edges, byEdge, components, changed.ToArray(), integrated, reused);
    }
}
