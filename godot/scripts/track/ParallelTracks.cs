using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record ParallelTrackPlan(TrackNode Source, TrackNode? Target, float Offset, Vector3[] Controls, float LengthA, float LengthB);
public sealed partial class TrackNetwork
{
    public bool CanExtendParallel(TrackNode node, int side) => node.Group != 0 && Groups.TryGetValue(node.Group, out var group) &&
        group.Offsets.Count >= 2 && group.Offsets.Keys.All(id => CanExtend(Nodes[id], side));
    public Vector3[] Preview(TrackNode source, int side, Vector3 point, TrackNode? target = null, Vector3 endDirection = default)
    {
        var chord = point - source.Position;
        float length = chord.Length() / 3;
        var first = source.Position + (source.Group != 0 || Incident(source.Id).Count > 0 ? source.Axis * side * length : chord / 3);
        var second = point - (endDirection == Vector3.Zero ? chord.Normalized() : endDirection.Normalized()) * length;
        if (target != null && (target.Group != 0 || Incident(target.Id).Count > 0))
            second = point + target.Axis * (target.Axis.Dot(chord) >= 0 ? -1 : 1) * length;
        return new[] { source.Position, first, second, point };
    }
    public ParallelTrackPlan[] PreviewParallel(TrackNode source, int side, Vector3 point, TrackNode? target = null, Vector3 endDirection = default)
    {
        if (!CanExtendParallel(source, side))
            throw new InvalidOperationException("Choose a parallel group with no station-facing junctions.");
        var group = Groups[source.Group];
        ParallelGroup? targetGroup = target != null && target.Group != 0 ? Groups[target.Group] : null;
        if (target != null && (targetGroup == null || targetGroup == group))
            throw new InvalidOperationException("Connect to a different, matching parallel group.");
        if (point.DistanceTo(source.Position) < 1)
            throw new InvalidOperationException("Choose an endpoint at least 1 m away.");
        var baseCurve = new TrackCurve(Preview(source, side, point, target, endDirection));
        var forward = (baseCurve.Controls[3] - baseCurve.Controls[2]).Unit();
        double Dot(TrackVector a, TrackVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        int targetSign = targetGroup != null && Dot(forward * side, TrackVector.From(targetGroup.Axis)) < 0 ? -1 : 1;
        var used = new HashSet<int>();
        var plan = new List<ParallelTrackPlan>();
        foreach (var (id, value) in group.Offsets)
        {
            var node = Nodes[id];
            float offset = value - group.Offsets[source.Id];
            if (node.Gauge != source.Gauge)
                throw new InvalidOperationException("Parallel tracks must share a gauge.");
            TrackNode? matching = null;
            if (targetGroup != null)
            {
                foreach (var (candidate, targetOffset) in targetGroup.Offsets)
                    if (!used.Contains(candidate) && Math.Abs((targetOffset - targetGroup.Offsets[target!.Id]) * targetSign - offset) < .05)
                    {
                        matching = Nodes[candidate];
                        break;
                    }
                if (matching == null || matching.Gauge != source.Gauge)
                    throw new InvalidOperationException("Destination group must match every track's gauge and spacing.");
                used.Add(matching.Id);
            }
            TrackVector Sample(double t) => baseCurve.Point(t) + TrackFrame.From(baseCurve.Tangent(t)).Right * (offset * side);
            var start = TrackVector.From(node.Position);
            var end = matching != null ? TrackVector.From(matching.Position) : Sample(1);
            var d0 = TrackVector.From(node.Axis) * side;
            var d1 = forward;
            double aa = 0, ab = 0, bb = 0, ar = 0, br = 0;
            for (int i = 1; i < 64; i++)
            {
                double t = i / 64.0, u = 1 - t, b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
                var a = d0 * b1;
                var b = d1 * (-b2);
                var residual = Sample(t) - start * (b0 + b1) - end * (b2 + b3);
                aa += Dot(a, a);
                ab += Dot(a, b);
                bb += Dot(b, b);
                ar += Dot(a, residual);
                br += Dot(b, residual);
            }
            double determinant = aa * bb - ab * ab, aLength = (ar * bb - br * ab) / determinant, bLength = (br * aa - ar * ab) / determinant;
            if (!double.IsFinite(aLength + bLength) || aLength < .25 || bLength < .25)
                throw new InvalidOperationException("Parallel turn is too tight. Use a gentler curve.");
            var controls = new[] { start.Float(), (start + d0 * aLength).Float(), (end - d1 * bLength).Float(), end.Float() };
            var fitted = new TrackCurve(controls);
            for (int i = 0; i <= 128; i++)
            {
                double t = i / 128.0;
                if ((fitted.Point(t) - Sample(t)).Length > .25 || Dot(fitted.Tangent(t), baseCurve.Tangent(t)) <= 0 ||
                    i > 0 && Dot(Sample(t) - Sample(t - 1 / 128.0), baseCurve.Tangent(t)) <= 0)
                    throw new InvalidOperationException("Cannot keep parallel spacing here. Use a gentler curve or shorter segment.");
            }
            plan.Add(new(node, matching, offset, controls, (float)aLength, (float)bLength));
        }
        return plan.ToArray();
    }
    public (TrackNode Node, int Side, TrackEdge[] Edges) ExtendParallel(TrackNode source, int side, Vector3 point, TrackNode? target = null, Vector3 endDirection = default)
    {
        var plan = PreviewParallel(source, side, point, target, endDirection);
        int nodeCounter = _nextNode, edgeCounter = _nextEdge, groupCounter = _nextGroup;
        var addedNodes = new List<int>();
        var addedEdges = new List<int>();
        int createdGroup = 0;
        _changeDepth++;
        try
        {
            int reference = Array.FindIndex(plan, p => p.Source.Id == source.Id);
            var controls = plan[reference].Controls;
            var axis = (controls[3] - controls[2]).Normalized() * side;
            var targets = plan.Select(p =>
            {
                if (p.Target != null)
                    return p.Target;
                var node = AddNode(p.Controls[3], source.Gauge);
                node.Axis = axis;
                addedNodes.Add(node.Id);
                return node;
            }).ToArray();
            if (target == null)
            {
                createdGroup = _nextGroup++;
                var group = new ParallelGroup { Anchor = targets[reference].Id, Origin = targets[reference].Position, Axis = axis };
                Groups[createdGroup] = group;
                for (int i = 0; i < targets.Length; i++)
                {
                    targets[i].Group = createdGroup;
                    group.Offsets[targets[i].Id] = plan[i].Offset;
                }
            }
            var edges = new TrackEdge[plan.Length];
            for (int i = 0; i < plan.Length; i++)
            {
                var edge = ConnectNodes(plan[i].Source, targets[i], side) ?? throw new InvalidOperationException("Cannot connect every track in this group.");
                addedEdges.Add(edge.Id);
                edge.SideB = targets[i].Axis.Dot(plan[i].Controls[3] - plan[i].Controls[2]) >= 0 ? -1 : 1;
                edge.LengthA = plan[i].LengthA;
                edge.LengthB = plan[i].LengthB;
                edges[i] = edge;
            }
            return (targets[reference], -edges[reference].SideB, edges);
        }
        catch
        {
            foreach (int id in addedEdges)
                Edges.Remove(id);
            foreach (int id in addedNodes)
                Nodes.Remove(id);
            if (createdGroup != 0)
                Groups.Remove(createdGroup);
            _nextNode = nodeCounter;
            _nextEdge = edgeCounter;
            _nextGroup = groupCounter;
            throw;
        }
        finally { _changeDepth--; if (_changeDepth == 0 && _changePending) { _changePending = false; NotifyChanged(); } }
    }
}
