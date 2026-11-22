using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

/// <summary>Native C# parity/integration runner. Launch tests/runner.tscn -- --suite=all.</summary>
public partial class TestRunner : Node
{
    private int _failures;
    private bool _finished;
    private readonly Dictionary<string, string> _args = new();
    private void Check(bool condition, string message)
    {
        if (condition)
            return;
        _failures++;
        GD.PushError("FAIL: " + message);
    }
    private static double Number(JsonElement element, string key) => element.GetProperty(key).GetDouble();
    private static int Integer(JsonElement element, string key) => element.GetProperty(key).GetInt32();
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Frames(int count = 4)
    {
        for (int i = 0; i < count; i++)
            await Frame();
    }
    public override async void _Ready()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            var parts = argument.TrimStart('-').Split('=', 2);
            if (parts.Length == 2)
                _args[parts[0]] = parts[1];
        }
        GetTree().CreateTimer(_args.GetValueOrDefault("suite") == "earthwork-save-performance" ? 150 : 45).Timeout += () => { if (!_finished) { GD.PushError("C# test/capture timed out"); GetTree().Quit(1); } };
        try
        {
            string suite = _args.GetValueOrDefault("suite", "all");
            switch (suite)
            {
                case "train-liveries":
                    await TrainLiveryChecks.Run(Check, Frame, this);
                    break;
                case "train-audio":
                    await TrainAudioChecks.Run(Check, Frame, this);
                    break;
                case "display-shortcuts":
                    await DisplayShortcutChecks.Run(Check, Frame, this);
                    break;
                case "detour-replan":
                    await DetourReplanChecks.Run(Check, Frame, this);
                    await Frames(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames();
                    break;
                case "earthwork-save-performance":
                    await EarthworkSavePerformance.Run(Check, Frame, this, _args.GetValueOrDefault("save", "res://.godot/earthwork-profile"));
                    await Frames(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames();
                    break;
                case "train-parallel":
                    TrainParallelChecks.Run(Check);
                    await TrainParallelChecks.Integration(Check, Frame, this);
                    await Frames(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames();
                    break;
                case "save-performance":
                    await SavePerformance.Run(Frame, this, _args.GetValueOrDefault("phase", "simulation"), _args.GetValueOrDefault("save", "res://.godot/stutter-profile"));
                    await Frames(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames();
                    break;
                case "station-traffic-save":
                    await StationTrafficChecks.Saved(Check, Frame, this, _args.GetValueOrDefault("save", "res://.godot/station-traffic-repro"));
                    break;
                case "signals":
                    TrainCollisionChecks.Run(Check);
                    StationTrafficChecks.Run(Check);
                    SignalChecks.Run(Check);
                    await SignalScopeChecks.Run(Check, Frame, this);
                    await SignalIntegrationChecks.Run(Check, Frame, this);
                    await ChainStationChecks.Run(Check, Frame, this);
                    await RailDetourChecks.Run(Check, Frame, this);
                    await Frames(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames();
                    break;
                case "chain-stations":
                    await ChainStationChecks.Run(Check, Frame, this);
                    await Frames(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames();
                    break;
                case "saves":
                    await SaveGameChecks.Run(Check, Frame, this);
                    await PauseMenuChecks.Run(Check, Frame, this);
                    await Frames(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames();
                    break;
                case "performance":
                    TerrainPerformance.Run();
                    break;
                case "tree-performance":
                    await TreeStreamingPerformance.Run(this, Frame);
                    break;
                case "visuals":
                    await WorldVisualChecks.Run(Check, Frame, this);
                    break;
                case "orders":
                    await RailPathfindingChecks.Run(Check);
                    await TrainOrderChecks.Run(Check, Frame, this);
                    await Frames();
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    await Frames();
                    break;
                case "routing":
                    await RailPathfindingChecks.Run(Check);
                    break;
                case "routing-performance":
                    await RailPathfindingChecks.Benchmark(Check);
                    break;
                case "corridor-performance":
                    await CorridorRoutingChecks.Benchmark(Check);
                    break;
                case "invalidation-performance":
                    await RoutingInvalidationChecks.Benchmark(Check);
                    break;
                case "all":
                    await TrainLiveryChecks.Run(Check, Frame, this);
                    await TrainAudioChecks.Run(Check, Frame, this);
                    await DisplayShortcutChecks.Run(Check, Frame, this);
                    TrainParallelChecks.Run(Check);
                    await TrainParallelChecks.Integration(Check, Frame, this);
                    TrainCollisionChecks.Run(Check);
                    StationTrafficChecks.Run(Check);
                    await SaveGameChecks.Run(Check, Frame, this);
                    await PauseMenuChecks.Run(Check, Frame, this);
                    SignalChecks.Run(Check);
                    await SignalScopeChecks.Run(Check, Frame, this);
                    await SignalIntegrationChecks.Run(Check, Frame, this);
                    await ChainStationChecks.Run(Check, Frame, this);
                    await RailDetourChecks.Run(Check, Frame, this);
                    Core();
                    await WorldVisualChecks.Run(Check, Frame, this);
                    await Menu();
                    await Terrain();
                    await Startup();
                    await TrainPlacementChecks.Run(Check, Frame, this);
                    await RailPathfindingChecks.Run(Check);
                    await TrainOrderChecks.Run(Check, Frame, this);
                    await Frames();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await Frames();
                    break;
                case "core":
                    Core();
                    break;
                case "menu":
                    await Menu();
                    break;
                case "terrain":
                    await Terrain();
                    break;
                case "startup":
                    await Startup();
                    break;
                case "hover":
                    GetTree().Root.Size = new(1280, 800);
                    await Frames();
                    await NodeHoverChecks.Run(Check, Frame, this);
                    break;
                case "platform":
                    GetTree().Root.Size = new(1280, 800);
                    await Frames();
                    await PlatformChecks.Run(Check, Frame, this);
                    break;
                case "grid":
                    GetTree().Root.Size = new(1280, 800);
                    await Frames();
                    await GridSpacingChecks.Run(Check, Frame, this);
                    break;
                case "train":
                    GetTree().Root.Size = new(1280, 800);
                    await Frames();
                    await TrainPlacementChecks.Run(Check, Frame, this);
                    await Frames();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await Frames();
                    break;
                case "capture":
                    await Capture();
                    break;
                case "benchmark":
                    await Benchmark();
                    break;
                default:
                    throw new ArgumentException("Unknown suite: " + suite);
            }
            _finished = true;
            GD.Print($"C# {suite}: {(_failures == 0 ? "PASS" : $"{_failures} failures")}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception error) { _finished = true; GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void Core()
    {
        CameraOriginChecks.Parity(Check);
        TreeChecks.Run(Check);
        ClearanceChecks.Run(Check);
        TrackGeometryChecks.Run(Check);
        NodeHoverChecks.ParallelSourceParity(Check);
        var fixtures = JsonData.Read("res://tests/terrain-fixtures.json");
        foreach (var reference in fixtures.GetProperty("cases").EnumerateArray())
        {
            var generator = new TerrainGenerator(JsonData.Settings(reference.GetProperty("settings")));
            Check(generator.Seed == reference.GetProperty("seedHash").GetUInt32(), "UTF-16 seed hash matches TypeScript");
            foreach (var sample in reference.GetProperty("samples").EnumerateArray())
                Check(Math.Abs(generator.Height(Number(sample, "x"), Number(sample, "z")) - Number(sample, "height")) < 0.000001, "Seeded height matches TypeScript");
            var camera = new WorldCamera();
            AddChild(camera);
            camera.ResetWorld(generator);
            var expected = reference.GetProperty("camera");
            Check(camera.Target.DistanceTo(JsonData.Vector(expected.GetProperty("target"))) < 0.001, "Startup camera target matches browser");
            Check(camera.Position.DistanceTo(JsonData.Vector(expected.GetProperty("position"))) < 0.001, "Startup camera orbit matches browser");
            camera.Free();
            generator.Backend.Dispose();
        }
        var network = new TrackNetwork();
        var a = network.AddNode(Vector3.Zero);
        var b = network.AddNode(new(0, 0, 100));
        var c = network.AddNode(new(50, 0, 200));
        var d = network.AddNode(new(-50, 0, 200));
        var ab = network.ConnectNodes(a, b)!;
        var bc = network.ConnectNodes(b, c, 1)!;
        var bd = network.ConnectNodes(b, d, 1)!;
        Check(!network.CanMerge(b.Id), "Junction cannot be merged");
        Check(network.Continuations(bc, -1).Count == 1, "Same-side branch is not a legal U-turn");
        var router = new RailRouter();
        router.Rebuild(network);
        Check(router.Route(ab.Id * 2 + 1, bc.Id).SequenceEqual(new[] { ab.Id * 2 + 1, bc.Id * 2 + 1 }), "Directed route crosses junction");
        Check(router.Route(bc.Id * 2, bd.Id).Length == 0, "Router rejects same-side U-turn");
        var cached = router.Route(ab.Id * 2 + 1, bc.Id);
        cached[0] = -999;
        Check(router.Route(ab.Id * 2 + 1, bc.Id)[0] == ab.Id * 2 + 1, "Route results do not mutate cache");
        var original = network.Controls(ab);
        var node = network.Split(ab, 0.35f)!;
        Check(node != null, "Split creates node");
        var split = network.Incident(node!.Id);
        for (int i = 0; i <= 20; i++)
        {
            float t = i / 20f;
            var p = t <= 0.35f ? network.Point(split[0], t / 0.35f) : network.Point(split[1], (t - 0.35f) / 0.65f);
            Check(p.DistanceTo(TrackNetwork.Bezier(original, t)) < 0.0001, "De Casteljau preserves curve");
        }
        Check(network.CanMerge(node.Id), "Two opposite connections can merge");
        var merged = network.Merge(node.Id) ?? throw new Exception("Merge failed");
        Check(!network.Nodes.ContainsKey(node.Id), "Merge removes only intermediate node");
        var narrow = network.AddNode(new(0, 0, -100), 914);
        Check(network.ConnectNodes(narrow, a) == null, "Gauge mismatch rejected");
        var parallel = network.AddParallel(a, 1);
        network.SetDirection(a, new(1, 0, 1));
        Check(Math.Abs((parallel.Position - a.Position).Dot(a.Axis)) < 0.0001, "Parallel alignment preserved");
        var end = network.AddNode(parallel.Position + parallel.Axis * 100);
        var parallelEdge = network.ConnectNodes(parallel, end)!;
        network.SetHandle(merged, a.Id, a.Position + a.Axis * merged.SideA * 23);
        Check(Math.Abs(parallelEdge.LengthA - 23) < 0.001, "Parallel handle lengths remain synchronized");
        var third = network.AddParallel(a, -1);
        int group = third.Group;
        network.DeleteNode(parallel.Id);
        Check(network.Groups.ContainsKey(group) && a.Group == group && third.Group == group, "Deleting group member preserves others");
        var station = new TrackNetwork();
        var s0 = station.AddNode(Vector3.Zero);
        var s1 = station.AddNode(new(0, 0, 100));
        var edge = station.ConnectNodes(s0, s1)!;
        Check(station.AddStation(edge, 1) && station.AddStation(edge, -1), "Both platform sides share station");
        Check(!station.CanExtend(s0, 1) && station.CanExtend(s0, -1), "Station-facing side rejects junction");
        var cars = new[] { new VehiclePhysics(120000, 3000000, 300000, 40), new VehiclePhysics(40000, 0, 0, 50) };
        foreach (var test in JsonData.Read("res://tests/physics-fixtures.json").EnumerateArray())
        {
            var result = TrainPhysics.Step(cars, Number(test, "speed"), test.GetProperty("started").GetBoolean(), Number(test, "grade"), test.GetProperty("ho").GetBoolean(), Number(test, "dt"), Number(test, "limit"));
            var expected = test.GetProperty("result");
            Check(Math.Abs(result.Speed - Number(expected, "speed")) < 1e-10 && Math.Abs(result.Distance - Number(expected, "distance")) < 1e-10 && Math.Abs(result.Effort - Number(expected, "effort")) < 1e-10, "Realistic and OpenLoco-style HO physics match browser fixtures");
        }
        Check(Math.Abs(TrainPhysics.CurveLimit(100, true) / TrainPhysics.CurveLimit(100, false) - 3) < 1e-10, "HO corner speed scaling preserved");
    }
    private async Task Menu()
    {
        foreach (var reference in JsonData.Read("res://tests/menu-fixtures.json").EnumerateArray())
        {
            var diorama = new MenuDiorama { Preset = Integer(reference, "preset") };
            AddChild(diorama);
            diorama.SetProcess(false);
            diorama.Smoke.SetProcess(false);
            Check(diorama.Composition.Vehicles.Select(item => item.Id).SequenceEqual(reference.GetProperty("cars").EnumerateArray().Select(e => e.GetString())), "Menu compositions match source");
            Check(diorama.Composition.Gauge == Number(reference, "gauge"), "Menu gauges match source");
            Check(Math.Abs(diorama.Train.Length - Number(reference, "length")) < 1e-6, "Menu coupler spacing matches source");
            Check(diorama.Track.Network == diorama.Network && diorama.Network.Edges.Count == 1, "Menu uses gameplay track graph and renderer");
            Check(diorama.Network.Edges.Values.Single().Station != 0, "Menu platform is a gameplay station");
            foreach (var vehicle in diorama.Train.Vehicles)
            {
                Check(vehicle.Scale == Vector3.One && vehicle.Model.Scale == Vector3.One, "Catalog vehicle dimensions are not rescaled");
                Check(vehicle.Model.SceneFilePath == vehicle.Definition.Models[diorama.Composition.Gauge], "Menu instantiates catalog models");
            }
            if (diorama.Composition.Id == "tgv-sud-est")
                Check(diorama.Train.Vehicles.Last().Reversed, "TGV rear power car faces outward");
            foreach (var camera in reference.GetProperty("cameras").EnumerateArray())
            {
                diorama.UpdateCamera(Number(camera, "time"), Number(camera, "aspect"));
                var q = camera.GetProperty("quaternion");
                var rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle());
                Check(diorama.Camera.Position.DistanceTo(JsonData.Vector(camera.GetProperty("position"))) < 0.00001, "Menu framing and drift match source");
                Check(diorama.Camera.Quaternion.AngleTo(rotation) < 0.001, "Menu look direction matches source");
            }
            Check(diorama.Camera.Fov == 38 && diorama.Camera.Far == 1800, "Menu lens matches source");
            for (int i = 0; i < 60; i++)
                diorama.Smoke.Advance(1.0 / 60);
            var smoke = reference.GetProperty("smoke");
            Check(diorama.Smoke.Particles.Count == smoke.GetArrayLength(), "Idle exhaust count matches source");
            for (int i = 0; i < Math.Min(smoke.GetArrayLength(), diorama.Smoke.Particles.Count); i++)
            {
                var particle = diorama.Smoke.Particles[i];
                var expected = smoke[i];
                Check(particle.Position.DistanceTo(JsonData.Vector(expected.GetProperty("position")) + Vector3.Up * TrackRenderer.RailHeadHeight) < 0.0001, "Idle exhaust movement matches source at the native railhead height");
                foreach (var (key, value) in new[] { ("life", particle.Life), ("radius", particle.Radius), ("density", particle.Density), ("shade", particle.Shade), ("growth", particle.Growth), ("phase", particle.Phase) })
                    Check(Math.Abs(value - Number(expected, key)) < 1e-6, "Exhaust " + key + " matches source");
            }
            diorama.Free();
        }
        // A new definition works without editing MenuDiorama or exporting a scene.
        var extra = new MenuDiorama
        {
            Preset = 0,
            Definitions = new[] {
            new ConsistDefinition("extra-ge", 1435, new[] { new ConsistItem("ge-809"), new ConsistItem("atsf-coach", true) }) }
        };
        AddChild(extra);
        extra.SetProcess(false);
        extra.Smoke.SetProcess(false);
        Check(extra.Train.Vehicles.Count == 2 && extra.Composition.Id == "extra-ge", "Additional menu consists use data-driven assembly");
        Check(extra.Smoke.Outlets.Count == 1, "Exhaust comes from actual vehicles");
        extra.Free();
        bool rejected = false;
        try
        {
            _ = ConsistVisual.Create(VehicleCatalog.Load(), new("bad", 914, new[] { new ConsistItem("atsf-f7a") }));
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Menu consist rejects incompatible gauges");
        GetTree().Root.Size = new(1280, 800);
        const string preferences = "res://.godot/menu-test-preferences.cfg";
        if (FileAccess.FileExists(preferences))
            DirAccess.RemoveAbsolute(preferences);
        var ui = new MenuUI { SettingsFile = preferences };
        AddChild(ui);
        Check(ui.ChosenSettings().Physics == "ho", "Recommended uses HO");
        ui.OpenSetup();
        Check(ui.Dialog.Visible && ui.Windows.HasModal, "Start opens modal");
        Check(!ui.Dialog.Collapsible && !ui.Dialog.Minimizable, "New game hides collapse and minimize controls");
        ui.Dialog.Minimize();
        ui.Dialog.ToggleCollapsed();
        Check(ui.Dialog.Visible && !ui.Dialog.IsMinimized && !ui.Dialog.IsCollapsed, "Disabled window actions are guarded");
        ui.Custom.ButtonPressed = true;
        ui.SeedInput.Text = "  a reproducible world  ";
        ui.Relief.Value = 2400;
        ui.Landform.Select(2);
        ui.Physics.Select(1);
        Check(ui.Advanced.Visible && ui.ChosenSettings() == new WorldSettings("a reproducible world", 2400, 2, 0, "realistic"), "Advanced settings preserved");
        ui.Recommended.ButtonPressed = true;
        Check(!ui.Advanced.Visible && ui.ChosenSettings() == new WorldSettings(), "Recommended ignores advanced values");
        ui.Custom.ButtonPressed = true;
        await Frames();
        float bottom = ui.Launch.GlobalPosition.Y + ui.Launch.Size.Y;
        ui.Recommended.ButtonPressed = true;
        await Frames();
        Check(Math.Abs(ui.Launch.GlobalPosition.Y + ui.Launch.Size.Y - bottom) < 1, "Start footer stays pinned");
        ui.Custom.ButtonPressed = true;
        ui.CloseSetup();
        ui.OpenSetup();
        Check(ui.SeedInput.Text == "  a reproducible world  ", "Closing preserves unsubmitted values");
        ui.SeedInput.Text = "  ";
        Check(ui.ChosenSettings().Seed == "the-long-way-home", "Blank seed fallback preserved");
        ui.SeedInput.Text = "saved-seed";
        ui.SavePreferences(true);
        ui.Free();
        var restored = new MenuUI { SettingsFile = preferences };
        AddChild(restored);
        Check(restored.SeedInput.Text == "saved-seed" && restored.Relief.Value == 2400, "World options persist");
        Check(restored.Recommended.ButtonPressed, "New launch defaults recommended");
        restored.Free();
    }
    private async Task Terrain()
    {
        EarthworkInvalidationChecks.Run(Check);
        var fixture = JsonData.Read("res://tests/terrain-chunk-fixtures.json");
        var generator = new TerrainGenerator(JsonData.Settings(fixture.GetProperty("settings")));
        var pending = new Dictionary<Vector2I, JsonElement>();
        foreach (var reference in fixture.GetProperty("chunks").EnumerateArray())
        {
            var key = new Vector2I(Integer(reference, "x"), Integer(reference, "z"));
            pending[key] = reference;
            generator.Backend.Request(key, Integer(reference, "segments"));
        }
        var timer = Stopwatch.StartNew();
        while (pending.Count != 0 && timer.Elapsed.TotalSeconds < 20)
        {
            foreach (var data in generator.Backend.Poll())
            {
                Check(!data.ContainsKey("error"), "Parallel chunk succeeds");
                if (data.ContainsKey("error"))
                    continue;
                var key = data["key"].AsVector2I();
                var reference = pending[key];
                pending.Remove(key);
                var arrays = data["arrays"].AsGodotArray();
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                Check(vertices.Length == Integer(reference, "vertexCount") && indices.Length == Integer(reference, "indexCount"), "Chunk buffer counts match browser");
                if (Integer(reference, "segments") < 32)
                    Check(data["trees"].AsFloat32Array().Length == 0, "Distant tiles skip vegetation");
                else
                {
                    var vegetation = JsonData.Read("res://tests/vegetation-fixtures.json").GetProperty("cases");
                    foreach (var sample in vegetation.EnumerateArray())
                    {
                        if (Integer(sample, "x") != key.X || Integer(sample, "z") != key.Y ||
                            sample.GetProperty("settings").GetProperty("seed").GetString() != generator.SeedText)
                            continue;
                        var actual = data["trees"].AsFloat32Array();
                        var expected = sample.GetProperty("trees");
                        bool identical = actual.Length == expected.GetArrayLength();
                        for (int i = 0; identical && i < actual.Length; i++)
                            identical = actual[i] == expected[i].GetSingle();
                        Check(identical, "Parallel chunk uploads preserve original natural-tree data at both near LODs");
                    }
                }
                foreach (var triangle in reference.GetProperty("triangles").EnumerateArray())
                {
                    int cursor = Integer(triangle, "index") * 3;
                    var source = triangle.GetProperty("vertices");
                    Check(indices[cursor] == source[0].GetInt32() && indices[cursor + 1] == source[2].GetInt32() && indices[cursor + 2] == source[1].GetInt32(), "Topology matches with Godot winding");
                }
                foreach (var sample in reference.GetProperty("samples").EnumerateArray())
                {
                    int index = Integer(sample, "index");
                    Check(vertices[index].DistanceTo(JsonData.Vector(sample.GetProperty("position"))) < 0.0001, "Chunk heights/skirt match browser");
                    Check(normals[index].DistanceTo(JsonData.Vector(sample.GetProperty("normal"))) < 0.00001, "Chunk normals match browser");
                    var color = colors[index].SrgbToLinear();
                    var expected = sample.GetProperty("color");
                    Check(Math.Abs(color.R - expected[0].GetDouble()) < 1e-6 && Math.Abs(color.G - expected[1].GetDouble()) < 1e-6 && Math.Abs(color.B - expected[2].GetDouble()) < 1e-6, "Terrain colors match browser");
                }
            }
            await ToSignal(GetTree().CreateTimer(0.001), SceneTreeTimer.SignalName.Timeout);
        }
        Check(pending.Count == 0, "All parallel chunks complete");
        generator.Backend.Shutdown();
        generator.Backend.Dispose();
        generator = new(JsonData.Settings(fixture.GetProperty("settings")));
        var c = fixture.GetProperty("camera");
        var camera = new Camera3D { Position = new((float)Number(c, "x"), (float)Number(c, "y"), (float)Number(c, "z")) };
        AddChild(camera);
        var world = new TerrainWorld { Generator = generator, Camera = camera };
        AddChild(world);
        world.SetProcess(false);
        world._Process(0);
        var tiles = fixture.GetProperty("tiles");
        Check(world.Wanted.Count == tiles.GetArrayLength(), "Browser view radius preserved");
        foreach (var tile in tiles.EnumerateArray())
        {
            var key = new Vector2I(Integer(tile, "x"), Integer(tile, "z"));
            Check(world.Wanted.TryGetValue(key, out var actual) && actual.Segments == Integer(tile, "segments"), "Altitude-aware LOD matches browser");
        }
        var ordered = world.Wanted.Keys.ToArray();
        for (int i = 0; i < 9; i++)
            Check(ordered[i] == new Vector2I(Integer(tiles[i], "x"), Integer(tiles[i], "z")), "Nearby work prioritized like browser");
        world.Free();
        camera.Free();
        await EarthworkMeshChecks.Run(Check, Frame);
        await TerrainCacheChecks.Run(Check, Frame, this);
        await TreeStreamingChecks.Run(Check, Frame, this);
    }
    private async Task Startup()
    {
        GetTree().Root.Size = new(1280, 800);
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.HudSettingsFile = "res://.godot/hud-test-preferences.cfg";
        if (FileAccess.FileExists(main.HudSettingsFile))
            DirAccess.RemoveAbsolute(main.HudSettingsFile);
        main.MenuSettingsFile = "res://.godot/startup-test-preferences.cfg";
        AddChild(main);
        await Frame();
        Check(main.Catalog.GetArrayLength() == 10, "All catalog definitions present");
        foreach (var definition in main.Catalog.EnumerateArray())
            foreach (var model in definition.GetProperty("models").EnumerateObject())
            {
                var vehicle = GD.Load<PackedScene>(model.Value.GetString()!).Instantiate();
                Check(vehicle is Node3D, "Vehicle GLB imports");
                vehicle.Free();
            }
        main.ShowSetup();
        main.MenuUI.SeedInput.Text = "native-startup-check";
        main.MenuUI.Relief.Value = 800;
        main.MenuUI.Physics.Select(1);
        main.StartWorld(true);
        Check(main.Camera.Enabled && main.Generator.SeedText == "native-startup-check", "Advanced world starts");
        Check(main.World.GetMeta("physics_mode").AsString() == "realistic", "Physics option preserved");
        Check(main.Camera.Target.X == 600 && main.Camera.Target.Z == 900 && main.Camera.Distance == 1100 && main.Camera.Fov == 65, "Browser startup framing preserved");
        await Frames();
        await CheckHUD(main);
        var timer = Stopwatch.StartNew();
        while (main.World.Chunks.Count == 0 && timer.Elapsed.TotalSeconds < 20)
            await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        Check(main.World.Chunks.Count > 0, "Workers generate scene chunks");
        main.World.ShowContours(true);
        foreach (var chunk in main.World.Chunks.Values)
            if (chunk.GetNodeOrNull<TreeChunk>("Trees") is { } trees)
                Check(!trees.Visible, "Contours hide trees");
        main.World.ShowContours(false);
        await CheckClearanceLifecycle(main);
        // Exercise the converted editor/controller and renderer, not only the graph model.
        var editor = main.TrackEditor;
        editor.SetEnabled(true);
        var p = main.Camera.Target + Vector3.Up;
        Check(editor.Place(p) && editor.Place(p + new Vector3(0, 0, 100)), "Continuous track placement works");
        Check(main.TrackNetwork.Edges.Count == 1 && editor.Source != 0, "Second click connects and continues");
        editor.Cancel();
        Check(editor.Source == 0, "Cancel ends construction");
        var edge = main.TrackNetwork.Edges.Values.First();
        Check(main.TrackNetwork.AddStation(edge, 1), "Station placement works");
        await Frames();
        Check(main.TrackRenderer.Holders.Count == 1, "Track renderer rebuilds");
        editor.BeginFrom(edge.B, 1);
        editor.Parallel(edge.A, 1);
        Check(main.TrackNetwork.Edges.Count == 2, "Parallel action connects during construction");
        editor.Cancel();
        var distantA = main.TrackNetwork.AddNode(p + new Vector3(300, 0, 0));
        var distantB = main.TrackNetwork.AddNode(p + new Vector3(300, 0, 100));
        var independent = main.TrackNetwork.ConnectNodes(distantA, distantB)!;
        main.TrackRenderer._Process(0);
        var preserved = main.TrackRenderer.Holders[independent.Id];
        main.TrackRenderer.Selected = edge.A;
        main.TrackRenderer.Dragging = true;
        main.TrackNetwork.MoveNode(main.TrackNetwork.Nodes[edge.A], p + new Vector3(5, 0, 0));
        main.TrackRenderer._Process(0);
        var edited = main.TrackRenderer.Holders[edge.Id].Geometry;
        Check(edited.Sleepers.Count == 0 && edited.Surfaces.All(s => s.Name != "Ballast"), "Dragging uses rails-only geometry for affected tracks");
        Check(ReferenceEquals(preserved, main.TrackRenderer.Holders[independent.Id]) && preserved.Geometry.Sleepers.Count > 0, "Unchanged tracks retain their meshes and sleepers during dragging");
        main.TrackRenderer.Dragging = false;
        main.TrackRenderer.Dirty = true;
        main.TrackRenderer._Process(0);
        Check(main.TrackRenderer.Holders[edge.Id].Geometry.Sleepers.Count > 0, "Ending a drag restores sleepers");
        var restoredTrack = main.TrackRenderer.Holders[edge.Id];
        main.Camera.Position += new Vector3(500, 0, 0);
        main.TrackRenderer._Process(0);
        Check(ReferenceEquals(restoredTrack, main.TrackRenderer.Holders[edge.Id]), "Camera movement updates sleeper draw counts without rebuilding track meshes");
        main.TrackRenderer.ShowPreview(main.TrackNetwork.Controls(edge), 914);
        var preview = main.TrackRenderer.Preview.GetChild<TrackVisual>(0);
        Check(preview.Geometry.Gauge == 914 && preview.Geometry.Sleepers.Count == 0 && preview.Geometry.Surfaces.Count == 3, "Placement preview uses matching swept rails and ballast for its gauge");
        editor.SetEnabled(false);
        main.Free();
        await Frame();
        await NodeHoverChecks.Run(Check, Frame, this);
        await PlatformChecks.Run(Check, Frame, this);
        await GridSpacingChecks.Run(Check, Frame, this);
    }
    private async Task CheckClearanceLifecycle(Main main)
    {
        async Task Settle()
        {
            var timeout = Stopwatch.StartNew();
            while ((main.TreeClearance.IsPending || main.World.PendingEarthworks > 0 || main.World.ActiveJobs > 0) && timeout.Elapsed.TotalSeconds < 15)
                await ToSignal(GetTree().CreateTimer(.025), SceneTreeTimer.SignalName.Timeout);
            Check(!main.TreeClearance.IsPending && main.World.PendingEarthworks == 0, "Earthwork workers apply latest track revision and terrain meshes");
        }
        await Settle();
        var holder = main.World.Chunks.Values.FirstOrDefault(c => c.GetNodeOrNull<TreeChunk>("Trees") is { } t && t.Batches.Count > 0);
        Check(holder != null, "Streaming produces natural trees for clearance integration");
        if (holder == null)
            return;
        var key = main.World.Chunks.First(pair => pair.Value == holder).Key;
        Node3D Current() => main.World.Chunks[key];
        int Count() => Current().GetNodeOrNull<TreeChunk>("Trees")?.Batches.Sum(b => b.Crown.Multimesh.InstanceCount) ?? 0;
        int before = Count();
        var originalMesh = holder.GetChild<MeshInstance3D>(0).Mesh;
        var tree = holder.GetNode<TreeChunk>("Trees").Batches[0].TrunkTransforms[0].Origin + holder.Position + Vector3.Up * .8f;
        var a = main.TrackNetwork.AddNode(tree + new Vector3(-30, 25, 0));
        var b = main.TrackNetwork.AddNode(tree + new Vector3(30, 25, 0));
        main.TrackNetwork.ConnectNodes(a, b);
        main.World.ShowContours(true);
        await Settle();
        Check(Count() < before, "Building track clears trees around its earthwork shoulders");
        var editedMesh = Current().GetChild<MeshInstance3D>(0).Mesh;
        Check(!ReferenceEquals(originalMesh, editedMesh), "Track edits replace affected terrain meshes");
        var terrainVertices = editedMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Check(terrainVertices.Any(v =>
        {
            var point = v + Current().Position;
            return new Vector2(point.X - tree.X, point.Z - tree.Z).LengthSquared() < 9 &&
                point.Y > main.Generator.Sampler.Height(main.Camera.Space.ToWorld(point).X, main.Camera.Space.ToWorld(point).Z) + 10;
        }), "Rendered terrain contains the raised track formation");
        Check(Current().GetNodeOrNull<TreeChunk>("Trees") is not { Visible: true }, "Clearance rebuild preserves contour tree hiding");
        main.World.ShowContours(false);
        main.TrackNetwork.MoveNode(a, a.Position + new Vector3(10000, 0, 0));
        main.TrackNetwork.MoveNode(b, b.Position + new Vector3(10000, 0, 0));
        await Settle();
        Check(Count() == before, "Moving track restores trees at its previous position");
        main.TrackNetwork.MoveNode(a, tree + new Vector3(-30, 25, 0));
        main.TrackNetwork.MoveNode(b, tree + new Vector3(30, 25, 0));
        await Settle();
        Check(Count() < before, "Moving track back reapplies clearance");
        main.TrackNetwork.DeleteNode(a.Id);
        main.TrackNetwork.DeleteNode(b.Id);
        await Settle();
        Check(Count() == before, "Deleting track restores original natural trees");
        CameraOriginChecks.TerrainOrigin(Check, main);
        Check(Math.Abs(main.Generator.Height(tree.X, tree.Z) - main.Generator.Sampler.Height(main.Camera.Space.ToWorld(tree).X, main.Camera.Space.ToWorld(tree).Z)) < 1e-8,
            "Deleting track restores natural height queries");
        Check(Current().GetChild<MeshInstance3D>(0).Mesh.GetSurfaceCount() == 1 &&
            Current().GetChild<MeshInstance3D>(0).Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length == originalMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length,
            "Deleting track restores the natural terrain tessellation");
    }
    private async Task CheckHUD(Main main)
    {
        var hud = main.HUD;
        var editor = main.TrackEditor;
        var manager = main.GameWindows;
        Check(!hud.VehiclesButton.Disabled && !hud.TrainsButton.Disabled, "Ported vehicle catalog and fleet actions are enabled");
        Check(!hud.DebugPanel.Visible, "Debug information starts hidden");
        float width = GetTree().Root.Size.X, height = GetTree().Root.Size.Y;
        Check(Math.Abs(hud.ActionBar.Position.X + hud.ActionBar.Size.X / 2 - width / 2) < 1, "Action bar is centered horizontally");
        Check(Math.Abs(height - hud.ActionBar.Position.Y - hud.ActionBar.Size.Y - 46) < 1, "Action bar keeps the browser bottom offset");
        hud.BuildButton.ButtonPressed = true;
        Check(editor.Enabled && editor.Toolbar.Visible && manager.Active == editor.Toolbar, "Build track opens and activates its reusable window");
        editor.Toolbar.Close();
        Check(!editor.Enabled && !hud.BuildButton.ButtonPressed, "Closing build window synchronizes its action button");
        hud.BuildButton.ButtonPressed = true;
        editor.Toolbar.Minimize();
        Check(editor.Toolbar.IsMinimized && !editor.Enabled && manager.Taskbar.GetChildCount() == 1, "Minimizing a build window suspends the tool and creates a taskbar button");
        ((Button)manager.Taskbar.GetChild(0)).EmitSignal(Button.SignalName.Pressed);
        Check(editor.Toolbar.Visible && editor.Enabled && hud.BuildButton.ButtonPressed, "Taskbar restores the window and tool");
        editor.Toolbar.ToggleCollapsed();
        Check(editor.Toolbar.IsCollapsed && editor.Toolbar.Size.Y == 34, "Windows roll up to their title bar");
        editor.Toolbar.ToggleCollapsed();
        editor.Toolbar.SetBounds(new(-999, -999, 5000, 5000));
        Check(editor.Toolbar.Position.X >= 8 && editor.Toolbar.Position.Y >= 8 && editor.Toolbar.Size.X <= width - 16, "Window resizing is constrained to the viewport");
        editor.Toolbar.SetBounds(new(24, 85, 330, 460));
        hud.ViewButton.EmitSignal(Button.SignalName.Pressed);
        Check(manager.Active == hud.ViewWindow && hud.ViewWindow.Visible, "View opens a separate stacked window");
        hud.Contours.ButtonPressed = true;
        hud.ContourInterval.Select(2);
        hud.ContourInterval.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        Check(hud.ContourLegend.Text.Contains("100 m"), "View settings update contour interval legend");
        hud.Contours.ButtonPressed = false;
        hud.OpenSettings();
        Check(manager.HasModal && main.Camera.InputBlocked, "Modal settings block world camera input");
        hud.SettingsWindow.Close();
        Check(!manager.HasModal && !main.Camera.InputBlocked, "Closing settings releases camera input");
        string audioFile = main.SoundSettings.FilePath;
        main.SoundSettings.FilePath = "res://.godot/hud-audio-check.cfg";
        float oldMaster = main.SoundSettings.MasterVolume;
        bool oldMute = main.SoundSettings.Muted;
        hud.OpenSoundSettings();
        hud.MasterVolume.Value = 37;
        hud.MuteSound.ButtonPressed = true;
        var soundReloaded = new SoundSettings { FilePath = main.SoundSettings.FilePath };
        soundReloaded.Load();
        Check(Math.Abs(main.Trains.Audio.MasterVolume - .37f) < .001 && main.Trains.Audio.Settings.Muted,
            "Sound menu changes the live game mixer");
        Check(Math.Abs(soundReloaded.MasterVolume - .37f) < .001 && soundReloaded.Muted, "Sound preferences persist across sessions");
        main.SoundSettings.MasterVolume = oldMaster; main.SoundSettings.Muted = oldMute;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(main.SoundSettings.FilePath));
        main.SoundSettings.FilePath = audioFile;
        hud.SettingsWindow.Close();
        bool previous = main.Camera.FreeMode;
        hud.CameraButton.EmitSignal(Button.SignalName.Pressed);
        Check(main.Camera.FreeMode != previous && hud.CameraMode.Selected == 1, "Camera button and settings stay synchronized");
        main.Camera.SetFreeMode(false);
        hud._UnhandledInput(new InputEventKey { Keycode = Key.F3, Pressed = true });
        Check(hud.DebugPanel.Visible, "F3 shows debug information");
        hud._UnhandledInput(new InputEventKey { Keycode = Key.F3, Pressed = true });
        Check(!hud.DebugPanel.Visible, "F3 hides debug information");
        hud.ViewWindow.Close();
        editor.SetEnabled(false);
        GetTree().Root.Size = new(400, 700);
        await Frames();
        Check(hud.ActionBar.GetGlobalRect().Position.X >= 0 && hud.ActionBar.GetGlobalRect().End.X <= 400, "Action bar fits a narrow viewport");
        GetTree().Root.Size = new((int)width, (int)height);
        await Frames();
        Check(hud.BuildButton.Text == "" && hud.BuildButton.IconAlignment == HorizontalAlignment.Center && hud.BuildButton.TooltipText == "Build track" && hud.SettingsButton.Text == "Settings", "HUD keeps the build action icon-only after resizing back to desktop");
    }
    private async Task Capture()
    {
        int preset = int.Parse(_args.GetValueOrDefault("preset", "2"));
        var main = new Main { MenuPresetOverride = preset, MenuSettingsFile = "res://.godot/menu-review/preferences.cfg" };
        AddChild(main);
        main.MenuScene.SetProcess(false);
        main.MenuScene.Smoke.SetProcess(false);
        var root = GetTree().Root;
        main.MenuScene.UpdateCamera(0, root.Size.X / (double)root.Size.Y);
        string state = _args.GetValueOrDefault("state", "menu");
        bool gameplay = new[] { "game", "build", "view", "settings" }.Contains(state);
        if (gameplay)
        {
            main.HudSettingsFile = "res://.godot/menu-review/hud-preferences.cfg";
            if (FileAccess.FileExists(main.HudSettingsFile))
                DirAccess.RemoveAbsolute(main.HudSettingsFile);
            main.StartWorld(false);
            main.Camera.SetProcess(false);
            var timer = Stopwatch.StartNew();
            while ((main.World.Wanted.Count == 0 || main.World.Chunks.Count < main.World.Wanted.Count) && timer.Elapsed.TotalSeconds < 15)
                await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
            if (state == "build")
                main.HUD.BuildButton.ButtonPressed = true;
            if (state == "view")
                main.HUD.ViewWindow.Open();
            if (state == "settings")
                main.HUD.OpenSettings();
            if (_args.GetValueOrDefault("hover", "") is "node" or "segment" or "platform" or "station" or "grid")
            {
                var point = main.Camera.Target + Vector3.Up * 2;
                if (_args["hover"] is "platform" or "station" or "grid")
                    for (int z = -65; z <= 65; z += 5)
                        for (int x = -6; x <= 6; x += 3)
                            point.Y = Math.Max(point.Y, (float)main.Generator.Sampler.Height(point.X + x, point.Z + z) + 3);
                main.Camera.Position = point + new Vector3(105, 95, 150);
                main.Camera.LookAt(point, Vector3.Up);
                var a = main.TrackNetwork.AddNode(point - Vector3.Back * 65);
                var middle = main.TrackNetwork.AddNode(point);
                var b = main.TrackNetwork.AddNode(point + Vector3.Back * 65);
                main.TrackNetwork.ConnectNodes(a, middle);
                main.TrackNetwork.ConnectNodes(middle, b);
                main.TrackNetwork.AddParallel(middle, 1);
                main.TrackRenderer._Process(0);
                main.TrackEditor.SetEnabled(true);
                main.TrackEditor.SetProcess(false);
                var timeout = Stopwatch.StartNew();
                while ((main.TreeClearance.IsPending || main.World.PendingEarthworks > 0) && timeout.Elapsed.TotalSeconds < 15)
                    await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
                var hoverPoint = _args["hover"] == "segment" ? new TrackCurve(main.TrackNetwork.Controls(main.TrackNetwork.Edges.Values.First())).PointAt(.5).Float() : point;
                main.TrackEditor.RefreshHover(main.Camera.UnprojectPosition(hoverPoint) + TrackNodeHoverMenu.ScreenAxis(main.Camera, point, middle.Axis) * .25f);
                if (_args["hover"] == "grid")
                {
                    main.TrackRenderer.Selected = middle.Id;
                    main.TrackRenderer.UpdateMarkers();
                    main.TrackEditor.SetGridSpacing(5);
                    main.TrackEditor.GridOverlay.ShowAt(point, true);
                    main.TrackEditor.NodeMenu.Hide();
                    main.TrackEditor.SegmentMenu.Hide();
                }
                if (_args["hover"] is "platform" or "station")
                {
                    main.TrackEditor.SetMode("station");
                    var edge = main.TrackNetwork.Edges.Values.First();
                    var platformPoint = new TrackCurve(main.TrackNetwork.Controls(edge)).PointAt(.5).Float() + TrackNetwork.Right(main.TrackNetwork.Tangent(edge, .5f)) * 3;
                    main.TrackEditor.RefreshPlatformHover(main.Camera.UnprojectPosition(platformPoint));
                    if (_args["hover"] == "station")
                    {
                        main.TrackNetwork.AddStation(edge, 1);
                        main.TrackRenderer._Process(0);
                        main.TrackEditor.Stations.Open(edge.Station);
                    }
                }
            }
            if (_args.GetValueOrDefault("hover", "").StartsWith("train-"))
            {
                var point = main.Camera.Target;
                for (int z = -200; z <= 200; z += 10)
                    for (int x = -6; x <= 6; x += 3)
                        point.Y = Math.Max(point.Y, (float)main.Generator.Sampler.Height(point.X + x, point.Z + z) + 3);
                main.Camera.Position = point + new Vector3(80, 45, 100);
                main.Camera.LookAt(point, Vector3.Up);
                var a = main.TrackNetwork.AddNode(point - Vector3.Back * 200);
                var b = main.TrackNetwork.AddNode(point + Vector3.Back * 200);
                var edge = main.TrackNetwork.ConnectNodes(a, b)!;
                main.TrackNetwork.AddStation(edge, 1);
                main.TrackRenderer._Process(0);
                main.TrainUI.CatalogClick("atsf-f7a");
                main.TrainUI.CatalogClick("atsf-coach");
                main.TrainUI.CatalogClick("atsf-pleasure-dome");
                int id = main.Trains.Builder.Selected!.Id;
                main.TrackEditor.SetEnabled(false);
                main.Trains.StartPlacement(id);
                main.Trains.SetProcess(false);
                main.Trains.RefreshPreview(main.Camera.UnprojectPosition(point));
                if (_args["hover"] != "train-preview")
                    main.Trains.CommitPreview();
                main.TrainUI.OpenTrain(id, _args["hover"] is not ("train-viewport" or "train-moving"));
                if (_args["hover"] == "train-preview")
                    main.Trains.StartPlacement(id);
                main.TrainUI.Views[id].Window.SetBounds(new(870, 100, 380, 530));
                if (_args["hover"] == "train-orders")
                {
                    main.TrainUI.AddOrder(id, TrainOrderKind.Station, edge.Station);
                    main.TrainUI.AddOrder(id, TrainOrderKind.Wait, seconds: 30);
                    main.TrainUI.AddOrder(id, TrainOrderKind.Waypoint, b.Id);
                    main.TrainUI.OpenRoutes(id);
                }
                if (_args["hover"] == "train-edit")
                {
                    main.TrainUI.CatalogWindow.Open();
                    await Frames(2);
                    main.TrainUI.CatalogButtons["atsf-f7a"].GrabFocus();
                }
                if (_args["hover"] == "train-moving")
                {
                    main.Trains.ToggleRunning(id);
                    for (int i = 0; i < 100; i++) main.Trains.AdvanceMovement(.1);
                }
                main.TrainUI._Process(.2);
                main.TrainUI.SetProcess(_args["hover"] is "train-edit" or "train-viewport" or "train-moving" or "train-orders");
                if (_args["hover"] == "train-preview")
                    main.Trains.RefreshPreview(main.Camera.UnprojectPosition(point));
                var timeout = Stopwatch.StartNew();
                while ((main.TreeClearance.IsPending || main.World.PendingEarthworks > 0) && timeout.Elapsed.TotalSeconds < 15)
                    await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
            }
        }
        else if (state != "menu")
            main.ShowSetup();
        if (state == "advanced")
            main.MenuUI.Custom.ButtonPressed = true;
        for (int i = 0; i < 4; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string prefix = $"res://.godot/menu-review/godot-{preset}-{root.Size.X}x{root.Size.Y}-{state}";
        using var image = root.GetTexture().GetImage();
        image.SavePng(prefix + ".png");
        object Rect(Control control) => new
        {
            x = control.Position.X,
            y = control.Position.Y,
            width = control.Size.X,
            height = control.Size.Y
        };
        using (var file = FileAccess.Open(prefix + ".json", FileAccess.ModeFlags.Write))
            file.StoreString(JsonSerializer.Serialize(new
            {
                title = Rect(main.MenuUI.Title),
                start = Rect(main.MenuUI.StartButton),
                actions = gameplay ? Rect(main.HUD.ActionBar) : null
            }));
        GD.Print("Captured " + prefix + ".png");
        main.Free();
        await Frame();
    }
    private async Task Benchmark()
    {
        var generator = new TerrainGenerator();
        double sum = 0;
        for (int run = 0; run < 4; run++)
        {
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
                sum += generator.Height(600 + i % 100 * 8, 900 + i / 100 * 8);
            GD.Print("C# 10000 heights ms: ", timer.Elapsed.TotalMilliseconds);
        }
        Check(Math.Abs(sum - 11039133.493746432) < 0.0001, "Benchmark checksum matches original implementation");
        int completed = 0, submitted = 0, limit = generator.Backend.WorkerLimit();
        double first = -1;
        var clock = Stopwatch.StartNew();
        while (completed < 25)
        {
            while (submitted < 25 && submitted - completed < limit)
            {
                generator.Backend.Request(new(submitted % 5, submitted / 5), 64);
                submitted++;
            }
            foreach (var data in generator.Backend.Poll())
            {
                Check(!data.ContainsKey("error"), "Benchmark worker succeeds");
                completed++;
                if (first < 0)
                    first = clock.Elapsed.TotalMilliseconds;
            }
            await ToSignal(GetTree().CreateTimer(0.001), SceneTreeTimer.SignalName.Timeout);
        }
        GD.Print($"Parallel chunks: workers={limit} first_ms={first:F2} 25_chunks_ms={clock.Elapsed.TotalMilliseconds:F2}");
        generator.Backend.Shutdown();
        generator.Backend.Dispose();
    }
}
