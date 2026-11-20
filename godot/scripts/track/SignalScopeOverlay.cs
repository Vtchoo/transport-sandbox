using Godot;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Absolute world strokes projected every frame; never participates in picking.</summary>
public partial class SignalScopeOverlay : Control
{
    public TrackNetwork Network = null!;
    public Camera3D Camera = null!;
    private readonly List<(TrackVector[] Points, Color Color, float Width, bool Glow)> _strokes = new();
    public int StrokeCount => _strokes.Count;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; ZIndex = -2;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); Hide();
    }
    public void Clear() { _strokes.Clear(); Hide(); }
    public void Add(int[] edges, Color color, float width = 4, bool glow = true)
    {
        foreach (int id in edges)
        {
            if (!Network.Edges.TryGetValue(id, out var edge)) continue;
            var curve = new TrackCurve(Network.WorldControls(edge));
            int count = System.Math.Clamp((int)(curve.Length / 6) + 1, 8, 256);
            var points = new TrackVector[count + 1];
            for (int i = 0; i <= count; i++) points[i] = curve.PointAt(i / (double)count) + new TrackVector(0, .35, 0);
            _strokes.Add((points, color, width, glow));
        }
    }
    public override void _Draw()
    {
        foreach (var (points, color, width, glow) in _strokes)
            for (int i = 1; i < points.Length; i++)
            {
                var a = Network.Space.ToLocal(points[i - 1]); var b = Network.Space.ToLocal(points[i]);
                if (Camera.IsPositionBehind(a) || Camera.IsPositionBehind(b)) continue;
                var start = Camera.UnprojectPosition(a); var end = Camera.UnprojectPosition(b);
                if (glow) DrawLine(start, end, new Color(color, .3f), 13, true);
                DrawLine(start, end, color, width, true);
            }
    }
}
