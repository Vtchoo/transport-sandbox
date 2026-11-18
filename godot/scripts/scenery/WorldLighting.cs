using Godot;

namespace TransportSandbox;

/// <summary>Shared native lighting for the game world and object-based menu scene.</summary>
public static class WorldLighting
{
    public static void AddTo(Node3D parent)
    {
        parent.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new(0.55f, 0.68f, 0.79f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new(0.75f, 0.82f, 0.94f),
                AmbientLightEnergy = 0.65f
            }
        });
        parent.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new(-42, -28, 0),
            LightEnergy = 1.8f,
            ShadowEnabled = true
        });
    }
}
