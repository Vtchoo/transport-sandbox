using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TransportSandbox.Terrain;

public readonly record struct EarthPoint(double X, double Y, double Z, double Nx = 0, double Nz = 1,
    double Left = double.PositiveInfinity, double Right = double.PositiveInfinity);
public readonly record struct EarthHit(double Distance, double RailHeight, double Width);

/// <summary>Immutable port of earthworks.ts: first-ground-intersection 2:1 shoulders and nearest-path BVH.</summary>
public sealed class EarthworkField
{
    private sealed record Edge(EarthPoint A, EarthPoint B)
    {
        public double MinX => Math.Min(A.X, B.X);
        public double MaxX => Math.Max(A.X, B.X);
        public double MinZ => Math.Min(A.Z, B.Z);
        public double MaxZ => Math.Max(A.Z, B.Z);
    }
    private sealed class Branch
    {
        public double MinX, MaxX, MinZ, MaxZ;
        public Branch? Left, Right;
        public Edge[]? Edges;
        public double DistanceSquared(double x, double z) => Math.Pow(Math.Max(Math.Max(MinX - x, 0), x - MaxX), 2)
            + Math.Pow(Math.Max(Math.Max(MinZ - z, 0), z - MaxZ), 2);
    }
    private readonly Branch? _root;
    private readonly Edge[] _edges;
    public EarthworkField(IEnumerable<EarthPoint[]> paths)
    {
        var edges = paths.SelectMany(path => Enumerable.Range(1, Math.Max(0, path.Length - 1)).Select(i => new Edge(path[i - 1], path[i]))).ToArray();
        _edges = edges;
        if (edges.Length > 0)
            _root = Build(edges);
    }
    /// <summary>Stable local cache identity, independent of transient revision numbers and floating origins.</summary>
    public string ChunkSignature(int x, int z)
    {
        using var bytes = new System.IO.MemoryStream(); using var writer = new System.IO.BinaryWriter(bytes);
        double ox = x * 512.0, oz = z * 512.0;
        foreach (var e in _edges)
        {
            double width = new[] { e.A.Left, e.A.Right, e.B.Left, e.B.Right }.Max();
            if (e.MaxX + width < ox - 34 || e.MinX - width > ox + 546 || e.MaxZ + width < oz - 34 || e.MinZ - width > oz + 546) continue;
            foreach (var p in new[] { e.A, e.B })
            { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); writer.Write(p.Nx); writer.Write(p.Nz); writer.Write(p.Left); writer.Write(p.Right); }
        }
        return bytes.Length == 0 ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
    }
    private static Branch Build(Edge[] edges)
    {
        var branch = new Branch { MinX = edges.Min(e => e.MinX), MaxX = edges.Max(e => e.MaxX), MinZ = edges.Min(e => e.MinZ), MaxZ = edges.Max(e => e.MaxZ) };
        if (edges.Length <= 8)
            branch.Edges = edges;
        else
        {
            bool x = branch.MaxX - branch.MinX >= branch.MaxZ - branch.MinZ;
            var sorted = edges.OrderBy(e => x ? e.A.X + e.B.X : e.A.Z + e.B.Z).ToArray();
            int half = sorted.Length / 2;
            branch.Left = Build(sorted[..half]);
            branch.Right = Build(sorted[half..]);
        }
        return branch;
    }
    public static EarthPoint[] Prepare(EarthPoint[] path, Func<double, double, double> natural, double mountainHeight, CancellationToken token = default)
    {
        var result = new EarthPoint[path.Length];
        for (int i = 0; i < path.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var point = path[i];
            var before = path[Math.Max(0, i - 1)];
            var after = path[Math.Min(path.Length - 1, i + 1)];
            double dx = after.X - before.X, dz = after.Z - before.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (length == 0)
                length = 1;
            double nx = -dz / length, nz = dx / length, bed = point.Y - .65;
            double Width(int side)
            {
                double At(double d) => natural(point.X + nx * d * side, point.Z + nz * d * side);
                bool fill = bed >= At(2);
                bool Inside(double d) => fill ? At(d) < bed - (d - 2) / 2 : At(d) > bed + (d - 2) / 2;
                double limit = 2 + 2 * Math.Max(Math.Abs(bed + 700), Math.Abs(bed - mountainHeight - 600)), previous = 2;
                for (double d = 3; d <= limit + 20; d += Math.Min(8, Math.Max(1, d * .02)))
                {
                    token.ThrowIfCancellationRequested();
                    if (!Inside(d))
                    {
                        double low = previous, high = d;
                        for (int j = 0; j < 12; j++)
                        {
                            double mid = (low + high) / 2;
                            if (Inside(mid))
                                low = mid;
                            else
                                high = mid;
                        }
                        return (low + high) / 2;
                    }
                    previous = d;
                }
                return limit;
            }
            result[i] = point with
            {
                Nx = nx,
                Nz = nz,
                Left = Width(-1),
                Right = Width(1)
            };
        }
        return result;
    }
    public EarthHit Nearest(double x, double z)
    {
        double distanceSq = double.PositiveInfinity, railHeight = 0, width = double.PositiveInfinity;
        void Visit(Branch branch)
        {
            if (branch.DistanceSquared(x, z) > distanceSq)
                return;
            if (branch.Edges is { } edges)
            {
                foreach (var e in edges)
                {
                    double dx = e.B.X - e.A.X, dz = e.B.Z - e.A.Z, denominator = dx * dx + dz * dz;
                    double t = denominator > 1e-12 ? Math.Clamp(((x - e.A.X) * dx + (z - e.A.Z) * dz) / denominator, 0, 1) : 0;
                    double px = x - e.A.X - t * dx, pz = z - e.A.Z - t * dz, d = px * px + pz * pz;
                    if (d >= distanceSq)
                        continue;
                    distanceSq = d;
                    railHeight = e.A.Y + (e.B.Y - e.A.Y) * t;
                    double nx = e.A.Nx + (e.B.Nx - e.A.Nx) * t, nz = e.A.Nz + (e.B.Nz - e.A.Nz) * t;
                    bool right = px * nx + pz * nz >= 0;
                    double a = right ? e.A.Right : e.A.Left, b = right ? e.B.Right : e.B.Left;
                    width = double.IsFinite(a) && double.IsFinite(b) ? a + (b - a) * t : double.PositiveInfinity;
                }
            }
            else if (branch.Left is { } left && branch.Right is { } right)
            {
                bool first = left.DistanceSquared(x, z) < right.DistanceSquared(x, z);
                Visit(first ? left : right);
                Visit(first ? right : left);
            }
        }
        if (_root != null)
            Visit(_root);
        return new(Math.Sqrt(distanceSq), railHeight, width);
    }
    public double Height(double x, double z, double natural)
    {
        var hit = Nearest(x, z);
        if (!double.IsFinite(hit.Distance) || hit.Distance > hit.Width)
            return natural;
        double bed = hit.RailHeight - .65, rise = Math.Max(0, hit.Distance - 2) / 2;
        return Math.Max(bed - rise, Math.Min(natural, bed + rise));
    }
    public bool ClearsTree(double x, double z, double natural)
    {
        var hit = Nearest(x, z);
        return hit.Distance < 8 || (double.IsFinite(hit.Width) ? hit.Distance < hit.Width + 4 : Math.Abs(Height(x, z, natural) - natural) > .05);
    }
    public bool AffectsBounds(double x, double z, double size, Func<double, double, double> natural)
    {
        if (_root == null)
            return false;
        if (Nearest(x + size / 2, z + size / 2).Distance <= size * Math.Sqrt(.5) + 3)
            return true;
        for (int i = 0; i < 5; i++)
        {
            double dx = i == 4 ? size / 2 : i % 2 == 1 ? size : 0;
            double dz = i == 4 ? size / 2 : i >= 2 ? size : 0;
            double baseHeight = natural(x + dx, z + dz);
            if (Math.Abs(Height(x + dx, z + dz, baseHeight) - baseHeight) > .02)
                return true;
        }
        return false;
    }
}
