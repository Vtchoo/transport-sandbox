using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TransportSandbox;

public readonly record struct TerrainTile(Vector2I Key, int Segments, double Distance);
public partial class TerrainWorld : Node3D
{
    public const int Chunk = 512;
    public TerrainGenerator Generator = null!;
    public Camera3D Camera = null!;
    public Func<bool>? InitialReady;
    public readonly Dictionary<Vector2I, Node3D> Chunks = new();
    public readonly Dictionary<Vector2I, TerrainTile> Wanted = new();
    private readonly HashSet<Vector2I> _tasks = new();
    private readonly HashSet<Vector2I> _invalidatedTasks = new();
    public TerrainMeshCache MeshCache { get; } = new();
    public long SubmittedJobs
    {
        get; private set;
    }
    private readonly Queue<Godot.Collections.Dictionary> _completed = new();
    private readonly Dictionary<Vector2I, PreparedTrees?> _preparedTrees = new();
    public TreeStreamer Trees { get; private set; } = null!;
    public int MaxUploadsPerFrame = 2;
    public double UploadBudgetMs = 2;
    public int LastUploads
    {
        get; private set;
    }
    public double LastUploadMs
    {
        get; private set;
    }
    public int ActiveJobs => _tasks.Count + (Trees?.Pending ?? 0);
    private Vector3I _cell = new(int.MaxValue, 0, 0);
    private ShaderMaterial _terrainMaterial = null!;
    private MeshInstance3D _water = null!;
    private bool _contours, _stopping;
    private Terrain.EarthworkField? _clearance;
    private long _terrainRevision;
    private readonly HashSet<Vector2I> _dirtyChunks = new();
    public int PendingEarthworks => _dirtyChunks.Count + (Trees?.Pending ?? 0);
    private readonly Dictionary<Vector2I, TreeData[]> _naturalTrees = new(), _visibleTrees = new();
    public int NaturalTreeCount => _naturalTrees.Values.Sum(t => t.Length);
    public int VisibleTreeCount => _visibleTrees.Values.Sum(t => t.Length);
    internal IEnumerable<Vector2I> EarthworkCandidates() => Wanted.Keys.Concat(Chunks.Keys).Concat(MeshCache.Chunks).Concat(_tasks).Distinct();
    public void SetEarthworks(Terrain.EarthworkField field, Terrain.EarthworkChanges? changes = null)
    {
        using var probe = FrameProbe.Measure("earthworks.apply");
        var previous = _clearance;
        _clearance = field;
        Generator.Earthworks = field;
        _terrainRevision++;
        var affected = new HashSet<Vector2I>();
        using (FrameProbe.Measure("earthworks.find-affected"))
        foreach (var key in EarthworkCandidates())
            if (changes != null ? changes.Affects(key) :
                previous?.AffectsBounds(key.X * 512.0, key.Y * 512.0, 512, Generator.Sampler.Height) == true ||
                field.AffectsBounds(key.X * 512.0, key.Y * 512.0, 512, Generator.Sampler.Height))
                affected.Add(key);
        using (FrameProbe.Measure("earthworks.cache-revision")) MeshCache.AdvanceRevision(_terrainRevision, affected.Contains);
        foreach (var key in _tasks) if (affected.Contains(key)) _invalidatedTasks.Add(key);
        foreach (var key in affected)
            if (Wanted.ContainsKey(key))
                _dirtyChunks.Add(key);
        foreach (var (key, holder) in Chunks)
            if (!affected.Contains(key))
                holder.SetMeta("earthwork_revision", _terrainRevision);
        using (FrameProbe.Measure("earthworks.refresh-trees"))
        foreach (var (key, holder) in Chunks)
            if (affected.Contains(key))
                RefreshTrees(key, holder);
    }
    private void RefreshTrees(Vector2I key, Node3D holder)
    {
        if (!_naturalTrees.TryGetValue(key, out var natural))
            return;
        if (holder.GetMeta("segments").AsInt32() < 32)
        {
            Trees.Deactivate(key);
            return;
        }
        Trees.Activate(key, holder, natural, _preparedTrees.GetValueOrDefault(key), _clearance, _contours);
    }
    private void TreesCommitted(Vector2I key, TreeData[] visible)
    {
        if (!Chunks.TryGetValue(key, out var holder))
            return;
        _visibleTrees[key] = visible;
        if (holder.GetNodeOrNull<TreeChunk>("Trees") is { } trees)
            UpdateTreeLod(key, holder, trees);
    }
    private void UpdateTreeLod(Vector2I key, Node3D holder, TreeChunk trees)
    {
        float centerY = holder.GetChild<MeshInstance3D>(0).Mesh.GetAabb().GetCenter().Y;
        var center = Generator.Space.ToLocal(new TrackVector((key.X + .5) * Chunk, centerY, (key.Y + .5) * Chunk));
        trees.UpdateLod(Camera.Position.DistanceTo(center));
    }
    public int WorkerLimit
    {
        get; private set;
    }
    public override void _Ready()
    {
        Generator.Space.Rebased += Rebased;
        Generator.Backend.ProvidePreparedTrees = true;
        Trees = new(this, Generator.Sampler);
        Trees.Committed += TreesCommitted;
        WorkerLimit = Generator.Backend.WorkerLimit();
        _terrainMaterial = new()
        {
            Shader = GD.Load<Shader>("res://shaders/terrain.gdshader")
        };
        UpdateMaterialOrigin();
        _water = new()
        {
            Name = "Water",
            Mesh = new PlaneMesh { Size = new(32000, 32000) },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_water);
        _water.Position = new(0, (float)Generator.SeaLevel, 0);
    }
    public override void _Process(double delta)
    {
        using var probe = FrameProbe.Measure("terrain");
        if (InitialReady != null) { if (!InitialReady()) return; InitialReady = null; }
        if (_stopping || !IsInstanceValid(Camera))
            return;
        foreach (var data in Generator.Backend.Poll())
            _completed.Enqueue(data);
        LastUploads = 0;
        var uploadTimer = Stopwatch.StartNew();
        while (_completed.Count > 0 && LastUploads < MaxUploadsPerFrame && uploadTimer.Elapsed.TotalMilliseconds < UploadBudgetMs)
        {
            using var data = _completed.Dequeue();
            var key = data["key"].AsVector2I();
            _tasks.Remove(key);
            bool invalidated = _invalidatedTasks.Remove(key);
            var prepared = Generator.Backend.TakePreparedTrees(key);
            if (data.ContainsKey("error"))
            {
                GD.PushError(data["error"].AsString());
                _stopping = true;
                continue;
            }
            int segments = data["segments"].AsInt32();
            if (invalidated)
                continue;
            using var arrays = data["arrays"].AsGodotArray();
            var mesh = new ArrayMesh();
            using (FrameProbe.Measure("terrain.mesh.upload")) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var trees = prepared?.Trees ?? Array.Empty<TreeData>();
            long bytes = 2L * (arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().LongLength * 40 + arrays[(int)Mesh.ArrayType.Index].AsInt32Array().LongLength * 4) + (prepared?.EstimatedBytes ?? trees.LongLength * 28);
            var cached = new CachedTerrainMesh(mesh, trees, bytes, prepared);
            if (Wanted.TryGetValue(key, out var tile) && tile.Segments == segments)
                Mount(key, segments, cached);
            else
                MeshCache.Store(new(key, segments, _terrainRevision), cached);
            LastUploads++;
        }
        var eye = Camera is WorldCamera worldCamera ? worldCamera.WorldPosition : Generator.Space.ToWorld(Camera.Position);
        foreach (var (key, holder) in Chunks)
            if (holder.GetNodeOrNull<TreeChunk>("Trees") is { } trees)
                UpdateTreeLod(key, holder, trees);
        var center = new Vector2I((int)Math.Floor(eye.X / Chunk), (int)Math.Floor(eye.Z / Chunk));
        _water.Position = new(Camera.Position.X, _water.Position.Y, Camera.Position.Z);
        var cell = new Vector3I(center.X, center.Y, Mathf.FloorToInt(Camera.Position.Y / 256));
        if (cell != _cell)
        {
            _cell = cell;
            SetWantedTiles(WantedTiles(Generator, eye));
        }
        foreach (var (key, tile) in Wanted)
        {
            if (!_dirtyChunks.Contains(key) && Chunks.TryGetValue(key, out var holder) && holder.GetMeta("segments").AsInt32() == tile.Segments)
                continue;
            var cacheKey = new TerrainMeshKey(key, tile.Segments, _terrainRevision);
            if (MeshCache.Contains(cacheKey))
            {
                if (LastUploads < MaxUploadsPerFrame && uploadTimer.Elapsed.TotalMilliseconds < UploadBudgetMs && MeshCache.TryTake(cacheKey, out var cached))
                {
                    Mount(key, tile.Segments, cached);
                    LastUploads++;
                }
                continue;
            }
            if (_tasks.Contains(key) || _tasks.Count >= WorkerLimit)
                continue;
            _tasks.Add(key);
            SubmittedJobs++;
            Generator.Backend.Request(key, tile.Segments, _clearance, _terrainRevision);
        }
        LastUploadMs = uploadTimer.Elapsed.TotalMilliseconds;
        Trees.Pump(_contours);
    }
    private void Retire(Vector2I key, Node3D holder)
    {
        long revision = holder.GetMeta("earthwork_revision").AsInt64();
        if (revision == _terrainRevision)
            MeshCache.Store(new(key, holder.GetMeta("segments").AsInt32(), revision),
                new((ArrayMesh)holder.GetChild<MeshInstance3D>(0).Mesh, _naturalTrees[key], holder.GetMeta("cache_bytes").AsInt64(), _preparedTrees.GetValueOrDefault(key)));
        holder.QueueFree();
    }
    private void Mount(Vector2I key, int segments, CachedTerrainMesh entry)
    {
        using var probe = FrameProbe.Measure("terrain.mount");
        Trees.Deactivate(key);
        if (Chunks.TryGetValue(key, out var previous))
            Retire(key, previous);
        _visibleTrees.Remove(key);
        _naturalTrees[key] = entry.Trees;
        _preparedTrees[key] = entry.Prepared;
        var holder = new Node3D { Position = Generator.Space.ToLocal(new TrackVector(key.X * (double)Chunk, 0, key.Y * (double)Chunk)) };
        holder.AddChild(new MeshInstance3D { Mesh = entry.Mesh, MaterialOverride = _terrainMaterial });
        holder.SetMeta("segments", segments);
        holder.SetMeta("earthwork_revision", _terrainRevision);
        holder.SetMeta("cache_bytes", entry.EstimatedBytes);
        AddChild(holder);
        Chunks[key] = holder;
        _dirtyChunks.Remove(key);
        RefreshTrees(key, holder);
    }
    internal void SetWantedTiles(IEnumerable<TerrainTile> tiles)
    {
        Wanted.Clear();
        foreach (var tile in tiles)
            Wanted[tile.Key] = tile;
        _dirtyChunks.RemoveWhere(key => !Wanted.ContainsKey(key));
        foreach (var key in Chunks.Keys.ToArray())
            if (!Wanted.ContainsKey(key))
            {
                Trees.Deactivate(key);
                Retire(key, Chunks[key]);
                Chunks.Remove(key);
                _naturalTrees.Remove(key);
                _preparedTrees.Remove(key);
                _visibleTrees.Remove(key);
            }
    }
    public bool HasTerrainAt(double x, double z) => Chunks.ContainsKey(new((int)Math.Floor(x / Chunk), (int)Math.Floor(z / Chunk)));
    public double GroundHeight(double x, double z)
    {
        if (Generator.Earthworks != null)
            return Generator.HeightWorld(x, z);
        var key = new Vector2I((int)Math.Floor(x / Chunk), (int)Math.Floor(z / Chunk));
        int segments = Chunks.TryGetValue(key, out var chunk) ? chunk.GetMeta("segments").AsInt32() : 128;
        double step = Chunk / (double)segments;
        double gx = Math.Floor(x / step) * step, gz = Math.Floor(z / step) * step;
        double u = (x - gx) / step, v = (z - gz) / step;
        double a = Generator.HeightWorld(gx, gz), b = Generator.HeightWorld(gx + step, gz);
        double c = Generator.HeightWorld(gx, gz + step), d = Generator.HeightWorld(gx + step, gz + step);
        return u + v <= 1 ? a + (b - a) * u + (c - a) * v : d + (c - d) * (1 - u) + (b - d) * (1 - v);
    }
    private void Rebased(TrackVector worldShift)
    {
        UpdateMaterialOrigin();
        var shift = worldShift.Float();
        foreach (var (key, holder) in Chunks)
            holder.Position = Generator.Space.ToLocal(new TrackVector(key.X * (double)Chunk, 0, key.Y * (double)Chunk));
        _water.Position -= shift;
    }
    private void UpdateMaterialOrigin() => _terrainMaterial.SetShaderParameter("terrain_origin",
        new Vector2((float)Generator.Space.Origin.X, (float)Generator.Space.Origin.Z));
    public static TerrainTile[] WantedTiles(TerrainGenerator generator, Vector3 camera) => WantedTiles(generator, TrackVector.From(camera));
    public static TerrainTile[] WantedTiles(TerrainGenerator generator, TrackVector camera)
    {
        int cx = (int)Math.Floor(camera.X / Chunk), cz = (int)Math.Floor(camera.Z / Chunk);
        var tiles = new List<TerrainTile>();
        for (int dz = -10; dz <= 10; dz++)
            for (int dx = -10; dx <= 10; dx++)
            {
                var key = new Vector2I(cx + dx, cz + dz);
                double height = generator.HeightWorld((key.X + 0.5) * Chunk, (key.Y + 0.5) * Chunk), vertical = Math.Abs(camera.Y - height) / Chunk;
                double detail = Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dz)), vertical);
                tiles.Add(new(key, detail <= 1.5 ? 128 : detail <= 3.5 ? 64 : detail <= 6.5 ? 32 : 16, Math.Sqrt(dx * dx + dz * dz + vertical * vertical)));
            }
        return tiles.OrderBy(t => t.Distance).ToArray();
    }
    public void ShowContours(bool value)
    {
        _contours = value;
        _terrainMaterial.SetShaderParameter("contours", value);
        foreach (var holder in Chunks.Values)
            if (holder.GetNodeOrNull<TreeChunk>("Trees") is { } trees)
                trees.Visible = !value;
    }
    public void SetContourInterval(double interval)
    {
        _terrainMaterial.SetShaderParameter("contour_interval", Math.Max(1, interval));
    }
    public override void _ExitTree()
    {
        _stopping = true;
        Generator.Space.Rebased -= Rebased;
        Trees.Dispose();
        Generator.Backend.Shutdown();
        while (_completed.TryDequeue(out var data))
            data.Dispose();
        _tasks.Clear();
        MeshCache.Clear();
    }
}
