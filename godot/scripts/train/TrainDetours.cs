using System;
using System.Linq;

namespace TransportSandbox;

public partial class TrainEditor
{
    public RailDetourSearch Detours { get; } = new();
    public long DetourReplanAttempts { get; private set; }
    public long DetourReplans { get; private set; }
    private int _detourBudget;
    private int _stationTargetRevision = -1;
    private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, double>> _stationTargets = new();
    private bool TryTrafficDetour(ParkedTrain parked, TrainConsist train, SignalLimit blocked, double lookahead)
    {
        using var probe = FrameProbe.Measure("detours");
        if (_detourBudget <= 0 || parked.DetourCooldown > 0 || blocked.CanReverse || !double.IsFinite(blocked.Distance)) return false;
        if (parked.Journey is { ViaDeadEnd: true } or { NeedsReplan: true } or { Reachable: false }) return false;
        _detourBudget--; parked.DetourCooldown = 2 + train.Id % 7 * .13;
        var route = RouteFor(parked);
        var input = new SignalTrain(train.Id, route, parked.Position, train.Length, train.TravelDirection);
        var journey = parked.Journey;
        double stop = journey?.Distance ?? double.PositiveInfinity;
        if (journey != null && train.Orders.Current is { Kind: TrainOrderKind.Station } order)
        {
            if (_stationTargetRevision != _revision) { _stationTargets.Clear(); _stationTargetRevision = _revision; }
            if (!_stationTargets.TryGetValue(order.Target, out var targets)) _stationTargets[order.Target] = targets = OrderTargets(order);
            var before = route.Copy().Trace(route.Sample(parked.Position, train.TravelDirection * train.Length / 2).Position,
                train.TravelDirection, Math.Min(stop, RailDetourSearch.MaxDistance)).Take(RailDetourSearch.MaxOriginalLegs)
                .Select(span => RailRouter.StateId(span.Edge.Id, span.Direction)).ToArray();
            var platform = Detours.FindStation(Network, Placement, input, targets, stop, train.Orders.RoutingPolicy,
                edge => Interlocking.DetourEdgeClear(edge, train.Id),
                alternative => Interlocking.CanDetour(train.Id, before, alternative.States)
                    && Interlocking.Limit(input with { Route = alternative.Route }, lookahead, commit: false).Distance > blocked.Distance + 1,
                _pathfinder.ConflictCount);
            if (platform != null)
            {
                var leading = route.Copy().Sample(parked.Position, train.TravelDirection * train.Length / 2).Position;
                platform.Route.MarkDetour(leading, train.TravelDirection, before, platform.States);
                parked.Route = platform.Route; journey.Distance = platform.Distance; journey.TargetPosition = platform.Target;
                journey.Dependencies.Clear(); journey.Dependencies.UnionWith(platform.States.Select(state => state / 2));
                ReplanDetour(parked, train, before, platform.States, lookahead);
                Interlocking.ReleasePending(train.Id);
                train.Orders.Status = "Taking a clear station approach";
                return true;
            }
            if (_detourBudget <= 0) return false;
            _detourBudget--;
        }
        var candidate = Detours.Find(Network, Placement, input, blocked.Distance, stop,
            train.Orders.Items.Count == 0 ? null : train.Orders.RoutingPolicy,
            edge => Interlocking.DetourEdgeClear(edge, train.Id),
            detour => Interlocking.CanDetour(train.Id, detour.Before, detour.After)
                && Interlocking.Limit(input with { Route = detour.Route }, lookahead, commit: false).Distance > blocked.Distance + 1,
            _pathfinder.ConflictCount);
        if (candidate == null) return false;
        // The old occupied occurrence and all history remain fixed. Only future indices shift.
        parked.Route = candidate.Route;
        if (journey != null)
        {
            journey.Distance += candidate.ExtraDistance;
            journey.Dependencies.UnionWith(candidate.After.Select(state => state / 2));
            if (journey.TargetPosition is { } target)
                journey.TargetPosition = target with { RouteIndex = target.RouteIndex + candidate.IndexShift };
            ReplanDetour(parked, train, candidate.Before, candidate.After, lookahead);
        }
        if (parked.Wandering is { } wandering)
        {
            wandering.Route = candidate.Route; wandering.Station = 0; wandering.Distance = double.PositiveInfinity;
            wandering.ScanTimer = 0; wandering.Servicing = false;
        }
        Interlocking.ReleasePending(train.Id);
        train.Orders.Status = "Taking a clear detour";
        return true;
    }

    /// <summary>Keep the accepted entrance, then optimize the remaining destination route without requiring a rejoin.</summary>
    private void ReplanDetour(ParkedTrain parked, TrainConsist train, int[] before, int[] after, double lookahead)
    {
        if (parked.Journey is not { Reachable: true, ViaDeadEnd: false } journey || !_pathfinder.Ready
            || train.Orders.Current is not { Kind: not TrainOrderKind.Wait } order) return;
        int prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        if (prefix == after.Length) return;
        DetourReplanAttempts++;
        var route = RouteFor(parked);
        var candidate = route.Copy();
        int direction = train.TravelDirection;
        var leading = candidate.Sample(parked.Position, direction * train.Length / 2).Position;
        // Trace on the copy materializes the accepted prefix. It must survive SetPath's
        // new plan origin at the first divergent leg, including reverse-running consists.
        var entrance = candidate.Trace(leading, direction, RailDetourSearch.MaxDistance).Skip(prefix).FirstOrDefault();
        if (entrance == null || RailRouter.StateId(entrance.Edge.Id, entrance.Direction) != after[prefix]) return;
        var planned = PlanOrder(parked, train, order, candidate, entrance.Position);
        if (planned is not { Reachable: true, ViaDeadEnd: false }) return;
        int limit = Math.Max(route.PlannedLegCount + route.CachedLegCount, candidate.PlannedLegCount + candidate.CachedLegCount) + 1;
        var oldLegs = route.Inspect(leading, direction, journey.TargetPosition, out bool oldTruncated, limit);
        var newLegs = candidate.Inspect(leading, direction, planned.TargetPosition, out bool newTruncated, limit);
        if (oldTruncated || newTruncated) return;
        var policy = train.Orders.RoutingPolicy;
        int station = order.Kind == TrainOrderKind.Station ? order.Target : 0;
        var previousCost = route.Measure(oldLegs, _pathfinder.ConflictCount, policy, station, edgeLength: _pathfinder.EdgeLength);
        var newCost = candidate.Measure(newLegs, _pathfinder.ConflictCount, policy, station, edgeLength: _pathfinder.EdgeLength);
        if (!(newCost.Cost < previousCost.Cost - 1e-6)) return;
        // Include seeded continuation beyond a pass-through waypoint in reservation validation.
        var oldStates = route.Inspect(leading, direction, null, out oldTruncated, limit)
            .Select(leg => RailRouter.StateId(leg.Edge, leg.Direction)).ToArray();
        var newStates = candidate.Inspect(leading, direction, null, out newTruncated, limit)
            .Select(leg => RailRouter.StateId(leg.Edge, leg.Direction)).ToArray();
        if (oldTruncated || newTruncated) return;
        if (!Interlocking.CanDetour(train.Id, oldStates, newStates)) return;
        var input = new SignalTrain(train.Id, route, parked.Position, train.Length, direction);
        var oldLimit = Interlocking.Limit(input, lookahead, commit: false);
        var newLimit = Interlocking.Limit(input with { Route = candidate }, lookahead, commit: false);
        if (newLimit.CanReverse || newLimit.Distance < oldLimit.Distance) return;
        // PlanOrder already cleared detour annotations: this is now the normal destination plan.
        planned.Distance += entrance.Distance;
        planned.Dependencies.UnionWith(newStates.Select(state => state / 2));
        parked.Route = candidate; parked.Journey = planned;
        DetourReplans++;
    }
}
