using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public readonly record struct TrainPosition(int Edge, double U, int Facing = 1, int RouteIndex = 0);
public sealed record TrainSample(Vector3 Point, Vector3 Tangent, TrainPosition Position);
public sealed record VehiclePose(Vector3 Center, Vector3 Front, Vector3 Rear, Vector3 FrontDirection, Vector3 RearDirection, VehiclePose[] Sections)
{
    public Vector3 BodyCenter => (Front + Rear) / 2;
    public Basis Basis => TrackRenderer.Frame(Front - Rear);
}
/// <summary>Arc-distance walking, signed connections, station-end snapping and footprints ported from placement.ts.</summary>
public sealed class TrainPlacement : IDisposable
{
    public TrackNetwork Network { get; }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TrackCurve> _curves = new();
    public TrainPlacement(TrackNetwork network) { Network = network; Network.Changed += Invalidate; Network.Space.Rebased += Rebased; }
    private void Invalidate() => _curves.Clear();
    private void Rebased(TrackVector worldShift) => Invalidate();
    public void Dispose() { Network.Changed -= Invalidate; Network.Space.Rebased -= Rebased; }
    public TrackCurve Curve(TrackEdge edge)
    {
        if (!_curves.TryGetValue(edge.Id, out var curve)) curve = _curves.GetOrAdd(edge.Id, new TrackCurve(Network.PreciseControls(edge)));
        return curve;
    }
    public double CachedLength(int edge) => _curves.TryGetValue(edge, out var curve) ? curve.Length : -1;
    public TrainSample Sample(TrainPosition position, double distance)
    {
        if (!Network.Edges.TryGetValue(position.Edge, out var edge) || !double.IsFinite(position.U) || !double.IsFinite(distance) || position.U < 0 || position.U > 1) throw new InvalidOperationException("Choose an existing track segment.");
        int gauge = edge.Gauge, sign = distance < 0 ? -1 : 1, direction = sign * position.Facing;
        double remaining = Math.Abs(distance), u = position.U;
        for (int hop = 0; hop < 256; hop++)
        {
            var curve = Curve(edge); double length = curve.Length, available = length * (direction > 0 ? 1 - u : u);
            if (!double.IsFinite(length) || length < 1e-6) throw new InvalidOperationException("Track segment has no usable length.");
            if (remaining <= available + 1e-8)
            {
                u = Math.Clamp(u + direction * remaining / length, 0, 1);
                return new(curve.PointAt(u).Float(), curve.FrameAt(u).Forward.Float() * (direction * sign), new(edge.Id, u, direction * sign));
            }
            remaining -= available;
            int node = direction > 0 ? edge.B : edge.A;
            var next = Network.Incident(node).Where(e => e.Id != edge.Id && e.Gauge == gauge && TrackNetwork.SideAt(e, node) != TrackNetwork.SideAt(edge, node)).ToArray();
            if (next.Length == 0) throw new InvalidOperationException("Not enough connected track for the full vehicle.");
            if (next.Length > 1) throw new InvalidOperationException("Place clear of a turnout with multiple possible routes.");
            edge = next[0]; direction = edge.A == node ? 1 : -1; u = direction > 0 ? 0 : 1;
        }
        throw new InvalidOperationException("Track route is too complex for placement.");
    }
    public VehiclePose Pose(VehicleDefinition definition, TrainPosition position, int facing = 1, Func<TrainPosition, double, TrainSample>? sample = null)
    {
        sample ??= Sample;
        if (!Network.Edges.TryGetValue(position.Edge, out var edge)) throw new InvalidOperationException("Track supporting this vehicle was removed.");
        if (!definition.Models.ContainsKey(edge.Gauge)) throw new InvalidOperationException("This train is incompatible with this track gauge.");
        position = position with { Facing = position.Facing * facing };
        sample(position, definition.Length / 2); sample(position, -definition.Length / 2);
        var center = sample(position, 0); var front = sample(position, definition.BogieCenters / 2); var rear = sample(position, -definition.BogieCenters / 2);
        if (definition.Sections.Count == 0 && front.Point.DistanceTo(rear.Point) < definition.BogieCenters * .94) throw new InvalidOperationException("This curve is too tight for the vehicle's bogie spacing.");
        return new(center.Point, front.Point, rear.Point, front.Tangent, rear.Tangent,
            definition.Sections.Select(s => Pose(s.Definition, sample(position, s.Offset).Position, sample: sample)).ToArray());
    }
    public VehiclePose[] Poses(TrainConsist train, TrainPosition position, Func<TrainPosition, double, TrainSample>? sample = null)
    {
        sample ??= Sample;
        if (train.Items.Count == 0) throw new InvalidOperationException("Add at least one vehicle to this train.");
        if (!Network.Edges.TryGetValue(position.Edge, out var edge) || !TrainBuilder.Gauges(train.Items.Select(i => i.Definition)).Contains(edge.Gauge)) throw new InvalidOperationException("This train is incompatible with this track gauge.");
        var offsets = train.Offsets;
        return train.Items.Select((item, i) => Pose(item.Definition, sample(position, offsets[i]).Position, item.Facing, sample)).ToArray();
    }
    public (TrainPosition Incoming, TrainPosition Combined) Append(TrainPosition position, double oldLength, double incomingLength, bool front)
    {
        int sign = front ? 1 : -1;
        return (Sample(position, sign * (oldLength / 2 + .12 + incomingLength / 2)).Position, Sample(position, sign * (incomingLength + .12) / 2).Position);
    }
    public (TrainPosition Position, int Station)? SnapStation(TrainPosition position, double length, double threshold = 5)
    {
        if (!double.IsFinite(length) || length <= 0 || !Network.Edges.TryGetValue(position.Edge, out var edge)) return null;
        var ends = new Dictionary<(int, int), int>();
        foreach (var e in Network.Edges.Values.Where(e => e.Station != 0))
            foreach (int u in new[] { 0, 1 })
            {
                int node = u == 0 ? e.A : e.B;
                if (!Network.Incident(node).Any(other => other.Id != e.Id && other.Station == e.Station && other.Gauge == e.Gauge && TrackNetwork.SideAt(other, node) != TrackNetwork.SideAt(e, node))) ends[(e.Id, u)] = e.Station;
            }
        double travelled = 0, best = threshold + 1e-8, u0 = position.U, half = length / 2;
        int direction = position.Facing;
        (TrainPosition, int)? result = null;
        for (int hop = 0; hop < 256 && travelled <= half + threshold; hop++)
        {
            double distance = Curve(edge).Length;
            if (distance < 1e-6) break;
            foreach (int end in new[] { 0, 1 })
            {
                double d = travelled + (end - u0) * direction * distance, error = Math.Abs(d - half);
                if (!ends.TryGetValue((edge.Id, end), out int station) || d < -1e-8 || error >= best) continue;
                try { var center = Sample(position, d - half).Position; Sample(center, -half); Sample(center, half); result = (center, station); best = error; }
                catch (InvalidOperationException) { }
            }
            travelled += distance * (direction > 0 ? 1 - u0 : u0);
            if (travelled > half + threshold) break;
            int node = direction > 0 ? edge.B : edge.A;
            var next = Network.Incident(node).Where(e => e.Id != edge.Id && e.Gauge == edge.Gauge && TrackNetwork.SideAt(e, node) != TrackNetwork.SideAt(edge, node)).ToArray();
            if (next.Length != 1) break;
            edge = next[0]; direction = edge.A == node ? 1 : -1; u0 = direction > 0 ? 0 : 1;
        }
        return result;
    }
    public static bool Overlap(VehicleDefinition a, VehiclePose pa, VehicleDefinition b, VehiclePose pb, double margin = .08)
    {
        if (pa.Sections.Length != 0)
        {
            for (int i = 0; i < pa.Sections.Length; i++) if (Overlap(a.Sections[i].Definition, pa.Sections[i], b, pb, margin)) return true;
            return false;
        }
        if (pb.Sections.Length != 0)
        {
            for (int i = 0; i < pb.Sections.Length; i++) if (Overlap(a, pa, b.Sections[i].Definition, pb.Sections[i], margin)) return true;
            return false;
        }
        var delta = pb.BodyCenter - pa.BodyCenter;
        if (Math.Abs(delta.Y) > Math.Max(a.Height, b.Height)) return false;
        Vector3 Forward(VehiclePose p) { var v = p.Front - p.Rear; v.Y = 0; return v.Normalized(); }
        var af = Forward(pa); var bf = Forward(pb); var ar = new Vector3(af.Z, 0, -af.X); var br = new Vector3(bf.Z, 0, -bf.X);
        bool Separated(Vector3 axis)
        {
            double ra = Math.Abs(ar.Dot(axis)) * a.Width / 2 + Math.Abs(af.Dot(axis)) * (a.Length / 2 + margin);
            double rb = Math.Abs(br.Dot(axis)) * b.Width / 2 + Math.Abs(bf.Dot(axis)) * (b.Length / 2 + margin);
            return Math.Abs(delta.Dot(axis)) >= ra + rb;
        }
        return !Separated(ar) && !Separated(af) && !Separated(br) && !Separated(bf);
    }
}
