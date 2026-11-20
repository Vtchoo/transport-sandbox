using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TransportSandbox.Terrain;
using TransportSandbox;

/// <summary>Parallel chunk backend. Workers own managed buffers; scene uploads happen in Poll.</summary>
public partial class TerrainBackend : RefCounted
{
    private TerrainSampler _sampler = new("the-long-way-home", 1800, 1, 0);
    private readonly Dictionary<Vector2I, Task<ChunkData>> _jobs = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _vegetationLock = new();
    private readonly Dictionary<Vector2I, Lazy<(float[] Raw, PreparedTrees Prepared)>> _vegetation = new();
    private readonly Queue<Vector2I> _vegetationOrder = new();
    private readonly Dictionary<Vector2I, long> _vegetationSizes = new();
    private long _vegetationBytes;
    private readonly Dictionary<Vector2I, PreparedTrees> _completedTrees = new();
    public bool ProvidePreparedTrees;
    public TerrainRegionStore? Regions;
    private long _meshGenerations;
    public long MeshGenerations => Interlocked.Read(ref _meshGenerations);
    private long _naturalTreeGenerations;
    public long NaturalTreeGenerations => Interlocked.Read(ref _naturalTreeGenerations);
    public long VegetationGenerations
    {
        get; private set;
    }
    public PreparedTrees? TakePreparedTrees(Vector2I key) => _completedTrees.Remove(key, out var value) ? value : null;
    private (float[] Raw, PreparedTrees Prepared) VegetationFor(Vector2I key, CancellationToken token)
    {
        Lazy<(float[], PreparedTrees)> value;
        lock (_vegetationLock)
        {
            if (!_vegetation.TryGetValue(key, out value!))
            {
                value = new(() =>
                {
                    var saved = Regions?.Read(key.X, key.Y, 0, "natural-trees");
                    float[] raw;
                    if (saved != null)
                    { raw = new float[saved.Length / 4]; Buffer.BlockCopy(saved, 0, raw, 0, saved.Length); }
                    else
                    {
                        raw = Vegetation.Generate(_sampler, key.X, key.Y, token);
                        Interlocked.Increment(ref _naturalTreeGenerations);
                        var bytes = new byte[raw.Length * 4]; Buffer.BlockCopy(raw, 0, bytes, 0, bytes.Length);
                        Regions?.Write(key.X, key.Y, 0, "natural-trees", bytes);
                    }
                    var trees = new TreeData[raw.Length / 7];
                    for (int i = 0; i < trees.Length; i++)
                    {
                        int j = i * 7;
                        trees[i] = new(raw[j], raw[j + 1], raw[j + 2], raw[j + 3], raw[j + 4], raw[j + 5], (int)raw[j + 6]);
                    }
                    return (raw, TreeScenery.Prepare(trees));
                });
                _vegetation[key] = value;
                _vegetationOrder.Enqueue(key);
                VegetationGenerations++;
            }
        }
        var result = value.Value;
        lock (_vegetationLock)
        {
            if (_vegetation.TryGetValue(key, out var current) && ReferenceEquals(current, value) && !_vegetationSizes.ContainsKey(key))
            {
                long bytes = result.Item1.LongLength * 4 + result.Item2.EstimatedBytes;
                _vegetationSizes[key] = bytes;
                _vegetationBytes += bytes;
            }
            while ((_vegetation.Count > 128 || _vegetationBytes > 32L * 1024 * 1024) && _vegetationOrder.TryDequeue(out var oldest))
            {
                _vegetation.Remove(oldest);
                if (_vegetationSizes.Remove(oldest, out long bytes))
                    _vegetationBytes -= bytes;
            }
        }
        return result;
    }

    public void Configure(string seed, double mountainHeight, double horizontalScale, double seaLevel)
    {
        if (_jobs.Count != 0)
            throw new InvalidOperationException("Cannot change a running terrain snapshot");
        _sampler = new(seed, mountainHeight, horizontalScale, seaLevel);
        lock (_vegetationLock)
        {
            _vegetation.Clear();
            _vegetationOrder.Clear();
            _vegetationSizes.Clear();
            _vegetationBytes = 0;
        }
    }

    public double SampleHeight(double x, double z) => _sampler.Height(x, z);
    public long SeedHash() => _sampler.Seed;
    public int WorkerLimit() => Math.Min(3, Math.Max(1, System.Environment.ProcessorCount - 2));

    public void Request(Vector2I key, int segments, EarthworkField? earthworks = null, long revision = 0)
    {
        if (_shutdown.IsCancellationRequested || _jobs.ContainsKey(key))
            return;
        var sampler = _sampler;
        var token = _shutdown.Token;
        _jobs.Add(key, Task.Run(() =>
        {
            using var jobProbe = EarthworkProbe.Measure("chunk.total", key.X, key.Y);
            string signature = earthworks?.ChunkSignature(key.X, key.Y) ?? "";
            byte[]? saved;
            using (EarthworkProbe.Measure("region.read", key.X, key.Y)) saved = Regions?.Read(key.X, key.Y, segments, signature);
            ChunkData data;
            if (saved != null)
            {
                using var decodeProbe = EarthworkProbe.Measure("region.decode", key.X, key.Y);
                data = DecodeChunk(saved, revision);
            }
            else
            {
                using (EarthworkProbe.Measure("chunk.build", key.X, key.Y)) data = Build(sampler, key, segments, token, earthworks, revision, false);
                Interlocked.Increment(ref _meshGenerations);
                using (EarthworkProbe.Measure("region.encode-write", key.X, key.Y)) Regions?.Write(key.X, key.Y, segments, signature, EncodeChunk(data));
            }
            if (segments < 32)
                return data;
            var vegetation = VegetationFor(key, token);
            return data with
            {
                Trees = vegetation.Raw,
                Prepared = vegetation.Prepared
            };
        }, token));
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> Poll()
    {
        using var probe = FrameProbe.Measure("terrain.poll-convert");
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
            if (ProvidePreparedTrees && data.Prepared != null)
                _completedTrees[key] = data.Prepared;
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
                ["revision"] = data.Revision,
                ["arrays"] = arrays,
                ["trees"] = ProvidePreparedTrees ? Array.Empty<float>() : data.Trees
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
        _completedTrees.Clear();
        lock (_vegetationLock)
        {
            _vegetation.Clear();
            _vegetationOrder.Clear();
            _vegetationSizes.Clear();
            _vegetationBytes = 0;
        }
    }

    private sealed record ChunkData(int Segments, Vector3[] Vertices, Vector3[] Normals,
        Color[] Colors, int[] Indices, float[] Trees, long Revision, PreparedTrees? Prepared = null);

    internal static (int Vertices, int Indices) BenchmarkMesh(TerrainSampler sampler, Vector2I key, int segments, EarthworkField? earthworks)
    {
        var data = Build(sampler, key, segments, CancellationToken.None, earthworks, 0, false);
        return (data.Vertices.Length, data.Indices.Length);
    }
    private static ChunkData Build(TerrainSampler sampler, Vector2I key, int segments, CancellationToken token, EarthworkField? earthworks, long revision, bool vegetation = true)
    {
        if (earthworks?.AffectsBounds(key.X * 512.0, key.Y * 512.0, 512, sampler.Height) == true)
            return BuildEarthwork(sampler, key, segments, token, earthworks, revision, vegetation);
        double Height(double x, double z)
        {
            double natural = sampler.Height(x, z);
            return earthworks?.Height(x, z, natural) ?? natural;
        }
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
                heights[z * padded + x] = Height(key.X * 512.0 + (x - 1) * step, key.Y * 512.0 + (z - 1) * step);
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
        var trees = vegetation && segments >= 32 ? Vegetation.Generate(sampler, key.X, key.Y, token) : Array.Empty<float>();
        return new(segments, vertices, normals, colors, indices, trees, revision);
    }
}
