# Spline performance verification — 2026-11-25

The change improves lookup speed and reduces lookup-table memory, but it does not reduce rendered track geometry. Full train simulation showed a small measured improvement; run-to-run variation is large enough that the exact simulation percentage is not established.

## Controlled comparison

The latest local save contains 255 edges, 10 trains and 101 vehicles. Its SHA-256 is `0ACDB60C0F69462C7A8D49D37B6779675F4B43BA6E567548AAE519F369CFF028`. The original save was copied and its hash remained unchanged.

An isolated copy of the current working Godot project preserved the other current code and model changes. The legacy variant replaced only `TrackGeometry.cs` and `RailRoutingGraph.cs` with their versions immediately before commit `96adec8`. A constant `SampleCount` accessor was added to the isolated legacy class only to keep current test code compiling. The adaptive helper remained available for test compilation but legacy runtime paths did not call it. The original workspace code and assembly were not replaced.

Godot 4.7.2 .NET, Debug, 12 logical processors, headless. Simulation order was current → legacy → legacy → current, each with 120 warmup updates and 1,800 measured updates at 1/60 second. Rendering counters from headless runs are not used as GPU measurements.

| Mean train-update CPU per tick | Legacy | Current |
| --- | ---: | ---: |
| First run | 10.553 ms | 9.938 ms |
| Second run | 11.022 ms | 10.564 ms |
| Average of run means | 10.788 ms | 10.251 ms |

That is approximately **5.0% lower measured mean CPU time**. Average curve-batch time was 2.030 → 1.947 ms (4.1% lower). The per-variant run ranges overlap, and unaffected work such as applying poses also varied. Two runs per variant do not establish statistical significance or a reliable 5% causal improvement. They support a modest overall effect rather than the roughly 2× speedup seen in point-query microbenchmarks.

This save and the codebase differ from the November 23 investigation and the November 24 report (which used nine trains and 89 vehicles on a 16-processor runtime). Absolute timings and FPS from those reports are not a controlled baseline for this run.

## Query, construction and memory measurements

The existing `track-arcs` diagnostic passed its accuracy checks and compared the current implementation with both the fixed/Bernstein reference and a fixed/Horner control. Its benchmark performs 261,120 point queries per repeat, averaged over ten repeats; construction builds all 255 curves per repeat, averaged over thirty repeats.

| Metric | Fixed 512-step reference | Current adaptive |
| --- | ---: | ---: |
| Point-query batch | 45.77 ms | 24.45 ms |
| Construct all 255 curves | 21.85 ms | 36.64 ms |
| Allocated bytes per construction batch | 1,081,201 | 832,729 |
| Total lookup samples | 130,815 | 47,583 |
| Retained numeric lookup payload | 1,046,520 bytes | 761,328 bytes |

Queries take **46.6% less time** (about 1.87× throughput). Samples fall **63.6%**, while numeric lookup storage falls **27.3%**, because each adaptive entry stores both parameter and cumulative length rather than length alone. Construction allocations fall **23.0%**, but construction time increases **67.6%**, approximately 15 ms extra for this entire saved network. Construction is a cached/setup cost, not a cost paid for every query.

The fixed-512/Horner control took **25.88 ms**, close to adaptive/Horner's 24.45 ms. This indicates that precomputed polynomial evaluation provides most query speedup; adaptive subdivision primarily provides error control and lower table storage. The microbenchmark is not a direct simulation/FPS prediction.

Adaptive curves use 2–547 samples here: some curves need more entries than the old fixed table. Worst sampled positional deviation against a 131,072-step reference was **0.391 mm** in the saved network; the legacy table was **0.173 mm** on this particular network. Both are submillimetre here. Across synthetic/adversarial cases, the old reference reached **54.8 mm**, while adaptive stayed below **0.4 mm**. The saved-network result does not imply adaptive mapping is more accurate on every individual curve.

## Rendering impact

A geometry census in each isolated implementation reproduced the production renderer's relative float control points, gauges and platforms for all saved edges:

| Generated geometry | Legacy | Current |
| --- | ---: | ---: |
| Surface vertices | 430,488 | 430,488 |
| Surface triangles | 854,760 | 854,760 |
| Sleeper instances | 79,716 | 79,716 |

The changed subdivision controls the arc-length lookup table. Track sweeps still use one step per 1.5 m, platforms one per 2 m, and sleeper spacing is unchanged. Therefore this change does not reduce these mesh/instance counts or address the earlier tree/shadow GPU bottleneck. Faster CPU evaluation may help simulation, track rebuilding and CPU-bound frames, but a substantial GPU-bound FPS gain has not been demonstrated. No graphical FPS or GPU timing claim is made from this headless experiment.

## Artifacts and validation

All four saved-simulation runs and adaptive accuracy checks completed successfully. Only this report and its [summary data](track-arcs-verification-2026-11-25.json) were added to the original workspace. Full raw samples, per-run logs, exact baseline/current source snapshots and the isolated project are under the ignored `.tmp/spline-ab-2026-11-25/` directory. The isolated simulation harness also prints the geometry census before timing; those diagnostics do not run inside measured updates. Shader-cache files could not all be copied due to Windows path length, but these runs use the headless dummy renderer and do not rely on those caches.

Re-run the microbenchmark against a disposable save copy:

```powershell
dotnet build "godot/Transport Sandbox.csproj"
& C:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path godot tests/runner.tscn -- --suite=track-arcs "--save=ABSOLUTE_SAVE_COPY_DIRECTORY"
```

The complete comparison used `--suite=save-performance --phase=simulation --frames=1800`, with `--save` pointing at the same copied snapshot and `--output` pointing at a separate directory for every run.
