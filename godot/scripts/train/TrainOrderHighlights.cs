using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>Map overlay for the focused train's route. Absolute geometry survives floating-origin shifts.</summary>
public partial class TrainOrderHighlights : Control
{
    public TrainWindows Trains = null!;
    private sealed record Marker(TrainOrderKind Kind, TrackVector Point, string Text, bool Current);
    private sealed record Stroke(TrackVector[] Points, bool Current);
    private readonly List<Marker> _markers = new();
    private readonly List<Stroke> _strokes = new();
    private int _train = -1, _revision = -1, _current = -1;
    private bool _dirty = true;
    private readonly StyleBoxFlat _badge = UiSkin.Box("20382f", "91a580", 4, 4, 1);
    public int MarkerCount => _markers.Count;
    public int StationStrokeCount => _strokes.Count;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = -1; // Above the world, behind the game windows and HUD.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Trains.Editor.Network.Changed += Invalidate;
    }
    private void Invalidate() => _dirty = true;
    public override void _ExitTree() => Trains.Editor.Network.Changed -= Invalidate;
    public override void _Process(double delta)
    {
        var focused = Trains.FocusedTrain();
        var train = focused is { } entry && entry.View.RoutesTab
            ? Trains.Editor.Builder.Trains.FirstOrDefault(t => t.Id == entry.Id) : null;
        int id = train?.Id ?? -1, revision = train?.Orders.Revision ?? -1, current = train?.Orders.Current?.Id ?? -1;
        if (_dirty || id != _train || revision != _revision || current != _current)
        {
            _train = id; _revision = revision; _current = current; _dirty = false;
            Rebuild(train);
        }
        Visible = train != null;
        QueueRedraw();
    }
    private void Rebuild(TrainConsist? train)
    {
        _markers.Clear(); _strokes.Clear();
        if (train == null) return;
        var network = Trains.Editor.Network;
        // Repeated destinations share a badge with every corresponding order number.
        foreach (var group in train.Orders.Items.Select((order, i) => (Order: order, Number: i + 1))
            .Where(item => item.Order.Kind is TrainOrderKind.Station or TrainOrderKind.Waypoint).GroupBy(item => (item.Order.Kind, item.Order.Target)))
        {
            bool active = group.Any(item => item.Order.Id == _current);
            string numbers = string.Join(", ", group.Select(item => item.Number));
            if (group.Key.Kind == TrainOrderKind.Waypoint)
            {
                if (network.Nodes.TryGetValue(group.Key.Target, out var node))
                    _markers.Add(new(group.Key.Kind, node.WorldPosition, $"{numbers}  Waypoint {node.Id}", active));
                continue;
            }
            var edges = network.Edges.Values.Where(edge => edge.Station == group.Key.Target).OrderBy(edge => edge.Id).ToArray();
            foreach (var edge in edges)
            {
                var controls = network.WorldControls(edge);
                int count = Math.Clamp((int)((controls[3] - controls[0]).Length / 8) + 8, 16, 256);
                var points = new TrackVector[count + 1];
                for (int i = 0; i <= count; i++)
                {
                    double t = (double)i / count, s = 1 - t;
                    points[i] = controls[0] * (s * s * s) + controls[1] * (3 * s * s * t)
                        + controls[2] * (3 * s * t * t) + controls[3] * (t * t * t);
                }
                _strokes.Add(new(points, active));
            }
            if (edges.Length > 0)
            {
                var controls = network.WorldControls(edges[0]);
                var point = (controls[0] + controls[1] * 3 + controls[2] * 3 + controls[3]) * .125;
                _markers.Add(new(group.Key.Kind, point, $"{numbers}  Station {group.Key.Target}", active));
            }
        }
    }
    public override void _Draw()
    {
        var camera = Trains.Editor.Camera;
        var space = Trains.Editor.Network.Space;
        Color ColorFor(bool active) => active ? new("ffe28a") : new("73dce6");
        foreach (var stroke in _strokes)
            for (int i = 1; i < stroke.Points.Length; i++)
            {
                var a = space.ToLocal(stroke.Points[i - 1]); var b = space.ToLocal(stroke.Points[i]);
                if (camera.IsPositionBehind(a) || camera.IsPositionBehind(b)) continue;
                var start = camera.UnprojectPosition(a); var end = camera.UnprojectPosition(b);
                var color = ColorFor(stroke.Current);
                DrawLine(start, end, new(color, .25f), 12, true);
                DrawLine(start, end, color, 3, true);
            }
        var font = ThemeDB.FallbackFont;
        foreach (var marker in _markers)
        {
            var point = space.ToLocal(marker.Point);
            if (camera.IsPositionBehind(point)) continue;
            var position = camera.UnprojectPosition(point);
            if (!GetViewportRect().Grow(100).HasPoint(position)) continue;
            var color = ColorFor(marker.Current);
            DrawArc(position, 10, 0, Mathf.Tau, 32, color, 3, true);
            var origin = position + new Vector2(15, -34);
            var bounds = new Rect2(origin, new Vector2(font.GetStringSize(marker.Text, fontSize: 14).X + 36, 28));
            DrawStyleBox(_badge, bounds);
            DrawTextureRect(UiSkin.Icon(marker.Kind == TrainOrderKind.Station ? "station" : "waypoint"), new(origin + new Vector2(5, 5), new Vector2(18, 18)), false, color);
            DrawString(font, origin + new Vector2(29, 19), marker.Text, fontSize: 14, modulate: color);
        }
    }
}
