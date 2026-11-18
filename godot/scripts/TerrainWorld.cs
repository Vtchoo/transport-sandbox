using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public readonly record struct TerrainTile(Vector2I Key, int Segments, double Distance);
public partial class TerrainWorld : Node3D
{
    public const int Chunk = 512;
    public TerrainGenerator Generator = null!;
    public Camera3D Camera = null!;
    public readonly Dictionary<Vector2I, Node3D> Chunks = new();
    public readonly Dictionary<Vector2I, TerrainTile> Wanted = new();
    private readonly HashSet<Vector2I> _tasks = new();
    private Vector3I _cell = new(int.MaxValue, 0, 0);
    private ShaderMaterial _terrainMaterial = null!;
    private MeshInstance3D _water = null!;
    private bool _contours, _stopping;
    private Terrain.EarthworkField? _clearance;
    private long _terrainRevision;
    private readonly HashSet<Vector2I> _dirtyChunks = new();
    public int PendingEarthworks => _dirtyChunks.Count;
    private readonly Dictionary<Vector2I, TreeData[]> _naturalTrees = new(), _visibleTrees = new();
    public int NaturalTreeCount => _naturalTrees.Values.Sum(t => t.Length);
    public int VisibleTreeCount => _visibleTrees.Values.Sum(t => t.Length);
    public void SetEarthworks(Terrain.EarthworkField field)
    {
        var previous = _clearance;
        _clearance = field;
        Generator.Earthworks = field;
        _terrainRevision++;
        foreach (var key in Wanted.Keys)
            if (previous?.AffectsBounds(key.X * 512.0, key.Y * 512.0, 512, Generator.Sampler.Height) == true ||
                field.AffectsBounds(key.X * 512.0, key.Y * 512.0, 512, Generator.Sampler.Height))
                _dirtyChunks.Add(key);
        foreach (var (key, holder) in Chunks)
            RefreshTrees(key, holder);
    }
    private void RefreshTrees(Vector2I key, Node3D holder)
    {
        if (!_naturalTrees.TryGetValue(key, out var natural))
            return;
        var filtered = _clearance == null ? natural : natural.Where(tree =>
        {
            var point = Terrain.Vegetation.WorldPosition(Generator.Sampler, key.X, key.Y, tree.X, tree.Z);
            return !_clearance.ClearsTree(point.X, point.Z, tree.Y);
        }).ToArray();
        if (_visibleTrees.TryGetValue(key, out var old) && old.SequenceEqual(filtered))
            return;
        _visibleTrees[key] = filtered;
        if (holder.GetNodeOrNull<TreeChunk>("Trees") is { } previous)
        {
            holder.RemoveChild(previous);
            previous.QueueFree();
        }
        if (filtered.Length == 0)
            return;
        var trees = TreeScenery.Create(filtered);
        trees.Visible = !_contours;
        holder.AddChild(trees);
    }
    public int WorkerLimit
    {
        get; private set;
    }
    public override void _Ready()
    {
        WorkerLimit = Generator.Backend.WorkerLimit();
        _terrainMaterial = new()
        {
            Shader = GD.Load<Shader>("res://shaders/terrain.gdshader")
        };
        _water = new()
        {
            Mesh = new PlaneMesh { Size = new(24000, 24000) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new(0.13f, 0.35f, 0.42f), Roughness = 0.28f }
        };
        AddChild(_water);
        _water.Position = new(0, (float)Generator.SeaLevel, 0);
    }
    public override void _Process(double delta)
    {
        if (_stopping || !IsInstanceValid(Camera))
            return;
        foreach (var data in Generator.Backend.Poll())
        {
            var key = data["key"].AsVector2I();
            _tasks.Remove(key);
            if (data.ContainsKey("error"))
            {
                GD.PushError(data["error"].AsString());
                _stopping = true;
                continue;
            }
            int segments = data["segments"].AsInt32();
            if (!Wanted.TryGetValue(key, out var tile) || tile.Segments != segments || data["revision"].AsInt64() != _terrainRevision)
                continue;
            if (Chunks.TryGetValue(key, out var previous))
                previous.QueueFree();
            _visibleTrees.Remove(key);
            var holder = new Node3D { Position = new(key.X * Chunk, 0, key.Y * Chunk) };
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data["arrays"].AsGodotArray());
            holder.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _terrainMaterial });
            var entries = data["trees"].AsFloat32Array();
            if (entries.Length != 0)
            {
                var treeData = new List<TreeData>();
                for (int i = 0; i < entries.Length; i += Terrain.Vegetation.Stride)
                    treeData.Add(new(entries[i], entries[i + 1], entries[i + 2], entries[i + 3], entries[i + 4], entries[i + 5], (int)entries[i + 6]));
                _naturalTrees[key] = treeData.ToArray();
            }
            else
                _naturalTrees[key] = Array.Empty<TreeData>();
            RefreshTrees(key, holder);
            AddChild(holder);
            holder.SetMeta("segments", segments);
            holder.SetMeta("earthwork_revision", _terrainRevision);
            Chunks[key] = holder;
            _dirtyChunks.Remove(key);
        }
        foreach (var (key, holder) in Chunks)
            if (holder.GetNodeOrNull<TreeChunk>("Trees") is { } trees)
            {
                float centerY = holder.GetChild<MeshInstance3D>(0).Mesh.GetAabb().GetCenter().Y;
                var centerPoint = new Vector3((key.X + .5f) * Chunk, centerY, (key.Y + .5f) * Chunk);
                trees.UpdateLod(Camera.Position.DistanceTo(centerPoint));
            }
        var center = new Vector2I(Mathf.FloorToInt(Camera.Position.X / Chunk), Mathf.FloorToInt(Camera.Position.Z / Chunk));
        _water.Position = new(Camera.Position.X, _water.Position.Y, Camera.Position.Z);
        var cell = new Vector3I(center.X, center.Y, Mathf.FloorToInt(Camera.Position.Y / 256));
        if (cell != _cell)
        {
            _cell = cell;
            Wanted.Clear();
            foreach (var tile in WantedTiles(Generator, Camera.Position))
                Wanted[tile.Key] = tile;
            _dirtyChunks.RemoveWhere(key => !Wanted.ContainsKey(key));
            foreach (var key in Chunks.Keys.ToArray())
                if (!Wanted.ContainsKey(key))
                {
                    Chunks[key].QueueFree();
                    Chunks.Remove(key);
                    _naturalTrees.Remove(key);
                    _visibleTrees.Remove(key);
                }
        }
        foreach (var (key, tile) in Wanted)
        {
            if (_tasks.Count >= WorkerLimit)
                break;
            if (_tasks.Contains(key) || !_dirtyChunks.Contains(key) && Chunks.TryGetValue(key, out var holder) && holder.GetMeta("segments").AsInt32() == tile.Segments)
                continue;
            _tasks.Add(key);
            Generator.Backend.Request(key, tile.Segments, _clearance, _terrainRevision);
        }
    }
    public static TerrainTile[] WantedTiles(TerrainGenerator generator, Vector3 camera)
    {
        int cx = Mathf.FloorToInt(camera.X / Chunk), cz = Mathf.FloorToInt(camera.Z / Chunk);
        var tiles = new List<TerrainTile>();
        for (int dz = -10; dz <= 10; dz++)
            for (int dx = -10; dx <= 10; dx++)
            {
                var key = new Vector2I(cx + dx, cz + dz);
                double height = generator.Height((key.X + 0.5) * Chunk, (key.Y + 0.5) * Chunk), vertical = Math.Abs(camera.Y - height) / Chunk;
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
        Generator.Backend.Shutdown();
        _tasks.Clear();
    }
}
