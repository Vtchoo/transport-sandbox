using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public readonly record struct TrackVector(double X, double Y, double Z)
{
    public static TrackVector From(Vector3 v) => new(v.X, v.Y, v.Z);
    public Vector3 Float() => new((float)X, (float)Y, (float)Z);
    public static TrackVector operator +(TrackVector a, TrackVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static TrackVector operator -(TrackVector a, TrackVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static TrackVector operator *(TrackVector a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);
    public TrackVector Unit() => Length == 0 ? this : this * (1 / Length);
    public TrackVector Cross(TrackVector b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
}

public readonly record struct TrackFrame(TrackVector Right, TrackVector Up, TrackVector Forward)
{
    public Basis Basis => new(Right.Float(), Up.Float(), Forward.Float());
    public static TrackFrame From(TrackVector tangent)
    {
        var forward = tangent.Unit();
        if (forward.LengthSquared < 1e-12)
            forward = new(0, 0, 1);
        var right = new TrackVector(0, 1, 0).Cross(forward);
        right = right.LengthSquared < 1e-12 ? new(1, 0, 0) : right.Unit();
        return new(right, forward.Cross(right).Unit(), forward);
    }
}

/// <summary>Immutable, error-controlled cubic arc table. All distances are metres.</summary>
public sealed class TrackCurve
{
    public TrackVector[] Controls { get; }
    private readonly double[] _parameters, _lengths;
    private readonly TrackVector _linear, _quadratic, _cubic;
    public double Length => _lengths[^1];
    internal int SampleCount => _lengths.Length;
    public TrackCurve(Vector3[] controls) : this(controls.Select(TrackVector.From).ToArray()) { }
    public TrackCurve(TrackVector[] controls)
    {
        if (controls.Length != 4)
            throw new ArgumentException("A cubic track requires four control points.");
        Controls = controls.ToArray();
        var a = Controls[1] - Controls[0]; var b = Controls[2] - Controls[0]; var c = Controls[3] - Controls[0];
        _linear = a * 3;
        _quadratic = (b - a * 2) * 3;
        _cubic = c - b * 3 + a * 3;
        var table = TrackArcTable.Build(Controls[0], Controls[1], Controls[2], Controls[3]);
        _parameters = table.Parameters;
        _lengths = table.Lengths;
    }
    public TrackVector Point(double t)
    {
        return new(Controls[0].X + ((_cubic.X * t + _quadratic.X) * t + _linear.X) * t,
            Controls[0].Y + ((_cubic.Y * t + _quadratic.Y) * t + _linear.Y) * t,
            Controls[0].Z + ((_cubic.Z * t + _quadratic.Z) * t + _linear.Z) * t);
    }
    private double Map(double u)
    {
        if (u <= 0) return 0;
        if (u >= 1) return 1;
        if (Length <= 1e-12) return u;
        double target = u * Length;
        int low = 0, high = _lengths.Length - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (_lengths[middle] <= target) low = middle;
            else high = middle;
        }
        double span = _lengths[high] - _lengths[low];
        return span <= 0 ? _parameters[low] : _parameters[low]
            + (target - _lengths[low]) / span * (_parameters[high] - _parameters[low]);
    }
    public TrackVector PointAt(double u) => Point(Map(u));
    public TrackVector Tangent(double t) => (Point(Math.Min(1, t + .0001)) - Point(Math.Max(0, t - .0001))).Unit();
    public TrackFrame FrameAt(double u) => TrackFrame.From(Tangent(Map(u)));
}

public sealed record TrackSurface(string Name, Vector3[] Vertices, Vector3[] Normals, int[] Indices)
{
    public ArrayMesh CreateMesh()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = Normals;
        arrays[(int)Mesh.ArrayType.Index] = Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
public sealed record SleeperBatch(Transform3D[] Transforms, Vector3 Center, int[] Counts)
{
    public int Level
    {
        get; private set;
    }
    private static readonly double[] Distances = { 150, 400, 900, 1600 };
    public int Update(double distance)
    {
        while (Level < 4 && distance > Distances[Level] * 1.1)
            Level++;
        while (Level > 0 && distance < Distances[Level - 1] * .9)
            Level--;
        return Counts[Level];
    }
}

/// <summary>Direct port of the original swept profiles and sleeper placement. Rail head is Y=0.</summary>
public sealed class TrackGeometry
{
    public TrackCurve Curve
    {
        get;
    }
    public Vector3 Anchor
    {
        get;
    }
    public int Gauge
    {
        get;
    }
    public IReadOnlyList<TrackSurface> Surfaces
    {
        get;
    }
    public IReadOnlyList<SleeperBatch> Sleepers
    {
        get;
    }
    public Vector3 SleeperSize => new(Gauge / 1000f + .85f, .18f, .24f);
    public TrackGeometry(Vector3[] controls, int gauge, IEnumerable<int>? platforms = null, bool preview = false, bool railsOnly = false, bool platformsOnly = false)
    {
        Curve = new(controls);
        Gauge = gauge;
        Anchor = controls[0];
        var surfaces = new List<TrackSurface>();
        int steps = Math.Max(2, (int)Math.Ceiling(Curve.Length / 1.5));
        if (!railsOnly && !platformsOnly)
            surfaces.Add(Sweep("Ballast", steps, new[] { (-2.0, -.65), (-1.36, -.33), (1.36, -.33), (2.0, -.65) }));
        foreach (int sign in platformsOnly ? Array.Empty<int>() : new[] { -1, 1 })
        {
            double offset = sign * (gauge / 2000.0 + .035);
            surfaces.Add(Sweep(sign < 0 ? "RailLeft" : "RailRight", steps, new[] { (offset - .035, 0.0), (offset + .035, 0.0), (offset + .035, -.15), (offset - .035, -.15) }));
        }
        foreach (int side in platforms ?? Array.Empty<int>())
        {
            double edge = Math.Max(1.75, gauge / 2000.0 + 1.05);
            int count = Math.Max(2, (int)Math.Ceiling(Curve.Length / 2));
            surfaces.Add(Sweep($"Platform_{side}", count, new[] { (edge, -.65), (edge + 3, -.65), (edge + 3, .45), (edge, .45) }, side, true));
            surfaces.Add(Sweep($"PlatformEdge_{side}", count, new[] { (edge, .451), (edge + .22, .451), (edge + .22, .465), (edge, .465) }, side, true));
        }
        Surfaces = surfaces;
        var sleepers = new List<SleeperBatch>();
        if (!preview && !railsOnly && !platformsOnly)
        {
            int count = Math.Max(1, (int)Math.Floor(Curve.Length / .65));
            for (int start = 0; start < count; start += 64)
            {
                int size = Math.Min(64, count - start);
                int Priority(int i) => i % 8 == 0 ? 0 : i % 4 == 0 ? 1 : i % 2 == 0 ? 2 : 3;
                var order = Enumerable.Range(start, size).OrderBy(Priority).ThenBy(i => i).ToArray();
                var transforms = order.Select(i =>
                {
                    double t = (i + .5) / count;
                    var frame = Curve.FrameAt(t);
                    return new Transform3D(frame.Basis, (Curve.PointAt(t) - Curve.Controls[0] + frame.Up * -.24).Float());
                }).ToArray();
                sleepers.Add(new(transforms, (Curve.PointAt((start + size / 2.0) / count) - Curve.Controls[0]).Float(),
                    new[] { size, order.Count(i => i % 2 == 0), order.Count(i => i % 4 == 0), order.Count(i => i % 8 == 0), 0 }));
            }
        }
        Sleepers = sleepers;
    }
    private TrackSurface Sweep(string name, int steps, (double X, double Y)[] profile, int side = 1, bool platform = false)
    {
        int width = profile.Length;
        var vertices = new Vector3[(steps + 1) * width];
        var sourceIndices = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            var point = Curve.PointAt(t) - Curve.Controls[0];
            var frame = Curve.FrameAt(t);
            for (int j = 0; j < width; j++)
            {
                var (x, y) = profile[j];
                var position = point + frame.Right * (x * side);
                position += platform ? new TrackVector(0, y, 0) : frame.Up * y;
                vertices[i * width + j] = position.Float();
            }
        }
        for (int i = 0; i < steps; i++)
            for (int j = 0; j < width; j++)
            {
                int a = i * width + j, b = i * width + (j + 1) % width, c = a + width, d = b + width;
                sourceIndices.AddRange(new[] { a, b, c, b, d, c });
            }
        if (platform)
            foreach (int first in new[] { 0, steps * width })
                sourceIndices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        var normals = new Vector3[vertices.Length];
        for (int i = 0; i < sourceIndices.Count; i += 3)
        {
            int a = sourceIndices[i], b = sourceIndices[i + 1], c = sourceIndices[i + 2];
            var p = TrackVector.From(vertices[a]);
            var q = TrackVector.From(vertices[b]);
            var r = TrackVector.From(vertices[c]);
            var normal = (r - q).Cross(p - q);
            foreach (int index in new[] { a, b, c })
                normals[index] = (TrackVector.From(normals[index]) + normal).Float();
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = TrackVector.From(normals[i]).Unit().Float();
        // Three uses counterclockwise faces; Godot uses clockwise faces.
        var indices = sourceIndices.ToArray();
        for (int i = 0; i < indices.Length; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        return new(name, vertices, normals, indices);
    }
}
