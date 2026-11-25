using System;
using System.Buffers;

namespace TransportSandbox;

/// <summary>
/// De Casteljau subdivision with a global length-error budget and a local parameter-map bound.
/// A flatness-only test is insufficient: even a straight cubic may have nonuniform parameter speed.
/// </summary>
internal static class TrackArcTable
{
    // The midpoint of chord/control-polygon bounds has <= 0.2 mm total length error.
    // Prefix/normalization error plus local interpolation remain below 1 mm in arc distance.
    private const double LengthGap = .0004, MapError = .0004;
    internal readonly record struct Table(double[] Parameters, double[] Lengths);

    public static Table Build(TrackVector p0, TrackVector p1, TrackVector p2, TrackVector p3)
    {
        var builder = new Builder(true);
        try
        {
            builder.Run(p0, p1, p2, p3);
            return new(builder.Parameters.AsSpan(0, builder.Count).ToArray(),
                builder.Lengths.AsSpan(0, builder.Count).ToArray());
        }
        finally { builder.Dispose(); }
    }

    public static double Measure(TrackVector p0, TrackVector p1, TrackVector p2, TrackVector p3)
    {
        var builder = new Builder(false);
        builder.Run(p0, p1, p2, p3);
        return builder.Length;
    }

    private struct Builder : IDisposable
    {
        public double[] Parameters, Lengths;
        public int Count;
        public double Length;
        private readonly bool _retain;
        public Builder(bool retain)
        {
            _retain = retain;
            Parameters = retain ? ArrayPool<double>.Shared.Rent(512) : Array.Empty<double>();
            Lengths = retain ? ArrayPool<double>.Shared.Rent(512) : Array.Empty<double>();
            Count = 1; Length = 0;
            if (retain) { Parameters[0] = 0; Lengths[0] = 0; }
        }
        public void Dispose()
        {
            if (!_retain) return;
            ArrayPool<double>.Shared.Return(Parameters);
            ArrayPool<double>.Shared.Return(Lengths);
        }
        public void Run(TrackVector p0, TrackVector p1, TrackVector p2, TrackVector p3)
        {
            static bool Finite(TrackVector p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
            if (!Finite(p0) || !Finite(p1) || !Finite(p2) || !Finite(p3))
                throw new ArgumentException("Track controls must be finite.");
            // Relative coordinates preserve accuracy far from the world origin.
            Visit(default, p1 - p0, p2 - p0, p3 - p0, 0, 1, 0);
        }
        private void Visit(TrackVector a, TrackVector b, TrackVector c, TrackVector d, double t0, double t1, int depth)
        {
            var delta = d - a;
            // Bernstein basis bounds deviation from a linear-in-t chord, including straight cubics.
            double deviationSquared = .5625 * Math.Max((b - a - delta * (1.0 / 3)).LengthSquared,
                (c - a - delta * (2.0 / 3)).LengthSquared);
            if (deviationSquared <= MapError * MapError)
            {
                double chord = delta.Length;
                double polygon = (b - a).Length + (c - b).Length + (d - c).Length;
                double gap = Math.Max(0, polygon - chord);
                if (gap <= LengthGap * (t1 - t0) && Math.Sqrt(deviationSquared) + gap <= MapError)
                {
                    Length += (chord + polygon) * .5;
                    if (_retain)
                    {
                        if (Count == Parameters.Length)
                        {
                            var parameters = ArrayPool<double>.Shared.Rent(Count * 2);
                            var lengths = ArrayPool<double>.Shared.Rent(Count * 2);
                            Parameters.CopyTo(parameters, 0); Lengths.CopyTo(lengths, 0);
                            ArrayPool<double>.Shared.Return(Parameters); ArrayPool<double>.Shared.Return(Lengths);
                            Parameters = parameters; Lengths = lengths;
                        }
                        Parameters[Count] = t1; Lengths[Count++] = Length;
                    }
                    return;
                }
            }
            // Never silently accept an interval that failed the accuracy tests.
            if (depth >= 30) throw new InvalidOperationException("Track exceeds adaptive arc-table precision limits.");
            var ab = (a + b) * .5; var bc = (b + c) * .5; var cd = (c + d) * .5;
            var abc = (ab + bc) * .5; var bcd = (bc + cd) * .5;
            var middle = (abc + bcd) * .5;
            double tm = (t0 + t1) * .5;
            Visit(a, ab, abc, middle, t0, tm, depth + 1);
            Visit(middle, bcd, cd, d, tm, t1, depth + 1);
        }
    }
}
