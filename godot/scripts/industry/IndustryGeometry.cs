using Godot;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Simple original industry scenery, shared by the world and isolated industry viewports.</summary>
public static class IndustryGeometry
{
    private static readonly Dictionary<string, StandardMaterial3D> Materials = new();
    private static Material Material(string color)
    {
        if (!Materials.TryGetValue(color, out var material)) Materials[color] = material = new() { AlbedoColor = new(color), Roughness = .9f };
        return material;
    }
    public static Node3D Create(string type)
    {
        var root = new Node3D();
        void Box(string name, Vector3 size, Vector3 position, string color)
            => root.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(color), Position = position });
        void Stack(float x, float z, float height)
            => root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.8f, BottomRadius = 2.5f, Height = height, RadialSegments = 12 }, MaterialOverride = Material("725f4c"), Position = new(x, height / 2, z) });
        Box("foundation", new(70, 2, 65), new(0, -1, 0), "68675a");
        if (type == "steel-mill")
        {
            Box("mill", new(38, 18, 28), new(-7, 9, 0), "78634d");
            Box("roof", new(40, 1, 30), new(-7, 18.5f, 0), "535d59");
            Box("office", new(12, 8, 15), new(20, 4, 12), "b7ad8d");
            Stack(18, -17, 35); Stack(26, -17, 30);
            for (int i = 0; i < 6; i++) Box("steel_" + i, new(20, .8f, 1), new(6, 1 + i * .9f, 25), "707c83");
        }
        else
        {
            bool coal = type == "coal-mine";
            Box("processing_house", new(20, 15, 24), new(-15, 7.5f, -10), coal ? "756951" : "956445");
            Box("headframe_left", new(1, 25, 1), new(-23, 12.5f, -10), "485651");
            Box("headframe_right", new(1, 25, 1), new(-7, 12.5f, -10), "485651");
            Box("headframe_top", new(18, 2, 3), new(-15, 24, -10), "485651");
            Box("conveyor", new(32, 2, 4), new(1, 10, -9), "484d48");
            for (int i = 0; i < 3; i++)
                root.AddChild(new MeshInstance3D { Name = "stockpile_" + i, Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 10, Height = 7, RadialSegments = 10 },
                    MaterialOverride = Material(coal ? "272e2d" : "a36e49"), Position = new(13, 3.5f, -15 + i * 16) });
        }
        return root;
    }
}
