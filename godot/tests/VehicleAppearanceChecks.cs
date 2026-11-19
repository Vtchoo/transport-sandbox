using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class VehicleAppearanceChecks
{
    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if (node is MeshInstance3D mesh)
            yield return mesh;
        foreach (var child in node.GetChildren())
            foreach (var nested in Meshes(child))
                yield return nested;
    }

    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        var catalog = VehicleCatalog.Load();
        foreach (var source in JsonData.Read("res://tests/vehicle-appearance-fixtures.json").EnumerateArray())
        {
            string id = source.GetProperty("id").GetString()!;
            int gauge = source.GetProperty("gauge").GetInt32();
            string name = $"{id}/{gauge}";
            var definition = catalog.Get(id);
            var vehicle = new PosedVehicleVisual(definition, gauge);
            var preview = new PosedVehicleVisual(definition, gauge, preview: true);
            var instances = new TrainInstances();
            parent.AddChild(vehicle);
            parent.AddChild(preview);
            parent.AddChild(instances);
            try
            {
                var signs = Meshes(vehicle.Vehicle.Model).Where(mesh => mesh.Name.ToString().StartsWith("lettering_")).ToArray();
                check(signs.Length == source.GetProperty("lettering").GetInt32(), name + ": imports every browser lettering plane");
                var textures = new HashSet<ulong>();
                foreach (var sign in signs)
                {
                    var material = sign.GetActiveMaterial(0) as StandardMaterial3D;
                    check(material?.AlbedoTexture != null && material.AlbedoTexture.GetWidth() == 1024, name + ": lettering keeps the browser canvas resolution");
                    check(material?.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded && material.Transparency == BaseMaterial3D.TransparencyEnum.Alpha, name + $": lettering stays unlit with transparent background ({material?.ShadingMode}, {material?.Transparency})");
                    check(material?.DepthDrawMode == BaseMaterial3D.DepthDrawModeEnum.Disabled, name + ": lettering matches browser depthWrite=false");
                    if (material?.AlbedoTexture != null)
                        textures.Add(material.AlbedoTexture.GetInstanceId());
                }
                foreach (var sign in Meshes(preview.Vehicle.Model).Where(mesh => mesh.Name.ToString().StartsWith("lettering_")))
                {
                    var material = sign.GetActiveMaterial(0) as StandardMaterial3D;
                    check(material?.AlbedoTexture != null && material.AlbedoColor.A <= .421f && material.DepthDrawMode == BaseMaterial3D.DepthDrawModeEnum.Disabled, name + ": placement tint retains lettering textures and transparency");
                }
                instances.Sync(new[] { vehicle });
                var instancedTextures = new HashSet<ulong>();
                foreach (var batch in instances.GetChildren().OfType<MultiMeshInstance3D>())
                {
                    var mesh = batch.Multimesh.Mesh;
                    for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                    {
                        if (mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D { AlbedoTexture: not null } material)
                            continue;
                        instancedTextures.Add(material.AlbedoTexture.GetInstanceId());
                        using var arrays = mesh.SurfaceGetArrays(surface);
                        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                        check(uvs.Any(uv => uv.X > .9f) && uvs.Any(uv => uv.Y > .9f), name + ": instanced lettering keeps its UV coordinates");
                    }
                }
                check(textures.IsSubsetOf(instancedTextures), name + ": instancing preserves every lettering texture");
            }
            finally
            {
                vehicle.QueueFree();
                preview.QueueFree();
                instances.QueueFree();
            }
            await frame();
        }
    }
}
