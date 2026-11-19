# Browser tree streaming, 2026-11-18

Windows, Chrome 157, AMD Radeon RX 6600 through ANGLE/D3D11. Five warmups and 25 samples, with 2,048 synthetic trees split equally between species. Each created batch was actually rendered, but **timings cover CPU creation and attachment only**, excluding that render and GPU completion. The original per-instance renderer is reproduced in the benchmark using the same geometry, materials, colors and transforms.

| Operation | Median | P95 |
| --- | ---: | ---: |
| Original create + attach | 1.30 ms | 1.80 ms |
| Worker-prepared create + attach | 0.10 ms | 0.30 ms |
| Cached return, same prepared data | Below timer resolution | Below timer resolution |
| Reuse batches with changed root heights | Below timer resolution | 0.10 ms |

The measured median creation cost on the render thread fell by about 92%. This does not measure total CPU work, worker preparation latency, GPU upload time, gameplay frame-time percentiles or FPS. New preparation includes additional distant bounding volumes; the work is moved to workers rather than eliminated. Chrome's roughly 0.1 ms timer resolution limits the smaller measurements. Reuse with changed terrain root heights still requires GPU buffer updates.

The same run loaded terrain through real module workers and rendered 3,344 trees across ten chunks, reaching nine nearby ready chunks in eleven animation frames without WebGL errors. This is a smoke check, not a loading-time benchmark.

The runtime retains compatible instance meshes and buffers across terrain LOD changes. It keeps the current terrain and trees until a replacement is committed; contours and far terrain retain their original visibility behavior. Uploads, cached terrain mounts and disposal share a maximum of two operations and a soft 2 ms budget per frame. One operation or a driver upload cannot be preempted. Worker-result queues have backpressure; inactive vegetation caches have explicit count and memory bounds.

Regression coverage verifies exact matrix/color arrays and culling calculations against per-instance Three.js, both terrain LOD root seating and transferable-buffer ownership, stale revision rejection, previous-visual retention, buffer reuse, contour visibility, bounded queues and deferred disposal. Production build and all 124 regression tests passed.

Reproduce with `node scripts/benchmark-tree-streaming.mjs`. Override `CHROME_PATH` if necessary. Raw data: [browser-tree-streaming-2026-11-18.json](browser-tree-streaming-2026-11-18.json).
