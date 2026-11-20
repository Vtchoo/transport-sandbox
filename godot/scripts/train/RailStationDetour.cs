using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record RailStationAlternative(TrainRoute Route, int[] States, TrainPosition Target, double Distance);

public sealed partial class RailDetourSearch
{
    /// <summary>Live station search accepts any platform end of the same station, with no original-route rejoin.</summary>
    public RailStationAlternative? FindStation(TrackNetwork network, TrainPlacement placement, SignalTrain train,
        IReadOnlyDictionary<int, double> targets, double originalDistance, RailRoutingPolicy policy,
        Func<int, bool> available, Func<RailStationAlternative, bool> accept, Func<int, int>? conflicts = null)
    {
        Searches++;
        var leading = train.Route.Sample(train.Position, train.Direction * train.Length / 2).Position;
        int initial = RailRouter.StateId(leading.Edge, leading.Facing * train.Direction);
        var costs = new Dictionary<int, double> { [initial] = 0 };
        var distances = new Dictionary<int, double> { [initial] = 0 };
        var previous = new Dictionary<int, int>(); var clear = new Dictionary<int, bool>();
        var queue = new PriorityQueue<int, (double Cost, int State)>(); queue.Enqueue(initial, (0, initial));
        RailStationAlternative? best = null; double bestCost = double.PositiveInfinity;
        RailStationAlternative? Finish() { if (best != null) Successes++; return best; }
        int expanded = 0, candidates = 0;
        while (queue.TryDequeue(out int state, out var priority) && expanded < MaxStates)
        {
            if (priority.Cost != costs[state] || !network.Edges.TryGetValue(state / 2, out var edge)) continue;
            if (priority.Cost >= bestCost) return Finish();
            expanded++; ExpandedStates++;
            int direction = RailRouter.Direction(state), node = direction > 0 ? edge.B : edge.A;
            double length = placement.Curve(edge).Length;
            double startU = state == initial ? leading.U : direction > 0 ? 0 : 1;
            if (targets.TryGetValue(state, out double u) && direction * (u - startU) >= 0
                && (direction * (u - (direction > 0 ? 1 : 0)) < -1e-8 || network.SignalAllows(node, -TrackNetwork.SideAt(edge, node))))
            {
                double distance = distances[state] + direction * (u - startU) * length;
                if (distance <= MaxDistance && distance <= originalDistance + MaxExtraDistance)
                {
                    var path = new List<int> { state };
                    for (int at = state; previous.TryGetValue(at, out int parent); at = parent) path.Add(parent);
                    path.Reverse(); int[] states = path.ToArray();
                    var route = train.Route.Copy(); route.SetPath(leading, train.Direction, states);
                    var candidate = new RailStationAlternative(route, states, route.PathTarget(leading, train.Direction, states, u), distance);
                    double cost = priority.Cost + direction * (u - startU) * length * (1 + (conflicts?.Invoke(edge.Id) ?? 0));
                    if (cost < bestCost && accept(candidate)) { best = candidate; bestCost = cost; }
                    if (++candidates >= 4) return Finish();
                }
                // Do not route through a destination platform just to reach another platform end.
                continue;
            }
            if (!network.SignalAllows(node, -TrackNetwork.SideAt(edge, node))) continue;
            double remaining = length * (direction > 0 ? 1 - startU : startU);
            foreach (var next in network.Continuations(edge, direction).OrderBy(e => e.Id))
            {
                if (!clear.TryGetValue(next.Id, out bool free)) clear[next.Id] = free = available(next.Id);
                if (!free) continue;
                int successor = RailRouter.StateId(next.Id, next.A == node ? 1 : -1);
                double penalty = next.Station != 0 && next.Station != edge.Station && !policy.Allows(next.Station)
                    ? RailRoutingPolicy.UnorderedStationPenalty : 0;
                if (penalty > MaxDistance) continue;
                double distance = distances[state] + remaining;
                double cost = priority.Cost + remaining * (1 + (conflicts?.Invoke(edge.Id) ?? 0)) + penalty;
                if (distance > MaxDistance || distance > originalDistance + MaxExtraDistance || costs.TryGetValue(successor, out double old) && cost >= old) continue;
                if (costs.Count >= MaxStates * 2) return Finish();
                costs[successor] = cost; distances[successor] = distance; previous[successor] = state; queue.Enqueue(successor, (cost, successor));
            }
        }
        return Finish();
    }
}
