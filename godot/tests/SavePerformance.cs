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
        var save = WorldSaveStore.Read(directory);
        GD.Print($"Profiling save {save.SavedUtc:O}, {save.Trains.Length} trains, {save.Trains.Sum(t => t.Items.Length)} vehicles, {save.Edges.Length} edges, {save.Signals.Length} signals");
        var samples = new List<Dictionary<string, double>>();
        Main? game = null; TrainEditor editor; CanvasLayer? layer = null; WorldCamera? camera = null;
        if (phase.StartsWith("world"))
        {
            game = new Main { MenuPresetOverride = 0, MenuSettingsFile = "res://.godot/stutter-profile/menu.cfg", HudSettingsFile = "res://.godot/stutter-profile/hud.cfg" };
            parent.AddChild(game); game.LoadGame(directory); editor = game.Trains;
            game.Camera.SetProcess(false); game.TrackEditor.SetProcess(false);
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
            editor.ParallelCalculations = !phase.Contains("serial");
            if (phase.EndsWith("stopped"))
                foreach (var t in editor.Builder.Trains) { t.Running = false; t.Speed = 0; }
            FrameProbe.Enabled = true;
            for (int i = 0; i < 600; i++)
            {
                FrameProbe.Milliseconds.Clear();
                long allocated = GC.GetAllocatedBytesForCurrentThread(); int gc = GC.CollectionCount(0);
                var timer = Stopwatch.StartNew();
                if (game == null)
                {
                    editor._Process(phase.EndsWith("30") ? 1.0 / 30 : 1.0 / 60);
                    if (editor.RoutingPending) await Task.Delay(1);
                }
                else await frame();
                var sample = new Dictionary<string, double>(FrameProbe.Milliseconds)
                {
                    ["elapsed"] = timer.Elapsed.TotalMilliseconds,
                    ["allocated.bytes"] = GC.GetAllocatedBytesForCurrentThread() - allocated,
                    ["gc.gen0"] = GC.CollectionCount(0) - gc
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
            string output = ProjectSettings.GlobalizePath($"res://.godot/stutter-profile/{phase}.json");
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
