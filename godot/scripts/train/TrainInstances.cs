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
    private readonly Dictionary<PosedVehicleVisual, Node3D[]> _parts = new();
    public override void _ExitTree()
    {
        foreach (var mesh in _templates.Values) mesh.Dispose();
        _templates.Clear();
        _parts.Clear();
    }
    public int BatchCount { get; private set; }
    private readonly Dictionary<string, MultiMeshInstance3D> _batches = new();
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
        var active = vehicles.ToArray();
        var activeSet = active.ToHashSet();
        foreach (var obsolete in _parts.Keys.Where(v => !activeSet.Contains(v)).ToArray()) _parts.Remove(obsolete);
        var batches = new Dictionary<string, (ArrayMesh Mesh, List<Transform3D> Transforms, List<Color>? Data)>();
        foreach (var vehicle in active)
        {
            if (!_parts.TryGetValue(vehicle, out var parts))
            {
                _parts[vehicle] = parts = Parts(vehicle.Vehicle.Model).ToArray();
                foreach (var leaf in Meshes(vehicle.Vehicle.Model)) leaf.Visible = false;
            }
            foreach (var part in parts)
            {
                string templateKey = $"{vehicle.Definition.Id}:{vehicle.Gauge}:{vehicle.Vehicle.Livery}:{part.Name}";
                if (!_templates.TryGetValue(templateKey, out var mesh))
                {
                    if (!Meshes(part).Any()) continue;
                    _templates[templateKey] = mesh = Template(part);
                }
                var world = Space.ToWorld(part.GlobalPosition);
                string key = templateKey + $":{Math.Floor(world.X / 512)}:{Math.Floor(world.Z / 512)}";
                if (!batches.TryGetValue(key, out var batch)) batches[key] = batch = (mesh, new(), null);
                batch.Transforms.Add(part.GlobalTransform);
            }
            if (vehicle.Vehicle.CargoFill is { Load: not null } cargo)
            {
                cargo.Visible = false;
                var world = Space.ToWorld(cargo.GlobalPosition);
                string key = cargo.BatchKey + $":{Math.Floor(world.X / 512)}:{Math.Floor(world.Z / 512)}";
                if (!batches.TryGetValue(key, out var batch)) batches[key] = batch = (cargo.Template, new(), new());
                batch.Transforms.Add(cargo.GlobalTransform); batch.Data!.Add(cargo.InstanceData);
            }
        }
        BatchCount = batches.Count;
        foreach (var key in _batches.Keys.Where(key => !batches.ContainsKey(key)).ToArray())
        {
            var obsolete = _batches[key]; RemoveChild(obsolete); obsolete.QueueFree(); _batches.Remove(key);
        }
        foreach (var (key, batch) in batches)
        {
            if (!_batches.TryGetValue(key, out var node))
            {
                node = new MultiMeshInstance3D(); _batches[key] = node; AddChild(node);
                using var created = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = batch.Mesh, UseCustomData = batch.Data != null };
                node.Multimesh = created;
            }
            using var multi = node.Multimesh;
            if (multi.InstanceCount != batch.Transforms.Count) multi.InstanceCount = batch.Transforms.Count;
            for (int i = 0; i < batch.Transforms.Count; i++)
            {
                multi.SetInstanceTransform(i, batch.Transforms[i]);
                if (batch.Data != null) multi.SetInstanceCustomData(i, batch.Data[i]);
            }
        }
    }
}
