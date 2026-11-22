using System;

namespace TransportSandbox;

// DTOs contain absolute double world coordinates and stable IDs; no scene objects or terrain meshes.
public sealed class GameSave
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "New World";
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public WorldSettings Settings { get; set; } = new();
    public SavedNode[] Nodes { get; set; } = Array.Empty<SavedNode>();
    public TrackEdge[] Edges { get; set; } = Array.Empty<TrackEdge>();
    public SavedGroup[] Groups { get; set; } = Array.Empty<SavedGroup>();
    public SavedSignal[] Signals { get; set; } = Array.Empty<SavedSignal>();
    public SavedTrain[] Trains { get; set; } = Array.Empty<SavedTrain>();
    public SavedCamera Camera { get; set; } = new();
    public SavedLease[] Reservations { get; set; } = Array.Empty<SavedLease>();
    public int NextNode { get; set; } = 1;
    public int NextEdge { get; set; } = 1;
    public int NextGroup { get; set; } = 1;
    public int NextStation { get; set; } = 1;
    public int NextTrain { get; set; } = 1;
    public int NextItem { get; set; } = 1;
}
public sealed record SavedNode(int Id, int Gauge, int Group, TrackVector Position, TrackVector Axis);
public sealed record SavedGroup(int Id, int Anchor, TrackVector Origin, TrackVector Axis, System.Collections.Generic.Dictionary<int, float> Offsets);
public sealed record SavedLease(int Train, SignalKey Signal, int[] Edges, bool Entered);
public sealed record SavedSignal(int Node, int Side, RailSignalKind Kind);
public sealed record SavedItem(int Id, string Vehicle, int Facing, string? Livery = null, CargoLoad? Cargo = null);
public sealed record SavedRouteLeg(int Index, int Edge, int Facing);
public sealed record SavedRoute(uint Random, SavedRouteLeg[] Legs, int[] Plan, int PlanStart, int PlanStep, int Revision, int[]? DetourLegs = null);
public sealed class SavedTrain
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public SavedItem[] Items { get; set; } = Array.Empty<SavedItem>();
    public bool Running { get; set; }
    public double Speed { get; set; }
    public int Direction { get; set; } = 1;
    public TrainOrder[] Orders { get; set; } = Array.Empty<TrainOrder>();
    public int OrderIndex { get; set; }
    public double WaitRemaining { get; set; } = -1;
    public int NextOrder { get; set; } = 1;
    public TrainPosition? Position { get; set; }
    public bool Supported { get; set; } = true;
    public SavedRoute? Route { get; set; }
    public SavedPose[] Poses { get; set; } = Array.Empty<SavedPose>();
    public TrainWanderingSave? Wandering { get; set; }
}
public sealed record TrainWanderingSave(int Direction, int Station, int EndRouteIndex, int EndEdge,
    int ServicedStation, int ServicedIndex, double Distance, bool Servicing);
public sealed record SavedPose(TrackVector Center, TrackVector Front, TrackVector Rear, TrackVector FrontDirection, TrackVector RearDirection, SavedPose[] Sections);
public sealed class SavedCamera
{
    public TrackVector Position { get; set; }
    public TrackVector Target { get; set; }
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double Distance { get; set; }
    public bool Free { get; set; }
}
