using System.Collections.Generic;
using System.Diagnostics;

namespace TransportSandbox;

/// <summary>Opt-in CPU timings for saved-world diagnostics; disabled during normal play.</summary>
internal static class FrameProbe
{
    public static bool Enabled;
    public static readonly Dictionary<string, double> Milliseconds = new();
    public static Scope Measure(string name) => new(name);
    internal readonly struct Scope : System.IDisposable
    {
        private readonly string _name;
        private readonly long _start;
        private readonly long _allocated;
        public Scope(string name) { _name = name; _start = Enabled ? Stopwatch.GetTimestamp() : 0; _allocated = Enabled ? System.GC.GetAllocatedBytesForCurrentThread() : 0; }
        public void Dispose()
        {
            if (_start != 0)
            {
                Milliseconds[_name] = Milliseconds.GetValueOrDefault(_name) + Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
                string allocations = _name + ".allocated.bytes";
                Milliseconds[allocations] = Milliseconds.GetValueOrDefault(allocations) + System.GC.GetAllocatedBytesForCurrentThread() - _allocated;
                string calls = _name + ".calls";
                Milliseconds[calls] = Milliseconds.GetValueOrDefault(calls) + 1;
            }
        }
    }
}
