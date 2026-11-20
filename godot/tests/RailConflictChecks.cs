using System;
using System.Linq;
using System.Threading;

namespace TransportSandbox.Tests;

public static class RailConflictChecks
{
    public static void Run(Action<bool, string> check)
    {
        RailRoutingEdge Edge(int id, int a, int b, TrackVector start, TrackVector end) =>
            new(id, a, b, 1435, 1, -1, 0, start, start + (end - start) * (1.0 / 3),
                start + (end - start) * (2.0 / 3), end, (end - start).Length);
        var tracks = new[]
        {
            Edge(1, 1, 2, new(-10, 0, 0), new(0, 0, 0)),
            Edge(2, 2, 3, new(0, 0, 0), new(100, 0, 0)),
            Edge(3, 2, 4, new(0, 0, 0), new(50, 0, 30)),
            Edge(4, 4, 3, new(50, 0, 30), new(100, 0, 0)),
            Edge(5, 3, 5, new(100, 0, 0), new(110, 0, 0)),
            Edge(6, 10, 11, new(50, 0, -10), new(50, 0, 10))
        };
        var weighted = RailConflictCosts.Build(tracks);
        int Count(int id) => weighted.Single(e => e.Id == id).Conflicts;
        check(Count(1) == 0 && Count(5) == 0, "Through continuations do not count as junction conflicts");
        check(Count(2) == 3 && Count(3) == 1 && Count(4) == 1 && Count(6) == 1,
            "Count each conflicting segment once, including switches and unsignalled geometric crossings");
        var goal = new RailDestination(TrainOrderKind.Waypoint, 5);
        var plan = RailRoutingGraph.Build(tracks).Search(goal).Plan(new(1, .5), 1)!;
        double bypass = 13 + Math.Sqrt(3400) * 2;
        check(plan.States.Contains(7) && !plan.States.Contains(5) && Math.Abs(plan.Distance - bypass) < 1e-8,
            "A slightly longer low-conflict route wins; physical distance excludes the multiplier");
        var simple = RailRoutingGraph.Build(tracks.Take(5).Select(e => e with { Conflicts = 1 }).ToArray()).Search(goal).Plan(new(1, .5), 1)!;
        check(simple.States.Contains(5) && Math.Abs(simple.Distance - 113) < 1e-8,
            "Equal conflict counts retain the shorter route");
        var elevated = tracks.Select(e => e.Id == 6 ? e with
        { P0 = e.P0 + new TrackVector(0, 10, 0), P1 = e.P1 + new TrackVector(0, 10, 0),
          P2 = e.P2 + new TrackVector(0, 10, 0), P3 = e.P3 + new TrackVector(0, 10, 0) } : e).ToArray();
        check(RailConflictCosts.Build(elevated).Single(e => e.Id == 2).Conflicts == 2,
            "Grade-separated crossings add no conflict");
        var parallel = new[] { tracks[1], Edge(20, 20, 21, new(0, 0, 5), new(100, 0, 5)) };
        check(RailConflictCosts.Build(parallel).All(e => e.Conflicts == 0), "Parallel tracks with clearance have no conflict");
        var splitCosts = RailConflictCosts.Build(new[]
        {
            tracks[1], tracks[5], Edge(20, 3, 20, new(100, 0, 0), new(1000, 0, 0))
        });
        check(splitCosts.Single(e => e.Id == 2).Conflicts == 1 && splitCosts.Single(e => e.Id == 20).Conflicts == 0,
            "A crossing penalizes only its own segment, not the long connected continuation");
        var divided = CorridorRoutingChecks.Subdivide(new[] { tracks[1] }, 2).Concat(new[] { tracks[5] }).ToArray();
        // Move the crossing into the first half, clear of the split endpoint.
        divided[^1] = Edge(6, 10, 11, new(25, 0, -10), new(25, 0, 10));
        var dividedCosts = RailConflictCosts.Build(divided);
        check(dividedCosts.Single(e => e.Id == 1).Conflicts == 1 && dividedCosts.Single(e => e.Id == 2).Conflicts == 0,
            "After splitting, only the piece containing the crossing receives a multiplier");
        var snapshot = RailRoutingSnapshot.Build(tracks, null, CancellationToken.None);
        var removed = RailRoutingSnapshot.Build(tracks.Take(5).ToArray(), snapshot, CancellationToken.None);
        check(removed.ChangedEdges.Contains(2) && removed.ForEdge(1).Version != snapshot.ForEdge(1).Version,
            "Removing a crossing invalidates its neighboring routing component even without a connecting node");
        var signals = tracks.Select(e => e with { SignalA = 1, SignalB = 2 }).ToArray();
        check(RailConflictCosts.Build(signals).Select(e => e.Conflicts).SequenceEqual(weighted.Select(e => e.Conflicts)),
            "Conflict counts are independent of signal presence, type and direction");
        var inside = RailRoutingGraph.Build(tracks).Search(new(TrainOrderKind.Waypoint, 3)).Plan(new(2, .9), 1)!;
        check(Math.Abs(inside.Distance - 10) < 1e-8, "A direct arrival inside a weighted segment reports real distance");
    }
}
