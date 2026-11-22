using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>Merge imported leaves by material into body/bogie templates, then instance repeats in 512 m cells.</summary>
public partial class TrainInstances : Node3D
{
    public WorldSpace Space = new();
    private readonly Dictionary<string, ArrayMesh> _templates = new();
    private readonly record struct Part(Node3D Node, string Template, ArrayMesh Mesh);
    private readonly record struct BatchKey(string Template, int X, int Z);
    private sealed class Batch(ArrayMesh mesh, bool custom)
    {
        public readonly List<Transform3D> Transforms = new();
        public readonly List<Color> Data = new();
        public readonly MultiMesh Multi = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, UseCustomData = custom };
        public readonly MultiMeshInstance3D Node = new();
        public readonly Aabb MeshBounds = mesh.GetAabb();
        public Vector3 Origin;
    }
    private readonly Dictionary<PosedVehicleVisual, Part[]> _parts = new();
    private readonly HashSet<PosedVehicleVisual> _active = new();
    private readonly List<PosedVehicleVisual> _removedParts = new();
    private readonly List<BatchKey> _removedBatches = new();
    public override void _ExitTree()
    {
        foreach (var mesh in _templates.Values) mesh.Dispose();
        _templates.Clear();
        _parts.Clear();
        foreach (var batch in _batches.Values) batch.Multi.Dispose();
        _batches.Clear();
    }
    public int BatchCount { get; private set; }
    private readonly Dictionary<BatchKey, Batch> _batches = new();
    private sealed class Surface
    {
        public Material? Material;
        public readonly List<Vector3> Vertices = new(), Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<int> Indices = new();
    }
    private static IEnumerable<MeshInstance3D> Meshes(Node node, bool skipMotion = false)
    {
        if (node is MeshInstance3D mesh) yield return mesh;
        foreach (var child in node.GetChildren())
        {
            if (skipMotion && child.Name.ToString().Contains("motion_")) continue;
            foreach (var nested in Meshes(child, skipMotion)) yield return nested;
        }
    }
    private ArrayMesh Template(Node3D root)
    {
        var surfaces = new Dictionary<ulong, Surface>();
        var inverse = root.GlobalTransform.AffineInverse();
        foreach (var mesh in Meshes(root, true))
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                var material = mesh.GetActiveMaterial(s); ulong id = material?.GetInstanceId() ?? 0;
                if (!surfaces.TryGetValue(id, out var surface)) surfaces[id] = surface = new() { Material = material };
                using var arrays = mesh.Mesh.SurfaceGetArrays(s);
                var points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.PackedVector3Array ? arrays[(int)Mesh.ArrayType.Normal].AsVector3Array() : Array.Empty<Vector3>();
                var uvs = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.PackedVector2Array ? arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array() : Array.Empty<Vector2>();
                var indices = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.PackedInt32Array ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : Enumerable.Range(0, points.Length).ToArray();
                var transform = inverse * mesh.GlobalTransform; var normal = transform.Basis.Inverse().Transposed(); int offset = surface.Vertices.Count;
                for (int i = 0; i < points.Length; i++)
                {
                    surface.Vertices.Add(transform * points[i]);
                    surface.Normals.Add(i < normals.Length ? (normal * normals[i]).Normalized() : Vector3.Up);
                    surface.Uvs.Add(i < uvs.Length ? uvs[i] : Vector2.Zero);
                }
                bool mirror = transform.Basis.Determinant() < 0;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    surface.Indices.Add(offset + indices[i]); surface.Indices.Add(offset + indices[i + (mirror ? 2 : 1)]); surface.Indices.Add(offset + indices[i + (mirror ? 1 : 2)]);
                }
            }
        var result = new ArrayMesh();
        foreach (var surface in surfaces.Values)
        {
            using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = surface.Vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = surface.Normals.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs.ToArray(); arrays[(int)Mesh.ArrayType.Index] = surface.Indices.ToArray();
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); result.SurfaceSetMaterial(result.GetSurfaceCount() - 1, surface.Material);
        }
        return result;
    }
    private static IEnumerable<Node3D> Parts(Node node)
    {
        if (node is Node3D part && (part.Name.ToString().EndsWith("body") || part.Name.ToString().Contains("bogie_") || part.Name.ToString().Contains("motion_"))) yield return part;
        foreach (var child in node.GetChildren()) foreach (var nested in Parts(child)) yield return nested;
    }
    public void Sync(IEnumerable<PosedVehicleVisual> vehicles)
    {
        _active.Clear();
        foreach (var (key, batch) in _batches)
        {
            batch.Transforms.Clear(); batch.Data.Clear();
            PositionBatch(key, batch);
        }
        foreach (var vehicle in vehicles)
        {
            _active.Add(vehicle);
            if (!_parts.TryGetValue(vehicle, out var parts))
            {
                var entries = new List<Part>();
                foreach (var node in Parts(vehicle.Vehicle.Model))
                {
                    string template = $"{vehicle.Definition.Id}:{vehicle.Gauge}:{vehicle.Vehicle.Livery}:{node.Name}";
                    if (!_templates.TryGetValue(template, out var mesh))
                    {
                        if (!Meshes(node).Any()) continue;
                        _templates[template] = mesh = Template(node);
                    }
                    entries.Add(new(node, template, mesh));
                }
                _parts[vehicle] = parts = entries.ToArray();
                foreach (var leaf in Meshes(vehicle.Vehicle.Model)) leaf.Visible = false;
            }
            foreach (var part in parts)
            {
                var world = Space.ToWorld(part.Node.GlobalPosition);
                var key = new BatchKey(part.Template, (int)Math.Floor(world.X / 512), (int)Math.Floor(world.Z / 512));
                var batch = GetBatch(key, part.Mesh, false);
                var transform = part.Node.GlobalTransform;
                transform.Origin -= batch.Origin;
                batch.Transforms.Add(transform);
            }
            if (vehicle.Vehicle.CargoFill is { Load: not null } cargo)
            {
                cargo.Visible = false;
                var world = Space.ToWorld(cargo.GlobalPosition);
                var key = new BatchKey(cargo.BatchKey, (int)Math.Floor(world.X / 512), (int)Math.Floor(world.Z / 512));
                var batch = GetBatch(key, cargo.Template, true);
                var transform = cargo.GlobalTransform;
                transform.Origin -= batch.Origin;
                batch.Transforms.Add(transform); batch.Data.Add(cargo.InstanceData);
            }
        }
        _removedParts.Clear();
        foreach (var vehicle in _parts.Keys) if (!_active.Contains(vehicle)) _removedParts.Add(vehicle);
        foreach (var vehicle in _removedParts) _parts.Remove(vehicle);
        _removedBatches.Clear();
        foreach (var (key, batch) in _batches) if (batch.Transforms.Count == 0) _removedBatches.Add(key);
        foreach (var key in _removedBatches)
        {
            var obsolete = _batches[key]; RemoveChild(obsolete.Node); obsolete.Node.QueueFree(); obsolete.Multi.Dispose(); _batches.Remove(key);
        }
        BatchCount = _batches.Count;
        foreach (var batch in _batches.Values)
        {
            var multi = batch.Multi;
            if (multi.InstanceCount != batch.Transforms.Count) multi.InstanceCount = batch.Transforms.Count;
            var bounds = batch.Transforms[0] * batch.MeshBounds;
            for (int i = 0; i < batch.Transforms.Count; i++)
            {
                multi.SetInstanceTransform(i, batch.Transforms[i]);
                if (multi.UseCustomData) multi.SetInstanceCustomData(i, batch.Data[i]);
                if (i > 0) bounds = bounds.Merge(batch.Transforms[i] * batch.MeshBounds);
            }
            // Automatic MultiMesh bounds can lag buffer updates by one rendered frame.
            // Publish the current bounds for camera and shadow culling, including new cells.
            multi.CustomAabb = bounds;
        }
    }
    private Batch GetBatch(BatchKey key, ArrayMesh mesh, bool custom)
    {
        if (!_batches.TryGetValue(key, out var batch))
        {
            batch = new(mesh, custom); batch.Node.Multimesh = batch.Multi;
            // The batch's axes match world axes even when the owner has a transform.
            batch.Node.TopLevel = true;
            _batches.Add(key, batch); AddChild(batch.Node);
            PositionBatch(key, batch);
        }
        return batch;
    }
    private void PositionBatch(BatchKey key, Batch batch)
    {
        // Keep instance coordinates relative to a stable world cell. Rebasing moves the
        // spatial node instead of teleporting all geometry inside its instance buffer.
        var origin = Space.ToLocal(new TrackVector(key.X * 512.0, 0, key.Z * 512.0));
        if (batch.Origin == origin) return;
        batch.Origin = origin;
        batch.Node.GlobalPosition = origin;
    }
}
