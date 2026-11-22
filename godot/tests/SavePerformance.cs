using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class SavePerformance
{
    public static async Task Run(Func<Task> frame, Node parent, string phase, string directory)
    {
        directory = ProjectSettings.GlobalizePath(directory);
        string Option(string name, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--" + name + "="))?.Split('=', 2)[1] ?? fallback;
        int count = int.Parse(Option("frames", "600"));
        string outputDirectory = ProjectSettings.GlobalizePath(Option("output", "res://.godot/stutter-profile"));
        System.IO.Directory.CreateDirectory(outputDirectory);
        var save = WorldSaveStore.Read(directory);
        GD.Print($"Profiling save {save.SavedUtc:O}, {save.Trains.Length} trains, {save.Trains.Sum(t => t.Items.Length)} vehicles, {save.Edges.Length} edges, {save.Signals.Length} signals");
        var samples = new List<Dictionary<string, double>>();
        Main? game = null; TrainEditor editor; CanvasLayer? layer = null; WorldCamera? camera = null;
        if (phase.StartsWith("world"))
        {
            game = new Main { MenuPresetOverride = 0, MenuSettingsFile = "res://.godot/stutter-profile/menu.cfg", HudSettingsFile = "res://.godot/stutter-profile/hud.cfg" };
            parent.AddChild(game); game.LoadGame(directory); editor = game.Trains;
            game.Camera.SetProcess(false); game.TrackEditor.SetProcess(false);
            editor.SetProcess(false);
            var settle = Stopwatch.StartNew();
            while ((game.World.Chunks.Count == 0 || game.World.ActiveJobs > 0 || game.TreeClearance.IsPending) && settle.Elapsed.TotalSeconds < 15) await frame();
            GD.Print($"World warmup {settle.Elapsed.TotalMilliseconds:F1} ms, {game.World.Chunks.Count} chunks");
        }
        else
        {
            var network = new TrackNetwork(); network.Restore(save);
            camera = new WorldCamera(); parent.AddChild(camera); camera.SetProcess(false);
            layer = new CanvasLayer(); parent.AddChild(layer); var windows = new WindowManager(); layer.AddChild(windows);
            editor = new TrainEditor { Network = network, Camera = camera, Catalog = VehicleCatalog.Load(), Windows = windows };
            parent.AddChild(editor); editor.SetProcess(false); editor.Restore(save);
        }
        try
        {
            bool fixedWorld = game != null && phase.Contains("fixed");
            if (fixedWorld) editor.SetProcess(false);
            if (game != null)
            {
                if (phase.Contains("noscene")) game.Hide();
                if (phase.Contains("notracks")) game.TrackRenderer.Hide();
                if (phase.Contains("nosignals")) game.TrackRenderer.Signals.Hide();
                if (phase.Contains("nobridges")) game.TrackRenderer.Bridges.Hide();
                if (phase.Contains("nosleepers")) foreach (var mesh in game.TrackRenderer.FindChildren("Sleepers", "MultiMeshInstance3D", true, false).Cast<MultiMeshInstance3D>()) mesh.Layers = 0;
                if (phase.Contains("noterrainshadows")) foreach (var chunk in game.World.Chunks.Values) chunk.GetChild<MeshInstance3D>(0).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                if (phase.Contains("notrains")) { editor.Instances.Hide(); editor.Smoke.Hide(); }
                if (phase.Contains("noindustries")) game.Industries.Hide();
                if (phase.EndsWith("-notrees")) foreach (var chunk in game.World.Chunks.Values) chunk.GetNodeOrNull<Node3D>("Trees")?.Hide();
                if (phase.Contains("notreeshadows")) foreach (var chunk in game.World.Chunks.Values)
                    if (chunk.GetNodeOrNull<Node3D>("Trees") is { } trees)
                        foreach (var mesh in trees.GetChildren().OfType<GeometryInstance3D>()) mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                if (phase.Contains("noshadows")) foreach (var light in game.FindChildren("*", "DirectionalLight3D", true, false).Cast<DirectionalLight3D>()) light.ShadowEnabled = false;
            }
            var viewport = parent.GetViewport().GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
            editor.ParallelCalculations = !phase.Contains("serial");
            if (phase.EndsWith("stopped"))
                foreach (var t in editor.Builder.Trains) { t.Running = false; t.Speed = 0; }
            // Exclude JIT/template initialization and use the same deterministic simulation warmup in comparisons.
            for (int i = 0; i < 120; i++)
            {
                editor._Process(1.0 / 60);
                if (game != null) await frame();
                else if (editor.RoutingPending) await Task.Delay(1);
            }
            if (game != null && !fixedWorld) editor.SetProcess(true);
            GD.Print($"Renderer: {DisplayServer.GetName()}, {RenderingServer.GetVideoAdapterName()}, phase={phase}, frames={count}");
            FrameProbe.Enabled = Option("probes", "true") != "false";
            for (int i = 0; i < count; i++)
            {
                FrameProbe.Milliseconds.Clear();
                long allocated = GC.GetAllocatedBytesForCurrentThread(); int gc = GC.CollectionCount(0);
                var timer = Stopwatch.StartNew();
                if (game == null)
                {
                    editor._Process(phase.EndsWith("30") ? 1.0 / 30 : 1.0 / 60);
                    if (editor.RoutingPending) await Task.Delay(1);
                }
                else { if (fixedWorld) editor._Process(1.0 / 60); await frame(); }
                var sample = new Dictionary<string, double>(FrameProbe.Milliseconds)
                {
                    ["elapsed"] = timer.Elapsed.TotalMilliseconds,
                    ["allocated.bytes"] = GC.GetAllocatedBytesForCurrentThread() - allocated,
                    ["gc.gen0"] = GC.CollectionCount(0) - gc,
                    ["render.cpu"] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport),
                    ["render.gpu"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport),
                    ["render.setup"] = RenderingServer.GetFrameSetupTimeCpu(),
                    ["render.draw_calls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                    ["render.primitives"] = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
                    ["scene.nodes"] = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)
                };
                samples.Add(sample);
            }
            FrameProbe.Enabled = false;
            var summaries = samples.SelectMany(s => s.Keys).Distinct().ToDictionary(key => key, key =>
            {
                var values = samples.Select(s => s.GetValueOrDefault(key)).Order().ToArray();
                return new { mean = values.Average(), p95 = values[(int)(values.Length * .95)], p99 = values[(int)(values.Length * .99)], max = values[^1] };
            });
            foreach (var (name, summary) in summaries) GD.Print($"{name}: mean={summary.mean:F3} p95={summary.p95:F3} p99={summary.p99:F3} max={summary.max:F3}");
            string output = System.IO.Path.Combine(outputDirectory, phase + ".json");
            System.IO.File.WriteAllText(output, JsonSerializer.Serialize(new { phase, save.SavedUtc, processors = System.Environment.ProcessorCount, summaries, samples }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("Profile written: ", output);
        }
        finally
        {
            FrameProbe.Enabled = false;
            if (game != null) game.Free();
            else { editor.Free(); layer!.Free(); camera!.Free(); }
        }
    }
}
