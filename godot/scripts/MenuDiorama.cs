using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
    private double _elapsed, _arrivalTime;
    private TrackCurve _approach = null!;
    private double _stationStart, _cameraFront, _stopCenter, _longestConsist;
    public bool Departing { get; private set; }
    private TrainPlacement _placement = null!;
    private TrainConsist _consist = null!;
    private VehiclePhysics[] _physics = Array.Empty<VehiclePhysics>();
    private int _approachEdge;
    private double _centerDistance, _initialDistance, _simulationTime, _effort;
    public double ApproachSpeedLimit { get; private set; }
    public bool Arrived { get; private set; }
    public double ArrivalSpeed { get; private set; }

    public override void _Ready()
    {
        Catalog ??= VehicleCatalog.Load();
        Definitions ??= ConsistDefinition.LoadMenuConsists();
        if (Definitions.Count == 0)
            throw new InvalidOperationException("No menu consists are configured.");
        if (Preset < 0)
            Preset = GD.RandRange(0, Definitions.Count - 1);
        double Length(ConsistDefinition definition) => definition.Vehicles.Sum(item => Catalog.Get(item.Id).Length)
            + Math.Max(0, definition.Vehicles.Count - 1) * ConsistVisual.CouplerGap;
        _longestConsist = Definitions.Max(Length);
        _cameraFront = Length(Definitions[Preset]) / 2;
        WorldLighting.AddTo(this);
        Camera = new()
        {
            Fov = 38,
            Near = 0.1f,
            Far = 1800
        };
        AddChild(Camera);
        Camera.Current = true;
        Smoke = new() { Camera = Camera };
        AddChild(Smoke);
        Audio = new() { Settings = SoundSettings };
        AddChild(Audio);
        BuildTrainScene();
        UpdateCamera(0, 16.0 / 9);
    }
    private void BuildTrainScene()
    {
        bool firstTrain = !IsInstanceValid(Scenery);
        if (!firstTrain)
        {
            Scenery.RemoveChild(Train);
            Train.QueueFree();
        }
        Composition = Definitions![Preset];
        if (firstTrain)
        {
            Scenery = new() { Name = "StationScene" };
            AddChild(Scenery);
        }
        Train = ConsistVisual.Create(Catalog, Composition, posed: true);
        Train.Name = Composition.Id;
        Scenery.AddChild(Train);
        float half = (float)(_longestConsist / 2 + 16);
        _stopCenter = _cameraFront - Train.Length / 2;
        double stationCenter = _cameraFront + 16 - half;
        _arrivalTime = _simulationTime = 0;
        _effort = 0;
        Arrived = Departing = false;
        if (firstTrain)
        {
            Network = new();
            var start = Network.AddNode(new(0, 0, (float)stationCenter - half), Composition.Gauge);
            var end = Network.AddNode(new(0, 0, (float)stationCenter + half), Composition.Gauge);
            var edge = Network.ConnectNodes(start, end)!;
            Network.AddStation(edge, 1);
            var exit = Network.AddNode(new(0, 0, (float)stationCenter + half + (float)_longestConsist + 200), Composition.Gauge);
            Network.ConnectNodes(end, exit);
            // Continue the station tangent through a broad curve behind the platform.
            // Its length also leaves room for the longest consist before arrival.
            var approachStart = Network.AddNode(new(-180, 0, (float)stationCenter - half - (float)_longestConsist - 280), Composition.Gauge);
            var approachEdge = Network.ConnectNodes(approachStart, start)!;
            approachStart.Axis = Vector3.Back;
            _approach = new TrackCurve(Network.Controls(approachEdge));
            _stationStart = stationCenter - half;
            _approachEdge = approachEdge.Id;
            _placement = new(Network);
        }
        else if (Network.Edges.Values.Any(edge => edge.Gauge != Composition.Gauge))
        {
            foreach (var node in Network.Nodes.Values) node.Gauge = Composition.Gauge;
            foreach (var edge in Network.Edges.Values) edge.Gauge = Composition.Gauge;
            Network.NotifyChanged();
            // Rebuild the new gauge synchronously, with no empty or overlapping track frame.
            foreach (var holder in Track.Holders.Values) holder.Hide();
            Track._Process(0);
        }
        _consist = new();
        for (int i = 0; i < Composition.Vehicles.Count; i++)
            _consist.Items.Add(new(i, Catalog.Get(Composition.Vehicles[i].Id), Composition.Vehicles[i].Reversed ? -1 : 1));
        _physics = _consist.Items.Select(item => item.Physics).ToArray();
        _initialDistance = _centerDistance = Train.Length / 2 + 20;
        // Use gameplay's radius sampling and HO curve limit over this entire approach.
        var route = new TrainRoute(_placement, new(_approachEdge, 0), 1);
        ApproachSpeedLimit = TrainPhysics.Performance(_physics, ho: true).MaxSpeed;
        for (double distance = 0; distance <= _approach.Length; distance += 5)
        {
            var location = route.Sample(new(_approachEdge, 0), distance).Position;
            ApproachSpeedLimit = Math.Min(ApproachSpeedLimit,
                TrainPhysics.CurveLimit(route.TurnRadius(location), ho: true));
        }
        ArrivalSpeed = ApproachSpeedLimit;
        if (firstTrain)
        {
            Track = new()
            {
                Name = "Track",
                Network = Network,
                Camera = Camera
            };
            Scenery.AddChild(Track);
            Track.Markers.Hide();
            Track._Process(0);
            Scenery.AddChild(CreateGround());
            Scenery.AddChild(new StationScenery(half) { Position = new(0, 0, (float)stationCenter) });
            Scenery.AddChild(TreeScenery.CreateWoodland());
            Scenery.AddChild(CreateArrivalWoodland());
        }
        UpdateArrival(0);
    }
    private static MeshInstance3D CreateGround()
    {
        // Share gameplay's grass palette, world-space grain, lighting and distance fog.
        using var plane = new PlaneMesh { Size = new(1800, 1800), SubdivideWidth = 89, SubdivideDepth = 89 };
        using var arrays = plane.GetMeshArrays();
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var colors = new Color[vertices.Length];
        var settings = new WorldSettings();
        var sampler = new Terrain.TerrainSampler(settings.Seed, settings.MountainHeight, settings.HorizontalScale, settings.SeaLevel);
        for (int i = 0; i < vertices.Length; i++)
        {
            // A flat meadow above the shore band, without alpine or exposed-rock blending.
            var color = sampler.Color(vertices[i].X, vertices[i].Z, settings.SeaLevel + 20, 0);
            colors[i] = new Color((float)color.R, (float)color.G, (float)color.B).LinearToSrgb();
        }
        arrays[(int)Mesh.ArrayType.Color] = colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D
        {
            Name = "Ground",
            Mesh = mesh,
            Position = new(0, -.66f, 0),
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") }
        };
    }
    private TreeChunk CreateArrivalWoodland()
    {
        var trees = new List<TreeData>();
        // Screen the entire initial footprint of the longest train, then let it emerge
        // around the bend. Low conifers fill the gaps beneath the taller canopy.
        double end = Math.Min(_approach.Length - 60, _longestConsist + 110);
        for (double distance = 0; distance <= end; distance += 7)
        {
            var point = _approach.PointAt(distance / _approach.Length).Float();
            var right = _approach.FrameAt(distance / _approach.Length).Right.Float();
            for (int row = 0; row < 4; row++)
            {
                float side = row == 3 ? -11 : 11 + row * 8;
                double phase = distance * .71 + row * 5.3;
                var position = point + right * (side + (float)Math.Sin(phase));
                float height = 17 + (float)Math.Cos(phase) * 2;
                trees.Add(new(position.X, -.65f, position.Z, height, 7, (float)phase, 1));
                trees.Add(new(position.X + right.X, -.65f, position.Z + right.Z,
                    7 + (float)Math.Sin(phase), 5, (float)(phase + 1), 1));
            }
        }
        return TreeScenery.Create(trees, "ArrivalWoodland");
    }
    public void UpdateCamera(double timeMs, double aspect)
    {
        double front = _cameraFront, portrait = Math.Clamp(1 / aspect, 1, 1.65), drift = Math.Sin(timeMs * 0.00004) * 0.3;
        Camera.Position = new((float)(-7 / portrait + drift), 2.8f, (float)(front + 13 * portrait));
        Camera.LookAt(new(-0.8f, 2, (float)(front - 9 / portrait)));
    }
    public override void _Process(double delta)
    {
        _elapsed += delta;
        var viewport = GetViewport().GetVisibleRect().Size;
        UpdateCamera(_elapsed * 1000, viewport.X / Math.Max(1.0, viewport.Y));
        _arrivalTime += Math.Clamp(delta, 0, .1);
        UpdateArrival(_arrivalTime);
        Audio.UpdateSounds(AudioVehicles(), Camera, delta);
    }
    /// <summary>Fixed-step HO gameplay physics, placement and imported running-gear animation.</summary>
    public void UpdateArrival(double seconds)
    {
        if (seconds < _simulationTime)
        {
            _simulationTime = 0;
            _centerDistance = _initialDistance;
            ArrivalSpeed = ApproachSpeedLimit;
            Arrived = false;
            _effort = 0;
            foreach (var visual in Train.PosedVehicles)
                visual.AdvanceRunningGear(-visual.WheelTurns * Math.Tau * visual.Definition.RunningGearWheelRadius);
        }
        double travelled = 0;
        const double dt = 1.0 / 60;
        double stop = _approach.Length + _stopCenter - _stationStart;
        while (_simulationTime + dt <= seconds + 1e-9)
        {
            _simulationTime += dt;
            if (Departing)
            {
                double departureLimit = _centerDistance - Train.Length / 2 < _approach.Length
                    ? ApproachSpeedLimit : double.PositiveInfinity;
                var departure = TrainPhysics.Step(_physics, ArrivalSpeed, true, 0, ho: true, dt: dt, limit: departureLimit);
                _centerDistance += departure.Distance;
                travelled += departure.Distance;
                ArrivalSpeed = departure.Speed;
                _effort = departure.Effort;
                // Replace only after the entire consist has passed behind the unchanged camera.
                double tailZ = _stationStart + _centerDistance - _approach.Length - Train.Length / 2;
                if (tailZ > _cameraFront + 100)
                {
                    Preset = Definitions!.Count == 1 ? 0 : (Preset + GD.RandRange(1, Definitions.Count - 1)) % Definitions.Count;
                    BuildTrainScene();
                    return;
                }
                continue;
            }
            if (Arrived) continue;
            double remaining = stop - _centerDistance;
            if (remaining < .05 && ArrivalSpeed < .1)
            {
                travelled += remaining;
                _centerDistance = stop;
                ArrivalSpeed = _effort = 0;
                Arrived = true;
                continue;
            }
            double limit = Math.Min(ApproachSpeedLimit,
                Math.Sqrt(2 * TrainPhysics.BrakingDeceleration(0, ho: true) * remaining));
            var next = TrainPhysics.Step(_physics, ArrivalSpeed, true, 0, ho: true, dt: dt, limit: limit);
            double distance = Math.Min(next.Distance, remaining);
            _centerDistance += distance;
            travelled += distance;
            ArrivalSpeed = next.Speed;
            _effort = next.Effort;
        }
        var position = _placement.Sample(new(_approachEdge, 0), _centerDistance).Position;
        // Keep the original menu railhead height while using gameplay's independent body/bogie poses.
        TrainSample Sample(TrainPosition origin, double distance)
        {
            var sample = _placement.Sample(origin, distance);
            return sample with { Point = sample.Point + Vector3.Up * TrackRenderer.RailHeadHeight };
        }
        var poses = _placement.Poses(_consist, position, Sample);
        for (int i = 0; i < poses.Length; i++)
        {
            Train.PosedVehicles[i].Apply(poses[i]);
            Train.PosedVehicles[i].AdvanceRunningGear(travelled * _consist.Items[i].Facing);
        }
        Smoke.Outlets.Clear();
        foreach (var visual in Train.PosedVehicles)
            Smoke.Outlets.AddRange(visual.Exhausts().Select(outlet => outlet with { Effort = _effort }));
    }
    public void DispatchTrain()
    {
        if (Departing) return;
        Arrived = false;
        Departing = true;
        // Tests/tools can advance the deterministic clock independently of frame processing.
        _arrivalTime = _simulationTime;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left } mouse
            || Departing || !HitsTrain(mouse.Position)) return;
        DispatchTrain();
        GetViewport().SetInputAsHandled();
    }
    public bool HitsTrain(Vector2 mouse)
    {
        var origin = Camera.ProjectRayOrigin(mouse);
        var direction = Camera.ProjectRayNormal(mouse);
        foreach (var mesh in Train.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (!mesh.IsVisibleInTree() || mesh.Mesh == null) continue;
            var inverse = mesh.GlobalTransform.AffineInverse();
            var local = inverse * origin;
            var ray = inverse.Basis * direction;
            if (!mesh.GetAabb().IntersectsSegment(local, local + ray * Camera.Far)) continue;
            var faces = mesh.Mesh.GetFaces();
            for (int i = 0; i + 2 < faces.Length; i += 3)
                if (Geometry3D.RayIntersectsTriangle(local, ray, faces[i], faces[i + 1], faces[i + 2]).VariantType == Variant.Type.Vector3)
                    return true;
        }
        return false;
    }
    public override void _ExitTree() => _placement?.Dispose();
    private IEnumerable<TrainAudioVehicle> AudioVehicles()
    {
        for (int i = 0; i < Train.Vehicles.Count; i++)
        {
            var vehicle = Train.Vehicles[i];
            yield return new(i, vehicle.Definition.Id, Train.PosedVehicles[i].Pose!.BodyCenter, ArrivalSpeed, _effort, double.PositiveInfinity);
        }
    }
}
