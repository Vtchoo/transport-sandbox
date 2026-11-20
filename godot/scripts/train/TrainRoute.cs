using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record WanderingStationStop(int Station, int EndRouteIndex, double Distance, int EndEdge);
public sealed record RouteSpan(TrackEdge Edge, TrainPosition Position, int Direction, double Distance, double Length, bool ReachesNode)
{
    public int Node => Direction > 0 ? Edge.B : Edge.A;
    public int Side => -TrackNetwork.SideAt(Edge, Node);
}

public sealed class RouteEnd(double distance) : InvalidOperationException("End of connected track.")
{
    public double Distance { get; } = distance;
}

public sealed class RouteBlocked(double distance) : InvalidOperationException("Track changed ahead of this train.")
{
    public double Distance { get; } = distance;
}

/// <summary>Ordered itinerary: each visit to a junction or loop has its own route index.</summary>
public sealed class TrainRoute
{
    private readonly TrainPlacement _placement;
    private readonly Dictionary<int, (int Edge, int Facing)> _legs = new();
    // A plan is immutable and independent of the bounded, materialized traversal history.
    private int[] _plan = Array.Empty<int>();
    private int _planStart, _planStep = 1;
    public int PlannedLegCount => _plan.Length;
    public int PlanRevision { get; private set; }
    public int CachedLegCount => _legs.Count;
    public bool BlockedAhead { get; private set; }
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
        copy._plan = _plan; copy._planStart = _planStart; copy._planStep = _planStep; copy.PlanRevision = PlanRevision;
        return copy;
    }
    /// <summary>Splice a forward detour into a copy, preserving occupied history and the original suffix.</summary>
    public TrainRoute WithDetour(TrainPosition leading, int direction, int[] states, TrainPosition rejoin)
    {
        int step = TravelStep(leading, direction);
        int skipped = (rejoin.RouteIndex - leading.RouteIndex) * step;
        if (skipped <= 0 || states.Length < 2 || states[0] != RailRouter.StateId(leading.Edge, leading.Facing * direction)
            || states[^1] != RailRouter.StateId(rejoin.Edge, rejoin.Facing * direction))
            throw new InvalidOperationException("Detour must rejoin a future occurrence in the same direction.");
        var copy = Copy();
        int shift = (states.Length - 1 - skipped) * step;
        copy._legs.Clear();
        foreach (var (index, leg) in _legs)
        {
            int progress = (index - leading.RouteIndex) * step;
            if (progress <= 0) copy._legs[index] = leg;
            else if (progress >= skipped) copy._legs[index + shift] = leg;
        }
        int suffix = (rejoin.RouteIndex - _planStart) * _planStep;
        copy._plan = _planStep == step && suffix >= 0 && suffix < _plan.Length
            ? states.Concat(_plan.Skip(suffix + 1)).ToArray() : states.ToArray();
        copy._planStart = leading.RouteIndex; copy._planStep = step;
        for (int i = 0; i < states.Length; i++)
            copy._legs[leading.RouteIndex + i * step] = (states[i] / 2, RailRouter.Direction(states[i]) * step);
        copy.PlanRevision++;
        return copy;
    }
    private bool TryLeg(int index, out (int Edge, int Facing) leg)
    {
        if (_legs.TryGetValue(index, out leg)) return true;
        long offset = ((long)index - _planStart) * _planStep;
        if (offset < 0 || offset >= _plan.Length) return false;
        int state = _plan[(int)offset];
        leg = (state / 2, RailRouter.Direction(state) * _planStep);
        return true;
    }
    public int TravelStep(TrainPosition position, int direction)
    {
        if (!TryLeg(position.RouteIndex, out var leg) || leg.Edge != position.Edge)
            throw new InvalidOperationException("Train route no longer matches its track.");
        return (position.Facing == leg.Facing ? 1 : -1) * direction;
    }
    public TrainPosition PathTarget(TrainPosition leading, int travelDirection, IReadOnlyList<int> states, double u)
    {
        int last = states[^1];
        return new(last / 2, u, RailRouter.Direction(last) * travelDirection,
            leading.RouteIndex + TravelStep(leading, travelDirection) * (states.Count - 1));
    }
    public void ClearPlan(TrainPosition leading, int travelDirection)
    {
        int step = TravelStep(leading, travelDirection);
        if (TryLeg(leading.RouteIndex, out var current)) _legs[leading.RouteIndex] = current;
        foreach (int index in _legs.Keys.Where(index => ((long)index - leading.RouteIndex) * step > 0).ToArray()) _legs.Remove(index);
        _plan = Array.Empty<int>();
        PlanRevision++;
    }
    /// <summary>Replace only future legs beyond the leading coupler; preserve occupied junctions.</summary>
    public void SetPath(TrainPosition leading, int travelDirection, IReadOnlyList<int> states)
    {
        if (states.Count == 0 || states[0] / 2 != leading.Edge || !TryLeg(leading.RouteIndex, out var current))
            throw new InvalidOperationException("Route start no longer matches the train.");
        int step = (leading.Facing == current.Facing ? 1 : -1) * travelDirection;
        foreach (int index in _legs.Keys.Where(index => (index - leading.RouteIndex) * step > 0).ToArray()) _legs.Remove(index);
        _legs[leading.RouteIndex] = current;
        _plan = states.ToArray(); _planStart = leading.RouteIndex; _planStep = step;
        PlanRevision++;
    }
    public TrainSample Sample(TrainPosition position, double distance)
    {
        if (!double.IsFinite(distance) || !double.IsFinite(position.U) || position.U < 0 || position.U > 1)
            throw new InvalidOperationException("Invalid train route position.");
        int index = position.RouteIndex;
        if (!TryLeg(index, out var leg) || leg.Edge != position.Edge)
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
            if (TryLeg(index + step, out var next))
            {
                if (!network.Edges.TryGetValue(next.Edge, out var segment) || segment.Id == edge.Id || segment.Gauge != edge.Gauge
                    || segment.A != node && segment.B != node || TrackNetwork.SideAt(segment, node) == TrackNetwork.SideAt(edge, node)
                    || (segment.A == node ? 1 : -1) != next.Facing * step)
                    throw new RouteBlocked(travelled);
            }
            else
            {
                var options = network.Incident(node).Where(e => e.Id != edge.Id && e.Gauge == edge.Gauge && TrackNetwork.SideAt(e, node) != TrackNetwork.SideAt(edge, node)).OrderBy(e => e.Id).ToArray();
                if (options.Length == 0)
                    throw new RouteEnd(travelled);
                _random = unchecked(_random * 1664525 + 1013904223);
                var chosen = options[(int)(_random / 4294967296.0 * options.Length)];
                next = (chosen.Id, (chosen.A == node ? 1 : -1) * step);
            }
            _legs[index + step] = next;
            index += step;
            leg = next;
            u = step * leg.Facing > 0 ? 0 : 1;
        }
        throw new InvalidOperationException("Train route exceeded the traversal limit.");
    }
    public double Clearance(TrainPosition position, double distance)
    {
        BlockedAhead = false;
        try
        {
            Sample(position, distance);
            return Math.Abs(distance);
        }
        catch (RouteEnd end) { return end.Distance; }
        catch (RouteBlocked blocked) { BlockedAhead = true; return blocked.Distance; }
    }
    /// <summary>Walk the already chosen itinerary. Traffic controls never choose junction exits.</summary>
    public IEnumerable<RouteSpan> Trace(TrainPosition start, int travelDirection, double distance)
    {
        var position = start; double walked = 0;
        for (int hop = 0; hop < 4096 && walked <= distance; hop++)
        {
            if (!_placement.Network.Edges.TryGetValue(position.Edge, out var edge)) yield break;
            int direction = position.Facing * travelDirection;
            double available = _placement.Curve(edge).Length * (direction > 0 ? 1 - position.U : position.U);
            double length = Math.Min(available, distance - walked);
            yield return new(edge, position, direction, walked, length, available <= distance - walked + 1e-8);
            if (walked + available >= distance) yield break;
            TrainPosition next;
            try { next = Sample(position, travelDirection * (available + .00001)).Position; }
            catch (InvalidOperationException) { yield break; }
            walked += available + .00001; position = next;
        }
    }
    /// <summary>Find the next platform run on this seeded itinerary, without choosing a destination or changing junction rules.</summary>
    public WanderingStationStop? NextStation(TrainPosition leading, int travelDirection, double lookahead,
        int servicedStation = 0, int servicedIndex = int.MinValue)
    {
        var location = leading;
        double travelled = 0;
        for (int hop = 0; hop < 4096; hop++)
        {
            if (!_placement.Network.Edges.TryGetValue(location.Edge, out var edge)) return null;
            double length = _placement.Curve(edge).Length;
            if (length < 1e-6) return null;
            int direction = location.Facing * travelDirection;
            double available = length * (direction > 0 ? 1 - location.U : location.U);
            if (edge.Station == 0 && travelled + available > lookahead) return null;
            TrainPosition? next = null;
            try { next = Sample(location, travelDirection * (available + .001)).Position; }
            catch (RouteEnd) { }
            catch (RouteBlocked) { }
            int nextStation = next is { } following ? _placement.Network.Edges[following.Edge].Station : 0;
            if (edge.Station != 0 && nextStation != edge.Station)
            {
                double distance = travelled + available;
                if (distance >= -.02 && (edge.Station != servicedStation || location.RouteIndex != servicedIndex))
                    return new(edge.Station, location.RouteIndex, Math.Max(0, distance), edge.Id);
            }
            if (next == null || travelled + available > lookahead) return null;
            travelled += available + .001;
            location = next.Value;
        }
        return null;
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
            catch (RouteBlocked) { return center; }
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
