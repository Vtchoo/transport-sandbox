using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace TransportSandbox;

/// <summary>Opt-in worker-safe timings. Kept separate from the main-thread frame probe.</summary>
internal static class EarthworkProbe
{
    internal sealed record Sample(string Stage, int X, int Z, double Milliseconds, int Thread, long StartedTicks);
    public static volatile bool Enabled;
    public static readonly ConcurrentQueue<Sample> Samples = new();
    public static Scope Measure(string stage, int x = 0, int z = 0) => new(stage, x, z);
    internal readonly struct Scope : IDisposable
    {
        private readonly string _stage;
        private readonly int _x, _z;
        private readonly long _start;
        public Scope(string stage, int x, int z) { _stage = stage; _x = x; _z = z; _start = Enabled ? Stopwatch.GetTimestamp() : 0; }
        public void Dispose()
        {
            if (_start != 0) Samples.Enqueue(new(_stage, _x, _z, Stopwatch.GetElapsedTime(_start).TotalMilliseconds, System.Environment.CurrentManagedThreadId, _start));
        }
    }
}
