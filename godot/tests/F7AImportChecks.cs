using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class F7AImportChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var catalog = VehicleCatalog.Load();
        var original = catalog.Get("atsf-f7a");
        var definition = catalog.Get("emd-f7a");
        check(definition.DisplayName == "F7A" && original.Id != definition.Id, "Authored F7A has its own catalog entry and retains the procedural locomotive");
        check(definition.Models.Keys.SequenceEqual(new[] { 1435 }) && Math.Abs(definition.BogieCenters - 9.144) < .001, "F7A preserves standard gauge and 30 ft bogie spacing");
        check(definition.Physics == original.Physics, "F7A retains the existing locomotive physics specifications");
        var stage = new Node3D(); parent.AddChild(stage);
        var visual = new PosedVehicleVisual(definition, 1435); stage.AddChild(visual);
        var instances = new TrainInstances(); stage.AddChild(instances);
        try
        {
            WorldLighting.AddTo(stage);
            var camera = new Camera3D { Current = true, Position = new(-20, 10, 24), Fov = 42, Far = 3000 };
            stage.AddChild(camera); camera.LookAt(new(0, 2, 0), Vector3.Up);
            stage.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new(200, 200) }, Position = new(0, -.1f, 0), MaterialOverride = TrackRenderer.Material(new("738c62")) });
            visual.Apply(PosedVehicleVisual.Straight(definition, Vector3.Zero, Vector3.Back));
            var model = visual.Vehicle.Model;
            check(model.FindChild("body", true, false) is Node3D && model.FindChild("bogie_0", true, false) is Node3D && model.FindChild("bogie_1", true, false) is Node3D,
                "Imported F7A exposes independently posed body and bogies");
            var wheelsets = model.FindChildren("wheelset-*", "Node3D", true, false);
            check(wheelsets.Count == 4, "F7A keeps four authored wheelset components");
            var meshes = model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToArray();
            var materials = meshes.SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial)).OfType<StandardMaterial3D>().ToArray();
            var paint = materials.Where(m => m.ResourceName == definition.LiveryMaterial).ToArray();
            check(paint.Length > 0 && paint.All(m => m.AlbedoTexture == GD.Load<Texture2D>(definition.Liveries[0].Texture)), "F7A paint surfaces use the authored Santa Fe texture");
            check(materials.Where(m => m.ResourceName != definition.LiveryMaterial).All(m => m.AlbedoTexture == null), "Hardware retains solid materials outside the paint atlas");
            var audio = new TrainAudio(); stage.AddChild(audio);
            check(audio.PlayHorn(new(9999, definition.Id, Vector3.Zero, 0, 0, double.PositiveInfinity), camera), "New F7A uses the working F7 horn profile");
            audio.Settings.Muted = true;
            instances.Sync(new[] { visual });
            check(instances.BatchCount == 3 && meshes.All(m => !m.Visible), "F7A renders through the shared instancing pipeline");
            if (DisplayServer.GetName() != "headless")
            {
                for (int i = 0; i < 5; i++) await frame();
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string output = ProjectSettings.GlobalizePath("res://.godot/f7a-import-preview.png");
                using var image = parent.GetViewport().GetTexture().GetImage(); image.SavePng(output);
                GD.Print("F7A preview: " + output);
            }
        }
        finally { stage.Free(); await frame(); }
    }
}
