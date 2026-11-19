# Rail pathfinding benchmark - 2026-11-18

Measured on AMD Ryzen 7 7700 (8 cores / 16 logical CPUs), Windows, Godot 4.7.2 .NET, runtime .NET 10.0.13. Normal development Debug assembly, two routing workers. One measured run after correctness checks; timings are observations, not performance guarantees.

The workload is a synthetic 225 x 225 node grid with 100,800 track segments / 201,600 directed states. Segments are 100 m long, with signed connections and dead-end-only reversal. The initial worker build computes the actual 512-step cubic arc lengths, including the straight segments; lengths are not supplied analytically. Requests start partway along a segment and alternate travel direction in the shared-destination batch.

| Workload | Result |
| --- | ---: |
| Initial graph construction, including exact arc lengths | 1,402.8 ms wall / 1,385.5 ms worker |
| 500 simultaneous requests sharing 16 destinations, cold fields | 524.0 ms total |
| Cold request arrival latency p50 / p95 / p99 / max | 385.7 / 523.5 / 523.6 / 523.6 ms |
| Largest scheduler Pump call during shared batch | 0.936 ms |
| Same 500 requests with cached destination fields | 3.57 ms total |
| Warm per-route extraction p99 / max | 0.017 / 0.026 ms |
| Caller allocations for 500 warm paths | 3,302.7 KiB |
| Shared destination field arrays | 49.2 MiB |
| 500 simultaneous requests to 500 distinct destinations | 11,768.4 ms total; all 500 completed |
| Distinct destination arrival latency p99 | 11,643.0 ms |
| Field arrays after distinct-destination churn | 61.5 MiB |
| Graph rebuild after a station assignment edit | 203.9 ms wall / 153.4 ms worker |
| Splines reintegrated after that edit | 0 |

The shared batch performs 16 reverse searches, not 500. The distinct batch performs 500 searches and exceeds both the 128-entry queue limit and the field cache's capacity; callers retry until admitted. No request is discarded permanently. Destination arrays are retained with a 64-entry / 64 MiB LRU budget (a single oversized field is permitted on exceptionally large graphs). Graph storage, worker scratch arrays and in-flight results are additional memory.

Each destination search ranks paths by reversal count first, then arc distance. An available forward route always wins over a shorter reversing route. Gauge and signed-node restrictions are encoded in adjacency. Every reversal transition is at a directed dead end.

The later station-avoidance rule adds 10,000 equivalent metres when entering a station absent from a train's complete orders list. Cache keys now include the canonical station order set as well as destination. Trains with different permitted stations may need separate fields even when their immediate destination matches, reducing sharing. The grid above has no platforms, so it measures no station-avoidance workload; its recorded timings predate that rule. Routing costs are kept separate from real path distances used by train braking and arrival.

These numbers measure routing only. They do **not** establish the FPS or simulation cost of 500 moving/rendered trains. They exclude Godot scene updates, train collisions, vehicle poses, route installation, GPU work and the main-thread snapshot copy. The shared batch's scheduler measurement is not a full-frame timing. Snapshot copying is spread over frames with a soft 2 ms budget after capturing/allocating the edge-reference/input arrays; those initial allocations are not time-budgeted. A cold network must finish graph construction before destination searches begin.

The 500-unique-destination cold burst is the clear remaining limitation: bounded scheduling avoids launching hundreds of tasks, but the final requests wait seconds. Normal trains keep their installed journeys and do not search every frame. Large bursts of unrelated destinations could benefit from contracted corridors, regional hierarchy or a start-specific search fallback. Network edits currently rebuild adjacency and invalidate all destination fields, while reusing unchanged curve lengths.

To reproduce from the repository root:

```powershell
dotnet build "godot/Transport Sandbox.csproj" --no-restore
& "D:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path godot tests/runner.tscn --log-file .godot/routing-performance.log -- --suite=routing-performance
```

Run `--suite=routing` for correctness, queue/cache bounds, shared requests, invalidation and length reuse. `--suite=orders` also exercises actual train movement and the UI. Parity covers 480 partial-edge/direction/destination combinations, plus a test where a roughly 3 km forward detour must beat a roughly 120 m reversal and a fallback case where only reversal works.
