using Godot;
using System;

namespace TransportSandbox;

/// <summary>Absolute double coordinates for game data; small camera-relative coordinates for Godot.</summary>
public sealed class WorldSpace
{
    public TrackVector Origin
    {
        get; private set;
    }
    public event Action<TrackVector>? Rebased;
    public TrackVector ToWorld(Vector3 local) => Origin + TrackVector.From(local);
    public Vector3 ToLocal(TrackVector world) => (world - Origin).Float();
    public void Rebase(TrackVector eye)
    {
        using var probe = FrameProbe.Measure("origin.rebase");
        var next = new TrackVector(Math.Floor(eye.X / 512) * 512, 0, Math.Floor(eye.Z / 512) * 512);
        if (next == Origin)
            return;
        var shift = next - Origin;
        Origin = next;
        Rebased?.Invoke(shift);
    }
}
