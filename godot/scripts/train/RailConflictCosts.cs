using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TransportSandbox;

/// <summary>Static physical conflicts, independent of signals, occupancy and scene objects.</summary>
public static class RailConflictCosts
{
    private const double Cell = 128, Clearance = 3.5;
    public static RailRoutingEdge[] Build(RailRoutingEdge[] source, CancellationToken token = default)
    {
        var incident = new Dictionary<(int Node, int Gauge), List<int>>();
        for (int i = 0; i < source.Length; i++)
            foreach (int node in new[] { source[i].A, source[i].B })
            {
                var key = (node, source[i].Gauge);
                if (!incident.TryGetValue(key, out var list)) incident[key] = list = new();
                list.Add(i);
            }
        int Side(int i, int node) => source[i].A == node ? source[i].SideA : source[i].SideB;
        var conflicts = new Dictionary<int, HashSet<int>>();
        void Conflict(int a, int b)
        {
            if (a == b) return;
            if (!conflicts.TryGetValue(a, out var aa)) conflicts[a] = aa = new();
            if (!conflicts.TryGetValue(b, out var bb)) conflicts[b] = bb = new();
            aa.Add(b); bb.Add(a);
        }
        // Competing branches share a switch. Opposite halves are ordinary through continuations.
        foreach (var (key, list) in incident)
            for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                {
                    if (Side(list[a], key.Node) == Side(list[b], key.Node)) Conflict(list[a], list[b]);
                }

        var cells = new Dictionary<(long X, long Z), List<int>>();
        var wide = new List<int>();
        var curves = new Dictionary<int, TrackCurve>();
        var boxes = source.Select(e => (
            Min: new TrackVector(Math.Min(Math.Min(e.P0.X, e.P1.X), Math.Min(e.P2.X, e.P3.X)),
                Math.Min(Math.Min(e.P0.Y, e.P1.Y), Math.Min(e.P2.Y, e.P3.Y)), Math.Min(Math.Min(e.P0.Z, e.P1.Z), Math.Min(e.P2.Z, e.P3.Z))),
            Max: new TrackVector(Math.Max(Math.Max(e.P0.X, e.P1.X), Math.Max(e.P2.X, e.P3.X)),
                Math.Max(Math.Max(e.P0.Y, e.P1.Y), Math.Max(e.P2.Y, e.P3.Y)), Math.Max(Math.Max(e.P0.Z, e.P1.Z), Math.Max(e.P2.Z, e.P3.Z))))).ToArray();
        TrackCurve Curve(int i)
        {
            if (!curves.TryGetValue(i, out var curve))
            { var e = source[i]; curves[i] = curve = new(new[] { e.P0, e.P1, e.P2, e.P3 }); }
            return curve;
        }
        void Test(int i, int j)
        {
            if (conflicts.TryGetValue(i, out var known) && known.Contains(j)) return;
            var a = source[i]; var b = source[j];
            if (a.A == b.A || a.A == b.B || a.B == b.A || a.B == b.B) return;
            var ba = boxes[i]; var bb = boxes[j];
            double width = Math.Max(Clearance, (a.Gauge + b.Gauge) / 2000.0 + 2);
            if (ba.Min.X > bb.Max.X + width || bb.Min.X > ba.Max.X + width
                || ba.Min.Z > bb.Max.Z + width || bb.Min.Z > ba.Max.Z + width
                || ba.Min.Y > bb.Max.Y + 4.5 || bb.Min.Y > ba.Max.Y + 4.5) return;
            if (RailInterlocking.GeometricConflict(Curve(i), Curve(j), width)) Conflict(i, j);
        }
        // Broad phase checks nearby pieces only; very large boxes use a bounded-memory fallback.
        double margin = Math.Max(Clearance, source.Length == 0 ? 0 : source.Max(e => e.Gauge) / 1000.0 + 2);
        for (int i = 0; i < source.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var e = source[i]; if (e.P0 == e.P1 && e.P0 == e.P2 && e.P0 == e.P3) continue;
            var box = boxes[i];
            long x0 = (long)Math.Floor((box.Min.X - margin) / Cell), x1 = (long)Math.Floor((box.Max.X + margin) / Cell);
            long z0 = (long)Math.Floor((box.Min.Z - margin) / Cell), z1 = (long)Math.Floor((box.Max.Z + margin) / Cell);
            if ((double)(x1 - x0 + 1) * (z1 - z0 + 1) > 4096)
            { for (int j = 0; j < i; j++) { token.ThrowIfCancellationRequested(); Test(i, j); } wide.Add(i); continue; }
            var seen = new HashSet<int>();
            foreach (int j in wide) Test(i, j);
            for (long x = x0; x <= x1; x++)
                for (long z = z0; z <= z1; z++)
                {
                    if (!cells.TryGetValue((x, z), out var list)) cells[(x, z)] = list = new();
                    foreach (int j in list) if (seen.Add(j)) Test(i, j);
                    list.Add(i);
                }
        }
        return source.Select((e, i) => e with { Conflicts = conflicts.GetValueOrDefault(i)?.Count ?? 0 }).ToArray();
    }
    internal static bool SameGeometry(RailRoutingEdge a, RailRoutingEdge b) =>
        a.Id == b.Id && a.A == b.A && a.B == b.B && a.Gauge == b.Gauge && a.SideA == b.SideA && a.SideB == b.SideB
        && a.P0 == b.P0 && a.P1 == b.P1 && a.P2 == b.P2 && a.P3 == b.P3;
}
