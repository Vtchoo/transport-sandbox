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
            var steel = materials.First(m => m.ResourceName == "metal-stainless");
            var glass = materials.First(m => m.ResourceName == "glass-tinted");
            check(steel.Metallic == .75f && Math.Abs(steel.Roughness - .32f) < .001 && glass.Metallic == 0 && Math.Abs(glass.Roughness - .15f) < .001,
                "Named steel and glass use the game's PBR settings");
            check(glass.AlbedoColor == new Color("263e49") && glass.Transparency == BaseMaterial3D.TransparencyEnum.Disabled,
                "Glass uses the shared dark opaque tint instead of exported gray");
            check(paint.All(m => m.Metallic == 0 && Math.Abs(m.Roughness - .45f) < .001), "Livery textures use the game paint surface response");
            var duplicate = new VehicleVisual(definition, 1435);
            var ghost = new PosedVehicleVisual(definition, 1435, preview: true);
            var imported = GD.Load<PackedScene>(definition.Models[1435]).Instantiate<Node3D>();
            StandardMaterial3D FindMaterial(Node root, string name) => root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>()
                .SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial)).OfType<StandardMaterial3D>().First(m => m.ResourceName == name);
            try
            {
                var coach = new VehicleVisual(catalog.Get("atsf-coach"), 1435);
                try
                {
                    var coachSilver = coach.Model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>()
                        .SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
                        .OfType<StandardMaterial3D>().First(m => Math.Abs(m.Metallic - .75f) < .001);
                    check(Math.Abs(steel.Roughness - coachSilver.Roughness) < .001 &&
                        steel.AlbedoColor.IsEqualApprox(coachSilver.AlbedoColor),
                        "Custom stainless matches the streamlined passenger car silver finish");
                }
                finally { coach.Free(); }
                check(FindMaterial(duplicate.Model, "metal-stainless") == steel, "Identical vehicles share runtime materials for batching");
                check(FindMaterial(imported, "metal-stainless") != steel && FindMaterial(imported, "glass-tinted").AlbedoColor != glass.AlbedoColor,
                    "Runtime replacements leave the imported resources untouched");
                var ghostSteel = FindMaterial(ghost.Vehicle.Model, "metal-stainless");
                check(ghostSteel != steel && ghostSteel.Metallic == steel.Metallic && ghostSteel.AlbedoColor.A <= .42f && steel.AlbedoColor.A == 1,
                    "Placement previews inherit steel settings without changing solid vehicles");
            }
            finally { duplicate.Free(); ghost.Free(); imported.Free(); }
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
                string? review = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--f7a-review="))?.Split('=', 2)[1];
                if (!string.IsNullOrEmpty(review))
                {
                    string directory = ProjectSettings.GlobalizePath("res://.godot/" + review);
                    Directory.CreateDirectory(directory);
                    camera.Projection = Camera3D.ProjectionType.Orthogonal;
                    var target = new Vector3(0, 2.2f, 0);
                    foreach (var shot in new[] {
                        ("left-front", new Vector3(-22, 8, 25), 11f, Vector3.Up),
                        ("right-front", new Vector3(22, 8, 25), 11f, Vector3.Up),
                        ("left", new Vector3(-25, 2.2f, 0), 11f, Vector3.Up),
                        ("right", new Vector3(25, 2.2f, 0), 11f, Vector3.Up),
                        ("front", new Vector3(0, 2.4f, 30), 6f, Vector3.Up),
                        ("rear", new Vector3(0, 2.4f, -30), 6f, Vector3.Up),
                        ("roof", new Vector3(0, 30, 0), 11f, Vector3.Right) })
                    {
                        camera.Position = shot.Item2; camera.Size = shot.Item3;
                        camera.LookAt(target, shot.Item4);
                        for (int i = 0; i < 3; i++) await frame();
                        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var capture = parent.GetViewport().GetTexture().GetImage();
                        capture.SavePng(Path.Combine(directory, shot.Item1 + ".png"));
                    }
                    GD.Print("F7A review views: " + directory);
                }
            }
        }
        finally { stage.Free(); await frame(); }
    }
}
