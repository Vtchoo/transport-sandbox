using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

/// <summary>Real-time moving-camera trace. Image readbacks are opt-in and kept separate from clean timing runs.</summary>
public static class FollowTrainPerformance
{
    public static async Task Run(Node parent, Func<Task> frame, string directory)
    {
        string Option(string key, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--" + key + "="))?.Split('=', 2)[1] ?? fallback;
        directory = ProjectSettings.GlobalizePath(directory);
        string output = ProjectSettings.GlobalizePath(Option("output", "res://.godot/follow-profile")); Directory.CreateDirectory(output);
        string name = Option("name", "follow-native"); double seconds = double.Parse(Option("seconds", "70"), System.Globalization.CultureInfo.InvariantCulture);
        bool capture = Option("images", "false") == "true", floating = Option("floating", "true") == "true";
        bool cameraLate = Option("camera-late", "false") == "true";
        bool probes = Option("probes", "true") != "false";
        var save = WorldSaveStore.Read(directory);
        int id = int.Parse(Option("train", "0"));
        if (id == 0) id = save.Trains.Where(t => t.Running && t.Items.Any(i => i.Vehicle == "ge-dash9") && t.Items.Any(i => i.Vehicle == "atsf-coach"))
            .OrderBy(t => Math.Abs(t.Speed - 30)).Select(t => t.Id).First();
        var game = new Main { MenuPresetOverride = 0, MenuSettingsFile = output + "/menu.cfg", HudSettingsFile = output + "/hud.cfg" };
        parent.AddChild(game); game.SetProcess(false); game.LoadGame(directory);
        game.Trains.SetProcess(false); game.Camera.SetProcess(false); game.Camera.SetProcessUnhandledInput(false);
        game.Camera.InputBlocked = false; game.TrackEditor.SetProcess(false); game.TrackEditor.SetProcessUnhandledInput(false); game.Trains.SetProcessUnhandledInput(false);
        game.Camera.FloatingOriginEnabled = floating;
        if (cameraLate) game.Camera.ProcessPriority = 100;
        var train = game.Trains.Builder.Get(id); var parked = game.Trains.Parked.Single(p => p.Id == id);
        int rebases = 0; game.TrackNetwork.Space.Rebased += _ => rebases++;
        var samples = new List<Dictionary<string, double>>(); var pictures = new List<object>();
        var viewport = parent.GetViewport().GetViewportRid(); RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        var recent = new Queue<double>(); var clock = new Stopwatch(); double nextCheckpoint = 0, lastEvent = -10;
        int capturedEvents = 0, pendingPictures = 0, contaminated = 0;
        try
        {
            game.TrainUI.ToggleFollow(id); game.Camera._Process(0);
            var settle = Stopwatch.StartNew(); int quiet = 0;
            while (quiet < 5 && settle.Elapsed.TotalSeconds < 35)
            {
                await frame();
                bool pending = game.World.Chunks.Count < game.World.Wanted.Count || game.World.ActiveJobs > 0 || game.World.PendingEarthworks > 0 || game.TreeClearance.IsPending;
                quiet = pending ? 0 : quiet + 1;
            }
            GD.Print($"Follow trace: save={save.SavedUtc:O}, train={id}, cars={train.Items.Count}, speed={train.Speed * 3.6:F1} km/h, chunks={game.World.Chunks.Count}, warmup={settle.Elapsed.TotalSeconds:F1}s, images={capture}, floating={floating}");
            game.Trains.SetProcess(true); game.Camera.SetProcess(true);
            await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            FrameProbe.Enabled = probes; clock.Start();
            var previousCamera = game.Camera.WorldPosition; var previousTrain = game.TrackNetwork.Space.ToWorld(parked.Poses[0].BodyCenter);
            var previousOrigin = game.TrackNetwork.Space.Origin;
            long previousJobs = game.World.SubmittedJobs, previousEvictions = game.World.MeshCache.Evictions;
            Vector2 previousScreen = game.Camera.UnprojectPosition(game.Camera.Following!.GlobalPosition);
            int previousRebases = rebases;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                FrameProbe.Milliseconds.Clear();
                int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                long allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                await frame();
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                double elapsed = timer.Elapsed.TotalMilliseconds;
                var body = game.Camera.Following!; var worldBody = game.TrackNetwork.Space.ToWorld(body.GlobalPosition);
                var worldPose = game.TrackNetwork.Space.ToWorld(parked.Poses[0].BodyCenter);
                var camera = game.Camera.WorldPosition; var origin = game.TrackNetwork.Space.Origin;
                var screen = game.Camera.UnprojectPosition(body.GlobalPosition);
                double gcAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                var sample = new Dictionary<string, double>(FrameProbe.Milliseconds)
                {
                    ["frame"] = samples.Count, ["time"] = clock.Elapsed.TotalSeconds, ["elapsed"] = elapsed,
                    ["gc.gen0"] = GC.CollectionCount(0) - gc0, ["gc.gen1"] = GC.CollectionCount(1) - gc1, ["gc.gen2"] = GC.CollectionCount(2) - gc2,
                    ["allocated.bytes"] = gcAllocated, ["capture.contaminated"] = contaminated,
                    ["render.cpu"] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport), ["render.gpu"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport),
                    ["render.setup"] = RenderingServer.GetFrameSetupTimeCpu(),
                    ["render.draw_calls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), ["render.primitives"] = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
                    ["speed.kmh"] = train.Speed * 3.6, ["train.edge"] = parked.Position.Edge, ["train.u"] = parked.Position.U,
                    ["train.x"] = worldPose.X, ["train.y"] = worldPose.Y, ["train.z"] = worldPose.Z,
                    ["train.step"] = (worldPose - previousTrain).Length, ["train.visual_error"] = (worldPose - worldBody).Length,
                    ["camera.x"] = camera.X, ["camera.y"] = camera.Y, ["camera.z"] = camera.Z, ["camera.step"] = (camera - previousCamera).Length,
                    ["screen.x"] = screen.X, ["screen.y"] = screen.Y, ["screen.step"] = screen.DistanceTo(previousScreen),
                    ["origin.x"] = origin.X, ["origin.z"] = origin.Z, ["origin.changed"] = origin != previousOrigin ? 1 : 0, ["origin.events"] = rebases - previousRebases,
                    ["terrain.chunks"] = game.World.Chunks.Count, ["terrain.jobs"] = game.World.ActiveJobs, ["terrain.submitted"] = game.World.SubmittedJobs - previousJobs,
                    ["terrain.uploads"] = game.World.LastUploads, ["terrain.upload_ms"] = game.World.LastUploadMs, ["terrain.pending"] = game.World.PendingEarthworks,
                    ["trees.operations"] = game.World.Trees.LastOperations, ["terrain.evictions"] = game.World.MeshCache.Evictions - previousEvictions,
                    ["train.batches"] = game.Trains.Instances.BatchCount
                };
                samples.Add(sample);
                previousCamera = camera; previousTrain = worldPose; previousOrigin = origin; previousScreen = screen; previousRebases = rebases;
                previousJobs = game.World.SubmittedJobs; previousEvictions = game.World.MeshCache.Evictions;
                double median = recent.Count == 0 ? elapsed : recent.Order().ElementAt(recent.Count / 2);
                recent.Enqueue(elapsed); if (recent.Count > 120) recent.Dequeue();
                bool spike = elapsed > Math.Max(50, median * 2.5);
                if (spike && contaminated == 0) GD.Print($"Slow follow frame {samples.Count - 1}: {elapsed:F1}ms, t={sample["time"]:F2}, rebase={sample["origin.events"]}, gc={sample["gc.gen0"]}/{sample["gc.gen1"]}/{sample["gc.gen2"]}, uploads={game.World.LastUploads}, trees={game.World.Trees.LastOperations}");
                if (capture)
                {
                    string? reason = null;
                    if ((spike || sample["origin.changed"] > 0) && clock.Elapsed.TotalSeconds - lastEvent > 2 && capturedEvents < 10)
                    { lastEvent = clock.Elapsed.TotalSeconds; capturedEvents++; pendingPictures = 3; reason = spike ? "spike" : "rebase"; }
                    else if (pendingPictures > 0) reason = "after";
                    else if (clock.Elapsed.TotalSeconds >= nextCheckpoint) reason = "checkpoint";
                    if (reason != null)
                    {
                        string path = Path.Combine(output, $"{name}-{samples.Count - 1:D5}-{reason}.jpg");
                        using var image = parent.GetViewport().GetTexture().GetImage(); image.SaveJpg(path, .9f);
                        pictures.Add(new { frame = samples.Count - 1, reason, elapsed, time = clock.Elapsed.TotalSeconds, path = Path.GetFileName(path) });
                        if (pendingPictures > 0) pendingPictures--;
                        nextCheckpoint = clock.Elapsed.TotalSeconds + 2; contaminated = 5;
                    }
                    else contaminated = Math.Max(0, contaminated - 1);
                }
            }
            FrameProbe.Enabled = false;
            var values = samples.Select(s => s["elapsed"]).Order().ToArray();
            var slowest = samples.OrderByDescending(s => s["elapsed"]).Take(25).ToArray();
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                save.SavedUtc, train = id, seconds, capture, floating, cameraLate, probes, width = parent.GetViewport().GetVisibleRect().Size.X, height = parent.GetViewport().GetVisibleRect().Size.Y,
                summary = new { frames = samples.Count, mean = values.Average(), p50 = values[values.Length / 2], p95 = values[(int)(values.Length * .95)], p99 = values[(int)(values.Length * .99)], max = values[^1], rebases, over50 = values.Count(v => v > 50), over100 = values.Count(v => v > 100) },
                slowest, pictures, samples
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"Follow profile {name}: {samples.Count} frames, mean={values.Average():F2}ms p99={values[(int)(values.Length * .99)]:F2}ms max={values[^1]:F2}ms rebases={rebases}; written to {output}");
        }
        finally { FrameProbe.Enabled = false; game.Free(); await frame(); }
    }
}
