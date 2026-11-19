# Corridor routing comparison - 2026-11-19

Hardware: AMD Ryzen 7 7700, 16 logical CPUs. Windows, Godot 4.7.2 .NET, runtime .NET 10.0.13, normal development Debug assembly. Two search workers. [Raw measurements](rail-routing-corridors-2026-11-19.json) retain every sample, including warmup.

The same immutable graph is searched with corridor compression enabled and disabled. It contains 99,900 segments / 199,800 directed states: a 75 x 75 junction grid whose 900 m links are each divided into nine 100 m segments. Of those states, 156,576 have a unique predecessor and successor without a station boundary or reversal. All original segments remain available for path extraction and arbitrary waypoint positions.

Each batch searches 16 destinations on two workers, then extracts 500 paths from varied starting segments, partial positions and travel directions. Run 0 is warmup; runs 1-3 alternate execution order. Medians below use those three measured runs.

| Measurement | Without compression | With compression |
| --- | ---: | ---: |
| Search batch median | 254.68 ms | 75.22 ms |
| Search batch samples | 254.68 / 274.96 / 213.55 ms | 110.44 / 75.22 / 73.14 ms |
| Priority-queue pops, 16 fields | 3,196,800 | 691,584 |
| Relaxed states, 16 fields | 3,196,738 | 3,196,738 |
| Median extraction time for 500 cached paths | 7.43 ms | 7.14 ms |
| Destination array storage | 48.78 MiB | 48.78 MiB |

Searches are about **3.4x faster** on this corridor-heavy workload, with **78.4% fewer heap pops**. This is search compression, not removal of track geometry or dense destination arrays: intermediate states receive exact costs/continuations while a corridor is traversed directly, instead of being individually queued. Branches, merges, station boundaries, target states and dead-end reversals remain explicit heap decisions. Targets inside corridors and closed loops are supported. Gauge restrictions, fewest-reversals priority, the 10 km unordered-station penalty, and physical arrival distances remain unchanged.

The graph was built once in 120.92 ms with analytically supplied straight-segment lengths. Build time is not part of the search batches. These measurements exclude main-thread snapshot capture, scheduler admission latency, scene updates, route installation, train physics, collision scans and rendering. They do not establish FPS for 500 moving trains. Short samples are sensitive to JIT/GC and system activity; the raw data includes an extraction outlier of 2.37 ms in one measured compressed run. Three measured runs are a local comparison, not a general performance guarantee.

Networks dominated by junctions will benefit much less. The earlier dense-grid benchmark and its 500-unique-destination cold burst are a different workload; these results do not claim to eliminate that limitation. Destination fields still use O(number of directed segments) storage. Incremental invalidation and a start-specific search fallback remain future work.

Alongside compression, long-distance train execution now stores immutable planned states separately from bounded materialized traversal history. Trimming beyond roughly 3 km no longer discards future junction choices. Copy/rollback preserve the plan; clearing orders releases it and resumes seeded wandering. Arrival validates the leading coupler against the actual destination visit/position before advancing the order, rather than relying only on a decremented distance counter.

Correctness checks cover 11,088 compressed/uncompressed path comparisons, 480 reference-router cases, interior waypoints, station penalties, closed loops, reverse travel, copied plans, clearing orders and a simulated train taking a planned junction more than five kilometres away and stopping at its actual station. They also deliberately corrupt a distance counter to verify that an order cannot complete at the wrong location.

Reproduce from the repository root:

```powershell
dotnet build "godot/Transport Sandbox.csproj" --no-restore
& "D:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path godot tests/runner.tscn -- --suite=orders
& "D:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path godot tests/runner.tscn -- --suite=corridor-performance
```

The performance run writes `godot/.godot/corridor-routing-performance.json`. `RailRoutingGraph.Search(..., compressCorridors: false)` retains the uncompressed algorithm for comparison.
