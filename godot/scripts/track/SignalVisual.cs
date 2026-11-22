using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public partial class SignalVisual : Node3D
{
    private readonly MeshInstance3D[] _lamps = new MeshInstance3D[3];
    private readonly StandardMaterial3D[] _lit = new StandardMaterial3D[3];
    private readonly StandardMaterial3D _dark;
    private RailSignalAspect? _aspect;
    private (Vector3 Position, Vector3 Axis, int Gauge, int Side)? _placement;
    public Node3D DirectionArrow { get; } = new();
    public SignalVisual(RailSignalKind kind, bool preview = false, bool remove = false)
    {
        StandardMaterial3D Material(string color, bool glow = false)
        {
            var mat = TrackRenderer.Material(new Color(color));
            if (preview) { mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; mat.AlbedoColor = new Color(remove ? "ef7864" : color) { A = .5f }; }
            if (glow) { mat.EmissionEnabled = true; mat.Emission = new(color); mat.EmissionEnergyMultiplier = .8f; }
            return mat;
        }
        void Box(Vector3 size, Vector3 point, string color, float rotation = 0)
        {
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = point,
                Rotation = new(0, 0, rotation), MaterialOverride = Material(color) });
        }
        Box(new(.25f, .15f, .25f), new(0, .05f, 0), "777e77");
        Box(new(.09f, 2.9f, .09f), new(0, 1.5f, 0), "737e78");
        Box(kind == RailSignalKind.Chain ? new(.52f, .52f, .18f) : new(.42f, 1, .18f), new(0, 3.05f, 0), "202e29",
            kind == RailSignalKind.Chain ? Mathf.Pi / 4 : 0);
        if (kind == RailSignalKind.Path) Box(new(.6f, .12f, .2f), new(0, 3.65f, 0), "d9bb65");
        _dark = Material("23312a");
        string[] colors = { "ef4b43", "6cdd72", "54a6f5" };
        for (int i = 0; i < 3; i++)
        {
            _lit[i] = Material(colors[i], true);
            var point = kind == RailSignalKind.Chain ? new Vector3((i - 1) * .17f, 3.05f, -.12f) : new Vector3(0, 3.32f - i * .27f, -.12f);
            _lamps[i] = new() { Mesh = new SphereMesh { Radius = .085f, Height = .17f, RadialSegments = 8, Rings = 4 }, Position = point, MaterialOverride = _dark };
            AddChild(_lamps[i]);
        }
        var arrowMaterial = Material(remove ? "ef7864" : "cddd83");
        arrowMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        arrowMaterial.NoDepthTest = true;
        arrowMaterial.RenderPriority = 10;
        AddChild(DirectionArrow);
        DirectionArrow.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(.32f, .12f, 1.8f) },
            Position = new(0, 0, .2f), MaterialOverride = arrowMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        DirectionArrow.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = .64f, Height = 1.4f, RadialSegments = 8 },
            Position = new(0, 0, 1.7f), Rotation = new(Mathf.Pi / 2, 0, 0), MaterialOverride = arrowMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        SetAspect(RailSignalAspect.Red);
    }
    public void Place(TrackNode node, int side)
    {
        var placement = (node.Position, node.Axis, node.Gauge, side);
        if (_placement == placement) return;
        _placement = placement;
        Position = node.Position + TrackNetwork.SignalRight(node.Axis) * (side * TrackNetwork.SignalOffset(node.Gauge));
        var axis = new Vector3(node.Axis.X, 0, node.Axis.Z);
        Basis = TrackRenderer.Frame((axis.LengthSquared() > .00001f ? axis.Normalized() : Vector3.Back) * side);
        // Cancel the pole's lateral offset in its own frame: arrows sit over the rails.
        DirectionArrow.Position = Basis.Inverse() * (node.Position - Position + node.Axis * (side * .8f) + Vector3.Up * .8f);
        DirectionArrow.Basis = Basis.Inverse() * TrackRenderer.Frame(node.Axis * side);
    }
    public void SetAspect(RailSignalAspect aspect)
    {
        if (_aspect == aspect) return;
        _aspect = aspect;
        int active = aspect == RailSignalAspect.Red ? 0 : aspect == RailSignalAspect.Green ? 1 : 2;
        for (int i = 0; i < 3; i++) _lamps[i].MaterialOverride = active == i ? _lit[i] : _dark;
    }
}

public partial class SignalRenderer : Node3D
{
    public TrackNetwork Network = null!;
    public Func<bool>? DirectionVisibility;
    private readonly Dictionary<SignalKey, (RailSignalKind Kind, SignalVisual Visual)> _visuals = new();
    private readonly List<SignalKey> _removed = new();
    public override void _Ready() => Network.Space.Rebased += Rebased;
    public override void _ExitTree() => Network.Space.Rebased -= Rebased;
    private void Rebased(TrackVector shift)
    {
        // The follow camera may rebase after this renderer's process tick. Move
        // existing visuals now, before rendering, rather than waiting one frame.
        foreach (var (key, entry) in _visuals)
            if (Network.Nodes.TryGetValue(key.Node, out var node)) entry.Visual.Place(node, key.Side);
    }
    public SignalKey? Pick(Camera3D camera, Vector2 mouse)
    {
        var origin = camera.ProjectRayOrigin(mouse); var direction = camera.ProjectRayNormal(mouse);
        SignalKey? found = null; float distance = float.PositiveInfinity;
        foreach (var (key, entry) in _visuals)
            foreach (var mesh in entry.Visual.GetChildren().OfType<MeshInstance3D>())
            {
                var inverse = mesh.GlobalTransform.AffineInverse(); var local = inverse * origin; var ray = inverse.Basis * direction;
                if (!mesh.GetAabb().IntersectsSegment(local, local + ray * camera.Far)) continue;
                var faces = mesh.Mesh.GetFaces();
                for (int i = 0; i < faces.Length; i += 3)
                {
                    var hit = Geometry3D.RayIntersectsTriangle(local, ray, faces[i], faces[i + 1], faces[i + 2]);
                    if (hit.VariantType != Variant.Type.Vector3) continue;
                    float candidate = origin.DistanceSquaredTo(mesh.GlobalTransform * hit.AsVector3());
                    if (candidate < distance) { distance = candidate; found = key; }
                }
            }
        return found;
    }
    public override void _Process(double delta)
    {
        using var probe = FrameProbe.Measure("signals.render.update");
        _removed.Clear();
        foreach (var key in _visuals.Keys)
            if (!Network.Signals.ContainsKey(key) || !Network.Nodes.ContainsKey(key.Node)) _removed.Add(key);
        foreach (var key in _removed)
        { RemoveChild(_visuals[key].Visual); _visuals[key].Visual.QueueFree(); _visuals.Remove(key); }
        bool arrows = DirectionVisibility?.Invoke() == true;
        foreach (var (key, signal) in Network.Signals)
        {
            if (!Network.Nodes.TryGetValue(key.Node, out var node)) continue;
            if (!_visuals.TryGetValue(key, out var view) || view.Kind != signal.Kind)
            {
                if (view.Visual != null) { RemoveChild(view.Visual); view.Visual.QueueFree(); }
                var visual = new SignalVisual(signal.Kind); AddChild(visual); _visuals[key] = view = (signal.Kind, visual);
            }
            view.Visual.Place(node, key.Side); view.Visual.SetAspect(signal.Aspect);
            if (view.Visual.DirectionArrow.Visible != arrows) view.Visual.DirectionArrow.Visible = arrows;
        }
    }
}
