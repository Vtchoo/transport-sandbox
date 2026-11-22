using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class ShadowRebaseChecks
{
    public static async Task Run(Action<bool, string> check, Node root, Func<Task> frame, string directory)
    {
        string Option(string key, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--" + key + "="))?.Split('=', 2)[1] ?? fallback;
        string output = ProjectSettings.GlobalizePath(Option("output", "res://.godot/shadow-profile/baseline")); Directory.CreateDirectory(output);
        var game = new Main { MenuPresetOverride = 0, MenuSettingsFile = output + "/menu.cfg", HudSettingsFile = output + "/hud.cfg" };
        root.AddChild(game); game.SetProcess(false); game.LoadGame(ProjectSettings.GlobalizePath(directory));
        game.SoundSettings.Muted = true;
        game.Trains.SetProcess(false); game.Camera.SetProcess(false); game.TrackEditor.SetProcess(false);
        game.Camera.SetProcessUnhandledInput(false); game.Trains.SetProcessUnhandledInput(false); game.TrackEditor.SetProcessUnhandledInput(false);
        game.TrainUI.ToggleFollow(int.Parse(Option("train", "2"))); game.Camera._Process(0);
        game.Trains.Smoke.Hide();
        try
        {
            for (int count = 0; count < 1500; count++)
            {
                await frame();
                if (game.World.Chunks.Count > 0 && game.World.Chunks.Count == game.World.Wanted.Count && game.World.ActiveJobs == 0 && game.World.PendingEarthworks == 0 && !game.TreeClearance.IsPending) break;
            }
            game.World.SetProcess(false); game.Industries.SetProcess(false);
            check(game.World.Chunks.Count > 0, "Rebase capture includes loaded terrain");
            async Task Draw() { await frame(); await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
            for (int i = 0; i < 5; i++) await Draw();
            void Capture(string name)
            { using var image = root.GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(output, name + ".png")); }
            Capture("before");
            var origin = game.TrackNetwork.Space.Origin;
            var records = new System.Collections.Generic.List<object>();
            for (int cycle = 0; cycle < 4; cycle++)
            {
                game.TrackNetwork.Space.Rebase(origin + (cycle % 2 == 0 ? new TrackVector(512, 0, 512) : new TrackVector(0, 0, 0)));
                for (int i = 0; i < 4; i++)
                {
                    await Draw(); Capture($"shift-{cycle}-{i}");
                    records.Add(new { cycle, frame = i, origin = game.TrackNetwork.Space.Origin, batches = game.Trains.Instances.BatchCount });
                }
                using var first = Image.LoadFromFile(Path.Combine(output, $"shift-{cycle}-0.png"));
                using var next = Image.LoadFromFile(Path.Combine(output, $"shift-{cycle}-1.png"));
                // Static scene: compare the train area on the very first frame against
                // the following frame. The original missing-train frame differs greatly.
                double difference = 0; int pixels = 0;
                for (int y = first.GetHeight() * 3 / 8; y < first.GetHeight() * 7 / 8; y += 2)
                    for (int x = first.GetWidth() / 5; x < first.GetWidth() * 2 / 3; x += 2)
                    {
                        var a = first.GetPixel(x, y); var b = next.GetPixel(x, y);
                        difference += (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3;
                        pixels++;
                    }
                double error = difference / pixels;
                records.Add(new { cycle, firstFrameMeanError = error });
                check(error < .015, $"Train remains visible on first origin-shift frame ({cycle}, image error {error:F5})");
            }
            File.WriteAllText(Path.Combine(output, "trace.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("Shadow rebase capture written: " + output);
        }
        finally { game.Free(); await frame(); }
    }
}
