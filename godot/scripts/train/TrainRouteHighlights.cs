using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>Read-only, throttled projection of one train's actual itinerary and detour provenance.</summary>
public partial class TrainRouteHighlights : Control
{
    public TrainWindows Trains = null!;
    private readonly List<(TrackVector[] Points, bool Detour)> _strokes = new();
    private readonly Dictionary<int, TrackCurve> _curves = new();
    private double _timer;
    private bool _dirty = true;
    private int _train = -1;
    public int LegCount => _strokes.Count;
    public int DetourLegCount => _strokes.Count(s => s.Detour);
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; ZIndex = -1;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); Hide();
        Trains.Editor.Network.Changed += Invalidate;
    }
    private void Invalidate() { _dirty = true; _curves.Clear(); }
    public override void _ExitTree() => Trains.Editor.Network.Changed -= Invalidate;
    public override void _Process(double delta)
    {
        var focused = Trains.FocusedTrain();
        bool enabled = focused?.View.DebugTab == true;
        int id = enabled ? focused!.Value.Id : -1;
        if (id != _train) { _train = id; _dirty = true; _strokes.Clear(); _curves.Clear(); }
        Visible = enabled;
        if (!enabled) return;
        _timer += delta;
        if (_dirty || _timer >= .2)
        {
            _dirty = false; _timer = 0;
            Rebuild(id, focused!.Value.View);
        }
        QueueRedraw();
    }
    private void Rebuild(int id, TrainView view)
    {
        _strokes.Clear();
        var parked = Trains.Editor.Parked.FirstOrDefault(p => p.Id == id);
        if (parked?.Route is not { } route)
        {
            view.DebugInfo.Text = parked == null ? "Train is not placed on track." : "No itinerary calculated yet.";
            return;
        }
        var train = Trains.Editor.Builder.Get(id);
        try
        {
            // Front-coupler sampling uses a private copy, including the random traversal state.
            var leading = route.Copy().Sample(parked.Position, train.TravelDirection * train.Length / 2).Position;
            var legs = route.Inspect(leading, train.TravelDirection, parked.Journey?.TargetPosition, out bool truncated);
            var metrics = route.Measure(legs, Trains.Editor.RoutingCostsReady ? Trains.Editor.RoutingConflictCount : null,
                train.Orders.RoutingPolicy, train.Orders.Current is { Kind: TrainOrderKind.Station } order ? order.Target : 0,
                penalizeStations: train.Orders.Items.Count > 0);
            var used = legs.Select(leg => leg.Edge).ToHashSet();
            foreach (int edge in _curves.Keys.Where(edge => !used.Contains(edge)).ToArray()) _curves.Remove(edge);
            foreach (var leg in legs)
            {
                if (!Trains.Editor.Network.Edges.TryGetValue(leg.Edge, out var edge)) continue;
                if (!_curves.TryGetValue(leg.Edge, out var curve))
                    _curves[leg.Edge] = curve = new(Trains.Editor.Network.WorldControls(edge));
                int count = Math.Clamp((int)Math.Ceiling(curve.Length * Math.Abs(leg.To - leg.From) / 8), 2, 128);
                var points = new TrackVector[count + 1];
                for (int i = 0; i <= count; i++)
                    points[i] = curve.PointAt(leg.From + (leg.To - leg.From) * i / count) + new TrackVector(0, .4, 0);
                _strokes.Add((points, leg.Detour));
            }
            string extent = truncated ? "Displayed" : train.Orders.Items.Count == 0 ? "Known" : "Remaining";
            string cost = metrics.Cost is { } value ? FormattableString.Invariant($"{value:N1} equivalent m") : "calculating...";
            view.DebugInfo.Text = FormattableString.Invariant($"{extent} route length: {metrics.Length:N1} m\n{extent} route cost: {cost}\n")
                + $"{LegCount} segments ahead - {DetourLegCount} detour segments\nPlan revision: {route.PlanRevision}\n{train.Orders.Status}"
                + (truncated ? "\nDisplay limited to the first 8,192 known segments." : "")
                + (LegCount == 0 ? "\nNo chosen track ahead." : "");
        }
        catch (InvalidOperationException)
        {
            _strokes.Clear(); view.DebugInfo.Text = "The itinerary no longer matches the supporting track.";
        }
    }
    public override void _Draw()
    {
        var camera = Trains.Editor.Camera;
        var space = Trains.Editor.Network.Space;
        var viewport = GetViewportRect().Grow(30);
        foreach (var (points, detour) in _strokes)
        {
            var color = new Color(detour ? "ffad58" : "73dce6");
            for (int i = 1; i < points.Length; i++)
            {
                var a = space.ToLocal(points[i - 1]); var b = space.ToLocal(points[i]);
                if (camera.IsPositionBehind(a) || camera.IsPositionBehind(b)) continue;
                var start = camera.UnprojectPosition(a); var end = camera.UnprojectPosition(b);
                if (!viewport.Intersects(new Rect2(start, end - start).Abs())) continue;
                DrawLine(start, end, new(color, .3f), 12, true);
                DrawLine(start, end, color, 4, true);
                if (i == points.Length / 2 && start.DistanceTo(end) > 8)
                {
                    var heading = (end - start).Normalized(); var normal = new Vector2(-heading.Y, heading.X);
                    DrawLine(end, end - heading * 9 + normal * 5, color, 3, true);
                    DrawLine(end, end - heading * 9 - normal * 5, color, 3, true);
                }
            }
        }
    }
}
