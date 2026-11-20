using System;
using System.Linq;

namespace TransportSandbox;

public partial class TrainEditor
{
    public RailDetourSearch Detours { get; } = new();
    private int _detourBudget;
    private bool TryTrafficDetour(ParkedTrain parked, TrainConsist train, SignalLimit blocked, double lookahead)
    {
        if (_detourBudget <= 0 || parked.DetourCooldown > 0 || blocked.CanReverse || !double.IsFinite(blocked.Distance)) return false;
        if (parked.Journey is { ViaDeadEnd: true } or { NeedsReplan: true } or { Reachable: false }) return false;
        _detourBudget--; parked.DetourCooldown = 2 + train.Id % 7 * .13;
        var route = RouteFor(parked);
        var input = new SignalTrain(train.Id, route, parked.Position, train.Length, train.TravelDirection);
        var journey = parked.Journey;
        double stop = journey?.Distance ?? double.PositiveInfinity;
        var candidate = Detours.Find(Network, Placement, input, blocked.Distance, stop,
            train.Orders.Items.Count == 0 ? null : train.Orders.RoutingPolicy,
            edge => Interlocking.DetourEdgeClear(edge, train.Id),
            detour => Interlocking.CanDetour(train.Id, detour.Before, detour.After)
                && Interlocking.Limit(input with { Route = detour.Route }, lookahead, commit: false).Distance > blocked.Distance + 1);
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
