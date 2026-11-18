using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TransportSandbox.Terrain;

/// <summary>Parallel chunk backend. Workers own managed buffers; scene uploads happen in Poll.</summary>
public partial class TerrainBackend : RefCounted
{
    private TerrainSampler _sampler = new("the-long-way-home", 1800, 1, 0);
    private readonly Dictionary<Vector2I, Task<ChunkData>> _jobs = new();
    private readonly CancellationTokenSource _shutdown = new();

    public void Configure(string seed, double mountainHeight, double horizontalScale, double seaLevel)
    {
        if (_jobs.Count != 0)
            throw new InvalidOperationException("Cannot change a running terrain snapshot");
        _sampler = new(seed, mountainHeight, horizontalScale, seaLevel);
    }

    public double SampleHeight(double x, double z) => _sampler.Height(x, z);
    public long SeedHash() => _sampler.Seed;
    public int WorkerLimit() => Math.Min(3, Math.Max(1, System.Environment.ProcessorCount - 2));

    public void Request(Vector2I key, int segments)
    {
        if (_shutdown.IsCancellationRequested || _jobs.ContainsKey(key))
            return;
        var sampler = _sampler;
        var token = _shutdown.Token;
        _jobs.Add(key, Task.Run(() => Build(sampler, key, segments, token), token));
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> Poll()
    {
        var results = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var completed = new List<Vector2I>();
        foreach (var (key, job) in _jobs)
        {
            if (!job.IsCompleted)
                continue;
            completed.Add(key);
            if (job.IsCanceled)
                continue;
            if (job.IsFaulted)
            {
                results.Add(new()
                {
                    ["key"] = key,
                    ["error"] = job.Exception!.ToString()
                });
                continue;
            }
            var data = job.Result;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
            arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
            arrays[(int)Mesh.ArrayType.Color] = data.Colors;
            arrays[(int)Mesh.ArrayType.Index] = data.Indices;
            results.Add(new()
            {
                ["key"] = key,
                ["segments"] = data.Segments,
                ["arrays"] = arrays,
                ["trees"] = data.Trees
            });
        }
        foreach (var key in completed)
            _jobs.Remove(key);
        return results;
    }

    public void Shutdown()
    {
        _shutdown.Cancel();
        foreach (var task in _jobs.Values)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { GD.PushError(error.ToString()); }
        }
        _jobs.Clear();
    }

    private sealed record ChunkData(int Segments, Vector3[] Vertices, Vector3[] Normals,
        Color[] Colors, int[] Indices, float[] Trees);

    private static ChunkData Build(TerrainSampler sampler, Vector2I key, int segments, CancellationToken token)
    {
        int width = segments + 1, padded = segments + 3, count = width * width;
        var heights = new double[padded * padded];
        var vertices = new Vector3[count + width * 4];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var indices = new int[(segments * segments * 2 + segments * 8) * 3];
        double step = 512.0 / segments;
        for (int z = 0; z < padded; z++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < padded; x++)
                heights[z * padded + x] = sampler.Height(key.X * 512.0 + (x - 1) * step, key.Y * 512.0 + (z - 1) * step);
        }
        for (int z = 0; z < width; z++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int sample = (z + 1) * padded + x + 1, vertex = z * width + x;
                double h = heights[sample];
                double dx = (heights[sample + 1] - heights[sample - 1]) / (step * 2);
                double dz = (heights[sample + padded] - heights[sample - padded]) / (step * 2);
                double length = Math.Sqrt(dx * dx + 1 + dz * dz);
                vertices[vertex] = new((float)(x * step), (float)h, (float)(z * step));
                normals[vertex] = new((float)(-dx / length), (float)(1 / length), (float)(-dz / length));
                var color = sampler.Color(key.X * 512.0 + x * step, key.Y * 512.0 + z * step, h, Math.Sqrt(dx * dx + dz * dz));
                colors[vertex] = new Color((float)color.R, (float)color.G, (float)color.B).LinearToSrgb();
            }
        }
        int cursor = 0;
        void Triangle(int a, int b, int c)
        {
            indices[cursor++] = a;
            indices[cursor++] = b;
            indices[cursor++] = c;
        }
        for (int z = 0; z < segments; z++)
            for (int x = 0; x < segments; x++)
            {
                int a = z * width + x;
                // Godot uses clockwise front faces, the reverse of Three's index winding.
                Triangle(a, a + 1, a + width);
                Triangle(a + 1, a + width + 1, a + width);
            }
        int Edge(int side, int i) => side switch
        {
            0 => i,
            1 => i * width + segments,
            2 => segments * width + segments - i,
            _ => (segments - i) * width
        };
        for (int side = 0; side < 4; side++)
            for (int i = 0; i < width; i++)
            {
                int source = Edge(side, i), target = count + side * width + i;
                vertices[target] = vertices[source] - Vector3.Up * 180;
                normals[target] = normals[source];
                colors[target] = colors[source];
                if (i < segments)
                {
                    int next = Edge(side, i + 1);
                    Triangle(source, target, next);
                    Triangle(next, target, target + 1);
                }
            }
        // Match browser scheduling: distant tiles omit vegetation, near LODs share identical data.
        var trees = segments >= 64 ? Vegetation.Generate(sampler, key.X, key.Y, token) : Array.Empty<float>();
        return new(segments, vertices, normals, colors, indices, trees);
    }
}
