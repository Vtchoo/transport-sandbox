using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>A station assembled from the same rail, platform, vehicle and tree objects as gameplay.</summary>
public partial class MenuDiorama : Node3D
{
    public int Preset = -1;
    public VehicleCatalog? Catalog;
    public IReadOnlyList<ConsistDefinition>? Definitions;
    public ConsistDefinition Composition = null!;
    public Camera3D Camera = null!;
    public Node3D Scenery = null!;
    public MenuSmoke Smoke = null!;
    public ConsistVisual Train = null!;
    public TrackNetwork Network = null!;
    public TrackRenderer Track = null!;
    public TrainAudio Audio = null!;
    public SoundSettings SoundSettings = new();
    private double _elapsed;
    public override void _Ready()
    {
        Catalog ??= VehicleCatalog.Load();
        Definitions ??= ConsistDefinition.LoadMenuConsists();
        if (Definitions.Count == 0)
            throw new InvalidOperationException("No menu consists are configured.");
        if (Preset < 0)
            Preset = GD.RandRange(0, Definitions.Count - 1);
        Composition = Definitions[Preset];
        Scenery = new()
        {
            Name = "StationScene"
        };
        AddChild(Scenery);
        WorldLighting.AddTo(this);
        Camera = new()
        {
            Fov = 38,
            Near = 0.1f,
            Far = 1800
        };
        AddChild(Camera);
        Camera.Current = true;
        Train = ConsistVisual.Create(Catalog, Composition);
        Train.Name = Composition.Id;
        Train.Position = Vector3.Up * TrackRenderer.RailHeadHeight;
        Scenery.AddChild(Train);
        float half = (float)(Train.Length / 2 + 16);
        Network = new();
        var start = Network.AddNode(new(0, 0, -half), Composition.Gauge);
        var end = Network.AddNode(new(0, 0, half), Composition.Gauge);
        var edge = Network.ConnectNodes(start, end)!;
        Network.AddStation(edge, 1);
        Track = new()
        {
            Name = "Track",
            Network = Network,
            Camera = Camera
        };
        Scenery.AddChild(Track);
        Track.Markers.Hide();
        Scenery.AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new(1800, 1800) },
            Position = new(0, -0.66f, 0),
            MaterialOverride = TrackRenderer.Material(new("6c8050"))
        });
        Scenery.AddChild(new StationScenery(half));
        Scenery.AddChild(TreeScenery.CreateWoodland());
        Smoke = new()
        {
            Camera = Camera
        };
        Smoke.Outlets.AddRange(Train.WorldExhausts());
        AddChild(Smoke);
        Audio = new() { Settings = SoundSettings };
        AddChild(Audio);
        UpdateCamera(0, 16.0 / 9);
    }
    public void UpdateCamera(double timeMs, double aspect)
    {
        double front = Train.Length / 2, portrait = Math.Clamp(1 / aspect, 1, 1.65), drift = Math.Sin(timeMs * 0.00004) * 0.3;
        Camera.Position = new((float)(-7 / portrait + drift), 2.8f, (float)(front + 13 * portrait));
        Camera.LookAt(new(-0.8f, 2, (float)(front - 9 / portrait)));
    }
    public override void _Process(double delta)
    {
        _elapsed += delta;
        var viewport = GetViewport().GetVisibleRect().Size;
        UpdateCamera(_elapsed * 1000, viewport.X / Math.Max(1.0, viewport.Y));
        Audio.UpdateSounds(AudioVehicles(), Camera, delta);
    }
    private IEnumerable<TrainAudioVehicle> AudioVehicles()
    {
        for (int i = 0; i < Train.Vehicles.Count; i++)
        {
            var vehicle = Train.Vehicles[i];
            yield return new(i, vehicle.Definition.Id, vehicle.GlobalPosition, 0, 0, double.PositiveInfinity);
        }
    }
}
