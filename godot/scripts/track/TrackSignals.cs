using System;
using System.Collections.Generic;
using System.Linq;

namespace TransportSandbox;

public enum RailSignalKind { Normal, Chain, Path }
public enum RailSignalAspect { Red, Green, Blue }
public readonly record struct SignalKey(int Node, int Side);
public sealed class TrackSignal(SignalKey key, RailSignalKind kind)
{
    public SignalKey Key { get; } = key;
    public RailSignalKind Kind = kind;
    public RailSignalAspect Aspect = RailSignalAspect.Red;
}

public sealed partial class TrackNetwork
{
    public readonly Dictionary<SignalKey, TrackSignal> Signals = new();
    public const float StandardParallelSpacing = 5;
    public static float DefaultParallelSpacing(int gauge) => StandardParallelSpacing + (gauge - 1435) / 1000f;
    public static float SignalOffset(int gauge) => Math.Max(1.5f, 2 + (gauge - 1435) / 2000f);
    // Driver's right is defined from actual travel, independently of mesh-frame conventions.
    public static Godot.Vector3 SignalRight(Godot.Vector3 direction)
    {
        var right = direction.Cross(Godot.Vector3.Up);
        return right.LengthSquared() > .000001f ? right.Normalized() : Godot.Vector3.Right;
    }
    public int SignalMask(int node)
    {
        int mask = (Signals.ContainsKey(new(node, -1)) ? 1 : 0) | (Signals.ContainsKey(new(node, 1)) ? 2 : 0);
        return mask == 0 ? 3 : mask;
    }
    public bool SignalAllows(int node, int side) => (SignalMask(node) & (side > 0 ? 2 : 1)) != 0;
    public bool SetSignal(int node, int side, RailSignalKind kind)
    {
        if (!Nodes.ContainsKey(node) || side is not (-1 or 1) || Incident(node).Count == 0) return false;
        var key = new SignalKey(node, side);
        if (Signals.TryGetValue(key, out var existing) && existing.Kind == kind) return false;
        Signals[key] = new(key, kind); NotifyChanged(); return true;
    }
    public bool RemoveSignal(int node, int side)
    {
        if (!Signals.Remove(new(node, side))) return false;
        NotifyChanged(); return true;
    }
}
