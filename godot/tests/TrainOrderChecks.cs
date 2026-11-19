using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class TrainOrderChecks
{
    public static void ModelAndRouting(Action<bool, string> check)
    {
        var orders = new TrainOrders();
        orders.Add(TrainOrderKind.Station, 7); orders.Add(TrainOrderKind.Wait, seconds: 60);
        var copied = orders.Copy();
        orders.Remove(orders.Items[0].Id);
        check(copied.Items.Count == 2 && copied.Items[0].Target == 7, "Copied orders are independent of the source list");
        orders.Append(copied.Items);
        check(orders.Items.Count == 3 && orders.Items.Select(o => o.Id).Distinct().Count() == 3, "Paste appends orders and assigns independent drag IDs");
        orders.Index = orders.Items.Count - 1; orders.Advance();
        check(orders.Index == 0, "The final order repeats from the beginning");
        int current = orders.Current!.Id;
        orders.Move(orders.Items[^1].Id, 0);
        check(orders.Current!.Id == current, "Reordering keeps the current order selected by identity");
        orders.WaitRemaining = 17;
        orders.Add(TrainOrderKind.Wait, seconds: 3);
        check(orders.WaitRemaining == 17, "Appending orders preserves the current wait countdown");
        var network = new TrackNetwork();
        var a = network.AddNode(new(0, 0, 0));
        var b = network.AddNode(new(0, 0, 100));
        var c = network.AddNode(new(0, 0, 300));
        var d = network.AddNode(new(100, 0, 250));
        var ab = network.ConnectNodes(a, b)!;
        var bc = network.ConnectNodes(b, c)!;
        var bd = network.ConnectNodes(b, d)!;
        using var placement = new TrainPlacement(network);
        var router = new RailRouter(); router.Rebuild(network, placement);
        var path = router.Plan(new(ab.Id, .2), 1, new System.Collections.Generic.Dictionary<int, double> { [RailRouter.StateId(bd.Id, 1)] = .8 });
        check(path != null && path.States.SequenceEqual(new[] { RailRouter.StateId(ab.Id, 1), RailRouter.StateId(bd.Id, 1) }), "Station routing chooses the requested branch instead of a random junction");
        var behind = router.Plan(new(ab.Id, .8), 1, new System.Collections.Generic.Dictionary<int, double> { [RailRouter.StateId(ab.Id, -1)] = .2 });
        check(behind != null && behind.States.Length >= 4 && behind.States[1] != (behind.States[0] ^ 1), "A destination behind the train requires reaching a dead end instead of reversing at a junction");
        var route = new TrainRoute(placement, new(ab.Id, .5), 1);
        route.SetPath(new(ab.Id, .5), 1, new[] { RailRouter.StateId(ab.Id, 1), RailRouter.StateId(bd.Id, 1) });
        check(route.Sample(new(ab.Id, .5), 100).Position.Edge == bd.Id, "Planned itinerary controls the physical junction choice");
        var occupied = route.Sample(new(ab.Id, .5), 70).Position;
        route.SetPath(occupied, 1, new[] { RailRouter.StateId(bd.Id, 1) });
        check(route.Sample(occupied, -70).Position.Edge == ab.Id, "Replanning beyond the leading coupler preserves the track under the train");
        var stationLine = new TrackNetwork();
        var stops = Enumerable.Range(0, 5).Select(i => stationLine.AddNode(new(0, 0, i * 100))).ToArray();
        var spans = Enumerable.Range(0, 4).Select(i => stationLine.ConnectNodes(stops[i], stops[i + 1])!).ToArray();
        spans[1].Station = spans[2].Station = 70;
        using var stationPlacement = new TrainPlacement(stationLine);
        var stationRoute = new TrainRoute(stationPlacement, new(spans[0].Id, .5), 12);
        var stationStop = stationRoute.NextStation(new(spans[0].Id, .5), 1, 500);
        check(stationStop is { Station: 70, EndRouteIndex: 2 } && Math.Abs(stationStop.Distance - 248) < 1e-6,
            "Wandering finds the far end of a multi-segment station rather than stopping at each segment");
        check(stationRoute.NextStation(new(spans[0].Id, .5), 1, 500, 70, 2) == null,
            "A serviced station visit is skipped until the train encounters another visit");
        var backwardsRoute = new TrainRoute(stationPlacement, new(spans[3].Id, .5), 12);
        check(Math.Abs(backwardsRoute.NextStation(new(spans[3].Id, .5), -1, 500)!.Distance - 248) < 1e-6,
            "Wandering station discovery respects the opposite travel direction");
    }
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        ModelAndRouting(check);
        var network = new TrackNetwork();
        var generator = new TerrainGenerator();
        var camera = new WorldCamera { Position = new(250, 350, 500) };
        parent.AddChild(camera); camera.SetProcess(false); camera.LookAt(new(0, 0, 400), Vector3.Up);
        var renderer = new TrackRenderer { Network = network, Camera = camera }; parent.AddChild(renderer); renderer.SetProcess(false);
        var layer = new CanvasLayer(); parent.AddChild(layer);
        var windows = new WindowManager(); layer.AddChild(windows);
        var track = new TrackEditor { Network = network, Renderer = renderer, Camera = camera, Generator = generator, Interface = layer, Windows = windows };
        parent.AddChild(track); track.SetProcess(false);
        var editor = new TrainEditor { Network = network, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows };
        parent.AddChild(editor); editor.SetProcess(false);
        var ui = new TrainWindows { Editor = editor, Manager = windows, TrackEditor = track, Interface = layer }; parent.AddChild(ui); ui.SetProcess(false);
        try
        {
            var a = network.AddNode(new(0, 0, 0)); var b = network.AddNode(new(0, 0, 400));
            var c = network.AddNode(new(0, 0, 800)); var d = network.AddNode(new(0, 0, 1200));
            var ab = network.ConnectNodes(a, b)!; var bc = network.ConnectNodes(b, c)!; network.ConnectNodes(c, d);
            network.AddStation(bc, 1);
            renderer._Process(0);
            ui.CatalogClick("atsf-f7a"); int id = editor.Builder.Selected!.Id;
            var train = editor.Builder.Get(id);
            ui.OpenRoutes(id);
            var view = ui.Views[id]; var panel = view.Orders;
            check(view.RoutesTab && panel.Root.Visible && !view.Edit.Visible && !view.Overview.Visible && view.RoutesButton.Icon != null, "Routes is a separate icon tab and suspends train viewport rendering");
            check(panel.FullLoad.Disabled && panel.Item.Disabled && panel.FullLoad.Icon != null, "Full-load orders and item selection are visible but disabled");
            panel.Duration.Value = 2; panel.Unit.Select(1); panel.Wait.EmitSignal(Button.SignalName.Pressed);
            check(train.Orders.Items.Single().Seconds == 120, "Wait editor converts minutes to simulation seconds");
            var waitId = train.Orders.Items.Single().Id;
            var trashData = TrainDragButton.Data("order", id, waitId);
            check(panel.Trash._CanDropData(Vector2.Zero, trashData), "Order trash accepts this train's dragged orders");
            panel.Trash._DropData(Vector2.Zero, trashData);
            check(train.Orders.Items.Count == 0, "Dropping an order on trash deletes it");
            ui.BeginOrderPicking(id, TrainOrderKind.Station); ui.StationList.ActivateStation(bc.Station);
            check(!ui.PickingOrder && train.Orders.Items.Single().Target == bc.Station, "Station list selects a destination for the active train route");
            ui.AddOrder(id, TrainOrderKind.Wait, seconds: 2);
            ui.BeginOrderPicking(id, TrainOrderKind.Waypoint);
            ui._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = camera.UnprojectPosition(b.Position) });
            check(train.Orders.Items.Last().Kind == TrainOrderKind.Waypoint && train.Orders.Items.Last().Target == b.Id, "World picking accepts existing track nodes as waypoints");
            train.Orders.Remove(train.Orders.Items.Last().Id); editor.OrdersChanged(id);
            ui.AddOrder(id, TrainOrderKind.Waypoint, a.Id);
            ui.AddOrder(id, TrainOrderKind.Wait, seconds: 1);
            ui.OrderHighlights._Process(0);
            check(ui.OrderHighlights.Visible && ui.OrderHighlights.MarkerCount == 2 && ui.OrderHighlights.StationStrokeCount == 1,
                "Routes highlight station segments and waypoint nodes, without wait markers");
            var beforeRebase = ui.OrderHighlights.MarkerCount;
            network.Space.Rebase(new(1000, 0, 1000));
            ui.OrderHighlights._Process(0);
            check(ui.OrderHighlights.MarkerCount == beforeRebase, "Order map highlights survive floating-origin changes");
            network.Space.Rebase(new(0, 0, 0));
            ui.OpenTrain(id, true); ui.OrderHighlights._Process(0);
            check(!ui.OrderHighlights.Visible && ui.OrderHighlights.MarkerCount == 0, "Leaving Routes hides all route map highlights");
            ui.OpenRoutes(id); ui.OrderHighlights._Process(0);
            view.Window.Close(); ui.OrderHighlights._Process(0);
            check(!ui.OrderHighlights.Visible, "Closing a train window clears its route highlights");
            ui.OpenRoutes(id);
            ui.BeginOrderPicking(id, TrainOrderKind.Station);
            ui._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            check(!ui.PickingOrder && train.Orders.Items.Count == 4 && !renderer.Markers.Visible, "Escape cancels target picking without adding an order and hides temporary node markers");
            ui.CopyRoute(id);
            var other = editor.Builder.Create(); editor.Builder.Add(other, editor.Catalog.Get("atsf-f7a")); ui.OpenRoutes(other.Id);
            ui.AddOrder(other.Id, TrainOrderKind.Wait, seconds: 3); ui.PasteRoute(other.Id);
            check(other.Orders.Items.Count == 5 && other.Orders.Items[0].Seconds == 3, "Paste appends a copied route after existing orders in another train");
            other.Orders.Remove(other.Orders.Items[1].Id);
            check(train.Orders.Items.Count == 4, "Editing pasted orders never changes the source train");
            editor.Select(id); editor.StartPlacement(id);
            editor.RefreshPreview(camera.UnprojectPosition(new(0, 0, 150)));
            check(editor.Valid && editor.CommitPreview(), "Scheduled train places on a compatible rail");
            if (!train.Placed) return;
            var parked = editor.Parked.Single(p => p.Id == id);
            editor.ToggleRunning(id);
            async Task<bool> DriveUntil(Func<bool> done, int steps = 1800)
            {
                for (int i = 0; i < steps && !done(); i++)
                {
                    editor.AdvanceMovement(.1);
                    if (editor.RoutingPending) await Task.Delay(1);
                }
                return done();
            }
            check(await DriveUntil(() => train.Orders.Index == 1 && train.Orders.WaitRemaining > 0), "Train navigates to its station and starts the following timed wait");
            check(Math.Abs(train.Speed) < .001 && Math.Abs(parked.Poses[0].Center.Z + train.Length / 2 - 798) < .15, "Station arrival stops the leading coupler two metres before the platform end");
            double remaining = train.Orders.WaitRemaining;
            editor.ToggleRunning(id); for (int i = 0; i < 10; i++) editor.AdvanceMovement(.1);
            check(Math.Abs(train.Orders.WaitRemaining - remaining) < 1e-8, "Stopped trains pause their wait countdown");
            editor.ToggleRunning(id);
            check(await DriveUntil(() => train.Orders.Index == 2, 60), "Timed wait advances to the next order");
            check(train.TravelDirection == 1, "A destination behind the train does not trigger a mid-track reversal");
            check(await DriveUntil(() => train.TravelDirection == -1), "Route planning reaches a dead end before reversing toward a waypoint behind it");
            check(parked.Poses[0].Center.Z > 1180, "Reversal happens at the actual end of connected track");
            check(await DriveUntil(() => train.Orders.Index == 3), "Passing the waypoint advances the route to its next wait");
            check(parked.Poses[0].Center.Z < 25, "Waypoint completion corresponds to the selected node");
            check(await DriveUntil(() => train.Orders.Index == 0, 40), "Final wait wraps to the first station order");
            foreach (var order in train.Orders.Items.ToArray()) train.Orders.Remove(order.Id);
            train.Orders.Add(TrainOrderKind.Waypoint, b.Id); train.Orders.Add(TrainOrderKind.Wait, seconds: .5); editor.OrdersChanged(id);
            check(await DriveUntil(() => train.Orders.Index == 1) && Math.Abs(train.Speed) > 1, "Interior waypoints advance while the train passes without stopping");
            foreach (var order in train.Orders.Items.ToArray()) train.Orders.Remove(order.Id);
            int routeWarnings = 0;
            void CountRouteWarning(string message) { if (message.Contains("Can't find a route")) routeWarnings++; }
            editor.Warning += CountRouteWarning;
            train.Orders.Add(TrainOrderKind.Station, 99999); editor.OrdersChanged(id);
            await DriveUntil(() => train.Orders.Status.Contains("Can't find a route") && Math.Abs(train.Speed) < .001, 80);
            check(train.Orders.Status.Contains("Can't find a route to the destination: Station 99999") && Math.Abs(train.Speed) < .001 && parked.Supported,
                "Missing destinations hold the train safely with an explicit destination message");
            var heldPosition = parked.Position; int heldDirection = train.TravelDirection;
            for (int i = 0; i < 20; i++) editor.AdvanceMovement(.1);
            check(parked.Position == heldPosition && train.TravelDirection == heldDirection && train.Orders.Index == 0
                && train.Running && routeWarnings == 1,
                "An unreachable order stays stopped without skipping, reversing, wandering or repeating warnings");
            check(view.Orders.Status.Text == train.Orders.Status && view.Location.Text.Contains(train.Orders.Status),
                "Route failure is visible in both the Routes and train viewport tabs");
            void ResetTrain()
            {
                train.Speed = 0; train.TravelDirection = 1; train.Running = true;
                parked.Position = new(ab.Id, .375); parked.Route = new(editor.Placement, parked.Position, 13);
                parked.Journey = null; parked.Wandering = null;
                parked.Poses = editor.Placement.Poses(train, parked.Position, parked.Route.Sample);
                for (int i = 0; i < parked.Poses.Length; i++) parked.Visuals[i].Apply(parked.Poses[i]);
            }
            var isolatedA = network.AddNode(new(1000, 0, 400)); var isolatedB = network.AddNode(new(1000, 0, 800));
            var isolated = network.ConnectNodes(isolatedA, isolatedB)!; network.AddStation(isolated, 1);
            foreach (var order in train.Orders.Items.ToArray()) train.Orders.Remove(order.Id);
            train.Orders.Add(TrainOrderKind.Waypoint, b.Id); train.Orders.Add(TrainOrderKind.Station, isolated.Station);
            editor.OrdersChanged(id); ResetTrain();
            check(await DriveUntil(() => train.Orders.Status.Contains($"destination: Station {isolated.Station}") && Math.Abs(train.Speed) < .001, 100)
                && train.Orders.Index == 0 && parked.Position.Edge == ab.Id && parked.Supported,
                "An unreachable destination after a waypoint holds before taking a random exit and reports the correct next order");
            check(routeWarnings == 2, "A disconnected existing station generates one warning for the new failed journey");
            foreach (var order in train.Orders.Items.ToArray()) train.Orders.Remove(order.Id);
            train.Orders.Add(TrainOrderKind.Waypoint, isolatedA.Id); editor.OrdersChanged(id);
            check(await DriveUntil(() => train.Orders.Status.Contains($"destination: Waypoint {isolatedA.Id}") && Math.Abs(train.Speed) < .001, 100),
                "An unreachable waypoint reports its destination and keeps the train stopped");
            var bridge = network.ConnectNodes(d, isolatedB, 1);
            check(bridge != null, "Recovery fixture connects through the free sides of existing nodes");
            check(await DriveUntil(() => Math.Abs(train.Speed) > 1 && !train.Orders.Status.Contains("Can't find"), 100),
                "A repaired connection invalidates the failed journey and resumes the retained order");
            if (bridge != null) { network.Edges.Remove(bridge.Id); network.NotifyChanged(); }
            editor.Warning -= CountRouteWarning;
            foreach (var order in train.Orders.Items.ToArray()) train.Orders.Remove(order.Id);
            train.Orders.Add(TrainOrderKind.Waypoint, d.Id); editor.OrdersChanged(id); ResetTrain();
            double passingSpeed = double.PositiveInfinity;
            check(await DriveUntil(() =>
            {
                double front = parked.Poses[0].Center.Z + train.Length / 2;
                if (front > 500 && front < 790) passingSpeed = Math.Min(passingSpeed, Math.Abs(train.Speed));
                return front > 820;
            }), "A scheduled train passes through an unavoidable station absent from its orders");
            check(double.IsFinite(passingSpeed) && passingSpeed > 1 && parked.Wandering == null,
                "An unordered station causes no automatic station stop for scheduled trains");
            foreach (var order in train.Orders.Items.ToArray()) train.Orders.Remove(order.Id);
            editor.OrdersChanged(id); ResetTrain();
            bool busy = true; int serviceTicks = 0;
            editor.ServiceStation = (_, _, _) => { serviceTicks++; return busy; };
            check(await DriveUntil(() => parked.Wandering?.Servicing == true), "An empty-order train wanders and brakes for the next station");
            check(train.Orders.Items.Count == 0 && train.Running && Math.Abs(train.Speed) < .001
                && Math.Abs(parked.Poses[0].Center.Z + train.Length / 2 - 798) < .15,
                "Wandering arrival stops at the platform end without adding synthetic route orders");
            for (int i = 0; i < 20; i++) editor.AdvanceMovement(.1);
            check(parked.Wandering!.Servicing && Math.Abs(train.Speed) < .001 && serviceTicks > 0,
                "Wandering remains stopped while station loading or unloading work is pending");
            int beforePause = serviceTicks;
            editor.ToggleRunning(id); for (int i = 0; i < 10; i++) editor.AdvanceMovement(.1);
            check(serviceTicks == beforePause && parked.Wandering.Servicing, "Manually stopping a wandering train pauses station work");
            editor.ToggleRunning(id);
            busy = false;
            check(await DriveUntil(() => parked.Poses[0].Center.Z + train.Length / 2 > 820)
                && Math.Abs(train.Speed) > 1 && train.Orders.Items.Count == 0,
                "Wandering departs when station work becomes idle without a fixed timer or repeated station stop");
            busy = true;
            check(await DriveUntil(() => train.TravelDirection == -1 && parked.Wandering?.Servicing == true),
                "After a dead-end reversal, wandering stops at the station again on the return visit");
            check(Math.Abs(parked.Poses[0].Center.Z - train.Length / 2 - 402) < .15,
                "Return visits stop two metres before the platform end in the actual travel direction");
            ui.AddOrder(id, TrainOrderKind.Waypoint, a.Id);
            check(await DriveUntil(() => parked.Poses[0].Center.Z < 350) && parked.Wandering == null,
                "Adding a route during a wandering dwell cancels the automatic station pause and follows orders");
            foreach (var order in train.Orders.Items.ToArray()) train.Orders.Remove(order.Id);
            editor.OrdersChanged(id); network.AddStation(ab, 1);
            check(await DriveUntil(() => parked.Wandering?.Servicing == true) && train.TravelDirection == -1,
                "A platform ending at a dead end receives its station stop before the train reverses");
            check(Math.Abs(parked.Poses[0].Center.Z - train.Length / 2 - 2) < .15,
                "Dead-end station arrivals use the same two-metre stopping clearance");
            editor.ServiceStation = null;
            check(await DriveUntil(() => train.TravelDirection == 1, 20), "With no cargo work, the wandering train departs and reverses without an artificial delay");
            await frame();
        }
        finally
        {
            ui.Free(); editor.Free(); track.Free(); renderer.Free(); layer.Free(); camera.Free(); generator.Backend.Dispose();
        }
        await frame();
    }
}
