using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TransportSandbox;

public readonly record struct RailDestination(TrainOrderKind Kind, int Target);
public readonly record struct RailRoutingEdge(int Id, int A, int B, int Gauge, int SideA, int SideB, int Station,
    TrackVector P0, TrackVector P1, TrackVector P2, TrackVector P3, double KnownLength = -1, int SignalA = 3, int SignalB = 3);

/// <summary>Immutable directed graph. Build and search use managed data only, never the scene tree.</summary>
public sealed class RailRoutingGraph
{
    private static long _nextVersion;
    public long Version { get; } = Interlocked.Increment(ref _nextVersion);
    private readonly RailRoutingEdge[] _edges;
    private readonly Dictionary<int, int> _indices;
    private readonly Dictionary<int, int[]> _incident, _stations;
    private readonly double[] _length;
    private readonly int[][] _next, _previous;
    private readonly bool[] _corridor;
    public int StateCount => _length.Length;
    public int CorridorStateCount { get; }
    public int IntegratedEdges { get; private set; }
    internal bool Matches(RailRoutingEdge[] edges) => edges.Length == _edges.Length && edges.All(edge =>
        _indices.TryGetValue(edge.Id, out int index) && Equivalent(edge, _edges[index]));
    internal static bool Equivalent(RailRoutingEdge a, RailRoutingEdge b) => a with { KnownLength = 0 } == b with { KnownLength = 0 };
    internal double ReusableLength(RailRoutingEdge edge)
    {
        if (!_indices.TryGetValue(edge.Id, out int index)) return -1;
        var old = _edges[index];
        return old.P0 == edge.P0 && old.P1 == edge.P1 && old.P2 == edge.P2 && old.P3 == edge.P3 ? _length[index * 2] : -1;
    }
    private RailRoutingGraph(RailRoutingEdge[] edges, Dictionary<int, int> indices, Dictionary<int, int[]> incident,
        Dictionary<int, int[]> stations, double[] length, int[][] next, int[][] previous)
    {
        _edges = edges; _indices = indices; _incident = incident; _stations = stations; _length = length; _next = next; _previous = previous;
        _corridor = new bool[length.Length];
        for (int state = 0; state < length.Length; state++)
            if (next[state].Length == 1 && previous[state].Length == 1 && next[state][0] != (state ^ 1) && previous[state][0] != (state ^ 1)
                && edges[state / 2].Station == edges[next[state][0] / 2].Station && edges[state / 2].Station == edges[previous[state][0] / 2].Station)
            { _corridor[state] = true; CorridorStateCount++; }
    }

    public static RailRoutingGraph Build(RailRoutingEdge[] source, CancellationToken token = default, RailRoutingGraph? reuse = null)
    {
        var edges = source.OrderBy(e => e.Id).ToArray();
        var indices = new Dictionary<int, int>(edges.Length);
        var incident = new Dictionary<int, List<int>>();
        var stations = new Dictionary<int, List<int>>();
        static void Add(Dictionary<int, List<int>> map, int key, int value)
        { if (!map.TryGetValue(key, out var list)) map[key] = list = new(); list.Add(value); }
        var length = new double[edges.Length * 2];
        int integrated = 0;
        for (int i = 0; i < edges.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var edge = edges[i]; indices.Add(edge.Id, i);
            Add(incident, edge.A, i); Add(incident, edge.B, i);
            if (edge.Station != 0) Add(stations, edge.Station, i);
            double distance = edge.KnownLength;
            if (distance < 0 && reuse != null && reuse._indices.TryGetValue(edge.Id, out int oldIndex))
            {
                var old = reuse._edges[oldIndex];
                if (old.P0 == edge.P0 && old.P1 == edge.P1 && old.P2 == edge.P2 && old.P3 == edge.P3) distance = reuse._length[oldIndex * 2];
            }
            if (distance < 0)
            {
                integrated++;
                // Same 512-step arc table as placement, without retaining a table for every graph edge.
                var p1 = edge.P1 - edge.P0; var p2 = edge.P2 - edge.P0; var p3 = edge.P3 - edge.P0;
                double lastX = 0, lastY = 0, lastZ = 0; distance = 0;
                for (int step = 1; step <= 512; step++)
                {
                    double t = step / 512.0, s = 1 - t;
                    double c1 = 3 * s * s * t, c2 = 3 * s * t * t, c3 = t * t * t;
                    double x = p1.X * c1 + p2.X * c2 + p3.X * c3;
                    double y = p1.Y * c1 + p2.Y * c2 + p3.Y * c3;
                    double z = p1.Z * c1 + p2.Z * c2 + p3.Z * c3;
                    double dx = x - lastX, dy = y - lastY, dz = z - lastZ;
                    distance += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    lastX = x; lastY = y; lastZ = z;
                }
            }
            if (!double.IsFinite(distance) || distance < 0) throw new ArgumentException("Invalid track arc length.");
            length[i * 2] = length[i * 2 + 1] = distance;
        }
        var next = new int[length.Length][];
        var previous = Enumerable.Range(0, length.Length).Select(_ => new List<int>()).ToArray();
        for (int state = 0; state < next.Length; state++)
        {
            token.ThrowIfCancellationRequested();
            var edge = edges[state / 2]; bool forward = (state & 1) != 0;
            int node = forward ? edge.B : edge.A, side = forward ? edge.SideB : edge.SideA;
            var exits = new List<int>();
            foreach (int index in incident[node])
            {
                var candidate = edges[index]; bool fromA = candidate.A == node;
                if (candidate.Id != edge.Id && candidate.Gauge == edge.Gauge && (fromA ? candidate.SideA : candidate.SideB) != side)
                    exits.Add(index * 2 + (fromA ? 1 : 0));
            }
            // A wrong-way signal is a directed dead end, even when rails continue beyond it.
            int mask = forward ? edge.SignalB : edge.SignalA;
            bool allowed = (mask & (side < 0 ? 2 : 1)) != 0;
            next[state] = exits.Count == 0 || !allowed ? new[] { state ^ 1 } : exits.ToArray();
            foreach (int successor in next[state]) previous[successor].Add(state);
        }
        return new(edges, indices, incident.ToDictionary(p => p.Key, p => p.Value.ToArray()),
            stations.ToDictionary(p => p.Key, p => p.Value.ToArray()), length, next, previous.Select(p => p.ToArray()).ToArray()) { IntegratedEdges = integrated };
    }
    private bool DeadEnd(int state) => _next[state].Length == 1 && _next[state][0] == (state ^ 1);
    private Dictionary<int, double> Targets(RailDestination destination)
    {
        var targets = new Dictionary<int, double>();
        if (destination.Kind == TrainOrderKind.Waypoint && _incident.TryGetValue(destination.Target, out var incident))
            foreach (int index in incident)
            {
                var edge = _edges[index]; int state = index * 2 + (edge.B == destination.Target ? 1 : 0);
                int side = edge.B == destination.Target ? edge.SideB : edge.SideA;
                int mask = edge.B == destination.Target ? edge.SignalB : edge.SignalA;
                if ((mask & (side < 0 ? 2 : 1)) == 0) continue;
                if (_length[state] <= 0) continue;
                double buffer = DeadEnd(state) ? Math.Min(2, _length[state]) / _length[state] : 0;
                targets[state] = (state & 1) != 0 ? 1 - buffer : buffer;
            }
        if (destination.Kind == TrainOrderKind.Station && _stations.TryGetValue(destination.Target, out var station))
            foreach (int index in station)
                for (int direction = 0; direction < 2; direction++)
                {
                    int state = index * 2 + direction;
                    if (!DeadEnd(state) && _next[state].Any(s => _edges[s / 2].Station == destination.Target)) continue;
                    double remaining = 0;
                    var visited = new HashSet<int>();
                    while (visited.Add(state))
                    {
                        if (_length[state] > remaining)
                        {
                            targets[state] = (state & 1) != 0 ? 1 - remaining / _length[state] : remaining / _length[state];
                            break;
                        }
                        remaining -= _length[state];
                        int previous = _next[state ^ 1].FirstOrDefault(s => s != state && _edges[s / 2].Station == destination.Target, -1);
                        if (previous < 0) break;
                        state = previous ^ 1;
                    }
                }
        return targets;
    }
    private double EntryPenalty(int from, int to, RailDestination destination, RailRoutingPolicy policy)
    {
        int station = _edges[to / 2].Station;
        return station == 0 || station == _edges[from / 2].Station || policy.Allows(station)
            || destination.Kind == TrainOrderKind.Station && destination.Target == station
            ? 0 : RailRoutingPolicy.UnorderedStationPenalty;
    }
    public RailDestinationField Search(RailDestination destination, CancellationToken token = default, RailRoutingPolicy? policy = null, bool compressCorridors = true)
    {
        policy ??= RailRoutingPolicy.Empty;
        var targets = Targets(destination);
        var costs = new double[StateCount]; Array.Fill(costs, double.PositiveInfinity);
        var reversals = new int[StateCount]; Array.Fill(reversals, int.MaxValue);
        var next = new int[StateCount]; Array.Fill(next, -1);
        var heap = new PriorityQueue<int, (int Reversals, double Cost, int State)>();
        foreach (var (state, u) in targets)
        {
            double cost = _length[state] * ((state & 1) != 0 ? u : 1 - u);
            costs[state] = cost; reversals[state] = 0; heap.Enqueue(state, (0, cost, state));
        }
        int visits = 0, heapPops = 0, relaxed = 0;
        while (heap.TryDequeue(out int state, out var priority))
        {
            if ((visits++ & 255) == 0) token.ThrowIfCancellationRequested();
            heapPops++;
            if (priority.Reversals != reversals[state] || priority.Cost != costs[state]) continue;
            foreach (int predecessor in _previous[state])
            {
                int previous = predecessor, successor = state;
                double suffixCost = priority.Cost; int suffixTurns = priority.Reversals;
                while (true)
                {
                    if ((visits++ & 255) == 0) token.ThrowIfCancellationRequested();
                    double cost = suffixCost + _length[previous] + EntryPenalty(previous, successor, destination, policy);
                    int turns = suffixTurns + (successor == (previous ^ 1) ? 1 : 0);
                    if (turns > reversals[previous] || turns == reversals[previous] && cost >= costs[previous]) break;
                    costs[previous] = cost; reversals[previous] = turns; next[previous] = successor; relaxed++;
                    if (!compressCorridors || !_corridor[previous] || targets.ContainsKey(previous))
                    {
                        heap.Enqueue(previous, (turns, cost, previous));
                        break;
                    }
                    // A unique continuation has the settled suffix's exact cost. Walk the
                    // corridor directly; junctions, station boundaries and targets remain heap vertices.
                    successor = previous; previous = _previous[previous][0]; suffixCost = cost; suffixTurns = turns;
                }
            }
        }
        token.ThrowIfCancellationRequested();
        return new(this, costs, reversals, next, targets, destination, policy, heapPops, relaxed);
    }
    public sealed class RailDestinationField
    {
        private readonly RailRoutingGraph _graph;
        private readonly double[] _costs;
        private readonly int[] _reversals;
        private readonly int[] _next;
        private readonly Dictionary<int, double> _targets;
        private readonly RailDestination _destination;
        private readonly RailRoutingPolicy _policy;
        public long GraphVersion => _graph.Version;
        public int HeapPops { get; }
        public int RelaxedStates { get; }
        internal RailDestinationField(RailRoutingGraph graph, double[] costs, int[] reversals, int[] next, Dictionary<int, double> targets,
            RailDestination destination, RailRoutingPolicy policy, int heapPops, int relaxed)
        { _graph = graph; _costs = costs; _reversals = reversals; _next = next; _targets = targets; _destination = destination; _policy = policy; HeapPops = heapPops; RelaxedStates = relaxed; }
        public long Bytes => (long)_costs.Length * 16 + _targets.Count * 32L;
        public double TargetU(int externalState) => _targets[_graph._indices[externalState / 2] * 2 + (externalState & 1)];
        public RailPath? Plan(TrainPosition start, int travelDirection)
        {
            if (!_graph._indices.TryGetValue(start.Edge, out int index)) return null;
            int initial = index * 2 + (start.Facing * travelDirection > 0 ? 1 : 0);
            double best = double.PositiveInfinity;
            int bestReversals = int.MaxValue;
            if (_targets.TryGetValue(initial, out double u))
            {
                double direct = (u - start.U) * ((initial & 1) != 0 ? 1 : -1) * _graph._length[initial];
                if (direct >= -.02) { best = Math.Max(0, direct); bestReversals = 0; }
            }
            double remaining = _graph._length[initial] * ((initial & 1) != 0 ? 1 - start.U : start.U);
            int successor = -1;
            foreach (int state in _graph._next[initial])
            {
                if (_reversals[state] == int.MaxValue) continue;
                int turns = _reversals[state] + (state == (initial ^ 1) ? 1 : 0);
                double cost = remaining + _costs[state] + _graph.EntryPenalty(initial, state, _destination, _policy);
                if (turns < bestReversals || turns == bestReversals && cost < best)
                { best = cost; bestReversals = turns; successor = state; }
            }
            if (!double.IsFinite(best)) return null;
            int External(int state) => _graph._edges[state / 2].Id * 2 + (state & 1);
            var path = new List<int> { External(initial) };
            double distance = successor < 0 ? best : remaining;
            for (int state = successor, steps = 0; state >= 0; state = _next[state])
            {
                if (++steps > _next.Length) throw new InvalidOperationException("Routing field contains a cycle.");
                path.Add(External(state));
                // Routing penalties must never enter the physical braking/arrival distance.
                distance += _graph._length[state] * (_next[state] < 0
                    ? (state & 1) != 0 ? _targets[state] : 1 - _targets[state] : 1);
            }
            return new(path.ToArray(), distance);
        }
    }
}
