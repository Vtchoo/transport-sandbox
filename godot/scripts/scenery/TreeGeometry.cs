using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Original tree primitives, with Three.js vertex conventions and Godot face winding.</summary>
public static class TreeGeometry
{
    private readonly record struct V(double X, double Y, double Z)
    {
        public V Lerp(V b, double t) => new(X + (b.X - X) * t, Y + (b.Y - Y) * t, Z + (b.Z - Z) * t);
        public V Unit() { double d = Math.Sqrt(X * X + Y * Y + Z * Z); return new(X / d, Y / d, Z / d); }
        public Vector3 Float() => new((float)X, (float)Y, (float)Z);
    }
    private sealed class Builder
    {
        public readonly List<Vector3> Vertices = new(), Normals = new();
        public readonly List<int> Indices = new();
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3? na = null, Vector3? nb = null, Vector3? nc = null)
        {
            int start = Vertices.Count;
            var face = (b - a).Cross(c - a).Normalized();
            Vertices.AddRange(new[] { a, b, c });
            Normals.AddRange(new[] { na ?? face, nb ?? face, nc ?? face });
            Indices.AddRange(new[] { start, start + 2, start + 1 });
        }
        public ArrayMesh Mesh()
        {
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = Vertices.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Normal] = Normals.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Index] = Indices.ToArray();
            var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays); return mesh;
        }
    }
    public static ArrayMesh Trunk()
    {
        var builder = new Builder(); Cylinder(builder, .013, .025, 1, 5, .5, false); return builder.Mesh();
    }
    public static ArrayMesh Crown(int species, bool distant)
    {
        var builder = new Builder();
        if (species == 1)
        {
            var parts = distant ? new[] { (1.0, .82, .59) } : new[] { (1.0, .57, .4), (.82, .52, .6), (.56, .43, .8) };
            foreach (var (r, h, y) in parts) Cylinder(builder, 0, r, h, distant ? 5 : 7, y, true);
        }
        else
        {
            var parts = distant ? new[] { (0.0, .69, 0.0, 1.0, .32, .9) } : new[] {
                (0.0,.72,0.0,.85,.29,.8),(-.4,.57,.12,.64,.24,.67),
                (.42,.62,.17,.66,.27,.65),(.05,.59,-.4,.72,.26,.63) };
            foreach (var (x, y, z, sx, sy, sz) in parts) Icosahedron(builder, distant ? 0 : 1, new(x, y, z), new(sx, sy, sz));
        }
        return builder.Mesh();
    }
    private static void Cylinder(Builder builder, double top, double bottom, double height, int segments, double y, bool flat)
    {
        // Float32BufferAttribute rounds before translate() in Three.js.
        Vector3 Vertex(double radius, int i, double localY) => new((float)(radius * Math.Sin(i * Math.Tau / segments)),
            (float)((float)localY + y), (float)(radius * Math.Cos(i * Math.Tau / segments)));
        Vector3 Normal(int i) => new V(Math.Sin(i * Math.Tau / segments), (bottom - top) / height, Math.Cos(i * Math.Tau / segments)).Unit().Float();
        for (int i = 0; i < segments; i++)
        {
            var a = Vertex(top, i, height / 2); var b = Vertex(bottom, i, -height / 2);
            var c = Vertex(bottom, i + 1, -height / 2); var d = Vertex(top, i + 1, height / 2);
            if (top > 0) builder.Triangle(a, b, d, flat ? null : Normal(i), flat ? null : Normal(i), flat ? null : Normal(i + 1));
            if (bottom > 0) builder.Triangle(b, c, d, flat ? null : Normal(i), flat ? null : Normal(i + 1), flat ? null : Normal(i + 1));
        }
        foreach (bool upper in new[] { true, false })
        {
            double radius = upper ? top : bottom; if (radius == 0) continue;
            double localY = height / 2 * (upper ? 1 : -1); var center = Vertex(0, 0, localY);
            for (int i = 0; i < segments; i++)
            {
                var a = Vertex(radius, i, localY); var b = Vertex(radius, i + 1, localY);
                if (upper) builder.Triangle(a, b, center); else builder.Triangle(b, a, center);
            }
        }
    }
    private static void Icosahedron(Builder builder, int detail, V offset, V scale)
    {
        double t = (1 + Math.Sqrt(5)) / 2;
        V[] vertices = {new(-1,t,0),new(1,t,0),new(-1,-t,0),new(1,-t,0),new(0,-1,t),new(0,1,t),
            new(0,-1,-t),new(0,1,-t),new(t,0,-1),new(t,0,1),new(-t,0,-1),new(-t,0,1)};
        int[] indices = {0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
            3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        Vector3 Transform(V p)
        {
            var v = p.Unit().Float();
            return new((float)((float)(v.X * scale.X) + offset.X), (float)((float)(v.Y * scale.Y) + offset.Y), (float)((float)(v.Z * scale.Z) + offset.Z));
        }
        for (int face = 0; face < indices.Length; face += 3)
        {
            var a = vertices[indices[face]]; var b = vertices[indices[face + 1]]; var c = vertices[indices[face + 2]];
            int cols = detail + 1; var grid = new V[cols + 1][];
            for (int i = 0; i <= cols; i++)
            {
                var aj = a.Lerp(c, (double)i / cols); var bj = b.Lerp(c, (double)i / cols); int rows = cols - i;
                grid[i] = new V[rows + 1];
                for (int j = 0; j <= rows; j++) grid[i][j] = rows == 0 ? aj : aj.Lerp(bj, (double)j / rows);
            }
            for (int i = 0; i < cols; i++) for (int j = 0; j < 2 * (cols - i) - 1; j++)
            {
                int k = j / 2;
                if (j % 2 == 0) builder.Triangle(Transform(grid[i][k + 1]), Transform(grid[i + 1][k]), Transform(grid[i][k]));
                else builder.Triangle(Transform(grid[i][k + 1]), Transform(grid[i + 1][k + 1]), Transform(grid[i + 1][k]));
            }
        }
    }
}
