using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class BridgeChecks
{
    public static async Task Capture(Node parent, Func<Task> frame)
    {
        var root = new Node3D(); parent.AddChild(root); WorldLighting.AddTo(root);
        var graph = new TrackNetwork();
        var a = graph.AddNode(new(0, 22, 0)); graph.AddParallel(a, 1);
        var span = graph.ExtendParallel(a, 1, new(35, 22, 160), support: TrackSupport.ConcreteBridge);
        var camera = new WorldCamera { Space = graph.Space, Current = true, Fov = 50 }; root.AddChild(camera);
        camera.SetProcess(false); camera.SetProcessUnhandledInput(false);
        camera.Position = new(110, 85, 175); camera.LookAt(new(10, 10, 85));
        var renderer = new TrackRenderer { Network = graph, Camera = camera }; root.AddChild(renderer);
        renderer._Process(0); renderer.Markers.Hide();
        root.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new(400, 400) }, Position = new(0, -.1f, 80), MaterialOverride = TrackRenderer.Material(new("647850")) });
        root.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new(400, 80) }, Position = new(0, 0, 80), MaterialOverride = TrackRenderer.Material(new("427c89")) });
        var layer = new CanvasLayer(); root.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
        var generator = new TerrainGenerator();
        var editor = new TrackEditor { Network = graph, Renderer = renderer, Camera = camera, Generator = generator, Interface = layer, Windows = windows }; root.AddChild(editor);
        editor.SetProcess(false); editor.SetProcessUnhandledInput(false);
        // Retain the zero-height capture ground used by the live renderer above.
        renderer.Generator = null;
        editor.SegmentProperties.Open(span.Edges[0].Id);
        editor.SegmentProperties.Window.Place(WindowPlacement.TopLeft, new(12, 12));
        for (int i = 0; i < 10; i++) await frame();
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = parent.GetViewport().GetTexture().GetImage(); image.SavePng("res://.godot/bridge-visuals.png");
        root.QueueFree(); await frame(); generator.Backend.Shutdown(); generator.Backend.Dispose();
    }
    public static void Core(Action<bool, string> check)
    {
        var graph = new TrackNetwork();
        var a = graph.AddNode(new(0, 30, 0)); var parallel = graph.AddParallel(a, 1);
        var span = graph.ExtendParallel(a, 1, new(0, 30, 100));
        var edge = span.Edges[0];
        check(span.Edges.All(e => e.Support == TrackSupport.Fill), "Existing/new track defaults to fill");
        graph.SetSupport(edge, TrackSupport.ConcreteBridge);
        check(span.Edges.All(e => e.Support == TrackSupport.ConcreteBridge), "Matching parallel spans share their support type");
        var extension = graph.ExtendParallel(span.Node, span.Side, new(0, 35, 200));
        check(extension.Edges.All(e => e.Support == TrackSupport.ConcreteBridge), "Parallel extensions inherit the preceding support type");
        graph.SetSupport(extension.Edges[0], TrackSupport.Fill);
        check(span.Edges.All(e => e.Support == TrackSupport.ConcreteBridge), "Support edits do not propagate into the next parallel span");
        var crossover = graph.ConnectNodes(a, graph.Nodes[span.Edges[1].B])!;
        graph.SetSupport(crossover, TrackSupport.Fill);
        check(graph.SupportPeers(crossover).Length == 1 && span.Edges.All(e => e.Support == TrackSupport.ConcreteBridge), "Crossovers are not treated as matching parallel lanes");
        graph.SetBridgePiers(edge, new[] { new BridgePier(.2), new BridgePier(.7) });
        check(span.Edges.All(e => e.BridgePiers!.SequenceEqual(edge.BridgePiers!)), "Pier edits apply to the shared parallel bridge");
        var layout = new BridgeLayout(graph, edge); var bounds = layout.Bounds(.5);
        check(Math.Abs((bounds.Right - bounds.Left).Length - 9.8) < .01, "Shared deck spans both tracks and their shoulders");
        var geometry = layout.Create((_, _) => 0);
        check(geometry.GetChildren().OfType<MeshInstance3D>().Count(m => m.Name.ToString().StartsWith("Pier_")) == 2, "Manual layout generates two upright concrete piers");
        check(geometry.GetChildren().OfType<MeshInstance3D>().Where(m => m.Name.ToString().StartsWith("Pier_")).All(m => Math.Abs(m.Position.Y + layout.Anchor.Y - ((BoxMesh)m.Mesh).Size.Y / 2 + .4) < .01), "Piers extend into the natural ground without raising terrain");
        geometry.Free();
        var save = new GameSave(); graph.Capture(save);
        var options = new JsonSerializerOptions { IncludeFields = true };
        var copy = JsonSerializer.Deserialize<GameSave>(JsonSerializer.Serialize(save, options), options)!;
        var restored = new TrackNetwork(); restored.Restore(copy);
        check(restored.Edges[edge.Id].Support == TrackSupport.ConcreteBridge && restored.Edges[edge.Id].BridgePiers!.SequenceEqual(edge.BridgePiers!), "Save roundtrip preserves support and individually edited piers");
        check(JsonSerializer.Deserialize<TrackEdge>("{\"Id\":1,\"A\":1,\"B\":2}", options)!.Support == TrackSupport.Fill, "Old saves without support metadata load as fill");
        var single = new TrackNetwork(); var s = single.AddNode(new(0, 30, 0));
        var original = single.ConnectNodes(s, single.AddNode(new(0, 30, 100)), support: TrackSupport.ConcreteBridge)!;
        single.SetBridgePiers(original, new[] { new BridgePier(.2), new BridgePier(.8) });
        var split = single.Split(original, .5f)!; var halves = single.Incident(split.Id);
        check(halves.All(e => e.Support == TrackSupport.ConcreteBridge && e.BridgePiers!.Count == 1), "Splitting preserves bridge support and partitions manual piers");
        single.SetSupport(halves[0], TrackSupport.Fill);
        check(!single.CanMerge(split.Id), "Different support types cannot merge");
        single.SetSupport(halves[0], TrackSupport.ConcreteBridge);
        var merged = single.Merge(split.Id)!;
        check(merged.BridgePiers!.Count == 2 && Math.Abs(merged.BridgePiers[0].Along - .2) < .001 && Math.Abs(merged.BridgePiers[1].Along - .8) < .001, "Merging matching bridges retains both manual pier layouts");
        var inherited = single.ConnectNodes(single.Nodes[merged.B], single.AddNode(new(0, 30, 150)))!;
        check(inherited.Support == TrackSupport.ConcreteBridge, "Individual extensions inherit bridge support");

        double Natural(double x, double z) => 10;
        var raw = new[] { new EarthPoint(0, 30, 0), new EarthPoint(0, 30, 100) };
        var fillPath = EarthworkField.Prepare(raw, Natural, 1800);
        var bridgePath = EarthworkField.Prepare(raw.Select(p => p with { CutOnly = true }).ToArray(), Natural, 1800);
        var fill = new EarthworkField(new[] { fillPath }); var bridge = new EarthworkField(new[] { bridgePath });
        check(fill.Height(0, 50, 10) > 29 && bridge.Height(0, 50, 10) == 10, "Bridge support removes fill without changing low natural ground");
        var cutting = EarthworkField.Prepare(new[] { new EarthPoint(0, 5, 0, CutOnly: true), new EarthPoint(0, 5, 100, CutOnly: true) }, Natural, 1800);
        var cut = new EarthworkField(new[] { cutting });
        check(Math.Abs(cut.Height(0, 50, 10) - 4.35) < 1e-8 && Math.Abs(cut.Height(6, 50, 10) - 6.35) < 1e-8, "Bridge intersections cut terrain with the existing 2:1 shoulder slope");
        var lower = EarthworkField.Prepare(new[] { new EarthPoint(1, 15, 0), new EarthPoint(1, 15, 100) }, Natural, 1800);
        var stacked = new EarthworkField(new[] { bridgePath, lower });
        check(stacked.Height(0, 50, 10) > 14, "An elevated bridge does not mask earthworks on a lower crossing track");
        check(fill.ChunkSignature(0, 0) != bridge.ChunkSignature(0, 0), "Support changes invalidate resolved terrain region/cache signatures");
    }

    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        Core(check); ClearanceChecks.Run(check);
        var graph = new TrackNetwork(); var generator = new TerrainGenerator { Space = graph.Space };
        var camera = new WorldCamera { Space = graph.Space }; parent.AddChild(camera); camera.SetProcess(false); camera.SetProcessUnhandledInput(false);
        camera.Position = new(100, 100, 170); camera.LookAt(new(0, 30, 60));
        var renderer = new TrackRenderer { Network = graph, Camera = camera, Generator = generator }; parent.AddChild(renderer); renderer.SetProcess(false);
        var layer = new CanvasLayer(); parent.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
        var editor = new TrackEditor { Network = graph, Renderer = renderer, Camera = camera, Generator = generator, Interface = layer, Windows = windows }; parent.AddChild(editor); editor.SetProcess(false); editor.SetProcessUnhandledInput(false);
        var world = new TerrainWorld { Generator = generator, Camera = camera }; parent.AddChild(world); world.SetProcess(false);
        var clearance = new TrackTreeClearance { Network = graph, World = world }; parent.AddChild(clearance); clearance.SetProcess(false);
        try
        {
            editor.SetEnabled(true);
            editor.SupportPicker.Select(1); editor.SupportPicker.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            editor.Place(new(0, 30, 0)); editor.Place(new(0, 30, 100)); var edge = graph.Edges.Values.Single();
            check(edge.Support == TrackSupport.ConcreteBridge, "Build-track selector controls newly built support");
            renderer._Process(0); check(renderer.Bridges.Holders.Count == 1, "Concrete bridge appears in the normal game renderer");
            editor.Cancel(); editor.RefreshHover(camera.UnprojectPosition(new(0, 30, 50)));
            check(editor.SegmentMenu.Edit.Visible, "Segment hover menu exposes Edit segment");
            editor.SegmentMenu.Edit.EmitSignal(Button.SignalName.Pressed);
            var properties = editor.SegmentProperties;
            check(properties.Window.Visible && properties.EdgeId == edge.Id && properties.SupportPicker.Selected == 1, "Segment edit window reflects the current support");
            int automatic = TrackNetwork.AutomaticPiers(100).Length;
            properties.Add(); check(edge.BridgePiers?.Count == automatic + 1, "Adding a pier starts from the automatic layout");
            properties.Delete(); check(edge.BridgePiers?.Count == automatic, "Deleting a selected pier preserves the remaining piers");
            properties.ResetPiers.EmitSignal(Button.SignalName.Pressed); check(edge.BridgePiers == null, "Automatic-piers control restores generated layout");
            properties._Process(0);
            var handle = (Node3D)renderer.FindChild("PierHandle_0", true, false);
            float handleHeight = handle.GlobalPosition.Y;
            var startMouse = camera.UnprojectPosition(handle.GlobalPosition);
            properties._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = startMouse });
            var targetMouse = camera.UnprojectPosition(new(0, handleHeight, 33));
            properties._UnhandledInput(new InputEventMouseMotion { Position = targetMouse });
            properties._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            check(edge.BridgePiers != null && Math.Abs(edge.BridgePiers[0].Along - .33) < .005, "Dragging a pier handle slides it continuously along the bridge centreline");
            graph.Space.Rebase(new(4096, 0, 4096)); renderer.Bridges.Rebase();
            check(renderer.Bridges.Holders[edge.Id].Position.IsEqualApprox(graph.Nodes[edge.A].Position), "Bridge rendering follows floating-origin changes");
            graph.Space.Rebase(new(0, 0, 0));
            properties.SupportPicker.Select(0); properties.SupportPicker.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            renderer._Process(0); check(renderer.Bridges.Holders.Count == 0 && edge.Support == TrackSupport.Fill, "Editing back to fill removes the bridge visual");
            double natural = generator.Sampler.Height(0, 0);
            graph.MoveNode(graph.Nodes[edge.A], new(0, (float)natural + 25, 0)); graph.MoveNode(graph.Nodes[edge.B], new(0, (float)natural + 25, 100));
            async Task Flush()
            {
                for (int i = 0; i < 600; i++) { clearance._Process(.5); if (!clearance.IsPending) return; await frame(); }
                check(false, "Earthwork worker completes bridge updates");
            }
            await Flush(); double filled = generator.HeightWorld(0, 0);
            graph.SetSupport(edge, TrackSupport.ConcreteBridge); await Flush();
            check(filled > natural + 20 && Math.Abs(generator.HeightWorld(0, 0) - natural) < 1e-7, "Live support edit rebuilds the worker snapshot and removes the old embankment");
            graph.SetSupport(edge, TrackSupport.Fill); await Flush();
            check(Math.Abs(generator.HeightWorld(0, 0) - filled) < 1e-7, "Changing back to fill restores the original formation height");
        }
        finally
        {
            clearance.Free(); editor.Free(); renderer.Free(); world.Free(); layer.Free(); camera.Free(); await frame();
        }
    }
}
