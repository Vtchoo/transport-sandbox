using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>Station windows share the live game world; platform sides remain purely visual.</summary>
public partial class StationWindows : Node
{
    public TrackNetwork Network = null!;
    public WindowManager Windows = null!;
    public TrackRenderer Renderer = null!;
    public readonly Dictionary<int, GameWindow> Views = new();
    private readonly Dictionary<int, (SubViewport Viewport, Camera3D Camera, Label Length)> _views = new();
    public override void _Ready() { Network.Changed += Sync; Network.Space.Rebased += Rebased; }
    private void Rebased(TrackVector worldShift) => Sync();
    public override void _ExitTree() { Network.Changed -= Sync; Network.Space.Rebased -= Rebased; }
    public void Open(int id)
    {
        if (id == 0 || !Network.Edges.Values.Any(e => e.Station == id))
            return;
        if (!Views.TryGetValue(id, out var window))
        {
            window = Windows.Create($"Station {id}", WindowPlacement.Center, new Vector2(420, 320), minimum: new Vector2(300, 240), scrollBody: false);
            Views[id] = window;
            var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new(0, 180), SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
            window.Body.AddChild(container);
            var viewport = new SubViewport { Size = new(384, 216), World3D = GetViewport().World3D, HandleInputLocally = false };
            container.AddChild(viewport);
            var camera = new Camera3D { Fov = 45, Far = 20000, Current = true };
            viewport.AddChild(camera);
            var label = UiSkin.Label(window.Body, "", 11, "e8eddf");
            _views[id] = (viewport, camera, label);
            window.StateChanged += () => viewport.RenderTargetUpdateMode = window.Visible && !window.IsCollapsed ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        }
        Refresh(id);
        window.Open();
    }
    private void Sync()
    {
        foreach (int id in Views.Keys.ToArray())
        {
            if (!Network.Edges.Values.Any(e => e.Station == id))
            {
                Views[id].Close();
                _views[id].Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            }
            else
                Refresh(id);
        }
    }
    private void Refresh(int id)
    {
        var edges = Network.Edges.Values.Where(e => e.Station == id).ToArray();
        if (edges.Length == 0)
            return;
        var remaining = edges.ToDictionary(e => e.Id);
        var lengths = new List<double>();
        while (remaining.Count > 0)
        {
            var pending = new Stack<TrackEdge>();
            var first = remaining.Values.First();
            pending.Push(first);
            remaining.Remove(first.Id);
            double length = 0;
            while (pending.TryPop(out var edge))
            {
                length += new TrackCurve(Network.WorldControls(edge)).Length;
                foreach (int node in new[] { edge.A, edge.B })
                    foreach (var other in Network.Incident(node))
                        if (other.Station == id && other.Gauge == edge.Gauge && TrackNetwork.SideAt(other, node) != TrackNetwork.SideAt(edge, node) && remaining.Remove(other.Id))
                            pending.Push(other);
            }
            lengths.Add(length);
        }
        var view = _views[id];
        double min = lengths.Min(), max = lengths.Max();
        view.Length.Text = Math.Abs(min - max) < .05 ? FormattableString.Invariant($"Station length: {max:F1} m") : FormattableString.Invariant($"Station length: {min:F1}-{max:F1} m");
        var points = edges.SelectMany(e => Network.Controls(e)).ToArray();
        var low = points.Aggregate((a, b) => a.Min(b));
        var high = points.Aggregate((a, b) => a.Max(b));
        var center = (low + high) / 2;
        float radius = Math.Max(15, (high - low).Length() * .65f);
        view.Camera.Position = center + new Vector3(radius, radius * .7f, radius);
        view.Camera.LookAt(center, Vector3.Up);
    }
    public int PickAt(Camera3D camera, Vector2 mouse)
    {
        float closest = float.PositiveInfinity;
        int station = 0;
        var origin = camera.ProjectRayOrigin(mouse);
        var direction = camera.ProjectRayNormal(mouse);
        foreach (var edge in Network.Edges.Values.Where(e => e.Station != 0))
        {
            // Reuse the rendered platform triangles, including curved/graded platforms.
            if (!Renderer.Holders.TryGetValue(edge.Id, out var visual))
                continue;
            foreach (var surface in visual.Geometry.Surfaces.Where(s => s.Name.StartsWith("Platform")))
                for (int i = 0; i < surface.Indices.Length; i += 3)
                {
                    var hit = Geometry3D.RayIntersectsTriangle(origin, direction,
                        visual.Position + surface.Vertices[surface.Indices[i]],
                        visual.Position + surface.Vertices[surface.Indices[i + 1]],
                        visual.Position + surface.Vertices[surface.Indices[i + 2]]);
                    if (hit.VariantType == Variant.Type.Vector3 && origin.DistanceSquaredTo(hit.AsVector3()) < closest)
                    {
                        closest = origin.DistanceSquaredTo(hit.AsVector3());
                        station = edge.Station;
                    }
                }
        }
        return station;
    }
    public bool OpenAt(Camera3D camera, Vector2 mouse)
    {
        int station = PickAt(camera, mouse);
        if (station == 0) return false;
        Open(station); return true;
    }
}
