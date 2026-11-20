using System;
using System.Linq;

namespace TransportSandbox;

public partial class TrainEditor
{
    public RailDetourSearch Detours { get; } = new();
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
}
