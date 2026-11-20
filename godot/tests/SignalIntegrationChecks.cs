using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class SignalIntegrationChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var network = new TrackNetwork(); var generator = new TerrainGenerator();
        var nodes = Enumerable.Range(0, 7).Select(i => network.AddNode(new(0, 0, i * 100))).ToArray();
        var edges = Enumerable.Range(0, 6).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        var camera = new WorldCamera { Position = new(65, 80, 180) }; parent.AddChild(camera); camera.SetProcess(false); camera.LookAt(new(0, 0, 150), Vector3.Up);
        var renderer = new TrackRenderer { Network = network, Camera = camera }; parent.AddChild(renderer); renderer.SetProcess(false);
        var layer = new CanvasLayer(); parent.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
        var editor = new TrackEditor { Network = network, Renderer = renderer, Camera = camera, Generator = generator, Interface = layer, Windows = windows };
        parent.AddChild(editor); editor.SetProcess(false);
        var trains = new TrainEditor { Network = network, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows };
        parent.AddChild(trains); trains.SetProcess(false);
        try
        {
            editor.SetEnabled(true); editor.SetMode("signal");
            check(editor.Toolbar.Body.GetChildren().OfType<HBoxContainer>().First().GetChildren().OfType<Button>()
                .Any(button => button.TooltipText == "Signals" && button.Text == "" && button.Icon != null),
                "The builder exposes an icon-only Signals tab using the shared Material Design icon");
            Vector2 Side(int side) => camera.UnprojectPosition(nodes[2].Position - nodes[2].Axis * side * 4);
            editor.RefreshSignalHover(Side(1));
            check(editor.SignalType.ItemCount == 3 && editor.SignalTarget == new SignalKey(nodes[2].Id, 1) && editor.SignalPreview != null,
                "Signals tab provides all three types and previews the hovered node side");
            editor._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Side(1) });
            check(network.Signals.ContainsKey(new(nodes[2].Id, 1)), "World clicking places the previewed signal on the node");
            editor.SignalType.Select(1); editor.CommitSignal(Side(-1));
            check(network.Signals[new(nodes[2].Id, -1)].Kind == RailSignalKind.Chain && network.SignalAllows(nodes[2].Id, 1)
                && network.SignalAllows(nodes[2].Id, -1), "Opposite sides coexist and make the node bidirectional");
            renderer.Signals._Process(0);
            var signalVisuals = renderer.Signals.GetChildren().OfType<SignalVisual>().ToArray();
            check(signalVisuals.All(visual => (visual.GlobalPosition - nodes[2].Position)
                .Dot(visual.DirectionArrow.GlobalBasis.Z.Cross(Vector3.Up).Normalized()) > 1.5f),
                "Each signal pole is on the driver's right relative to its allowed-direction arrow");
            foreach (var visual in signalVisuals)
            {
                var head = visual.DirectionArrow.GetChildren().OfType<MeshInstance3D>().Single(mesh => mesh.Mesh is CylinderMesh);
                var cone = (CylinderMesh)head.Mesh;
                var tip = head.GlobalTransform * new Vector3(0, cone.Height / 2, 0);
                var tail = head.GlobalTransform * new Vector3(0, -cone.Height / 2, 0);
                var travel = (tip - tail).Normalized();
                check((visual.GlobalPosition - nodes[2].Position).Dot(travel.Cross(Vector3.Up).Normalized()) > 1.5f,
                    "Signal is right of the actual rendered arrowhead, not just the arrow transform's axis");
            }
            check(signalVisuals.Length == 2 && signalVisuals.All(visual => visual.DirectionArrow.Visible
                && Math.Abs((visual.DirectionArrow.GlobalPosition - nodes[2].Position).Dot(TrackNetwork.Right(nodes[2].Axis))) < .001)
                && signalVisuals[0].DirectionArrow.GlobalBasis.Z.Dot(signalVisuals[1].DirectionArrow.GlobalBasis.Z) < -.99,
                "Placed signal arrows sit over the track centerline and show both permitted directions");
            editor.SetMode("track"); renderer.Signals._Process(0);
            check(signalVisuals.All(visual => !visual.DirectionArrow.Visible), "Switching away from Signals hides placed direction arrows");
            editor.SetMode("signal"); editor.Toolbar.ToggleCollapsed(); renderer.Signals._Process(0);
            check(signalVisuals.All(visual => !visual.DirectionArrow.Visible), "Collapsing the signal tool hides its world arrows");
            editor.Toolbar.ToggleCollapsed(); renderer.Signals._Process(0);
            check(signalVisuals.All(visual => visual.DirectionArrow.Visible), "Expanding the Signals tab restores its direction arrows");
            editor.SignalAction.Select(1); editor.CommitSignal(Side(-1));
            check(network.Signals.Count == 1 && !network.SignalAllows(nodes[2].Id, -1), "Removal affects only the hovered signal and restores one-way behavior");
            editor.SignalAction.Select(0); editor.SignalDirections.Select(1);
            editor.RefreshSignalHover(Side(1));
            check(editor.OppositeSignalPreview != null, "Both directions previews both poles before placement");
            editor.CommitSignal(Side(1));
            check(network.Signals.Count == 2 && network.Signals.Values.All(signal => signal.Kind == RailSignalKind.Chain),
                "Both directions places or replaces both directional slots in one click");
            editor.SignalAction.Select(1); editor.CommitSignal(Side(-1));
            check(network.Signals.Count == 0, "Both directions removes both slots in one click");
            editor.SignalDirections.Select(0); editor.SignalAction.Select(0); editor.CommitSignal(Side(1));
            editor.RefreshSignalHover(Side(1)); editor.Cancel();
            check(editor.SignalPreview == null && editor.SignalTarget == null, "Escape/cancel clears the signal preview without deleting a signal");
            editor.SetEnabled(false);
            renderer.Signals._Process(0);
            check(renderer.Signals.GetChildren().OfType<SignalVisual>().All(visual => !visual.DirectionArrow.Visible),
                "Closing the signal tool hides placed arrows");
            // Two signals bound the protected block; a stopped locomotive occupies it.
            network.SetSignal(nodes[4].Id, 1, RailSignalKind.Normal);
            int Place(float z)
            {
                // This traffic scenario intentionally places two trains in one protected block.
                trains.PlacementBlockOverride = () => true;
                var train = trains.Builder.Create(); trains.Builder.Add(train, trains.Catalog.Get("atsf-f7a"));
                trains.Select(train.Id); trains.StartPlacement(train.Id); trains.RefreshPreview(camera.UnprojectPosition(new(0, 0, z)));
                check(trains.Valid && trains.CommitPreview(), "Signal integration train places on compatible track");
                return train.Id;
            }
            int id = Place(50); int obstacle = Place(250);
            var consist = trains.Builder.Get(id); var parked = trains.Parked.Single(p => p.Id == id);
            consist.Orders.Add(TrainOrderKind.Waypoint, nodes[6].Id); trains.OrdersChanged(id); trains.ToggleRunning(id);
            async Task<bool> Drive(Func<bool> done, int count = 1500)
            {
                for (int i = 0; i < count && !done(); i++) { trains.AdvanceMovement(.1); if (trains.RoutingPending) await Task.Delay(1); }
                return done();
            }
            check(await Drive(() => parked.Poses[0].Center.Z > 150 && Math.Abs(consist.Speed) < .001)
                && Math.Abs(parked.Poses[0].Center.Z + consist.Length / 2 - 198) < .15 && consist.Running && parked.Supported,
                "A running scheduled train brakes and stops two metres before a red signal");
            // Remove the obstruction from the world without changing track or destination routing.
            var blocker = trains.Parked.Single(p => p.Id == obstacle);
            foreach (var visual in blocker.Visuals) visual.QueueFree();
            trains.Parked.Remove(blocker); trains.Builder.Get(obstacle).Placed = false;
            long searches = trains.RoutingStats.Searches;
            check(await Drive(() => parked.Poses[0].Center.Z > 220, 200) && Math.Abs(consist.Speed) > 1
                && trains.RoutingStats.Searches == searches,
                "Clearing the block resumes the train without a destination-route search");
            check(consist.TravelDirection == 1, "A red signal wait does not reverse the train");
            network.RemoveSignal(nodes[2].Id, 1); network.RemoveSignal(nodes[2].Id, -1);
            network.RemoveSignal(nodes[4].Id, 1); network.SetSignal(nodes[4].Id, -1, RailSignalKind.Normal);
            consist.Orders.Remove(consist.Orders.Items.Single().Id);
            consist.Orders.Add(TrainOrderKind.Waypoint, nodes[1].Id); trains.OrdersChanged(id);
            check(await Drive(() => consist.TravelDirection == -1) && Math.Abs(consist.Speed) < .001
                && Math.Abs(parked.Poses[0].Center.Z + consist.Length / 2 - 398) < .15 && parked.Supported,
                "A scheduled train stops two metres before a wrong-way signal, then reverses without crossing it");
            check(await Drive(() => parked.Poses[0].Center.Z < 330, 200) && consist.Speed < -1,
                "After wrong-way reversal the train continues toward its order behind it");
            consist.Orders.Remove(consist.Orders.Items.Single().Id); trains.OrdersChanged(id);
            consist.TravelDirection = 1; consist.Speed = 0;
            check(await Drive(() => consist.TravelDirection == -1) && parked.Supported,
                "A wandering train also stops and reverses at a wrong-way signal");
            network.Space.Rebase(new(512, 0, 512));
            editor.SetEnabled(true); editor.SetMode("signal");
            camera.Position -= new Vector3(512, 0, 512);
            editor.SignalAction.Select(0); editor.SignalType.Select(2);
            editor.RefreshSignalHover(camera.UnprojectPosition(nodes[3].Position + Vector3.Right * 2));
            check(editor.SignalTarget?.Node == nodes[3].Id && editor.SignalPreview != null,
                "Signal picking and preview follow floating-origin rebasing");
            network.DeleteNode(nodes[2].Id);
            check(network.Signals.Keys.All(key => key.Node != nodes[2].Id), "Deleting a node removes both of its signal slots");
            await frame();
        }
        finally
        {
            trains.Free(); editor.Free(); renderer.Free(); layer.Free(); camera.Free(); generator.Backend.Dispose();
        }
        await frame();
    }
}
