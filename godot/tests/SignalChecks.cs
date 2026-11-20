using System;
using System.Linq;

namespace TransportSandbox.Tests;

public static class SignalChecks
{
    private static (TrackNode[] Nodes, TrackEdge[] Edges) Line(TrackNetwork network, float x, int count = 7)
    {
        var nodes = Enumerable.Range(0, count).Select(i => network.AddNode(new(x, 0, i * 100))).ToArray();
        var edges = Enumerable.Range(0, count - 1).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        return (nodes, edges);
    }
    private static SignalTrain Train(TrainPlacement placement, int id, TrackEdge[] edges, int edge = 0, double u = .5, double length = 20)
    {
        var position = new TrainPosition(edges[edge].Id, u, RouteIndex: edge);
        var route = new TrainRoute(placement, position, (uint)id);
        route.SetPath(position, 1, edges.Skip(edge).Select(e => RailRouter.StateId(e.Id, 1)).ToArray());
        return new(id, route, position, length, 1);
    }
    public static void Run(Action<bool, string> check)
    {
        var network = new TrackNetwork(); var line = Line(network, 0);
        check(Math.Abs(TrackNetwork.DefaultParallelSpacing(1435) - 5) < 1e-6 && TrackNetwork.DefaultParallelSpacing(914) < 5,
            "New parallel spacing is five metres at standard gauge and adjusts with gauge");
        var parallel = network.AddParallel(line.Nodes[0], 1);
        check(TrackNetwork.Gauges.All(gauge => TrackNetwork.DefaultParallelSpacing(gauge) - TrackNetwork.SignalOffset(gauge) * 2 >= .9f),
            "Gauge-adjusted spacing leaves room for opposing signal heads between parallel tracks");
        check(Math.Abs(parallel.Position.X - 5) < 1e-6, "New parallel nodes use the widened default");
        var original = parallel.Position; network.SetSignal(line.Nodes[1].Id, 1, RailSignalKind.Normal);
        check(parallel.Position == original, "Adding a signal does not reposition existing parallel tracks");
        check(!network.SetSignal(99999, 1, RailSignalKind.Normal), "Signals require an existing connected node");
        using var placement = new TrainPlacement(network);
        using var control = new RailInterlocking(network, placement);
        var train = Train(placement, 1, line.Edges);
        var blocker = Train(placement, 2, line.Edges, 1);
        network.SetSignal(line.Nodes[3].Id, 1, RailSignalKind.Normal);
        network.SetSignal(line.Nodes[5].Id, 1, RailSignalKind.Normal);
        check(control.PlacementBlockOccupied(blocker, new[] { Train(placement, 3, line.Edges, 2) }),
            "Placement rejects another train on a different segment of the same physical block");
        check(!control.PlacementBlockOccupied(train, new[] { Train(placement, 2, line.Edges, 3) }),
            "Placement allows another signal-separated block");
        check(control.PlacementBlockOccupied(Train(placement, 3, line.Edges, 2, .95, 30), new[] { Train(placement, 2, line.Edges, 3) }),
            "Placement checks every block touched by a train straddling a signal boundary");
        control.Update(new[] { train, blocker });
        var stopped = control.Limit(train, 80);
        check(Math.Abs(stopped.Distance - 38) < .001 && control.ReservationCount == 0,
            "A normal signal brakes two metres before its node when its block is occupied");
        blocker = Train(placement, 2, line.Edges, 3);
        control.Update(new[] { train, blocker });
        check(double.IsPositiveInfinity(control.Limit(train, 80).Distance) && control.ReservationCount == 1,
            "A normal signal admits a train into a clear block even when the following block is occupied");
        var follower = Train(placement, 3, line.Edges, u: .2);
        control.Update(new[] { train, follower, blocker });
        check(double.IsFinite(control.Limit(follower, 80).Distance), "An approaching reservation excludes a second train before the first enters");
        var within = train with { Position = train.Route.Sample(train.Position, 100).Position };
        control.Update(new[] { within, blocker });
        check(control.ReservationCount == 1, "A reservation remains while the train occupies its block");
        var straddling = train with { Position = train.Route.Sample(train.Position, 255).Position };
        control.Update(new[] { straddling });
        check(control.ReservationCount == 1, "The block stays reserved until the rear clears its exit");
        var cleared = train with { Position = train.Route.Sample(train.Position, 280).Position };
        control.Update(new[] { cleared });
        check(control.ReservationCount == 0, "Reservations release after the entire train clears");
        network.SetSignal(line.Nodes[1].Id, 1, RailSignalKind.Chain);
        control.Update(new[] { train, blocker });
        check(double.IsFinite(control.Limit(train, 80).Distance) && control.ReservationCount == 0,
            "A chain signal refuses an occupied exit block and acquires no partial reservation");
        control.Update(new[] { train });
        check(double.IsPositiveInfinity(control.Limit(train, 80).Distance) && control.ReservationCount == 2,
            "A chain signal reserves through to a normal exit and its receiving block atomically");
        network.SetSignal(line.Nodes[3].Id, 1, RailSignalKind.Chain);
        network.RemoveSignal(line.Nodes[5].Id, 1);
        control.Update(new[] { train });
        check(double.IsFinite(control.Limit(train, 80).Distance), "A chain sequence without a normal exit fails closed");
        network.SetSignal(line.Nodes[1].Id, 1, RailSignalKind.Normal);
        network.RemoveSignal(line.Nodes[3].Id, 1);
        var graph = RailRoutingGraph.Build(RailPathfindingChecks.Capture(network));
        check(graph.Search(new(TrainOrderKind.Waypoint, line.Nodes[5].Id)).Plan(new(line.Edges[0].Id, .5), 1) != null
            && graph.Search(new(TrainOrderKind.Waypoint, line.Nodes[0].Id)).Plan(new(line.Edges[3].Id, .5), -1) == null,
            "Reversing at a wrong-way signal never permits crossing it in the prohibited direction");
        var retreat = graph.Search(new(TrainOrderKind.Waypoint, line.Nodes[5].Id)).Plan(new(line.Edges[2].Id, .5), -1);
        check(retreat != null && retreat.States.Zip(retreat.States.Skip(1)).Any(pair => pair.Second == (pair.First ^ 1)),
            "Destination routing can retreat from a wrong-way signal when no forward route reaches the destination");
        network.SetSignal(line.Nodes[1].Id, -1, RailSignalKind.Normal);
        graph = RailRoutingGraph.Build(RailPathfindingChecks.Capture(network));
        check(graph.Search(new(TrainOrderKind.Waypoint, line.Nodes[0].Id)).Plan(new(line.Edges[3].Id, .5), -1) != null,
            "A signal on each side permits both travel directions");
        network.RemoveSignal(line.Nodes[1].Id, -1);
        var reverse = Train(placement, 4, line.Edges, 2) with { Direction = -1 };
        control.Update(new[] { reverse });
        check(control.Limit(reverse, 200).CanReverse, "A wrong-way signal requests reversal after braking to a stop");
        ParallelPaths(check);
        ChainJunction(check);
        Crossing(check);
        network.DeleteNode(line.Nodes[1].Id);
        check(!network.Signals.Keys.Any(key => key.Node == line.Nodes[1].Id), "Deleting a node removes both of its signals");
    }
    private static void ChainJunction(Action<bool, string> check)
    {
        var network = new TrackNetwork(); var line = Line(network, 0, 9);
        var fork = network.AddNode(new(80, 0, 400));
        var exit = network.AddNode(new(80, 0, 500));
        var tail = network.AddNode(new(80, 0, 600));
        var end = network.AddNode(new(80, 0, 700));
        var branch = new[] { network.ConnectNodes(line.Nodes[3], fork)!, network.ConnectNodes(fork, exit)!,
            network.ConnectNodes(exit, tail)!, network.ConnectNodes(tail, end)! };
        var branchRoute = line.Edges.Take(3).Concat(branch).ToArray();
        network.SetSignal(line.Nodes[1].Id, 1, RailSignalKind.Chain);
        network.SetSignal(line.Nodes[5].Id, 1, RailSignalKind.Normal);
        network.SetSignal(exit.Id, 1, RailSignalKind.Normal);
        using var placement = new TrainPlacement(network); using var control = new RailInterlocking(network, placement);
        var straight = Train(placement, 1, line.Edges);
        var turning = Train(placement, 1, branchRoute);
        var blocker = Train(placement, 2, branchRoute, 5);
        var entry = network.Signals[new(line.Nodes[1].Id, 1)];
        control.Update(new[] { straight, blocker });
        check(entry.Aspect == RailSignalAspect.Blue,
            "A chain before an unsignalled junction shows mixed exits beyond intermediate unsignalled nodes");
        check(double.IsPositiveInfinity(control.Limit(straight, 80, commit: false).Distance)
            && double.IsFinite(control.Limit(turning, 80, commit: false).Distance) && control.ReservationCount == 0,
            "Chain permission follows the chosen junction exit, skipping plain nodes and checking its receiving block");
        check(double.IsPositiveInfinity(control.Limit(straight, 80).Distance) && control.ReservationCount == 2,
            "The clear junction route reserves both blocks across several plain nodes");
        control.Update(new[] { turning, blocker });
        check(double.IsFinite(control.Limit(turning, 80).Distance) && control.ReservationCount == 0,
            "Changing to the blocked junction exit drops pending leases and acquires no partial chain reservation");
        control.Update(new[] { straight });
        // Force an aspect refresh after changing occupancy without a topology edit.
        for (int i = 0; i < 12; i++) control.Update(new[] { straight });
        check(entry.Aspect == RailSignalAspect.Green, "A chain turns green when both junction exits are clear");
        network.SetSignal(line.Nodes[5].Id, 1, RailSignalKind.Chain);
        network.SetSignal(line.Nodes[7].Id, 1, RailSignalKind.Normal);
        blocker = Train(placement, 2, line.Edges, 7);
        control.Update(new[] { straight, blocker });
        check(entry.Aspect == RailSignalAspect.Blue && double.IsFinite(control.Limit(straight, 80, commit: false).Distance),
            "Chains propagate a blocked normal exit through another chain and intervening plain nodes");
        network.RemoveSignal(exit.Id, 1); network.SetSignal(exit.Id, -1, RailSignalKind.Normal);
        control.Update(new[] { straight });
        check(entry.Aspect == RailSignalAspect.Blue && double.IsFinite(control.Limit(turning, 80, commit: false).Distance),
            "An opposite-facing signal blocks only its reachable exit and is not treated as a clear exit");
    }
    private static void ParallelPaths(Action<bool, string> check)
    {
        var network = new TrackNetwork(); var a = Line(network, 0, 5); var b = Line(network, 10, 5);
        network.ConnectNodes(a.Nodes[2], b.Nodes[2]); // Both routes belong to the same physical junction block.
        foreach (var line in new[] { a, b })
        { network.SetSignal(line.Nodes[1].Id, 1, RailSignalKind.Path); network.SetSignal(line.Nodes[3].Id, 1, RailSignalKind.Normal); }
        using var placement = new TrainPlacement(network); using var control = new RailInterlocking(network, placement);
        var first = Train(placement, 1, a.Edges); var second = Train(placement, 2, b.Edges);
        control.Update(new[] { first, second });
        check(double.IsPositiveInfinity(control.Limit(first, 80).Distance) && double.IsPositiveInfinity(control.Limit(second, 80).Distance),
            "Path signals allow two disjoint paths through the same physical block simultaneously");
        network.SetSignal(a.Nodes[1].Id, 1, RailSignalKind.Normal);
        network.SetSignal(b.Nodes[1].Id, 1, RailSignalKind.Normal);
        control.Update(new[] { first, second });
        control.Limit(first, 80);
        check(double.IsFinite(control.Limit(second, 80).Distance), "Normal signals exclude other paths throughout that same block");
        network.SetSignal(a.Nodes[1].Id, 1, RailSignalKind.Path);
        network.SetSignal(b.Nodes[1].Id, 1, RailSignalKind.Path);
        var longTrain = first with { Position = new(a.Edges[0].Id, .5), Length = 100 };
        // The 100 m receiving block needs four additional metres of clearance.
        control.Update(Array.Empty<SignalTrain>());
        check(double.IsFinite(control.Limit(longTrain, 80).Distance) && control.ReservationCount == 0,
            "Path signals require an exit block long enough to clear the whole train");
        bool denied;
        try { denied = double.IsFinite(control.Limit(longTrain, 80).Distance); }
        catch (InvalidOperationException) { denied = true; }
        check(denied, "A path signal requires a receiving block long enough for the whole train");
    }
    private static void Crossing(Action<bool, string> check)
    {
        var network = new TrackNetwork(); var a = Line(network, 0, 5);
        var nodes = Enumerable.Range(0, 5).Select(i => network.AddNode(new(-200 + i * 100, 0, 200))).ToArray();
        var edges = Enumerable.Range(0, 4).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        foreach (var pair in new[] { (a.Nodes, a.Edges), (nodes, edges) })
        { network.SetSignal(pair.Item1[1].Id, 1, RailSignalKind.Path); network.SetSignal(pair.Item1[3].Id, 1, RailSignalKind.Normal); }
        using var placement = new TrainPlacement(network); using var control = new RailInterlocking(network, placement);
        var first = Train(placement, 1, a.Edges); var second = Train(placement, 2, edges);
        control.Update(new[] { first, second });
        check(double.IsPositiveInfinity(control.Limit(first, 80).Distance) && double.IsFinite(control.Limit(second, 80).Distance),
            "Path reservations conflict at geometric crossings even without a shared graph node");
        foreach (var node in nodes) node.Position += Godot.Vector3.Up * 8;
        network.NotifyChanged();
        control.Update(new[] { first, second });
        check(double.IsPositiveInfinity(control.Limit(first, 80).Distance) && double.IsPositiveInfinity(control.Limit(second, 80).Distance),
            "Grade-separated crossing routes can reserve independently");
    }
}
