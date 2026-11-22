using System;

namespace TransportSandbox.Tests;

public static class CrossingOptimizationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var random = new Random(417);
        double R(double size) => (random.NextDouble() - .5) * size;
        bool same = true;
        for (int test = 0; test < 160; test++)
        {
            double y = test % 4 == 0 ? R(20) : 0, x = R(30);
            var a = new TrackCurve(new TrackVector[] { new(-30, 0, -100), new(R(80), R(10), -30), new(R(80), R(10), 30), new(30, 0, 100) });
            var b = new TrackCurve(new TrackVector[] { new(x - 30, y, -100), new(x + R(80), y, -30), new(x + R(80), y, 30), new(x + 30, y, 100) });
            double width = test % 2 == 0 ? 3.5 : 5;
            same &= RailInterlocking.GeometricConflict(a, b, width) == Original(a, b, width);
        }
        check(same, "Pruned crossing tests match the original chord-pair calculation across 160 curves, parallel lanes and height offsets");
    }
    private static bool Original(TrackCurve a, TrackCurve b, double width)
    {
        int na = Math.Clamp((int)Math.Ceiling(a.Length / 4), 1, 4096), nb = Math.Clamp((int)Math.Ceiling(b.Length / 4), 1, 4096);
        TrackVector Envelope(TrackVector p) => new(p.X, p.Y * width / 4.5, p.Z);
        var p = a.PointAt(0);
        for (int i = 1; i <= na; i++)
        {
            var q = a.PointAt(i / (double)na); var r = b.PointAt(0);
            for (int j = 1; j <= nb; j++)
            {
                var s = b.PointAt(j / (double)nb);
                if (Distance(Envelope(p), Envelope(q), Envelope(r), Envelope(s)) < width) return true;
                r = s;
            }
            p = q;
        }
        return false;
    }
    private static double Distance(TrackVector p, TrackVector q, TrackVector r, TrackVector s)
    {
        double Dot(TrackVector a, TrackVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        var u = q - p; var v = s - r; var w = p - r;
        double a = Dot(u, u), b = Dot(u, v), c = Dot(v, v), d = Dot(u, w), e = Dot(v, w), determinant = a * c - b * b;
        double x = determinant > 1e-10 ? Math.Clamp((b * e - c * d) / determinant, 0, 1) : 0;
        double y = c > 1e-10 ? Math.Clamp((b * x + e) / c, 0, 1) : 0;
        x = a > 1e-10 ? Math.Clamp((b * y - d) / a, 0, 1) : 0;
        return (w + u * x - v * y).Length;
    }
}
