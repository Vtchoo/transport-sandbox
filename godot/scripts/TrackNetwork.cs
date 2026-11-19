using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed class TrackNode
{
    public int Id, Gauge, Group;
    public WorldSpace Space = new();
    public TrackVector WorldPosition;
    public Vector3 Position { get => Space.ToLocal(WorldPosition); set => WorldPosition = Space.ToWorld(value); }
    public Vector3 Axis = Vector3.Back;
}
public sealed class TrackEdge
{
    public int Id, A, B, Gauge, SideA, SideB, Station;
    public float LengthA, LengthB;
    public List<int> Platforms = new();
    public TrackEdge Clone() => new()
    {
        Id = Id,
        A = A,
        B = B,
        Gauge = Gauge,
        SideA = SideA,
        SideB = SideB,
        Station = Station,
        LengthA = LengthA,
        LengthB = LengthB,
        Platforms = new(Platforms)
    };
}
public sealed class ParallelGroup
{
    public int Anchor;
    public WorldSpace Space = new();
    public TrackVector WorldOrigin;
    public Vector3 Origin { get => Space.ToLocal(WorldOrigin); set => WorldOrigin = Space.ToWorld(value); }
    public Vector3 Axis;
    public Dictionary<int, float> Offsets = new();
}
/// <summary>Gauge-aware signed-endpoint graph. Scene independent except Curve3D baking.</summary>
public sealed partial class TrackNetwork
{
    public WorldSpace Space = new();
    public event Action? Changed;
    private int _changeDepth;
    private bool _changePending;
    public void NotifyChanged()
    {
        if (_changeDepth > 0)
            _changePending = true;
        else
            Changed?.Invoke();
    }
    public readonly Dictionary<int, TrackNode> Nodes = new();
    public readonly Dictionary<int, TrackEdge> Edges = new();
    public readonly Dictionary<int, ParallelGroup> Groups = new();
    private int _nextNode = 1, _nextEdge = 1, _nextGroup = 1, _nextStation = 1;
    public static readonly int[] Gauges = { 1435, 914, 1000, 1067, 1520, 1524, 1600, 1668, 1676, 762, 610 };
    public TrackNode AddNode(Vector3 point, int gauge = 1435)
    {
        if (!Gauges.Contains(gauge))
            throw new ArgumentOutOfRangeException(nameof(gauge));
        var node = new TrackNode { Space = Space, Id = _nextNode++, Position = point, Gauge = gauge };
        Nodes[node.Id] = node;
        return node;
    }
    public List<TrackEdge> Incident(int id) => Edges.Values.Where(e => e.A == id || e.B == id).ToList();
    public static int SideAt(TrackEdge edge, int node) => edge.A == node ? edge.SideA : edge.SideB;
    public bool CanExtend(TrackNode node, int side) => !Incident(node.Id).Any(e => e.Station != 0 && SideAt(e, node.Id) == side);
    public TrackEdge? ConnectNodes(TrackNode a, TrackNode b, int side = 0, Vector3 endDirection = default)
    {
        if (a.Id == b.Id || a.Gauge != b.Gauge || a.Position.DistanceTo(b.Position) < 1)
            return null;
        if (Edges.Values.Any(e => e.A == a.Id && e.B == b.Id || e.A == b.Id && e.B == a.Id))
            return null;
        var chord = b.Position - a.Position;
        bool aLocked = Incident(a.Id).Count != 0 || a.Group != 0, bLocked = Incident(b.Id).Count != 0 || b.Group != 0;
        if (side == 0)
            side = aLocked && a.Axis.Dot(chord) < 0 ? -1 : 1;
        int bSide = bLocked && b.Axis.Dot(chord) < 0 ? 1 : -1;
        if (!CanExtend(a, side) || !CanExtend(b, bSide))
            return null;
        if (!aLocked)
            a.Axis = chord.Normalized() * side;
        if (!bLocked)
            b.Axis = endDirection != Vector3.Zero ? endDirection.Normalized() : chord.Normalized();
        var edge = new TrackEdge
        {
            Id = _nextEdge++,
            A = a.Id,
            B = b.Id,
            Gauge = a.Gauge,
            SideA = side,
            SideB = bSide,
            LengthA = chord.Length() / 3,
            LengthB = chord.Length() / 3
        };
        Edges[edge.Id] = edge;
        NotifyChanged();
        return edge;
    }
    public TrackVector[] WorldControls(TrackEdge edge)
    {
        var a = Nodes[edge.A]; var b = Nodes[edge.B];
        return new[] { a.WorldPosition, a.WorldPosition + TrackVector.From(a.Axis) * (edge.SideA * edge.LengthA), b.WorldPosition + TrackVector.From(b.Axis) * (edge.SideB * edge.LengthB), b.WorldPosition };
    }
    public TrackVector[] PreciseControls(TrackEdge edge) => WorldControls(edge).Select(p => p - Space.Origin).ToArray();
    public Vector3[] Controls(TrackEdge edge)
    {
        return PreciseControls(edge).Select(p => p.Float()).ToArray();
    }
    public static Vector3 Bezier(Vector3[] p, float t)
    {
        float u = 1 - t;
        return p[0] * u * u * u + p[1] * 3 * u * u * t + p[2] * 3 * u * t * t + p[3] * t * t * t;
    }
    public Vector3 Point(TrackEdge e, float t) => Bezier(Controls(e), t);
    public Vector3 Tangent(TrackEdge e, float t)
    {
        var p = Controls(e);
        return ((p[1] - p[0]) * 3 * (1 - t) * (1 - t) + (p[2] - p[1]) * 6 * (1 - t) * t + (p[3] - p[2]) * 3 * t * t).Normalized();
    }
    public static Vector3 Right(Vector3 axis)
    {
        var r = Vector3.Up.Cross(axis);
        return r.LengthSquared() > 0.000001f ? r.Normalized() : Vector3.Right;
    }
    public Curve3D Curve(TrackEdge edge)
    {
        var p = Controls(edge);
        var curve = new Curve3D { BakeInterval = 0.5f };
        curve.AddPoint(p[0], Vector3.Zero, p[1] - p[0]);
        curve.AddPoint(p[3], p[2] - p[3], Vector3.Zero);
        return curve;
    }
    public void SetDirection(TrackNode node, Vector3 direction)
    {
        if (direction.LengthSquared() < 0.000001f)
            return;
        node.Axis = direction.Normalized();
        if (node.Group == 0)
            return;
        var group = Groups[node.Group];
        group.Axis = node.Axis;
        group.Origin = node.Position - Right(node.Axis) * group.Offsets[node.Id];
        foreach (var (id, offset) in group.Offsets)
        {
            Nodes[id].Axis = node.Axis;
            Nodes[id].Position = group.Origin + Right(node.Axis) * offset;
        }
    }
    public void MoveNode(TrackNode node, Vector3 position)
    {
        var delta = position - node.Position;
        if (node.Group == 0)
            node.Position = position;
        else
        {
            var group = Groups[node.Group];
            group.Origin += delta;
            foreach (int id in group.Offsets.Keys)
                Nodes[id].Position += delta;
        }
        NotifyChanged();
    }
    public void SetHandle(TrackEdge edge, int nodeId, Vector3 position)
    {
        var node = Nodes[nodeId];
        int side = SideAt(edge, nodeId);
        var direction = position - node.Position;
        if (direction.Length() < 0.25f)
            return;
        SetDirection(node, direction.Normalized() * side);
        IEnumerable<int> ids = node.Group == 0 ? new[] { nodeId } : Groups[node.Group].Offsets.Keys;
        foreach (int id in ids)
            foreach (var other in Incident(id))
                if (SideAt(other, id) == side)
                {
                    if (other.A == id)
                        other.LengthA = direction.Length();
                    else
                        other.LengthB = direction.Length();
                }
        NotifyChanged();
    }
    public TrackNode AddParallel(TrackNode node, int side)
    {
        float spacing = node.Gauge / 1000f + 2.6f;
        bool hasPlatform = Incident(node.Id).Any(edge => edge.Platforms.Contains(side * (edge.A == node.Id ? edge.SideA : -edge.SideB)));
        if (hasPlatform)
            spacing = Math.Max(spacing, Math.Max(1.75f, node.Gauge / 2000f + 1.05f) * 2) + 6;
        if (node.Group == 0)
        {
            node.Group = _nextGroup++;
            Groups[node.Group] = new()
            {
                Space = Space,
                Anchor = node.Id,
                Origin = node.Position,
                Axis = node.Axis,
                Offsets = new()
                {
                    [node.Id] = 0
                }
            };
        }
        var group = Groups[node.Group];
        float offset = group.Offsets[node.Id] + side * spacing;
        while (group.Offsets.Values.Any(o => Math.Abs(o - offset) < 0.05))
            offset += side * spacing;
        var added = AddNode(group.Origin + Right(group.Axis) * offset, node.Gauge);
        added.Axis = node.Axis;
        added.Group = node.Group;
        group.Offsets[added.Id] = offset;
        NotifyChanged();
        return added;
    }
    public void SetParallelOffset(TrackNode node, float offset)
    {
        if (node.Group == 0 || !float.IsFinite(offset))
            return;
        var group = Groups[node.Group];
        if (node.Id == group.Anchor)
            return;
        group.Offsets[node.Id] = offset;
        node.Position = group.Origin + Right(group.Axis) * offset;
        NotifyChanged();
    }
    public void DeleteNode(int id)
    {
        if (!Nodes.TryGetValue(id, out var node))
            return;
        foreach (var edge in Incident(id))
            Edges.Remove(edge.Id);
        if (node.Group != 0)
        {
            var group = Groups[node.Group];
            group.Offsets.Remove(id);
            if (group.Offsets.Count < 2)
            {
                foreach (int remaining in group.Offsets.Keys)
                    Nodes[remaining].Group = 0;
                Groups.Remove(node.Group);
            }
            else if (group.Anchor == id)
            {
                var (anchor, offset) = group.Offsets.First();
                group.Anchor = anchor;
                group.Origin = Nodes[anchor].Position;
                foreach (int member in group.Offsets.Keys.ToArray())
                    group.Offsets[member] -= offset;
            }
        }
        Nodes.Remove(id);
        NotifyChanged();
    }
    public TrackNode? Split(TrackEdge edge, float t)
    {
        if (t <= 0 || t >= 1 || !Edges.ContainsKey(edge.Id))
            return null;
        var p = Controls(edge);
        var q0 = p[0].Lerp(p[1], t);
        var q1 = p[1].Lerp(p[2], t);
        var q2 = p[2].Lerp(p[3], t);
        var r0 = q0.Lerp(q1, t);
        var r1 = q1.Lerp(q2, t);
        var location = r0.Lerp(r1, t);
        if (location.DistanceTo(p[0]) < 1 || location.DistanceTo(p[3]) < 1 || r0.DistanceSquaredTo(r1) < 0.000001)
            return null;
        var node = AddNode(location, edge.Gauge);
        node.Axis = (r1 - r0).Normalized();
        var a = edge.Clone();
        var b = edge.Clone();
        a.Id = _nextEdge++;
        b.Id = _nextEdge++;
        a.B = node.Id;
        a.SideB = -1;
        a.LengthA *= t;
        a.LengthB = location.DistanceTo(r0);
        b.A = node.Id;
        b.SideA = 1;
        b.LengthA = location.DistanceTo(r1);
        b.LengthB *= 1 - t;
        Edges.Remove(edge.Id);
        Edges[a.Id] = a;
        Edges[b.Id] = b;
        NotifyChanged();
        return node;
    }
    public bool CanMerge(int id)
    {
        var es = Incident(id);
        if (es.Count != 2 || SideAt(es[0], id) == SideAt(es[1], id) || es[0].Station != es[1].Station)
            return false;
        int a = es[0].A == id ? es[0].B : es[0].A, b = es[1].A == id ? es[1].B : es[1].A;
        return a != b && Nodes[a].Position.DistanceTo(Nodes[b].Position) >= 1 && !Edges.Values.Any(e => e.A == a && e.B == b || e.A == b && e.B == a);
    }
    public TrackEdge? Merge(int id)
    {
        if (!CanMerge(id))
            return null;
        var es = Incident(id);
        int a = es[0].A == id ? es[0].B : es[0].A, b = es[1].A == id ? es[1].B : es[1].A;
        var edge = es[0].Clone();
        edge.Id = _nextEdge++;
        edge.A = a;
        edge.B = b;
        edge.SideA = SideAt(es[0], a);
        edge.SideB = SideAt(es[1], b);
        edge.LengthA = es[0].A == a ? es[0].LengthA : es[0].LengthB;
        edge.LengthB = es[1].A == b ? es[1].LengthA : es[1].LengthB;
        edge.Platforms = es[0].Platforms.Select(s => s * (es[0].A == a ? 1 : -1)).Concat(es[1].Platforms.Select(s => s * (es[1].B == b ? 1 : -1))).Distinct().ToList();
        DeleteNode(id);
        Edges[edge.Id] = edge;
        NotifyChanged();
        return edge;
    }
    public void Straighten(TrackEdge edge)
    {
        var direction = (Nodes[edge.B].Position - Nodes[edge.A].Position).Normalized();
        SetDirection(Nodes[edge.A], direction * edge.SideA);
        SetDirection(Nodes[edge.B], -direction * edge.SideB);
        edge.LengthA = Nodes[edge.A].Position.DistanceTo(Nodes[edge.B].Position) / 3;
        edge.LengthB = edge.LengthA;
        NotifyChanged();
    }
    public bool CanPlaceStation(TrackEdge edge, int side) => side is -1 or 1 && Edges.TryGetValue(edge.Id, out var current) && ReferenceEquals(edge, current) &&
        !edge.Platforms.Contains(side) && Incident(edge.A).Count <= 2 && Incident(edge.B).Count <= 2;
    public bool RemovePlatform(TrackEdge edge, int side)
    {
        if (!Edges.TryGetValue(edge.Id, out var current) || !ReferenceEquals(edge, current) || !edge.Platforms.Remove(side))
            return false;
        if (edge.Platforms.Count == 0)
            edge.Station = 0;
        NotifyChanged();
        return true;
    }
    public bool AddStation(TrackEdge edge, int side)
    {
        if (!CanPlaceStation(edge, side))
            return false;
        if (edge.Station == 0)
        {
            var neighbors = new HashSet<int> { edge.A, edge.B };
            foreach (int id in new[] { edge.A, edge.B })
                if (Nodes[id].Group != 0)
                    neighbors.UnionWith(Groups[Nodes[id].Group].Offsets.Keys);
            var stations = Edges.Values.Where(e => e.Station != 0 && (neighbors.Contains(e.A) || neighbors.Contains(e.B))).Select(e => e.Station).ToHashSet();
            edge.Station = stations.Count == 0 ? _nextStation++ : stations.Min();
            foreach (var e in Edges.Values)
                if (stations.Contains(e.Station))
                    e.Station = edge.Station;
        }
        edge.Platforms.Add(side);
        NotifyChanged();
        return true;
    }
    public List<TrackEdge> Continuations(TrackEdge edge, int direction)
    {
        int node = direction == 1 ? edge.B : edge.A, incoming = SideAt(edge, node);
        return Incident(node).Where(e => e.Id != edge.Id && SideAt(e, node) != incoming && e.Gauge == edge.Gauge).ToList();
    }
}
