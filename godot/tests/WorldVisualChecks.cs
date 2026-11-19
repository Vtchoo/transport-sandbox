using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

/// <summary>GPU output against pixels recorded from the original browser materials.</summary>
public static class WorldVisualChecks
{
    public static async Task Run(Action<bool, string> check, Func<Task> frame, Node parent)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("World visual pixel checks require a graphical display server.");
            return;
        }
        var viewport = new SubViewport
        {
            Size = new(3, 3),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        parent.AddChild(viewport);
        var root = new Node3D();
        viewport.AddChild(root);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new("b5cede"),
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            Sky = new Sky { SkyMaterial = WorldLighting.GameplaySkyMaterial() }
        };
        root.AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D
        {
            Basis = Basis.LookingAt(-new Vector3(-.7f, .8f, -.4f).Normalized(), Vector3.Up),
            ShadowEnabled = false
        };
        root.AddChild(sun);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = .02f, Near = .01f, Far = 14000 };
        root.AddChild(camera);
        camera.Current = true;
        var mesh = new ArrayMesh();
        void SetColor(Color linear)
        {
            mesh.ClearSurfaces();
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-10000, 0, -10000), new Vector3(10000, 0, -10000), new Vector3(-10000, 0, 10000), new Vector3(10000, 0, 10000) };
            arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, 4).ToArray();
            // Production chunk workers store sRGB colors in Godot's packed mesh array.
            arrays[(int)Mesh.ArrayType.Color] = Enumerable.Repeat(linear.LinearToSrgb(), 4).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        var plane = new MeshInstance3D { Mesh = mesh };
        root.AddChild(plane);
        using var terrain = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
        using var water = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        try
        {
            foreach (var sample in JsonData.Read("res://tests/world-visual-fixtures.json").EnumerateArray())
            {
                string material = sample.GetProperty("material").GetString()!;
                string description = material;
                if (material == "sky")
                {
                    plane.Hide();
                    environment.BackgroundMode = Godot.Environment.BGMode.Sky;
                    camera.Projection = Camera3D.ProjectionType.Perspective;
                    camera.Fov = 65;
                    camera.Position = Vector3.Zero;
                    var direction = JsonData.Vector(sample.GetProperty("direction"));
                    camera.LookAt(direction, Math.Abs(direction.Y) == 1 ? Vector3.Forward : Vector3.Up);
                    description += $" direction {direction}";
                }
                else
                {
                    var rgb = sample.GetProperty("color").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                    SetColor(new(rgb[0], rgb[1], rgb[2]));
                    float x = sample.GetProperty("x").GetSingle(), z = sample.GetProperty("z").GetSingle(), depth = sample.GetProperty("depth").GetSingle();
                    plane.MaterialOverride = material == "water" ? water : terrain;
                    camera.Position = new(x, depth, z);
                    camera.LookAt(new(x, 0, z), Vector3.Forward);
                    description += $" at ({x},{z}), depth {depth}";
                }
                await frame();
                await frame();
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                var actual = image.GetPixel(1, 1);
                var expected = sample.GetProperty("pixel").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var channels = new[] { actual.R8, actual.G8, actual.B8 };
                check(channels.Select((value, i) => Math.Abs(value - expected[i])).Max() <= 3,
                    $"{description}: browser pixel [{string.Join(',', expected.Take(3))}], Godot [{string.Join(',', channels)}]");
                if (material == "terrain")
                {
                    var shift = new Vector3(512, 0, -1024);
                    plane.Position -= shift;
                    camera.Position -= shift;
                    terrain.SetShaderParameter("terrain_origin", new Vector2(shift.X, shift.Z));
                    await frame();
                    await frame();
                    await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var shiftedImage = viewport.GetTexture().GetImage();
                    var shifted = shiftedImage.GetPixel(1, 1);
                    check(Math.Abs(actual.R8 - shifted.R8) <= 1 && Math.Abs(actual.G8 - shifted.G8) <= 1 && Math.Abs(actual.B8 - shifted.B8) <= 1,
                        description + ": floating origin preserves the ground texture");
                    plane.Position = Vector3.Zero;
                    terrain.SetShaderParameter("terrain_origin", Vector2.Zero);
                }
            }
            plane.Show();
            environment.BackgroundMode = Godot.Environment.BGMode.Color;
            camera.Projection = Camera3D.ProjectionType.Orthogonal;
            camera.Size = .02f;
            camera.Position = new(0, 100, 0);
            camera.LookAt(Vector3.Zero, Vector3.Forward);
            SetColor(new(.2f, .4f, .1f));
            sun.DirectionalShadowMaxDistance = 250;
            var caster = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One * 4 },
                Position = new(-7, 8, -4),
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
            };
            root.AddChild(caster);
            async Task<Color> ReadPixel()
            {
                await frame();
                await frame();
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                return image.GetPixel(1, 1);
            }
            foreach (var material in new[] { terrain, water })
            {
                plane.MaterialOverride = material;
                caster.Hide();
                sun.ShadowEnabled = false;
                var withoutShadows = await ReadPixel();
                sun.ShadowEnabled = true;
                var clear = await ReadPixel();
                check(Math.Abs(withoutShadows.R8 - clear.R8) <= 2 && Math.Abs(withoutShadows.G8 - clear.G8) <= 2 && Math.Abs(withoutShadows.B8 - clear.B8) <= 2,
                    "Enabling native shadow maps preserves unoccluded surface brightness");
                caster.Show();
                var shadow = await ReadPixel();
                float Luminance(Color c) => .2126f * c.R + .7152f * c.G + .0722f * c.B;
                check(Luminance(shadow) < Luminance(clear) - .04f,
                    material.Shader.ResourcePath + ": native shadow caster darkens the surface");
                check(Luminance(shadow) > .05f, "Shadowed surface retains hemisphere lighting and fog");
                caster.Hide();
                var restored = await ReadPixel();
                check(Math.Abs(restored.R8 - clear.R8) <= 2 && Math.Abs(restored.G8 - clear.G8) <= 2 && Math.Abs(restored.B8 - clear.B8) <= 2,
                    "Removing the shadow caster restores the original surface brightness");
            }
        }
        finally { viewport.QueueFree(); mesh.Dispose(); }
        await frame();
    }
}
