using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Game-owned surface settings for the modelling guide's semantic material slots.</summary>
public static class VehicleMaterials
{
    // Cache by imported resource and role: identical vehicles share overrides, while
    // authored paint colors/textures remain distinct. Never mutate a glTF resource.
    private static readonly Dictionary<(ulong Source, string Role), StandardMaterial3D> Materials = new();

    public static void Apply(Node node, string? liveryMaterial)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.GetActiveMaterial(surface) is StandardMaterial3D source)
                {
                    string role = source.ResourceName.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(liveryMaterial) && source.ResourceName == liveryMaterial) role = "paint";
                    var replacement = Resolve(source, role);
                    if (replacement != null) mesh.SetSurfaceOverrideMaterial(surface, replacement);
                }
        foreach (var child in node.GetChildren()) Apply(child, liveryMaterial);
    }

    private static StandardMaterial3D? Resolve(StandardMaterial3D source, string role)
    {
        float metallic = 0;
        float roughness;
        Color? color = null;
        switch (role)
        {
            // Match the streamlined passenger cars. Fully metallic surfaces lose
            // their diffuse lighting in our world without environment reflections.
            case "metal-stainless": metallic = .75f; roughness = .32f; color = new Color("c4c9cb"); break;
            case "metal-steel": metallic = 1; roughness = .35f; color = new Color("899198"); break;
            case "metal-dark": metallic = 1; roughness = .65f; color = new Color("353a3e"); break;
            case "glass-tinted": roughness = .15f; color = new Color("263e49"); break;
            case "rubber": roughness = .90f; color = new Color("202225"); break;
            case "interior": roughness = .80f; break;
            case "decal-fixed": roughness = .60f; break;
            case "lamp-white": roughness = .25f; color = new Color("f2efe0"); break;
            case "lamp-red": roughness = .25f; color = new Color("bc2525"); break;
            default:
                if (role != "paint" && !role.StartsWith("paint-", StringComparison.Ordinal)) return null;
                roughness = .45f;
                break;
        }
        var key = (source.GetInstanceId(), role);
        if (Materials.TryGetValue(key, out var existing)) return existing;
        var material = (StandardMaterial3D)source.Duplicate();
        material.Metallic = metallic;
        material.Roughness = roughness;
        material.MetallicTexture = null;
        material.RoughnessTexture = null;
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        if (color.HasValue)
        {
            material.AlbedoColor = color.Value;
            material.AlbedoTexture = null;
            material.EmissionEnabled = false;
            material.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
            material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly;
        }
        Materials[key] = material;
        return material;
    }
}
