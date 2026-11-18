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
    public static TreeChunk Create(IEnumerable<TreeData> data, string name = "Trees")
    {
        var chunk = new TreeChunk { Name = name };
        var entries = data.ToArray();
        for (int species = 0; species < 2; species++)
        {
            var trees = entries.Where(tree => tree.Species == species).ToArray();
            if (trees.Length == 0)
                continue;
            var trunk = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = Trunk, InstanceCount = trees.Length };
            var crown = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = Crown(species, false), InstanceCount = trees.Length };
            var trunkTransforms = new Transform3D[trees.Length];
            var crownTransforms = new Transform3D[trees.Length];
            var colors = new Color[trees.Length];
            for (int i = 0; i < trees.Length; i++)
            {
                var tree = trees[i];
                var origin = new Vector3(tree.X, (float)(tree.Y - .8), tree.Z);
                var basis = new Basis(Vector3.Up, tree.Yaw);
                trunkTransforms[i] = new(basis.ScaledLocal(Vector3.One * tree.Height), origin);
                crownTransforms[i] = new(basis.ScaledLocal(new(tree.Radius, tree.Height, tree.Radius)), origin);
                colors[i] = LeafColor(tree.Species, tree.Yaw);
                trunk.SetInstanceTransform(i, trunkTransforms[i]);
                crown.SetInstanceTransform(i, crownTransforms[i]);
                crown.SetInstanceColor(i, colors[i]);
            }
            var trunkNode = new MultiMeshInstance3D { Name = $"Trunks_{species}", Multimesh = trunk };
            var crownNode = new MultiMeshInstance3D { Name = $"Crowns_{species}", Multimesh = crown };
            chunk.AddChild(trunkNode);
            chunk.AddChild(crownNode);
            chunk.Batches.Add(new(species, trunkNode, crownNode, trunkTransforms, crownTransforms, colors));
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

public sealed record TreeBatch(int Species, MultiMeshInstance3D Trunk, MultiMeshInstance3D Crown,
    IReadOnlyList<Transform3D> TrunkTransforms, IReadOnlyList<Transform3D> CrownTransforms, IReadOnlyList<Color> Colors);
public partial class TreeChunk : Node3D
{
    public readonly List<TreeBatch> Batches = new();
    public void UpdateLod(double distance)
    {
        foreach (var batch in Batches)
        {
            batch.Trunk.Visible = distance < 1100;
            var mesh = TreeScenery.Crown(batch.Species, distance >= 850);
            if (batch.Crown.Multimesh.Mesh != mesh)
                batch.Crown.Multimesh.Mesh = mesh;
        }
    }
}
