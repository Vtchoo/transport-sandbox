using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>Catchment follows platform geometry. Only an active industry/station window or station tool shows it.</summary>
public partial class IndustryOverlay : Control
{
    public IndustryWorld Industries = null!;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; ZIndex = -1; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        var world = Industries; var simulation = world.Simulation;
        if (world.Windows.HasModal) return;
        int station = world.TrackEditor.Stations.Views.FirstOrDefault(p => p.Value == world.Windows.Active && p.Value.Visible && !p.Value.IsCollapsed).Key;
        string? industry = world.Views.FirstOrDefault(p => p.Value == world.Windows.Active && p.Value.Visible && !p.Value.IsCollapsed).Key;
        var lines = new List<StationPolyline>();
        if (station != 0) lines.AddRange(simulation.Platforms.Where(p => p.Station == station));
        else if (world.TrackEditor.Enabled && world.TrackEditor.Mode == "station" && world.TrackEditor.PlatformTarget is { } target)
        {
            var curve = new TrackCurve(world.Network.WorldControls(target.Edge));
            lines.Add(new(target.Edge.Station, Enumerable.Range(0, 17).Select(i => curve.PointAt(i / 16.0)).ToArray()));
        }
        if (industry != null)
            lines.AddRange(simulation.Platforms.Where(p => simulation.Connections.GetValueOrDefault(p.Station)?.Contains(industry) == true));
        if (lines.Count == 0 && industry == null) return;
        var radius = simulation.Catalog.CatchmentRadiusM;
        void Line(TrackVector a, TrackVector b, Color color, float width = 2)
        {
            var start = world.Network.Space.ToLocal(a); var end = world.Network.Space.ToLocal(b);
            if (!world.Camera.IsPositionBehind(start) && !world.Camera.IsPositionBehind(end)) DrawLine(world.Camera.UnprojectPosition(start), world.Camera.UnprojectPosition(end), color, width, true);
        }
        void Circle(TrackVector center, double r, Color color)
        {
            var prev = center + new TrackVector(r, 1, 0);
            for (int i = 1; i <= 48; i++)
            { double angle = i * Math.Tau / 48; var next = center + new TrackVector(Math.Cos(angle) * r, 1, Math.Sin(angle) * r); Line(prev, next, color, 1); prev = next; }
        }
        foreach (var line in lines)
        {
            for (int i = 1; i < line.Points.Length; i++) Line(line.Points[i - 1], line.Points[i], new("7ce8df"), 5);
            for (int i = 0; i < line.Points.Length; i += Math.Max(1, line.Points.Length / 4)) Circle(line.Points[i], radius, new("86d9c37f"));
        }
        if (industry != null) Circle(simulation.Industries[industry].Position, radius, new("86d9c37f"));
        foreach (var state in simulation.Industries.Values)
        {
            bool connected = lines.Any(p => IndustrySimulation.DistanceTo(p.Points, state.Position) <= radius);
            if (!connected && state.Id != industry) continue;
            var point = world.Network.Space.ToLocal(state.Position + new TrackVector(0, 22, 0));
            if (world.Camera.IsPositionBehind(point)) continue;
            var screen = world.Camera.UnprojectPosition(point); var color = new Color(connected ? "a8f2aa" : "f5df91");
            DrawCircle(screen, 9, color, false, 2, true);
            DrawString(ThemeDB.FallbackFont, screen + new Vector2(13, -6), simulation.Catalog.Types[state.Type].Name, fontSize: 14, modulate: color);
        }
    }
}
