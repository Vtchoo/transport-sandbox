using Godot;
using System;
using System.Linq;

namespace TransportSandbox;

public partial class TrainEditor
{
    public bool HoPhysics = true;
    public bool ParallelCalculations = true;
    public string WorldSeed = "railway";
    public event Action? MotionChanged;
    private double _motionHud;
    private double _movementRemainder;
    private bool _interpolatedRendering;
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
    /// <summary>Accumulate fixed 60 Hz simulation ticks; publish only the final pose of each rendered frame.</summary>
    public void AdvanceMovement(double delta, bool interpolate = false)
    {
        using var probe = FrameProbe.Measure("movement");
        _interpolatedRendering = interpolate;
        _detourBudget = 2;
        foreach (var parked in Parked) parked.DetourCooldown = Math.Max(0, parked.DetourCooldown - Math.Clamp(delta, 0, .1));
        PrepareRouting();
        // Track edits are safety events, even on a render frame without a simulation tick.
        foreach (var parked in Parked)
        {
            if (parked.SupportRevision == _revision) continue;
            parked.SupportRevision = _revision;
            var train = Builder.Get(parked.Id);
            try { Placement.Poses(train, parked.Position, parked.Route == null ? null : parked.Route.Sample); }
            catch (InvalidOperationException error)
            {
                bool moving = !train.Stopped;
                parked.Supported = false; train.Running = false; train.Speed = 0; parked.PreviousPoses = null;
                if (moving) { Warning?.Invoke("Train stopped: " + error.Message); MotionChanged?.Invoke(); }
            }
        }
        _movementRemainder += Math.Clamp(delta, 0, .1);
        const double step = 1.0 / 60;
        while (_movementRemainder + 1e-10 >= step)
        {
            MoveTrains(step);
            _movementRemainder = Math.Max(0, _movementRemainder - step);
        }
        bool visualChanged = false;
        using (FrameProbe.Measure("poses.apply"))
            foreach (var parked in Parked)
            {
                bool blend = interpolate && parked.PreviousPoses is { } previous && previous.Length == parked.Poses.Length && !ReferenceEquals(previous, parked.Poses);
                if (!parked.VisualDirty && !blend) continue;
                var train = Builder.Get(parked.Id);
                for (int i = 0; i < parked.Poses.Length; i++)
                {
                    parked.Visuals[i].Apply(blend ? BlendPose(parked.PreviousPoses![i], parked.Poses[i], (float)(_movementRemainder / step)) : parked.Poses[i]);
                    parked.Visuals[i].AdvanceRunningGear(parked.RunningGearDistance * train.Items[i].Facing);
                }
                parked.VisualDirty = false; parked.RunningGearDistance = 0; visualChanged = true;
            }
        if (visualChanged)
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
        foreach (var parked in Parked) parked.PreviousPoses = parked.Poses;
        if (Network.Signals.Count > 0 || _interlocking != null)
        {
            Interlocking.Update(Parked.Select(p => new SignalTrain(p.Id, RouteFor(p), p.Position, Builder.Get(p.Id).Length, Builder.Get(p.Id).TravelDirection)));
            foreach (var p in Parked)
                if (Builder.Get(p.Id).Stopped) Interlocking.ReleasePending(p.Id);
        }
        using (FrameProbe.Measure("collision.index"))
        {
            _collisions.Begin();
            foreach (var p in Parked) _collisions.Add(Builder.Get(p.Id), p.Poses);
        }
        double catalogMax = Catalog.Definitions.Max(d => d.Physics.MaxSpeedMps);
        var workItems = new System.Collections.Generic.List<MovementWork>();
        foreach (var parked in Parked)
        {
            parked.Effort = 0;
            var train = Builder.Get(parked.Id);
            parked.MotorState = Math.Abs(train.Speed) <= .001 ? TrainMotorState.Stopped
                : train.Running ? TrainMotorState.Coasting : TrainMotorState.Braking;
            parked.StationAudio.Initialize(Network.Edges.TryGetValue(parked.Position.Edge, out var supporting) ? supporting.Station : 0, train.Speed);
            if (train.Stopped) continue;
            var route = RouteFor(parked);
            var work = new MovementWork(parked, train, route, HoPhysics, catalogMax);
            workItems.Add(work);
            try
            {
                if (parked.SupportRevision != _revision)
                {
                    try { Placement.Poses(train, parked.Position, route.Sample); }
                    catch (RouteBlocked) { throw new InvalidOperationException("Track supporting this train was disconnected."); }
                    parked.SupportRevision = _revision;
                }
            }
            catch (InvalidOperationException error) { work.Error = error; }
        }
        using (FrameProbe.Measure("grade.batch"))
            TrainCalculationBatch.Run(workItems, work => work.CalculateGrade(), ParallelCalculations);
        // Orders, live reservations and detour choices stay on the coordinator thread.
        foreach (var work in workItems)
        {
            if (work.Error != null) continue;
            var parked = work.Parked; var train = work.Train; var route = work.Route;
            double grade = work.Grade, length = work.Length; int direction = work.Direction;
            try
            {
                double braking = TrainPhysics.BrakingDeceleration(grade, HoPhysics);
                double lookahead = Math.Max(100, train.Speed * train.Speed / (2 * braking) + 50);
                (bool Hold, double StopDistance) command;
                using (FrameProbe.Measure("orders.execute")) command = ExecuteOrders(parked, train, route, dt, lookahead);
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
                work.Route = route; work.Braking = braking; work.Lookahead = lookahead;
                work.Command = command; work.Clearance = clearance; work.Free = free; work.ReverseAtSignal = reverseAtSignal;
            }
            catch (InvalidOperationException error) { work.Error = error; }
        }
        using (FrameProbe.Measure("curve.batch"))
            TrainCalculationBatch.Run(workItems, work => work.CalculateCurveLimit(), ParallelCalculations);
        foreach (var work in workItems)
        {
            var parked = work.Parked; var train = work.Train;
            var route = work.Route; var command = work.Command;
            var definitions = work.Definitions; double grade = work.Grade, length = work.Length;
            double braking = work.Braking, free = work.Free, clearance = work.Clearance;
            int direction = work.Direction; bool reverseAtSignal = work.ReverseAtSignal;
            try
            {
                if (work.Error != null) throw work.Error;
                using (FrameProbe.Measure("collision"))
                {
                    if (Parked.Count > 1)
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
                double limit = Math.Min(work.CurveLimit, command.Hold ? 0 : Math.Sqrt(2 * braking * free));
                var next = TrainPhysics.Step(definitions, Math.Abs(train.Speed), train.Running && !command.Hold, grade, HoPhysics, dt, limit, catalogMax);
                var motorState = EngineAudioController.Classify(train.Speed,
                    Math.Min(limit, work.MaximumSpeed), train.Running && !command.Hold);
                if (free < .05 && Math.Abs(train.Speed) < .1)
                {
                    train.Speed = 0;
                    parked.MotorState = TrainMotorState.Stopped;
                    if (train.Running && !command.Hold && (clearance < .05 || reverseAtSignal) && command.StopDistance > .05)
                        train.TravelDirection = -direction;
                    continue;
                }
                double distance = Math.Min(next.Distance, free);
                var position = route.Sample(parked.Position, direction * distance).Position;
                VehiclePose[] poses;
                using (FrameProbe.Measure("poses")) poses = Placement.Poses(train, position, route.Sample);
                bool overlaps;
                using (FrameProbe.Measure("collision.validate")) overlaps = Occupied(train, poses, Parked);
                if (overlaps)
                {
                    train.Speed = 0;
                    parked.MotorState = TrainMotorState.Stopped;
                    continue;
                }
                parked.VisualDirty = true;
                parked.RunningGearDistance += direction * distance;
                parked.Position = position;
                if (parked.Journey is { } journey) journey.Distance -= distance;
                if (parked.Wandering is { Station: not 0, Servicing: false } wandering) wandering.Distance -= distance;
                parked.Poses = poses;
                using (FrameProbe.Measure("collision.update")) _collisions.Update(train, poses);
                parked.Effort = next.Effort;
                parked.MotorState = motorState;
                parked.Supported = true;
                train.Speed = next.Distance > free && free < .1 ? 0 : next.Speed * direction;
                if (parked.StationAudio.Depart(train.Speed)) StationHorn(parked, train, departure: true);
                route.Trim(position, length + 3000);
            }
            catch (RouteBlocked)
            {
                // A broken future itinerary is different from missing supporting track.
                train.Speed = 0;
                parked.MotorState = TrainMotorState.Stopped;
                if (parked.Journey is { } journey) { journey.NeedsReplan = true; journey.HoldForReplan = true; }
            }
            catch (InvalidOperationException error)
            {
                train.Speed = 0;
                parked.MotorState = TrainMotorState.Stopped;
                train.Running = false;
                parked.Supported = false;
                Warning?.Invoke("Train stopped: " + error.Message);
                MotionChanged?.Invoke();
            }
        }
    }
    private static VehiclePose BlendPose(VehiclePose previous, VehiclePose current, float weight)
    {
        if (previous.Sections.Length != current.Sections.Length) return current;
        var sections = current.Sections.Length == 0 ? Array.Empty<VehiclePose>() : new VehiclePose[current.Sections.Length];
        for (int i = 0; i < sections.Length; i++) sections[i] = BlendPose(previous.Sections[i], current.Sections[i], weight);
        return new(previous.Center.Lerp(current.Center, weight), previous.Front.Lerp(current.Front, weight), previous.Rear.Lerp(current.Rear, weight),
            previous.FrontDirection.Lerp(current.FrontDirection, weight).Normalized(), previous.RearDirection.Lerp(current.RearDirection, weight).Normalized(), sections);
    }
}
