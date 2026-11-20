using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace TransportSandbox.Tests;

public static class RailPathfindingChecks
{
    public static RailRoutingEdge[] Capture(TrackNetwork network) => network.Edges.Values.Select(edge =>
    {
        var p = network.WorldControls(edge);
            return new RailRoutingEdge(edge.Id, edge.A, edge.B, edge.Gauge, edge.SideA, edge.SideB, edge.Station, p[0], p[1], p[2], p[3], SignalA: network.SignalMask(edge.A), SignalB: network.SignalMask(edge.B));
    }).ToArray();
    public static async Task Run(Action<bool, string> check)
    {
        CorridorRoutingChecks.Run(check);
        await RoutingInvalidationChecks.Run(check);
        RailRoutingEdge Edge(int id, int a, int b, int sideA, int sideB, double length) =>
            new(id, a, b, 1435, sideA, sideB, 0, default, default, default, default, length);
        var detour = new[]
        {
            Edge(1, 1, 2, 1, -1, 100), Edge(2, 2, 3, 1, -1, 10), Edge(3, 2, 9, -1, 1, 50),
            Edge(4, 2, 4, 1, -1, 1000), Edge(5, 4, 5, 1, -1, 1000), Edge(6, 5, 2, 1, 1, 1000)
        };
        var preferred = RailRoutingGraph.Build(detour).Search(new(TrainOrderKind.Waypoint, 9)).Plan(new(1, .5), 1)!;
        bool Reverses(RailPath path) => path.States.Zip(path.States.Skip(1)).Any(pair => pair.Second == (pair.First ^ 1));
        check(!Reverses(preferred) && preferred.Distance > 3000, "A long forward route wins over a much shorter dead-end reversal");
        var fallback = RailRoutingGraph.Build(detour.Take(3).ToArray()).Search(new(TrainOrderKind.Waypoint, 9)).Plan(new(1, .5), 1)!;
        check(Reverses(fallback) && fallback.Distance < 150, "Reversing remains available when no forward route reaches the destination");
        var penalizedForward = detour.Select(e => e.Id >= 4 ? e with { Station = 42 } : e).ToArray();
        check(!Reverses(RailRoutingGraph.Build(penalizedForward).Search(new(TrainOrderKind.Waypoint, 9)).Plan(new(1, .5), 1)!),
            "A station penalty never makes a reversing route beat an available forward route");
        var stationBranches = new[]
        {
            Edge(1, 1, 2, 1, -1, 100), Edge(2, 2, 3, 1, -1, 100) with { Station = 42 },
            Edge(3, 3, 4, 1, -1, 100) with { Station = 42 }, Edge(4, 4, 9, 1, -1, 100),
            Edge(5, 2, 5, 1, -1, 1000), Edge(6, 5, 4, 1, -1, 1000)
        };
        var stationGraph = RailRoutingGraph.Build(stationBranches);
        var finish = new RailDestination(TrainOrderKind.Waypoint, 9);
        var unordered = stationGraph.Search(finish).Plan(new(1, .5), 1)!;
        var allowed = RailRoutingPolicy.Create(new[] { 42 });
        var ordered = stationGraph.Search(finish, policy: allowed).Plan(new(1, .5), 1)!;
        check(unordered.States.Contains(RailRouter.StateId(5, 1)) && Math.Abs(unordered.Distance - 2148) < 1e-8,
            "Unordered station incurs a heavy cost and the train selects a longer through-track detour");
        check(ordered.States.Contains(RailRouter.StateId(2, 1)) && Math.Abs(ordered.Distance - 348) < 1e-8,
            "Stations elsewhere in the orders list are exempt and physical arrival distance excludes penalties");
        var unavoidable = RailRoutingGraph.Build(stationBranches.Take(4).ToArray()).Search(finish).Plan(new(1, .5), 1)!;
        check(Math.Abs(unavoidable.Distance - 348) < 1e-8, "A station penalty does not forbid the only available route or inflate braking distance");
        var longBypass = stationBranches.Select(e => e.Id >= 5 ? e with { KnownLength = 7500 } : e).ToArray();
        check(RailRoutingGraph.Build(longBypass).Search(finish).Plan(new(1, .5), 1)!.States.Contains(RailRouter.StateId(2, 1)),
            "A multi-segment station is penalized once per entry and the finite penalty permits an excessive detour to lose");
        check(stationGraph.Search(finish).Plan(new(2, .5), 1)!.States.Contains(RailRouter.StateId(3, 1)),
            "A train already inside a station can leave without repeated segment penalties");
        var stationGoal = stationGraph.Search(new(TrainOrderKind.Station, 42)).Plan(new(1, .5), 1)!;
        check(Math.Abs(stationGoal.Distance - 250) < 1e-8, "The current station destination is exempt even for direct routing API callers");
        var routeOrders = new TrainOrders(); routeOrders.Add(TrainOrderKind.Waypoint, 9); routeOrders.Add(TrainOrderKind.Station, 42);
        check(routeOrders.RoutingPolicy.Allows(42), "All station orders participate in the routing policy, including future stops");
        var oldPolicy = routeOrders.RoutingPolicy; routeOrders.Remove(routeOrders.Items[^1].Id);
        check(!routeOrders.RoutingPolicy.Allows(42) && oldPolicy.Allows(42), "Order edits refresh policy without mutating policies used by worker jobs");
        var network = new TrackNetwork();
        var nodes = Enumerable.Range(0, 7).Select(i => network.AddNode(new(i > 3 ? (i - 3) * 100 : 0, 0, i * 100))).ToArray();
        network.ConnectNodes(nodes[0], nodes[1]); network.ConnectNodes(nodes[1], nodes[2]);
        network.ConnectNodes(nodes[2], nodes[3]);
        network.ConnectNodes(nodes[2], nodes[4]); var platform = network.ConnectNodes(nodes[4], nodes[5])!;
        network.ConnectNodes(nodes[5], nodes[6]); network.ConnectNodes(nodes[6], nodes[1]);
        network.AddStation(platform, 1);
        var narrowA = network.AddNode(new(1000, 0, 0), 914); var narrowB = network.AddNode(new(1000, 0, 100), 914);
        network.ConnectNodes(narrowA, narrowB);
        using var placement = new TrainPlacement(network);
        var reference = new RailRouter(); reference.Rebuild(network, placement);
        var graph = RailRoutingGraph.Build(Capture(network));
        var edited = Capture(network); edited[0] = edited[0] with { Station = 200 };
        var stationEdit = RailRoutingGraph.Build(edited, reuse: graph);
        check(stationEdit.IntegratedEdges == 0, "Station/topology edits reuse exact lengths of unchanged splines");
        edited[0] = edited[0] with { P1 = edited[0].P1 + new TrackVector(25, 0, 0) };
        check(RailRoutingGraph.Build(edited, reuse: stationEdit).IntegratedEdges == 1, "A changed handle recomputes only that segment's arc length");
        var targets = new List<(RailDestination Destination, Dictionary<int, double> Targets)>();
        foreach (var node in network.Nodes.Values)
        {
            var destination = new RailDestination(TrainOrderKind.Waypoint, node.Id);
            var positions = new Dictionary<int, double>();
            foreach (var edge in network.Incident(node.Id))
            {
                int direction = edge.B == node.Id ? 1 : -1;
                double length = placement.Curve(edge).Length;
                double buffer = network.Continuations(edge, direction).Count == 0 ? Math.Min(2, length) / length : 0;
                positions[RailRouter.StateId(edge.Id, direction)] = direction > 0 ? 1 - buffer : buffer;
            }
            targets.Add((destination, positions));
        }
        targets.Add((new(TrainOrderKind.Station, platform.Station), new()
        {
            [RailRouter.StateId(platform.Id, 1)] = 1,
            [RailRouter.StateId(platform.Id, -1)] = 0
        }));
        int cases = 0;
        foreach (var (destination, positions) in targets)
        {
            var field = graph.Search(destination, policy: RailRoutingPolicy.Create(new[] { platform.Station }));
            foreach (var edge in network.Edges.Values)
                foreach (int direction in new[] { -1, 1 })
                    foreach (double u in new[] { .05, .5, .95 })
                    {
                        var start = new TrainPosition(edge.Id, u);
                        var expected = reference.Plan(start, direction, positions, allowReversals: false) ?? reference.Plan(start, direction, positions);
                        var actual = field.Plan(start, direction);
                        check((expected == null) == (actual == null) && (expected == null || Math.Abs(expected.Distance - actual!.Distance) < 1e-6),
                            $"Shared reverse search matches reference distance: {destination}, edge {edge.Id}, direction {direction}, u {u}");
                        if (actual != null)
                        {
                            for (int i = 1; i < actual.States.Length; i++)
                            {
                                int previous = actual.States[i - 1], next = actual.States[i];
                                var from = network.Edges[previous / 2]; int travel = RailRouter.Direction(previous);
                                var exits = network.Continuations(from, travel);
                                check(exits.Count == 0 ? next == (previous ^ 1) : exits.Any(e => e.Id == next / 2), "Path respects signed junctions, gauge and dead-end reversals");
                            }
                        }
                        cases++;
                    }
        }
        check(graph.Search(new(TrainOrderKind.Station, 99999)).Plan(new(platform.Id, .5), 1) == null, "Missing destinations produce a cached unreachable field");
        Godot.GD.Print($"Rail path parity: {cases} partial-edge/direction/destination cases");

        using var scheduler = new RailPathfinder(workers: 2, maxQueued: 4, maxCached: 2, cacheBudget: 1024 * 1024);
        scheduler.SetSnapshot(1, Capture(network));
        async Task Until(Func<bool> done, RailPathfinder? service = null)
        {
            service ??= scheduler;
            var timer = Stopwatch.StartNew();
            while (!done() && timer.Elapsed.TotalSeconds < 10) { service.Pump(); await Task.Delay(1); }
            check(done(), "Worker scheduler completes within test timeout");
        }
        await Until(() => scheduler.Ready);
        var shared = new RailDestination(TrainOrderKind.Waypoint, nodes[3].Id);
        for (int i = 0; i < 500; i++) scheduler.TryGet(shared, out _);
        await Until(() => scheduler.TryGet(shared, out _));
        check(scheduler.Stats.Searches == 1 && scheduler.Stats.ActiveWorkers <= 2, "Five hundred simultaneous requests share one worker search");
        await Until(() => scheduler.TryGet(shared, out _, policy: allowed));
        long policySearches = scheduler.Stats.Searches;
        scheduler.TryGet(shared, out _, policy: RailRoutingPolicy.Create(new[] { 42, 42 }));
        scheduler.Pump();
        check(policySearches == 2 && scheduler.Stats.Searches == 2 && scheduler.Stats.Cached == 2,
            "Cache keys separate different station order sets and share equivalent sets regardless of duplicate orders");
        using var policyScheduler = new RailPathfinder(); policyScheduler.SetSnapshot(1, stationBranches);
        await Until(() => policyScheduler.Ready, policyScheduler);
        await Until(() => policyScheduler.TryGet(finish, out _), policyScheduler);
        await Until(() => policyScheduler.TryGet(finish, out _, policy: allowed), policyScheduler);
        policyScheduler.TryGet(finish, out var bypassField);
        policyScheduler.TryGet(finish, out var directField, policy: allowed);
        check(bypassField!.Plan(new(1, .5), 1)!.States.Contains(RailRouter.StateId(5, 1))
            && directField!.Plan(new(1, .5), 1)!.States.Contains(RailRouter.StateId(2, 1)),
            "Worker requests carry each train's policy through search and cache retrieval");
        for (int i = 0; i < 500; i++) scheduler.TryGet(new(TrainOrderKind.Waypoint, i + 1000), out _);
        check(scheduler.Stats.Queued <= 4, "Burst requests cannot grow an unbounded job queue");
        scheduler.Pump();
        scheduler.Invalidate(2); scheduler.SetSnapshot(2, Array.Empty<RailRoutingEdge>());
        await Until(() => scheduler.Ready);
        await Until(() => scheduler.TryGet(shared, out _));
        scheduler.TryGet(shared, out var empty);
        check(empty!.Plan(new(platform.Id, .5), 1) == null && scheduler.Stats.Revision == 2, "Removed components discard their cached and in-flight routes");
        foreach (var destination in targets.Take(4).Select(t => t.Destination))
            await Until(() => scheduler.TryGet(destination, out _));
        check(scheduler.Stats.Cached <= 2 && scheduler.Stats.CacheBytes <= scheduler.CacheBudget, "Destination cache obeys count and memory budgets under eviction");
    }

    public static RailRoutingEdge[] Grid(int width, int height)
    {
        var edges = new List<RailRoutingEdge>(); int id = 0;
        void Add(int a, int b, TrackVector start, TrackVector end)
        {
            var d = end - start;
            edges.Add(new(++id, a, b, 1435, 1, -1, 0, start, start + d * (1.0 / 3), start + d * (2.0 / 3), end, d.Length));
        }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int node = y * width + x + 1; var p = new TrackVector(x * 100, 0, y * 100);
                if (x + 1 < width) Add(node, node + 1, p, p + new TrackVector(100, 0, 0));
                if (y + 1 < height) Add(node, node + width, p, p + new TrackVector(0, 0, 100));
            }
        return edges.ToArray();
    }
    public static async Task Benchmark(Action<bool, string> check)
    {
        var input = Grid(225, 225).Select(e => e with { KnownLength = -1 }).ToArray(); // 100,800 segments, exact 512-step lengths.
        using var scheduler = new RailPathfinder(workers: 2);
        var watch = Stopwatch.StartNew(); scheduler.SetSnapshot(1, input);
        while (!scheduler.Ready && watch.Elapsed.TotalSeconds < 30) { scheduler.Pump(); await Task.Delay(1); }
        check(scheduler.Ready, "Large graph builds within benchmark timeout");
        double graphMs = watch.Elapsed.TotalMilliseconds;
        var destinations = Enumerable.Range(0, 16).Select(i => new RailDestination(TrainOrderKind.Waypoint, 225 * 225 - i * 313)).ToArray();
        var latency = new double[500]; var complete = new bool[500]; int count = 0;
        var cold = Stopwatch.StartNew(); double maxPump = 0;
        while (count < 500 && cold.Elapsed.TotalSeconds < 30)
        {
            var pump = Stopwatch.StartNew(); scheduler.Pump(); maxPump = Math.Max(maxPump, pump.Elapsed.TotalMilliseconds);
            for (int i = 0; i < 500; i++)
            {
                if (complete[i] || !scheduler.TryGet(destinations[i % destinations.Length], out var field)) continue;
                var path = field!.Plan(new(i * 197 % input.Length + 1, .5), i % 2 == 0 ? 1 : -1);
                check(path != null, "Benchmark train finds a route across the grid");
                complete[i] = true; count++; latency[i] = cold.Elapsed.TotalMilliseconds;
            }
            if (count < 500) await Task.Delay(1);
        }
        check(count == 500 && scheduler.Stats.Searches == destinations.Length, "Five hundred trains share sixteen destination searches");
        cold.Stop();
        var warmSamples = new double[500]; long allocated = GC.GetAllocatedBytesForCurrentThread();
        var warm = Stopwatch.StartNew();
        for (int i = 0; i < 500; i++)
        {
            var timer = Stopwatch.StartNew(); scheduler.TryGet(destinations[i % destinations.Length], out var field);
            check(field!.Plan(new(i * 197 % input.Length + 1, .5), i % 2 == 0 ? 1 : -1) != null, "Warm route extraction remains valid");
            warmSamples[i] = timer.Elapsed.TotalMilliseconds;
        }
        double warmMs = warm.Elapsed.TotalMilliseconds;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(latency); Array.Sort(warmSamples);
        Godot.GD.Print($"ROUTING BENCHMARK: {Godot.OS.GetProcessorName()}; logical CPUs={Environment.ProcessorCount}; .NET={Environment.Version}");
        Godot.GD.Print($"segments={input.Length}; trains=500; destinations=16; workers={scheduler.Workers}; graph wall={graphMs:F1}ms, worker={scheduler.Stats.LastBuildMs:F1}ms");
        Godot.GD.Print($"cold batch={cold.Elapsed.TotalMilliseconds:F1}ms; arrival latency p50={latency[249]:F1}ms p95={latency[474]:F1}ms p99={latency[494]:F1}ms max={latency[^1]:F1}ms; max scheduler pump={maxPump:F3}ms");
        Godot.GD.Print($"warm 500 batch={warmMs:F2}ms; per-route p99={warmSamples[494]:F3}ms max={warmSamples[^1]:F3}ms; caller allocations={allocatedBytes / 1024.0:F1}KiB; cache={scheduler.Stats.CacheBytes / 1048576.0:F1}MiB");
        // High-churn case: all trains request different destinations, exceeding both cache and queue caps.
        var diverse = Stopwatch.StartNew(); var diverseLatency = new double[500]; Array.Fill(complete, false); count = 0;
        long searchesBefore = scheduler.Stats.Searches;
        while (count < 500 && diverse.Elapsed.TotalSeconds < 30)
        {
            scheduler.Pump();
            for (int i = 0; i < 500; i++)
            {
                if (complete[i] || !scheduler.TryGet(new(TrainOrderKind.Waypoint, i * 97 + 1), out var field)) continue;
                check(field!.Plan(new(i * 197 % input.Length + 1, .5), 1) != null, "Diverse-destination train obtains a valid route");
                complete[i] = true; count++; diverseLatency[i] = diverse.Elapsed.TotalMilliseconds;
            }
            check(scheduler.Stats.Queued <= scheduler.MaxQueued && scheduler.Stats.ActiveWorkers <= scheduler.Workers, "Large unique-destination burst stays within scheduler bounds");
            if (count < 500) await Task.Delay(1);
        }
        check(count == 500 && scheduler.Stats.Cached <= scheduler.MaxCached && scheduler.Stats.CacheBytes <= scheduler.CacheBudget,
            "Five hundred unique destinations complete despite cache eviction and queue backpressure");
        Array.Sort(diverseLatency);
        Godot.GD.Print($"unique-destination burst: completed={count}/500; searches={scheduler.Stats.Searches - searchesBefore}; wall={diverse.Elapsed.TotalMilliseconds:F1}ms; p99={diverseLatency[494]:F1}ms; cache={scheduler.Stats.CacheBytes / 1048576.0:F1}MiB");
        var editTimer = Stopwatch.StartNew(); var editedInput = input.ToArray(); editedInput[0] = editedInput[0] with { Station = 1 };
        scheduler.SetSnapshot(2, editedInput);
        while (!scheduler.Ready && editTimer.Elapsed.TotalSeconds < 10) { scheduler.Pump(); await Task.Delay(1); }
        check(scheduler.Ready && scheduler.Stats.IntegratedEdges == 0, "Large graph edits reuse every unchanged spline length");
        Godot.GD.Print($"station-edit graph rebuild={editTimer.Elapsed.TotalMilliseconds:F1}ms wall, {scheduler.Stats.LastBuildMs:F1}ms worker; integrated edges={scheduler.Stats.IntegratedEdges}");
        Godot.GD.Print("Routing-only benchmark: includes worker graph construction and exact arc-length integration; excludes scene rendering, physics, collision scans and main-thread snapshot capture.");
    }
}
