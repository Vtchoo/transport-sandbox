using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class EarthworkMeshChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame)
    {
        var fixture = JsonData.Read("res://tests/earthwork-mesh-fixtures.json");
        var settings = JsonData.Settings(fixture.GetProperty("settings"));
        var generator = new TerrainGenerator(settings);
        var paths = fixture.GetProperty("paths").EnumerateArray().Select(path => path.GetProperty("points").EnumerateArray()
            .Select(p => new EarthPoint(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), p.GetProperty("z").GetDouble())).ToArray());
        var field = new EarthworkField(paths.Select(path => EarthworkField.Prepare(path, generator.Sampler.Height, settings.MountainHeight)));
        try
        {
            foreach (var sample in fixture.GetProperty("chunks").EnumerateArray())
            {
                var key = new Vector2I(sample.GetProperty("x").GetInt32(), sample.GetProperty("z").GetInt32());
                int segments = sample.GetProperty("segments").GetInt32();
                generator.Backend.Request(key, segments, field, 73);
                var timeout = Stopwatch.StartNew();
                Godot.Collections.Dictionary? data = null;
                while (data == null && timeout.Elapsed.TotalSeconds < 20)
                {
                    var results = generator.Backend.Poll();
                    if (results.Count > 0)
                        data = results[0];
                    else
                        await frame();
                }
                check(data != null && !data.ContainsKey("error"), "Adaptive earthwork worker completes");
                if (data == null || data.ContainsKey("error"))
                    continue;
                check(data["revision"].AsInt64() == 73, "Earthwork worker preserves request revision");
                var arrays = data["arrays"].AsGodotArray();
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                check(vertices.Length == sample.GetProperty("vertexCount").GetInt32() && indices.Length == sample.GetProperty("indexCount").GetInt32(), "Adaptive leaf/fan/skirt counts match original");
                uint hash = 2166136261;
                foreach (int index in indices)
                    hash = unchecked((hash ^ (uint)index) * 16777619);
                check(hash == sample.GetProperty("indicesHash").GetUInt32(), "Every stitched earthwork triangle matches original topology");
                foreach (var point in sample.GetProperty("samples").EnumerateArray())
                {
                    int i = point.GetProperty("index").GetInt32();
                    if (i >= vertices.Length)
                        continue;
                    check(vertices[i].DistanceTo(JsonData.Vector(point.GetProperty("position"))) < .00001, "Adaptive cut/fill and skirt vertices match original");
                    check(normals[i].DistanceTo(JsonData.Vector(point.GetProperty("normal"))) < .000001, "Earthwork finite-difference normals match original");
                    var color = colors[i].SrgbToLinear();
                    var expected = point.GetProperty("color");
                    check(Math.Abs(color.R - expected[0].GetDouble()) < 1e-6 && Math.Abs(color.G - expected[1].GetDouble()) < 1e-6 && Math.Abs(color.B - expected[2].GetDouble()) < 1e-6, "Exposed-soil and natural earthwork colors match original");
                }
            }
        }
        finally { generator.Backend.Shutdown(); generator.Backend.Dispose(); }
    }
}
