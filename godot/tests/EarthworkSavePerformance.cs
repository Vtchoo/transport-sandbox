using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class EarthworkSavePerformance
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent, string directory)
    {
        directory = ProjectSettings.GlobalizePath(directory);
        var saved = WorldSaveStore.Read(directory);
        var game = new Main { MenuPresetOverride = 0, MenuSettingsFile = "res://.godot/earthwork-profile/menu.cfg", HudSettingsFile = "res://.godot/earthwork-profile/hud.cfg" };
        parent.AddChild(game); game.SetProcess(false); game.LoadGame(directory);
        game.Camera.SetProcess(false); game.Trains.SetProcess(false); game.Trains.Smoke.SetProcess(false);
        game.TrackEditor.SetProcess(false); game.TrackRenderer.SetProcess(false); game.TrainUI.SetProcess(false);
        var cases = new List<object>();
        async Task Settle(List<Dictionary<string, double>>? frames = null, double timeout = 35)
        {
            var clock = Stopwatch.StartNew(); int quiet = 0;
            do
            {
                FrameProbe.Milliseconds.Clear(); var timer = Stopwatch.StartNew();
                await frame();
                if (frames != null)
                {
                    var sample = new Dictionary<string, double>(FrameProbe.Milliseconds) { ["elapsed"] = timer.Elapsed.TotalMilliseconds };
                    frames.Add(sample);
                }
                bool pending = game.TreeClearance.IsPending || game.World.PendingEarthworks > 0 || game.World.ActiveJobs > 0
                    || game.World.Chunks.Count < game.World.Wanted.Count;
                quiet = pending ? 0 : quiet + 1;
            } while (quiet < 4 && clock.Elapsed.TotalSeconds < timeout);
            check(quiet >= 4, "Earthwork save benchmark settles within its time bound");
        }
        try
        {
            await Settle(timeout: 90);
            GD.Print($"Earthwork save {saved.SavedUtc:O}: {saved.Edges.Length} segments; {game.World.Chunks.Count} resident chunks; {game.World.WorkerLimit} chunk workers");
            var node = game.TrackNetwork.Nodes.Values.Where(n => game.World.Chunks.TryGetValue(new((int)Math.Floor(n.WorldPosition.X / 512), (int)Math.Floor(n.WorldPosition.Z / 512)), out var chunk)
                && chunk.GetMeta("segments").AsInt32() >= 32 && game.TrackNetwork.Incident(n.Id).Count == 2)
                .OrderBy(n => n.Position.DistanceSquaredTo(game.Camera.Target)).First();
            var original = node.Position;
            GD.Print($"Editing node {node.Id}, parallel group {node.Group}, original {original}");
            async Task Case(string name, Action action)
            {
                while (EarthworkProbe.Samples.TryDequeue(out _)) { }
                FrameProbe.Enabled = true; EarthworkProbe.Enabled = true;
                var frames = new List<Dictionary<string, double>>();
                var meshes = game.World.Chunks.ToDictionary(pair => pair.Key, pair => pair.Value.GetChild<MeshInstance3D>(0).Mesh.GetInstanceId());
                long submitted = game.World.SubmittedJobs, generated = game.Generator.Backend.MeshGenerations;
                var timer = Stopwatch.StartNew(); action(); await Settle(frames);
                FrameProbe.Enabled = false; EarthworkProbe.Enabled = false;
                var workers = new List<EarthworkProbe.Sample>(); while (EarthworkProbe.Samples.TryDequeue(out var sample)) workers.Add(sample);
                var main = frames.SelectMany(s => s.Keys).Distinct().ToDictionary(key => key, key =>
                {
                    var values = frames.Select(s => s.GetValueOrDefault(key)).Order().ToArray();
                    return new { total = values.Sum(), mean = values.Average(), p99 = values[(int)(values.Length * .99)], max = values[^1] };
                });
                var stages = workers.GroupBy(s => s.Stage).ToDictionary(g => g.Key, g => new { count = g.Count(), total = g.Sum(s => s.Milliseconds), max = g.Max(s => s.Milliseconds), threads = g.Select(s => s.Thread).Distinct().Count() });
                int replaced = game.World.Chunks.Count(pair => meshes.TryGetValue(pair.Key, out ulong id) && pair.Value.GetChild<MeshInstance3D>(0).Mesh.GetInstanceId() != id);
                var result = new { name, elapsedMs = timer.Elapsed.TotalMilliseconds, submitted = game.World.SubmittedJobs - submitted,
                    generated = game.Generator.Backend.MeshGenerations - generated, replaced, frameCount = frames.Count, main, stages, workers };
                cases.Add(result);
                if (name is "unchanged-notification" or "superseded-raise-undo")
                    check(result.submitted == 0 && replaced == 0, "Unchanged or superseded edits retain all terrain meshes and create no chunk jobs");
                GD.Print($"Earthwork {name}: elapsed={result.elapsedMs:F2} ms submitted={result.submitted} generated={result.generated} replaced={replaced}");
                GD.Print(JsonSerializer.Serialize(new { main, stages }));
            }
            await Case("unchanged-notification", () => game.TrackNetwork.NotifyChanged());
            await Case("raise-node-2m", () => game.TrackNetwork.MoveNode(node, original + Vector3.Up * 2));
            await Case("restore-node", () => game.TrackNetwork.MoveNode(node, original));
            await Case("repeat-raise-after-restore", () => game.TrackNetwork.MoveNode(node, original + Vector3.Up * 2));
            await Case("superseded-raise-undo", () =>
            {
                game.TrackNetwork.MoveNode(node, original + Vector3.Up * 4);
                game.TreeClearance._Process(.5);
                game.TrackNetwork.MoveNode(node, original + Vector3.Up * 2);
            });
            string output = ProjectSettings.GlobalizePath("res://.godot/earthwork-profile/results.json");
            System.IO.File.WriteAllText(output, JsonSerializer.Serialize(new { saved.SavedUtc, edges = saved.Edges.Length, chunks = game.World.Chunks.Count,
                node = node.Id, parallelGroup = node.Group, cases }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("Earthwork profile written: ", output);
        }
        finally { FrameProbe.Enabled = false; EarthworkProbe.Enabled = false; game.Free(); }
    }
}
