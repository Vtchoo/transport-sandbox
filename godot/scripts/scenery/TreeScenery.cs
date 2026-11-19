using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public readonly record struct TreeData(float X, float Y, float Z, float Height, float Radius, float Yaw, int Species);

/// <summary>Shared original tree meshes, instanced separately by species and terrain chunk.</summary>
public static class TreeScenery
{
    private static ArrayMesh? _trunk;
    private static readonly ArrayMesh?[] Near = new ArrayMesh?[2], Far = new ArrayMesh?[2];
    public static ArrayMesh Trunk => _trunk ??= Materialize(TreeGeometry.Trunk(), false);
    public static ArrayMesh Crown(int species, bool distant) => distant
        ? Far[species] ??= Materialize(TreeGeometry.Crown(species, true), true)
        : Near[species] ??= Materialize(TreeGeometry.Crown(species, false), true);
    private static ArrayMesh Materialize(ArrayMesh mesh, bool crown)
    {
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = crown ? Colors.White : new Color("655343"),
            Roughness = 1,
            VertexColorUseAsAlbedo = crown,
            VertexColorIsSrgb = false
        });
        return mesh;
    }
    public static TreeData[] MenuTrees()
    {
        var data = new List<TreeData>();
        for (int row = 0; row < 8; row++)
            for (int i = 0; i < 32; i++)
            {
                double jitter = Math.Sin(i * 17.13 + row * 8.7);
                // Match the original Float32Array rounding before transforms are constructed.
                data.Add(new((float)(18 + row * 10 + jitter * 3), -.65f,
                    (float)((i - 16) * 10 + Math.Cos(row * 12 + i) * 4),
                    (float)(9 + (jitter + 1) * 3), (float)(3 + jitter + 1), (float)(i + row * .7), row % 2));
            }
        return data.ToArray();
    }
    public static TreeChunk CreateWoodland() => Create(MenuTrees(), "Woodland");
    public static PreparedTrees Prepare(TreeData[] entries)
    {
        var batches = new List<PreparedTreeSpecies>();
        for (int species = 0; species < 2; species++)
        {
            var trees = entries.Where(tree => tree.Species == species).ToArray();
            if (trees.Length == 0)
                continue;
            var trunks = new Transform3D[trees.Length];
            var crowns = new Transform3D[trees.Length];
            var colors = new Color[trees.Length];
            var trunkBuffer = new float[trees.Length * 12];
            var crownBuffer = new float[trees.Length * 16];
            for (int i = 0; i < trees.Length; i++)
            {
                var tree = trees[i];
                var origin = new Vector3(tree.X, (float)(tree.Y - .8), tree.Z);
                var basis = new Basis(Vector3.Up, tree.Yaw);
                trunks[i] = new(basis.ScaledLocal(Vector3.One * tree.Height), origin);
                crowns[i] = new(basis.ScaledLocal(new(tree.Radius, tree.Height, tree.Radius)), origin);
                colors[i] = LeafColor(tree.Species, tree.Yaw);
                Pack(trunkBuffer, i * 12, trunks[i]);
                Pack(crownBuffer, i * 16, crowns[i]);
                crownBuffer[i * 16 + 12] = colors[i].R;
                crownBuffer[i * 16 + 13] = colors[i].G;
                crownBuffer[i * 16 + 14] = colors[i].B;
                crownBuffer[i * 16 + 15] = colors[i].A;
            }
            batches.Add(new(species, trunks, crowns, colors, trunkBuffer, crownBuffer));
        }
        return new(entries, batches.ToArray());
    }
    private static void Pack(float[] buffer, int offset, Transform3D t)
    {
        buffer[offset] = t.Basis.X.X;
        buffer[offset + 1] = t.Basis.Y.X;
        buffer[offset + 2] = t.Basis.Z.X;
        buffer[offset + 3] = t.Origin.X;
        buffer[offset + 4] = t.Basis.X.Y;
        buffer[offset + 5] = t.Basis.Y.Y;
        buffer[offset + 6] = t.Basis.Z.Y;
        buffer[offset + 7] = t.Origin.Y;
        buffer[offset + 8] = t.Basis.X.Z;
        buffer[offset + 9] = t.Basis.Y.Z;
        buffer[offset + 10] = t.Basis.Z.Z;
        buffer[offset + 11] = t.Origin.Z;
    }
    public static TreeChunk Create(IEnumerable<TreeData> data, string name = "Trees") => Create(Prepare(data.ToArray()), name);
    public static TreeChunk Create(PreparedTrees prepared, string name = "Trees")
    {
        var chunk = new TreeChunk { Name = name, Source = prepared.Trees };
        foreach (var batch in prepared.Batches)
        {
            using var trunk = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = Trunk, InstanceCount = batch.Trunks.Length };
            using var crown = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = Crown(batch.Species, false), InstanceCount = batch.Crowns.Length };
            trunk.Buffer = batch.TrunkBuffer;
            crown.Buffer = batch.CrownBuffer;
            var trunkNode = new MultiMeshInstance3D { Name = $"Trunks_{batch.Species}", Multimesh = trunk };
            var crownNode = new MultiMeshInstance3D { Name = $"Crowns_{batch.Species}", Multimesh = crown };
            chunk.AddChild(trunkNode);
            chunk.AddChild(crownNode);
            chunk.Batches.Add(new(batch.Species, trunkNode, crownNode, batch.Trunks, batch.Crowns, batch.Colors));
        }
        return chunk;
    }
    public static Color LeafColor(int species, double yaw)
    {
        double h = (species == 1 ? .32 : .28) + Math.Sin(yaw) * .018;
        double s = .42 + Math.Cos(yaw) * .06, l = species == 1 ? .14 : .19;
        double p = l <= .5 ? l * (1 + s) : l + s - l * s, q = 2 * l - p;
        double Hue(double t)
        {
            if (t < 0)
                t += 1;
            if (t > 1)
                t -= 1;
            if (t < 1.0 / 6)
                return q + (p - q) * 6 * t;
            if (t < .5)
                return p;
            if (t < 2.0 / 3)
                return q + (p - q) * 6 * (2.0 / 3 - t);
            return q;
        }
        return new((float)Hue(h + 1.0 / 3), (float)Hue(h), (float)Hue(h - 1.0 / 3));
    }
}

public sealed record PreparedTreeSpecies(int Species, Transform3D[] Trunks, Transform3D[] Crowns, Color[] Colors, float[] TrunkBuffer, float[] CrownBuffer);
public sealed record PreparedTrees(TreeData[] Trees, PreparedTreeSpecies[] Batches)
{
    // Managed source/transforms/buffers plus both GPU copies; excludes shared tree meshes.
    public long EstimatedBytes => Trees.LongLength * 364;
}
public sealed record TreeBatch(int Species, MultiMeshInstance3D Trunk, MultiMeshInstance3D Crown,
    IReadOnlyList<Transform3D> TrunkTransforms, IReadOnlyList<Transform3D> CrownTransforms, IReadOnlyList<Color> Colors);
public partial class TreeChunk : Node3D
{
    public TreeData[] Source = Array.Empty<TreeData>();
    public readonly List<TreeBatch> Batches = new();
    private bool? _trunksVisible, _distant;
    public int LodChanges
    {
        get; private set;
    }
    public void UpdateLod(double distance)
    {
        bool trunksVisible = distance < 1100, distant = distance >= 850;
        if (_trunksVisible == trunksVisible && _distant == distant)
            return;
        LodChanges++;
        foreach (var batch in Batches)
        {
            if (_trunksVisible != trunksVisible)
                batch.Trunk.Visible = trunksVisible;
            if (_distant != distant)
                batch.Crown.Multimesh.Mesh = TreeScenery.Crown(batch.Species, distant);
        }
        _trunksVisible = trunksVisible;
        _distant = distant;
    }
}
