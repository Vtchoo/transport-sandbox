using Godot;

namespace TransportSandbox;

/// <summary>Station furniture assembled from native scene objects, separate from track geometry.</summary>
public partial class StationScenery : Node3D
{
    public StationScenery(float halfLength)
    {
        Name = "StationFurniture";
        Box(new(8, 0.08f, halfLength * 2 + 12), new(8.5f, -0.58f, 0), "9b9584");
        for (float z = -halfLength + 12; z < halfLength - 8; z += 17)
        {
            Box(new(0.12f, 3.5f, 0.12f), new(4, 2.2f, z), "3e514a");
            Box(new(3.6f, 0.18f, 14), new(3.7f, 4, z), "535f58");
            Box(new(0.65f, 0.14f, 2.8f), new(3.7f, 1.1f, z + 2), "7a5739");
            foreach (float dz in new[] { -0.95f, 0.95f })
                Box(new(0.5f, 0.55f, 0.1f), new(3.7f, 0.75f, z + 2 + dz), "3e514a");
            Box(new(0.12f, 4.5f, 0.12f), new(5.1f, 2.65f, z - 6), "3e514a");
            Box(new(0.5f, 0.18f, 0.8f), new(5.1f, 4.9f, z - 6), "e9dfb8");
        }
    }
    private void Box(Vector3 size, Vector3 position, string color)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = position,
            MaterialOverride = TrackRenderer.Material(new(color))
        });
    }
}
