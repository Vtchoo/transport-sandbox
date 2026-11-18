using Godot;
using System;

namespace TransportSandbox;

/// <summary>Fixed world-axis grid; coordinates and snap rounding match the browser implementation.</summary>
public partial class WorldTrackGrid : Node3D
{
    public static readonly int[] Spacings = { 0, 1, 5, 10, 25 };
    public int Spacing
    {
        get; private set;
    }
    private readonly ImmediateMesh _mesh = new();
    public override void _Ready()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            RenderPriority = 10
        };
        AddChild(new MeshInstance3D { Mesh = _mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        Hide();
    }
    public void SetSpacing(int spacing)
    {
        if (Array.IndexOf(Spacings, spacing) < 0 || spacing == Spacing)
            return;
        Spacing = spacing;
        _mesh.ClearSurfaces();
        Hide();
        if (spacing == 0)
            return;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        float extent = spacing * 40;
        for (int i = -40; i <= 40; i++)
        {
            var color = new Color(i == 0 ? "b6e0c4" : "8ab7a0") { A = .24f };
            _mesh.SurfaceSetColor(color);
            float offset = i * spacing;
            _mesh.SurfaceAddVertex(new(-extent, 0, offset));
            _mesh.SurfaceAddVertex(new(extent, 0, offset));
            _mesh.SurfaceAddVertex(new(offset, 0, -extent));
            _mesh.SurfaceAddVertex(new(offset, 0, extent));
        }
        _mesh.SurfaceEnd();
    }
    public Vector3 Snap(Vector3 point) => Spacing == 0 ? point : new(
        (float)(Math.Floor((double)point.X / Spacing + .5) * Spacing), point.Y,
        (float)(Math.Floor((double)point.Z / Spacing + .5) * Spacing));
    public void ShowAt(Vector3? point, bool enabled)
    {
        Visible = enabled && Spacing != 0 && point.HasValue;
        if (Visible)
            Position = Snap(point!.Value);
    }
}
