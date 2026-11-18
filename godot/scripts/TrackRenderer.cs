using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

public sealed record TrackHit(TrackEdge Edge, float T, Vector3 Point);
public partial class TrackRenderer : Node3D
{
    public const float RailHeadHeight = 0;
    public TrackNetwork Network = null!;
    public Camera3D Camera = null!;
    public readonly Dictionary<int, TrackVisual> Holders = new();
    public bool Dirty = true, Dragging;
    public Node3D Preview = null!;
    public int Selected;
    public Node3D Markers = new();
    private readonly record struct RenderKey(Vector3 A, Vector3 B, Vector3 C, Vector3 D, int Gauge, string Platforms, bool RailsOnly);
    private readonly Dictionary<int, RenderKey> _keys = new();
    private RenderKey? _previewKey;
    private TrackVisual? _previewVisual;
    public static StandardMaterial3D Material(Color color, float metal = 0) => new() { AlbedoColor = color, Metallic = metal, Roughness = 0.65f };
    private void MarkDirty() => Dirty = true;
    public override void _Ready()
    {
        Network.Changed += MarkDirty;
        AddChild(Markers);
        Preview = new()
        {
            Name = "TrackPreview",
            Visible = false
        };
        AddChild(Preview);
    }
    public override void _ExitTree() => Network.Changed -= MarkDirty;
    public override void _Process(double delta)
    {
        if (Dirty)
        {
            Dirty = false;
            foreach (int id in new List<int>(Holders.Keys))
                if (!Network.Edges.ContainsKey(id))
                {
                    Holders[id].QueueFree();
                    Holders.Remove(id);
                    _keys.Remove(id);
                }
            foreach (var edge in Network.Edges.Values)
                BuildEdge(edge);
            UpdateMarkers();
        }
        if (IsInstanceValid(Camera))
            foreach (var holder in Holders.Values)
                holder.UpdateSleeperLod(Camera.GlobalPosition);
    }
    public static Basis Frame(Vector3 direction) => TrackFrame.From(TrackVector.From(direction)).Basis;
    private void BuildEdge(TrackEdge edge)
    {
        var controls = Network.Controls(edge);
        bool affected = Network.Nodes.TryGetValue(Selected, out var selected) &&
            (edge.A == Selected || edge.B == Selected || selected.Group != 0 &&
            (Network.Nodes[edge.A].Group == selected.Group || Network.Nodes[edge.B].Group == selected.Group));
        bool railsOnly = Dragging && affected;
        var key = new RenderKey(controls[0], controls[1], controls[2], controls[3], edge.Gauge, string.Join(",", edge.Platforms), railsOnly);
        if (_keys.TryGetValue(edge.Id, out var previous) && previous == key)
            return;
        if (Holders.TryGetValue(edge.Id, out var old))
            old.QueueFree();
        var holder = new TrackVisual(new TrackGeometry(controls, edge.Gauge, edge.Platforms, railsOnly: railsOnly));
        Holders[edge.Id] = holder;
        _keys[edge.Id] = key;
        AddChild(holder);
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
    public void ShowPreview(Vector3[] points, int gauge = 1435)
    {
        var key = new RenderKey(points[0], points[1], points[2], points[3], gauge, "", false);
        if (_previewKey != key)
        {
            if (IsInstanceValid(_previewVisual))
            {
                Preview.RemoveChild(_previewVisual!);
                _previewVisual!.QueueFree();
            }
            _previewVisual = new TrackVisual(new TrackGeometry(points, gauge, preview: true), preview: true);
            Preview.AddChild(_previewVisual);
            _previewKey = key;
        }
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
