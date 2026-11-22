using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TransportSandbox;

/// <summary>Interior dimensions in body-local metres; quantity is converted to occupied volume.</summary>
public sealed record HopperProfile(float BottomY, float TopY, float BottomWidth, float TopWidth, float Length)
{
    public static HopperProfile Read(JsonElement data) => new(data.GetProperty("bottomY").GetSingle(),
        data.GetProperty("topY").GetSingle(), data.GetProperty("bottomWidth").GetSingle(),
        data.GetProperty("topWidth").GetSingle(), data.GetProperty("length").GetSingle());
    public float HeightFraction(double fraction)
    {
        float f = (float)Math.Clamp(fraction, 0, 1), difference = TopWidth - BottomWidth;
        return Math.Abs(difference) < .0001f ? f :
            2 * f * (BottomWidth + difference / 2) / (BottomWidth + Mathf.Sqrt(BottomWidth * BottomWidth + 2 * difference * f * (BottomWidth + difference / 2)));
    }
}

/// <summary>Shared mesh/material templates. Fill height is instance data, never baked per car.</summary>
public partial class CargoVisual : MultiMeshInstance3D
{
    private static readonly Dictionary<(HopperProfile, string, bool), ArrayMesh> Templates = new();
    private static readonly Dictionary<string, JsonElement> Styles = new();
    public CargoLoad? Load { get; private set; }
    public float FillHeight { get; private set; }
    public string BatchKey => $"cargo:{_definition.Id}:{Load?.Type}";
    public ArrayMesh Template => (ArrayMesh)Multimesh.Mesh;
    public Color InstanceData => new(FillHeight, 0, 0, 1);
    private readonly VehicleDefinition _definition;
    private readonly bool _preview;
    public CargoVisual(VehicleDefinition definition, bool preview = false)
    {
        _definition = definition; _preview = preview;
        Name = "cargo_fill"; Visible = false;
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true };
        var p = definition.CargoVisual!;
        CustomAabb = new(new(-p.TopWidth / 2, p.BottomY - .1f, -p.Length / 2), new(p.TopWidth, p.TopY - p.BottomY + .2f, p.Length));
        CastShadow = ShadowCastingSetting.Off;
    }
    public bool SetLoad(CargoLoad? load)
    {
        if (Load == load) return false;
        _definition.Cargo!.Validate(load);
        Load = load; Visible = load != null;
        if (load == null) { Multimesh.InstanceCount = 0; FillHeight = 0; return true; }
        FillHeight = _definition.CargoVisual!.HeightFraction(load.Quantity / _definition.Cargo.CapacityFor(load.Type));
        var key = (_definition.CargoVisual, load.Type, _preview);
        if (!Templates.TryGetValue(key, out var mesh)) Templates[key] = mesh = Build(key.Item1, key.Type, _preview);
        Multimesh.Mesh = mesh; Multimesh.InstanceCount = 1;
        Multimesh.SetInstanceTransform(0, Transform3D.Identity);
        Multimesh.SetInstanceCustomData(0, InstanceData);
        return true;
    }
    private static ArrayMesh Build(HopperProfile p, string cargo, bool preview)
    {
        if (Styles.Count == 0)
            foreach (var entry in JsonData.Read("res://assets/cargo/visuals.json").EnumerateObject()) Styles.Add(entry.Name, entry.Value.Clone());
        var style = Styles[cargo];
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(preview ? "res://assets/cargo/fill-preview.gdshader" : "res://assets/cargo/fill.gdshader") };
        material.SetShaderParameter("base_color", new Color(style.GetProperty("baseColor").GetString()!));
        material.SetShaderParameter("profile", new Vector4(p.BottomY, p.TopY, p.BottomWidth, p.TopWidth));
        material.SetShaderParameter("cargo_length", p.Length);
        foreach (var (json, uniform, fallback) in new[] { ("tileMetres", "tile_metres", 1f), ("textureDistance", "texture_distance", 250f), ("detailDistance", "detail_distance", 70f), ("displacementMetres", "displacement_metres", 0f), ("normalStrength", "normal_strength", 1f) })
            material.SetShaderParameter(uniform, style.TryGetProperty(json, out var value) ? value.GetSingle() : fallback);
        foreach (var (json, uniform) in new[] { ("texture", "albedo_map"), ("displacementMap", "height_map"), ("normalMap", "normal_map") })
        {
            bool available = style.TryGetProperty(json, out var path) && path.ValueKind == JsonValueKind.String;
            material.SetShaderParameter("has_" + uniform, available);
            if (available) material.SetShaderParameter(uniform, GD.Load<Texture2D>(path.GetString()!));
        }
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var indices = new List<int>();
        // A subdivided horizontal lid for close displacement, and four closed trapezoidal walls.
        const int across = 12, along = 48;
        for (int z = 0; z <= along; z++) for (int x = 0; x <= across; x++)
        {
            vertices.Add(new((float)x / across - .5f, 1, (float)z / along - .5f)); normals.Add(Vector3.Up); uvs.Add(new((float)x / across, (float)z / along));
        }
        for (int z = 0; z < along; z++) for (int x = 0; x < across; x++)
        {
            int a = z * (across + 1) + x, b = a + across + 1;
            indices.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
        }
        foreach (var (a, b, normal) in new[] {
            (new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), Vector3.Forward),
            (new Vector3(.5f,0,-.5f), new Vector3(.5f,0,.5f), Vector3.Right),
            (new Vector3(.5f,0,.5f), new Vector3(-.5f,0,.5f), Vector3.Back),
            (new Vector3(-.5f,0,.5f), new Vector3(-.5f,0,-.5f), Vector3.Left) })
        {
            int offset = vertices.Count;
            vertices.AddRange(new[] { a, b, a + Vector3.Up, b + Vector3.Up });
            for (int i = 0; i < 4; i++) { normals.Add(normal); uvs.Add(new(i % 2, i / 2)); }
            indices.AddRange(new[] { offset, offset + 2, offset + 1, offset + 1, offset + 2, offset + 3 });
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        // Distant views keep just the four corner lid vertices and walls.
        var coarse = new List<int> { 0, across, along * (across + 1), across, (along + 1) * (across + 1) - 1, along * (across + 1) };
        coarse.AddRange(indices.GetRange(across * along * 6, 24));
        using var lods = new Godot.Collections.Dictionary { [.05f] = coarse.ToArray() };
        var result = new ArrayMesh(); result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, lods: lods); result.SurfaceSetMaterial(0, material);
        result.CustomAabb = new(new(-p.TopWidth / 2, p.BottomY - .1f, -p.Length / 2), new(p.TopWidth, p.TopY - p.BottomY + .2f, p.Length));
        return result;
    }
}
