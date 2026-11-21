using System;
using System.Collections.Generic;

namespace TransportSandbox;

public readonly record struct TrainDebugLeg(int Index, int Edge, int Direction, double From, double To, bool Detour);
public readonly record struct TrainRouteMetrics(double Length, double? Cost);

public sealed partial class TrainRoute
{
    // Diagnostic provenance only; never participates in routing, traversal or reservations.
    private readonly HashSet<int> _debugDetours = new();
    /// <summary>Remaining physical metres and routing-equivalent metres for the inspected itinerary.</summary>
    public TrainRouteMetrics Measure(IReadOnlyList<TrainDebugLeg> legs, Func<int, int>? conflicts,
        RailRoutingPolicy policy, int destinationStation = 0, bool penalizeStations = true, Func<int, double>? edgeLength = null)
    {
        double length = 0, cost = 0;
        int previousStation = 0;
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            if (!_placement.Network.Edges.TryGetValue(leg.Edge, out var edge))
                throw new InvalidOperationException("Track in the inspected itinerary was removed.");
            double distance = (edgeLength?.Invoke(edge.Id) ?? _placement.Curve(edge).Length) * Math.Abs(leg.To - leg.From);
            length += distance;
            cost += distance * (1 + (conflicts?.Invoke(edge.Id) ?? 0));
            // Match destination routing: charge an unrelated station once on entry,
            // not when starting inside it or traversing its consecutive segments.
            if (penalizeStations && i > 0 && edge.Station != 0 && edge.Station != previousStation
                && edge.Station != destinationStation && !policy.Allows(edge.Station))
                cost += RailRoutingPolicy.UnorderedStationPenalty;
            previousStation = edge.Station;
        }
        return new(length, conflicts == null ? null : cost);
    }
    internal void MarkDetour(TrainPosition leading, int direction, IReadOnlyList<int> before, IReadOnlyList<int> after)
    {
        int prefix = 0, suffix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix] == after[prefix]) prefix++;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix
            && before[before.Count - 1 - suffix] == after[after.Count - 1 - suffix]) suffix++;
        int step = TravelStep(leading, direction);
        for (int i = prefix; i < after.Count - suffix; i++) _debugDetours.Add(leading.RouteIndex + i * step);
    }

    /// <summary>Read only known itinerary occurrences; never sample or choose random junction exits.</summary>
    public TrainDebugLeg[] Inspect(TrainPosition start, int direction, TrainPosition? target, out bool truncated, int limit = 8192)
    {
        truncated = false;
        var output = new List<TrainDebugLeg>();
        if (!TryLeg(start.RouteIndex, out var first) || first.Edge != start.Edge) return output.ToArray();
        int step = (start.Facing == first.Facing ? 1 : -1) * direction;
        for (int i = 0; i <= limit; i++)
        {
            int index = start.RouteIndex + i * step;
            if (!TryLeg(index, out var leg) || !_placement.Network.Edges.ContainsKey(leg.Edge)) break;
            if (i == limit) { truncated = true; break; }
            int travel = leg.Facing * step;
            double from = i == 0 ? start.U : travel > 0 ? 0 : 1;
            bool end = target is { } t && t.RouteIndex == index && t.Edge == leg.Edge;
            double to = end ? target!.Value.U : travel > 0 ? 1 : 0;
            if ((to - from) * travel < -1e-8) break;
            output.Add(new(index, leg.Edge, travel, from, to, _debugDetours.Contains(index)));
            if (end) break;
        }
        return output.ToArray();
    }
}
