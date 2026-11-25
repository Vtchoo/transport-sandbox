using Godot;
using System;
using System.Linq;
using System.Text.Json;
using TransportSandbox.Terrain;

namespace TransportSandbox.Tests;

public static class ClearanceChecks
{
    private static EarthPoint Point(JsonElement p) => new(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), p.GetProperty("z").GetDouble());
    private static EarthPoint[][] Paths(JsonElement paths) => paths.EnumerateArray().Select(p => p.GetProperty("points").EnumerateArray().Select(Point).ToArray()).ToArray();
    public static void Run(Action<bool, string> check)
    {
        var fixture = JsonData.Read("res://tests/clearance-fixtures.json");
        var settings = JsonData.Settings(fixture.GetProperty("settings"));
        var sampler = new TerrainSampler(settings.Seed, settings.MountainHeight, settings.HorizontalScale, settings.SeaLevel);
        foreach (var sample in fixture.GetProperty("cases").EnumerateArray())
        {
            string ground = sample.GetProperty("ground").GetString()!;
            double Natural(double x, double z) => ground == "flat" ? 20 : ground == "valley" ? 20 + Math.Sin(x / 15) * 12 + Math.Cos(z / 31) * 18 : sampler.Height(x, z);
            var controls = sample.GetProperty("controls").EnumerateArray().Select(p => new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
            var raw = TrackTreeClearance.SamplePath(controls);
            var expectedRaw = sample.GetProperty("raw");
            check(raw.Length == expectedRaw.GetArrayLength(), "Earthwork paths use original two-metre arc sampling");
            for (int i = 0; i < Math.Min(raw.Length, expectedRaw.GetArrayLength()); i++)
            {
                var expected = Point(expectedRaw[i]);
                // The adaptive distance table intentionally improves on the fixed-512 fixture.
                check(Math.Abs(raw[i].X - expected.X) < .001 && Math.Abs(raw[i].Y - expected.Y) < .001 && Math.Abs(raw[i].Z - expected.Z) < .001, "Earthwork formation within 1 mm of original pitched ballast underside");
            }
            var prepared = Paths(sample.GetProperty("paths")).Select(p => EarthworkField.Prepare(p, Natural, settings.MountainHeight)).ToArray();
            var expectedPaths = sample.GetProperty("prepared");
            for (int p = 0; p < prepared.Length; p++)
            for (int i = 0; i < prepared[p].Length; i++)
            {
                var expected = expectedPaths[p].GetProperty("points")[i];
                var point = prepared[p][i];
                check(Math.Abs(point.Left - expected.GetProperty("left").GetDouble()) < 1e-8 && Math.Abs(point.Right - expected.GetProperty("right").GetDouble()) < 1e-8, "Cut/fill shoulders stop at original first-ground intersection");
            }
            var field = new EarthworkField(prepared);
            foreach (var query in sample.GetProperty("queries").EnumerateArray())
            {
                double x = query.GetProperty("x").GetDouble(), z = query.GetProperty("z").GetDouble(), natural = query.GetProperty("natural").GetDouble();
                var hit = field.Nearest(x, z);
                check(Math.Abs(hit.Distance - query.GetProperty("distance").GetDouble()) < 1e-8 && Math.Abs(hit.Width - query.GetProperty("width").GetDouble()) < 1e-8 && Math.Abs(hit.RailHeight - query.GetProperty("railHeight").GetDouble()) < 1e-8, "BVH nearest-path queries match original");
                check(Math.Abs(field.Height(x, z, natural) - query.GetProperty("height").GetDouble()) < 1e-8 && field.ClearsTree(x, z, natural) == query.GetProperty("clears").GetBoolean(), "Original 8 m track / shoulder plus 4 m clearance rules match");
            }
        }
        var vegetation = fixture.GetProperty("vegetation");
        var earth = new EarthworkField(Paths(vegetation.GetProperty("paths")).Select(p => EarthworkField.Prepare(p, sampler.Height, settings.MountainHeight)));
        var source = Vegetation.Generate(sampler, 1, 1);
        var kept = new System.Collections.Generic.List<float>();
        for (int i = 0; i < source.Length; i += 7)
        {
            var point = Vegetation.WorldPosition(sampler, 1, 1, source[i], source[i + 2]);
            if (!earth.ClearsTree(point.X, point.Z, source[i + 1]))
                kept.AddRange(source[i..(i + 7)]);
        }
        var cleared = vegetation.GetProperty("cleared");
        check(kept.Count < source.Length && kept.Count == cleared.GetArrayLength() && kept.Select((value, i) => value == cleared[i].GetSingle()).All(equal => equal), "Every retained natural tree matches original earthwork clearance");
        check(!new EarthworkField(Array.Empty<EarthPoint[]>()).ClearsTree(0, 0, 0), "Deleting all track restores natural tree eligibility");
    }
}
