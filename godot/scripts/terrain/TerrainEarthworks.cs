using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TransportSandbox;
using TransportSandbox.Terrain;

public partial class TerrainBackend
{
    /// <summary>Original adaptive earthwork mesh: 1 m leaves, stitched edge fans, exposed soil and skirts.</summary>
    private static ChunkData BuildEarthwork(TerrainSampler sampler, Vector2I key, int segments, CancellationToken token, EarthworkField field, long revision, bool vegetation)
    {
        double ox = key.X * 512.0, oz = key.Y * 512.0;
        // Exact per-job memoization: neighbouring normal samples and quadtree
        // corners repeatedly ask for the same heights. Caps bound worker memory.
        const int maxCachedSamples = 65536;
        var naturalHeights = new Dictionary<(double X, double Z), double>();
        var resolvedHeights = new Dictionary<(double X, double Z), double>();
        double Natural(double x, double z)
        {
            if (naturalHeights.TryGetValue((x, z), out double value)) return value;
            value = sampler.Height(x, z);
            if (naturalHeights.Count < maxCachedSamples) naturalHeights[(x, z)] = value;
            return value;
        }
        var leaves = new List<(double X, double Z, double Size)>();
        var horizontal = new Dictionary<double, SortedSet<double>>();
        var vertical = new Dictionary<double, SortedSet<double>>();
        void Corner(double x, double z)
        {
            if (!horizontal.TryGetValue(z, out var row))
                horizontal[z] = row = new();
            if (!vertical.TryGetValue(x, out var column))
                vertical[x] = column = new();
            row.Add(x);
            column.Add(z);
        }
        void Subdivide(double x, double z, double size)
        {
            token.ThrowIfCancellationRequested();
            if (size > 1 && field.AffectsBounds(ox + x, oz + z, size, Natural))
            {
                double half = size / 2;
                Subdivide(x, z, half);
                Subdivide(x + half, z, half);
                Subdivide(x, z + half, half);
                Subdivide(x + half, z + half, half);
            }
            else
            {
                leaves.Add((x, z, size));
                Corner(x, z);
                Corner(x + size, z);
                Corner(x, z + size);
                Corner(x + size, z + size);
            }
        }
        double step = 512.0 / segments;
        using (EarthworkProbe.Measure("mesh.subdivision", key.X, key.Y))
        for (double z = 0; z < 512; z += step)
        for (double x = 0; x < 512; x += step)
            Subdivide(x, z, step);
        var positions = new List<TrackVector>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        var vertices = new Dictionary<(double, double), int>();
        double Height(double x, double z)
        {
            if (resolvedHeights.TryGetValue((x, z), out double value)) return value;
            value = field.Height(x, z, Natural(x, z));
            if (resolvedHeights.Count < maxCachedSamples) resolvedHeights[(x, z)] = value;
            return value;
        }
        int Vertex(double x, double z)
        {
            if (vertices.TryGetValue((x, z), out int previous))
                return previous;
            double wx = ox + x, wz = oz + z, h = Height(wx, wz);
            double dx = (Height(wx + 1, wz) - Height(wx - 1, wz)) / 2, dz = (Height(wx, wz + 1) - Height(wx, wz - 1)) / 2;
            double length = Math.Sqrt(dx * dx + 1 + dz * dz);
            int index = positions.Count;
            positions.Add(new(x, h, z));
            normals.Add(new((float)(-dx / length), (float)(1 / length), (float)(-dz / length)));
            var c = Math.Abs(h - Natural(wx, wz)) > .2 ? (.26, .19, .12) : sampler.Color(wx, wz, h, Math.Sqrt(dx * dx + dz * dz));
            colors.Add(new Color((float)c.Item1, (float)c.Item2, (float)c.Item3).LinearToSrgb());
            vertices[(x, z)] = index;
            return index;
        }
        void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(c);
            indices.Add(b);
        }
        using (EarthworkProbe.Measure("mesh.vertices-stitch", key.X, key.Y))
        foreach (var (x, z, size) in leaves)
        {
            token.ThrowIfCancellationRequested();
            var boundary = new List<int>();
            // Enumerate this leaf's edge interval rather than scanning the entire
            // chunk row/column for every leaf. Preserve winding and exclusive ends.
            boundary.AddRange(vertical[x].GetViewBetween(z, z + size).Where(v => v < z + size).Select(v => Vertex(x, v)));
            boundary.AddRange(horizontal[z + size].GetViewBetween(x, x + size).Where(v => v < x + size).Select(v => Vertex(v, z + size)));
            boundary.AddRange(vertical[x + size].GetViewBetween(z, z + size).Reverse().Where(v => v > z).Select(v => Vertex(x + size, v)));
            boundary.AddRange(horizontal[z].GetViewBetween(x, x + size).Reverse().Where(v => v > x).Select(v => Vertex(v, z)));
            int center = Vertex(x + size / 2, z + size / 2);
            for (int i = 0; i < boundary.Count; i++)
                Triangle(center, boundary[i], boundary[(i + 1) % boundary.Count]);
        }
        var edges = new[]{horizontal[0].Select(x=>Vertex(x,0)).ToArray(),vertical[512].Select(z=>Vertex(512,z)).ToArray(),
            horizontal[512].Reverse().Select(x=>Vertex(x,512)).ToArray(),vertical[0].Reverse().Select(z=>Vertex(0,z)).ToArray()};
        foreach (var edge in edges)
        {
            var bottom = new int[edge.Length];
            for (int i = 0; i < edge.Length; i++)
            {
                int source = edge[i];
                bottom[i] = positions.Count;
                var p = positions[source];
                positions.Add(p with
                {
                    Y = p.Y - 180
                });
                normals.Add(normals[source]);
                colors.Add(colors[source]);
            }
            for (int i = 0; i < edge.Length - 1; i++)
            {
                Triangle(edge[i], edge[i + 1], bottom[i]);
                Triangle(edge[i + 1], bottom[i + 1], bottom[i]);
            }
        }
        var trees = vegetation && segments >= 32 ? Vegetation.Generate(sampler, key.X, key.Y, token) : Array.Empty<float>();
        return new(segments, positions.Select(p => p.Float()).ToArray(), normals.ToArray(), colors.ToArray(), indices.ToArray(), trees, revision);
    }
}
