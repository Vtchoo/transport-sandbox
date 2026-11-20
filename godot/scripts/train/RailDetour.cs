using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record RailDetour(TrainRoute Route, int[] Before, int[] After, TrainPosition Rejoin, double ExtraDistance, int IndexShift);

/// <summary>Small live-traffic search; never rebuilds or invalidates shared destination fields.</summary>
public sealed class RailDetourSearch
{
    public const int MaxStates = 256, MaxOriginalLegs = 128;
    public const double MaxDistance = 2000, MaxExtraDistance = 500;
    public long Searches { get; private set; }
    public long ExpandedStates { get; private set; }
    public long Successes { get; private set; }

    public RailDetour? Find(TrackNetwork network, TrainPlacement placement, SignalTrain train, double blockedDistance,
        double orderDistance, RailRoutingPolicy? policy, Func<int, bool> available, Func<RailDetour, bool> accept)
    {
        Searches++;
        var leading = train.Route.Sample(train.Position, train.Direction * train.Length / 2).Position;
        var original = train.Route.Copy().Trace(leading, train.Direction, Math.Min(MaxDistance, orderDistance))
            .Take(MaxOriginalLegs).ToArray();
        var goals = new Dictionary<int, (RouteSpan Span, int Index)>();
        for (int i = 1; i < original.Length; i++)
        {
            var span = original[i];
            if (span.Distance > blockedDistance + 2.01)
                goals.TryAdd(RailRouter.StateId(span.Edge.Id, span.Direction), (span, i));
        }
        if (goals.Count == 0) return null;
        int initial = RailRouter.StateId(leading.Edge, leading.Facing * train.Direction);
        var costs = new Dictionary<int, double> { [initial] = 0 };
        var previous = new Dictionary<int, int>();
        var clear = new Dictionary<int, bool>();
        var queue = new PriorityQueue<int, (double Cost, int State)>(); queue.Enqueue(initial, (0, initial));
        int expanded = 0, candidates = 0;
        while (queue.TryDequeue(out int state, out var priority) && expanded < MaxStates)
        {
            if (priority.Cost != costs[state]) continue;
            expanded++; ExpandedStates++;
            if (state != initial && goals.TryGetValue(state, out var goal) && priority.Cost <= goal.Span.Distance + MaxExtraDistance)
            {
                var path = new List<int> { state };
                for (int at = state; previous.TryGetValue(at, out int parent); at = parent) path.Add(parent);
                path.Reverse();
                var before = original.Take(goal.Index + 1).Select(span => RailRouter.StateId(span.Edge.Id, span.Direction)).ToArray();
                if (!path.SequenceEqual(before))
                {
                    int[] after = path.ToArray();
                    var candidate = new RailDetour(train.Route.WithDetour(leading, train.Direction, after, goal.Span.Position),
                        before, after, goal.Span.Position, priority.Cost - goal.Span.Distance,
                        (after.Length - before.Length) * train.Route.TravelStep(leading, train.Direction));
                    if (accept(candidate)) { Successes++; return candidate; }
                    if (++candidates >= 4) return null;
                }
            }
            if (!network.Edges.TryGetValue(state / 2, out var edge)) continue;
            int direction = RailRouter.Direction(state), node = direction > 0 ? edge.B : edge.A;
            if (!network.SignalAllows(node, -TrackNetwork.SideAt(edge, node))) continue;
            double length = placement.Curve(edge).Length;
            if (state == initial) length *= direction > 0 ? 1 - leading.U : leading.U;
            foreach (var next in network.Continuations(edge, direction).OrderBy(edge => edge.Id))
            {
                if (!clear.TryGetValue(next.Id, out bool free)) clear[next.Id] = free = available(next.Id);
                if (!free) continue;
                int successor = RailRouter.StateId(next.Id, next.A == node ? 1 : -1);
                double penalty = policy != null && next.Station != 0 && next.Station != edge.Station && !policy.Allows(next.Station)
                    ? RailRoutingPolicy.UnorderedStationPenalty : 0;
                double cost = priority.Cost + length + penalty;
                if (cost > MaxDistance || costs.TryGetValue(successor, out double old) && cost >= old) continue;
                if (costs.Count >= MaxStates * 2) return null;
                costs[successor] = cost; previous[successor] = state; queue.Enqueue(successor, (cost, successor));
            }
        }
        return null;
    }
}
