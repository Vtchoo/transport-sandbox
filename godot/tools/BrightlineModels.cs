using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Native, metre-scale authoring source. Run the brightline-author diagnostic to rebuild the packed assets.</summary>
public static class BrightlineModels
{
    public const string Locomotive = "brightline-scb40", Smart = "brightline-venture-smart", Premium = "brightline-venture-premium";
    public static readonly string[] Ids = { Locomotive, Smart, Premium };
    public static Node3D Build(string id)
    {
        bool power = id == Locomotive, premium = id == Premium;
        float length = power ? 21.7932f : 25.908f, half = length / 2 - .22f;
        float w = power ? 1.524f : 1.6002f, top = 4.2672f;
        var root = new Node3D { Name = id };
        var body = new Node3D { Name = "body" }; root.AddChild(body);
        var yellow = Paint("paint-brightline-yellow", "f6d20a");
        var silver = Paint("metal-stainless", "c7cbd0");
        var white = Paint("paint-silver-white", "e5e7e8");
        var gray = Paint("paint-roof-gray", "49545f");
        var black = Paint("metal-frame", "252b31");
        var canopy = Paint("paint-cab-mask", "18212a");
        var glass = Paint("glass-tinted", "263e49");
        var accent = Paint(premium ? "paint-premium-yellow" : "paint-smart-magenta", premium ? "f6d20a" : "d53979");
        var lamp = Paint("lamp-ivory", "fff1cf");
        lamp.EmissionEnabled = true; lamp.Emission = new("ffe1aa"); lamp.EmissionEnergyMultiplier = .6f;
        Box(body, "underframe", new(w * 1.9f, .28f, half * 2 - .2f), new(0, 1.16f, 0), black);
        Box(body, "equipment", new(1.65f, .48f, power ? 6 : 8), new(0, .8f, -1), black);
        float end = power ? 6.3f : half;
        Shell(body, "body-shell", new[] { new Section(-half, w, 1.3f, 3.78f, top - .08f), new Section(end, w, 1.3f, 3.78f, top - .08f) }, white);
        Shell(body, "roof", new[] { new Section(-half, w * .93f, 3.82f, 3.94f, top), new Section(end, w * .93f, 3.82f, 3.94f, top) }, gray);
        foreach (int side in new[] { -1, 1 })
        {
            Box(body, "lower-skirt", new(.025f, .34f, half + end), new(side * (w + .006f), 1.45f, (end - half) / 2), gray);
            if (power)
            {
                // Photo-derived broad yellow field interrupted by silver diagonal ribbons.
                SidePanel(body, "yellow-field", side, w + .025f, new[] { new Vector2(-half, 1.65f), new(-half + 2, 3.83f), new(4.7f, 3.83f), new(2.4f, 1.65f) }, yellow);
                SidePanel(body, "silver-diagonal", side, w + .032f, new[] { new Vector2(-6.9f, 1.65f), new(-2.9f, 3.83f), new(-1.55f, 3.83f), new(-5.55f, 1.65f) }, white);
                SidePanel(body, "yellow-chevron", side, w + .038f, new[] { new Vector2(1, 2.1f), new(4.25f, 3.83f), new(5.05f, 3.83f), new(1.8f, 2.1f) }, yellow);
                Box(body, "waist-stripe", new(.028f, .045f, half + end), new(side * (w + .045f), 2.02f, (end - half) / 2), yellow);
                Letter(body, "brightline", 4.55f, .57f, new(side * (w + .052f), 2.8f, -.6f), side * Mathf.Pi / 2, canopy);
                Box(body, "cab-door-rim", new(.05f, 1.97f, .86f), new(side * (w + .017f), 2.44f, 5.4f), silver);
                Box(body, "cab-door", new(.052f, 1.82f, .72f), new(side * (w + .025f), 2.45f, 5.4f), white);
                Box(body, "cab-door-glass", new(.054f, .7f, .49f), new(side * (w + .03f), 2.93f, 5.4f), glass);
                Box(body, "door-handle", new(.07f, .19f, .035f), new(side * (w + .035f), 2.23f, 5.13f), black);
                for (int step = 0; step < 3; step++) Box(body, "cab-step", new(.28f, .06f, .7f), new(side * (w - .09f), .65f + step * .22f, 5.4f), silver);
                foreach (float z in new[] { -7.8f, -6.2f, 2.6f })
                {
                    Box(body, "vent-recess", new(.04f, 1.38f, 1.35f), new(side * (w + .044f), 2.96f, z), canopy);
                    for (int i = 0; i < 10; i++) Box(body, "vent-slat", new(.055f, 1.31f, .035f), new(side * (w + .052f), 2.96f, z - .58f + i * .13f), gray);
                }
                Letter(body, "101", .4f, .18f, new(side * (w + .05f), 1.75f, 5.4f), side * Mathf.Pi / 2, white);
            }
            else
            {
                Box(body, "window-band", new(.035f, .91f, half * 2 - 1), new(side * (w + .018f), 3.02f, 0), canopy);
                // Large single-level Venture windows, with solid mullions between panes.
                for (int i = 0; i < 14; i++) Box(body, "passenger-window", new(.043f, .68f, 1.13f), new(side * (w + .035f), 3.03f, -9.25f + i * 1.42f), glass);
                Box(body, "yellow-belt", new(.045f, .18f, half * 2 - .2f), new(side * (w + .032f), 2.34f, 0), yellow);
                foreach (int edge in new[] { -1, 1 })
                {
                    float z = edge * 11.15f;
                    Box(body, "vestibule-color", new(.045f, 2.23f, 1.75f), new(side * (w + .035f), 2.52f, z), accent);
                    Box(body, "plug-door-rim", new(.05f, 1.98f, .95f), new(side * (w + .04f), 2.45f, z), black);
                    Box(body, "plug-door", new(.055f, 1.86f, .83f), new(side * (w + .045f), 2.46f, z), white);
                    Box(body, "door-window", new(.045f, .64f, .52f), new(side * (w + .045f), 2.89f, z), glass);
                    Box(body, "threshold", new(.12f, .07f, 1.03f), new(side * w, 1.48f, z), silver);
                }
                foreach (float z in new[] { -6.8f, 6.8f })
                    SidePanel(body, "color-ribbon", side, w + .06f, new[] { new Vector2(z - 1.2f, 1.66f), new(z + .25f, 2.25f), new(z + 1.45f, 2.25f), new(z, 1.66f) }, accent);
                Letter(body, "brightline", 3.7f, .42f, new(side * (w + .068f), 1.93f, 0), side * Mathf.Pi / 2, canopy);
                Letter(body, premium ? "PREMIUM" : "SMART", 1.1f, .2f, new(side * (w + .068f), 3.64f, -8.75f), side * Mathf.Pi / 2, canopy);
                Letter(body, premium ? "201" : "401", .4f, .16f, new(side * (w + .068f), 1.91f, 9.7f), side * Mathf.Pi / 2, canopy);
            }
        }
        if (power)
        {
            Shell(body, "streamlined-nose", new[] { new Section(6.3f, w, 1.3f, 3.78f, 4.18f), new Section(7.4f, 1.46f, 1.24f, 3.48f, 3.8f), new Section(9.4f, 1.21f, 1.14f, 2.14f, 2.45f), new Section(half, .72f, 1.1f, 1.47f, 1.65f) }, gray);
            Shell(body, "black-cab-canopy", new[] { new Section(6.3f, w + .008f, 3.08f, 3.78f, 4.195f), new Section(7.4f, 1.472f, 2.92f, 3.49f, 3.818f), new Section(9.4f, 1.222f, 1.9f, 2.15f, 2.468f) }, canopy);
            foreach (int side in new[] { -1, 1 })
            {
                Quad(body, "windshield", new(side * .075f, 3.745f, 7.51f), new(side * 1.01f, 3.735f, 7.51f), new(side * .86f, 2.83f, 8.91f), new(side * .065f, 2.845f, 8.91f), glass);
                var wiper = Box(body, "wiper", new(.025f, .58f, .025f), new(side * .46f, 2.99f, 8.73f), black); wiper.Rotation = new(-.98f, 0, 0);
                Box(body, "side-cab-window", new(.035f, .55f, .64f), new(side * 1.53f, 3.36f, 6.93f), glass);
                var sweep = new[] { new Vector3(side * 1.54f, 3.06f, 6.3f), new(side * 1.49f, 2.9f, 7.4f), new(side * 1.24f, 1.88f, 9.4f) };
                for (int i = 0; i < sweep.Length - 1; i++)
                {
                    Vector3 delta = sweep[i + 1] - sweep[i];
                    var stripe = Mesh(body, "cab-yellow-sweep", new CylinderMesh { TopRadius = .018f, BottomRadius = .018f, Height = delta.Length(), RadialSegments = 6 }, (sweep[i] + sweep[i + 1]) / 2, yellow);
                    stripe.Quaternion = new Quaternion(Vector3.Up, delta.Normalized());
                }
                Box(body, "ditch-light-rim", new(.28f, .22f, .07f), new(side * .55f, 1.52f, half - .03f), black);
                Box(body, "ditch-light", new(.21f, .13f, .08f), new(side * .55f, 1.52f, half - .015f), lamp);
                Box(body, "headlight", new(.16f, .16f, .065f), new(side * .11f, 2.35f, 9.62f), lamp);
            }
            // Surface-following Brightline barcode rays, clear of the windshield.
            NoseGraphic(body, "nose-black-face", -.97f, .97f, -.94f, .94f, 9.02f, half - .10f, half, canopy, .012f);
            NoseGraphic(body, "nose-yellow-field", -.47f, .47f, -.31f, .31f, 9.28f, half - .18f, half, yellow, .022f);
            for (int i = 0; i < 23; i++)
            {
                float u = -.91f + i * .079f;
                float reach = .17f + ((i * 7) % 11) * .052f;
                NoseGraphic(body, "nose-yellow-ray", u, u + .034f, u - .12f, u - .105f,
                    9.23f, 9.23f + reach, half, yellow, .026f);
            }
            for (int i = 0; i < 9; i++)
            {
                float u = -.35f + i * .085f;
                NoseGraphic(body, "nose-barcode-cut", u, u + .025f, u - .16f, u - .145f,
                    9.72f + (i % 3) * .10f, half - .15f, half, canopy, .036f);
                NoseGraphic(body, "nose-yellow-tail", u - .16f, u - .13f, u - .21f, u - .205f,
                    half - .19f, half - .025f, half, yellow, .028f);
            }
            for (int i = 0; i < 25; i++)
            {
                float u = -.90f + i * .071f;
                NoseGraphic(body, "forehead-yellow-ray", u, u + .035f, u - .065f, u - .043f,
                    6.94f + (i % 3) * .035f, 7.32f, half, yellow, .024f);
            }
            Letter(body, "brightline", 1.08f, .18f, new(0, 1.66f, half + .008f), 0, white);
            Box(body, "rear-radiator-roof", new(2.18f, .045f, 3.6f), new(0, 4.21f, -7), black);
            for (int i = 0; i < 12; i++) Box(body, "roof-radiator-slat", new(2.1f, .035f, .045f), new(0, 4.242f, -8.58f + i * .28f), gray);
            Box(body, "exhaust", new(.43f, .045f, .7f), new(0, 4.24f, -2.8f), black);
            foreach (float x in new[] { -.24f, .24f }) Cylinder(body, "horn", .07f, .38f, new(x, 4.1f, 4.5f), gray, new(Mathf.Pi / 2, 0, 0));
        }
        else
        {
            foreach (int endSign in new[] { -1, 1 })
            {
                Box(body, "gangway", new(1.87f, 2.3f, .17f), new(0, 2.48f, endSign * (half + .065f)), black);
                Box(body, "gangway-door", new(.9f, 1.83f, .185f), new(0, 2.41f, endSign * (half + .065f)), silver);
                Box(body, "hvac", new(2.12f, .12f, 2.8f), new(0, 4.17f, endSign * 7.8f), gray);
            }
        }
        foreach (int sign in new[] { -1, 1 })
        {
            Box(body, "coupler-shank", new(.18f, .18f, .56f), new(0, .88f, sign * (length / 2 - .28f)), black);
            Box(body, "coupler", new(.32f, .25f, .2f), new(0, .88f, sign * (length / 2 - .1f)), black);
            var bogie = Bogie(power, black, silver); bogie.Name = sign == 1 ? "bogie_0" : "bogie_1";
            bogie.Position = new(0, 0, sign * (power ? 6.4f : 9.0678f)); root.AddChild(bogie);
        }
        return root;
    }
    private static StandardMaterial3D Paint(string name, string color) => new() { ResourceName = name, AlbedoColor = new(color), Roughness = .65f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
    private static MeshInstance3D Box(Node3D p, string name, Vector3 size, Vector3 at, Material mat) => Mesh(p, name, new BoxMesh { Size = size }, at, mat);
    private static MeshInstance3D Mesh(Node3D p, string name, Mesh mesh, Vector3 at, Material mat)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = at, MaterialOverride = mat }; p.AddChild(node, true); return node;
    }
    private static void Cylinder(Node3D p, string name, float radius, float height, Vector3 at, Material mat, Vector3 rotation = default)
        => Mesh(p, name, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 12 }, at, mat).Rotation = rotation;
    private static Node3D Bogie(bool power, Material frame, Material steel)
    {
        var root = new Node3D(); float radius = power ? .5588f : .4572f, axle = power ? 1.49352f : 1.25f;
        Box(root, "cross-member", new(2.22f, .22f, .4f), new(0, .85f, 0), frame);
        foreach (int end in new[] { -1, 1 })
        {
            Cylinder(root, "axle", .085f, 1.9f, new(0, radius, end * axle), frame, new(0, 0, Mathf.Pi / 2));
            foreach (int side in new[] { -1, 1 })
            {
                Cylinder(root, "wheel", radius, .12f, new(side * .7775f, radius, end * axle), frame, new(0, 0, Mathf.Pi / 2));
                Box(root, "axle-box", new(.24f, .22f, .28f), new(side * 1.05f, radius, end * axle), steel);
            }
        }
        foreach (int side in new[] { -1, 1 })
        {
            Box(root, "side-frame", new(.17f, .25f, axle * 2 + .45f), new(side * 1.05f, .72f, 0), frame);
            foreach (float z in new[] { -.4f, .4f }) Cylinder(root, "air-spring", .19f, .24f, new(side * .93f, 1, z), frame);
            if (power) Box(root, "traction-motor", new(.8f, .33f, .75f), new(side * .42f, .62f, 0), frame);
        }
        return root;
    }
    private readonly record struct Section(float Z, float W, float Bottom, float Shoulder, float Top);
    private static void Shell(Node3D p, string name, Section[] sections, Material mat)
    {
        var v = new List<Vector3>(); var index = new List<int>();
        foreach (var s in sections)
            foreach (var point in new[] { new Vector2(-s.W, s.Bottom), new(-s.W, s.Shoulder), new(-s.W * .87f, s.Top - .07f), new(0, s.Top), new(s.W * .87f, s.Top - .07f), new(s.W, s.Shoulder), new(s.W, s.Bottom) })
                v.Add(new(point.X, point.Y, s.Z));
        for (int i = 0; i < sections.Length - 1; i++) for (int j = 0; j < 7; j++)
        { int a = i * 7 + j, b = i * 7 + (j + 1) % 7, c = a + 7, d = b + 7; index.AddRange(new[] { a, b, c, b, d, c }); }
        for (int j = 1; j < 6; j++) { index.AddRange(new[] { 0, j + 1, j }); int n = v.Count - 7; index.AddRange(new[] { n, n + j, n + j + 1 }); }
        Triangles(p, name, v, index, mat);
    }
    // Coordinates are fractions of the local half-width and longitudinal metres.
    // Subdivide each ribbon so it hugs the faceted nose instead of floating across it.
    private static void NoseGraphic(Node3D body, string name, float left0, float right0,
        float left1, float right1, float start, float end, float tip, Material material, float offset)
    {
        var sections = new[] { new Section(6.3f, 1.532f, 0, 3.78f, 4.195f),
            new Section(7.4f, 1.472f, 0, 3.49f, 3.818f),
            new Section(9.4f, 1.222f, 0, 2.15f, 2.468f),
            new Section(tip, .72f, 0, 1.47f, 1.65f) };
        Vector3 Point(float u, float z)
        {
            int k = 0;
            while (k < sections.Length - 2 && z > sections[k + 1].Z) k++;
            var a = sections[k]; var b = sections[k + 1];
            float t = (z - a.Z) / (b.Z - a.Z);
            float width = Mathf.Lerp(a.W, b.W, t), top = Mathf.Lerp(a.Top, b.Top, t);
            float shoulder = Mathf.Lerp(a.Shoulder, b.Shoulder, t), q = Mathf.Abs(u);
            float y = q <= .87f ? top - .07f * q / .87f
                : Mathf.Lerp(top - .07f, shoulder, (q - .87f) / .13f);
            return new(u * width, y + offset, z);
        }
        var vertices = new List<Vector3>(); var indices = new List<int>();
        const int steps = 12;
        int across = name is "nose-black-face" or "nose-yellow-field" ? 12 : 1;
        for (int row = 0; row <= steps; row++)
        {
            float t = (float)row / steps;
            for (int col = 0; col <= across; col++)
                vertices.Add(Point(Mathf.Lerp(Mathf.Lerp(left0, left1, t), Mathf.Lerp(right0, right1, t), (float)col / across), Mathf.Lerp(start, end, t)));
        }
        for (int row = 0; row < steps; row++) for (int col = 0; col < across; col++)
        {
            int a = row * (across + 1) + col, b = a + across + 1;
            indices.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
        }
        Triangles(body, name, vertices, indices, material);
    }
    private static void SidePanel(Node3D p, string name, int side, float x, Vector2[] points, Material mat)
        => Quad(p, name, new(side * x, points[0].Y, points[0].X), new(side * x, points[1].Y, points[1].X), new(side * x, points[2].Y, points[2].X), new(side * x, points[3].Y, points[3].X), mat);
    private static void Quad(Node3D p, string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Material mat)
        => Triangles(p, name, new[] { a, b, c, d }, new[] { 0, 1, 2, 0, 2, 3 }, mat);
    private static void Triangles(Node3D p, string name, IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, Material mat)
    {
        var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        foreach (int i in indices) surface.AddVertex(vertices[i]);
        surface.GenerateNormals(); Mesh(p, name, surface.Commit(), Vector3.Zero, mat);
    }
    private static void Letter(Node3D p, string text, float width, float height, Vector3 at, float yaw, Material mat)
    {
        var source = new TextMesh { Text = text, Font = ThemeDB.FallbackFont, FontSize = 48, PixelSize = .01f, Depth = 0, CurveStep = .5f,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // Bake font triangles into the asset: no dynamic text geometry or system fonts at runtime.
        var arrays = source.SurfaceGetArrays(0); var baked = new ArrayMesh(); baked.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        var size = baked.GetAabb().Size; var mesh = Mesh(p, "wordmark", baked, at, mat);
        mesh.Scale = new(width / Math.Max(.001f, size.X), height / Math.Max(.001f, size.Y), 1); mesh.Rotation = new(0, yaw, 0);
    }
    public static void Export()
    {
        foreach (string id in Ids)
        {
            var root = Build(id);
            void Own(Node node) { foreach (var child in node.GetChildren()) { child.Owner = root; Own(child); } }
            Own(root); var packed = new PackedScene(); var result = packed.Pack(root);
            if (result != Error.Ok) throw new InvalidOperationException(result.ToString());
            result = ResourceSaver.Save(packed, $"res://assets/vehicles/{id}-1435.tscn"); root.Free();
            if (result != Error.Ok) throw new InvalidOperationException(result.ToString());
            GD.Print("Authored " + id);
        }
    }
}
