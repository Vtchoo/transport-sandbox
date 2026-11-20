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
    private readonly TrainCollisionIndex _collisions = new();
    private RailInterlocking? _interlocking;
    public RailInterlocking Interlocking => _interlocking ??= new(Network, Placement);
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
        using var probe = FrameProbe.Measure("movement");
        _detourBudget = 2;
        foreach (var parked in Parked) parked.DetourCooldown = Math.Max(0, parked.DetourCooldown - Math.Clamp(delta, 0, .1));
        PrepareRouting();
        bool moving = Parked.Any(p => !Builder.Get(p.Id).Stopped);
        for (double remaining = Math.Clamp(delta, 0, .1); remaining > 1e-8;)
        {
            double step = Math.Min(remaining, 1.0 / 60);
            MoveTrains(step);
            remaining -= step;
        }
        if (moving)
        {
            using var instancesProbe = FrameProbe.Measure("instances.sync");
            SyncInstances();
        }
        _motionHud += delta;
        if (_motionHud >= .2)
        {
            _motionHud = 0;
            MotionChanged?.Invoke();
        }
    }
    private void MoveTrains(double dt)
    {
        using var probe = FrameProbe.Measure("substeps");
        if (Network.Signals.Count > 0 || _interlocking != null)
        {
            Interlocking.Update(Parked.Select(p => new SignalTrain(p.Id, RouteFor(p), p.Position, Builder.Get(p.Id).Length, Builder.Get(p.Id).TravelDirection)));
            foreach (var p in Parked)
                if (Builder.Get(p.Id).Stopped) Interlocking.ReleasePending(p.Id);
        }
        _collisions.Begin();
        foreach (var p in Parked) _collisions.Add(Builder.Get(p.Id), p.Poses);
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
                if (parked.SupportRevision != _revision)
                {
                    try { Placement.Poses(train, parked.Position, route.Sample); }
                    catch (RouteBlocked) { throw new InvalidOperationException("Track supporting this train was disconnected."); }
                    parked.SupportRevision = _revision;
                }
                var performance = TrainPhysics.Performance(definitions, HoPhysics, catalogMax);
                double grade = train.Items.Select((item, i) => item.Definition.Physics.MassKg * route.Sample(parked.Position, offsets[i]).Tangent.Y * direction).Sum() / performance.Mass;
                double braking = TrainPhysics.BrakingDeceleration(grade, HoPhysics);
                double lookahead = Math.Max(100, train.Speed * train.Speed / (2 * braking) + 50);
                var command = ExecuteOrders(parked, train, route, dt, lookahead);
                double endClearance = route.Clearance(parked.Position, direction * (length / 2 + lookahead)) - length / 2;
                // A platform at a physical dead end must still be reachable at its exact edge.
                // Signal and collision stopping distances remain independent safety limits.
                bool stationAtEnd = !route.BlockedAhead && double.IsFinite(command.StopDistance)
                    && Math.Abs(endClearance - command.StopDistance) < .1;
                double clearance = Math.Max(0, endClearance - (stationAtEnd ? 0 : 2));
                if (route.BlockedAhead)
                {
                    command = (true, command.StopDistance);
                    if (parked.Journey is { Reachable: true } blocked) { blocked.NeedsReplan = true; blocked.HoldForReplan = true; }
                }
                double free = Math.Min(clearance, command.StopDistance);
                bool reverseAtSignal = false;
                if (Network.Signals.Count > 0)
                {
                    if (command.Hold) Interlocking.ReleasePending(train.Id);
                    var signal = Interlocking.Limit(new(train.Id, route, parked.Position, length, direction), lookahead, commit: !command.Hold);
                    if (!command.Hold && TryTrafficDetour(parked, train, signal, lookahead))
                    {
                        route = RouteFor(parked);
                        command = ExecuteOrders(parked, train, route, dt, lookahead);
                        endClearance = route.Clearance(parked.Position, direction * (length / 2 + lookahead)) - length / 2;
                        stationAtEnd = !route.BlockedAhead && double.IsFinite(command.StopDistance) && Math.Abs(endClearance - command.StopDistance) < .1;
                        clearance = Math.Max(0, endClearance - (stationAtEnd ? 0 : 2));
                        free = Math.Min(clearance, command.StopDistance);
                        signal = Interlocking.Limit(new(train.Id, route, parked.Position, length, direction), lookahead);
                    }
                    free = Math.Min(free, signal.Distance);
                    reverseAtSignal = signal.CanReverse && signal.Distance < .05;
                    if (signal.Message.Length > 0) train.Orders.Status = signal.Message;
                }
                var others = Parked.Where(p => p != parked).ToArray();
                using (FrameProbe.Measure("collision"))
                {
                    if (others.Length > 0)
                        for (double d = 0; d < free; d += 4)
                        {
                            var point = route.Sample(parked.Position, direction * (length / 2 + d)).Point;
                            bool blocked = _collisions.Blocked(point, train.Items[0].Definition.Width, train.Id);
                            if (blocked)
                            {
                                free = Math.Min(free, Math.Max(0, d - 5));
                                break;
                            }
                        }
                }
                double limit = command.Hold ? 0 : Math.Sqrt(2 * braking * free);
                double scan = Math.Min(lookahead, clearance);
                using (FrameProbe.Measure("curve.scan"))
                for (double d = -length / 2; d <= length / 2 + scan; d += 5)
                {
                    var location = route.Sample(parked.Position, direction * d).Position;
                    double curveLimit = TrainPhysics.CurveLimit(route.TurnRadius(location), HoPhysics);
                    limit = Math.Min(limit, Math.Sqrt(curveLimit * curveLimit + 2 * braking * Math.Max(0, d - length / 2)));
                }
                var next = TrainPhysics.Step(definitions, Math.Abs(train.Speed), train.Running && !command.Hold, grade, HoPhysics, dt, limit, catalogMax);
                if (free < .05 && Math.Abs(train.Speed) < .1)
                {
                    train.Speed = 0;
                    if (train.Running && !command.Hold && (clearance < .05 || reverseAtSignal) && command.StopDistance > .05)
                        train.TravelDirection = -direction;
                    continue;
                }
                double distance = Math.Min(next.Distance, free);
                var position = route.Sample(parked.Position, direction * distance).Position;
                VehiclePose[] poses;
                using (FrameProbe.Measure("poses")) poses = Placement.Poses(train, position, route.Sample);
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
                if (parked.Journey is { } journey) journey.Distance -= distance;
                if (parked.Wandering is { Station: not 0, Servicing: false } wandering) wandering.Distance -= distance;
                parked.Poses = poses;
                _collisions.Update(train, poses);
                parked.Effort = next.Effort;
                parked.Supported = true;
                train.Speed = next.Distance > free && free < .1 ? 0 : next.Speed * direction;
                route.Trim(position, length + 3000);
            }
            catch (RouteBlocked)
            {
                // A broken future itinerary is different from missing supporting track.
                train.Speed = 0;
                if (parked.Journey is { } journey) { journey.NeedsReplan = true; journey.HoldForReplan = true; }
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
