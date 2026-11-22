using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed record TrackHit(TrackEdge Edge, float T, Vector3 Point);
public partial class TrackRenderer : Node3D
{
    public const float RailHeadHeight = 0;
    public TrackNetwork Network = null!;
    public Camera3D Camera = null!;
    public TerrainGenerator? Generator;
    public BridgeRenderer Bridges = null!;
    public readonly Dictionary<int, TrackVisual> Holders = new();
    public bool Dirty = true, Dragging;
    public bool SpacingDrag;
    public Node3D Preview = null!;
    public int Selected;
    public Node3D Markers = new();
    public SignalRenderer Signals = null!;
    private readonly record struct RenderKey(TrackVector A, TrackVector B, TrackVector C, TrackVector D, int Gauge, string Platforms, bool RailsOnly, TrackSupport Support);
    private readonly Dictionary<int, RenderKey> _keys = new();
    private RenderKey[] _previewKeys = Array.Empty<RenderKey>();
    private readonly Dictionary<int, TrackCurve> _pickCurves = new();
    private readonly Dictionary<int, (MeshInstance3D Marker, MeshInstance3D Line)> _spacingHandles = new();
    private bool _markersWereVisible;
    private Vector3? _sleeperEye;
    public System.Collections.Generic.IEnumerable<int> SpacingNodes => _spacingHandles.Keys;
    public static StandardMaterial3D Material(Color color, float metal = 0) => new() { AlbedoColor = color, Metallic = metal, Roughness = 0.65f };
    private void MarkDirty()
    {
        Dirty = true;
        _pickCurves.Clear();
    }
    public override void _Ready()
    {
        Network.Changed += MarkDirty;
        Bridges = new() { Tracks = this }; AddChild(Bridges);
        Network.Space.Rebased += Rebased;
        AddChild(Markers);
        Signals = new() { Network = Network }; AddChild(Signals);
        Preview = new()
        {
            Name = "TrackPreview",
            Visible = false
        };
        AddChild(Preview);
    }
    public override void _ExitTree() { Network.Changed -= MarkDirty; Network.Space.Rebased -= Rebased; }
    private void Rebased(TrackVector worldShift)
    {
        using var probe = FrameProbe.Measure("origin.tracks");
        var shift = worldShift.Float();
        _pickCurves.Clear();
        foreach (var (id, holder) in Holders) if (Network.Edges.TryGetValue(id, out var edge)) holder.Position = Network.Nodes[edge.A].Position;
        foreach (var child in Preview.GetChildren()) if (child is Node3D node) node.Position -= shift;
        Bridges.Rebase();
        UpdateMarkers();
    }
    public override void _Process(double delta)
    {
        using var probe = FrameProbe.Measure("track.render.update");
        bool rebuilt = Dirty;
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
            Bridges.Sync();
            UpdateMarkers();
        }
        if (IsInstanceValid(Camera))
        {
            if (Markers.Visible && !_markersWereVisible) UpdateMarkers();
            _markersWereVisible = Markers.Visible;
            var eye = Camera.GlobalPosition;
            if (rebuilt || _sleeperEye != eye)
            {
                _sleeperEye = eye;
                foreach (var holder in Holders.Values) holder.UpdateSleeperLod(eye);
            }
            UpdateSpacingHandles();
        }
    }
    public static Basis Frame(Vector3 direction) => TrackFrame.From(TrackVector.From(direction)).Basis;
    private void BuildEdge(TrackEdge edge)
    {
        var world = Network.WorldControls(edge);
        var controls = world.Select(p => (p - world[0]).Float()).ToArray();
        bool affected = Network.Nodes.TryGetValue(Selected, out var selected) &&
            (edge.A == Selected || edge.B == Selected || !SpacingDrag && selected.Group != 0 &&
            (Network.Nodes[edge.A].Group == selected.Group || Network.Nodes[edge.B].Group == selected.Group));
        bool railsOnly = Dragging && affected;
        var key = new RenderKey(world[0], world[1], world[2], world[3], edge.Gauge, string.Join(",", edge.Platforms), railsOnly, edge.Support);
        if (_keys.TryGetValue(edge.Id, out var previous) && previous == key)
            return;
        if (Holders.TryGetValue(edge.Id, out var old))
            old.QueueFree();
        var holder = new TrackVisual(new TrackGeometry(controls, edge.Gauge, edge.Platforms, railsOnly: railsOnly)) { Position = Network.Nodes[edge.A].Position };
        Holders[edge.Id] = holder;
        _keys[edge.Id] = key;
        AddChild(holder);
    }
    public void UpdateMarkers()
    {
        foreach (var child in Markers.GetChildren())
        {
            Markers.RemoveChild(child);
            child.QueueFree();
        }
        _spacingHandles.Clear();
        if (!Visible || !Markers.Visible)
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
        foreach (int id in SpacingHandleNodes())
        {
            var material = Material(new("d19aff"));
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.NoDepthTest = true;
            material.RenderPriority = 20;
            var marker = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2 }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            var line = new MeshInstance3D { Mesh = new ImmediateMesh(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            Markers.AddChild(marker);
            Markers.AddChild(line);
            _spacingHandles[id] = (marker, line);
        }
        UpdateSpacingHandles();
    }
    public int[] SpacingHandleNodes()
    {
        if (!Network.Nodes.TryGetValue(Selected, out var selected) || selected.Group == 0)
            return Array.Empty<int>();
        var group = Network.Groups[selected.Group];
        return group.Offsets.Keys.Where(id => id != group.Anchor && (Selected == group.Anchor || id == Selected)).ToArray();
    }
    public Vector3 SpacingPosition(TrackNode node)
    {
        var group = Network.Groups[node.Group];
        var right = TrackNetwork.Right(group.Axis);
        var center = Camera.UnprojectPosition(node.Position);
        float pixels = center.DistanceTo(Camera.UnprojectPosition(node.Position + right));
        float units = Camera.Position.DistanceTo(node.Position) * 2 * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2)) / Math.Max(1, GetViewport().GetVisibleRect().Size.Y);
        float distance = pixels > .01f ? 25 / pixels : units * 25;
        float sign = group.Offsets[node.Id] < 0 ? -1 : 1;
        return node.Position + right * sign * Math.Max(.2f, distance);
    }
    private void UpdateSpacingHandles()
    {
        if (!IsInstanceValid(Camera))
            return;
        foreach (var (id, visual) in _spacingHandles)
        {
            if (!Network.Nodes.TryGetValue(id, out var node) || node.Group == 0)
                continue;
            var position = SpacingPosition(node);
            visual.Marker.Position = position;
            float units = Camera.Position.DistanceTo(position) * 2 * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2)) / Math.Max(1, GetViewport().GetVisibleRect().Size.Y);
            visual.Marker.Scale = Vector3.One * Math.Max(.08f, units * 5);
            bool visible = !Camera.IsPositionBehind(node.Position) && Camera.Position.DistanceTo(node.Position) <= 600;
            visual.Marker.Visible = visual.Line.Visible = visible;
            var mesh = (ImmediateMesh)visual.Line.Mesh;
            mesh.ClearSurfaces();
            mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            mesh.SurfaceAddVertex(node.Position);
            mesh.SurfaceAddVertex(position);
            mesh.SurfaceEnd();
        }
    }
    private void Marker(Vector3 point, float radius, Color color) => Markers.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = radius, Height = radius * 2 }, Position = point, MaterialOverride = Material(color) });
    public void ShowPreview(Vector3[] points, int gauge = 1435, TrackSupport support = TrackSupport.Fill) => ShowPreviews(new[] { points }, gauge, support);
    public void ShowPreviews(Vector3[][] curves, int gauge = 1435, TrackSupport support = TrackSupport.Fill)
    {
        var keys = curves.Select(p => new RenderKey(TrackVector.From(p[0]), TrackVector.From(p[1]), TrackVector.From(p[2]), TrackVector.From(p[3]), gauge, "", false, support)).ToArray();
        if (!_previewKeys.SequenceEqual(keys))
        {
            foreach (var child in Preview.GetChildren())
            {
                Preview.RemoveChild(child);
                child.QueueFree();
            }
            foreach (var points in curves)
                Preview.AddChild(new TrackVisual(new TrackGeometry(points, gauge, preview: true), preview: true));
            if (support == TrackSupport.ConcreteBridge && curves.Length > 0)
            {
                var temporary = new TrackNetwork { Space = Network.Space };
                foreach (var p in curves)
                {
                    var a = temporary.AddNode(p[0], gauge); var b = temporary.AddNode(p[3], gauge);
                    a.Axis = (p[1] - p[0]).Normalized(); b.Axis = (p[3] - p[2]).Normalized();
                    a.Group = 1; b.Group = 2;
                    int id = temporary.Edges.Count + 1;
                    temporary.Edges[id] = new() { Id = id, A = a.Id, B = b.Id, Gauge = gauge, SideA = 1, SideB = -1, LengthA = p[0].DistanceTo(p[1]), LengthB = p[3].DistanceTo(p[2]), Support = support };
                }
                var layout = new BridgeLayout(temporary, temporary.Edges[1]);
                var bridge = layout.Create((x, z) => Generator?.Sampler.Height(x, z) ?? 0, true); bridge.Position = curves[0][0]; Preview.AddChild(bridge);
            }
            _previewKeys = keys;
        }
        Preview.Visible = true;
    }
    public TrackHit? Closest(Camera3D camera, Vector2 mouse, float maximum = 14)
    {
        TrackHit? result = null;
        float distance = maximum;
        foreach (var edge in Network.Edges.Values)
        {
            if (!_pickCurves.TryGetValue(edge.Id, out var curve))
                _pickCurves[edge.Id] = curve = new TrackCurve(Network.PreciseControls(edge));
            int count = Math.Min(1024, Math.Max(32, (int)Math.Ceiling(curve.Length / 4)));
            var previousPoint = curve.Point(0).Float();
            var previous = camera.UnprojectPosition(previousPoint);
            for (int i = 1; i <= count; i++)
            {
                var nextPoint = curve.Point((double)i / count).Float();
                var next = camera.UnprojectPosition(nextPoint);
                if (!camera.IsPositionBehind(previousPoint) && !camera.IsPositionBehind(nextPoint))
                {
                    var line = next - previous;
                    float fraction = Mathf.Clamp((mouse - previous).Dot(line) / Math.Max(line.LengthSquared(), .000001f), 0, 1);
                    float d = mouse.DistanceTo(previous + line * fraction);
                    if (d < distance)
                    {
                        float t = (i - 1 + fraction) / count;
                        var point = curve.Point(t).Float();
                        if (point.DistanceTo(Network.Nodes[edge.A].Position) >= 1 && point.DistanceTo(Network.Nodes[edge.B].Position) >= 1)
                        {
                            distance = d;
                            result = new(edge, t, point);
                        }
                    }
                }
                previous = next;
                previousPoint = nextPoint;
            }
        }
        return result;
    }
}
