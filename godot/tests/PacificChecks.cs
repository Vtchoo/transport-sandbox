using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class PacificChecks
{
    public static async Task Run(Action<bool, string> check, Node parent, Func<Task> frame)
    {
        var catalog = VehicleCatalog.Load(); var stage = new Node3D(); parent.AddChild(stage);
        try
        {
            WorldLighting.AddTo(stage);
            var camera = new Camera3D { Position = new(-25, 10, 25), Current = true, Fov = 42 }; stage.AddChild(camera); camera.LookAt(new(0, 1.7f, 0));
            stage.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new(70, 70) }, Position = new(0, -.03f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new("62665d") } });
            var instances = new TrainInstances(); stage.AddChild(instances);
            var visuals = new System.Collections.Generic.List<PosedVehicleVisual>();
            var composition = catalog.Get(PacificModels.Engine);
            check(composition.Sections.Count == 2, "Pacific is one catalog composition with two rigid sections");
            check(!catalog.Definitions.Any(d => d.Id == PacificModels.Tender), "Tender is not a separate builder item");
            var visual = new PosedVehicleVisual(composition, 1435); stage.AddChild(visual);
            foreach (string id in PacificModels.Ids)
            {
                bool engine = id == PacificModels.Engine; int index = engine ? 0 : 1;
                var definition = composition.Sections[index].Definition;
                var model = visual.Vehicle.Model.GetNode<Node3D>("section_" + index);
                string prefix = $"section_{index}_";
                check(definition.Sections.Count == 0 && definition.Models.Keys.SequenceEqual(new[] { 1435 }), id + " is a standard gauge rigid section");
                check(model.GetNodeOrNull(prefix + "body") != null && model.GetNodeOrNull(prefix + "bogie_0") != null && model.GetNodeOrNull(prefix + "bogie_1") != null, id + " supports production placement");
                var bounds = new Aabb(); bool first = true; int triangles = 0;
                foreach (var mesh in model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
                {
                    var box = mesh.GlobalTransform * mesh.Mesh.GetAabb(); bounds = first ? box : bounds.Merge(box); first = false;
                    check(mesh.Mesh is not TextMesh, "Lettering is baked");
                    for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                    {
                        var arrays = mesh.Mesh.SurfaceGetArrays(i); var indices = arrays[(int)Mesh.ArrayType.Index];
                        triangles += indices.VariantType == Variant.Type.PackedInt32Array ? indices.AsInt32Array().Length / 3 : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
                    }
                }
                GD.Print($"{id}: {bounds.Size}, {triangles} triangles, bottom {bounds.Position.Y}");
                check(Math.Abs(bounds.Size.Z - definition.Length) < .05, id + " matches catalog length");
                check(bounds.Size.X <= definition.Width + .05, id + " stays within width");
                check(bounds.End.Y <= definition.Height + .05 && bounds.Position.Y >= -.01, id + " clears the railhead and fits height");
                check(triangles < 35000, id + " has bounded geometry");
                if (engine)
                {
                    check(model.FindChildren("motion_driver_*", "Node3D", true, false).Count == 6, "Pacific has six driving wheels");
                    check(model.FindChildren("pilot-wheel-*", "Node3D", true, false).Count == 4, "Pacific has four pilot wheels");
                    check(model.FindChildren("trailing-wheel-*", "Node3D", true, false).Count == 2, "Pacific has two trailing wheels");
                    check(Math.Abs(definition.Height - 15 * .3048) < .00001 && Math.Abs(definition.Length - (48 * .3048 + 2.625 * .0254)) < .00001, "K4s matches period drawing envelope");
                    check(Math.Abs(definition.RunningGearWheelRadius * 2 - 80 * .0254) < .00001, "K4s has prototype 80-inch drivers");
                    var rearDriver = model.GetNode<Node3D>(prefix + "body/motion_driver_1_0");
                    var frontDriver = model.GetNode<Node3D>(prefix + "body/motion_driver_1_2");
                    var pilot = model.GetNode<Node3D>(prefix + "bogie_0/pilot-wheel-front-1");
                    var trailing = model.GetNode<Node3D>(prefix + "bogie_1/trailing-wheel-1");
                    check(Math.Abs(frontDriver.GlobalPosition.Z - rearDriver.GlobalPosition.Z - (13 * .3048 + 10 * .0254)) < .00001, "13 ft 10 in driving wheelbase");
                    check(Math.Abs(pilot.GlobalPosition.Z - trailing.GlobalPosition.Z - (36 * .3048 + 2 * .0254)) < .00001, "36 ft 2 in total engine wheelbase");
                    check(Math.Abs(pilot.Position.Y * 2 - 36 * .0254) < .00001 && Math.Abs(trailing.Position.Y * 2 - 50 * .0254) < .00001, "Prototype pilot and trailing wheel diameters");
                    var wheel = model.GetNode<Node3D>(prefix + "body/motion_driver_1_0");
                    var rod = model.GetNode<Node3D>(prefix + "body/motion_coupling_rod_1");
                    var initial = rod.Position;
                    visual.AdvanceRunningGear(Math.Tau * PacificModels.DriverRadius / 4);
                    check(Math.Abs(visual.WheelTurns - .25) < .0001, "Wheel phase follows actual driver circumference");
                    check(Math.Abs(wheel.Rotation.X - Mathf.Pi / 2) < .01 && rod.Position.DistanceTo(initial) > .3, "Drivers and rods animate through production API");
                    visual.AdvanceRunningGear(Math.Tau * PacificModels.DriverRadius * .75);
                    check(rod.Position.DistanceTo(initial) < .001, "Running gear completes a continuous loop");
                    check(composition.Exhausts.Count == 1 && definition.Physics.PowerW > 0, "Pacific has traction and a stack outlet");
                }
                else check(definition.Physics.PowerW == 0 && model.FindChildren("tender-wheel-*", "Node3D", true, false).Count == 8, "Tender has eight wheels and no traction");

            }
            visual.Apply(PosedVehicleVisual.Straight(composition, Vector3.Zero, Vector3.Back));
            visuals.Add(visual);
            var builder = new TrainBuilder(); var train = builder.Create();
            builder.Add(train, composition);
            var network = new TrackNetwork(); var a = network.AddNode(Vector3.Zero, 1435); var b = network.AddNode(new(0, 0, 200), 1435);
            var edge = network.ConnectNodes(a, b)!;
            using var placement = new TrainPlacement(network);
            var poses = placement.Poses(train, new(edge.Id, .5));
            check(train.Items.Count == 1 && poses.Length == 1 && poses[0].Sections.Length == 2, "Adding the Pacific places engine and tender as one item");
            check(poses[0].Sections[0].Center.DistanceTo(poses[0].Sections[1].Center) >= (PacificModels.EngineLength + PacificModels.TenderLength) / 2 + .11f, "Rigid sections retain coupling clearance");
            visual.Apply(poses[0]);
            var outlet = visual.Exhausts().Single();
            var engineBody = visual.Vehicle.Model.GetNode<Node3D>("section_0/section_0_body");
            check(outlet.Position.DistanceTo(engineBody.GlobalTransform * new Vector3(0, 4.582f, PacificModels.StackZ)) < .001f, "Composition exhaust remains at engine stack");
            builder.Flip(train.Id, train.Items[0].Id);
            var reversed = placement.Poses(train, new(edge.Id, .5));
            check(reversed[0].Sections.Length == 2 && reversed[0].Sections[0].Basis.Z.Dot(poses[0].Sections[0].Basis.Z) < -.99f, "Flipping the item reverses both sections");
            builder.Flip(train.Id, train.Items[0].Id);
            TrainSample Arc(TrainPosition position, double distance)
            {
                double angle = (position.U - .5) * 2 + distance * position.Facing / 100;
                return new(new((float)(100 * (1 - Math.Cos(angle))), 0, (float)(100 * Math.Sin(angle))),
                    new Vector3((float)Math.Sin(angle), 0, (float)Math.Cos(angle)) * position.Facing,
                    position with { U = .5 + angle / 2 });
            }
            var curved = placement.Poses(train, new(edge.Id, .5), Arc)[0];
            visual.Apply(curved);
            check(curved.Sections[0].Basis.Z.Dot(curved.Sections[1].Basis.Z) < .999f, "Engine and tender follow curves independently");
            for (int i = 0; i < 2; i++)
            {
                var sectionBody = visual.Vehicle.Model.GetNode<Node3D>($"section_{i}/section_{i}_body");
                check(sectionBody.GlobalPosition.DistanceTo(curved.Sections[i].BodyCenter) < .001f, "Section geometry follows its independent track pose");
            }
            check(Math.Abs(composition.Physics.MassKg - composition.Sections.Sum(s => s.Definition.Physics.MassKg)) < .001, "Composition includes tender mass exactly once");
            visual.Apply(PosedVehicleVisual.Straight(composition, Vector3.Zero, Vector3.Back));
            instances.Sync(visuals); check(instances.BatchCount > 2, "Native running gear uses production instancing");
            for (int i = 0; i < 6; i++) await frame();
            if (DisplayServer.GetName() != "headless")
            {
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = parent.GetViewport().GetTexture().GetImage(); image.SavePng("res://.godot/pacific-review.png");
            }
        }
        finally { stage.Free(); }
    }
}
