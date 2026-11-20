using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public sealed class ParkedTrain
{
    public int Id;
    public TrainRoute? Route;
    public TrainOrderJourney? Journey;
    public TrainWanderingState? Wandering;
    public double Effort;
    public TrainPosition Position;
    public bool Supported = true;
    public int SupportRevision = -1;
    public VehiclePose[] Poses = Array.Empty<VehiclePose>();
    public PosedVehicleVisual[] Visuals = Array.Empty<PosedVehicleVisual>();
}
public partial class TrainEditor : Node3D
{
    public TrackNetwork Network = null!;
    public VehicleCatalog Catalog = null!;
    public WorldCamera Camera = null!;
    public WindowManager Windows = null!;
    public readonly TrainBuilder Builder = new();
    public readonly List<ParkedTrain> Parked = new();
    public TrainPlacement Placement = null!;
    public bool Placing { get; private set; }
    public bool Valid { get; private set; }
    public bool OccupiedBlock { get; private set; }
    public Func<bool> PlacementBlockOverride = () => Input.IsPhysicalKeyPressed(Key.Shift);
    public TrainPosition? Target { get; private set; }
    public int Facing { get; private set; } = 1;
    public bool ReverseAppend { get; private set; }
    public int? SnappedStation { get; private set; }
    public string Message { get; private set; } = "";
    public event Action<string?>? Changed;
    public event Action<string>? Warning;
    public event Action<int>? TrainClicked;
    public event Action? PlacementStarted;
    public readonly List<PosedVehicleVisual> Ghosts = new();
    public (ParkedTrain Train, bool Front, TrainPosition Combined)? AppendTarget { get; private set; }
    private string _ghostKey = "";
    private record SavedParked(int Id, TrainPosition Position, bool Supported, VehiclePose[] Poses, int[] Gauges, TrainRoute? Route);
    private record Snapshot(TrainConsist[] Trains, int? Selected, SavedParked[] Parked);
    private bool _trackDirty;
    private double _trackTimer;
    private int _revision;
    private (Vector2 Mouse, Transform3D Camera, int Revision, int Facing, int? Train, bool Reverse, bool Override)? _previewSignature;
    private Vector2 _previewMouse;
    private double _previewTimer;
    public TrainInstances Instances = null!;
    public MenuSmoke Smoke = null!;
    public override void _Ready() { Placement = new(Network); Network.Changed += TrackChanged; Network.Space.Rebased += Rebased; Instances = new() { Space = Network.Space }; AddChild(Instances); Smoke = new() { Camera = Camera }; AddChild(Smoke); }
    public override void _ExitTree() { Network.Changed -= TrackChanged; Network.Space.Rebased -= Rebased; _interlocking?.Dispose(); Placement.Dispose(); _pathfinder.Dispose(); }
    private void TrackChanged() { _trackDirty = true; _revision++; }
    public int? TrackGauge(int id) => Parked.FirstOrDefault(p => p.Id == id) is { } parked && Network.Edges.TryGetValue(parked.Position.Edge, out var edge) ? edge.Gauge : null;
    public void Select(int id) { if (Builder.Selected?.Id != id) CancelPlacement(); Builder.Selected = Builder.Get(id); }
    public bool Allows(VehicleDefinition definition, TrainConsist? train)
    {
        if (train == null) return true;
        var gauges = TrainBuilder.Gauges(train.Items.Select(i => i.Definition).Append(definition));
        return TrackGauge(train.Id) is { } gauge ? gauges.Contains(gauge) : gauges.Length > 0;
    }
    private Snapshot Capture() => new(Builder.Trains.Select(t => t.Copy()).ToArray(), Builder.Selected?.Id,
        Parked.Select(p => new SavedParked(p.Id, p.Position, p.Supported, p.Poses, p.Visuals.Select(v => v.Gauge).ToArray(), p.Route?.Copy())).ToArray());
    private static void RemoveVisuals(IEnumerable<PosedVehicleVisual> visuals)
    {
        foreach (var visual in visuals) { visual.GetParent()?.RemoveChild(visual); visual.QueueFree(); }
    }
    private PosedVehicleVisual[] CreateVisuals(TrainConsist train, VehiclePose[] poses, int[] gauges)
    {
        var visuals = new List<PosedVehicleVisual>();
        try
        {
            for (int i = 0; i < train.Items.Count; i++)
            {
                var visual = new PosedVehicleVisual(train.Items[i].Definition, gauges[i]);
                visuals.Add(visual); AddChild(visual); visual.Apply(poses[i]);
            }
            return visuals.ToArray();
        }
        catch { RemoveVisuals(visuals); throw; }
    }
    private void Restore(Snapshot snapshot)
    {
        foreach (var parked in Parked) RemoveVisuals(parked.Visuals);
        Parked.Clear(); Builder.Restore(snapshot.Trains, snapshot.Selected);
        foreach (var saved in snapshot.Parked)
            Parked.Add(new() { Id = saved.Id, Position = saved.Position, Supported = saved.Supported, Route = saved.Route, Poses = saved.Poses, Visuals = CreateVisuals(Builder.Get(saved.Id), saved.Poses, saved.Gauges) });
    }
    public bool Perform(string label, Func<bool> action)
    {
        var before = Capture();
        try
        {
            if (!action()) return false;
            CancelPlacement(); SyncInstances(); Message = label + " complete."; Changed?.Invoke(label); return true;
        }
        catch (Exception error)
        {
            Restore(before); CancelPlacement(); SyncInstances(); Message = error.Message; Warning?.Invoke(Message); Changed?.Invoke(null); return false;
        }
    }
    private bool Occupied(TrainConsist train, VehiclePose[] poses, IEnumerable<ParkedTrain> others, double margin = .08)
    {
        foreach (var other in others)
        {
            var model = Builder.Get(other.Id);
            for (int i = 0; i < poses.Length; i++)
                for (int j = 0; j < other.Poses.Length; j++)
                    if (TrainPlacement.Overlap(train.Items[i].Definition, poses[i], model.Items[j].Definition, other.Poses[j], margin)) return true;
        }
        return false;
    }
    public bool Edit(string label, Func<bool> action)
    {
        bool lifted = false;
        bool result = Perform(label, () =>
        {
            var originals = Parked.Select(p => (Parked: p, Before: Builder.Get(p.Id).Copy())).ToArray();
            if (!action()) return false;
            var changed = originals.Where(p => !p.Before.Items.SequenceEqual(Builder.Get(p.Parked.Id).Items)).OrderBy(p => Builder.Get(p.Parked.Id).Length - p.Before.Length).ToArray();
            var affected = changed.Select(p => p.Parked).ToHashSet();
            var plans = new List<ParkedTrain>();
            var removed = new List<ParkedTrain>();
            foreach (var (parked, before) in changed)
            {
                var train = Builder.Get(parked.Id); TrainBuilder.RequireStopped(train);
                if (train.Items.Count == 0) { removed.Add(parked); continue; }
                try
                {
                    int survivor = before.Items.FindIndex(item => train.Items.Any(i => i.Id == item.Id));
                    double shift = survivor < 0 ? (before.Length - train.Length) / 2 : before.Offsets[survivor] - train.Offsets[train.Items.FindIndex(i => i.Id == before.Items[survivor].Id)];
                    var route = RouteFor(parked);
                    var position = route.Sample(parked.Position, shift).Position;
                    var poses = Placement.Poses(train, position, route.Sample);
                    if (Occupied(train, poses, Parked.Where(p => !affected.Contains(p)).Concat(plans))) throw new InvalidOperationException("Not enough free track: another train occupies this space.");
                    plans.Add(new() { Id = parked.Id, Position = position, Poses = poses, Route = route });
                }
                catch (InvalidOperationException error) when (error is RouteEnd || error.Message.StartsWith("Not enough") || error.Message.Contains("curve is too tight")) { removed.Add(parked); lifted = true; }
            }
            foreach (var parked in affected) { RemoveVisuals(parked.Visuals); Parked.Remove(parked); }
            foreach (var plan in plans)
            {
                var train = Builder.Get(plan.Id); int gauge = Network.Edges[plan.Position.Edge].Gauge;
                plan.Visuals = CreateVisuals(train, plan.Poses, train.Items.Select(_ => gauge).ToArray()); Parked.Add(plan);
            }
            foreach (var parked in removed) { var train = Builder.Get(parked.Id); train.Placed = train.Running = false; train.Speed = 0; }
            return true;
        });
        if (result && lifted) { Message = "Not enough free track for the edited train. It has been lifted for repositioning."; Warning?.Invoke(Message); Changed?.Invoke(null); }
        return result;
    }
    public bool AddVehicle(string id, int? target = null, int? index = null) => Edit("Add vehicle", () =>
    {
        var train = target == null ? Builder.Create() : Builder.Get(target.Value);
        var definition = Catalog.Get(id); TrainBuilder.RequireStopped(train);
        if (!Allows(definition, train)) throw new InvalidOperationException("This vehicle is incompatible with this train's track gauge.");
        Builder.Add(train, definition, index); return true;
    });
    public bool RemoveFromTrack(int id) => Perform("Remove from track", () =>
    {
        var train = Builder.Get(id); TrainBuilder.RequireStopped(train);
        var parked = Parked.FirstOrDefault(p => p.Id == id); if (parked == null) return false;
        Builder.Selected = train; RemoveVisuals(parked.Visuals); Parked.Remove(parked);
        train.Placed = train.Running = false; train.Speed = 0; train.TravelDirection = 1; return true;
    });
    public void StartPlacement(int id)
    {
        Select(id); var train = Builder.Get(id);
        if (train.Placed || train.Items.Count == 0) return;
        Placing = true; ReverseAppend = false; ClearGhosts(); PlacementStarted?.Invoke();
        Message = "Hover track to preview. R reverses; Esc cancels."; Changed?.Invoke(null);
    }
    public void ReversePreview() { if (!Placing) return; Facing = -Facing; ReverseAppend = !ReverseAppend; RefreshPreview(GetViewport().GetMousePosition()); }
    private void ClearGhosts() { RemoveVisuals(Ghosts); Ghosts.Clear(); _ghostKey = ""; Target = null; AppendTarget = null; Valid = false; OccupiedBlock = false; _previewSignature = null; }
    public void CancelPlacement() { Placing = false; ClearGhosts(); }
    public void SuspendPreview() { foreach (var ghost in Ghosts) ghost.Hide(); Valid = false; _previewSignature = null; }
    public TrainPosition? PickTrack(Vector2 mouse)
    {
        float best = 400; TrainPosition? target = null;
        foreach (var edge in Network.Edges.Values)
        {
            var curve = Placement.Curve(edge); int steps = Math.Min(512, Math.Max(24, (int)Math.Ceiling(curve.Length / 3)));
            var prev = curve.PointAt(0).Float();
            for (int i = 1; i <= steps; i++)
            {
                var next = curve.PointAt((double)i / steps).Float();
                if (!Camera.IsPositionBehind(prev) && !Camera.IsPositionBehind(next))
                {
                    var a = Camera.UnprojectPosition(prev); var b = Camera.UnprojectPosition(next); var line = b - a;
                    float f = Mathf.Clamp((mouse - a).Dot(line) / Math.Max(line.LengthSquared(), .000001f), 0, 1);
                    float d = mouse.DistanceSquaredTo(a + line * f);
                    if (d < best) { best = d; target = new(edge.Id, (i - 1.0 + f) / steps, Facing); }
                }
                prev = next;
            }
        }
        return target;
    }
    public void RefreshPreview(Vector2 mouse)
    {
        if (!Placing || Builder.Selected is not { } train) return;
        _previewMouse = mouse; OccupiedBlock = false;
        Target = PickTrack(mouse); AppendTarget = null; Valid = false; SnappedStation = null;
        string? appendError = null;
        (ParkedTrain Parked, bool Front)? nearest = null; float best = 28;
        foreach (var parked in Parked.Where(p => Builder.Get(p.Id).Stopped))
            foreach (bool front in new[] { true, false })
                try
                {
                    var end = Placement.Sample(parked.Position, (front ? 1 : -1) * Builder.Get(parked.Id).Length / 2).Point;
                    float d = Camera.UnprojectPosition(end).DistanceTo(mouse);
                    if (!Camera.IsPositionBehind(end) && d < best) { best = d; nearest = (parked, front); }
                }
                catch (InvalidOperationException) { }
        if (nearest is { } n)
            try
            {
                var positions = Placement.Append(n.Parked.Position, Builder.Get(n.Parked.Id).Length, train.Length, n.Front);
                Target = positions.Incoming with { Facing = positions.Incoming.Facing * (ReverseAppend ? -1 : 1) };
                AppendTarget = (n.Parked, n.Front, positions.Combined);
            }
            catch (InvalidOperationException error) { appendError = error.Message; }
        if (Target is { } location && AppendTarget == null && appendError == null && Placement.SnapStation(location, train.Length) is { } snap) { Target = snap.Position; SnappedStation = snap.Station; }
        if (Target is not { } target) { SuspendPreview(); Message = appendError ?? "Hover track to preview the train. R reverses facing."; return; }
        int gauge = Network.Edges[target.Edge].Gauge;
        var key = train.Id + ":" + string.Join(",", train.Items.Select(i => i.Id)) + ":" + gauge;
        if (key != _ghostKey)
        {
            RemoveVisuals(Ghosts); Ghosts.Clear(); _ghostKey = key;
            foreach (var item in train.Items)
            {
                var ghost = new PosedVehicleVisual(item.Definition, item.Definition.Models.ContainsKey(gauge) ? gauge : item.Definition.Models.Keys.Order().First(), true);
                Ghosts.Add(ghost); AddChild(ghost);
            }
        }
        foreach (var ghost in Ghosts) ghost.Show();
        try
        {
            if (appendError != null) throw new InvalidOperationException(appendError);
            var poses = Placement.Poses(train, target);
            if (AppendTarget is { } append)
            {
                var combined = Builder.Get(append.Train.Id).Copy();
                var incoming = ReverseAppend ? train.Items.AsEnumerable().Reverse().Select(i => i with { Facing = -i.Facing }) : train.Items;
                combined.Items = append.Front ? incoming.Concat(combined.Items).ToList() : combined.Items.Concat(incoming).ToList();
                Placement.Poses(combined, append.Combined);
            }
            for (int i = 0; i < poses.Length; i++) Ghosts[i].Apply(poses[i]);
            foreach (var parked in Parked)
                if (Occupied(train, poses, new[] { parked }, parked == AppendTarget?.Train ? 0 : .08)) throw new InvalidOperationException("This train overlaps a parked train.");
            var candidate = new SignalTrain(train.Id, new TrainRoute(Placement, target, 1), target, train.Length, 1);
            OccupiedBlock = Interlocking.PlacementBlockOccupied(candidate, Parked.Where(p => p != AppendTarget?.Train)
                .Select(p => new SignalTrain(p.Id, RouteFor(p).Copy(), p.Position, Builder.Get(p.Id).Length, Builder.Get(p.Id).TravelDirection)));
            if (OccupiedBlock && !PlacementBlockOverride())
                throw new InvalidOperationException("Block occupied by another train. Hold Shift to place anyway.");
            Valid = true;
            Message = OccupiedBlock ? "Warning: placing in an occupied block (Shift override)." : AppendTarget is { } a ? $"Attach to {(a.Front ? "front" : "rear")} - {Builder.Get(a.Train.Id).Name}" : SnappedStation is { } station ? $"Front snapped to Station {station} end" : $"{train.Name} - ready to place";
        }
        catch (InvalidOperationException error)
        {
            Message = error.Message.Contains("gauge") ? $"Requires {string.Join(" or ", TrainBuilder.Gauges(train.Items.Select(i => i.Definition)))} mm track. This track is {gauge} mm." : error.Message;
            var curve = Placement.Curve(Network.Edges[target.Edge]); var center = curve.PointAt(target.U).Float(); var direction = curve.FrameAt(target.U).Forward.Float() * target.Facing; var offsets = train.Offsets;
            for (int i = 0; i < Ghosts.Count; i++) Ghosts[i].Apply(PosedVehicleVisual.Straight(train.Items[i].Definition, center + direction * (float)offsets[i], direction, train.Items[i].Facing));
        }
        foreach (var ghost in Ghosts) ghost.Tint(Valid);
    }
    public bool CommitPreview()
    {
        if (!Placing) return false;
        // Occupancy and Shift may have changed since the last rendered preview.
        RefreshPreview(_previewMouse);
        if (!Valid || Target is not { } target || Builder.Selected is not { } train)
        { if (OccupiedBlock) Warning?.Invoke(Message); return false; }
        bool overridden = OccupiedBlock;
        var append = AppendTarget;
        bool placed = Perform(append == null ? "Place train" : "Attach trains", () =>
        {
            TrainConsist result = train; TrainPosition position = target;
            if (append is { } a)
            {
                result = Builder.Get(a.Train.Id); Builder.Merge(result, train, a.Front, ReverseAppend); position = a.Combined;
                RemoveVisuals(a.Train.Visuals); Parked.Remove(a.Train);
            }
            var poses = Placement.Poses(result, position); int gauge = Network.Edges[position.Edge].Gauge;
            var visuals = CreateVisuals(result, poses, result.Items.Select(_ => gauge).ToArray());
            result.Placed = true; Parked.Add(new() { Id = result.Id, Position = position, Poses = poses, Visuals = visuals }); return true;
        });
        if (placed && overridden) Warning?.Invoke("Warning: train placed in a block occupied by another train.");
        return placed;
    }
    public void Revalidate()
    {
        foreach (var parked in Parked)
            try
            {
                var poses = Placement.Poses(Builder.Get(parked.Id), parked.Position, parked.Route == null ? null : parked.Route.Sample);
                for (int i = 0; i < poses.Length; i++) parked.Visuals[i].Apply(poses[i]);
                parked.Poses = poses; parked.Supported = true;
            }
            catch (InvalidOperationException) { parked.Supported = false; var train = Builder.Get(parked.Id); train.Running = false; train.Speed = 0; }
        SyncInstances(); Changed?.Invoke(null);
    }
    private void SyncInstances() => Instances.Sync(Parked.SelectMany(p => p.Visuals));
    public override void _Process(double delta)
    {
        AdvanceMovement(delta);
        Smoke.Outlets.Clear(); Smoke.Outlets.AddRange(Parked.SelectMany(p => p.Visuals.SelectMany(v => v.Exhausts()).Select(o => o with { Effort = p.Effort })));
        _trackTimer += delta;
        if (_trackDirty && _trackTimer >= .25) { _trackDirty = false; _trackTimer = 0; Revalidate(); }
        if (Placing)
        {
            if (Windows.HasModal || GetViewport().GuiGetHoveredControl() != null) SuspendPreview();
            else
            {
                _previewTimer += delta;
                var signature = (GetViewport().GetMousePosition(), Camera.GlobalTransform, _revision, Facing, Builder.Selected?.Id, ReverseAppend, PlacementBlockOverride());
                if (_previewSignature != signature || _previewTimer >= .1) { RefreshPreview(signature.Item1); _previewSignature = signature; _previewTimer = 0; }
            }
        }
    }
    public int? PickTrain(Vector2 mouse)
    {
        var origin = Camera.ProjectRayOrigin(mouse); var direction = Camera.ProjectRayNormal(mouse);
        float best = float.PositiveInfinity; int? id = null;
        foreach (var parked in Parked)
            foreach (var visual in parked.Visuals)
                foreach (var mesh in Meshes(visual))
                {
                    var inverse = mesh.GlobalTransform.AffineInverse(); var local = inverse * origin; var ray = inverse.Basis * direction;
                    if (!mesh.GetAabb().IntersectsSegment(local, local + ray * Camera.Far)) continue;
                    var faces = mesh.Mesh.GetFaces();
                    for (int i = 0; i < faces.Length; i += 3)
                    {
                        var hit = Geometry3D.RayIntersectsTriangle(local, ray, faces[i], faces[i + 1], faces[i + 2]);
                        if (hit.VariantType != Variant.Type.Vector3) continue;
                        float distance = origin.DistanceSquaredTo(mesh.GlobalTransform * hit.AsVector3());
                        if (distance < best) { best = distance; id = parked.Id; }
                    }
                }
        return id;
    }
    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if (node is MeshInstance3D mesh) yield return mesh;
        foreach (var child in node.GetChildren()) foreach (var nested in Meshes(child)) yield return nested;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Windows.HasModal) return;
        if (Placing && input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.R) ReversePreview();
            else if (key.Keycode == Key.Escape) { CancelPlacement(); Changed?.Invoke(null); }
            else return;
            GetViewport().SetInputAsHandled();
        }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse) return;
        if (Placing) { RefreshPreview(mouse.Position); CommitPreview(); GetViewport().SetInputAsHandled(); }
        else if (PickTrain(mouse.Position) is { } id) { TrainClicked?.Invoke(id); GetViewport().SetInputAsHandled(); }
    }
}
