using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class TreeStreamingPerformance
{
    public static TreeData[] Forest(int count) => Enumerable.Range(0, count).Select(i => new TreeData((i % 64) * 8 + .3f, 100, (i / 64) * 8 + .7f, 13 + i % 11, 3 + i % 4, (i % 63) * .1f, i % 2)).ToArray();
    public static async Task Run(Node parent, Func<Task> frame)
    {
        var data = Forest(2048);
        var create = new double[25];
        var remove = new double[25];
        var lod = new double[25];
        for (int i = -3; i < create.Length; i++)
        {
            var clock = Stopwatch.StartNew();
            var chunk = TreeScenery.Create(data);
            parent.AddChild(chunk);
            double created = clock.Elapsed.TotalMilliseconds;
            await frame();
            clock.Restart();
            for (int repeat = 0; repeat < 1000; repeat++)
                chunk.UpdateLod(500);
            double update = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            parent.RemoveChild(chunk);
            chunk.Free();
            double removed = clock.Elapsed.TotalMilliseconds;
            if (i >= 0)
            {
                create[i] = created;
                remove[i] = removed;
                lod[i] = update;
            }
            await frame();
        }
        var prepared = await Task.Run(() => TreeScenery.Prepare(data));
        var upload = new double[25];
        for (int i = -3; i < upload.Length; i++)
        {
            var clock = Stopwatch.StartNew();
            var chunk = TreeScenery.Create(prepared);
            parent.AddChild(chunk);
            if (i >= 0)
                upload[i] = clock.Elapsed.TotalMilliseconds;
            await frame();
            parent.RemoveChild(chunk);
            chunk.Free();
            await frame();
        }
        object Summary(double[] values)
        {
            var sorted = values.Order().ToArray();
            return new
            {
                medianMs = sorted[sorted.Length / 2],
                p95Ms = sorted[(int)Math.Floor((sorted.Length - 1) * .95)],
                maxMs = sorted[^1]
            };
        }
        var result = new
        {
            treesPerChunk = data.Length,
            samples = create.Length,
            display = DisplayServer.GetName(),
            create = Summary(create),
            preparedUpload = Summary(upload),
            remove = Summary(remove),
            unchangedLod1000Calls = Summary(lod)
        };
        string json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        using var file = FileAccess.Open("res://.godot/tree-performance.json", FileAccess.ModeFlags.Write);
        file.StoreString(json);
        GD.Print(json);
    }
}
