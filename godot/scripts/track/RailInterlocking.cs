using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record SignalTrain(int Id, TrainRoute Route, TrainPosition Position, double Length, int Direction);
public readonly record struct SignalLimit(double Distance, string Message, bool CanReverse = false);

/// <summary>Live occupancy/reservations are separate from immutable destination routing fields.</summary>
public sealed class RailInterlocking : IDisposable
{
    private sealed class Lease(SignalTrain train, SignalKey signal, HashSet<int> edges)
    {
        public readonly int Train = train.Id;
        public readonly TrainRoute Route = train.Route;
        public readonly int PlanRevision = train.Route.PlanRevision, Direction = train.Direction;
        public readonly SignalKey Signal = signal;
        public readonly HashSet<int> Edges = edges;
        public bool Entered;
    }
    private readonly TrackNetwork _network;
    private readonly TrainPlacement _placement;
    private readonly Dictionary<int, int> _block = new();
    private readonly Dictionary<int, HashSet<int>> _blocks = new();
    private readonly Dictionary<int, List<TrackEdge>> _incident = new();
    private readonly Dictionary<int, HashSet<int>> _occupied = new();
    private readonly Dictionary<int, HashSet<int>> _footprints = new();
    private readonly List<Lease> _leases = new();
    private readonly Dictionary<(int, int), bool> _crossings = new();
    private readonly Dictionary<int, (TrackVector Min, TrackVector Max)> _bounds = new();
    private readonly Dictionary<(int Edge, int From), SignalKey?[]> _nextSignals = new();
    private bool _dirty = true;
    private int _aspectTick;
    public int ReservationCount => _leases.Count;
    public SavedLease[] SaveReservations() => _leases.Select(l => new SavedLease(l.Train, l.Signal, l.Edges.ToArray(), l.Entered)).ToArray();
    public void RestoreReservations(SavedLease[] saved, SignalTrain[] trains)
    {
        Update(trains);
        _leases.Clear();
        foreach (var item in saved)
        {
            var train = trains.FirstOrDefault(t => t.Id == item.Train);
            if (train == null || !_network.Signals.ContainsKey(item.Signal) || item.Edges.Any(e => !_network.Edges.ContainsKey(e))) continue;
            _leases.Add(new(train, item.Signal, item.Edges.ToHashSet()) { Entered = item.Entered });
        }
    }
    public RailInterlocking(TrackNetwork network, TrainPlacement placement)
    { _network = network; _placement = placement; network.Changed += Changed; }
    private void Changed() => _dirty = true;
    public void Dispose() => _network.Changed -= Changed;
    private void Rebuild()
    {
        _dirty = false; _block.Clear(); _blocks.Clear(); _incident.Clear(); _leases.Clear(); _crossings.Clear(); _bounds.Clear();
        _nextSignals.Clear();
        foreach (var edge in _network.Edges.Values)
            foreach (int node in new[] { edge.A, edge.B })
            { if (!_incident.TryGetValue(node, out var list)) _incident[node] = list = new(); list.Add(edge); }
        // Signals cut physical blocks in both directions. Gauge-incompatible edges never share a block.
        foreach (var start in _network.Edges.Values)
        {
            if (_block.ContainsKey(start.Id)) continue;
            int id = start.Id; var members = new HashSet<int>(); _blocks.Add(id, members);
            var queue = new Queue<TrackEdge>(); queue.Enqueue(start); _block[start.Id] = id;
            while (queue.TryDequeue(out var edge))
            {
                members.Add(edge.Id);
                foreach (int node in new[] { edge.A, edge.B })
                {
                    bool boundary = _network.SignalMask(node) != 3 || _network.Signals.ContainsKey(new(node, 1));
                    foreach (var next in _incident[node])
                        // A signal separates the two approach halves, not branches on the same half.
                        if (next.Gauge == start.Gauge && !_block.ContainsKey(next.Id)
                            && (!boundary || TrackNetwork.SideAt(next, node) == TrackNetwork.SideAt(edge, node)))
                        { _block[next.Id] = id; queue.Enqueue(next); }
                }
            }
        }
    }
    public void Update(IEnumerable<SignalTrain> trains)
    {
        if (_network.Signals.Count == 0) { _leases.Clear(); _occupied.Clear(); _footprints.Clear(); return; }
        bool rebuilt = _dirty;
        if (_dirty) Rebuild();
        _occupied.Clear(); _footprints.Clear();
        var byId = trains.ToDictionary(train => train.Id);
        foreach (var train in byId.Values)
        {
            var edges = new HashSet<int>();
            try
            {
                var tail = train.Route.Sample(train.Position, -train.Length / 2).Position;
                edges.UnionWith(train.Route.Trace(tail, 1, train.Length).Select(span => span.Edge.Id));
            }
            catch (InvalidOperationException) { if (_network.Edges.ContainsKey(train.Position.Edge)) edges.Add(train.Position.Edge); }
            _footprints[train.Id] = edges;
            foreach (int edge in edges)
            { if (!_occupied.TryGetValue(edge, out var owners)) _occupied[edge] = owners = new(); owners.Add(train.Id); }
        }
        _leases.RemoveAll(lease =>
        {
            if (!_footprints.TryGetValue(lease.Train, out var footprint)) return true;
            var train = byId[lease.Train];
            if (!lease.Entered && (lease.Route != train.Route || lease.PlanRevision != train.Route.PlanRevision || lease.Direction != train.Direction)) return true;
            if (footprint.Overlaps(lease.Edges)) lease.Entered = true;
            return lease.Entered && !footprint.Overlaps(lease.Edges);
        });
        if (rebuilt || ++_aspectTick % 12 == 0)
            foreach (var signal in _network.Signals.Values)
                signal.Aspect = signal.Kind == RailSignalKind.Path ? RailSignalAspect.Red : IdleAspect(signal, new());
    }
    private IEnumerable<TrackEdge> Outgoing(SignalKey key) => _incident.GetValueOrDefault(key.Node, new())
        .Where(edge => TrackNetwork.SideAt(edge, key.Node) == key.Side);
    private HashSet<int> Footprint(SignalTrain train)
    {
        var edges = new HashSet<int>();
        try
        {
            var tail = train.Route.Sample(train.Position, -train.Length / 2).Position;
            edges.UnionWith(train.Route.Trace(tail, 1, train.Length).Select(span => span.Edge.Id));
        }
        catch (InvalidOperationException)
        {
            if (_network.Edges.ContainsKey(train.Position.Edge)) edges.Add(train.Position.Edge);
        }
        return edges;
    }
    /// <summary>Placement checks physical blocks, including networks without signals, against live train footprints.</summary>
    public bool PlacementBlockOccupied(SignalTrain candidate, IEnumerable<SignalTrain> others)
    {
        if (_dirty) Rebuild();
        var blocks = Footprint(candidate).Where(_block.ContainsKey).Select(edge => _block[edge]).ToHashSet();
        return others.Any(other => other.Id != candidate.Id && Footprint(other)
            .Any(edge => _block.TryGetValue(edge, out int block) && blocks.Contains(block)));
    }
    private bool Clear(HashSet<int> edges, int train)
    {
        foreach (int edge in edges)
        {
            foreach (var (occupied, owners) in _occupied)
                if (owners.Any(owner => owner != train) && Conflicts(edge, occupied)) return false;
            foreach (var lease in _leases)
                if (lease.Train != train && lease.Edges.Any(other => Conflicts(edge, other))) return false;
        }
        return true;
    }
    internal bool DetourEdgeClear(int edge, int train) => Clear(new() { edge }, train);
    internal bool CanDetour(int train, int[] oldStates, int[] newStates)
    {
        var changed = oldStates.Select(state => state / 2).ToHashSet();
        changed.SymmetricExceptWith(newStates.Select(state => state / 2));
        return !_leases.Any(lease => lease.Train == train && lease.Entered && lease.Edges.Overlaps(changed));
    }
    internal void ReleasePending(int train) => _leases.RemoveAll(lease => lease.Train == train && !lease.Entered);
    /// <summary>Directed exits of a signal block. Plain nodes are connections, never boundaries.</summary>
    private SignalKey?[] NextSignals(TrackEdge first, int from)
    {
        var start = (first.Id, from);
        if (_nextSignals.TryGetValue(start, out var cached)) return cached;
        var exits = new HashSet<SignalKey?>();
        var visited = new HashSet<(int Edge, int From)>();
        var queue = new Queue<(TrackEdge Edge, int From)>();
        queue.Enqueue((first, from));
        while (queue.TryDequeue(out var leg))
        {
            if (!visited.Add((leg.Edge.Id, leg.From))) continue;
            int node = leg.Edge.A == leg.From ? leg.Edge.B : leg.Edge.A;
            int side = -TrackNetwork.SideAt(leg.Edge, node);
            if (_network.Signals.ContainsKey(new(node, -1)) || _network.Signals.ContainsKey(new(node, 1)))
            {
                var key = new SignalKey(node, side);
                // A wrong-way signal is a blocked exit, not permission from its opposite face.
                exits.Add(_network.Signals.ContainsKey(key) ? key : null);
                continue;
            }
            var next = Outgoing(new(node, side)).Where(edge => edge.Gauge == first.Gauge).ToArray();
            if (next.Length == 0) exits.Add(null);
            foreach (var edge in next) queue.Enqueue((edge, node));
        }
        if (exits.Count == 0) exits.Add(null); // An unsignalled closed loop has no normal exit.
        return _nextSignals[start] = exits.ToArray();
    }
    private RailSignalAspect IdleAspect(TrackSignal signal, HashSet<SignalKey> visited)
    {
        if (!visited.Add(signal.Key)) return RailSignalAspect.Red;
        bool green = false, red = false;
        foreach (var edge in Outgoing(signal.Key))
        {
            var block = _blocks[_block[edge.Id]];
            if (!Clear(block, -1)) { red = true; continue; }
            if (signal.Kind == RailSignalKind.Normal) { green = true; continue; }
            foreach (var exit in NextSignals(edge, signal.Key.Node))
            {
                var aspect = exit is { } key ? IdleAspect(_network.Signals[key], new(visited)) : RailSignalAspect.Red;
                green |= aspect != RailSignalAspect.Red;
                red |= aspect != RailSignalAspect.Green;
            }
        }
        return green ? red && signal.Kind == RailSignalKind.Chain ? RailSignalAspect.Blue : RailSignalAspect.Green : RailSignalAspect.Red;
    }
    public SignalLimit Limit(SignalTrain train, double lookahead, bool commit = true)
    {
        if (_network.Signals.Count == 0) return new(double.PositiveInfinity, "");
        var leading = train.Route.Sample(train.Position, train.Direction * train.Length / 2).Position;
        var spans = train.Route.Trace(leading, train.Direction, double.PositiveInfinity).GetEnumerator();
        using (spans)
        {
            while (spans.MoveNext())
            {
                var span = spans.Current; double distance = span.Distance + span.Length;
                if (distance > lookahead || !span.ReachesNode) break;
                var key = new SignalKey(span.Node, span.Side);
                if (_network.SignalMask(key.Node) == 3 && !_network.Signals.ContainsKey(new(key.Node, 1))) continue;
                if (!_network.Signals.TryGetValue(key, out var signal))
                    return new(Math.Max(0, distance - 2), "Wrong-way signal: stopping to reverse", CanReverse: true);
                if (_leases.Any(lease => lease.Train == train.Id && lease.Signal == key
                    && (lease.Entered || lease.Route == train.Route && lease.PlanRevision == train.Route.PlanRevision && lease.Direction == train.Direction)))
                { if (commit) signal.Aspect = RailSignalAspect.Green; continue; }
                if (!Reserve(train, signal, span, commit))
                { if (commit) signal.Aspect = RailSignalAspect.Red; return new(Math.Max(0, distance - 2), "Waiting at " + signal.Kind.ToString().ToLowerInvariant() + " signal"); }
                if (commit) signal.Aspect = RailSignalAspect.Green;
            }
        }
        return new(double.PositiveInfinity, "");
    }
    private bool Reserve(SignalTrain train, TrackSignal entry, RouteSpan approach, bool commit)
    {
        TrainPosition start;
        try { start = train.Route.Sample(approach.Position, train.Direction * (approach.Length + .00001)).Position; }
        catch (InvalidOperationException) { return false; }
        using var spans = train.Route.Trace(start, train.Direction, double.PositiveInfinity).GetEnumerator();
        var units = new List<(SignalKey Key, HashSet<int> Edges)>();
        var seen = new HashSet<SignalKey> { entry.Key };
        var key = entry.Key; var kind = entry.Kind; var edges = new HashSet<int>();
        bool exitBlock = kind == RailSignalKind.Normal;
        double exitLength = 0;
        while (spans.MoveNext())
        {
            var span = spans.Current;
            if (kind == RailSignalKind.Path) edges.Add(span.Edge.Id);
            else edges = _blocks[_block[span.Edge.Id]]; // Immutable until the next map rebuild; avoid copying a whole block per segment.
            if (exitBlock)
            {
                exitLength += span.Length;
                if (entry.Kind == RailSignalKind.Normal || exitLength >= train.Length + 4)
                {
                    units.Add((key, edges)); return Acquire(train, units, commit);
                }
            }
            bool boundary = _network.Signals.ContainsKey(new(span.Node, -1)) || _network.Signals.ContainsKey(new(span.Node, 1));
            if (!boundary) continue;
            if (!exitBlock && !_network.Signals.ContainsKey(new(span.Node, span.Side))) return false;
            units.Add((key, edges));
            if (exitBlock)
            {
                if (entry.Kind != RailSignalKind.Normal && exitLength < train.Length + 4) return false;
                return Acquire(train, units, commit);
            }
            key = new(span.Node, span.Side);
            if (!seen.Add(key)) return false;
            kind = _network.Signals[key].Kind;
            exitBlock = kind == RailSignalKind.Normal; edges = new();
        }
        if (!exitBlock || edges.Count == 0) return false; // Chain/path entries require a normal exit.
        if (entry.Kind != RailSignalKind.Normal && exitLength < train.Length + 4) return false;
        units.Add((key, edges));
        return Acquire(train, units, commit);
    }
    private bool Acquire(SignalTrain train, List<(SignalKey Key, HashSet<int> Edges)> units, bool commit)
    {
        if (units.Any(unit => !Clear(unit.Edges, train.Id))) return false;
        if (!commit) return true;
        foreach (var unit in units)
            if (!_leases.Any(lease => lease.Train == train.Id && lease.Signal == unit.Key))
                _leases.Add(new(train, unit.Key, unit.Edges));
        return true;
    }
    private bool Conflicts(int a, int b)
    {
        if (a == b) return true;
        if (!_network.Edges.TryGetValue(a, out var first) || !_network.Edges.TryGetValue(b, out var second)) return false;
        int common = first.A == second.A || first.A == second.B ? first.A : first.B == second.A || first.B == second.B ? first.B : 0;
        if (common != 0) return _incident[common].Count >= 3; // Junction conflict; ordinary signal boundaries remain independent.
        var key = a < b ? (a, b) : (b, a);
        double width = Math.Max(3.5, (first.Gauge + second.Gauge) / 2000.0 + 2);
        var ba = Bounds(first); var bb = Bounds(second);
        bool nearby = ba.Min.X <= bb.Max.X + width && bb.Min.X <= ba.Max.X + width
            && ba.Min.Z <= bb.Max.Z + width && bb.Min.Z <= ba.Max.Z + width
            && ba.Min.Y <= bb.Max.Y + 4.5 && bb.Min.Y <= ba.Max.Y + 4.5;
        // Far-away traffic must not evict the useful nearby crossing tests from the bounded cache.
        if (!nearby) return false;
        if (_crossings.TryGetValue(key, out bool result)) return result;
        result = GeometricConflict(_placement.Curve(first), _placement.Curve(second), width);
        if (_crossings.Count >= 8192) _crossings.Clear();
        _crossings[key] = result; return result;
    }
    private (TrackVector Min, TrackVector Max) Bounds(TrackEdge edge)
    {
        if (_bounds.TryGetValue(edge.Id, out var box)) return box;
        var p = _network.WorldControls(edge);
        box = (new(p.Min(v => v.X), p.Min(v => v.Y), p.Min(v => v.Z)), new(p.Max(v => v.X), p.Max(v => v.Y), p.Max(v => v.Z)));
        _bounds.Add(edge.Id, box); return box;
    }
    private static bool GeometricConflict(TrackCurve a, TrackCurve b, double width)
    {
        bool Overlap(Func<TrackVector, double> coordinate, double margin) =>
            a.Controls.Min(coordinate) <= b.Controls.Max(coordinate) + margin && b.Controls.Min(coordinate) <= a.Controls.Max(coordinate) + margin;
        if (!Overlap(p => p.X, width) || !Overlap(p => p.Z, width) || !Overlap(p => p.Y, 4.5)) return false;
        // Only nearby crossing candidates reach this test. Sample short chords in double precision.
        int na = Math.Clamp((int)Math.Ceiling(a.Length / 4), 1, 4096), nb = Math.Clamp((int)Math.Ceiling(b.Length / 4), 1, 4096);
        var lastA = a.PointAt(0);
        for (int i = 1; i <= na; i++)
        {
            var nextA = a.PointAt(i / (double)na); var lastB = b.PointAt(0);
            for (int j = 1; j <= nb; j++)
            {
                var nextB = b.PointAt(j / (double)nb);
                TrackVector Envelope(TrackVector point) => new(point.X, point.Y * width / 4.5, point.Z);
                if (SegmentDistance(Envelope(lastA), Envelope(nextA), Envelope(lastB), Envelope(nextB)) < width) return true;
                lastB = nextB;
            }
            lastA = nextA;
        }
        return false;
    }
    private static double SegmentDistance(TrackVector p, TrackVector q, TrackVector r, TrackVector s)
    {
        double Dot(TrackVector x, TrackVector y) => x.X * y.X + x.Y * y.Y + x.Z * y.Z;
        var u = q - p; var v = s - r; var w = p - r;
        double a = Dot(u, u), b = Dot(u, v), c = Dot(v, v), d = Dot(u, w), e = Dot(v, w), determinant = a * c - b * b;
        double x = determinant > 1e-10 ? Math.Clamp((b * e - c * d) / determinant, 0, 1) : 0;
        double y = c > 1e-10 ? Math.Clamp((b * x + e) / c, 0, 1) : 0;
        x = a > 1e-10 ? Math.Clamp((b * y - d) / a, 0, 1) : 0;
        return (w + u * x - v * y).Length;
    }
}
