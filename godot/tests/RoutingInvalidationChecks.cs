using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace TransportSandbox.Tests;

public static class RoutingInvalidationChecks
{
    public static async Task Benchmark(Action<bool, string> check)
    {
        const int components = 20, size = 5000;
        var edges = Enumerable.Range(0, components * size).Select(i =>
        {
            int group = i / size, local = i % size, node = group * (size + 1) + local + 1;
            var p = new TrackVector(group * 1000, 0, local * 100);
            return new RailRoutingEdge(i + 1, node, node + 1, 1435, 1, -1, 0,
                p, p + new TrackVector(0, 0, 100.0 / 3), p + new TrackVector(0, 0, 200.0 / 3), p + new TrackVector(0, 0, 100), 100);
        }).ToArray();
        async Task<double> Warm(RailPathfinder service, int revision, RailRoutingEdge[] input)
        {
            var timer = Stopwatch.StartNew(); service.SetSnapshot(revision, input);
            bool done = false;
            while (!done && timer.Elapsed.TotalSeconds < 20)
            {
                service.Pump(); done = service.Ready;
                if (service.Ready)
                    for (int i = 0; i < components; i++)
                        done &= service.TryGet(new(TrainOrderKind.Waypoint, i * (size + 1) + size + 1), out _, startEdge: i * size + 1);
                if (!done) await Task.Delay(1);
            }
            check(done, "Large-network invalidation benchmark completes");
            return timer.Elapsed.TotalMilliseconds;
        }
        using var incremental = new RailPathfinder(workers: 2);
        await Warm(incremental, 1, edges);
        var edited = edges.ToArray(); edited[^1] = edited[^1] with { Station = 1 };
        long before = incremental.Stats.Searches;
        double incrementalMs = await Warm(incremental, 2, edited);
        using var cold = new RailPathfinder(workers: 2);
        double coldMs = await Warm(cold, 2, edited);
        check(incremental.Stats.ReusedComponents == 19 && incremental.Stats.Searches - before == 1 && cold.Stats.Searches == 20,
            "A local edit on 100,000 segments reuses nineteen fields and searches only the affected component");
        var result = new
        {
            segments = edges.Length, components, workers = incremental.Workers,
            after_edit_ms = incrementalMs, full_reset_ms = coldMs,
            reused_components = incremental.Stats.ReusedComponents,
            after_edit_searches = incremental.Stats.Searches - before, full_reset_searches = cold.Stats.Searches,
            cache_bytes = incremental.Stats.CacheBytes,
            note = "One headless run; immutable input preallocated; supplied arc lengths; snapshot build and twenty field requests included; simulation and rendering excluded."
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        using var file = Godot.FileAccess.Open("res://.godot/routing-invalidation-performance.json", Godot.FileAccess.ModeFlags.Write);
        file.StoreString(json); Godot.GD.Print(json);
    }
    public static async Task Run(Action<bool, string> check)
    {
        RailRoutingEdge Edge(int id, int a, int b, double x) => new(id, a, b, 1435, 1, -1, 0,
            new(x, 0, a * 100), new(x, 0, a * 100 + 30), new(x, 0, b * 100 - 30), new(x, 0, b * 100), 100);
        var edges = new[] { Edge(1, 1, 2, 0), Edge(2, 2, 3, 0), Edge(3, 11, 12, 1000), Edge(4, 12, 13, 1000) };
        using var service = new RailPathfinder(workers: 1);
        async Task Until(Func<bool> done)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.Elapsed.TotalSeconds < 10) { service.Pump(); await Task.Delay(1); }
            check(done(), "Incremental routing scheduler completes within timeout");
        }
        var destination = new RailDestination(TrainOrderKind.Waypoint, 3);
        service.SetSnapshot(1, edges); await Until(() => service.Ready);
        await Until(() => service.TryGet(destination, out _, startEdge: 1));
        await Until(() => service.TryGet(destination, out _, startEdge: 3));
        service.TryGet(destination, out var retained, startEdge: 1);
        service.TryGet(destination, out var unreachable, startEdge: 3);
        check(retained!.Plan(new(1, .5), 1) != null && unreachable!.Plan(new(3, .5), 1) == null,
            "Each physical component has its own destination field, including unreachable results");
        long searches = service.Stats.Searches;
        edges = edges.Select(edge => edge.Id == 4 ? edge with { Station = 50 } : edge).ToArray();
        service.Invalidate(2);
        check(!service.Ready && service.Stats.Cached == 2, "Snapshot capture retains caches while suspending new requests");
        service.SetSnapshot(2, edges); await Until(() => service.Ready);
        service.TryGet(destination, out var reused, startEdge: 1);
        check(ReferenceEquals(retained, reused) && service.Stats.Searches == searches && service.Stats.Cached == 1
            && service.Stats.Components == 2 && service.Stats.ReusedComponents == 1,
            "Editing one component preserves the exact field and avoids searches in another");
        check(!service.AffectsSince(1, new HashSet<int> { 1, 2 }) && service.AffectsSince(1, new HashSet<int> { 3 }),
            "Published changes include neighbours of edited connections but exclude isolated tracks");
        // Launch a search, then edit the other component before collecting its result.
        var another = new RailDestination(TrainOrderKind.Waypoint, 2);
        service.TryGet(another, out _, startEdge: 1); service.Pump();
        searches = service.Stats.Searches;
        edges = edges.Select(edge => edge.Id == 4 ? edge with { Station = 51 } : edge).ToArray();
        service.SetSnapshot(3, edges); await Until(() => service.Ready);
        check(service.TryGet(another, out _, startEdge: 1) && service.Stats.Searches == searches,
            "In-flight work in an unchanged component survives a new snapshot");
        var bridge = Edge(5, 3, 11, 500);
        edges = edges.Append(bridge).ToArray();
        service.SetSnapshot(4, edges); await Until(() => service.Ready);
        check(service.Stats.Components == 1 && service.Stats.Cached == 0,
            "Joining components invalidates both former component fields");
        await Until(() => service.TryGet(destination, out _, startEdge: 3));
        service.TryGet(destination, out var connected, startEdge: 3);
        check(connected!.Plan(new(3, .5), 1) != null, "A previously unreachable destination becomes reachable after joining networks");
        edges = edges.Where(edge => edge.Id != 5).ToArray();
        service.SetSnapshot(5, edges); await Until(() => service.Ready);
        check(service.Stats.Components == 2 && service.Stats.ReusedComponents == 0,
            "Deleting a bridge rebuilds both resulting components");
        await Until(() => service.TryGet(destination, out _, startEdge: 1));
        service.TryGet(destination, out var beforeNoOp, startEdge: 1);
        service.SetSnapshot(6, edges.Reverse().ToArray()); await Until(() => service.Ready);
        service.TryGet(destination, out var afterNoOp, startEdge: 1);
        check(ReferenceEquals(beforeNoOp, afterNoOp) && service.Stats.ReusedComponents == 2,
            "No-op notifications and snapshot ordering changes preserve component graphs");
        // Rapid edits may cancel builds; only the latest snapshot can publish.
        service.SetSnapshot(7, edges);
        service.SetSnapshot(8, edges.Where(edge => edge.Id != 2).ToArray());
        service.SetSnapshot(9, edges);
        await Until(() => service.Ready);
        check(service.PublishedRevision == 9 && service.Stats.ActiveWorkers <= service.Workers,
            "Rapid consecutive edits publish only the latest revision within the worker cap");
        check(service.AffectsSince(-100, new HashSet<int> { 1 }), "Unknown edit history conservatively requires replanning");
        service.TryGet(destination, out var beforeGeometry, startEdge: 1);
        edges = edges.Select(edge => edge.Id == 1 ? edge with { P1 = edge.P1 + new TrackVector(20, 0, 0), KnownLength = -1 } : edge).ToArray();
        service.SetSnapshot(10, edges); await Until(() => service.Ready);
        check(service.Stats.IntegratedEdges == 1 && service.Stats.ReusedComponents == 1,
            "A handle edit integrates only its changed spline and reuses the other component");
        await Until(() => service.TryGet(destination, out _, startEdge: 1));
        service.TryGet(destination, out var afterGeometry, startEdge: 1);
        check(!ReferenceEquals(beforeGeometry, afterGeometry), "Changed geometry cannot return an old destination field");
        edges = edges.Select(edge => edge.Id == 2 ? edge with { Gauge = 914 } : edge).ToArray();
        service.SetSnapshot(11, edges); await Until(() => service.Ready);
        await Until(() => service.TryGet(destination, out _, startEdge: 1));
        service.TryGet(destination, out var wrongGauge, startEdge: 1);
        check(service.Stats.Components == 3 && wrongGauge!.Plan(new(1, .5), 1) == null,
            "A gauge edit splits connectivity and invalidates incompatible routes");
        Godot.GD.Print("Component invalidation: cache retention, in-flight retention, merge/split, no-op and rapid-edit checks passed");
    }
}
