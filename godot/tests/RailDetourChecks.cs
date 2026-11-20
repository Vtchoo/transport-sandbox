using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class RailDetourChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var graph = new TrackNetwork();
        var nodes = new[] { 0, 200, 350, 500, 650, 850, 1250 }.Select(z => graph.AddNode(new(0, 0, z))).ToArray();
        var line = Enumerable.Range(0, 6).Select(i => graph.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        var p = graph.AddNode(new(80, 0, 350)); var q = graph.AddNode(new(80, 0, 500));
        var middle = graph.AddNode(new(80, 0, 425));
        var bypass = new[] { graph.ConnectNodes(nodes[1], p)!, graph.ConnectNodes(p, middle)!, graph.ConnectNodes(middle, q)!, graph.ConnectNodes(q, nodes[4])! };
        graph.SetSignal(nodes[1].Id, 1, RailSignalKind.Path);
        graph.SetSignal(nodes[4].Id, 1, RailSignalKind.Normal); graph.SetSignal(nodes[5].Id, 1, RailSignalKind.Normal);
        using var placement = new TrainPlacement(graph); using var traffic = new RailInterlocking(graph, placement);
        var start = new TrainPosition(line[0].Id, .5);
        var route = new TrainRoute(placement, start, 1);
        route.SetPath(start, 1, line.Select(edge => RailRouter.StateId(edge.Id, 1)).ToArray());
        var mover = new SignalTrain(1, route, start, 20, 1);
        var blockedPosition = new TrainPosition(line[2].Id, .5);
        var blocker = new SignalTrain(2, new TrainRoute(placement, blockedPosition, 2), blockedPosition, 20, 1);
        traffic.Update(new[] { mover, blocker });
        var stop = traffic.Limit(mover, 150);
        check(double.IsFinite(stop.Distance), "Original itinerary waits for the train on the direct line");
        var search = new RailDetourSearch();
        RailDetour? Find(double distance = double.PositiveInfinity) => search.Find(graph, placement, mover, stop.Distance, distance, RailRoutingPolicy.Empty,
            edge => traffic.DetourEdgeClear(edge, mover.Id),
            detour => traffic.CanDetour(mover.Id, detour.Before, detour.After)
                && traffic.Limit(mover with { Route = detour.Route }, 150, commit: false).Distance > stop.Distance + 1);
        var detour = Find();
        check(detour != null && bypass.All(edge => detour.After.Any(state => state / 2 == edge.Id)),
            "Bounded local search takes the clear passing line and rejoins the original itinerary");
        if (detour != null)
        {
            check(traffic.ReservationCount == 0, "Testing a detour does not mutate reservations");
            check(detour.Route.Sample(start, -5).Point.DistanceTo(route.Sample(start, -5).Point) < .001,
                "Detours preserve occupied history behind the leading coupler");
            check(detour.Route.Sample(start, 1000).Position.Edge == line[5].Id,
                "Detours retain the original suffix after the merge");
            check(detour.IndexShift == 1, "A detour correctly shifts future occurrences when it adds a segment");
            var background = Enumerable.Range(0, 100).Select(i =>
            {
                var a = graph.AddNode(new(5000 + i * 50, 0, 0)); var b = graph.AddNode(new(5000 + i * 50, 0, 1000));
                var edge = graph.ConnectNodes(a, b)!; var position = new TrainPosition(edge.Id, .5);
                return new SignalTrain(100 + i, new TrainRoute(placement, position, (uint)i), position, 20, 1);
            }).ToArray();
            traffic.Update(new[] { mover, blocker }.Concat(background));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int found = 0;
            for (int i = 0; i < 100; i++) if (Find() != null) found++;
            check(found == 100, "Nearby detours remain available with one hundred unrelated train footprints");
            GD.Print($"Local detours: 100 searches with 102 train footprints in {timer.Elapsed.TotalMilliseconds:F2} ms");
            traffic.Update(new[] { mover, blocker });
            var reserved = mover with { Id = 3, Route = detour.Route };
            traffic.Update(new[] { blocker });
            check(double.IsPositiveInfinity(traffic.Limit(reserved, 150).Distance) && Find() == null,
                "Another train's pending reservation prevents taking an otherwise empty passing line");
            traffic.Update(new[] { mover, blocker });
            var changed = mover with { Route = detour.Route };
            traffic.Limit(changed, 150);
            traffic.Update(new[] { changed with { Position = detour.Route.Sample(start, 150).Position }, blocker });
            check(!traffic.CanDetour(mover.Id, detour.After, detour.Before),
                "An entered path reservation locks the chosen branches until the rear clears");
            traffic.Update(new[] { mover, blocker });
            check(Find(200) == null, "A detour cannot skip the next waypoint or station to rejoin beyond it");
            graph.SetSignal(nodes[1].Id, 1, RailSignalKind.Normal); traffic.Update(new[] { mover, blocker });
            check(Find() == null, "A passing line within the same occupied normal block remains forbidden");
            graph.SetSignal(nodes[1].Id, 1, RailSignalKind.Path);
            graph.SetSignal(p.Id, -1, RailSignalKind.Normal); traffic.Update(new[] { mover, blocker });
            check(Find() == null, "Detours obey one-way signals and never add a reversal");
            graph.RemoveSignal(p.Id, -1); graph.AddStation(bypass[1], 1); traffic.Update(new[] { mover, blocker });
            check(Find() == null, "Short local detours preserve the heavy penalty for unlisted stations");
            foreach (int side in bypass[1].Platforms.ToArray()) graph.RemovePlatform(bypass[1], side);
            graph.SetSignal(nodes[4].Id, -1, RailSignalKind.Path); graph.SetSignal(nodes[1].Id, -1, RailSignalKind.Normal);
            var backPosition = new TrainPosition(line[4].Id, .5);
            var backRoute = new TrainRoute(placement, backPosition, 4);
            backRoute.SetPath(backPosition, -1, line.Take(5).Reverse().Select(edge => RailRouter.StateId(edge.Id, -1)).ToArray());
            var backwards = new SignalTrain(4, backRoute, backPosition, 20, -1);
            traffic.Update(new[] { backwards, blocker });
            var backStop = traffic.Limit(backwards, 150);
            var backDetour = search.Find(graph, placement, backwards, backStop.Distance, double.PositiveInfinity, null,
                edge => traffic.DetourEdgeClear(edge, backwards.Id),
                candidate => traffic.Limit(backwards with { Route = candidate.Route }, 150, commit: false).Distance > backStop.Distance + 1);
            check(backDetour != null && backDetour.IndexShift == -1 && backDetour.Route.Sample(backPosition, -600).Position.Edge == line[0].Id,
                "Reverse-running trains splice detours and preserve the suffix using decreasing occurrence indices");
            graph.RemoveSignal(nodes[4].Id, -1); graph.RemoveSignal(nodes[1].Id, -1);
        }
        check(search.ExpandedStates <= search.Searches * RailDetourSearch.MaxStates, "Every local search obeys its state expansion budget");

        var camera = new WorldCamera { Position = new(250, 350, 500) }; parent.AddChild(camera); camera.SetProcess(false); camera.LookAt(new(0, 0, 400));
        var layer = new CanvasLayer(); parent.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
        var editor = new TrainEditor { Network = graph, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows };
        parent.AddChild(editor); editor.SetProcess(false);
        try
        {
            int Place(float z)
            {
                editor.AddVehicle("atsf-f7a"); int id = editor.Builder.Selected!.Id;
                editor.PlacementBlockOverride = () => true; editor.StartPlacement(id);
                editor.RefreshPreview(camera.UnprojectPosition(new(0, 0, z)));
                check(editor.Valid && editor.CommitPreview(), "Local detour fixture places a compatible train");
                return id;
            }
            int id = Place(60); Place(425);
            graph.AddStation(line[5], 1);
            var train = editor.Builder.Get(id); var parked = editor.Parked.Single(p => p.Id == id);
            train.Orders.Add(TrainOrderKind.Station, line[5].Station); editor.OrdersChanged(id); editor.ToggleRunning(id);
            bool usedBypass = false; long searchesAtDetour = -1;
            for (int i = 0; i < 4000; i++)
            {
                editor.AdvanceMovement(.1);
                if (editor.RoutingPending) await Task.Delay(1);
                usedBypass |= bypass.Any(edge => edge.Id == parked.Position.Edge);
                if (editor.Detours.Successes > 0 && searchesAtDetour < 0) searchesAtDetour = editor.RoutingStats.Searches;
                if (usedBypass && parked.Position.Edge == line[5].Id && Math.Abs(train.Speed) < .001) break;
            }
            check(usedBypass && editor.Detours.Successes == 1 && parked.Supported,
                "A simulated train overtakes a stopped train using the passing line exactly once");
            var front = parked.Route!.Sample(parked.Position, train.Length / 2);
            check(parked.Position.Edge == line[5].Id && Math.Abs(front.Point.Z - 1250) < .15 && Math.Abs(train.Speed) < .001,
                "After rejoining, the train still stops precisely at its ordered station edge");
            check(searchesAtDetour > 0 && editor.RoutingStats.Searches == searchesAtDetour,
                "Live detours do not invalidate or search shared destination fields again");
        }
        finally { editor.Free(); layer.Free(); camera.Free(); }
        await frame();
    }
}
