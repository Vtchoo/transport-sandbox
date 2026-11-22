using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class TrainLiveryChecks
{
    private static Texture2D[] Paint(Node node, string material) => node.FindChildren("*", "MeshInstance3D", true, false)
        .Cast<MeshInstance3D>().SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
        .OfType<StandardMaterial3D>().Where(m => m.ResourceName == material).Select(m => m.AlbedoTexture!).ToArray();

    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var catalog = VehicleCatalog.Load(); var definition = catalog.Get("ge-dash9");
        check(definition.Liveries.Select(l => l.Name).SequenceEqual(new[] { "Default", "Santa Fe", "Vale", "Ferronorte" }), "Dash 9 exposes the four authored liveries in order");
        var graph = new TrackNetwork();
        var camera = new WorldCamera { Space = graph.Space }; parent.AddChild(camera); camera.SetProcess(false);
        camera.Position = new(110, 100, 400); camera.LookAt(new(0, 0, 350), Vector3.Up);
        var renderer = new TrackRenderer { Network = graph, Camera = camera }; parent.AddChild(renderer); renderer.SetProcess(false);
        var layer = new CanvasLayer(); parent.AddChild(layer);
        var manager = new WindowManager(); layer.AddChild(manager);
        var track = new TrackEditor { Network = graph, Renderer = renderer, Camera = camera, Generator = new() { Space = graph.Space }, Interface = layer, Windows = manager };
        parent.AddChild(track); track.SetProcess(false);
        var editor = new TrainEditor { Network = graph, Catalog = catalog, Camera = camera, Windows = manager }; parent.AddChild(editor); editor.SetProcess(false);
        var ui = new TrainWindows { Editor = editor, Manager = manager, TrackEditor = track, Interface = layer }; parent.AddChild(ui); ui.SetProcess(false);
        graph.ConnectNodes(graph.AddNode(Vector3.Zero), graph.AddNode(new(0, 0, 700)));
        var visuals = definition.Liveries.Select(l => new PosedVehicleVisual(definition, 1435, livery: l.Id)).ToArray();
        var instances = new TrainInstances(); parent.AddChild(instances);
        try
        {
            for (int i = 0; i < visuals.Length; i++)
            {
                var visual = visuals[i]; parent.AddChild(visual);
                visual.Apply(PosedVehicleVisual.Straight(definition, new(i * 5, 0, 0), Vector3.Back));
                var textures = Paint(visual.Vehicle.Model, definition.LiveryMaterial);
                check(textures.Length > 0 && textures.All(t => t == GD.Load<Texture2D>(definition.Liveries[i].Texture)), "Authored paint surfaces use " + definition.Liveries[i].Name);
            }
            instances.Sync(visuals);
            check(instances.BatchCount == 12, "Four liveries keep distinct body/bogie instance batches in one cell");
            ui.CatalogClick(definition.Id); int trainId = editor.Builder.Selected!.Id;
            ui.CatalogClick(definition.Id);
            var train = editor.Builder.Get(trainId);
            var selectors = ui.Views[trainId].Consist.FindChildren("*", "OptionButton", true, false).Cast<OptionButton>().ToArray();
            check(selectors.Length == 2 && selectors.All(s => s.ItemCount == 4 && s.GetParent().GetChild(0) is TrainDragButton), "Each locomotive has its own dropdown below its draggable card");
            selectors[0].EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            check(train.Items[0].Livery == "vale" && train.Items[1].Livery == "default", "Dropdown changes only its locomotive");
            editor.StartPlacement(trainId); editor.RefreshPreview(camera.UnprojectPosition(new(0, 0, 350)));
            check(editor.Valid && editor.Ghosts[0].Vehicle.Livery == "vale" && Paint(editor.Ghosts[0].Vehicle.Model, definition.LiveryMaterial).All(t => t == GD.Load<Texture2D>(definition.Liveries[2].Texture)), "Placement ghost retains the selected texture");
            check(editor.CommitPreview(), "Multi-livery train places on track");
            var before = editor.Parked.Single().Poses[0].Center;
            check(editor.SetLivery(trainId, train.Items[0].Id, "ferronorte") && train.Placed && editor.Parked.Single().Poses[0].Center == before, "Changing a placed livery preserves track position");
            var save = new GameSave(); editor.Capture(save);
            editor.Restore(JsonSerializer.Deserialize<GameSave>(JsonSerializer.Serialize(save))!);
            check(editor.Builder.Get(trainId).Items[0].Livery == "ferronorte" && editor.Parked.Single().Visuals[0].Vehicle.Livery == "ferronorte", "Save roundtrip restores livery and rendered train");
            var old = JsonSerializer.Deserialize<SavedItem>("{\"Id\":1,\"Vehicle\":\"ge-dash9\",\"Facing\":1}")!;
            check(definition.ResolveLivery(old.Livery) == "default", "Older saves without liveries load the default texture");
        }
        finally
        {
            foreach (var visual in visuals) visual.QueueFree();
            ui.QueueFree(); editor.QueueFree(); track.QueueFree(); renderer.QueueFree(); instances.QueueFree(); layer.QueueFree(); camera.QueueFree();
            await frame(); await frame();
        }
    }
}
