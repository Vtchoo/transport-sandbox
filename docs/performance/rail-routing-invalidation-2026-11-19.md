# Routing invalidation - 2026-11-19

The scheduler now retains destination caches and in-flight searches for unchanged physical rail components, rather than clearing the whole world after any edit. Components are grouped by shared nodes and gauge; directed connection-side rules remain unchanged inside each graph.

Installed train journeys also survive snapshot rebuilding. A changed component refreshes its destination fields, but trains whose planned edges and adjacent connections were untouched keep moving while their replacement is prepared. Trains with affected paths brake and replan. Removed occupied track still stops the train as unsupported; a deleted future segment blocks the route without falsely removing support. Order edits still affect the individual train immediately.

The local headless comparison uses 100,000 straight segments across 20 separate networks of 5,000 segments each, two workers and one destination field per network. After warming all fields, one segment's station assignment changes in one network. A fresh scheduler on the edited input represents a full reset. Input arrays and known arc lengths are supplied before timing; snapshot construction, scheduler admission and all twenty field requests are included. This is one development Debug run on the same local Windows / Godot 4.7.2 .NET setup as the corridor report, not a statistical FPS measurement. [Raw result](rail-routing-invalidation-2026-11-19.json).

| Measurement | Localized invalidation | Full reset |
| --- | ---: | ---: |
| Snapshot and fields ready | 108.05 ms | 220.79 ms |
| Destination searches after edit | 1 | 20 |
| Existing component graphs retained | 19 | 0 |

The localized edit avoids 19 of 20 searches. Retained destination arrays occupy 3,200,640 bytes. Timings are sensitive to JIT, GC and system activity; this test excludes scene snapshot capture, per-train plan extraction/installation, movement, collision checks and rendering. It does not establish performance for hundreds of moving trains or for one enormous connected network.

The worker still scans the entire snapshot to identify components and changes. A changed component rebuilds its graph and destination fields; this is component-level invalidation, not incremental shortest-path repair within that component. A large connected railway therefore still pays a component-wide refresh, while unaffected installed journeys keep moving. Dependency checks deliberately include already traversed planned edges, so some behind-the-train edits can cause conservative braking. Published change history is bounded; journeys older than the retained history also replan conservatively. Rapid edits cancel stale builds without canceling work on unchanged components.

Correctness checks cover exact cached-field identity, unchanged in-flight searches, component merge/split, changed handles and gauges, no-op/reordered snapshots, rapid edits, worker limits, and conservative handling of missing edit history. Actual train tests verify no braking/search after an isolated edit, no braking during a remote same-component refresh, braking for deleted future track, recovery after reconnection, immediate stopping after removal of occupied track, and preservation of wandering station service.

Reproduce after building the C# project:

```powershell
& 'D:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path godot tests/runner.tscn -- --suite=invalidation-performance
```

The raw output is written to `godot/.godot/routing-invalidation-performance.json`. Run `--suite=orders` for routing and train integration correctness, or `--suite=all` for the full port checks.
