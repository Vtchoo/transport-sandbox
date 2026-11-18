using Godot;
using System.Collections.Generic;

namespace TransportSandbox;

public partial class TrackVisual : Node3D
{
    public TrackGeometry Geometry
    {
        get;
    }
    private readonly List<(SleeperBatch Data, MultiMeshInstance3D Visual)> _sleepers = new();
    public TrackVisual(TrackGeometry geometry, bool preview = false)
    {
        Geometry = geometry;
        Position = geometry.Anchor;
        StandardMaterial3D Material(string color, float roughness, float metal = 0, float opacity = 1)
        {
            var material = new StandardMaterial3D { AlbedoColor = new(color), Roughness = roughness, Metallic = metal, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            if (preview)
            {
                material.AlbedoColor = new Color("7aefc1") { A = opacity };
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
            }
            return material;
        }
        var steel = Material("a2acaf", .3f, .7f, .42f);
        var ballast = Material("77766b", 1, 0, .25f);
        var slab = Material("94918a", .95f, 0, .45f);
        var edge = Material("e9ce75", .95f, 0, .45f);
        foreach (var surface in geometry.Surfaces)
        {
            var material = surface.Name == "Ballast" ? ballast : surface.Name.StartsWith("PlatformEdge") ? edge : surface.Name.StartsWith("Platform") ? slab : steel;
            AddChild(new MeshInstance3D { Name = surface.Name, Mesh = surface.CreateMesh(), MaterialOverride = material });
        }
        var sleeperMesh = new BoxMesh { Size = geometry.SleeperSize, Material = Material("605346", .95f) };
        foreach (var batch in geometry.Sleepers)
        {
            var instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = sleeperMesh, InstanceCount = batch.Transforms.Length };
            for (int i = 0; i < batch.Transforms.Length; i++)
                instances.SetInstanceTransform(i, batch.Transforms[i]);
            var visual = new MultiMeshInstance3D { Name = "Sleepers", Multimesh = instances };
            AddChild(visual);
            _sleepers.Add((batch, visual));
        }
    }
    public void UpdateSleeperLod(Vector3 eye)
    {
        foreach (var (data, visual) in _sleepers)
        {
            int count = data.Update(eye.DistanceTo(ToGlobal(data.Center)));
            visual.Multimesh.VisibleInstanceCount = count;
            visual.Visible = count > 0;
        }
    }
}
