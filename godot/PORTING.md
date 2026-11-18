# Godot port

Target: Godot 4.7.2 .NET, desktop first, with one metre per world unit. The original Vite game is retained as the behavior reference during migration. This folder is an in-progress native port, not yet a replacement for the browser game.

Open `project.godot` in the .NET editor and press F5 to run the project. Start game opens world setup. In the world preview, WASD pans, middle drag orbits, right drag pans, the wheel zooms, C toggles free camera, and Q/E changes free-camera height.

The project now requires the .NET editor and .NET 8 SDK or newer. The editor builds the C# assembly when running; for command-line tests, first run `dotnet build "godot/Transport Sandbox.csproj"` from the repository root. The solution uses Godot's Debug, ExportDebug and ExportRelease configurations.

## Present

- The ten catalog vehicles, exported for every supported gauge as GLB, including distance-driven steam running-gear animation tracks. Metadata preserves mass, power, tractive effort, dimensions, gauge availability and exhaust outlets. Browser canvas-generated lettering is not included yet.
- C# terrain generator v1, with numerical and mesh fixtures generated from the original implementation. Immutable sampling settings and per-job buffers allow parallel chunk generation without changing the seeded heightfield.
- Gauge-aware signed-endpoint track graph: Bezier handles, parallel groups, split/merge, station grouping and junction restrictions.
- Directed-edge router with a binary priority queue and bounded route cache. It is not yet connected to a train simulation.

The startup scene provides the station/train menu, recommended/advanced world setup, streamed terrain with LOD, trees, contours and overlook/free camera controls. Track rendering uses the original swept rail, ballast and platform profiles, with instanced sleepers. Track/station editing and train building/parked placement are available. Moving-train simulation integration is not complete. Do not treat the project as feature complete.

## Gameplay UI

`scripts/ui/GameHUD.cs` restores the centered bottom action bar: Build track opens the track editor, Vehicles opens the independent catalog, and Trains opens the fleet window. Track/Stations tabs use Material Design icons. The top controls provide camera switching, View options, Settings, copying the world seed and returning to the main menu. Copying the seed replaces browser URL sharing on desktop.

`GameWindow.cs` is the reusable C# in-game window, independent of Godot's operating-system `Window` class. It provides title-bar dragging, resizing, closing, minimizing to a taskbar and rolling up. `WindowManager.cs` manages stacking, active styling, viewport bounds and modal input blocking. Build track, View options and Settings use this shared framework. Settings has a Gameplay submenu with the camera selector; C also switches camera mode. View options control contour intervals and hide trees when contours are enabled. Camera/view preferences persist in `user://game-ui.cfg`. Debug information is hidden by default and toggled with F3.

The menu's New game dialog also uses `GameWindow`, including its modal input handling, focus loop, dragging and resizing. `Collapsible` and `Minimizable` independently hide the corresponding buttons and guard their actions; both are disabled for New game. They can also be changed at runtime. Modal windows default to both disabled when created through `WindowManager.Create`. The shared optional footer keeps Start game fixed while advanced settings scroll; setup values and window bounds retain their existing persistence.

Windows can use explicit bounds or screen placement presets: Center, TopLeft, TopCenter, TopRight, CenterLeft, CenterRight, BottomLeft, BottomCenter and BottomRight. Presets support pixel offsets and follow viewport resizing until a manual drag, resize or `SetBounds` switches the window to Custom placement. Normal viewport constraints still apply, reserving room for the bottom HUD.

```csharp
var dialog = windows.Create("Settings", WindowPlacement.Center, new Vector2(440, 360), modal: true);
var tools = windows.Create("Tools", WindowPlacement.TopLeft, new Vector2(330, 460), offset: new(0, 61));
var custom = windows.Create("Custom", new Rect2(24, 85, 330, 460));
tools.Place(WindowPlacement.BottomRight);
```

The track editor's `TrackNodeHoverMenu` now follows the browser's screen projection: hover within 100 px of a node to select its tangent side, with continuation 65 px along that side, delete 32 px beside it, and parallel-node controls on the projected lateral axis. Controls hide beyond 600 m, behind the camera, during drags, in station mode or beneath modal UI. Station-facing continuation is suppressed. Node deletion and merging require a second click; deletion preserves neighboring/parallel nodes, while merge is available only for two compatible opposite-side connections and retains outer endpoint handles.

Parallel groups expose a separate continuation action that previews and builds every track, then continues from the new group until Escape. `ParallelTracks.cs` ports offset-curve handle fitting, gauge/spacing matching and tight-turn rejection from the original model. Group creation commits as one graph notification and rolls back failed connections. Preview fitting is reused while the endpoint and graph stay unchanged. Node hover, action confirmations, placement presets and grouped construction are covered by `NodeHoverChecks`; source curve fixtures can be refreshed with `node scripts/export-godot-parallel-fixtures.mjs`. The native hover layout was also reviewed in a GPU capture (`compare-godot-menu.mjs --state=build --hover=node` adds a native test track).

The startup checks cover window lifecycle, minimized restoration, roll-up, bounds, editor/HUD synchronization, modal camera blocking, view options, camera switching, debug visibility and narrow-screen layout. GPU captures were reviewed at 1280x800 and 700x800. `compare-godot-menu.mjs` accepts `game`, `build`, `view` and `settings` states in addition to the menu/setup states. Terrain rendering remains a separate unfinished migration.

The track tools include the original world grid selector (Off, 1, 5, 10, 25 metres). G toggles snapping and restores the last nonzero spacing. The 80-cell translucent overlay follows the pointer on fixed world X/Z axes, hides over UI/in station mode/when disabled, and preserves placement height. Snap rounding matches JavaScript even at negative half-cell boundaries. Existing-node placement and Shift's outgoing straight alignment take priority.

Selected parallel groups expose purple lateral spacing handles about 25 screen pixels outside their nodes. Selecting the anchor shows every non-anchor handle; selecting another member shows only its own. Dragging changes that node's offset only, ignoring Shift and world-grid snapping, with rails-only rendering restricted to its connected segments. Handles track the camera zoom. Deleting an anchor rebases the remaining offsets without moving the nodes. `--suite=grid` checks grid selection/toggling/rounding, snapping priority, native spacing dragging, rendering scope and group rebasing; `--hover=grid` adds a native visual fixture.

## Train building, placement and movement

`TrainBuilder`, `TrainPlacement`, `TrainEditor` and `TrainWindows` port the browser building/placement workflow. A catalog click appends at the rear of the frontmost expanded train window only when its Edit train tab is open; otherwise it creates a new train and opens that editor. The catalog owns no train selection and filters by the common vehicle gauges, plus the placed train's actual track gauge. It retains the original per-model SVG thumbnails and a hovering/focused vehicle's rotating 3D preview.

Each train has Viewport/Edit train tabs, horizontal front-to-rear cards, insertion gaps, drag-and-drop reorder/transfers, flip/remove controls, trash drop, Add vehicles, Place train, Reverse preview (R), and stopped-only pickup. Clicking a placed vehicle opens its viewport. The viewport and Follow train focus on the first vehicle; pan input stops following. The viewport play/pause command starts a placed powered train or applies service braking. Speed is shown in km/h.

Placement follows arc distance across compatible opposite-side connections, rejects ambiguous junctions, insufficient track, overly tight rigid sections and overlap with other trains, and shows translucent original models with valid/invalid tint and a cursor hint. R reverses the preview, Escape cancels without changing construction, hovering a stopped train's front/rear coupler previews attachment, and committing merges the incoming consist into it. Leading couplers snap within 5 m to station-run ends on their own track, including runs spanning split segments. Vehicle bodies, bogies, and the permanently coupled Rio Grande engine/tender follow independent source poses at real dimensions.

Stopped consists remain on rails when edited, preserving the first surviving original car's location. An edit that runs out of free track or exceeds the curve clearance lifts the train for repositioning; other invalid edits roll back. Moving/started trains reject edits and pickup with a pointer warning. Editing/removing supporting rails updates the train or marks it unsupported without teleporting it.

Placed models use merged material surfaces for reusable body/bogie templates and `MultiMesh` instances grouped in 512 m cells. Picking still tests the original model triangles. Diesel/steam exhaust responds to delivered traction with the browser emission rates, smoothing and particle parameters. Steam running gear uses the baked distance-driven animation; animated parts have independent instancing transforms. Draft/catalog viewports use private worlds; placed-train views share the live game world and update at the browser's viewport intervals.

`TrainMovement` and `TrainRoute` port bounded 1/60 s stepping, seeded opposite-side junction choices, persistent per-visit itineraries, mass-weighted grade, realistic/HO force integration, curve braking over the entire consist and approach, occupied-track braking and overlap rejection, and automatic reversal before dead ends. The world seed and physics selection feed the simulation. Stopped edits and transaction rollback retain the occupied itinerary. Missing or invalid supporting track stops the affected train at its last valid pose. MultiMesh batches update their transforms in place, rebuilding only changed cell/count membership. No signals, timetable/station stops or route planning yet.

`node scripts/export-godot-movement-fixtures.mjs` records browser junction itineraries, clearance, curvature and realistic/HO trajectory checkpoints. The train suite verifies these fixtures, plus native play/pause, reverse movement, service braking, stopped edits, stable instance nodes, collision waiting and unpowered start rejection.

Refresh thumbnails and articulated-section metadata with `node scripts/export-godot-train-metadata.mjs` (also called by the full asset exporter). `node scripts/export-godot-train-fixtures.mjs` captures browser placement/walking/station/rigid-section fixtures. `--suite=train` checks those numerical fixtures and native catalog routing, construction, transfers, placement, attachment, edits, collision rejection, gauge filtering, station snapping and stopped-only pickup. Native captures support `--hover=train-edit`, `train-preview` and `train-viewport` with `--state=game`. Headless train tests drain queued node deletion and managed finalizers before quitting to release temporary preview/mesh resources while Godot's C# runtime is alive.

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

`TerrainMeshCache.cs` retains inactive uploaded meshes and natural-tree records by chunk, LOD and earthwork revision, bounded to an estimated 128 MiB and 256 entries. Active chunks do not count toward that budget. Returning to a cached LOD or unloaded chunk mounts the existing mesh without worker generation or re-uploading geometry. Least recently used entries are evicted and their mesh references disposed. Entries too large for the budget are not retained. Track edits invalidate all cached LODs in affected chunks, advance unaffected entries to the current revision and prevent stale in-flight results from entering the cache. World teardown clears the cache. `TerrainCacheChecks` verifies LOD/unload reuse without job submission, resource limits, edit invalidation and contour visibility. The browser has the corresponding `src/terrain/cache.ts` cache and world integration, covered by `scripts/test-terrain-cache.mjs`.

Terrain now uses `scripts/terrain/TerrainSampler.cs` for double-precision noise, heights and palette calculation, and `TerrainBackend.cs` for complete chunk jobs. `TerrainWorld.cs` submits one request per chunk. Sampling and scheduling now stay in C# throughout. Up to three .NET worker tasks run concurrently (matching the browser's CPU-based limit); completed buffers are polled and uploaded on the main thread. Scene teardown cancels outstanding jobs and joins workers.

The loader now follows the browser's radius of ten chunks, altitude-aware 128/64/32/16 tessellation and nearby-first priorities, refilling workers every frame rather than every 200 ms. Distant chunks skip vegetation generation. Tree meshes reproduce the original broadleaf/conifer geometry and LODs; all 256 menu tree transforms and leaf colors match browser fixtures.

Natural tree spawning now ports `src/terrain/vegetation.ts` through `scripts/terrain/Vegetation.cs`: seeded 8 m global cells, jitter, forest patches, shore/treeline/slope exclusions, altitude-dependent species, dimensions and yaw. Workers return the original seven-float records without reconstructing or approximating tree sizes. Near 128/64 terrain LODs use identical placement rules; 32/16 tiles omit trees as in the browser. Source fixtures verify all float values for 12,007 trees across 24 chunks, four settings combinations, Unicode seeds, negative coordinates and distant coordinates. Regenerate with `node scripts/export-godot-vegetation-fixtures.mjs`. Parallel chunk tests also verify preservation of those records on upload. Terrain rendering parity is unfinished.

Tree clearance now uses the original earthwork field: two-metre arc samples follow the pitched ballast underside; 2H:1V shoulders end at their first intersection with natural ground. Trees are excluded within 8 m of the path or within 4 m beyond the nearest shoulder. `EarthworkField.cs` retains the original BVH query/tie rules, width interpolation and cut/fill height calculation. `TrackTreeClearance.cs` waits 400 ms after edits, prepares immutable snapshots on a .NET worker and discards superseded results. Unchanged track paths reuse prepared widths. Loaded tree batches are filtered against the latest field, and newly streamed chunks use that field immediately. Natural records are retained so moving/deleting tracks restores the original trees. Contour visibility survives batch replacement.

Refresh clearance fixtures with `node scripts/export-godot-clearance-fixtures.mjs`. Tests compare original arc samples, prepared shoulder widths, nearest queries and tree exclusions for flat, uneven and seeded ground, including crossings and degenerate path edges. Startup checks build, move and delete actual tracks and verify tree removal/restoration.

Cut/fill terrain now uses that same field. `TerrainEarthworks.cs` ports the browser's adaptive mesher: affected cells refine to 1 m, edge fans stitch unequal cells, exposed soil uses the original brown palette, and boundary skirts hide neighboring LOD gaps. `TerrainWorld.SetEarthworks` marks chunks affected by either the old or new field, preserving other loaded chunks. Rebuilds run in the existing terrain worker pool (up to three jobs); sampling, refinement, normals, colors and topology operate on immutable snapshots. Revisions prevent completed older jobs from replacing newer terrain. Mesh uploads remain on the main thread. Height queries for camera/track placement also follow current cuts/fills. Removing track rebuilds affected chunks from natural ground.

Regenerate adaptive-mesh fixtures with `node scripts/export-godot-earthwork-mesh-fixtures.mjs`. The terrain suite compares source mesh counts, a hash of every triangle, and sampled positions/normals/colors for cut/fill crossing a chunk boundary at three terrain LODs. Startup tests wait for actual rebuilds and check raised formation geometry, restoration of natural heights/tessellation, and tree clearance across edits.

`scripts/track/TrackGeometry.cs` ports the browser's 512-sample arc-length mapping, finite-difference tangents, upright frames and swept profiles. Rail heads are at curve Y=0; rails are 0.07 m wide and 0.15 m high, with gauge measured between inner faces. Ballast shoulders span 4 m at the base and 2.72 m at the top, from -0.65 m to -0.33 m. Sleepers measure gauge + 0.85 m by 0.18 m by 0.24 m and sit at -0.24 m, spaced by the original arc-length rules. Platforms retain the 3 m slab, caps and yellow edge strip. Menu and gameplay use the same `TrackVisual` objects and native materials.

Sleepers retain 64-instance batches, original ordering, draw-count LOD and hysteresis at 150/400/900/1600 m. Moving the camera updates visible instance counts without rebuilding mesh geometry. Edits rebuild changed segments only; affected segments use rails-only geometry while dragging, with full detail restored on release. Placement previews use the same swept rails and ballast with translucency.

Refresh geometry fixtures with `node scripts/export-godot-track-fixtures.mjs` and `node scripts/export-godot-tree-fixtures.mjs`. The core suite compares every swept vertex, normal and triangle against seven browser cases (straight, graded curve, translated coordinates, vertical, short, preview and rails-only), plus all sleeper matrices, counts and hysteresis transitions. Tree checks cover all five source meshes and the 256 original menu placements/colors. The startup suite verifies affected-only drag rendering, restoration, unchanged-track reuse and gauge-aware previews.

Run `godot --headless --path godot tests/runner.tscn -- --suite=terrain` for parallel completion, cancellation, chunk vertices/normals/colors/topology and all 441 LOD decisions against browser fixtures. Refresh these fixtures with `node scripts/export-godot-chunk-fixtures.mjs`. The `core` suite also validates the C# heights and UTF-16 seed hashes, including Unicode seeds.

Run `godot --headless --path godot tests/runner.tscn -- --suite=benchmark` for a repeatable CPU diagnostic. After the complete C# migration, 10,000 height samples took about 17-18 ms in Debug configuration on this development machine. Three workers produced 25 64-segment chunks in about 188 ms, with the first result at 25 ms. These figures exclude GPU uploads; they are not total-world load times or a cross-machine performance guarantee. The previous GDScript implementation took about 667 ms for the same 10,000 height samples.

For the current Chrome/C# comparison, run `node scripts/benchmark-terrain.mjs`, then `node scripts/benchmark-terrain.mjs --optimized`. The script benchmarks actual Chrome code, including the original browser terrain workers, sequentially against Godot; verifies workload checksums; records seven measurements after two warmups; and restores the normal development build after the optimized pass. See [the 2026-11-18 report](../docs/performance/terrain-2026-11-18.md) and its adjacent JSON for hardware, complete samples and limitations. This comparison includes current vegetation and adaptive cut/fill, superseding the older chunk diagnostic for current performance estimates. It excludes GPU uploads, rendering, scene updates and edit debounce, so it does not establish FPS or complete edit latency. The optimized pass changes C# compiler/JIT optimization in the development assembly; it is not a packaged export benchmark.

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

Segment actions use `TrackSegmentHoverMenu`: Straighten and Delete track stay above the arc-length midpoint, activate within 60 screen pixels, and require a second click with explicit confirmation colors. Clicking the rail selects a preview node and shows only Confirm split at that point; Escape or a world click cancels without modifying the track. Controls retain their target while hovered and hide during construction, dragging, station editing, modal windows and beyond 600 m. Run `--suite=hover` for focused node/segment interaction checks, or `compare-godot-menu.mjs --state=build --hover=segment` for a native visual fixture.

Station building now includes full-segment translucent platform previews, rail-height side selection, continuous screen-space segment picking, Build/Remove actions and red removal previews. Junction segments are rejected; consecutive segments and parallel groups share station IDs, and the second visual side does not create a second station. Parallel clearance accounts for two platforms once per node, including the meeting point of consecutive station segments. Clicking a platform opens a reusable station window with a live world viewport and per-run usable length (parallel tracks and visual sides are not summed). Removing the final platform closes its station window. `--suite=platform` checks these interactions; `--hover=platform` and `--hover=station` add native visual fixtures to build captures.

## C# verification tools

`tests/runner.tscn` launches `tests/TestRunner.cs`. Pass `-- --suite=core`, `menu`, `terrain`, `startup`, `hover`, `platform`, `grid`, `train`, `benchmark` or `capture`; `all` runs the four correctness/integration suites. Each run has a timeout and a nonzero exit code on failure. The startup suite exercises the actual C# scene, model imports, terrain worker lifecycle, track construction, station placement, parallel-node continuation and rendering rebuilds.

`tests/physics-fixtures.json` preserves 144 numerical cases captured from the previous GDScript physics helper before removal: realistic/HO, started/stopped, uphill/downhill/flat, several speeds and speed limits. It is a migration baseline, not evidence of full browser train-simulation integration.
