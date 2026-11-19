using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public partial class TrainEditor
{
    private readonly Dictionary<int, (VehiclePose[] Poses, TrackVector Origin)> _unsupportedOrigins = new();
    private static VehiclePose ShiftPose(VehiclePose pose, TrackVector origin, WorldSpace space)
    {
        Vector3 Shift(Vector3 point) => space.ToLocal(origin + TrackVector.From(point));
        return pose with
        {
            Center = Shift(pose.Center),
            Front = Shift(pose.Front),
            Rear = Shift(pose.Rear),
            Sections = pose.Sections.Select(p => ShiftPose(p, origin, space)).ToArray()
        };
    }
    private void Rebased(TrackVector worldShift)
    {
        var shift = worldShift.Float();
        var oldOrigin = Network.Space.Origin - worldShift;
        foreach (var parked in Parked)
        {
            if (parked.Supported)
            {
                try
                {
                    parked.Poses = Placement.Poses(Builder.Get(parked.Id), parked.Position, parked.Route == null ? null : parked.Route.Sample);
                    _unsupportedOrigins.Remove(parked.Id);
                }
                catch (InvalidOperationException)
                {
                    parked.Supported = false;
                    var train = Builder.Get(parked.Id);
                    train.Running = false;
                    train.Speed = 0;
                }
            }
            if (!parked.Supported)
            {
                if (!_unsupportedOrigins.TryGetValue(parked.Id, out var saved))
                    _unsupportedOrigins[parked.Id] = saved = (parked.Poses, oldOrigin);
                parked.Poses = saved.Poses.Select(p => ShiftPose(p, saved.Origin, Network.Space)).ToArray();
            }
            for (int i = 0; i < parked.Poses.Length; i++)
                parked.Visuals[i].Apply(parked.Poses[i]);
        }
        foreach (var ghost in Ghosts)
        if (ghost.Pose is { } pose)
            ghost.Apply(ShiftPose(pose, oldOrigin, Network.Space));
        Smoke.Rebase(shift);
        _previewSignature = null;
        SyncInstances();
        MotionChanged?.Invoke();
    }
}
