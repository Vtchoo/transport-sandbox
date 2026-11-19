using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class TrainPlacementChecks
{
    private static TrainPosition Position(JsonElement data) => new(data.GetProperty("edge").GetInt32(), data.GetProperty("u").GetDouble(), data.GetProperty("facing").GetInt32());
    public static void Parity(Action<bool, string> check)
    {
        var catalog = VehicleCatalog.Load();
        foreach (var scenario in JsonData.Read("res://tests/train-placement-fixtures.json").EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            var network = new TrackNetwork();
            int gauge = scenario.GetProperty("gauge").GetInt32();
            foreach (var node in scenario.GetProperty("nodes").EnumerateArray())
                network.AddNode(JsonData.Vector(node.GetProperty("position")), gauge).Axis = JsonData.Vector(node.GetProperty("axis"));
            foreach (var data in scenario.GetProperty("edges").EnumerateArray())
            {
                int id = data.GetProperty("id").GetInt32();
                var edge = new TrackEdge { Id = id, A = data.GetProperty("a").GetInt32(), B = data.GetProperty("b").GetInt32(), Gauge = gauge, SideA = data.GetProperty("aSide").GetInt32(), SideB = data.GetProperty("bSide").GetInt32(), LengthA = data.GetProperty("aLength").GetSingle(), LengthB = data.GetProperty("bLength").GetSingle() };
                if (data.TryGetProperty("station", out var station))
                {
                    edge.Station = station.GetProperty("id").GetInt32();
                    edge.Platforms.AddRange(station.GetProperty("sides").EnumerateArray().Select(s => s.GetInt32()));
                }
                network.Edges[id] = edge;
            }
            using var placement = new TrainPlacement(network);
            var train = new TrainConsist();
            int itemId = 0;
            train.Items.AddRange(scenario.GetProperty("ids").EnumerateArray().Select(id => new TrainItem(++itemId, catalog.Get(id.GetString()!))));
            void Pose(VehiclePose pose, JsonElement expected)
            {
                foreach (var (point, field) in new[] { (pose.Center, "center"), (pose.Front, "front"), (pose.Rear, "rear"), (pose.FrontDirection, "frontDirection"), (pose.RearDirection, "rearDirection") })
                    check(point.DistanceTo(JsonData.Vector(expected.GetProperty(field))) < .001, name + ": " + field + " matches browser within native coordinate precision");
                var sections = expected.GetProperty("sections");
                check(pose.Sections.Length == sections.GetArrayLength(), name + ": articulated section count matches");
                for (int i = 0; i < pose.Sections.Length; i++)
                    Pose(pose.Sections[i], sections[i]);
            }
            foreach (var sample in scenario.GetProperty("samples").EnumerateArray())
            {
                var position = Position(sample.GetProperty("position"));
                try
                {
                    var poses = placement.Poses(train, position);
                    check(!sample.TryGetProperty("error", out _), name + ": accepted placement matches browser");
                    if (sample.TryGetProperty("poses", out var expected))
                        for (int i = 0; i < poses.Length; i++)
                            Pose(poses[i], expected[i]);
                    var snap = placement.SnapStation(position, train.Length);
                    var station = sample.GetProperty("station");
                    check(snap.HasValue == (station.ValueKind != JsonValueKind.Null), name + ": station snap eligibility matches browser");
                    if (snap is { } result && station.ValueKind != JsonValueKind.Null)
                        check(result.Station == station.GetProperty("station").GetInt32() && Math.Abs(result.Position.U - Position(station.GetProperty("position")).U) < .00001, name + ": station end location matches");
                }
                catch (InvalidOperationException error) { check(sample.TryGetProperty("error", out var expected) && expected.GetString() == error.Message, name + ": rejected placement reason matches browser"); }
            }
            foreach (var walk in scenario.GetProperty("walks").EnumerateArray())
                try
                {
                    var sample = placement.Sample(new(network.Edges.Keys.First(), .7), walk.GetProperty("distance").GetDouble());
                    check(walk.TryGetProperty("point", out var expected) && sample.Point.DistanceTo(JsonData.Vector(expected)) < .001, name + ": signed route walking matches browser");
                }
                catch (InvalidOperationException error) { check(walk.TryGetProperty("error", out var expected) && expected.GetString() == error.Message, name + ": route walking rejects the same invalid path"); }
        }
    }
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        Parity(check);
        TrainMovementChecks.Parity(check);
        var graph = new TrackNetwork();
        var catalog = VehicleCatalog.Load();
        var generator = new TerrainGenerator() { Space = graph.Space };
        var camera = new WorldCamera { Space = graph.Space };
        parent.AddChild(camera);
        camera.SetProcess(false);
        camera.Position = new(110, 100, 400);
        camera.LookAt(new(0, 0, 350), Vector3.Up);
        var renderer = new TrackRenderer { Network = graph, Camera = camera };
        parent.AddChild(renderer);
        renderer.SetProcess(false);
        var layer = new CanvasLayer();
        parent.AddChild(layer);
        var manager = new WindowManager();
        layer.AddChild(manager);
        var trackEditor = new TrackEditor { Network = graph, Renderer = renderer, Camera = camera, Generator = generator, Interface = layer, Windows = manager };
        parent.AddChild(trackEditor);
        trackEditor.SetProcess(false);
        var editor = new TrainEditor { Network = graph, Catalog = catalog, Camera = camera, Windows = manager };
        parent.AddChild(editor);
        editor.SetProcess(false);
        var ui = new TrainWindows { Editor = editor, Manager = manager, TrackEditor = trackEditor, Interface = layer };
        parent.AddChild(ui);
        ui.SetProcess(false);
        var a = graph.AddNode(Vector3.Zero);
        var b = graph.AddNode(new(0, 0, 700));
        var edge = graph.ConnectNodes(a, b)!;
        string warning = "";
        editor.Warning += text => warning = text;
        try
        {
            ui.CatalogClick("atsf-f7a");
            int id = editor.Builder.Selected!.Id;
            check(ui.Views[id].Window.Visible && ui.Views[id].EditTab && !editor.Placing && editor.Builder.Get(id).Items.Count == 1, "Catalog creates a train and opens Edit train without starting placement");
            ui.CatalogClick("atsf-coach");
            check(editor.Builder.Get(id).Items.Count == 2, "Catalog appends to the frontmost Edit train window");
            ui.OpenTrain(id, false);
            ui.CatalogClick("ge-809");
            int second = editor.Builder.Selected!.Id;
            check(second != id && editor.Builder.Trains.Count == 2, "Frontmost viewport tab makes catalog clicks create a new train instead of falling through");
            ui.OpenTrain(id, true);
            ui.CatalogWindow.Open();
            check(ui.Destination()?.Id == id, "Catalog has no selection independent of train window stacking");
            ui.CatalogClick("atsf-pleasure-dome");
            ui.OpenTrain(id, true);
            ui.Views[id].Place.EmitSignal(Button.SignalName.Pressed);
            var point = camera.UnprojectPosition(new(0, 0, 350));
            editor.RefreshPreview(point);
            check(editor.Valid && editor.Ghosts.Count == 3 && editor.Ghosts.All(v => v.Visible), "Placement previews the full train translucently on compatible track");
            int beforeFacing = editor.Facing;
            editor.ReversePreview();
            check(editor.Facing == -beforeFacing, "R reverses placement preview direction");
            editor.ReversePreview();
            editor.RefreshPreview(point);
            check(editor.CommitPreview() && editor.Builder.Get(id).Placed && !ui.Views[id].EditTab, "Click places the full train and opens its viewport tab");
            check(editor.Instances.BatchCount <= 9 && editor.Instances.BatchCount > 0 && editor.Parked.Single(p => p.Id == id).Visuals.All(v => v.Vehicle.Model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().All(m => !m.Visible)), "Placed vehicles use merged body/bogie instancing instead of imported leaf draw calls");
            var parked = editor.Parked.Single(p => p.Id == id);
            var originalFront = parked.Poses[0].BodyCenter;
            check(editor.PickTrain(camera.UnprojectPosition(originalFront + Vector3.Up * 2)) == id, "World picking finds an instanced train through its original model geometry");
            check(editor.AddVehicle("atsf-coach", id) && editor.Builder.Get(id).Placed && editor.Parked.Single(p => p.Id == id).Poses[0].BodyCenter.DistanceTo(originalFront) < .001, "Stopped train edits remain on track and preserve the first original vehicle");
            editor.Builder.Get(id).Running = true;
            int count = editor.Builder.Get(id).Items.Count;
            warning = "";
            check(!editor.AddVehicle("atsf-coach", id) && editor.Builder.Get(id).Items.Count == count && warning == "Train must be stopped first.", "Editing a started train is rejected with the stopped warning");
            editor.Builder.Get(id).Running = false;
            var item = editor.Builder.Get(id).Items[1];
            check(ui.DropAt(second, 1, TrainDragButton.Data("item", id, item.Id)) && editor.Builder.Get(second).Items.Count == 2, "Drag payload transfers a vehicle between trains");
            check(ui.DropAt(id, 1, TrainDragButton.Data("item", second, item.Id)), "Transferred vehicle can be returned to its original consist");
            editor.Edit("Flip vehicle", () => { editor.Builder.Flip(id, item.Id); return true; });
            check(editor.Builder.Get(id).Items.First(i => i.Id == item.Id).Facing == -1 && editor.Builder.Get(id).Placed, "Flipping a stopped vehicle preserves its placed train");
            editor.Edit("Flip vehicle", () => { editor.Builder.Flip(id, item.Id); return true; });
            var trash = ui.Views[id].Trash;
            var drag = TrainDragButton.Data("item", id, item.Id);
            check(trash._CanDropData(Vector2.Zero, drag), "Train trash accepts a vehicle dragged from that train");
            trash._DropData(Vector2.Zero, drag);
            check(editor.Builder.Get(id).Items.All(i => i.Id != item.Id) && editor.Builder.Get(id).Placed, "Removing a car preserves the remaining placed consist");
            editor.AddVehicle(item.Definition.Id, id, 1);
            ui.OpenTrain(second, true);
            editor.StartPlacement(second);
            editor.RefreshPreview(point);
            check(!editor.Valid, "Overlapping parked train placement is rejected");
            editor.CancelPlacement();
            ui.OpenTrain(second, true);
            editor.StartPlacement(second);
            parked = editor.Parked.Single(p => p.Id == id);
            var end = editor.Placement.Sample(parked.Position, -editor.Builder.Get(id).Length / 2).Point;
            editor.RefreshPreview(camera.UnprojectPosition(end));
            check(editor.Valid && editor.AppendTarget?.Front == false, "Hovering a stopped train rear previews attachment");
            check(editor.CommitPreview() && editor.Builder.Trains.All(t => t.Id != second) && editor.Builder.Get(id).Items.Count == count + 1, "Attachment merges the incoming train into the existing train");
            editor.Edit("Remove vehicle", () => { editor.Builder.Remove(id, editor.Builder.Get(id).Items[^1].Id); return true; });
            editor.AddVehicle("ge-809");
            second = editor.Builder.Selected!.Id;
            ui.OpenTrain(second, true);
            editor.StartPlacement(second);
            parked = editor.Parked.Single(p => p.Id == id);
            var frontEnd = editor.Placement.Sample(parked.Position, editor.Builder.Get(id).Length / 2).Point;
            editor.RefreshPreview(camera.UnprojectPosition(frontEnd));
            check(editor.Valid && editor.AppendTarget?.Front == true, "Hovering a stopped train front previews front attachment");
            editor.ReversePreview();
            editor.RefreshPreview(camera.UnprojectPosition(frontEnd));
            check(editor.Valid && editor.CommitPreview() && editor.Builder.Get(id).Items[0].Definition.Id == "ge-809" && editor.Builder.Get(id).Items[0].Facing == -1, "Reversed front attachment prepends and flips the incoming consist");
            editor.Edit("Remove vehicle", () => { editor.Builder.Remove(id, editor.Builder.Get(id).Items[0].Id); return true; });
            editor.AddVehicle("ge-809");
            second = editor.Builder.Selected!.Id;
            ui.OpenTrain(second, true);
            editor.StartPlacement(second);
            editor.CancelPlacement();
            check(!editor.Builder.Get(second).Placed, "Cancelling placement leaves train construction unchanged");
            var savedPosition = editor.Parked.Single(p => p.Id == id).Position;
            void ReplaceTrain()
            {
                editor.StartPlacement(id);
                if (editor.Facing != savedPosition.Facing) editor.ReversePreview();
                editor.RefreshPreview(camera.UnprojectPosition(editor.Placement.Sample(savedPosition, 0).Point));
                check(editor.Valid && editor.CommitPreview(), "Stopped train can be placed back on its original track");
            }
            check(editor.RemoveFromTrack(id) && !editor.Builder.Get(id).Placed && editor.Parked.All(p => p.Id != id), "Stopped train can be removed from track");
            ReplaceTrain();
            edge.LengthA = edge.LengthB = 15;
            graph.MoveNode(b, new(0, 0, 45));
            editor.Revalidate();
            warning = "";
            check(editor.AddVehicle("atsf-coach", id) && !editor.Builder.Get(id).Placed && warning.Contains("lifted for repositioning"), "An edited train with insufficient connected track is lifted for repositioning");
            editor.Edit("Remove vehicle", () => { editor.Builder.Remove(id, editor.Builder.Get(id).Items[^1].Id); return true; });
            edge.LengthA = edge.LengthB = 700f / 3;
            graph.MoveNode(b, new(0, 0, 700));
            ReplaceTrain();
            editor.Revalidate();
            var lastValidFront = editor.Parked.Single(p => p.Id == id).Poses[0].BodyCenter;
            graph.Edges.Remove(edge.Id);
            graph.NotifyChanged();
            editor.Revalidate();
            check(!editor.Parked.Single(p => p.Id == id).Supported && editor.Parked.Single(p => p.Id == id).Poses[0].BodyCenter.DistanceTo(lastValidFront) < .001, "Removed supporting track leaves the train at its last valid position");
            graph.Edges[edge.Id] = edge;
            graph.NotifyChanged();
            editor.Revalidate();
            var na = graph.AddNode(new(30, 0, 0), 914);
            var nb = graph.AddNode(new(30, 0, 700), 914);
            var narrow = graph.ConnectNodes(na, nb)!;
            ui.OpenTrain(id, false);
            ui.OpenTrain(second, false);
            ui.CatalogClick("drgw-consolidation");
            int steam = editor.Builder.Selected!.Id;
            editor.StartPlacement(steam);
            editor.RefreshPreview(camera.UnprojectPosition(new(30, 0, 420)));
            check(editor.Valid && editor.CommitPreview() && editor.TrackGauge(steam) == 914, "Articulated steam train places on its supported narrow gauge");
            editor._Process(.05);
            check(editor.Smoke.Outlets.Any(o => o.Steam), "Parked steam trains expose idle exhaust outlets");
            ui.OpenTrain(steam, true);
            check(!ui.CatalogButtons["atsf-f7a"].Visible && ui.CatalogButtons["drgw-wood-coach"].Visible, "Catalog filters using the edited train's placed track gauge");
            editor.Builder.Get(steam).Speed = 1;
            warning = "";
            check(!editor.RemoveFromTrack(steam) && warning == "Train must be stopped first.", "Pickup rejects a moving train");
            editor.Builder.Get(steam).Speed = 0;
            graph.AddStation(narrow, 1);
            editor.RemoveFromTrack(steam);
            editor.StartPlacement(steam);
            if (editor.Facing < 0)
                editor.ReversePreview();
            double half = editor.Builder.Get(steam).Length / 2;
            editor.RefreshPreview(camera.UnprojectPosition(new(30, 0, (float)(700 - half - 2))));
            check(editor.Valid && editor.SnappedStation == narrow.Station, "Placement snaps the leading coupler to its own station end");
            TrainMovementChecks.Movement(check, editor, ui, steam);
            CameraOriginChecks.TrainOrigin(check, editor, camera);
            await frame();
        }
        finally { ui.Free(); editor.Free(); trackEditor.Free(); renderer.Free(); layer.Free(); camera.Free(); generator.Backend.Shutdown(); generator.Backend.Dispose(); }
    }
}
