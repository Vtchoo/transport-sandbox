using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace TransportSandbox.Tests;

public static class TrackArcChecks
{
    // Kept only as a benchmark/reference for the replaced implementation.
    private sealed class FixedCurve
    {
        private readonly TrackVector[] _p;
        private readonly double[] _lengths;
        private readonly bool _horner;
        private readonly TrackVector _linear, _quadratic, _cubic;
        public double Length => _lengths[^1];
        public FixedCurve(TrackVector[] p, int steps = 512, bool horner = false)
        {
            _p = p; _lengths = new double[steps + 1];
            _horner = horner;
            var a = p[1] - p[0]; var b = p[2] - p[0]; var c = p[3] - p[0];
            _linear = a * 3; _quadratic = (b - a * 2) * 3; _cubic = c - b * 3 + a * 3;
            var last = Point(0);
            for (int i = 1; i <= steps; i++)
            {
                var point = Point(i / (double)steps);
                _lengths[i] = _lengths[i - 1] + (point - last).Length;
                last = point;
            }
        }
        private TrackVector Point(double t)
        {
            if (_horner) return new(_p[0].X + ((_cubic.X * t + _quadratic.X) * t + _linear.X) * t,
                _p[0].Y + ((_cubic.Y * t + _quadratic.Y) * t + _linear.Y) * t,
                _p[0].Z + ((_cubic.Z * t + _quadratic.Z) * t + _linear.Z) * t);
            double u = 1 - t;
            return _p[0] * (u * u * u) + _p[1] * (3 * u * u * t)
                + _p[2] * (3 * u * t * t) + _p[3] * (t * t * t);
        }
        public TrackVector PointAt(double u)
        {
            double target = u * Length;
            int low = 0, high = _lengths.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (_lengths[middle] <= target) low = middle; else high = middle;
            }
            double span = _lengths[high] - _lengths[low];
            double t = span <= 0 ? low : low + (target - _lengths[low]) / span;
            return Point(t / (_lengths.Length - 1));
        }
    }

    public static void Run(Action<bool, string> check, string? directory = null)
    {
        var special = new[]
        {
            new[] { new TrackVector(0,0,0), new(100,0,0), new(200,0,0), new(300,0,0) },
            new[] { new TrackVector(0,0,0), new(1,0,0), new(999,0,0), new(1000,0,0) },
            new[] { new TrackVector(0,0,0), new(0,0,0), new(0,0,0), new(1000,0,0) },
            new[] { new TrackVector(0,0,0), new(100,500,300), new(-100,-500,300), new(0,0,600) },
            new[] { new TrackVector(0,0,0), new(100,0,0), new(-100,0,0), new(0,0,0) },
            new[] { new TrackVector(0,0,0), new(0,0,0), new(0,0,0), new(0,0,0) },
        };
        TrackVector[][] saved = Array.Empty<TrackVector[]>();
        if (directory != null)
        {
            var save = WorldSaveStore.Read(ProjectSettings.GlobalizePath(directory));
            var network = new TrackNetwork(); network.Restore(save);
            saved = network.Edges.Values.Select(e => network.PreciseControls(e)).ToArray();
        }
        double lengthError = 0, positionError = 0, legacyLengthError = 0, legacyPositionError = 0;
        double savedLengthError = 0, savedPositionError = 0, savedLegacyLengthError = 0, savedLegacyPositionError = 0;
        var savedSet = saved.ToHashSet();
        var fixtures = JsonData.Read("res://tests/track-geometry-fixtures.json").EnumerateArray()
            .Select(f => f.GetProperty("points").EnumerateArray().Select(p => TrackVector.From(JsonData.Vector(p))).ToArray());
        var random = new Random(17429);
        var adversarial = Enumerable.Range(0, 32).Select(_ => Enumerable.Range(0, 4)
            .Select(_ => new TrackVector((random.NextDouble() - .5) * 10000,
                (random.NextDouble() - .5) * 10000, (random.NextDouble() - .5) * 10000)).ToArray());
        foreach (var controls in special.Concat(saved).Concat(fixtures).Concat(adversarial))
        {
            var curve = new TrackCurve(controls);
            var reference = new FixedCurve(controls, 131072);
            var legacy = new FixedCurve(controls);
            lengthError = Math.Max(lengthError, Math.Abs(curve.Length - reference.Length));
            legacyLengthError = Math.Max(legacyLengthError, Math.Abs(legacy.Length - reference.Length));
            if (savedSet.Contains(controls))
            {
                savedLengthError = Math.Max(savedLengthError, Math.Abs(curve.Length - reference.Length));
                savedLegacyLengthError = Math.Max(savedLegacyLengthError, Math.Abs(legacy.Length - reference.Length));
            }
            check(Math.Abs(curve.Length - TrackArcTable.Measure(controls[0], controls[1], controls[2], controls[3])) < 1e-10,
                "routing and placement use identical arc integration");
            for (int i = 0; i <= 200; i++)
            {
                double u = i / 200.0;
                positionError = Math.Max(positionError, (curve.PointAt(u) - reference.PointAt(u)).Length);
                legacyPositionError = Math.Max(legacyPositionError, (legacy.PointAt(u) - reference.PointAt(u)).Length);
                if (savedSet.Contains(controls))
                {
                    savedPositionError = Math.Max(savedPositionError, (curve.PointAt(u) - reference.PointAt(u)).Length);
                    savedLegacyPositionError = Math.Max(savedLegacyPositionError, (legacy.PointAt(u) - reference.PointAt(u)).Length);
                }
            }
        }
        check(lengthError < .0003, $"adaptive length error <= 0.3 mm ({lengthError:R} m)");
        check(positionError < .001, $"adaptive position error <= 1 mm ({positionError:R} m)");
        check(new TrackCurve(special[0]).SampleCount == 2, "uniform straight needs only endpoints");
        check(new TrackCurve(special[1]).SampleCount > 2, "flat nonuniform cubic still refines distance mapping");
        var invalid = special[0].ToArray(); invalid[1] = new(double.NaN, 0, 0);
        bool rejected = false;
        try { _ = new TrackCurve(invalid); } catch (ArgumentException) { rejected = true; }
        check(rejected, "nonfinite controls rejected before subdivision");
        var translated = special[3].Select(p => p + new TrackVector(1e8, -1e8, 1e8)).ToArray();
        check(Math.Abs(new TrackCurve(translated).Length - new TrackCurve(special[3]).Length) < 1e-8,
            "arc integration is translation invariant");
        if (saved.Length == 0) return;

        var curves = saved.Select(p => new TrackCurve(p)).ToArray();
        var old = saved.Select(p => new FixedCurve(p)).ToArray();
        var fastFixed = saved.Select(p => new FixedCurve(p, horner: true)).ToArray();
        double sink = 0;
        (double ms, long bytes) Time(Action action, int repeats)
        {
            action(); // JIT warmup
            long allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++) action();
            return (timer.Elapsed.TotalMilliseconds / repeats, (GC.GetAllocatedBytesForCurrentThread() - allocated) / repeats);
        }
        var buildOld = Time(() => { foreach (var p in saved) sink += new FixedCurve(p).Length; }, 30);
        var buildNew = Time(() => { foreach (var p in saved) sink += new TrackCurve(p).Length; }, 30);
        var queryOld = Time(() => { foreach (var c in old) for (int i=0;i<1024;i++) sink += c.PointAt(i / 1023.0).X; }, 10);
        var queryNew = Time(() => { foreach (var c in curves) for (int i=0;i<1024;i++) sink += c.PointAt(i / 1023.0).X; }, 10);
        var queryFixedHorner = Time(() => { foreach (var c in fastFixed) for (int i=0;i<1024;i++) sink += c.PointAt(i / 1023.0).X; }, 10);
        var result = new { segments = saved.Length, samples = curves.Sum(c => c.SampleCount),
            fixedSamples = saved.Length * 513, minSamples = curves.Min(c => c.SampleCount), maxSamples = curves.Max(c => c.SampleCount),
            lengthError, positionError, legacyLengthError, legacyPositionError,
            savedLengthError, savedPositionError, savedLegacyLengthError, savedLegacyPositionError,
            buildOldMs = buildOld.ms, buildNewMs = buildNew.ms, buildOldBytes = buildOld.bytes, buildNewBytes = buildNew.bytes,
            queryOldMs = queryOld.ms, queryNewMs = queryNew.ms, queryFixedHornerMs = queryFixedHorner.ms, sink };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        GD.Print(json);
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(directory + "/arc-benchmark.json"), json);
    }
}
