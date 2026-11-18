using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

public sealed record TrackHit(TrackEdge Edge, float T, Vector3 Point);
public partial class TrackRenderer : Node3D
{
    public const float RailHeadHeight = 0.13f + 0.15f / 2;
    public TrackNetwork Network = null!;
    public Camera3D Camera = null!;
    public readonly Dictionary<int, Node3D> Holders = new();
    public bool Dirty = true, Dragging;
    private int _lastLod = -1;
    public MeshInstance3D Preview = null!;
    public int Selected;
    public Node3D Markers = new();
    private readonly StandardMaterial3D _railMaterial = Material(new(0.42f, 0.44f, 0.44f), 0.8f), _sleeperMaterial = Material(new(0.27f, 0.22f, 0.17f)), _ballastMaterial = Material(new(0.38f, 0.36f, 0.33f)), _platformMaterial = Material(new(0.55f, 0.53f, 0.47f));
    public static StandardMaterial3D Material(Color color, float metal = 0) => new() { AlbedoColor = color, Metallic = metal, Roughness = 0.65f };
    private void MarkDirty() => Dirty = true;
    public override void _Ready()
    {
        Network.Changed += MarkDirty;
        AddChild(Markers);
        var mat = Material(new(0.3f, 0.85f, 0.7f, 0.45f));
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        Preview = new()
        {
            MaterialOverride = mat
        };
        AddChild(Preview);
    }
    public override void _ExitTree() => Network.Changed -= MarkDirty;
    public override void _Process(double delta)
    {
        if (IsInstanceValid(Camera))
        {
            int lod = (int)(Camera.Position.Y / 100);
            if (lod != _lastLod)
            {
                _lastLod = lod;
                Dirty = true;
            }
        }
        if (!Dirty)
            return;
        Dirty = false;
        foreach (var holder in Holders.Values)
            holder.QueueFree();
        Holders.Clear();
        foreach (var edge in Network.Edges.Values)
            BuildEdge(edge);
        UpdateMarkers();
    }
    private static void Boxes(Node3D parent, Vector3 dimensions, Material material, List<Transform3D> transforms)
    {
        if (transforms.Count == 0)
            return;
        var box = new BoxMesh { Size = dimensions, Material = material };
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = box, InstanceCount = transforms.Count };
        for (int i = 0; i < transforms.Count; i++)
            multi.SetInstanceTransform(i, transforms[i]);
        parent.AddChild(new MultiMeshInstance3D { Multimesh = multi });
    }
    public static Basis Frame(Vector3 direction)
    {
        var right = TrackNetwork.Right(direction);
        return new(right, direction.Cross(right).Normalized(), direction.Normalized());
    }
    private void BuildEdge(TrackEdge edge)
    {
        var holder = new Node3D();
        Holders[edge.Id] = holder;
        AddChild(holder);
        using var curve = Network.Curve(edge);
        float length = curve.GetBakedLength();
        int count = Math.Max(1, Mathf.CeilToInt(length / 2));
        var rails = new List<Transform3D>();
        var bed = new List<Transform3D>();
        var platforms = new List<Transform3D>();
        for (int i = 0; i < count; i++)
        {
            var p = curve.SampleBaked(length * i / count);
            var q = curve.SampleBaked(length * (i + 1) / count);
            var direction = (q - p).Normalized();
            var basis = Frame(direction).ScaledLocal(new(1, 1, p.DistanceTo(q) + 0.02f));
            var center = (p + q) * 0.5f;
            foreach (int side in new[] { -1, 1 })
                rails.Add(new(basis, center + TrackNetwork.Right(direction) * side * (edge.Gauge / 2000f + 0.035f) + Vector3.Up * 0.13f));
            bed.Add(new(basis, center - Vector3.Up * 0.12f));
            foreach (int side in edge.Platforms)
                platforms.Add(new(basis, center + TrackNetwork.Right(direction) * side * (Math.Max(1.75f, edge.Gauge / 2000f + 1.05f) + 1.5f) + Vector3.Up * 0.3f));
        }
        Boxes(holder, new(0.075f, 0.15f, 1), _railMaterial, rails);
        Boxes(holder, new(edge.Gauge / 1000f + 1.8f, 0.2f, 1), _ballastMaterial, bed);
        Boxes(holder, new(3, 0.9f, 1), _platformMaterial, platforms);
        var middle = curve.SampleBaked(length / 2);
        float distance = IsInstanceValid(Camera) ? Camera.Position.DistanceTo(middle) : 0;
        bool affected = Network.Nodes.TryGetValue(Selected, out var selected) && (edge.A == Selected || edge.B == Selected || selected.Group != 0 && (Network.Nodes[edge.A].Group == selected.Group || Network.Nodes[edge.B].Group == selected.Group));
        if (distance < 900 && !(Dragging && affected))
        {
            var ties = new List<Transform3D>();
            float spacing = distance < 180 ? 0.65f : distance < 450 ? 2.6f : 7.8f;
            for (float s = 0; s < length; s += spacing)
            {
                var p = curve.SampleBaked(s);
                var tangent = (curve.SampleBaked(Math.Min(s + 0.3f, length)) - curve.SampleBaked(Math.Max(0, s - 0.3f))).Normalized();
                ties.Add(new(Frame(tangent), p));
            }
            Boxes(holder, new(edge.Gauge / 1000f + 1, 0.13f, 0.23f), _sleeperMaterial, ties);
        }
    }
    public void UpdateMarkers()
    {
        foreach (var child in Markers.GetChildren())
            child.QueueFree();
        if (!Visible)
            return;
        foreach (var node in Network.Nodes.Values)
        {
            if (IsInstanceValid(Camera) && Camera.Position.DistanceTo(node.Position) > 800)
                continue;
            Marker(node.Position, 0.6f, node.Id == Selected ? new(0.95f, 0.72f, 0.16f) : new(0.2f, 0.65f, 0.9f));
        }
        if (!Network.Nodes.ContainsKey(Selected))
            return;
        foreach (var edge in Network.Incident(Selected))
        {
            var points = Network.Controls(edge);
            var control = points[edge.A == Selected ? 1 : 2];
            Marker(control, 0.45f, new(0.95f, 0.5f, 0.18f));
            var line = new ImmediateMesh();
            line.SurfaceBegin(Mesh.PrimitiveType.Lines);
            line.SurfaceAddVertex(Network.Nodes[Selected].Position);
            line.SurfaceAddVertex(control);
            line.SurfaceEnd();
            Markers.AddChild(new MeshInstance3D { Mesh = line, MaterialOverride = Material(new(1, 0.6f, 0.1f)) });
        }
    }
    private void Marker(Vector3 point, float radius, Color color) => Markers.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = radius, Height = radius * 2 }, Position = point, MaterialOverride = Material(color) });
    public void ShowPreview(Vector3[] points)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        for (int i = 0; i < 65; i++)
            mesh.SurfaceAddVertex(TrackNetwork.Bezier(points, i / 64f) + Vector3.Up * 0.2f);
        mesh.SurfaceEnd();
        Preview.Mesh = mesh;
        Preview.Visible = true;
    }
    public TrackHit? Closest(Camera3D camera, Vector2 mouse, float maximum = 14)
    {
        TrackHit? result = null;
        float distance = maximum;
        foreach (var edge in Network.Edges.Values)
        {
            int count = Math.Max(20, Mathf.CeilToInt(Network.Nodes[edge.A].Position.DistanceTo(Network.Nodes[edge.B].Position) / 3));
            for (int i = 1; i < count; i++)
            {
                float t = (float)i / count;
                var point = Network.Point(edge, t);
                if (camera.IsPositionBehind(point))
                    continue;
                float d = mouse.DistanceTo(camera.UnprojectPosition(point));
                if (d < distance)
                {
                    distance = d;
                    result = new(edge, t, point);
                }
            }
        }
        return result;
    }
}
