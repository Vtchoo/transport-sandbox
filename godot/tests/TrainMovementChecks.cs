using Godot;
using System;
using System.Linq;
using System.Text.Json;

namespace TransportSandbox.Tests;

public static class TrainMovementChecks
{
    private static TrainPosition Position(JsonElement data) => new(data.GetProperty("edge").GetInt32(), data.GetProperty("u").GetDouble(), data.GetProperty("facing").GetInt32(), data.TryGetProperty("routeIndex", out var index) ? index.GetInt32() : 0);
    public static void Parity(Action<bool, string> check)
    {
        var data = JsonData.Read("res://tests/train-movement-fixtures.json");
        foreach (var scenario in data.GetProperty("routes").EnumerateArray())
        {
            var graph = new TrackNetwork();
            foreach (var node in scenario.GetProperty("nodes").EnumerateArray())
                graph.AddNode(JsonData.Vector(node.GetProperty("position"))).Axis = JsonData.Vector(node.GetProperty("axis"));
            foreach (var edge in scenario.GetProperty("edges").EnumerateArray())
            {
                int id = edge.GetProperty("id").GetInt32();
                graph.Edges[id] = new TrackEdge { Id = id, A = edge.GetProperty("a").GetInt32(), B = edge.GetProperty("b").GetInt32(), Gauge = 1435, SideA = edge.GetProperty("aSide").GetInt32(), SideB = edge.GetProperty("bSide").GetInt32(), LengthA = edge.GetProperty("aLength").GetSingle(), LengthB = edge.GetProperty("bLength").GetSingle() };
            }
            using var placement = new TrainPlacement(graph);
            var origin = Position(scenario.GetProperty("position"));
            var route = new TrainRoute(placement, origin, scenario.GetProperty("seed").GetUInt32());
            foreach (var sample in scenario.GetProperty("samples").EnumerateArray())
            {
                double distance = sample.GetProperty("distance").GetDouble();
                var result = route.Sample(origin, distance);
                var expected = Position(sample.GetProperty("position"));
                check(result.Position.Edge == expected.Edge && result.Position.Facing == expected.Facing && result.Position.RouteIndex == expected.RouteIndex, "Seeded junction choice and itinerary index match browser");
                check(result.Point.DistanceTo(JsonData.Vector(sample.GetProperty("point"))) < .001, "Junction route position matches browser within 1 mm");
                check(route.Sample(result.Position, -distance).Point.DistanceTo(JsonData.Vector(sample.GetProperty("back"))) < .001, "Returning over a junction follows the same itinerary");
                var radius = sample.GetProperty("radius");
                double actual = route.TurnRadius(result.Position);
                check(radius.ValueKind == JsonValueKind.Null ? double.IsPositiveInfinity(actual) : Math.Abs(actual - radius.GetDouble()) / radius.GetDouble() < .005, "Turn radius matches browser within native tangent precision");
            }
            check(Math.Abs(route.Clearance(origin, 2000) - scenario.GetProperty("clearance").GetDouble()) < .001, "Dead-end route clearance matches browser");
        }
        var catalog = VehicleCatalog.Load();
        foreach (var group in data.GetProperty("physics").EnumerateArray().GroupBy(e => (e.GetProperty("mode").GetString(), e.GetProperty("grade").GetDouble())))
        {
            var cars = group.First().GetProperty("ids").EnumerateArray().Select(id => catalog.Get(id.GetString()!).Physics).ToArray();
            double speed = 0, distance = 0;
            for (int i = 1; i <= 1800; i++)
            {
                var result = TrainPhysics.Step(cars, speed, i <= 1200, group.Key.Item2, group.Key.Item1 == "ho", 1.0 / 60, 24, catalog.Definitions.Max(d => d.Physics.MaxSpeedMps));
                speed = result.Speed;
                distance += result.Distance;
                var expected = group.FirstOrDefault(e => e.GetProperty("step").GetInt32() == i);
                if (expected.ValueKind != JsonValueKind.Undefined)
                    check(Math.Abs(speed - expected.GetProperty("speed").GetDouble()) < 1e-8 && Math.Abs(distance - expected.GetProperty("distance").GetDouble()) < 1e-7, "Realistic/HO acceleration, grade and braking trajectories match browser");
            }
        }
    }
    public static void Movement(Action<bool, string> check, TrainEditor editor, TrainWindows ui, int id)
    {
        check(editor.CommitPreview(), "Movement fixture places its steam train at the station end");
        var train = editor.Builder.Get(id);
        var parked = editor.Parked.Single(p => p.Id == id);
        var origin = parked.Poses[0].BodyCenter;
        ui.Views[id].Run.EmitSignal(Button.SignalName.Pressed);
        check(train.Running && !ui.Views[id].Run.Disabled && ui.Views[id].Run.TooltipText == "Stop train", "Play starts a placed powered train and updates its command");
        editor.AdvanceMovement(.1);
        check(train.TravelDirection == -1 && train.Running, "A started train reverses at a dead end without flipping its cars");
        for (int i = 0; i < 35; i++)
            editor.AdvanceMovement(.1);
        check(train.Speed < -.1 && parked.Supported && parked.Poses[0].BodyCenter.DistanceTo(origin) > .1, "A reversing train accelerates and moves its articulated poses along the rails");
        var animated = parked.Visuals[0].Vehicle.Model.FindChildren("*motion_*", "Node3D", true, false).Cast<Node3D>().FirstOrDefault();
        var gearTransform = animated?.Transform;
        var instance = editor.Instances.GetChild(0);
        editor.AdvanceMovement(.1);
        check(editor.Instances.GetChildren().Contains(instance), "Moving trains update existing instancing batches instead of rebuilding rendering nodes");
        check(animated != null && animated.Transform != gearTransform, "Steam running gear animates with accepted travel distance");
        int count = train.Items.Count;
        check(!editor.AddVehicle("drgw-wood-coach", id) && train.Items.Count == count, "Started trains cannot be edited during movement");
        // A rejected edit restores the builder transaction, so fetch the current model again.
        train = editor.Builder.Get(id);
        double movingSpeed = Math.Abs(train.Speed);
        ui.Views[id].Run.EmitSignal(Button.SignalName.Pressed);
        train = editor.Builder.Get(id);
        check(!train.Running && Math.Abs(train.Speed) == movingSpeed && !train.Stopped, "Pause commands service braking rather than stopping instantly");
        for (int i = 0; i < 100 && !train.Stopped; i++)
            editor.AdvanceMovement(.1);
        check(train.Stopped && editor.Parked.Single(p => p.Id == id).Supported, "A stopped command brakes to rest while keeping the train on its route");
        check(editor.AddVehicle("drgw-wood-coach", id) && editor.Builder.Get(id).Placed, "A train can be edited on its track again after braking to rest");
        parked = editor.Parked.Single(p => p.Id == id);
        var edge = editor.Network.Edges[parked.Position.Edge];
        var junction = editor.Network.Nodes[edge.A];
        var straight = editor.Network.AddNode(new(30, 0, -400), 914);
        var branch = editor.Network.AddNode(new(150, 0, -400), 914);
        editor.Network.ConnectNodes(junction, straight, -1);
        editor.Network.ConnectNodes(junction, branch, -1);
        parked.Position = new(edge.Id, .1);
        parked.Route = new(editor.Placement, parked.Position, 42);
        parked.Poses = editor.Placement.Poses(editor.Builder.Get(id), parked.Position, parked.Route.Sample);
        train = editor.Builder.Get(id);
        train.TravelDirection = -1;
        editor.ToggleRunning(id);
        for (int i = 0; i < 260; i++)
            editor.AdvanceMovement(.1);
        check(train.Running && parked.Supported && parked.Position.Edge != edge.Id && parked.Position.RouteIndex < 0, "A full articulated train traverses a random junction using one persistent itinerary");
        var lastPose = parked.Poses[0].BodyCenter;
        editor.Network.Edges.Remove(parked.Position.Edge);
        editor.Network.NotifyChanged();
        editor.Revalidate();
        check(!train.Running && train.Speed == 0 && !parked.Supported && parked.Poses[0].BodyCenter == lastPose, "Removing track under a moving train stops it without teleporting its cars");
        editor.AddVehicle("ge-809");
        int incoming = editor.Builder.Selected!.Id;
        editor.StartPlacement(incoming);
        if (editor.Facing != -1)
            editor.ReversePreview();
        // Collision braking fixtures deliberately share a block; use the placement override.
        editor.PlacementBlockOverride = () => true;
        editor.RefreshPreview(editor.Camera.UnprojectPosition(new(0, 0, 550)));
        check(editor.Valid && editor.CommitPreview(), "Collision fixture places an opposing train on the occupied line");
        editor.ToggleRunning(incoming);
        for (int i = 0; i < 650; i++)
            editor.AdvanceMovement(.1);
        var approach = editor.Parked.Single(p => p.Id == incoming);
        var other = editor.Parked.First(p => p.Id != incoming && editor.TrackGauge(p.Id) == 1435);
        var incomingTrain = editor.Builder.Get(incoming);
        var otherTrain = editor.Builder.Get(other.Id);
        check(incomingTrain.Running && Math.Abs(incomingTrain.Speed) < .1 && approach.Supported, "An approaching train waits before occupied track while retaining its started command");
        check(!approach.Poses.Select((pose, i) => other.Poses.Select((otherPose, j) => TrainPlacement.Overlap(incomingTrain.Items[i].Definition, pose, otherTrain.Items[j].Definition, otherPose)).Any(v => v)).Any(v => v), "The collision braking corridor prevents overlapping parked cars");
        editor.ToggleRunning(incoming);
        editor.AddVehicle("atsf-coach");
        int unpowered = editor.Builder.Selected!.Id;
        editor.StartPlacement(unpowered);
        editor.RefreshPreview(editor.Camera.UnprojectPosition(new(0, 0, 100)));
        check(editor.Valid && editor.CommitPreview(), "An unpowered consist can be parked independently");
        editor.ToggleRunning(unpowered);
        check(!editor.Builder.Get(unpowered).Running, "An unpowered consist cannot be started");
    }
}
