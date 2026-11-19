# Tree streaming measurements - 2026-11-18

Godot 4.7.2 .NET, Debug assembly, Windows Compatibility renderer, AMD Radeon RX 6600. Each sample uses 2,048 trees split evenly across the two species. Three warmups followed by 25 measurements; identical positions, dimensions, rotations and colors. The graphical engine ran off-screen. Raw summaries are in [the adjacent JSON](tree-streaming-2026-11-18.json).

| Main-thread work | Before median / p95 | After median / p95 |
| --- | --- | --- |
| Create and attach one chunk | 1.444 / 1.580 ms | 0.517 / 0.613 ms, using worker-prepared buffers |
| 1,000 unchanged tree LOD checks | 0.404 / 0.601 ms | 0.061 / 0.077 ms |
| Immediate destruction of one chunk | 0.086 / 0.105 ms | 0.112 / 0.145 ms |

Creation/attachment on the main thread fell about 64%. The after number is the actual streaming path with transforms and colors prepared by a worker. Preparing and uploading everything synchronously now costs 1.779 ms median / 2.169 ms p95, compared with 1.444 / 1.580 ms before: bulk buffer packing adds CPU work, but that work runs off-thread and is cached in the game. Immediate destruction itself did not improve; retaining batches and budgeting evictions avoids doing it repeatedly or in large bursts.

These are CPU submission timings, not GPU completion or gameplay FPS measurements. They exclude terrain generation and clearance calculation. They do not establish a maximum frame time. A single driver upload can exceed the soft time budget; there is also a hard operation-count limit. Actual tree counts vary by seed and chunk.

## Changes

- Seeded vegetation and immutable transform/color buffers are prepared on workers and reused across terrain LOD and earthwork revisions. The worker cache has a 32 MiB estimated budget and a 128-chunk limit.
- Existing uploaded tree batches move between terrain holders instead of being recreated during 64/128 LOD swaps. Far terrain hides them as before. Unloaded batches use a separate 32 MiB estimated, 128-chunk cache.
- Terrain uploads and cached mounts have a default two-operation, soft 2 ms budget per frame. Tree uploads and deferred destruction share another two-operation, soft 2 ms budget. Counts include completed work so pending uploads cannot trigger duplicate jobs.
- Track clearance filtering runs on workers against immutable fields, with version checks preventing stale results from replacing newer edits. Natural records remain unchanged, and removing track restores trees.
- Tree LOD only modifies resources when its state changes; the original 850 m crown and 1,100 m trunk thresholds remain unchanged.

Checks cover source placement/geometry/color fixtures, graphical buffer readback against the old per-instance setters (including renderer color quantization), upload/destruction limits, multiple cached batch restoration, early unload/reload, near-LOD reuse, clearance restoration, contours and shutdown.

To reproduce the current measurements, build the C# project, then run Godot with `tests/runner.tscn -- --suite=tree-performance`. Use the graphical renderer for upload measurements; headless mode has no real GPU uploads. The benchmark writes `godot/.godot/tree-performance.json`.
