using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class SaveGameChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        string root = ProjectSettings.GlobalizePath("res://.godot/save-checks/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string first = WorldSaveStore.CreateWorldDirectory(root, "São Paulo Railway");
        string second = WorldSaveStore.CreateWorldDirectory(root, "São Paulo Railway");
        string third = WorldSaveStore.CreateWorldDirectory(root, "São Paulo Railway");
        check(Path.GetFileName(first) == "sao-paulo-railway" && Path.GetFileName(second) == "sao-paulo-railway_"
            && Path.GetFileName(third) == "sao-paulo-railway__", "World folders use safe slugs and repeated underscore collision suffixes");
        check(WorldSaveStore.Slug("../CON") == "world-con" && WorldSaveStore.Slug("/../") == "world", "World names cannot escape the saves folder or use reserved device names");
        await Regions(check, first);
        await Snapshot(check, frame, parent, first);
        await MainWorkflow(check, frame, parent, root);
    }
    private static async Task Regions(Action<bool, string> check, string directory)
    {
        var settings = new WorldSettings("save-regions", 300);
        var regions = new TerrainRegionStore(directory, settings);
        byte[] payload = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
        regions.Write(-1, -33, 16, "one", payload);
        check(File.Exists(Path.Combine(directory, "regions/r.-1.-2.tsregion")), "Negative chunk coordinates map to floor-divided 32x32 region files");
        var reopened = new TerrainRegionStore(directory, settings);
        check(reopened.Read(-1, -33, 16, "one")!.SequenceEqual(payload) && reopened.Read(-1, -33, 16, "changed") == null,
            "Cold region reads preserve exact bytes and reject stale local earthwork identities");
        check(new TerrainRegionStore(directory, settings with { Seed = "different" }).Read(-1, -33, 16, "one") == null,
            "Different generator settings never reuse terrain records");
        string file = Path.Combine(directory, "regions/r.-1.-2.tsregion");
        using (var append = new FileStream(file, FileMode.Append)) append.Write(new byte[21]);
        reopened = new(directory, settings);
        check(reopened.Read(-1, -33, 16, "one")!.SequenceEqual(payload), "Incomplete final region writes leave earlier complete chunks readable");
        reopened.Write(-2, -33, 16, "two", payload);
        check(new TerrainRegionStore(directory, settings).Read(-2, -33, 16, "two")!.SequenceEqual(payload), "New records recover an interrupted region tail");
        for (int i = 0; i < 7; i++) reopened.Write(i * 32, 0, 16, "", payload);
        check(reopened.CachedRegions <= 4 && reopened.Read(-1, -33, 16, "one")!.SequenceEqual(payload), "Region indices unload from memory and reopen on demand within their bound");

        async Task<(Vector3[] Vertices, Color[] Colors, float[] Trees)> Chunk(TerrainBackend backend, int segments)
        {
            backend.Request(new(2, 2), segments);
            for (int i = 0; i < 10000; i++)
            {
                var results = backend.Poll();
                if (results.Count > 0)
                {
                    using var data = results[0];
                    if (data.ContainsKey("error")) throw new Exception(data["error"].AsString());
                    using var arrays = data["arrays"].AsGodotArray();
                    return (arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(), arrays[(int)Mesh.ArrayType.Color].AsColorArray(), data["trees"].AsFloat32Array());
                }
                await Task.Delay(1);
            }
            throw new Exception("Region test chunk timed out");
        }
        (Vector3[] Vertices, Color[] Colors, float[] Trees) original;
        using (var backend = new TerrainBackend())
        {
            backend.Configure(settings.Seed, settings.MountainHeight, 1, 0); backend.Regions = reopened;
            original = await Chunk(backend, 32); await Chunk(backend, 16);
            check(backend.MeshGenerations == 2, "Each first-requested terrain LOD is generated once and persisted"); backend.Shutdown();
        }
        using (var backend = new TerrainBackend())
        {
            backend.Configure(settings.Seed, settings.MountainHeight, 1, 0); backend.Regions = new(directory, settings);
            var loaded = await Chunk(backend, 32); await Chunk(backend, 16); await Chunk(backend, 32);
            check(backend.MeshGenerations == 0 && backend.NaturalTreeGenerations == 0 && loaded.Vertices.SequenceEqual(original.Vertices) && loaded.Colors.SequenceEqual(original.Colors)
                && loaded.Trees.SequenceEqual(original.Trees), "Cold restart and LOD revisits restore exact resolved terrain/trees without regenerating meshes");
            backend.Shutdown();
        }
        var field = new EarthworkField(new[] { new[] { new EarthPoint(100, 10, 100, Left: 10, Right: 10), new EarthPoint(110, 10, 110, Left: 10, Right: 10) } });
        check(field.ChunkSignature(0, 0) != "" && field.ChunkSignature(20, 20) == "", "Earthwork disk identities change locally while distant natural chunks remain valid");
    }
    private static async Task Snapshot(Action<bool, string> check, Func<Task> frame, Node parent, string directory)
    {
        var network = new TrackNetwork();
        var nodes = Enumerable.Range(0, 8).Select(i => network.AddNode(new(0, 0, i * 200))).ToArray();
        var edges = Enumerable.Range(0, 7).Select(i => network.ConnectNodes(nodes[i], nodes[i + 1])!).ToArray();
        var parallel = network.AddParallel(nodes[3], 1); network.SetParallelOffset(parallel, 7);
        var deleted = network.AddNode(new(400, 0, 400)); network.DeleteNode(deleted.Id);
        network.AddStation(edges[4], 1); network.AddStation(edges[4], -1); network.AddStation(edges[5], 1);
        network.SetSignal(nodes[1].Id, 1, RailSignalKind.Chain); network.SetSignal(nodes[3].Id, 1, RailSignalKind.Normal);
        var camera = new WorldCamera { Position = new(100, 300, 400) }; parent.AddChild(camera); camera.SetProcess(false); camera.LookAt(new(0, 0, 300));
        var layer = new CanvasLayer(); parent.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
        var editor = new TrainEditor { Network = network, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows }; parent.AddChild(editor); editor.SetProcess(false);
        var copyNetwork = new TrackNetwork();
        var copy = new TrainEditor { Network = copyNetwork, Camera = camera, Catalog = editor.Catalog, Windows = windows }; parent.AddChild(copy); copy.SetProcess(false);
        try
        {
            editor.AddVehicle("atsf-f7a"); int id = editor.Builder.Selected!.Id; editor.AddVehicle("atsf-coach", id);
            editor.StartPlacement(id); editor.RefreshPreview(camera.UnprojectPosition(new(0, 0, 100)));
            check(editor.Valid && editor.CommitPreview(), "Save fixture places a composed train");
            var train = editor.Builder.Get(id); var parked = editor.Parked.Single();
            train.Name = "Signal tester"; train.Running = true; train.Speed = 7;
            train.Orders.Add(TrainOrderKind.Station, edges[4].Station); train.Orders.Add(TrainOrderKind.Wait, seconds: 180);
            train.Orders.Add(TrainOrderKind.Waypoint, nodes[7].Id);
            parked.Route = new(editor.Placement, parked.Position, 55);
            parked.Route.SetPath(parked.Position, 1, edges.Select(e => RailRouter.StateId(e.Id, 1)).ToArray());
            editor.Interlocking.Update(new[] { new SignalTrain(id, parked.Route, parked.Position, train.Length, 1) });
            editor.Interlocking.Limit(new(id, parked.Route, parked.Position, train.Length, 1), 200);
            var save = new GameSave { Name = "São Paulo Railway" }; network.Capture(save); editor.Capture(save);
            WorldSaveStore.Write(directory, save); var loaded = WorldSaveStore.Read(directory);
            copyNetwork.Space.Rebase(new(10000, 0, 10000)); copyNetwork.Restore(loaded); copy.Restore(loaded);
            var restored = copy.Builder.Get(id); var restoredParked = copy.Parked.Single();
            check(restored.Items.Select(i => i.Definition.Id).SequenceEqual(train.Items.Select(i => i.Definition.Id))
                && restored.Name == train.Name && restored.Speed == 7 && restored.Running && restored.Orders.Items.SequenceEqual(train.Orders.Items),
                "World snapshot restores full consists, commands, speed and all order kinds");
            check(copyNetwork.Edges[edges[4].Id].Platforms.Count == 2 && copyNetwork.Edges[edges[5].Id].Station == edges[4].Station
                && copyNetwork.Signals.Count == 2 && copyNetwork.Groups[parallel.Group].Offsets[parallel.Id] == 7,
                "Platforms, station grouping, directional signals and parallel group spacing survive restart");
            check(restoredParked.Route!.Sample(restoredParked.Position, 700).Position == parked.Route.Sample(parked.Position, 700).Position
                && copy.Interlocking.ReservationCount == editor.Interlocking.ReservationCount,
                "Chosen route occurrences and pending signal reservations survive restart and floating origins");
            check(copyNetwork.Nodes[nodes[3].Id].WorldPosition == nodes[3].WorldPosition
                && copyNetwork.AddNode(new(0, 0, 0)).Id > deleted.Id, "Absolute coordinates and future object ID allocation survive deletion and restart");
            double before = restoredParked.Position.U;
            for (int i = 0; i < 100; i++) { copy.AdvanceMovement(.1); if (copy.RoutingPending) await Task.Delay(1); }
            check(restoredParked.Position.U != before || restoredParked.Position.Edge != parked.Position.Edge,
                "Restored trains simulate normally without any TerrainWorld or loaded chunks");
            restored.Orders.Index = 1; restored.Orders.WaitRemaining = 37.5; restored.Speed = 0;
            var waiting = new GameSave(); copyNetwork.Capture(waiting); copy.Capture(waiting); WorldSaveStore.Write(directory, waiting);
            var roundTrip = WorldSaveStore.Read(directory);
            check(roundTrip.Trains.Single().OrderIndex == 1 && roundTrip.Trains.Single().WaitRemaining == 37.5
                && File.Exists(Path.Combine(directory, "world.previous.json.gz")), "Timed wait progress persists and atomic replacement keeps the previous snapshot");
        }
        finally { copy.Free(); editor.Free(); layer.Free(); camera.Free(); }
        await frame();
    }
    private static async Task MainWorkflow(Action<bool, string> check, Func<Task> frame, Node parent, string root)
    {
        string? directory = null;
        var game = new Main { SavesRoot = root, MenuPresetOverride = 0, MenuSettingsFile = "res://.godot/save-checks/menu.cfg", HudSettingsFile = "res://.godot/save-checks/hud.cfg" };
        parent.AddChild(game); game.SetProcess(false);
        try
        {
            check(game.MenuUI.WorldName.Text == "New World" && game.MenuUI.LoadButton != null, "Menu exposes world naming and loading");
            game.CreateGame("My Test World", new());
            game.World.SetProcess(false); game.TreeClearance.SetProcess(false); game.Trains.SetProcess(false); game.TrackEditor.SetProcess(false);
            check(await game.SaveGame() && Path.GetFileName(game.WorldDirectory!) == "my-test-world", "New world creates a slug folder and a durable snapshot");
            game.TrackNetwork.AddNode(new(0, 0, 0));
            game._Process(301); check(await game.SaveGame(), "Five-minute autosave writes a snapshot");
            check(WorldSaveStore.Read(game.WorldDirectory!).Nodes.Length == 1, "Autosave captures current simulation objects");
            directory = game.WorldDirectory;
            game.HUD.MenuButton.EmitSignal(Button.SignalName.Pressed);
            check(game.PauseUI.IsOpen && game.GetTree().Paused && !game.Trains.CanProcess() && !game.Camera.CanProcess(),
                "HUD menu opens pause and freezes the actual train/camera nodes");
            check(await game.PauseUI.Save() && game.GetTree().Paused, "A real world snapshot can be saved from the paused menu");
            game.PauseUI.ResumeButton.EmitSignal(Button.SignalName.Pressed);
            game._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            check(game.PauseUI.IsOpen, "Unconsumed gameplay Escape opens the pause menu");
            game.PauseUI.Resume();
        }
        finally { game.Free(); }
        await frame();
        var loaded = new Main { SavesRoot = root, MenuPresetOverride = 0, MenuSettingsFile = "res://.godot/save-checks/menu.cfg", HudSettingsFile = "res://.godot/save-checks/hud.cfg" };
        parent.AddChild(loaded); loaded.SetProcess(false);
        try
        {
            loaded.OpenLoadGames();
            check(loaded.MenuUI.Windows.HasModal, "Saved world list uses the shared modal window system");
            loaded.LoadGame(directory!);
            loaded.World.SetProcess(false); loaded.TreeClearance.SetProcess(false); loaded.Trains.SetProcess(false); loaded.TrackEditor.SetProcess(false);
            check(loaded.TrackNetwork.Nodes.Count == 1 && loaded.WorldName == "My Test World" && loaded.World.Chunks.Count == 0
                && loaded.Generator.Backend.Regions != null && loaded.World.InitialReady != null,
                "Main menu load restores the global world before streaming chunks and waits for the initial earthworks snapshot");
        }
        finally { loaded.Free(); }
        await frame();
    }
}
