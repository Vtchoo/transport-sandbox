using Godot;
using System;
using System.Linq;
using System.Text.Json;

namespace TransportSandbox.Tests;

public static class TreeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var vegetation = JsonData.Read("res://tests/vegetation-fixtures.json");
        foreach (var sample in vegetation.GetProperty("cases").EnumerateArray())
        {
            var settings = JsonData.Settings(sample.GetProperty("settings"));
            var sampler = new Terrain.TerrainSampler(settings.Seed, settings.MountainHeight, settings.HorizontalScale, settings.SeaLevel);
            int x = sample.GetProperty("x").GetInt32(), z = sample.GetProperty("z").GetInt32();
            var actual = Terrain.Vegetation.Generate(sampler, x, z);
            var expected = sample.GetProperty("trees");
            check(actual.Length == expected.GetArrayLength(), "Natural-tree count matches original");
            bool identical = actual.Length == expected.GetArrayLength();
            for (int i = 0; identical && i < actual.Length; i++)
                identical = actual[i] == expected[i].GetSingle();
            check(identical, $"All natural-tree positions, dimensions, yaws and species match original ({settings.Seed}, {x}, {z})");
        }
        var reference = JsonData.Read("res://tests/tree-fixtures.json");
        var geometry = reference.GetProperty("geometry");
        void Mesh(ArrayMesh mesh, JsonElement expected)
        {
            var arrays = mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();
            var indices = arrays[(int)Godot.Mesh.ArrayType.Index].AsInt32Array();
            check(vertices.Length * 3 == expected.GetArrayLength(), "Tree triangle count matches Three.js");
            double error = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                error = Math.Max(error, Math.Abs(vertices[i].X - expected[i * 3].GetDouble()));
                error = Math.Max(error, Math.Abs(vertices[i].Y - expected[i * 3 + 1].GetDouble()));
                error = Math.Max(error, Math.Abs(vertices[i].Z - expected[i * 3 + 2].GetDouble()));
                check(Math.Abs(normals[i].Length() - 1) < .00001, "Tree normal is normalized");
            }
            check(error < .000001, "Every procedural tree vertex matches original geometry");
            for (int i = 0; i < indices.Length; i += 3)
                check(indices[i] == i && indices[i + 1] == i + 2 && indices[i + 2] == i + 1, "Tree faces use Godot winding");
        }
        Mesh(TreeScenery.Trunk, geometry.GetProperty("trunk"));
        for (int species = 0; species < 2; species++)
        {
            Mesh(TreeScenery.Crown(species, false), geometry.GetProperty("crowns")[species]);
            Mesh(TreeScenery.Crown(species, true), geometry.GetProperty("distant")[species]);
        }
        var woodland = TreeScenery.CreateWoodland();
        check(woodland.Batches.Sum(batch => batch.Crown.Multimesh.InstanceCount) == 256, "All original menu trees are present");
        foreach (var expected in reference.GetProperty("batches").EnumerateArray())
        {
            var batch = woodland.Batches.Single(batch => batch.Species == expected.GetProperty("species").GetInt32());
            var transforms = expected.GetProperty("transforms");
            check(batch.Crown.Multimesh.InstanceCount == transforms.GetArrayLength(), "Tree species counts match original");
            void Transform(Transform3D actual, JsonElement matrix)
            {
                Vector3 Column(int column) => new(matrix[column * 4].GetSingle(), matrix[column * 4 + 1].GetSingle(), matrix[column * 4 + 2].GetSingle());
                check(actual.Origin.DistanceTo(Column(3)) < .00001 && actual.Basis.X.DistanceTo(Column(0)) < .00001
                    && actual.Basis.Y.DistanceTo(Column(1)) < .00001 && actual.Basis.Z.DistanceTo(Column(2)) < .00001,
                    "Menu tree position, yaw and scale match original transform");
            }
            for (int i = 0; i < transforms.GetArrayLength(); i++)
            {
                // Validate the exact submitted data: the headless dummy renderer has no GPU readback.
                Transform(batch.TrunkTransforms[i], transforms[i].GetProperty("trunk"));
                Transform(batch.CrownTransforms[i], transforms[i].GetProperty("crown"));
                var color = batch.Colors[i];
                var source = transforms[i].GetProperty("color");
                check(Math.Abs(color.R - source[0].GetDouble()) < 1e-6 && Math.Abs(color.G - source[1].GetDouble()) < 1e-6
                    && Math.Abs(color.B - source[2].GetDouble()) < 1e-6, "Per-tree leaf color matches original HSL");
            }
        }
        woodland.UpdateLod(850);
        check(woodland.Batches.All(batch => batch.Crown.Multimesh.Mesh == TreeScenery.Crown(batch.Species, true) && batch.Trunk.Visible), "Distant tree crowns switch at 850 metres");
        woodland.UpdateLod(1100);
        check(woodland.Batches.All(batch => !batch.Trunk.Visible), "Tree trunks hide at 1100 metres");
        woodland.UpdateLod(849);
        check(woodland.Batches.All(batch => batch.Trunk.Visible && batch.Crown.Multimesh.Mesh == TreeScenery.Crown(batch.Species, false)), "Tree LOD restores when approaching");
        int changes = woodland.LodChanges;
        for (int i = 0; i < 100; i++) woodland.UpdateLod(500);
        check(woodland.LodChanges == changes, "Unchanged tree LOD does not repeatedly update rendering resources");
        if (DisplayServer.GetName() != "headless")
            foreach (var batch in woodland.Batches)
            {
                using var original = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = TreeScenery.Trunk, InstanceCount = 1 };
                for (int i = 0; i < batch.TrunkTransforms.Count; i++)
                {
                    check(batch.Trunk.Multimesh.GetInstanceTransform(i).IsEqualApprox(batch.TrunkTransforms[i]) && batch.Crown.Multimesh.GetInstanceTransform(i).IsEqualApprox(batch.CrownTransforms[i]), "GPU bulk tree buffer preserves original transforms");
                    original.SetInstanceColor(0, batch.Colors[i]);
                    check(batch.Crown.Multimesh.GetInstanceColor(i).IsEqualApprox(original.GetInstanceColor(0)), "Bulk tree colors match the original setter after renderer precision conversion");
                }
            }
        woodland.Free();
    }
}
