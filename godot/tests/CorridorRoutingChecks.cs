using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class CorridorRoutingChecks
{
    public static RailRoutingEdge[] Subdivide(RailRoutingEdge[] original, int parts, double scale = 1)
    {
        int node = original.Max(e => Math.Max(e.A, e.B)), id = 0;
        var output = new List<RailRoutingEdge>();
        foreach (var edge in original)
        {
            int from = edge.A;
            for (int part = 0; part < parts; part++)
            {
                int to = part == parts - 1 ? edge.B : ++node;
                var start = (edge.P0 + (edge.P3 - edge.P0) * ((double)part / parts)) * scale;
                var end = (edge.P0 + (edge.P3 - edge.P0) * ((double)(part + 1) / parts)) * scale;
                var delta = end - start;
                output.Add(new(++id, from, to, edge.Gauge, part == 0 ? edge.SideA : 1, part == parts - 1 ? edge.SideB : -1,
                    edge.Station, start, start + delta * (1.0 / 3), start + delta * (2.0 / 3), end, delta.Length));
                from = to;
            }
        }
        return output.ToArray();
    }
    private static (int Reversals, double Cost) Metric(RailPath path, Dictionary<int, RailRoutingEdge> edges, RailDestination goal, RailRoutingPolicy policy)
    {
        int reversals = 0; double cost = path.Distance;
        for (int i = 1; i < path.States.Length; i++)
        {
            int previous = path.States[i - 1], state = path.States[i];
            if (state == (previous ^ 1)) reversals++;
            var from = edges[previous / 2]; var to = edges[state / 2];
            if (to.Station != 0 && to.Station != from.Station && !policy.Allows(to.Station)
                && (goal.Kind != TrainOrderKind.Station || goal.Target != to.Station)) cost += RailRoutingPolicy.UnorderedStationPenalty;
        }
        return (reversals, cost);
    }
    public static void Run(Action<bool, string> check)
    {
        var original = RailPathfindingChecks.Grid(3, 3).Select(e => e.Id is 2 or 6 ? e with { Station = 42 } : e).ToArray();
        var input = Subdivide(original, 7); var graph = RailRoutingGraph.Build(input); var edges = input.ToDictionary(e => e.Id);
        check(graph.CorridorStateCount > graph.StateCount / 2, "Split track forms compressible corridors between graph decisions");
        var destinations = Enumerable.Range(1, 9).Select(id => new RailDestination(TrainOrderKind.Waypoint, id))
            .Append(new(TrainOrderKind.Waypoint, input[3].B)).Append(new(TrainOrderKind.Station, 42)).ToArray();
        int cases = 0, baselinePops = 0, compressedPops = 0;
        foreach (var policy in new[] { RailRoutingPolicy.Empty, RailRoutingPolicy.Create(new[] { 42 }) })
            foreach (var goal in destinations)
            {
                var baseline = graph.Search(goal, policy: policy, compressCorridors: false);
                var compressed = graph.Search(goal, policy: policy);
                baselinePops += baseline.HeapPops; compressedPops += compressed.HeapPops;
                foreach (var edge in input)
                    foreach (double u in new[] { .05, .5, .95 })
                        foreach (int direction in new[] { -1, 1 })
                        {
                            var start = new TrainPosition(edge.Id, u);
                            var a = baseline.Plan(start, direction); var b = compressed.Plan(start, direction);
                            check((a == null) == (b == null), "Compressed corridors retain reachability for starts and targets inside a corridor");
                            if (a != null && b != null)
                            {
                                var expected = Metric(a, edges, goal, policy); var actual = Metric(b, edges, goal, policy);
                                check(expected.Reversals == actual.Reversals && Math.Abs(expected.Cost - actual.Cost) < 1e-6,
                                    "Compressed paths preserve reversal priority, partial distances and station penalties");
                            }
                            cases++;
                        }
            }
        check(compressedPops < baselinePops / 2, "Corridor searches avoid most intermediate priority-queue pops");
        var ring = new[]
        {
            new RailRoutingEdge(1, 1, 2, 1435, 1, -1, 0, default, default, default, default, 100),
            new RailRoutingEdge(2, 2, 3, 1435, 1, -1, 0, default, default, default, default, 100),
            new RailRoutingEdge(3, 3, 4, 1435, 1, -1, 0, default, default, default, default, 100),
            new RailRoutingEdge(4, 4, 1, 1435, 1, -1, 0, default, default, default, default, 100)
        };
        var cycle = RailRoutingGraph.Build(ring);
        foreach (int direction in new[] { -1, 1 })
        {
            var field = cycle.Search(new(TrainOrderKind.Waypoint, 3));
            var baseline = cycle.Search(new(TrainOrderKind.Waypoint, 3), compressCorridors: false);
            check(Math.Abs(field.Plan(new(4, .2), direction)!.Distance - baseline.Plan(new(4, .2), direction)!.Distance) < 1e-8,
                "A closed corridor loop terminates at an interior waypoint without losing either travel direction");
        }
        Godot.GD.Print($"Corridor parity: {cases} cases; priority-queue pops {baselinePops} -> {compressedPops}");
    }
    public static async Task Benchmark(Action<bool, string> check)
    {
        var input = Subdivide(RailPathfindingChecks.Grid(75, 75), 9, 9);
        var build = Stopwatch.StartNew();
        var graph = await Task.Run(() => RailRoutingGraph.Build(input));
        double buildMs = build.Elapsed.TotalMilliseconds;
        var goals = Enumerable.Range(0, 16).Select(i => new RailDestination(TrainOrderKind.Waypoint, 75 * 75 - i * 313)).ToArray();
        var samples = new List<object>();
        // Alternate order to reduce systematic warmup bias; compare the same graph and destinations.
        foreach (int run in Enumerable.Range(0, 4))
            foreach (bool compressed in run % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                var fields = new RailRoutingGraph.RailDestinationField[goals.Length];
                var timer = Stopwatch.StartNew();
                await Task.Run(() => System.Threading.Tasks.Parallel.For(0, goals.Length,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 },
                    i => fields[i] = graph.Search(goals[i], compressCorridors: compressed)));
                double searches = timer.Elapsed.TotalMilliseconds;
                var routeTimes = new double[500]; timer.Restart();
                for (int i = 0; i < 500; i++)
                {
                    var extraction = Stopwatch.StartNew();
                    var path = fields[i % fields.Length].Plan(new(i * 197 % input.Length + 1, .5), i % 2 == 0 ? 1 : -1);
                    check(path != null, "Corridor benchmark trains obtain full expanded paths");
                    routeTimes[i] = extraction.Elapsed.TotalMilliseconds;
                }
                double warm = timer.Elapsed.TotalMilliseconds; Array.Sort(routeTimes);
                var sample = new { run, compressed, searchesMs = searches, warm500Ms = warm, warmP99Ms = routeTimes[494], warmMaxMs = routeTimes[^1],
                    heapPops = fields.Sum(f => f.HeapPops), relaxed = fields.Sum(f => f.RelaxedStates), cacheMiB = fields.Sum(f => f.Bytes) / 1048576.0 };
                samples.Add(sample);
                Godot.GD.Print(System.Text.Json.JsonSerializer.Serialize(sample));
            }
        string report = System.Text.Json.JsonSerializer.Serialize(new
        {
            cpu = Godot.OS.GetProcessorName(), logicalCpus = Environment.ProcessorCount, runtime = Environment.Version.ToString(),
            segments = input.Length, states = graph.StateCount, corridorStates = graph.CorridorStateCount, graphBuildMs = buildMs,
            workers = 2, trains = 500, destinations = goals.Length, samples,
            scope = "Routing-only A/B on identical managed graph. Graph lengths supplied analytically. Run 0 is warmup; excludes scene updates, physics, route installation and rendering."
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        using var file = Godot.FileAccess.Open("res://.godot/corridor-routing-performance.json", Godot.FileAccess.ModeFlags.Write); file.StoreString(report);
        Godot.GD.Print($"Corridor benchmark: {input.Length} segments, {graph.CorridorStateCount}/{graph.StateCount} corridor states; {Godot.OS.GetProcessorName()}");
    }
}
