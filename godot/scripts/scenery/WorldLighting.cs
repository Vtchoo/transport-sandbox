using Godot;

namespace TransportSandbox;

/// <summary>Shared native lighting for the game world and object-based menu scene.</summary>
public static class WorldLighting
{
    public static ShaderMaterial GameplaySkyMaterial()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/world_sky.gdshader") };
        material.SetShaderParameter("compatibility_output", RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility");
        return material;
    }
    public static void AddTo(Node3D parent, bool gameplay = false)
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new(0.55f, 0.68f, 0.79f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new(0.75f, 0.82f, 0.94f),
            AmbientLightEnergy = 0.65f
        };
        if (gameplay)
        {
            environment.BackgroundMode = Godot.Environment.BGMode.Sky;
            environment.Sky = new Sky
            {
                SkyMaterial = GameplaySkyMaterial()
            };
            // Original lighting has no environment map or screen-space reflections.
            environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        }
        parent.AddChild(new WorldEnvironment
        {
            Environment = environment
        });
        var sun = new DirectionalLight3D
        {
            RotationDegrees = new(-42, -28, 0),
            LightEnergy = 1.8f,
            ShadowEnabled = true
        };
        if (gameplay)
        {
            sun.Basis = Basis.LookingAt(-new Vector3(-.7f, .8f, -.4f).Normalized(), Vector3.Up);
            sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
            sun.DirectionalShadowMaxDistance = 1000;
        }
        parent.AddChild(sun);
    }
}
