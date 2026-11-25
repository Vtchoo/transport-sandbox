using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>PRR K4s Pacific and 110P75 tender, dimensioned from railroad and period engineering drawings.</summary>
public static class PacificModels
{
    public const string Engine = "na-pacific-462", Tender = "na-pacific-tender";
    public static readonly string[] Ids = { Engine, Tender };
    // Original imperial dimensions converted exactly to metres. See docs/models/pacific-reference.md.
    public const float DriverRadius = 80 * .0254f / 2, DriverSpacing = 83 * .0254f;
    public const float EngineLength = 578.625f * .0254f, TenderLength = 403.5f * .0254f;
    public const float TrailingAxle = -EngineLength / 2 + 103.5f * .0254f;
    public const float RearDriver = TrailingAxle + 126 * .0254f;
    public const float MiddleDriver = RearDriver + DriverSpacing;
    public const float FrontDriver = RearDriver + 2 * DriverSpacing;
    public const float FrontPilot = TrailingAxle + 434 * .0254f;
    public const float PilotSpacing = 82 * .0254f;
    public const float FrontPivot = FrontPilot - PilotSpacing / 2;
    public const float BogieCenters = FrontPivot - TrailingAxle;
    public const float TruckOffset = (FrontPivot + TrailingAxle) / 2;
    public const float BoilerCenter = 121 * .0254f, StackZ = FrontPilot - 41 * .0254f;
    private static StandardMaterial3D Paint(string name, string color) => new() { ResourceName = name, AlbedoColor = new(color), Roughness = .6f };
    private static Node3D Group(Node parent, string name, Vector3 position = default)
    {
        var node = new Node3D { Name = name, Position = position }; parent.AddChild(node); return node;
    }
    private static MeshInstance3D Mesh(Node parent, string name, Godot.Mesh geometry, Vector3 position, Material material)
    {
        var node = new MeshInstance3D { Name = name, Mesh = geometry, Position = position, MaterialOverride = material }; parent.AddChild(node, true); return node;
    }
    private static void Box(Node parent, string name, Vector3 size, Vector3 position, Material material)
        => Mesh(parent, name, new BoxMesh { Size = size }, position, material);
    private static MeshInstance3D Cylinder(Node parent, string name, float radius, float length, Vector3 position, Material material, Vector3 rotation = default)
    {
        var node = Mesh(parent, name, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = length, RadialSegments = 24 }, position, material);
        node.Rotation = rotation; return node;
    }
    private static void Bar(Node parent, string name, Vector3 a, Vector3 b, float radius, Material material)
    {
        var bar = Cylinder(parent, name, radius, a.DistanceTo(b), (a + b) / 2, material);
        bar.Quaternion = new Quaternion(Vector3.Up, (b - a).Normalized());
    }
    private static void Text(Node parent, string text, Vector3 position, float width, float height, float yaw, Material material)
    {
        var source = new TextMesh { Text = text, Font = ThemeDB.FallbackFont, FontSize = 48, PixelSize = .01f, Depth = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var baked = new ArrayMesh(); baked.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, source.SurfaceGetArrays(0));
        var size = baked.GetAabb().Size; var node = Mesh(parent, "lettering", baked, position, material);
        node.Scale = new(width / size.X, height / size.Y, 1); node.Rotation = new(0, yaw, 0);
    }
    private static void Wheel(Node3D parent, string name, int side, float z, float radius, Material paint, Material steel, bool spoked)
    {
        var wheel = Group(parent, name, new(side * .79f, radius, z));
        var rotation = new Vector3(0, 0, Mathf.Pi / 2);
        if (spoked)
        {
            var tire = Mesh(wheel, "tire", new TorusMesh { InnerRadius = radius - .11f, OuterRadius = radius, Rings = 24, RingSegments = 8 }, Vector3.Zero, steel);
            tire.Rotation = rotation;
            Cylinder(wheel, "hub", radius * .19f, .19f, Vector3.Zero, paint, rotation);
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.Tau / 16;
                var spoke = Mesh(wheel, "spoke", new BoxMesh { Size = new(.09f, radius * .77f, .055f) },
                    new(0, Mathf.Cos(angle) * radius * .55f, Mathf.Sin(angle) * radius * .55f), paint);
                spoke.Rotation = new(angle, 0, 0);
            }
            // Counterweight occupies the opposite quadrant from the crank pin.
            Box(wheel, "counterweight", new(.14f, radius * .39f, radius * .68f), new(0, -radius * .57f, 0), paint);
            Cylinder(wheel, "crank-pin", .09f, .19f, new(side * .17f, .3556f, 0), steel, rotation);
        }
        else
        {
            Cylinder(wheel, "wheel-disc", radius, .13f, Vector3.Zero, paint, rotation);
            var tire = Mesh(wheel, "tire", new TorusMesh { InnerRadius = radius - .045f, OuterRadius = radius, Rings = 20, RingSegments = 6 }, new(side * .07f, 0, 0), steel);
            tire.Rotation = rotation;
            Cylinder(wheel, "hub", .13f, .17f, Vector3.Zero, paint, rotation);
        }
    }
    public static Node3D Build(string id)
    {
        bool engine = id == Engine; float length = engine ? EngineLength : TenderLength;
        var root = new Node3D { Name = id }; var body = Group(root, "body");
        var black = Paint("paint-pacific-black", "202526"); var graphite = Paint("paint-graphite", "4c5152");
        var steel = Paint("pacific-steel", "a4adb0"); var brass = Paint("pacific-brass", "c8a45d");
        var glass = Paint("glass-tinted", "263e49"); var cream = Paint("paint-lettering", "e5d9ac");
        var coal = Paint("coal", "121719"); var lamp = Paint("pacific-lamp", "fff1ce");
        lamp.EmissionEnabled = true; lamp.Emission = new("ffe1a0");
        Box(body, "frame", new(1.5f, .28f, length - .6f), new(0, .9f, 0), black);
        foreach (int sign in new[] { -1, 1 })
        {
            Box(body, "coupler-shank", new(.2f, .18f, .55f), new(0, .87f, sign * (length / 2 - .275f)), steel);
            Box(body, "coupler", new(.35f, .26f, .18f), new(0, .87f, sign * (length / 2 - .09f)), black);
        }
        if (engine)
        {
            // The boiler barrel tapers from the Belpaire firebox to the 82-inch smokebox.
            Barrel(body, black);
            Cylinder(body, "smokebox", 41 * .0254f, 1.90f, new(0, BoilerCenter, 5.28f), graphite, new(Mathf.Pi / 2, 0, 0));
            Cylinder(body, "smokebox-door", .87f, .10f, new(0, BoilerCenter, 6.265f), black, new(Mathf.Pi / 2, 0, 0));
            Cylinder(body, "number-plate", .19f, .025f, new(0, BoilerCenter, 6.33f), brass, new(Mathf.Pi / 2, 0, 0));
            Text(body, "1737", new(0, BoilerCenter, 6.35f), .28f, .12f, 0, black);
            foreach (float z in new[] { -1.9f, -.3f, 1.35f, 3.15f })
            {
                float radius = BarrelRadius(z);
                var band = Mesh(body, "boiler-band", new TorusMesh { InnerRadius = radius - .005f, OuterRadius = radius + .018f, Rings = 32, RingSegments = 6 }, new(0, BoilerCenter, z), graphite);
                band.Rotation = new(Mathf.Pi / 2, 0, 0);
            }
            Cylinder(body, "stack-base", .32f, .12f, new(0, 4.11f, StackZ), black);
            Mesh(body, "stack", new CylinderMesh { TopRadius = .29f, BottomRadius = .25f, Height = .392f, RadialSegments = 24 }, new(0, 4.326f, StackZ), graphite);
            Cylinder(body, "stack-lip", .32f, .10f, new(0, 4.522f, StackZ), black);
            foreach (float z in new[] { -1.30f, .81f })
            {
                float baseY = BoilerCenter + BarrelRadius(z);
                Cylinder(body, "dome-base", .48f, .10f, new(0, baseY, z), black);
                Cylinder(body, "dome", .39f, .28f, new(0, baseY + .14f, z), black);
                Mesh(body, "dome-cap", new SphereMesh { Radius = .39f, Height = .22f, RadialSegments = 20, Rings = 8 }, new(0, baseY + .24f, z), black);
            }
            Cylinder(body, "bell-yoke", .035f, .36f, new(0, 4.28f, 3.1f), brass);
            Mesh(body, "bell", new CylinderMesh { TopRadius = .10f, BottomRadius = .23f, Height = .26f, RadialSegments = 16 }, new(0, 4.31f, 3.1f), brass);
            Cylinder(body, "whistle", .055f, .25f, new(.35f, 4.40f, -1.5f), brass);
            Box(body, "lower-firebox", new(2.12f, 1.7f, 3.20f), new(0, 2.62f, -3.54f), black);
            Box(body, "belpaire-shoulders", new(2.26f, .91f, 3.20f), new(0, 3.85f, -3.54f), black);
            Box(body, "cab-floor", new(3.048f, .14f, 2.78f), new(0, 1.9812f, -5.55f), black);
            Box(body, "cab-front", new(3.048f, 1.74f, .12f), new(0, 2.87f, -4.33f), black);
            CabRoof(body, black);
            Box(body, "roof-vent", new(1.0f, .07f, .9f), new(0, 4.4546f, -5.6f), graphite);
            foreach (int side in new[] { -1, 1 })
            {
                Box(body, "cab-lower-wall", new(.10f, 1.12f, 2.32f), new(side * 1.474f, 2.55f, -5.49f), black);
                Box(body, "cab-window", new(.065f, .72f, 1.10f), new(side * 1.485f, 3.48f, -5.15f), glass);
                foreach (float z in new[] { -4.48f, -5.13f, -5.75f, -6.6f }) Box(body, "cab-pillar", new(.14f, .93f, .085f), new(side * 1.48f, 3.55f, z), black);
                Text(body, "1737", new(side * 1.53f, 2.65f, -5.2f), .88f, .31f, side * Mathf.Pi / 2, cream);
                Box(body, "running-board", new(.43f, .09f, 7.85f), new(side * 1.17f, 2.23f, -.25f), black);
                Box(body, "front-running-board", new(.43f, .09f, 1.7f), new(side * 1.17f, 2.03f, 4.53f), black);
                Bar(body, "boiler-handrail", new(side * 1.05f, 3.58f, -4.1f), new(side * 1.05f, 3.58f, 6.10f), .025f, steel);
                for (int i = 0; i < 6; i++) Bar(body, "handrail-stanchion", new(side * .91f, 3.58f, -3.8f + i * 1.85f), new(side * 1.05f, 3.58f, -3.8f + i * 1.85f), .022f, steel);
                // 89-inch cylinder centre spread; 27-inch bore, 28-inch stroke.
                Cylinder(body, "steam-cylinder", .435f, 1.28f, new(side * 1.1303f, 1.016f, StackZ), graphite, new(Mathf.Pi / 2, 0, 0));
                Cylinder(body, "valve-chest", .21f, 1.13f, new(side * 1.1303f, 1.73f, StackZ), black, new(Mathf.Pi / 2, 0, 0));
                for (int i = 0; i < 4; i++) Box(body, "cab-step", new(.30f, .07f, .65f), new(side * 1.39f, .46f + i * .36f, -6.75f), graphite);
                Bar(body, "injector-pipe", new(side * 1.13f, 2.46f, -4.1f), new(side * 1.13f, 2.46f, 1.0f), .045f, graphite);
            }
            Cylinder(body, "headlamp-housing", .23f, .30f, new(0, 4.01f, 6.35f), black, new(Mathf.Pi / 2, 0, 0));
            Cylinder(body, "headlamp", .17f, .025f, new(0, 4.01f, 6.515f), lamp, new(Mathf.Pi / 2, 0, 0));
            Box(body, "pilot-beam", new(2.96f, .25f, .3f), new(0, .98f, 6.70f), graphite);
            for (int i = -7; i <= 7; i++) Bar(body, "cowcatcher-slat", new(i * .18f, .94f, 6.75f), new(i * .20f, .25f, 7.18f - Mathf.Abs(i) * .025f), .035f, black);
            Bar(body, "pilot-bottom", new(-1.45f, .25f, 7.01f), new(1.45f, .25f, 7.01f), .06f, graphite);
            // Production placement uses symmetric bogie pivots. Local offsets retain the drawing's axle stations.
            var leading = Group(root, "bogie_0", new(0, 0, BogieCenters / 2));
            var trailing = Group(root, "bogie_1", new(0, 0, -BogieCenters / 2));
            Box(leading, "pilot-truck-frame", new(1.6f, .18f, 2.5f), new(0, .6f, TruckOffset), black);
            Box(trailing, "trailing-truck-frame", new(1.65f, .22f, 1.5f), new(0, .7f, TruckOffset), black);
            foreach (int side in new[] { -1, 1 })
            {
                Wheel(leading, "pilot-wheel-front-" + side, side, TruckOffset + PilotSpacing / 2, 18 * .0254f, black, steel, false);
                Wheel(leading, "pilot-wheel-rear-" + side, side, TruckOffset - PilotSpacing / 2, 18 * .0254f, black, steel, false);
                Wheel(trailing, "trailing-wheel-" + side, side, TruckOffset, 25 * .0254f, black, steel, false);
            }
            RunningGear(root, body, black, steel);
        }
        else
        {
            Box(body, "tender-deck", new(3.1242f, .14f, 9.7917f), new(0, 1.09f, 0), black);
            Box(body, "water-tank", new(3.048f, 2.413f, 8.9662f), new(0, 2.2987f, -.10f), black);
            Box(body, "front-deck-plate", new(3.048f, .12f, .72f), new(0, 1.85135f, 4.68f), black);
            foreach (int side in new[] { -1, 1 })
                Box(body, "bunker-side", new(.10f, .4572f, 4.6f), new(side * 1.474f, 3.7338f, 1.8f), black);
            foreach (float z in new[] { -.5f, 4.1f })
                Box(body, "bunker-end", new(3.048f, .4572f, .10f), new(0, 3.7338f, z), black);
            // Individual low-poly lumps give the open bunker a visible coal load.
            var random = new Random(462);
            for (int i = 0; i < 85; i++)
                Mesh(body, "coal-lump", new SphereMesh { Radius = .18f + (float)random.NextDouble() * .12f, Height = .30f, RadialSegments = 6, Rings = 3 },
                    new((float)(random.NextDouble() - .5) * 2.45f, 3.71f + (float)random.NextDouble() * .06f, (float)(random.NextDouble() - .5) * 3.95f + 1.8f), coal);
            Cylinder(body, "water-hatch", .35f, .13f, new(0, 3.57f, -3.4f), graphite);
            foreach (int side in new[] { -1, 1 })
            {
                Text(body, "PENNSYLVANIA", new(side * 1.53f, 2.65f, -.7f), 5.7f, .43f, side * Mathf.Pi / 2, cream);
                for (int i = 0; i < 4; i++) Box(body, "tender-step", new(.35f, .07f, .65f), new(side * 1.37f, .4f + i * .27f, 4.65f), graphite);

            }
            foreach (float x in new[] { -1.15f, -.7f }) Bar(body, "rear-ladder", new(x, 1.25f, -4.65f), new(x, 3.35f, -4.65f), .035f, steel);
            for (int i = 0; i < 7; i++) Bar(body, "ladder-rung", new(-1.15f, 1.35f + i * .29f, -4.65f), new(-.7f, 1.35f + i * .29f, -4.65f), .025f, steel);
            foreach (int sign in new[] { -1, 1 })
            {
                var truck = Group(root, sign == 1 ? "bogie_0" : "bogie_1", new(0, 0, sign * 3.048f));
                Box(truck, "truck-frame", new(2.0f, .25f, 2.35f), new(0, .68f, 0), graphite);
                foreach (int side in new[] { -1, 1 }) foreach (float z in new[] { -.9779f, .9779f })
                {
                    Wheel(truck, "tender-wheel-" + side + "-" + z, side, z, .4572f, black, steel, false);
                    Box(truck, "axle-box", new(.22f, .24f, .28f), new(side * 1.02f, .46f, z), graphite);
                }
            }
        }
        return root;
    }
    private static float BarrelRadius(float z) => Mathf.Lerp(1.13f, 1.0414f, Mathf.Clamp((z + 2.3f) / 6.63f, 0, 1));
    private static void Barrel(Node3D body, Material paint)
    {
        var vertices = new List<Vector3>();
        const int count = 32;
        Vector3 Point(float z, int i) { float a = i * Mathf.Tau / count; return new(Mathf.Sin(a) * BarrelRadius(z), BoilerCenter + Mathf.Cos(a) * BarrelRadius(z), z); }
        for (int i = 0; i < count; i++)
        {
            var a = Point(-2.3f, i); var b = Point(-2.3f, i + 1); var c = Point(4.33f, i); var d = Point(4.33f, i + 1);
            vertices.AddRange(new[] { a, b, c, b, d, c });
        }
        Surface(body, "tapered-boiler", vertices, paint);
    }
    private static void Surface(Node3D body, string name, List<Vector3> vertices, Material paint)
    {
        var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        foreach (var vertex in vertices) surface.AddVertex(vertex);
        surface.GenerateNormals(); Mesh(body, name, surface.Commit(), Vector3.Zero, paint);
    }
    private static void CabRoof(Node3D body, Material paint)
    {
        var vertices = new List<Vector3>();
        Vector3 Point(float x, float z) => new(x, 3.78f + .6396f * Mathf.Sqrt(Mathf.Max(0, 1 - x * x / (1.5621f * 1.5621f))), z);
        for (int i = 0; i < 24; i++)
        {
            float x = -1.5621f + i * 3.1242f / 24, next = -1.5621f + (i + 1) * 3.1242f / 24;
            vertices.AddRange(new[] { Point(x, -6.93f), Point(next, -6.93f), Point(x, -4.05f), Point(next, -6.93f), Point(next, -4.05f), Point(x, -4.05f) });
        }
        var material = (StandardMaterial3D)paint.Duplicate(); material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        Surface(body, "arched-cab-roof", vertices, material);
    }
    private static void RunningGear(Node3D root, Node3D body, Material black, Material steel)
    {
        var animation = new Animation { Length = 1, LoopMode = Animation.LoopModeEnum.Linear };
        void Track(Node3D node, string property, Func<float, Variant> sample)
        {
            int track = animation.AddTrack(Animation.TrackType.Value);
            animation.TrackSetPath(track, new NodePath(root.GetPathTo(node) + ":" + property));
            for (int i = 0; i <= 64; i++) animation.TrackInsertKey(track, i / 64.0, sample(i / 64f));
        }
        foreach (int side in new[] { -1, 1 })
        {
            float phase = side == 1 ? 0 : Mathf.Pi / 2;
            for (int i = 0; i < 3; i++)
            {
                string name = "motion_driver_" + side + "_" + i;
                Wheel(body, name, side, RearDriver + i * DriverSpacing, DriverRadius, black, steel, true);
                var wheel = body.GetNode<Node3D>(name);
                Track(wheel, "rotation:x", t => phase + t * Mathf.Tau);
            }
            var rod = Group(body, "motion_coupling_rod_" + side);
            Box(rod, "coupling-rod", new(.10f, .13f, 2 * DriverSpacing + .20f), Vector3.Zero, steel);
            Track(rod, "position", t => new Vector3(side * 1.04f, DriverRadius + .3556f * Mathf.Cos(phase + t * Mathf.Tau), MiddleDriver + .3556f * Mathf.Sin(phase + t * Mathf.Tau)));
            // Slider-crank geometry: piston remains on its guide while the main rod follows the centre crank.
            const float rodLength = 3.18f;
            Vector3 Crank(float t) => new(side * 1.16f, DriverRadius + .3556f * Mathf.Cos(phase + t * Mathf.Tau), MiddleDriver + .3556f * Mathf.Sin(phase + t * Mathf.Tau));
            Vector3 Slider(float t) { var c = Crank(t); float dy = c.Y - DriverRadius; return new(c.X, DriverRadius, c.Z + Mathf.Sqrt(rodLength * rodLength - dy * dy)); }
            var main = Group(body, "motion_main_rod_" + side);
            Box(main, "main-rod", new(.10f, .14f, rodLength), Vector3.Zero, steel);
            Track(main, "position", t => (Crank(t) + Slider(t)) / 2);
            Track(main, "rotation:x", t => -Mathf.Atan2((Slider(t) - Crank(t)).Y, (Slider(t) - Crank(t)).Z));
            var piston = Group(body, "motion_crosshead_" + side);
            Box(piston, "crosshead", new(.20f, .23f, .35f), Vector3.Zero, steel);
            Cylinder(piston, "piston-rod", .045f, 1.25f, new(0, 0, .60f), steel, new(Mathf.Pi / 2, 0, 0));
            Track(piston, "position", t => Slider(t));
            Bar(body, "crosshead-guide", new(side * 1.16f, 1.23f, 3.4f), new(side * 1.16f, 1.23f, 4.5f), .04f, steel);
        }
        var library = new AnimationLibrary(); library.AddAnimation("running_gear", animation);
        var player = new AnimationPlayer { Name = "RunningGear" }; root.AddChild(player); player.AddAnimationLibrary("", library);
        player.Play("running_gear"); player.Seek(0, true); player.Pause();
    }
    public static Node3D BuildComposition()
    {
        var root = new Node3D { Name = "pacific-composition" };
        // The parent has placement anchors, while each rigid section follows its own track pose.
        Group(root, "body"); Group(root, "bogie_0"); Group(root, "bogie_1");
        for (int i = 0; i < Ids.Length; i++)
        {
            var section = Build(Ids[i]); section.Name = "section_" + i;
            section.Position = new(0, 0, i == 0 ? (TenderLength + .12f) / 2 : -(EngineLength + .12f) / 2);
            string prefix = $"section_{i}_";
            foreach (string name in new[] { "body", "bogie_0", "bogie_1" }) section.GetNode<Node3D>(name).Name = prefix + name;
            if (section.GetNodeOrNull<AnimationPlayer>("RunningGear") is { } player)
            {
                var animation = player.GetAnimation("running_gear");
                for (int track = 0; track < animation.GetTrackCount(); track++)
                {
                    string path = animation.TrackGetPath(track).ToString();
                    if (path.StartsWith("body/", StringComparison.Ordinal)) animation.TrackSetPath(track, new NodePath(prefix + path));
                }
                player.Play("running_gear"); player.Seek(0, true); player.Pause();
            }
            root.AddChild(section);
        }
        return root;
    }
    public static void Export()
    {
        foreach (string id in new[] { Engine, Tender, "na-pacific-462-composition" })
        {
            var root = id.EndsWith("composition") ? BuildComposition() : Build(id);
            void Own(Node node) { foreach (var child in node.GetChildren()) { child.Owner = root; Own(child); } }
            Own(root); var packed = new PackedScene(); var result = packed.Pack(root);
            if (result == Error.Ok) result = ResourceSaver.Save(packed, $"res://assets/vehicles/{id}-1435.tscn");
            root.Free(); if (result != Error.Ok) throw new InvalidOperationException(result.ToString());
            GD.Print("Authored " + id);
        }
    }
}
