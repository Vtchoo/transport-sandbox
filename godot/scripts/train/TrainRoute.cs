using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed class RouteEnd(double distance) : InvalidOperationException("End of connected track.")
{
    public double Distance { get; } = distance;
}

/// <summary>Ordered itinerary: each visit to a junction or loop has its own route index.</summary>
public sealed class TrainRoute
{
    private readonly TrainPlacement _placement;
    private readonly Dictionary<int, (int Edge, int Facing)> _legs = new();
    private uint _random;
    public TrainRoute(TrainPlacement placement, TrainPosition position, uint seed)
    {
        _placement = placement;
        _random = seed;
        _legs[position.RouteIndex] = (position.Edge, position.Facing);
    }
    public TrainRoute Copy()
    {
        var copy = new TrainRoute(_placement, default, _random);
        copy._legs.Clear();
        foreach (var leg in _legs)
            copy._legs.Add(leg.Key, leg.Value);
        return copy;
    }
    public TrainSample Sample(TrainPosition position, double distance)
    {
        if (!double.IsFinite(distance) || !double.IsFinite(position.U) || position.U < 0 || position.U > 1)
            throw new InvalidOperationException("Invalid train route position.");
        int index = position.RouteIndex;
        if (!_legs.TryGetValue(index, out var leg) || leg.Edge != position.Edge)
            throw new InvalidOperationException("Train route no longer matches its track.");
        int orientation = position.Facing == leg.Facing ? 1 : -1;
        int step = (distance < 0 ? -1 : 1) * orientation;
        double remaining = Math.Abs(distance), travelled = 0, u = position.U;
        var network = _placement.Network;
        for (int hop = 0; hop < 4096; hop++)
        {
            if (!network.Edges.TryGetValue(leg.Edge, out var edge))
                throw new InvalidOperationException("Track supporting this train was removed.");
            var curve = _placement.Curve(edge);
            double length = curve.Length;
            if (length < 1e-6)
                throw new InvalidOperationException("Track has no usable length.");
            int direction = step * leg.Facing;
            double available = length * (direction > 0 ? 1 - u : u);
            if (remaining <= available + 1e-8)
            {
                u = Math.Clamp(u + direction * remaining / length, 0, 1);
                int facing = leg.Facing * orientation;
                return new(curve.PointAt(u).Float(), curve.FrameAt(u).Forward.Float() * facing, new(edge.Id, u, facing, index));
            }
            remaining -= available;
            travelled += available;
            int node = direction > 0 ? edge.B : edge.A;
            var options = network.Incident(node).Where(e => e.Id != edge.Id && e.Gauge == edge.Gauge && TrackNetwork.SideAt(e, node) != TrackNetwork.SideAt(edge, node)).OrderBy(e => e.Id).ToArray();
            if (_legs.TryGetValue(index + step, out var next))
            {
                var segment = options.FirstOrDefault(e => e.Id == next.Edge);
                if (segment == null || (segment.A == node ? 1 : -1) != next.Facing * step)
                    throw new InvalidOperationException("Track changed along the train route.");
            }
            else
            {
                if (options.Length == 0)
                    throw new RouteEnd(travelled);
                _random = unchecked(_random * 1664525 + 1013904223);
                var chosen = options[(int)(_random / 4294967296.0 * options.Length)];
                next = (chosen.Id, (chosen.A == node ? 1 : -1) * step);
                _legs[index + step] = next;
            }
            index += step;
            leg = next;
            u = step * leg.Facing > 0 ? 0 : 1;
        }
        throw new InvalidOperationException("Train route exceeded the traversal limit.");
    }
    public double Clearance(TrainPosition position, double distance)
    {
        try
        {
            Sample(position, distance);
            return Math.Abs(distance);
        }
        catch (RouteEnd end) { return end.Distance; }
    }
    public double TurnRadius(TrainPosition position)
    {
        var center = Sample(position, 0);
        TrainSample Nearby(double d)
        {
            try
            {
                return Sample(position, d);
            }
            catch (RouteEnd) { return center; }
        }
        var a = Nearby(-2);
        var b = Nearby(2);
        var ta = new TrackVector(a.Tangent.X, 0, a.Tangent.Z).Unit();
        var tb = new TrackVector(b.Tangent.X, 0, b.Tangent.Z).Unit();
        // Double dot product avoids float rounding introducing phantom curvature on straight track.
        double dot = (double)ta.X * tb.X + (double)ta.Z * tb.Z;
        double angle = Math.Acos(Math.Clamp(dot, -1, 1));
        return angle < 1e-6 ? double.PositiveInfinity : Math.Sqrt(Math.Pow(b.Point.X - a.Point.X, 2) + Math.Pow(b.Point.Z - a.Point.Z, 2)) / (2 * Math.Sin(angle / 2));
    }
    public void Trim(TrainPosition position, double metres)
    {
        foreach (int direction in new[] { -1, 1 })
        {
            double distance = 0;
            for (int i = position.RouteIndex + direction; _legs.TryGetValue(i, out var leg); i += direction)
            {
                if (_placement.Network.Edges.TryGetValue(leg.Edge, out var edge))
                    distance += _placement.Curve(edge).Length;
                else
                    distance = double.PositiveInfinity;
                if (distance > metres)
                    _legs.Remove(i);
            }
        }
    }
}
