using Godot;
using System;
using System.Collections.Generic;

namespace TransportSandbox;

/// <summary>Reusable broad phase for the existing point-versus-car braking envelope.</summary>
internal sealed class TrainCollisionIndex
{
    private const float CellSize = 64;
    private sealed class Body
    {
        public int Train, Car, MinX, MaxX, MinZ, MaxZ;
        public Vector3 Center;
        public Basis Inverse;
        public double Width, Height, HalfLength;
        public VehiclePose? Pose;
        public VehicleDefinition? Definition;
    }
    private readonly Dictionary<(int X, int Z), List<Body>> _cells = new();
    private readonly Stack<List<Body>> _lists = new();
    private readonly List<Body> _bodies = new();
    private readonly Dictionary<int, (int Start, int Count)> _trains = new();
    private int _count;
    private static int Cell(float value) => (int)Math.Floor(value / CellSize);

    public void Begin()
    {
        foreach (var list in _cells.Values) { list.Clear(); _lists.Push(list); }
        _cells.Clear(); _trains.Clear(); _count = 0;
    }
    public void Add(TrainConsist train, VehiclePose[] poses)
    {
        _trains[train.Id] = (_count, poses.Length);
        for (int i = 0; i < poses.Length; i++)
        {
            if (_count == _bodies.Count) _bodies.Add(new Body());
            var body = _bodies[_count++]; body.Train = train.Id; body.Car = i;
            Set(body, train.Items[i].Definition, poses[i]); Insert(body);
        }
    }
    private static void Set(Body body, VehicleDefinition definition, VehiclePose pose)
    {
        if (ReferenceEquals(body.Pose, pose) && ReferenceEquals(body.Definition, definition)) return;
        body.Pose = pose; body.Definition = definition;
        var basis = pose.Basis;
        body.Center = pose.BodyCenter; body.Inverse = basis.Inverse();
        body.Width = definition.Width; body.Height = definition.Height; body.HalfLength = definition.Length / 2 + 5;
        var extent = basis.X.Abs() * (float)(body.Width / 2) + basis.Y.Abs() * (float)body.Height + basis.Z.Abs() * (float)body.HalfLength;
        // Include a small tolerance so the broad phase remains conservative at float cell boundaries.
        body.MinX = Cell(body.Center.X - extent.X - .01f); body.MaxX = Cell(body.Center.X + extent.X + .01f);
        body.MinZ = Cell(body.Center.Z - extent.Z - .01f); body.MaxZ = Cell(body.Center.Z + extent.Z + .01f);
    }
    private void Insert(Body body)
    {
        for (int x = body.MinX; x <= body.MaxX; x++)
            for (int z = body.MinZ; z <= body.MaxZ; z++)
            {
                if (!_cells.TryGetValue((x, z), out var list)) _cells[(x, z)] = list = _lists.Count > 0 ? _lists.Pop() : new();
                list.Add(body);
            }
    }
    public void Update(TrainConsist train, VehiclePose[] poses)
    {
        // Train movement is applied sequentially: later trains see the new poses in this same substep.
        if (!_trains.TryGetValue(train.Id, out var range)) return;
        for (int i = range.Start; i < range.Start + range.Count; i++)
        {
            var body = _bodies[i];
            for (int x = body.MinX; x <= body.MaxX; x++)
                for (int z = body.MinZ; z <= body.MaxZ; z++) _cells[(x, z)].Remove(body);
            Set(body, train.Items[body.Car].Definition, poses[body.Car]); Insert(body);
        }
    }
    public bool Blocked(Vector3 point, double width, int ignoreTrain)
    {
        float radius = (float)(width / 2) + .01f;
        for (int x = Cell(point.X - radius); x <= Cell(point.X + radius); x++)
            for (int z = Cell(point.Z - radius); z <= Cell(point.Z + radius); z++)
                if (_cells.TryGetValue((x, z), out var bodies))
                    foreach (var body in bodies)
                    {
                        if (body.Train == ignoreTrain) continue;
                        var delta = body.Inverse * (point - body.Center);
                        if (Math.Abs(delta.X) < (body.Width + width) / 2 && Math.Abs(delta.Y) < body.Height && Math.Abs(delta.Z) < body.HalfLength) return true;
                    }
        return false;
    }
}
