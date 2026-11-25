# Train-follow camera-angle check — 2026-11-25

## Result

Performance is strongly view-dependent. With the same current code, train and saved route, average throughput ranged from **20–23 FPS behind the train** to **33 FPS in front** and **38 FPS from above**. Looking from the opposite end at the same distance and pitch nearly halved the reported primitive workload. These differences are large enough that a single-view FPS result should not be presented as a general improvement across the game.

The earlier saved-camera rerun did restore the same camera pose, so this experiment does not retrospectively prove that camera position caused that before/after difference. It demonstrates view sensitivity and strengthens the need for matched moving-camera traces across code versions. This test uses only the current version; it does not isolate an old/new-code speedup or invalidate separate CPU-only measurements.

## Method

- Same archived November 23 snapshot, SHA-256 `D030AD5C6214A356B12D3FC3B505FF93BD314490458E349240EBDB8302FBD059`: 7 trains, 73 vehicles, 255 edges and 209 signals. Every case restarted the save.
- Followed **Train 5**, GE Dash 9 plus passenger coaches, 8 vehicles, saved speed approximately 75 km/h. It continued running normally for 20 seconds per case, including the other trains' simulation, normal audio updates, terrain/tree streaming, interpolation and floating origin.
- Godot 4.7.2 .NET Debug, Intel Iris Xe, Compatibility renderer, 1280 × 720, VSync off, no FPS cap.
- Low views use 60 m camera distance and pitch −0.25 radians (−14.3°). Rear yaw is heading +180°, front +0°, side +90°. The elevated rear view uses 180 m distance and pitch −1.1 radians (−63.0°), changing both height/distance and pitch intentionally.
- A diagnostic node runs at priority 99 to lock yaw to the current train tangent; the normal camera runs afterward at priority 100. Manual camera input is blocked. Recorded yaw offsets matched the requested angles exactly for every measured sample.
- All 441 wanted terrain chunks and pending tree/earthwork jobs settled for ten frames, up to 90 seconds. A start screenshot was captured before timing, followed by 45 static warmup frames. No screenshots/readbacks or audio recordings occurred during measured frames.
- Order: rear-low → front-low → elevated-rear → side-low → rear-low repeat. Each case is a fresh process and world load from the same snapshot. Real-time simulation is used, so it naturally executes different numbers of catch-up ticks per rendered frame.

## Measurements

FPS is 1,000 divided by mean measured frame interval. GPU and primitive values are means; frame p95 is the 95th-percentile interval. Primitive counts include the passes reported by Godot, including shadows.

| View | Average FPS | Mean frame | p95 frame | Mean GPU | Mean primitives/frame |
| --- | ---: | ---: | ---: | ---: | ---: |
| Rear, low | 22.6 | 44.32 ms | 53.50 ms | 32.68 ms | 8.07 million |
| Front, low | 33.4 | 29.96 ms | 47.8 ms | 22.84 ms | 4.28 million |
| Rear, elevated | 38.1 | 26.23 ms | 35.4 ms | 23.71 ms | 5.55 million |
| Side, low | 30.6 | 32.64 ms | 52.4 ms | 26.23 ms | 4.43 million |
| Rear, low repeat | 20.2 | 49.59 ms | 60.0 ms | 35.11 ms | 8.10 million |

At the same low-view distance and pitch, the front view averaged 48% more FPS than the first rear view, with 47% fewer reported primitives. The repeated rear run returned to the high-workload/low-FPS pattern. Its frame time was approximately 12% worse than the initial rear run, so machine/run drift remains and exact angle ratios should not be treated as universal constants. The elevated result is a combined change in direction/pitch/distance, not an isolated pitch-only test.

The starting images were inspected to confirm the follow targets and viewpoints. The front view looks toward a sparser hillside, while the rear view includes more forested scenery in the distance. The elevated view looks mainly down at the ground and train.

## Check against route-progress bias

Trains travelled 482–491 m in the full 20-second traces. The front trace includes a 394.8 ms stall; simulation delta clamping and differing frame schedules can produce small progress differences. Therefore the first **400 m of actual train travel** were also compared, using the accumulated measured train displacement rather than wall time.

Every case followed the same edge sequence **9 → 7 → 21** within those 400 m.

| View | FPS over shared first 400 m |
| --- | ---: |
| Rear, low | 22.6 |
| Front, low | 34.1 |
| Rear, elevated | 39.1 |
| Side, low | 30.3 |
| Rear, low repeat | 19.9 |

The ranking remains, so a shorter or different route is not the explanation for the view differences. Matching progress does not make traffic timing or machine scheduling identical.

## Stalls and limitations

The front view's worst frame was 394.8 ms. That sample reported no managed collection, rebase, terrain upload or tree operation. Its measured render GPU/CPU and train-update scopes did not account for the whole delay, so its source is unassigned; it must not be labelled a spline, terrain or GC stall from this trace alone. It is retained in the statistics. The next frames also slowed, and other cases have smaller outliers.

CPU and GPU measurements overlap and must not be added. Per-frame train-update CPU also depends on how many fixed simulation ticks occur in the frame. For example, the initial rear view averaged 2.67 ticks/frame versus 1.78 in front. This does not establish a corresponding per-tick algorithm improvement. Background load, power management and thermal conditions were not controlled. One pass per new viewpoint plus one repeated rear pass establishes strong view sensitivity here, not statistically precise performance for every route or code version.

## Artifacts and validation

All five follow runs completed successfully. Only diagnostic code inside the existing isolated `.tmp/rerun1125/project/` copy was changed: `FollowTrainPerformance.cs` gained controlled angle/distance options, strict warmup, angle telemetry and pre-timing screenshots; `FollowProfileCamera.cs` drives train-relative yaw. Production source and the main project assembly were preserved. The profiled snapshot hash remained unchanged.

[Summary data](follow-angles-2026-11-25.json) includes all case metrics, shared-distance metrics, angle validation and worst samples. Full traces, logs and start screenshots are under `.tmp/follow-angles1125/`. The JPEG names are `<case>-start.jpg`; they are not timing samples.

To reproduce with the isolated project, use the Godot .NET console with `--path .tmp/rerun1125/project --resolution 1280x720 --disable-vsync --max-fps 0 tests/runner.tscn`, then `-- --suite=follow-performance --save=ABSOLUTE_SAVE_COPY --output=ABSOLUTE_OUTPUT --train=5 --seconds=20 --images=false --audio=false --name=CASE --yaw-offset=DEGREES --pitch=RADIANS --distance=METRES`. Here `--audio=false` disables diagnostic audio recording, not normal gameplay audio processing.
