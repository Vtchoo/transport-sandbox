# Saved-world benchmark rerun — 2026-11-25

## Result

On the exact November 23 save and camera, current real-time play averaged **42.13 ms/frame (23.7 FPS)** versus **67.98 ms/frame (14.7 FPS)** in the earlier run. Mean frame time fell 38%. Calculation-only train updates fell 21%. Trees and shadows remain the main rendering targets; the geometry budget is effectively unchanged, and isolated GPU timings drift too much to establish a reliable GPU speedup.

This compares the current working version, including its other code/model changes, with the November 23 measurements. It does not isolate adaptive subdivision. The separate [spline-only A/B investigation](track-arcs-verification-2026-11-25.md) held the other current code constant and used the newer 10-train/101-vehicle save; its smaller measured overall gain is a different comparison.

## Same workload and method

- Archived November 23 snapshot: 7 trains, 73 vehicles, 255 edges and 209 signals; 6 trains running. SHA-256: `D030AD5C6214A356B12D3FC3B505FF93BD314490458E349240EBDB8302FBD059`.
- Saved camera and settings, 1280 × 720, Intel Iris Xe, Compatibility renderer, Godot 4.7.2 .NET Debug, 12 logical processors. VSync disabled, FPS uncapped. World settled at 441/441 chunks and 53,729 loaded tree instances, matching the earlier run.
- Calculation cases: 120 warmup updates, 300 measured updates, 1/60-second fixed delta, same parallel/serial/stopped variants.
- Frozen rendering matrix: one loaded snapshot, paused game calculations, 45 warmup frames and 120 measured frames per case, same case ordering and baseline repetitions.
- Graphical fixed-timestep case and normal real-time case: 120 warmup updates/frames, then 240 measured frames each. All wanted chunks, pending earthworks and tree work had to settle for ten frames before warmup completed; timeout 90 seconds.
- Code, assets and the archived save were copied into `.tmp/rerun1125/`. The earlier strict warmup and frozen-matrix harness were recreated only in that isolated copy. The main project's current source, assembly, and live save were not replaced.

## Calculation-only comparison

Mean milliseconds per 1/60-second update; positive improvement means less time.

| Scope | November 23 | Rerun | Reduction |
| --- | ---: | ---: | ---: |
| Entire train update, parallel | 7.230 | 5.703 | 21.1% |
| Entire train update, serial | 10.407 | 7.824 | 24.8% |
| Entire train update, all stopped | 0.227 | 0.220 | 2.9% |
| Curve-lookahead batch, parallel | 2.665 | 1.967 | 26.2% |
| Apply poses/running gear | 1.465 | 1.329 | 9.3% |
| Collision lookahead | 0.975 | 0.688 | 29.4% |
| Calculate poses | 0.655 | 0.457 | 30.3% |
| Sync train instances | 0.459 | 0.393 | 14.3% |

Scopes nest; do not sum them. Rendering counters from calculation-only cases are not used as GPU measurements, since those loops need not draw a fresh frame per update.

## Normal real-time play

| Metric | November 23 | Rerun |
| --- | ---: | ---: |
| Mean frame interval | 67.98 ms | 42.13 ms |
| Throughput, 1,000 / mean frame interval | 14.7 FPS | 23.7 FPS |
| p95 frame interval | 91.57 ms | 68.33 ms |
| p99 frame interval | 101.62 ms | 80.10 ms |
| Mean train-update CPU per rendered frame | 33.13 ms | 18.86 ms |
| Mean curve-batch CPU per rendered frame | 14.12 ms | 7.13 ms |
| Mean simulation substeps per rendered frame | 4.07 | 2.53 |
| Mean measured GPU elapsed time | 53.63 ms | 35.52 ms |

Faster frames require fewer 1/60-second catch-up ticks: substeps fell from 4.07 to 2.53 per rendered frame. Thus the 43% reduction in train CPU **per rendered frame** includes both cheaper calculations and fewer ticks; it is not a 43% reduction in the cost of one simulation tick. CPU and GPU measurements overlap and must not be added. These frame times still do not deliver a stable 60 FPS.

The graphical fixed-timestep case confirms a CPU improvement while showing no corresponding rendering win in that pass: train-update CPU fell 13.46 → 9.73 ms and curve batches fell 4.66 → 3.01 ms. Mean frame interval was 38.65 → 40.05 ms and GPU time 33.65 → 36.88 ms. With one simulation step forced per frame, it cannot benefit from reducing catch-up steps as normal real-time play does.

## Frozen rendering comparison

Median milliseconds, same static world/camera. Each row is a sequential diagnostic, not an independent additive contribution.

| Case | Previous frame interval | Rerun frame interval | Previous GPU | Rerun GPU |
| --- | ---: | ---: | ---: | ---: |
| Baseline | 33.67 | 52.17 | 32.96 | 44.12 |
| Trees hidden | 16.81 | 16.95 | 16.02 | 14.03 |
| Baseline after trees | 38.91 | 61.92 | 38.11 | 38.95 |
| All shadows disabled | 20.62 | 28.68 | 20.15 | 19.75 |
| Trains/smoke hidden | 34.53 | 45.61 | 33.70 | 34.27 |
| Tracks/bridges/signals hidden | 49.86 | 49.21 | 48.64 | 37.32 |
| Signals hidden | 38.66 | 47.30 | 38.34 | 34.45 |
| Industries hidden | 38.38 | 56.08 | 36.62 | 43.89 |
| Half linear 3D resolution | 34.56 | 46.69 | 33.88 | 18.79 |
| Baseline repeat | 38.89 | 51.83 | 37.98 | 20.43 |

Baseline GPU medians drifted 44.12 → 38.95 → 20.43 ms with identical geometry. Frame intervals also diverge from GPU timestamps in this rerun. Neither a precise GPU speedup nor a definite rendering regression can be attributed to code from these sequential runs. The fixed-timestep graphical result likewise gives no reliable evidence of a rendering improvement. Integrated CPU/GPU contention, scheduling and power/thermal/background conditions were not controlled. Pass-level GPU profiling is needed before assigning a cause to that drift.

The baseline draw budget remains **1,141 calls**, with **6,486,041 primitives** now versus **6,485,999** previously. Hiding trees still removes **3,944,100 primitives**, exactly the same reduction as before, and brings frame intervals close to 17 ms. Disabling shadows removes approximately 4.45 million primitives. Those geometry reductions overlap. The robust conclusion is that the large tree/shadow workload remains even though real-time simulation is cheaper.

## Validation and artifacts

All six benchmark phases completed successfully. Build completed with zero errors; existing nullable-context warnings remain. The archived source and profiled save snapshot hashes match after the run. The main project's source and assembly were preserved; profiling loaded only copied world directories and introduced no gameplay-setting or algorithm changes.

[Comparison data](saved-world-rerun-2026-11-25.json) contains before/after distributions, all frozen render cases and runtime metadata. Full raw samples, isolated profiling source, assets and logs remain in `.tmp/rerun1125/`. The [November 23 report](saved-world-2026-11-23.md) is retained unchanged.

For the isolated rerun project, invoke the Godot .NET console with `--path .tmp/rerun1125/project --resolution 1280x720 --disable-vsync --max-fps 0 tests/runner.tscn`, followed by user arguments `--suite=save-performance --save=ABSOLUTE_COPY_DIRECTORY --output=ABSOLUTE_OUTPUT_DIRECTORY --phase=PHASE --frames=N`. Phases were `simulation`, `simulation-serial`, `simulation-stopped` (300 samples), `world-render-matrix` (its own 45/120 frame protocol), `world-fixed`, and `world` (240 samples).
