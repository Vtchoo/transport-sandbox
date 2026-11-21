using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace TransportSandbox;

public partial class TrainEditor
{
    private readonly RailPathfinder _pathfinder = new();
    private TrackEdge[]? _routingSource;
    private RailRoutingEdge[]? _routingInput;
    private int _routingCaptureRevision = -1, _routingCursor;
    public RailRoutingStats RoutingStats => _pathfinder.Stats;
    internal bool RoutingCostsReady => _pathfinder.Ready;
    internal int RoutingConflictCount(int edge) => _pathfinder.ConflictCount(edge);
    public bool RoutingPending => !_pathfinder.Ready || RoutingStats.ActiveWorkers > 0 || RoutingStats.Queued > 0;
    private void PrepareRouting()
    {
        if (Parked.Count == 0) return;
        if (_routingCaptureRevision != _revision)
        {
            _routingCaptureRevision = _revision; _routingCursor = 0;
            _pathfinder.Invalidate(_revision);
            _routingSource = Network.Edges.Values.ToArray();
            _routingInput = new RailRoutingEdge[_routingSource.Length];
        }
        if (_routingSource != null && _routingInput != null)
        {
            // Bound main-thread copying. Length integration, adjacency and searches run on workers.
            var budget = Stopwatch.StartNew();
            do
            {
                if (_routingCursor >= _routingSource.Length) break;
                var edge = _routingSource[_routingCursor]; var p = Network.WorldControls(edge);
                _routingInput[_routingCursor++] = new(edge.Id, edge.A, edge.B, edge.Gauge, edge.SideA, edge.SideB, edge.Station,
                    p[0], p[1], p[2], p[3], Placement.CachedLength(edge.Id), Network.SignalMask(edge.A), Network.SignalMask(edge.B));
            } while (budget.Elapsed.TotalMilliseconds < 2);
            if (_routingCursor == _routingSource.Length)
            {
                _pathfinder.SetSnapshot(_revision, _routingInput); _routingSource = null; _routingInput = null;
            }
        }
        _pathfinder.Pump();
    }
    public void OrdersChanged(int id)
    {
        var parked = Parked.FirstOrDefault(p => p.Id == id);
        if (parked != null) { parked.Journey = null; parked.Wandering = null; }
        Changed?.Invoke(null);
    }
    public Dictionary<int, double> OrderTargets(TrainOrder order)
    {
        var targets = new Dictionary<int, double>();
        if (order.Kind == TrainOrderKind.Waypoint)
        {
            foreach (var edge in Network.Incident(order.Target))
            {
                int direction = edge.B == order.Target ? 1 : -1;
                double length = Placement.Curve(edge).Length;
                if (length <= 0) continue;
                double buffer = Network.Continuations(edge, direction).Count == 0 ? Math.Min(2, length) : 0;
                targets[RailRouter.StateId(edge.Id, direction)] = direction > 0 ? 1 - buffer / length : buffer / length;
            }
        }
        else if (order.Kind == TrainOrderKind.Station)
        {
            foreach (var end in Network.Edges.Values.Where(edge => edge.Station == order.Target))
                foreach (int outgoing in new[] { -1, 1 })
                {
                    if (Network.Continuations(end, outgoing).Any(edge => edge.Station == order.Target)) continue;
                    var edge = end;
                    int direction = outgoing;
                    double remaining = 0;
                    var visited = new HashSet<int>();
                    while (visited.Add(edge.Id))
                    {
                        double length = Placement.Curve(edge).Length;
                        if (length > remaining)
                        {
                            targets[RailRouter.StateId(edge.Id, direction)] = direction > 0 ? 1 - remaining / length : remaining / length;
                            break;
                        }
                        remaining -= length;
                        var previous = Network.Continuations(edge, -direction).Where(e => e.Station == order.Target).OrderBy(e => e.Id).FirstOrDefault();
                        if (previous == null) break;
                        int node = direction > 0 ? edge.A : edge.B;
                        edge = previous;
                        direction = edge.B == node ? 1 : -1;
                    }
                }
        }
        return targets;
    }
    private static int[] UntilReversal(int[] states)
    {
        int count = 1;
        while (count < states.Length && states[count] != (states[count - 1] ^ 1)) count++;
        return states.Take(count).ToArray();
    }
    private TrainOrderJourney? PlanOrder(ParkedTrain parked, TrainConsist train, TrainOrder order, TrainRoute route, TrainPosition? planningStart = null)
    {
        var result = new TrainOrderJourney { Order = order.Id, OrdersRevision = train.Orders.Revision, NetworkRevision = _revision, Direction = train.TravelDirection };
        var policy = train.Orders.RoutingPolicy;
        var leading = planningStart ?? route.Sample(parked.Position, train.TravelDirection * train.Length / 2).Position;
        if (!_pathfinder.TryGet(new(order.Kind, order.Target), out var field, policy: policy, startEdge: leading.Edge)) return null;
        result.GraphVersion = field!.GraphVersion;
        var path = field!.Plan(leading, train.TravelDirection);
        if (path == null) return result;
        result.Dependencies.UnionWith(path.States.Select(state => state / 2));
        var states = UntilReversal(path.States);
        result.Reachable = true;
        result.ViaDeadEnd = states.Length != path.States.Length;
        result.Distance = result.ViaDeadEnd ? double.PositiveInfinity : path.Distance;
        if (!result.ViaDeadEnd)
            result.TargetPosition = route.PathTarget(leading, train.TravelDirection, path.States, field.TargetU(path.States[^1]));
        if (!result.ViaDeadEnd && order.Kind == TrainOrderKind.Waypoint && train.Orders.Items.Count > 1)
        {
            // Seed the exit beyond a pass-through waypoint from the next travel order,
            // before braking/pose lookahead has a chance to choose a random junction.
            var next = train.Orders.Items[(train.Orders.Index + 1) % train.Orders.Items.Count];
            if (next.Kind != TrainOrderKind.Wait)
            {
                int last = states[^1], direction = RailRouter.Direction(last);
                if (!_pathfinder.TryGet(new(next.Kind, next.Target), out var nextField, urgent: true, policy: policy, startEdge: last / 2)) return null;
                double u = field.TargetU(last);
                var after = nextField!.Plan(new(last / 2, u, direction * train.TravelDirection), train.TravelDirection);
                if (after == null)
                {
                    // Do not run through a waypoint onto a random branch when its next destination is unreachable.
                    result.Reachable = false; result.BlockedOrder = next;
                    return result;
                }
                states = states.Concat(UntilReversal(after.States).Skip(1)).ToArray();
                result.Dependencies.UnionWith(after.States.Select(state => state / 2));
            }
        }
        route.SetPath(leading, train.TravelDirection, states);
        return result;
    }
    private (bool Hold, double StopDistance) ExecuteOrders(ParkedTrain parked, TrainConsist train, TrainRoute route, double dt, double lookahead)
    {
        var orders = train.Orders;
        if (orders.Current is not { } order) return ExecuteWandering(parked, train, route, dt, lookahead);
        parked.Wandering = null;
        if (!train.Running) return (true, double.PositiveInfinity);
        if (order.Kind == TrainOrderKind.Wait)
        {
            parked.Journey = null;
            if (orders.WaitRemaining < 0) orders.WaitRemaining = order.Seconds;
            if (Math.Abs(train.Speed) <= .001)
            {
                orders.WaitRemaining = Math.Max(0, orders.WaitRemaining - dt);
                if (orders.WaitRemaining <= 1e-8) orders.Advance();
            }
            orders.Status = FormattableString.Invariant($"Waiting - {Math.Max(0, orders.WaitRemaining):F1} s remaining");
            return (true, double.PositiveInfinity);
        }
        bool plannedNow = false;
        var journey = parked.Journey;
        if (journey != null && (journey.Order != order.Id || journey.OrdersRevision != orders.Revision || journey.Direction != train.TravelDirection))
            parked.Journey = journey = null;
        if (journey != null && _pathfinder.Ready && journey.NetworkRevision != _revision)
        {
            if (journey.GraphVersion != _pathfinder.ComponentVersion(parked.Position.Edge))
            {
                journey.NeedsReplan = true;
                journey.HoldForReplan |= !journey.Reachable || _pathfinder.AffectsSince(journey.NetworkRevision, journey.Dependencies);
            }
            journey.NetworkRevision = _revision;
        }
        if (journey == null || journey.NeedsReplan)
        {
            var planned = PlanOrder(parked, train, order, route);
            if (planned != null)
            {
                plannedNow = planned.Reachable || journey == null || journey.Reachable || journey.BlockedOrder != planned.BlockedOrder;
                parked.Journey = journey = planned;
            }
            else if (journey == null || journey.HoldForReplan)
            { orders.Status = "Finding route..."; return (true, double.PositiveInfinity); }
        }
        var target = journey!.BlockedOrder ?? order;
        string destination = target.Kind == TrainOrderKind.Station ? $"Station {target.Target}" : $"Waypoint {target.Target}";
        if (!journey.Reachable)
        {
            orders.Status = "Can't find a route to the destination: " + destination;
            if (plannedNow)
            {
                Warning?.Invoke(orders.Status);
                MotionChanged?.Invoke();
            }
            return (true, double.PositiveInfinity);
        }
        orders.Status = "Go to " + destination + (journey.ViaDeadEnd ? " - via dead-end reversal" : "");
        if (journey.Distance <= .05 && (order.Kind == TrainOrderKind.Waypoint || Math.Abs(train.Speed) < .1))
        {
            var leading = route.Sample(parked.Position, train.TravelDirection * train.Length / 2).Position;
            bool arrived = false;
            if (journey.TargetPosition is { } targetPosition)
            {
                int progress = (leading.RouteIndex - targetPosition.RouteIndex) * route.TravelStep(leading, train.TravelDirection);
                if (order.Kind == TrainOrderKind.Waypoint && progress > 0) arrived = true;
                else if (progress == 0 && leading.Edge == targetPosition.Edge)
                {
                    double remaining = (targetPosition.U - leading.U) * targetPosition.Facing * train.TravelDirection * Placement.Curve(Network.Edges[leading.Edge]).Length;
                    arrived = order.Kind == TrainOrderKind.Waypoint ? remaining <= .05 : Math.Abs(remaining) <= .1;
                }
            }
            if (!arrived)
            {
                parked.Journey = null; orders.Status = "Finding route...";
                return (true, double.PositiveInfinity);
            }
            if (order.Kind == TrainOrderKind.Station) train.Speed = 0;
            orders.Advance(); parked.Journey = null;
            return (order.Kind == TrainOrderKind.Station, double.PositiveInfinity);
        }
        return (false, order.Kind == TrainOrderKind.Station && !journey.ViaDeadEnd ? Math.Max(0, journey.Distance) : double.PositiveInfinity);
    }
}
