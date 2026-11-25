# Saved-world performance investigation — 2026-11-23

## Findings

The largest rendering targets in this save are natural trees and shadow geometry. The largest measured game calculation is train curve-speed lookahead, followed by updating vehicle transforms and collision work. Normal real-time play averaged 68 ms/frame (about 15 FPS), executing roughly four simulation steps per rendered frame. Signal logic, industry production and settled terrain streaming are much smaller. These findings apply to this saved camera and workload, not every possible camera or network.

The latest local `debug-playground` snapshot (display name `New World`, saved at 08:45:56 São Paulo time) contains 7 trains, 73 vehicles, 255 track edges, 221 nodes, 209 signals and 60 industries. Six trains are running. Measurements used a copy under `.tmp/save-profile-2026-11-23/save`; the original snapshot's SHA-256 remained unchanged: `D030AD5C6214A356B12D3FC3B505FF93BD314490458E349240EBDB8302FBD059`.

Hardware/runtime: Intel Iris Xe, Compatibility/OpenGL, Godot 4.7.2 .NET Debug assembly, 12 logical processors, requested 1280 × 720 with VSync disabled and no FPS cap. The frozen render diagnostic verified an actual 1280 × 720 viewport. Loaded world: 441 chunks and 53,729 tree instances; that tree count includes loaded off-screen trees.

## Rendering in one frozen snapshot

One world load, saved camera, paused game calculations, 45 warmup frames and 120 measured frames per case. Baselines repeat without reloading the save. Values below are median milliseconds; primitives and draw calls include the passes reported by Godot, including shadow work.

| Case | Frame interval | GPU | Draw calls | Primitives |
| --- | ---: | ---: | ---: | ---: |
| Baseline | 33.67 | 32.96 | 1,141 | 6,485,999 |
| Trees hidden | 16.81 | 16.02 | 1,022 | 2,541,899 |
| Baseline after trees | 38.91 | 38.11 | 1,141 | 6,485,999 |
| All directional shadows off | 20.62 | 20.15 | 725 | 2,040,410 |
| Train instances and smoke hidden | 34.53 | 33.70 | 1,046 | 6,259,599 |
| Tracks, bridges and signals hidden | 49.86 | 48.64 | 435 | 6,159,387 |
| Signals hidden | 38.66 | 38.34 | 847 | 6,472,475 |
| Industries hidden | 38.38 | 36.62 | 1,132 | 6,485,567 |
| Half linear 3D resolution | 34.56 | 33.88 | 1,141 | 6,485,999 |
| Baseline repeat | 38.89 | 37.98 | 1,141 | 6,485,999 |

Trees account for approximately 3.94 million of the reported primitives in this view. Disabling all shadows removes approximately 4.45 million. These overlap and must not be added. Halving linear resolution (one quarter of the pixels) changes much less than removing trees or shadows, favoring geometry/shadow work over pixel shading alone as the first investigation target.

Baselines drift from roughly 33 to 38 ms GPU time. The anomalously slow no-tracks case, despite its lower draw count, is a warning against treating these sequential local runs as precise additive costs. Power/thermal/background conditions were not controlled. Hiding trains, industries or signals did not show the substantial benefit observed when hiding trees or shadows. This is not proof that those objects are free to render. GPU timestamps are elapsed rendering measurements, not pass-level attribution; inspect the Visual Profiler before assigning all shadow cost to trees specifically.

## Game calculations

The calculation-only harness restores all saved trains and the track graph, manually advances the train editor by 1/60 second per update, and excludes terrain/industry/UI processes. Each case uses 120 initialization updates and 300 measured updates. Rendering counters from these cases are deliberately ignored because no new frame need be drawn during the timed loop.

| Calculation | Parallel mean per update | p99 |
| --- | ---: | ---: |
| Entire train update | 7.23 ms | 9.28 ms |
| Curve-speed lookahead batch | 2.66 ms | 3.42 ms |
| Apply body/bogie poses and running gear | 1.47 ms | 2.14 ms |
| Collision lookahead | 0.97 ms | 1.28 ms |
| Calculate vehicle poses | 0.65 ms | 0.87 ms |
| Validate new poses against other trains | 0.49 ms | 0.62 ms |
| Sync train instances | 0.46 ms | 0.82 ms |
| Grade batch | 0.12 ms | 0.24 ms |
| Signal/interlocking update | 0.11 ms | 1.05 ms |
| Execute train orders | 0.006 ms | 0.018 ms |

Scopes nest: the entire train update contains the other rows, so totals must not be summed. Serial train calculations took 10.41 ms/update, including 5.45 ms for curve lookahead. Existing parallelization is useful. Stopping every train reduced total train-update time to 0.23 ms. Main-thread allocation averaged about 59 KiB/update (about 3.5 MiB/s at 60 ticks/s); this excludes worker-thread allocations. Three Gen-0 collections occurred in 300 parallel samples; calculation timing does not show GC as the leading steady-state cost.

In the settled graphical fixed-timestep baseline, terrain processing averaged 0.19 ms/frame, tree pumping 0.08 ms, industry updates 0.03 ms, industry production roughly 0.02 ms and train UI 0.02 ms. This does not cover their startup work or editing/replanning spikes. Train calculation time grows when competing with the renderer on this integrated GPU system; isolated calculation timings are not an in-game FPS prediction.

Code targets:

- `TrainCalculationBatch.cs:64`: `CalculateCurveLimit` scans the entire consist plus braking lookahead every tick in 5 m increments. Every sample invokes `TrainRoute.TurnRadius`, which samples the route again at 0 and ±2 m. Cache or precompute curvature per track revision and reuse conservative route-limit envelopes before considering looser safety thresholds.
- `TrainMovement.cs:72` and `PosedVehicleVisual.cs`: applying poses updates native body/bogie transforms for moving vehicles; steam running gear also seeks animation players. This is separate from calculating those poses.
- `TrainMovement.cs:201`: collision lookahead scans at 4 m spacing, in addition to per-car pose overlap validation. Profile spatial-query/sample reuse before removing collision checks.
- `TreeScenery.cs`: near/far crowns and trunks remain chunk/species MultiMeshes. Investigate smaller spatial batches, crown LOD and cheaper shadow-caster representations. The shared light uses four directional shadow splits and a 1,000 m shadow distance. Measure tree-shadow and terrain-shadow changes separately before selecting a production setting.

## Real-time saved-camera confirmation

With train processing enabled normally, after all 441 chunks settled, 240 frames averaged 67.98 ms (median 66.77 ms, p99 101.62 ms). GPU elapsed time averaged 53.63 ms. The train update averaged 33.13 ms CPU/frame, including 14.12 ms curve batches, 5.14 ms collision lookahead, 4.01 ms pose calculation, 2.99 ms collision validation, 3.21 ms applying poses/running gear, and 0.71 ms syncing instances. Audio updates averaged 0.17 ms, industry updates 0.06 ms and terrain processing 0.15 ms. These nested and overlapping CPU/GPU numbers must not be added to reconstruct frame time.

The probe recorded 4.07 simulation substeps/frame on average, with p99 at 6. `TrainMovement.UpdateMovement` accumulates render delta and runs 1/60-second ticks, clamping one incoming delta to 0.1 seconds. Thus a slow rendered frame requires several curve/collision/pose calculations before another frame can be produced. Visual pose application happens once per rendered frame, while much of the movement logic repeats per substep. Fixing render cost can also reduce CPU work per rendered frame; speeding up curvature calculations makes the system less vulnerable to this catch-up load. These measurements do not justify dropping safety checks or changing simulation semantics without separate tests.

Real-time results use moving trains and share CPU/GPU power and memory bandwidth on an integrated GPU. They are not directly comparable as an isolated toggle against the frozen snapshot, but confirm that both rendering and calculations matter in actual play. No moving-camera trace, whole-session managed-heap profile or pass-level GPU capture was performed here.

## Reproduction and diagnostic changes

Only `godot/tests/SavePerformance.cs` changed for this investigation. It now waits for all wanted terrain chunks, pending earthworks and tree work to settle for ten frames (up to 90 seconds), rejects incomplete warmup, supports the frozen `world-render-matrix` and a half-resolution diagnostic. The former 15-second warmup started some preliminary runs with zero or only 41 loaded chunks. Those preliminary rendering numbers are excluded here.

```powershell
dotnet build "godot/Transport Sandbox.csproj"
# Set this to a disposable COPY of the save directory, including its regions.
$profileSave = (Resolve-Path '.tmp/save-profile-2026-11-23/save').Path
$profileOutput = (Resolve-Path '.tmp/save-profile-2026-11-23').Path
& C:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe --path godot --resolution 1280x720 --disable-vsync --max-fps 0 tests/runner.tscn -- --suite=save-performance "--save=$profileSave" "--output=$profileOutput" --phase=world-render-matrix
```

Use `--phase=simulation`, `simulation-serial` or `simulation-stopped` with `--frames=300` for calculations; `--phase=world --frames=240` measures normal real-time play at the saved camera. Do not use headless mode for rendering comparisons. Full raw samples and logs are under the ignored `.tmp/save-profile-2026-11-23/` directory. [Summary data](saved-world-2026-11-23.json) preserves the frozen render cases and calculation distributions. Production game settings and algorithms were not changed.
