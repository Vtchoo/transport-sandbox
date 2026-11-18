# Godot port

Target: Godot 4.7.2 .NET, desktop first, with one metre per world unit. The original Vite game is retained as the behavior reference during migration. This folder is an in-progress native port, not yet a replacement for the browser game.

Open `project.godot` in the .NET editor and press F5 to run the project. Start game opens world setup. In the world preview, WASD pans, middle drag orbits, right drag pans, the wheel zooms, C toggles free camera, and Q/E changes free-camera height.

The project now requires the .NET editor and .NET 8 SDK or newer. The editor builds the C# assembly when running; for command-line tests, first run `dotnet build "godot/Transport Sandbox.csproj"` from the repository root. The solution uses Godot's Debug, ExportDebug and ExportRelease configurations.

## Present

- The ten catalog vehicles, exported for every supported gauge as GLB, including distance-driven steam running-gear animation tracks. Metadata preserves mass, power, tractive effort, dimensions, gauge availability and exhaust outlets. Browser canvas-generated lettering is not included yet.
- C# terrain generator v1, with numerical and mesh fixtures generated from the original implementation. Immutable sampling settings and per-job buffers allow parallel chunk generation without changing the seeded heightfield.
- Gauge-aware signed-endpoint track graph: Bezier handles, parallel groups, split/merge, station grouping and junction restrictions.
- Directed-edge router with a binary priority queue and bounded route cache. It is not yet connected to a train simulation.

The startup scene provides the station/train menu, recommended/advanced world setup, streamed terrain with LOD, trees, contours and overlook/free camera controls. Track rendering uses the original swept rail, ballast and platform profiles, with instanced sleepers. Track editor work is in progress; consist editing, train simulation integration and train windows are not complete. Do not treat the project as feature complete.

## Gameplay UI

`scripts/ui/GameHUD.cs` restores the centered bottom action bar: Build track opens the editor, while Vehicles and Trains remain disabled until their systems are ported. Track/Stations tabs use Material Design icons. The top controls provide camera switching, View options, Settings, copying the world seed and returning to the main menu. Copying the seed replaces browser URL sharing on desktop.

`GameWindow.cs` is the reusable C# in-game window, independent of Godot's operating-system `Window` class. It provides title-bar dragging, resizing, closing, minimizing to a taskbar and rolling up. `WindowManager.cs` manages stacking, active styling, viewport bounds and modal input blocking. Build track, View options and Settings use this shared framework. Settings has a Gameplay submenu with the camera selector; C also switches camera mode. View options control contour intervals and hide trees when contours are enabled. Camera/view preferences persist in `user://game-ui.cfg`. Debug information is hidden by default and toggled with F3.

The menu's New game dialog also uses `GameWindow`, including its modal input handling, focus loop, dragging and resizing. `Collapsible` and `Minimizable` independently hide the corresponding buttons and guard their actions; both are disabled for New game. They can also be changed at runtime. Modal windows default to both disabled when created through `WindowManager.Create`. The shared optional footer keeps Start game fixed while advanced settings scroll; setup values and window bounds retain their existing persistence.

The startup checks cover window lifecycle, minimized restoration, roll-up, bounds, editor/HUD synchronization, modal camera blocking, view options, camera switching, debug visibility and narrow-screen layout. GPU captures were reviewed at 1280x800 and 700x800. `compare-godot-menu.mjs` accepts `game`, `build`, `view` and `settings` states in addition to the menu/setup states. Terrain rendering remains a separate unfinished migration.

## Migration agreement

Port behavior from the browser implementation and verify it against source-based fixtures. Do not substitute simplified behavior without discussing the change. Ask at subsystem boundaries when an engine-specific adaptation is useful; do not assume every visual or interaction should be redesigned.

The menu keeps **the original layout and colors with styled Godot controls** and the original camera framing/drift. Its scene is now assembled from **native game objects**, as requested: the gameplay track graph/renderer and platform, catalog vehicle instances at original scale, shared trees and station furniture. The former baked menu GLBs and material manifest have been removed. Native lighting and materials are shared with gameplay, so its 3D appearance now follows the native game renderer rather than the old browser-specific menu shaders.

All native game scripts and test tools are C#. The menu is separated into `MenuDiorama.cs`, `MenuSmoke.cs` and `MenuUI.cs`. `Main.cs` wires menu startup into the unfinished gameplay port. Graph data uses typed `TrackNode`, `TrackEdge` and `ParallelGroup` objects; `RailRouter.cs` operates on a directed snapshot. `TrainPhysics.cs` retains the existing helper behavior; full moving-train integration is still pending.

Source-exported menu assets and behavior fixtures:

```powershell
node scripts/export-godot-menu-fixtures.mjs
godot --headless --path godot tests/runner.tscn -- --suite=menu
```

Add future compositions to `assets/menu/consists.json`, using catalog vehicle IDs, a compatible gauge and optional `reversed` flags. `VehicleCatalog` and `ConsistVisual` handle model lookup, gauge checks, real dimensions, 0.12 m coupler gaps and exhaust outlets. Menu track length and camera framing derive from the assembled consist; there is no preset-specific geometry export.

`node scripts/compare-godot-menu.mjs --preset=2 --state=advanced --size=1280x800` captures both implementations at the same preset, frame time and viewport. It opens Godot off-screen because headless Godot uses the dummy renderer. Set `GODOT_PATH` for another editor installation. Review images are stored under the ignored `.godot/menu-review/` directory.

Menu verification: source-based fixtures pass for all three compositions, camera framing/drift in landscape and portrait, and deterministic idle exhaust (translated to the native railhead height). Tests also verify gameplay track/station objects, original catalog models without scaling, rear TGV facing, an additional composition and incompatible-gauge rejection. Setup tests cover recommended/advanced values, seed normalization, saved preferences, closing/reopening and the fixed Start footer. Browser/Godot GPU captures were reviewed for the landing screen and setup dialog; native fonts and form widgets retain the agreed Godot adaptation.

## Scalability design

Terrain now uses `scripts/terrain/TerrainSampler.cs` for double-precision noise, heights and palette calculation, and `TerrainBackend.cs` for complete chunk jobs. `TerrainWorld.cs` submits one request per chunk. Sampling and scheduling now stay in C# throughout. Up to three .NET worker tasks run concurrently (matching the browser's CPU-based limit); completed buffers are polled and uploaded on the main thread. Scene teardown cancels outstanding jobs and joins workers.

The loader now follows the browser's radius of ten chunks, altitude-aware 128/64/32/16 tessellation and nearby-first priorities, refilling workers every frame rather than every 200 ms. Distant chunks skip vegetation generation. Tree meshes now reproduce the original broadleaf/conifer geometry and LODs; all 256 menu tree transforms and leaf colors match browser fixtures. Natural terrain tree distribution and terrain rendering remain unfinished migrations; this does not claim vegetation placement, earthworks or gameplay rendering parity.

`scripts/track/TrackGeometry.cs` ports the browser's 512-sample arc-length mapping, finite-difference tangents, upright frames and swept profiles. Rail heads are at curve Y=0; rails are 0.07 m wide and 0.15 m high, with gauge measured between inner faces. Ballast shoulders span 4 m at the base and 2.72 m at the top, from -0.65 m to -0.33 m. Sleepers measure gauge + 0.85 m by 0.18 m by 0.24 m and sit at -0.24 m, spaced by the original arc-length rules. Platforms retain the 3 m slab, caps and yellow edge strip. Menu and gameplay use the same `TrackVisual` objects and native materials.

Sleepers retain 64-instance batches, original ordering, draw-count LOD and hysteresis at 150/400/900/1600 m. Moving the camera updates visible instance counts without rebuilding mesh geometry. Edits rebuild changed segments only; affected segments use rails-only geometry while dragging, with full detail restored on release. Placement previews use the same swept rails and ballast with translucency.

Refresh geometry fixtures with `node scripts/export-godot-track-fixtures.mjs` and `node scripts/export-godot-tree-fixtures.mjs`. The core suite compares every swept vertex, normal and triangle against seven browser cases (straight, graded curve, translated coordinates, vertical, short, preview and rails-only), plus all sleeper matrices, counts and hysteresis transitions. Tree checks cover all five source meshes and the 256 original menu placements/colors. The startup suite verifies affected-only drag rendering, restoration, unchanged-track reuse and gauge-aware previews.

Run `godot --headless --path godot tests/runner.tscn -- --suite=terrain` for parallel completion, cancellation, chunk vertices/normals/colors/topology and all 441 LOD decisions against browser fixtures. Refresh these fixtures with `node scripts/export-godot-chunk-fixtures.mjs`. The `core` suite also validates the C# heights and UTF-16 seed hashes, including Unicode seeds.

Run `godot --headless --path godot tests/runner.tscn -- --suite=benchmark` for a repeatable CPU diagnostic. After the complete C# migration, 10,000 height samples took about 17-18 ms in Debug configuration on this development machine. Three workers produced 25 64-segment chunks in about 188 ms, with the first result at 25 ms. These figures exclude GPU uploads; they are not total-world load times or a cross-machine performance guarantee. The previous GDScript implementation took about 667 ms for the same 10,000 height samples.

Train routing operates on directed track segments, not terrain navigation meshes. The state includes the direction of arrival, so two branches on the same node side cannot become an illegal U-turn. Route requests happen on departure, destination changes and invalidation, rather than each simulation tick. Cache static route geometry independently of occupancy; blocks/signals will decide permission to move.

Keep logical train state separate from rendering. Use compact arrays for position along a route, speed, mass and traction; evaluate track-aligned movement analytically instead of using wheel rigid bodies. Use a fixed simulation timestep and interpolate visible vehicles. Distant trains remain part of the traffic simulation, but need no bogie transforms, running-gear animation or exhaust particles.

Build immutable routing snapshots after edits and give each worker its own search scratch memory/cache. Apply finished results on the main thread only after checking their network revision. Never mutate an active scene tree or shared routing workspace from worker tasks. The current router is a correctness baseline; corridor contraction, regional hierarchy and incremental snapshot updates should be added if large-network profiling requires them.

Use spatially partitioned MultiMeshes for sleepers, trees, repeated train bodies and bogies. Bucket by geometry/material/gauge, world cell and LOD. A world-wide MultiMesh prevents useful culling because all its instances share visibility. Near vehicles may use individually animated detail; distant instances should use simpler geometry. Limit shadows and smoke by distance as well as polygon counts.

Keep authoritative world coordinates in double precision, with chunk-local float transforms and origin rebasing. Default Godot Vector3 coordinates are single precision; the current graph is a migration baseline and still needs this large-world storage layer.

Before declaring scale targets met, benchmark 500 moving trains, 10,000+ cars, a 100,000-segment network, burst route invalidation, heavy station traffic and a dense visible scene. Report simulation, routing, terrain generation and rendering times separately, including worst-frame/99th-percentile times on named hardware. Headless routing tests do not measure rendering performance.

## Rebuilding assets

From the repository root:

```powershell
node scripts/export-godot-assets.mjs
```

This uses the existing model definitions as the source of truth. Godot imports the resulting GLB files directly. No Unity dependency is involved.

Core tests, after Godot has scanned/imported the project:

```powershell
godot --headless --path godot --editor --import --quit
dotnet build "godot/Transport Sandbox.csproj"
godot --headless --path godot tests/runner.tscn -- --suite=all
```

Verified on the installed Godot 4.7.2 .NET editor: core numerical/graph parity and startup integration passed, including all ten imported vehicle definitions, advanced world settings, threaded terrain generation, contour visibility and worker shutdown. These are headless correctness checks, not a visual review or performance benchmark. The restricted execution environment reports a Windows certificate-store access error; the tests do not use network access and complete successfully.

All implemented Godot runtime logic and test tools now use C#. Shaders remain Godot shader language, scenes remain `.tscn`, and asset/fixture exporters remain Node scripts. The original browser TypeScript code is retained as the migration reference. Browser export remains optional and is not supported by this Godot 4 C# project currently.

## C# verification tools

`tests/runner.tscn` launches `tests/TestRunner.cs`. Pass `-- --suite=core`, `menu`, `terrain`, `startup`, `benchmark` or `capture`; `all` runs the four correctness/integration suites. Each run has a timeout and a nonzero exit code on failure. The startup suite exercises the actual C# scene, model imports, terrain worker lifecycle, track construction, station placement, parallel-node continuation and rendering rebuilds.

`tests/physics-fixtures.json` preserves 144 numerical cases captured from the previous GDScript physics helper before removal: realistic/HO, started/stopped, uphill/downhill/flat, several speeds and speed limits. It is a migration baseline, not evidence of full browser train-simulation integration.
