using Godot;
using System;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

/// <summary>GPU smoke scene: empty, half coal, full coal and full ore, sharing the normal train renderer.</summary>
public static class CargoVisualChecks
{
    public static async Task Capture(Node parent, Func<Task> frame)
    {
        var root = new Node3D(); parent.AddChild(root); WorldLighting.AddTo(root);
        var camera = new Camera3D { Current = true, Fov = 40, Position = new(26, 22, 28) }; root.AddChild(camera);
        camera.LookAt(new(0, 2, 0), Vector3.Up);
        var definition = VehicleCatalog.Load().Get("open-hopper");
        var vehicles = new PosedVehicleVisual[4];
        for (int i = 0; i < 4; i++)
        {
            var visual = new PosedVehicleVisual(definition, 1435); root.AddChild(visual); vehicles[i] = visual;
            visual.Vehicle.CargoFill!.SetLoad(i == 0 ? null : new(i == 3 ? "iron-ore" : "coal", i == 1 ? 35 : 70));
            visual.Apply(PosedVehicleVisual.Straight(definition, new((i - 1.5f) * 5, 0, 0), Vector3.Back));
        }
        var instances = new TrainInstances(); root.AddChild(instances); instances.Sync(vehicles);
        var ghost = new PosedVehicleVisual(definition, 1435, true); root.AddChild(ghost);
        ghost.Vehicle.CargoFill!.SetLoad(new("coal", 70));
        ghost.Apply(PosedVehicleVisual.Straight(definition, new(-12.5f, 0, 0), Vector3.Back)); ghost.Tint(true);
        for (int i = 0; i < 45; i++) await frame();
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = parent.GetViewport().GetTexture().GetImage();
        image.SavePng("res://.godot/cargo-visuals.png");
        root.QueueFree(); await frame();
    }
}
