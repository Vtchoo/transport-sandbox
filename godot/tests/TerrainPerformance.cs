using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class TerrainPerformance
{
    public static void Run()
    {
        var fixture = JsonData.Read("res://tests/earthwork-mesh-fixtures.json");
        var settings = JsonData.Settings(fixture.GetProperty("settings"));
        var sampler = new TerrainSampler(settings.Seed, settings.MountainHeight, settings.HorizontalScale, settings.SeaLevel);
        var paths = fixture.GetProperty("paths").EnumerateArray().Select(path => path.GetProperty("points").EnumerateArray()
            .Select(p => new EarthPoint(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), p.GetProperty("z").GetDouble())).ToArray()).ToArray();
        EarthPoint[][] Prepare() => paths.Select(path => EarthworkField.Prepare(path, sampler.Height, settings.MountainHeight)).ToArray();
        var field = new EarthworkField(Prepare());
        var results = new Dictionary<string, object>();
        void Measure(string name, Func<double> work)
        {
            for (int i = 0; i < 2; i++)
                work();
            var times = new List<double>();
            double checksum = 0;
            for (int i = 0; i < 7; i++)
            {
                var clock = Stopwatch.StartNew();
                checksum = work();
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            results[name] = new
            {
                samples_ms = times,
                checksum
            };
            GD.Print(name, " ", times.OrderBy(t => t).ElementAt(3), " ms");
        }
        Measure("height_10000", () => { double sum = 0; for (int i = 0; i < 10000; i++) sum += sampler.Height(600 + i % 100 * 8, 900 + i / 100 * 8); return sum; });
        Measure("trees_chunk", () => Vegetation.Generate(sampler, 1, 1).Length);
        Measure("prepare_shoulders", () => Prepare().Sum(p => p.Sum(v => v.Left + v.Right)));
        foreach (var (name, x, z, segments, earth) in new[] { ("natural_64", 1, 1, 64, false), ("natural_128", 1, 1, 128, false), ("earthwork_64", 1, 1, 64, true), ("earthwork_16", 1, 1, 16, true) })
            Measure(name, () => { var mesh = TerrainBackend.BenchmarkMesh(sampler, new(x, z), segments, earth ? field : null); return mesh.Vertices + mesh.Indices; });
        // Production chunk jobs, including vegetation and main-thread result packing, without GPU uploads.
        Measure("workers_9_natural", () => Batch(false));
        Measure("workers_2_earthwork", () => Batch(true));
        double Batch(bool earth)
        {
            using var backend = new TerrainBackend();
            backend.Configure(settings.Seed, settings.MountainHeight, settings.HorizontalScale, settings.SeaLevel);
            var keys = earth ? new[] { new Vector2I(0, 1), new Vector2I(1, 1) } : Enumerable.Range(0, 9).Select(i => new Vector2I(i % 3, i / 3)).ToArray();
            int submitted = 0, done = 0;
            double checksum = 0;
            while (done < keys.Length)
            {
                while (submitted < keys.Length && submitted - done < 3)
                {
                    backend.Request(keys[submitted++], 64, earth ? field : null, 1);
                }
                foreach (var data in backend.Poll())
                {
                    if (data.ContainsKey("error"))
                        throw new Exception(data["error"].AsString());
                    var arrays = data["arrays"].AsGodotArray();
                    checksum += arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length + arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length;
                    done++;
                }
                if (done < keys.Length)
                    Thread.Sleep(1);
            }
            backend.Shutdown();
            return checksum;
        }
        using var output = FileAccess.Open("res://.godot/performance/godot.json", FileAccess.ModeFlags.Write);
        output.StoreString(JsonSerializer.Serialize(new
        {
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            godot = Engine.GetVersionInfo()["string"].AsString(),
            configuration = typeof(TerrainPerformance).Assembly.GetCustomAttributes(false).OfType<DebuggableAttribute>().Any(a => a.IsJITOptimizerDisabled) ? "Debug (JIT optimization disabled)" : "JIT optimized",
            warmups = 2,
            repeats = 7,
            workers = 3,
            results
        }));
    }
}
