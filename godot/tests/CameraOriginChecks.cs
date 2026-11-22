using Godot;
using System;
using System.Linq;

namespace TransportSandbox.Tests;

public static class CameraOriginChecks
{
    private static TrackVector Point(System.Text.Json.JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    public static void Parity(Action<bool, string> check)
    {
        double Ground(double x, double z) => 100 + Math.Sin(x * .003) * 40 + Math.Cos(z * .002) * 20;
        foreach (var sequence in JsonData.Read("res://tests/camera-fixtures.json").EnumerateArray())
        {
            var rig = new CameraRig();
            foreach (var action in sequence.EnumerateArray())
            {
                double Number(string key) => action.GetProperty(key).GetDouble();
                string kind = action.GetProperty("kind").GetString()!;
                switch (kind)
                {
                    case "reset":
                        rig.Reset(Number("x"), Number("z"), Ground);
                        break;
                    case "look":
                        rig.Look(Number("x"), Number("y"), Ground);
                        break;
                    case "pan":
                        rig.Pan(Number("x"), Number("y"), Ground);
                        break;
                    case "scroll":
                        rig.Scroll(Number("value"), Ground);
                        break;
                    case "mode":
                        rig.SetMode(action.GetProperty("free").GetBoolean(), Ground);
                        break;
                    case "update":
                        rig.Update(action.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToHashSet(), Number("dt"), Ground, (_, _) => !action.TryGetProperty("blocked", out var blocked) || !blocked.GetBoolean());
                        break;
                    case "follow":
                        rig.Follow(Point(action.GetProperty("point")), Ground);
                        break;
                    case "updateFollow":
                        rig.UpdateFollow(Point(action.GetProperty("point")), Ground);
                        break;
                }
                var expected = action.GetProperty("expected");
                check((rig.Position - Point(expected.GetProperty("position"))).Length < .00001 && (rig.Target - Point(expected.GetProperty("target"))).Length < .00001, "Camera " + kind + " position/target match browser at ordinary and distant coordinates");
                check(Math.Abs(rig.Yaw - expected.GetProperty("yaw").GetDouble()) < 1e-10 && Math.Abs(rig.Pitch - expected.GetProperty("pitch").GetDouble()) < 1e-10 && Math.Abs(rig.Distance - expected.GetProperty("distance").GetDouble()) < 1e-5 && Math.Abs(rig.Speed - expected.GetProperty("speed").GetDouble()) < 1e-10, "Camera angles, zoom and free speed match browser");
                check(rig.Free == expected.GetProperty("free").GetBoolean() && rig.Following == expected.GetProperty("following").GetBoolean(), "Camera mode and follow cancellation match browser");
            }
        }
        var space = new WorldSpace();
        var graph = new TrackNetwork { Space = space };
        space.Rebase(new(1000000000.125, 100, -1000000000.375));
        var a = graph.AddNode(new(.125f, 100, .375f));
        var b = graph.AddNode(new(.125f, 103, 300.375f));
        var edge = graph.ConnectNodes(a, b)!;
        var parallel = graph.AddParallel(a, 1);
        var before = a.WorldPosition;
        var parallelBefore = parallel.WorldPosition;
        var expectedLength = new TrackCurve(graph.WorldControls(edge)).Length;
        using var placement = new TrainPlacement(graph);
        for (int i = 0; i < 20; i++)
        {
            space.Rebase(new(i % 2 == 0 ? -1000000000 : 1000000000, 100, i % 2 == 0 ? 1000000000 : -1000000000));
            check(a.WorldPosition == before && parallel.WorldPosition == parallelBefore, "Rebasing preserves precise track and parallel-node world coordinates");
            check(Math.Abs(placement.Curve(edge).Length - expectedLength) < .00001, "Train route length stays stable when its world is rebased");
        }
        var grid = new WorldTrackGrid { Space = space };
        grid.SetSpacing(25);
        var snap = space.ToWorld(grid.Snap(new(7.25f, 103, -17.75f)));
        check(snap.X % 25 == 0 && snap.Z % 25 == 0, "World grid remains anchored to absolute axes after rebasing");
        grid.Free();
    }
    public static void TrainOrigin(Action<bool, string> check, TrainEditor editor, WorldCamera camera)
    {
        var space = editor.Network.Space;
        var parked = editor.Parked.First(p => p.Supported);
        var train = editor.Builder.Get(parked.Id);
        var position = parked.Position;
        var origin = parked.Poses[0].BodyCenter;
        var worldPoint = space.ToWorld(origin);
        var node = editor.Network.Nodes.Values.First();
        var nodeWorld = node.WorldPosition;
        bool running = train.Running;
        space.Rebase(new(1000000000, 200, -1000000000));
        space.Rebase(new(4096, 200, -2048));
        InstanceBounds(check, editor);
        check(node.WorldPosition == nodeWorld && parked.Position == position && train.Running == running, "Origin shifts preserve track graph, route position and train command");
        check((space.ToWorld(parked.Poses[0].BodyCenter) - worldPoint).Length < .001, "Returning a distant train to local coordinates retains its precise pose");
        check(parked.Visuals[0].GlobalPosition.DistanceTo(parked.Poses[0].Center) < .001, "Train visuals and instancing follow the rebased route");
        space.Rebase(new(0, 200, 0));
        InstanceBounds(check, editor);
        var visual = parked.Visuals[0];
        var beforeMigration = visual.Position;
        visual.Position += new Vector3(1024, 0, -1024);
        editor.Instances.Sync(editor.Parked.SelectMany(p => p.Visuals));
        InstanceBounds(check, editor);
        visual.Position = beforeMigration;
        editor.Instances.Sync(editor.Parked.SelectMany(p => p.Visuals));
        check(camera.Space == space && parked.Poses[0].BodyCenter.DistanceTo(origin) < .001, "Main camera and trains share one floating coordinate frame");
        NativeInput(check, camera);
    }
    private static void InstanceBounds(Action<bool, string> check, TrainEditor editor)
    {
        var batches = editor.Instances.GetChildren().OfType<MultiMeshInstance3D>().ToArray();
        check(batches.Length > 0, "Origin and cell migration retain rendered train batches");
        // The headless dummy renderer returns identity for every instance transform.
        // Validate native buffer/bounds readback in the real-renderer train suite.
        if (DisplayServer.GetName() == "headless") return;
        foreach (var batch in batches)
        {
            var multi = batch.Multimesh;
            var bounds = multi.CustomAabb.Grow(.01f);
            bool contains = true;
            for (int i = 0; i < multi.InstanceCount; i++)
            {
                var transformed = multi.GetInstanceTransform(i) * multi.Mesh.GetAabb();
                for (int corner = 0; corner < 8; corner++) contains &= bounds.HasPoint(transformed.GetEndpoint(corner));
            }
            check(contains, "Current train culling bounds contain every instance immediately after rebasing or changing cells");
        }
    }
    private static void NativeInput(Action<bool, string> check, WorldCamera camera)
    {
        camera.Enabled = true;
        camera.SetFreeMode(true);
        double speed = camera.Rig.Speed;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Factor = 1 });
        check(Math.Abs(camera.Rig.Speed - speed * Math.Exp(.2)) < .00001, "Native wheel input adjusts free-camera speed using browser sensitivity");
        var position = camera.Position;
        var rotation = camera.Rotation;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        if (DisplayServer.GetName() != "headless")
            check(Input.MouseMode == Input.MouseModeEnum.Captured, "Free-camera terrain clicks capture the mouse outside construction mode");
        camera._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        check(Input.MouseMode == Input.MouseModeEnum.Visible, "Escape releases captured mouse input");
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        camera.InputBlocked = true;
        check(Input.MouseMode == Input.MouseModeEnum.Visible, "Modal UI releases mouse capture");
        camera._UnhandledInput(new InputEventMouseMotion { Relative = new(100, 100) });
        check(camera.Position == position && camera.Rotation == rotation, "Blocked camera input preserves its pose");
        camera.InputBlocked = false;
        camera.BuildMode = () => true;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        check(Input.MouseMode == Input.MouseModeEnum.Visible, "Construction clicks never capture the mouse");
        camera.BuildMode = () => false;
        camera.SetFreeMode(false);
        check(camera.Distance >= 50 && camera.Distance <= 3500, "Returning to overlook uses browser zoom bounds");
        camera.Enabled = false;
    }
    public static void TerrainOrigin(Action<bool, string> check, Main main)
    {
        var space = main.Camera.Space;
        var eye = main.Camera.WorldPosition;
        var origin = space.Origin;
        var chunks = main.World.Chunks.ToDictionary(p => p.Key, p => p.Value);
        long jobs = main.World.SubmittedJobs;
        int pending = main.World.PendingEarthworks;
        var sample = space.ToWorld(main.Camera.Target);
        double height = main.Generator.HeightWorld(sample.X, sample.Z);
        space.Rebase(new(eye.X + 4096, eye.Y, eye.Z - 4096));
        foreach (var holder in chunks.Values.Take(1))
        {
            var material = (ShaderMaterial)holder.GetChild<MeshInstance3D>(0).MaterialOverride;
            check(material.GetShaderParameter("terrain_origin").AsVector2() == new Vector2((float)space.Origin.X, (float)space.Origin.Z), "Terrain shader receives the current absolute origin for stationary ground grain");
        }
        var water = main.World.GetNode<MeshInstance3D>("Water");
        check(water.Mesh is PlaneMesh { Size: var size } && size == new Vector2(32000, 32000), "Water retains the browser's 32 km extent");
        check(Math.Abs(water.Position.Y - main.Generator.SeaLevel) < .001, "Origin changes keep water at the world's sea level");
        check(main.Camera.WorldPosition == eye, "Rebasing preserves the camera's absolute pose");
        foreach (var (key, holder) in chunks)
            check((space.ToWorld(holder.Position) - new TrackVector(key.X * 512.0, 0, key.Y * 512.0)).Length < .001, "Loaded chunk transforms retain their absolute placement after rebasing");
        var local = space.ToLocal(sample);
        check(Math.Abs(main.Generator.Height(local.X, local.Z) - height) < .001, "Ground queries sample the same seeded world after origin changes");
        check(main.World.SubmittedJobs == jobs && main.World.PendingEarthworks == pending && chunks.All(p => main.World.Chunks[p.Key] == p.Value), "Origin changes preserve meshes and earthwork revisions without resubmitting terrain jobs");
        space.Rebase(origin);
    }
}
