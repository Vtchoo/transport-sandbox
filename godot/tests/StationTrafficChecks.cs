using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class StationTrafficChecks
{
    public static void Run(Action<bool, string> check)
    {
        var network = new TrackNetwork();
        var a = network.AddNode(new(0, 0, 0)); var entry = network.AddNode(new(0, 0, 200));
        var left = network.AddNode(new(0, 0, 300)); var right = network.AddNode(new(10, 0, 300));
        var leftEnd = network.AddNode(new(0, 0, 600)); var rightEnd = network.AddNode(new(10, 0, 600));
        var approach = network.ConnectNodes(a, entry)!;
        var throatA = network.ConnectNodes(entry, left)!; var throatB = network.ConnectNodes(entry, right)!;
        // A branching endpoint with its normal entrance signal separates platform and throat.
        var other = network.AddNode(new(-10, 0, 200)); network.ConnectNodes(other, left);
        var platformA = network.ConnectNodes(left, leftEnd)!; var platformB = network.ConnectNodes(right, rightEnd)!;
        platformA.Station = platformB.Station = 1;
        network.SetSignal(entry.Id, 1, RailSignalKind.Chain);
        network.SetSignal(left.Id, 1, RailSignalKind.Normal); network.SetSignal(right.Id, 1, RailSignalKind.Normal);
        using var placement = new TrainPlacement(network); using var traffic = new RailInterlocking(network, placement);
        SignalTrain Make(int id, TrackEdge edge, double u, TrackEdge[] path)
        {
            var p = new TrainPosition(edge.Id, u); var route = new TrainRoute(placement, p, (uint)id);
            route.SetPath(p, 1, path.Select(e => RailRouter.StateId(e.Id, 1)).ToArray());
            return new(id, route, p, 20, 1);
        }
        var train = Make(1, approach, .5, new[] { approach, throatA, platformA });
        var occupied = Make(2, platformA, .5, new[] { platformA });
        traffic.Update(new[] { train, occupied });
        var blocked = traffic.Limit(train, 150);
        check(double.IsFinite(blocked.Distance), "Occupied destination platform blocks the original station approach");
        var targets = new[] { platformA, platformB }.ToDictionary(e => RailRouter.StateId(e.Id, 1), e => 1.0);
        var search = new RailDetourSearch();
        RailStationAlternative? Find() => search.FindStation(network, placement, train, targets, 500,
            RailRoutingPolicy.Create(new[] { platformA.Station }), edge => traffic.DetourEdgeClear(edge, train.Id),
            alt => traffic.CanDetour(train.Id, new[] { approach, throatA, platformA }.Select(e => RailRouter.StateId(e.Id, 1)).ToArray(), alt.States)
                && double.IsPositiveInfinity(traffic.Limit(train with { Route = alt.Route }, 150, false).Distance));
        var alternative = Find();
        check(alternative != null && alternative.Target.Edge == platformB.Id && alternative.Target.U == 1,
            "Station search reaches the free platform end without rejoining the occupied platform");
        check(traffic.ReservationCount == 0, "Station candidate validation does not acquire reservations");
        if (alternative != null)
        {
            var changed = train with { Route = alternative.Route };
            check(double.IsPositiveInfinity(traffic.Limit(changed, 150).Distance),
                "A platform behind a signalled branching boundary does not occupy the station throat");
            traffic.Update(new[] { changed with { Position = alternative.Route.Sample(train.Position, 130).Position }, occupied });
            check(!traffic.CanDetour(train.Id, alternative.States, new[] { approach, throatA, platformA }.Select(e => RailRouter.StateId(e.Id, 1)).ToArray()),
                "Entering the throat locks the selected station approach until the rear clears");
        }
        traffic.Update(new[] { train, occupied, Make(3, platformB, .5, new[] { platformB }) });
        check(Find() == null, "Both occupied platforms leave the train waiting");
        traffic.Update(new[] { train, occupied with { Position = occupied.Position with { U = .01 } } });
        check(Find() == null, "A train whose rear still occupies the throat blocks both station approaches");
        traffic.Update(new[] { train, occupied });
        var reversePosition = new TrainPosition(approach.Id, .5, Facing: -1);
        var reverseRoute = new TrainRoute(placement, reversePosition, 42);
        reverseRoute.SetPath(reversePosition, -1, new[] { approach, throatA, platformA }.Select(e => RailRouter.StateId(e.Id, 1)).ToArray());
        var reverse = train with { Route = reverseRoute, Position = reversePosition, Direction = -1 };
        traffic.Update(new[] { reverse, occupied });
        var reverseAlternative = search.FindStation(network, placement, reverse, targets, 500,
            RailRoutingPolicy.Create(new[] { platformA.Station }), edge => traffic.DetourEdgeClear(edge, train.Id),
            alt => double.IsPositiveInfinity(traffic.Limit(reverse with { Route = alt.Route }, 150, false).Distance));
        check(reverseAlternative != null && reverseAlternative.Target.Edge == platformB.Id && reverseAlternative.Target.RouteIndex < reversePosition.RouteIndex,
            "Reverse-running consists select another platform using decreasing route occurrence indices");
        network.SetSignal(right.Id, -1, RailSignalKind.Normal); network.RemoveSignal(right.Id, 1);
        traffic.Update(new[] { train, occupied });
        check(Find() == null, "Station alternatives obey wrong-way signals");
        check(search.ExpandedStates <= search.Searches * RailDetourSearch.MaxStates, "Station traffic searches remain bounded");
    }

    public static async Task Saved(Action<bool, string> check, Func<Task> frame, Node parent, string directory)
    {
        var save = WorldSaveStore.Read(ProjectSettings.GlobalizePath(directory));
        var network = new TrackNetwork(); network.Restore(save);
        var camera = new WorldCamera(); parent.AddChild(camera); camera.SetProcess(false);
        var layer = new CanvasLayer(); parent.AddChild(layer);
        var windows = new WindowManager(); layer.AddChild(windows);
        var editor = new TrainEditor { Network = network, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows };
        parent.AddChild(editor); editor.SetProcess(false); editor.Restore(save);
        try
        {
            var arrived = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 1100; i++)
            {
                editor.AdvanceMovement(.1);
                if (editor.RoutingPending) await Task.Delay(1);
                if (i % 300 == 0)
                    foreach (var p in editor.Parked)
                    {
                        var t = editor.Builder.Get(p.Id);
                        GD.Print($"saved t={i*.1:F1} train={p.Id} edge={p.Position.Edge} u={p.Position.U:F3} speed={t.Speed:F2} status={t.Orders.Status} detours={editor.Detours.Successes}");
                    }
                foreach (var p in editor.Parked.Where(p => p.Id is 1 or 3))
                {
                    var t = editor.Builder.Get(p.Id);
                    if (t.Orders.Index == 3 && t.Orders.WaitRemaining > 0 && Math.Abs(t.Speed) < .001)
                    {
                        var front = p.Route!.Sample(p.Position, t.TravelDirection * t.Length / 2).Position;
                        check(front.Edge == 152 && Math.Abs(front.U - 1) < .001, "Saved train stops at the exact empty platform end");
                        arrived.Add(p.Id);
                    }
                }
                if (arrived.Count == 2) break;
            }
            check(arrived.SetEquals(new[] { 1, 3 }), "Both saved waiting trains use the empty platform in succession");
            check(!editor.Interlocking.SaveReservations().Any(l => l.Train == 4 && !l.Entered),
                "Stopped trains release unused future reservations while retaining occupied blocks");
        }
        finally { editor.Free(); layer.Free(); camera.Free(); }
        await frame();
    }
}
