using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

public sealed record ExhaustOutlet(Vector3 Position, bool Steam, double? IdleRate = null);
public sealed class SmokeParticle
{
    public Vector3 Position, Velocity;
    public double Age, Life, Radius, Density, Shade, Growth, Phase;
}
public partial class MenuSmoke : Node3D
{
    public readonly List<ExhaustOutlet> Outlets = new();
    public readonly List<SmokeParticle> Particles = new();
    public Camera3D Camera = null!;
    private readonly List<double> _carry = new();
    private uint _sequence = 1;
    private MeshInstance3D _visual = null!;
    private double RandomValue()
    {
        _sequence = unchecked(_sequence * 1664525 + 1013904223);
        return _sequence / 4294967296.0;
    }
    public override void _Ready()
    {
        foreach (var outlet in Outlets)
            _carry.Add(0);
        _visual = new()
        {
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/menu_smoke.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_visual);
    }
    public void Advance(double delta)
    {
        double dt = Math.Clamp(delta, 0, 0.1);
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                Particles.RemoveAt(i);
                continue;
            }
            p.Velocity = new((float)(p.Velocity.X + (0.65 - p.Velocity.X) * dt * 0.6), (float)(p.Velocity.Y + (1.8 - p.Velocity.Y) * dt * 0.4), (float)(p.Velocity.Z + (0.25 - p.Velocity.Z) * dt * 0.6));
            p.Position += p.Velocity * (float)dt;
        }
        for (int i = 0; i < Outlets.Count; i++)
        {
            var outlet = Outlets[i];
            var position = outlet.Position;
            bool steam = outlet.Steam;
            if (position.DistanceSquaredTo(Camera.GlobalPosition) >= 2500 * 2500)
            {
                _carry[i] = 0;
                continue;
            }
            _carry[i] += dt * Math.Max(0, outlet.IdleRate ?? (steam ? 2.2 : 1.2));
            int amount = (int)Math.Floor(_carry[i]);
            _carry[i] -= amount;
            for (int puff = 0; puff < amount; puff++)
            {
                if (Particles.Count >= 256)
                    break;
                var p = new SmokeParticle { Position = position };
                p.Position.X += (float)((RandomValue() - 0.5) * 0.14);
                p.Position.Z += (float)((RandomValue() - 0.5) * 0.14);
                p.Velocity.X = (float)((RandomValue() - 0.5) * 0.5);
                p.Velocity.Y = (float)((steam ? 1.4 : 1) + RandomValue() * 0.5);
                p.Velocity.Z = (float)((RandomValue() - 0.5) * 0.5);
                p.Life = 2.4 + RandomValue() * 1.2;
                p.Radius = 0.12 + RandomValue() * 0.1;
                p.Density = steam ? 0.1 : 0.065;
                p.Shade = steam ? 0.43 : 0.32;
                p.Growth = steam ? 0.28 : 0.18;
                p.Phase = RandomValue() * Math.Tau;
                Particles.Add(p);
            }
        }
    }
    public override void _Process(double delta)
    {
        Advance(delta);
        Particles.Sort((a, b) => b.Position.DistanceSquaredTo(Camera.GlobalPosition).CompareTo(a.Position.DistanceSquaredTo(Camera.GlobalPosition)));
        if (Particles.Count == 0)
        {
            _visual.Hide();
            return;
        }
        _visual.Show();
        var vertices = new Vector3[Particles.Count * 4];
        var uvs = new Vector2[vertices.Length];
        var appearances = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var indices = new int[Particles.Count * 6];
        var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) };
        for (int index = 0; index < Particles.Count; index++)
        {
            var p = Particles[index];
            double age = p.Age / p.Life, size = p.Radius + p.Age * p.Growth;
            double depth = Math.Max(0.1, -(Camera.GlobalTransform.AffineInverse() * p.Position).Z), pixelScale = GetViewport().GetVisibleRect().Size.Y / Math.Tan(Camera.Fov * Math.PI / 360) / depth;
            size = Math.Min(size, 128 / pixelScale);
            double alpha = p.Density * Math.Min(1, p.Age / 0.1) * Math.Pow(1 - age, 1.6), shade = p.Shade + age * 0.25;
            int start = index * 4;
            for (int c = 0; c < 4; c++)
            {
                var corner = corners[c];
                vertices[start + c] = p.Position + Camera.GlobalBasis.X * corner.X * (float)size + Camera.GlobalBasis.Y * corner.Y * (float)size;
                uvs[start + c] = new(corner.X * 0.5f + 0.5f, -corner.Y * 0.5f + 0.5f);
                appearances[start + c] = new((float)(p.Phase + p.Age * 0.3), (float)alpha);
                colors[start + c] = new((float)shade, (float)shade, (float)shade);
            }
            int[] faces = { start, start + 2, start + 1, start + 1, start + 2, start + 3 };
            Array.Copy(faces, 0, indices, index * 6, 6);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = appearances;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _visual.Mesh = mesh;
    }
}
