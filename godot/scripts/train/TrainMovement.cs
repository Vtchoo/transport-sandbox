using Godot;
using System;
using System.Linq;

namespace TransportSandbox;

public partial class TrainEditor
{
    public bool HoPhysics = true;
    public string WorldSeed = "railway";
    public event Action? MotionChanged;
    private double _motionHud;
    private uint RouteSeed(int id)
    {
        uint hash = 2166136261;
        foreach (char c in WorldSeed + ":" + id)
            hash = unchecked((hash ^ c) * 16777619);
        return hash;
    }
    private TrainRoute RouteFor(ParkedTrain parked) => parked.Route ??= new(Placement, parked.Position, RouteSeed(parked.Id));
    public void ToggleRunning(int id)
    {
        var train = Builder.Get(id);
        var parked = Parked.FirstOrDefault(p => p.Id == id);
        string? error = parked == null ? "Place the train on track before starting it."
            : !train.Items.Any(i => i.Definition.Physics.PowerW > 0) ? "Add a locomotive before starting this train."
            : !parked.Supported ? "Repair the supporting track before starting this train." : null;
        if (error != null)
        {
            Warning?.Invoke(error);
            return;
        }
        train.Running = !train.Running;
        MotionChanged?.Invoke();
    }
    /// <summary>Same bounded 1/60 s substeps as the browser; all distances and forces remain SI.</summary>
    public void AdvanceMovement(double delta)
    {
        bool moving = Parked.Any(p => !Builder.Get(p.Id).Stopped);
        for (double remaining = Math.Clamp(delta, 0, .1); remaining > 1e-8;)
        {
            double step = Math.Min(remaining, 1.0 / 60);
            MoveTrains(step);
            remaining -= step;
        }
        if (moving)
            SyncInstances();
        _motionHud += delta;
        if (_motionHud >= .2)
        {
            _motionHud = 0;
            MotionChanged?.Invoke();
        }
    }
    private void MoveTrains(double dt)
    {
        double catalogMax = Catalog.Definitions.Max(d => d.Physics.MaxSpeedMps);
        foreach (var parked in Parked)
        {
            parked.Effort = 0;
            var train = Builder.Get(parked.Id);
            if (train.Stopped)
                continue;
            var definitions = train.Items.Select(i => i.Definition.Physics).ToArray();
            var offsets = train.Offsets;
            int direction = train.TravelDirection;
            double length = train.Length;
            try
            {
                var route = RouteFor(parked);
                var performance = TrainPhysics.Performance(definitions, HoPhysics, catalogMax);
                double grade = train.Items.Select((item, i) => item.Definition.Physics.MassKg * route.Sample(parked.Position, offsets[i]).Tangent.Y * direction).Sum() / performance.Mass;
                double braking = TrainPhysics.BrakingDeceleration(grade, HoPhysics);
                double lookahead = Math.Max(100, train.Speed * train.Speed / (2 * braking) + 50);
                double clearance = Math.Max(0, route.Clearance(parked.Position, direction * (length / 2 + lookahead)) - length / 2 - 2);
                double free = clearance;
                var others = Parked.Where(p => p != parked).ToArray();
                if (others.Length > 0)
                    for (double d = 0; d < clearance; d += 4)
                    {
                        var point = route.Sample(parked.Position, direction * (length / 2 + d)).Point;
                        bool blocked = others.Any(other => other.Poses.Select((pose, i) =>
                        {
                            var definition = Builder.Get(other.Id).Items[i].Definition;
                            var delta = pose.Basis.Inverse() * (point - pose.BodyCenter);
                            return Math.Abs(delta.X) < (definition.Width + train.Items[0].Definition.Width) / 2 && Math.Abs(delta.Y) < definition.Height && Math.Abs(delta.Z) < definition.Length / 2 + 5;
                        }).Any(v => v));
                        if (blocked)
                        {
                            free = Math.Max(0, d - 5);
                            break;
                        }
                    }
                double limit = Math.Sqrt(2 * braking * free);
                double scan = Math.Min(lookahead, clearance);
                for (double d = -length / 2; d <= length / 2 + scan; d += 5)
                {
                    var location = route.Sample(parked.Position, direction * d).Position;
                    double curveLimit = TrainPhysics.CurveLimit(route.TurnRadius(location), HoPhysics);
                    limit = Math.Min(limit, Math.Sqrt(curveLimit * curveLimit + 2 * braking * Math.Max(0, d - length / 2)));
                }
                var next = TrainPhysics.Step(definitions, Math.Abs(train.Speed), train.Running, grade, HoPhysics, dt, limit, catalogMax);
                if (free < .05 && Math.Abs(train.Speed) < .1)
                {
                    train.Speed = 0;
                    if (train.Running && clearance < .05)
                        train.TravelDirection = -direction;
                    continue;
                }
                double distance = Math.Min(next.Distance, free);
                var position = route.Sample(parked.Position, direction * distance).Position;
                var poses = Placement.Poses(train, position, route.Sample);
                if (Occupied(train, poses, others))
                {
                    train.Speed = 0;
                    continue;
                }
                for (int i = 0; i < poses.Length; i++)
                {
                    parked.Visuals[i].Apply(poses[i]);
                    parked.Visuals[i].AdvanceRunningGear(direction * distance * train.Items[i].Facing);
                }
                parked.Position = position;
                parked.Poses = poses;
                parked.Effort = next.Effort;
                parked.Supported = true;
                train.Speed = next.Distance > free && free < .1 ? 0 : next.Speed * direction;
                route.Trim(position, length + 3000);
            }
            catch (InvalidOperationException error)
            {
                train.Speed = 0;
                train.Running = false;
                parked.Supported = false;
                Warning?.Invoke("Train stopped: " + error.Message);
                MotionChanged?.Invoke();
            }
        }
    }
}
