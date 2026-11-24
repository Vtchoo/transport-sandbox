using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class BrightlineChecks
{
    public static async Task Run(Action<bool, string> check, Node parent, Func<Task> frame)
    {
        var catalog = VehicleCatalog.Load();
        var stage = new Node3D(); parent.AddChild(stage);
        WorldLighting.AddTo(stage);
        var camera = new Camera3D { Position = new(-25, 12, 27), Current = true, Fov = 42 }; stage.AddChild(camera); camera.LookAt(new(0, 1.7f, 0));
        stage.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new(70, 70) }, Position = new(0, -.03f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new("4a5658"), Roughness = 1 } });
        var instances = new TrainInstances(); stage.AddChild(instances);
        var posed = new System.Collections.Generic.List<PosedVehicleVisual>();
        try
        {
            for (int index = 0; index < BrightlineModels.Ids.Length; index++)
            {
                string id = BrightlineModels.Ids[index]; var definition = catalog.Get(id);
                check(definition.Models.Keys.SequenceEqual(new[] { 1435 }), id + " supports standard gauge only");
                check(definition.Sections.Count == 0, id + " is a separate train-builder item");
                check(definition.Physics.PowerW > 0 == (index == 0), id + " has the correct traction role");
                var visual = new PosedVehicleVisual(definition, 1435); stage.AddChild(visual);
                var body = visual.Vehicle.Model.GetNode<Node3D>("body");
                var wheels = visual.Vehicle.Model.FindChildren("wheel*", "MeshInstance3D", true, false);
                check(wheels.Count == 8, id + " has two independent two-axle bogies");
                check(body.FindChildren(index == 0 ? "windshield*" : "passenger-window*", "MeshInstance3D", true, false).Count == (index == 0 ? 2 : 28), id + " has the expected glazing");
                var bounds = new Aabb(); bool first = true; int triangles = 0;
                foreach (var mesh in visual.Vehicle.Model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
                {
                    var box = mesh.GlobalTransform * mesh.Mesh.GetAabb(); bounds = first ? box : bounds.Merge(box); first = false;
                    check(mesh.Mesh is not TextMesh, id + " lettering is baked into static geometry");
                    for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                    {
                        var arrays = mesh.Mesh.SurfaceGetArrays(s); var indices = arrays[(int)Mesh.ArrayType.Index];
                        triangles += indices.VariantType == Variant.Type.PackedInt32Array ? indices.AsInt32Array().Length / 3 : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
                    }
                }
                check(Math.Abs(bounds.Size.Z - definition.Length) < .05, id + " length stays in metres");
                check(bounds.Size.X <= definition.Width + .16 && bounds.Size.X >= definition.Width, id + " width matches catalog");
                check(Math.Abs(bounds.End.Y - definition.Height) < .08 && bounds.Position.Y >= -.001, id + " height and railhead alignment match");
                check(triangles < 25000, id + " keeps a bounded triangle budget");
                GD.Print($"{id}: {bounds.Size}, {triangles} triangles");
                visual.Apply(PosedVehicleVisual.Straight(definition, new((index - 1) * 5.5f, 0, 0), Vector3.Back, index == 2 ? -1 : 1));
                posed.Add(visual);
            }
            instances.Sync(posed);
            check(instances.BatchCount > 0, "Brightline native assets use the production vehicle instancer");
            var builder = new TrainBuilder(); var train = builder.Create();
            builder.Add(train, catalog.Get(BrightlineModels.Locomotive));
            builder.Add(train, catalog.Get(BrightlineModels.Premium));
            for (int i = 0; i < 3; i++) builder.Add(train, catalog.Get(BrightlineModels.Smart));
            var rear = builder.Add(train, catalog.Get(BrightlineModels.Locomotive)); builder.Flip(train.Id, rear.Id);
            var network = new TrackNetwork(); var a = network.AddNode(Vector3.Zero, 1435); var b = network.AddNode(new(0, 0, 500), 1435);
            var edge = network.ConnectNodes(a, b)!;
            using var placement = new TrainPlacement(network);
            var poses = placement.Poses(train, new(edge.Id, .5));
            check(poses.Length == 6 && train.Items[^1].Facing == -1, "Two SCB-40s and four independent coaches fit and the rear locomotive flips");
            check(poses[0].Basis.Z.Dot(poses[^1].Basis.Z) < -.99, "Rear cab points outward after track placement");
            check(Math.Abs(TrainPhysics.Performance(train.Items.Select(i => i.Physics), false).MaxSpeed - 55.88) < .001, "Formation has a 125 mph equipment speed rating");
            check(TrainPhysics.Performance(new[] { catalog.Get(BrightlineModels.Smart).Physics }, false).MaxSpeed == 0, "Passenger coaches cannot drive alone");
            for (int i = 0; i < 6; i++) await frame();
            if (DisplayServer.GetName() != "headless")
            {
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = parent.GetViewport().GetTexture().GetImage();
                image.SavePng("res://.godot/brightline-review.png");
            }
        }
        finally { stage.Free(); }
    }
}
