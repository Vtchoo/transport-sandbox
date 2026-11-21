using System;

namespace TransportSandbox;

/// <summary>Four exhaust beats per driver revolution, including reverse travel.</summary>
public sealed class SteamChuffClock
{
    private double? _previous;
    public int Advance(double turns)
    {
        if (!double.IsFinite(turns)) { _previous = null; return 0; }
        double? previous = _previous;
        _previous = turns;
        if (previous == null || Math.Abs(turns - previous.Value) > 4) return 0;
        double beats = Math.Abs(Math.Floor(turns * 4 + 1e-8) - Math.Floor(previous.Value * 4 + 1e-8));
        // Drop stale beats after a hitch rather than emitting an unbounded burst.
        return (int)Math.Min(4, beats);
    }
}
