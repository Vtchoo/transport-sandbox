using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

/// <summary>One deck between the outside edges of matching parallel tracks; upright piers reach natural ground.</summary>
public sealed class BridgeLayout
{
    public readonly TrackEdge Edge;
    public readonly TrackCurve Curve;
    private readonly (TrackEdge Edge, TrackCurve Curve, bool Forward)[] _lanes;
    public readonly TrackVector Anchor;
    public BridgeLayout(TrackNetwork network, TrackEdge edge)
    {
        Edge = edge; Curve = new(network.WorldControls(edge)); Anchor = Curve.Controls[0];
        _lanes = network.SupportPeers(edge).Select(e => (e, new TrackCurve(network.WorldControls(e)),
            e.A == edge.A || network.Nodes[e.A].Group != 0 && network.Nodes[e.A].Group == network.Nodes[edge.A].Group)).ToArray();
    }
    public (TrackVector Left, TrackVector Right) Bounds(double along)
    {
        var center = Curve.PointAt(along); var right = Curve.FrameAt(along).Right;
        TrackVector leftPoint = center, rightPoint = center; double low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (var (edge, curve, forward) in _lanes)
        {
            double u = forward ? along : 1 - along;
            var p = curve.PointAt(u); var frame = curve.FrameAt(u);
            foreach (int sign in new[] { -1, 1 })
            {
                double width = 2.4;
                if (edge.Platforms.Contains(sign)) width = Math.Max(width, Math.Max(1.75, edge.Gauge / 2000.0 + 1.05) + 3.2);
                var point = p + frame.Right * (sign * width) + frame.Up * -.65;
                var d = point - center; double offset = d.X * right.X + d.Y * right.Y + d.Z * right.Z;
                if (offset < low) { low = offset; leftPoint = point; }
                if (offset > high) { high = offset; rightPoint = point; }
            }
        }
        return (leftPoint, rightPoint);
    }
    public BridgePier[] Piers => Edge.BridgePiers?.ToArray() ?? TrackNetwork.AutomaticPiers(Curve.Length);
    public TrackVector PierPoint(BridgePier pier)
    {
        var (left, right) = Bounds(pier.Along);
        return (left + right) * .5 + new TrackVector(0, -1.2, 0);
    }
    public BridgePier Closest(TrackVector point)
    {
        double best = double.PositiveInfinity, along = .5;
        int steps = Math.Clamp((int)Math.Ceiling(Curve.Length / 2), 32, 2048);
        var initial = Bounds(0); var previous = (initial.Left + initial.Right) * .5;
        for (int i = 1; i <= steps; i++)
        {
            var bounds = Bounds((double)i / steps); var current = (bounds.Left + bounds.Right) * .5;
            double dx = current.X - previous.X, dz = current.Z - previous.Z, denominator = dx * dx + dz * dz;
            double fraction = denominator > 1e-12 ? Math.Clamp(((point.X - previous.X) * dx + (point.Z - previous.Z) * dz) / denominator, 0, 1) : 0;
            double d = Math.Pow(point.X - previous.X - dx * fraction, 2) + Math.Pow(point.Z - previous.Z - dz * fraction, 2);
            if (d < best) { best = d; along = Math.Clamp((i - 1 + fraction) / steps, .001, .999); }
            previous = current;
        }
        return new(along);
    }
    public Node3D Create(Func<double, double, double> terrain, bool preview = false)
    {
        var holder = new Node3D { Name = "ConcreteBridge" };
        var concrete = TrackRenderer.Material(new("8c908b")); concrete.Roughness = .95f;
        concrete.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        if (preview) { concrete.AlbedoColor = new Color("7aefc1") { A = .3f }; concrete.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; concrete.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled; }
        var vertices = new List<Vector3>(); var normals = new List<Vector3>();
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = (b - a).Cross(c - a).Normalized();
            vertices.AddRange(new[] { a, c, b }); normals.AddRange(new[] { normal, normal, normal });
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Triangle(a, b, c); Triangle(a, c, d); }
        int steps = Math.Max(2, (int)Math.Ceiling(Curve.Length / 2));
        var previous = Bounds(0);
        for (int i = 1; i <= steps; i++)
        {
            var current = Bounds((double)i / steps);
            var a = (previous.Left - Anchor).Float(); var b = (previous.Right - Anchor).Float();
            var c = (current.Right - Anchor).Float(); var d = (current.Left - Anchor).Float();
            var down = Vector3.Down * .6f;
            Quad(a, d, c, b); Quad(a + down, b + down, c + down, d + down);
            Quad(a, a + down, d + down, d); Quad(b, c, c + down, b + down);
            if (i == 1) Quad(a, b, b + down, a + down);
            if (i == steps) Quad(d, d + down, c + down, c);
            previous = current;
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        holder.AddChild(new MeshInstance3D { Name = "Deck", Mesh = mesh, MaterialOverride = concrete });
        void Box(string name, Vector3 size, Vector3 position, Basis basis)
        {
            holder.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = concrete, Transform = new(basis, position) });
        }
        int index = 0;
        foreach (var pier in Piers)
        {
            var point = PierPoint(pier); var bounds = Bounds(pier.Along); var mid = (bounds.Left + bounds.Right) * .5;
            double bottom = terrain(point.X, point.Z) - .4, top = point.Y;
            if (top <= bottom) { index++; continue; }
            var horizontal = Curve.FrameAt(pier.Along).Forward with { Y = 0 };
            var basis = TrackFrame.From(horizontal).Basis;
            Box($"Pier_{index}", new(1.2f, (float)(top - bottom), 1.6f), (new TrackVector(point.X, (top + bottom) / 2, point.Z) - Anchor).Float(), basis);
            Box($"Cap_{index}", new((float)(bounds.Right - bounds.Left).Length, .6f, 2), (mid + new TrackVector(0, -.9, 0) - Anchor).Float(), basis);
            index++;
        }
        return holder;
    }
}

public partial class BridgeRenderer : Node3D
{
    public TrackRenderer Tracks = null!;
    public readonly Dictionary<int, Node3D> Holders = new();
    private readonly Dictionary<int, string> _keys = new();
    public void Sync()
    {
        var network = Tracks.Network; var active = new HashSet<int>();
        foreach (var edge in network.Edges.Values.Where(e => e.Support == TrackSupport.ConcreteBridge).OrderBy(e => e.Id))
        {
            var peers = network.SupportPeers(edge); if (peers[0].Id != edge.Id) continue;
            active.Add(edge.Id);
            string key = string.Join(";", peers.Select(e => $"{e.Id}:{string.Join(',', network.WorldControls(e))}:{string.Join(',', e.Platforms)}"))
                + ":" + (edge.BridgePiers == null ? "auto" : string.Join(';', edge.BridgePiers));
            if (_keys.GetValueOrDefault(edge.Id) == key) continue;
            if (Holders.Remove(edge.Id, out var old)) { RemoveChild(old); old.QueueFree(); }
            var layout = new BridgeLayout(network, edge);
            var visual = layout.Create((x, z) => Tracks.Generator?.Sampler.Height(x, z) ?? 0);
            visual.Position = network.Nodes[edge.A].Position; AddChild(visual);
            Holders[edge.Id] = visual; _keys[edge.Id] = key;
        }
        foreach (int id in Holders.Keys.Where(id => !active.Contains(id)).ToArray())
        { var old = Holders[id]; RemoveChild(old); old.QueueFree(); Holders.Remove(id); _keys.Remove(id); }
    }
    public void Rebase()
    {
        foreach (var (id, visual) in Holders) if (Tracks.Network.Edges.TryGetValue(id, out var edge)) visual.Position = Tracks.Network.Nodes[edge.A].Position;
    }
}
