# Railway World

A real-scale terrain and track-editing foundation for a browser train simulation, built with Vite, TypeScript, and Three.js. One scene unit equals one metre. Start Game loads the world; Build track opens the spline editor. Train simulation comes later.

```sh
npm install
npm run dev
npm test
npm run build
```

Open Vite's printed local URL. On Windows PowerShell with restricted script execution, use `npm.cmd`. `npm run preview` serves the production build. Desktop keyboard and mouse are currently required.

## Exploring

The default **overlook camera** follows a terrain focus, like a city-building game:

- **WASD / arrows, left or right drag:** pan across the terrain.
- **Middle-mouse drag:** orbit and tilt around the focus. **Q / E:** rotate.
- **Scroll:** zoom between 50 and 3,500 m from the focus. Panning speed scales with zoom.
- **Shift:** pan faster.

Press **C**, use the camera toolbar button, or open **Settings → Gameplay → Camera mode** to switch to **free camera**. The preference is remembered locally and Settings is also available before starting a world. Opening Settings stops camera input without unloading terrain.

In free camera, **WASD / arrows** move horizontally, **E / Space, Q** rise / descend, and **Shift** moves five times faster. Click the landscape or “Click to look around” to capture the mouse; drag to look if capture is unavailable. **Scroll** adjusts base speed from 5 to 500 m/s. **Escape** releases the mouse or closes Settings. The camera stays above rendered terrain and sea level. Switching modes retains the current location, with a downward tilt enforced when returning to overlook.

**Main menu** unloads the world so you can change terrain settings.

**F3** toggles the debug overlay in the top left. Coordinates, altitude, camera speed/zoom, chunk counts, triangles, FPS, world seed, and generator details are hidden by default.

## Building track

The bottom-center action menu starts with **Build track**. The build window selects the gauge for new segments, defaulting to 1,435 mm. Presets include 610, 762, 1,000, 1,067, 1,435, 1,520, 1,524, 1,600, 1,668, and 1,676 mm. References for dimensions: [European Union Agency for Railways](https://www.era.europa.eu/system/files/2022-10/Annex%203%20-%20TSI%20INF.pdf), [UIC gauge systems](https://shop.uic.org/en/b-190-gauge-changeover-systems/9249-7471-gauge-changeover-systems-documentation-of-existing-automatic-gauge-changeover-systems.html), and [Indian Railways track guidance](https://nfr.indianrailways.gov.in/railwayboard/uploads/directorate/eff_res/camtech/Civil%20Engineering/SubjactWise/Induction%20Course%20of%20Track%20Maintainer%20-%20Part%20I%20Introduction%20-%20Aug%202024.pdf).

- Click terrain to place a first node. Hover to preview the curve; each subsequent click immediately adds a connected node and builds the next track segment. Placement continues from the latest endpoint, along the tangent away from its incoming track, until **Escape** is pressed.
- Endpoints snap to existing nodes. A translucent red preview warns about incompatible gauges; mismatched connections are rejected. Changing the dropdown affects new tracks only.
- Hover to either side of an existing node along its tangent. A **+** button appears in that direction and starts an extension or branch from the node.
- Hover near a segment's midpoint to reveal **Straighten**. Click once to arm it, then click the same button again to confirm. Endpoint positions and gauge stay fixed; the segment becomes a straight 3D line, and shared endpoint directions update connected tracks. Moving away, editing elsewhere, or pressing Escape cancels confirmation.
- Hover beside a node to reveal purple **Ⅱ+** buttons on its left and right. These create parallel nodes inheriting the source gauge. The editable game default is `gauge in metres + 2.6 m` center-to-center spacing (4.035 m for standard gauge), not a railway engineering clearance standard. Repeated clicks skip occupied default slots.
- During active placement, **Ⅱ+** connects the current track to the newly created parallel node and continues the chain from it. Gauge mismatches are rejected without creating a node.
- Node and segment action buttons appear only within 600 m of the camera. Moving farther away hides the buttons and cancels their pending confirmations; returning nearby enables them again on hover.
- Parallel nodes share 3D direction. Editing any member's tangent keeps that node fixed and repositions its partners on a horizontal lateral line at their stored offsets. Moving a member node translates its entire group. Drag a purple spacing handle laterally to change only that member's offset; this handle remains horizontal even with Shift. Parallel nodes remain after Escape, ready to connect or edit. Straightening a track updates its endpoint groups too; a segment directly joining members of the same group cannot be straightened while preserving their lateral alignment.
- Drag a gold/white node to reposition it. Selecting a node reveals cyan handles for every incident segment. Dragging a handle changes the shared 3D tangent direction while preserving the other handles' lengths. This gives geometric tangent continuity (G1) across connected tracks.
- During node or handle drags, only affected segments render just the rails, including tracks attached to moving parallel nodes. Unaffected track meshes retain their sleepers and ballast without being rebuilt. Full detail returns when the drag ends, including on release without movement or interruption.
- Hover near a node and click the red **×** beside its add-track button, then **✓** to confirm deletion. Its connected segments are removed and their terrain earthworks are updated; neighboring nodes and other parallel nodes remain. **Esc** cancels confirmation.
- Hover near a segment’s midpoint and click **Delete track** twice to remove only that segment, keeping both endpoint nodes and updating its earthworks. **Esc** cancels confirmation.
- Hover along a segment to preview a translucent node on the track. Click the preview to start a branch using the segment’s gauge and tangent, then place or snap to a second node to build the branch and confirm the split together. Until that connection succeeds, the original segment, nodes, and earthworks stay unchanged. **Esc**, **Done**, or changing gauge cancels the pending split. Successful splits preserve the original track’s exact 3D Bézier shape and continue placement from the branch endpoint. Split points must be at least 1 m from either endpoint.
- Hover a node with exactly two compatible tracks to reveal **⇄ Merge segments** beside **+**. Click, then confirm with **✓**, to replace the two segments with one and remove the middle node. The outer endpoints retain their positions, handle lengths, and directions; the resulting curve can change. Other parallel-group members stay grouped and in place, with the anchor rebased if needed. **Esc** cancels confirmation. Merging is unavailable at branches or when the outer nodes are already connected or less than 1 m apart.
- Drag nodes and handles normally to move on a horizontal plane at their current elevation. Hold **Shift** to move only vertically; changing Shift mid-drag switches the constraint without jumping.
- **Escape** stops the current chain while keeping all completed nodes and tracks; an isolated, unconnected first node is discarded. You can then edit nodes or start another chain. **Done** closes the tool while keeping built tracks visible. Right-drag and middle-drag retain overlook camera controls. In free-camera build mode, right-drag looks around and automatic mouse capture is disabled.

Rails use cubic 3D Bézier curves, with instanced sleepers and a 4 m wide ballast footprint. The lateral axis remains horizontal, preventing superelevation or unintended spline-frame roll. Each segment retains its own gauge; a shared node requires matching gauges throughout its connections. Branches share a centerline tangent; detailed turnout hardware and switching logic are not implemented.

Sleepers use distance-based detail in short batches along each segment: all nearby sleepers, then every second, fourth, and eighth sleeper, with none at long range. Nominal camera-distance thresholds are 150, 400, 900, and 1,600 m, with 10% hysteresis to prevent flickering. Rails and ballast remain visible. Camera movement changes instance draw counts without rebuilding track geometry; sleeper positions remain fixed.

The terrain is regenerated around the ballast underside with **2H:1V** open cuts or fill slopes. Each slope stops at its first intersection with natural ground. Modified terrain is adaptively refined to 1 m cells; edge fans stitch different cell sizes, with chunk skirts at LOD boundaries. Revisions reject obsolete worker results after edits. The build window shows when terrain updates are pending. Changes are recomputed from the original seeded terrain so moving a track restores its old earthworks. Earthwork slope reference: [Railway earthwork guidelines](https://rdso.indianrailways.gov.in/works/uploads/File/1%281%29.pdf).

Tracks are currently session-only: returning to the main menu or reloading discards them. World links share terrain settings, not the constructed railway. Earthworks are a heightfield approximation; bridges, tunnels, grade-separated earthwork intersections, engineering limits on curve radius/gradient, and undo are not implemented.

## Reproducible generation

Seed, mountain relief, landform scale, and generator version define a world. The menu remembers settings locally, and **Copy world link** shares seed, relief, and scale through URL parameters. Terrain is a pure function of absolute coordinates: chunk LOD, generation order, and worker timing do not change sampled heights. Terrain data regenerates from settings rather than being persisted.

`src/terrain/generator.ts` combines domain-warped gradient noise, continental elevation, connected ridge belts, foothills, fine relief, and an analytic meandering river corridor with a floodplain. Slope, moisture noise, elevation, and sea level control meadow, rock, sediment, and snow colors. The river and ocean share a flat sea-level water surface.

This is an independent implementation with [TerraForged](https://github.com/TerraForged/TerraForged) and [Tectonic](https://github.com/Apollounknowndev/tectonic) as visual and architectural references. No source or assets from either project are copied. It is not a port or an equivalent of either generator. Hydraulic erosion, watershed drainage, tributary networks, caves, vegetation, and physically simulated tectonics are not implemented. Increment `GENERATOR_VERSION` and update the regression test when intentionally changing landforms. Existing links assume the current generator; historical generator versions are not bundled.

## Streaming and rendering

- `generator.ts`: seeded sampling and mesh construction with typed arrays.
- `terrain.worker.ts`: up to three workers generate meshes off the main thread, transferring arrays without copying.
- `lod.ts`: 512 m chunks, prioritized by camera distance, with 4 / 8 / 16 / 32 m vertex spacing. Altitude also reduces detail. A 21 × 21 chunk window provides at least 5.12 km of terrain in each horizontal direction.
- `world.ts`: replaces meshes as LOD changes, rejects obsolete worker results, disposes unloaded geometry, and terminates workers when leaving the world. A floating origin keeps GPU geometry near the camera during long-distance travel. Shared edge samples and 180 m skirts conceal LOD junction gaps; LOD transitions currently switch discretely.
- `main.ts`: settings menu, camera input, atmospheric sky, fog, water, loading progress, and world telemetry. Keyboard movement waits at an ungenerated chunk boundary rather than entering a hole.
- `camera.ts`: overlook orbit, zoom, and ground-following pan; free flight; terrain clearance and mode transitions. Camera coordinates remain independent of the renderer's floating origin.
- `track/model.ts`: node/segment graph, shared tangents, independent handles, and gauge validation.
- `track/editor.ts`: screen-space picking, constrained dragging, directional start buttons, preview and terrain updates.
- `track/render.ts`: rail and ballast sweeps with upright frames and instanced sleepers.
- `track/earthworks.ts`: spatially indexed formation heights and cut/fill slope extents.

The world has no fixed gameplay boundary; only surrounding chunks remain in memory. Floating-point coordinate precision is finite, so this is not a mathematically infinite domain. Ground surface detail is procedural shading. Terrain is a heightfield, with no overhangs.

`npm test` checks seeded terrain, LOD and camera behavior, gauge enforcement, tangent continuity, independent handle lengths, upright rail frames, earthwork slopes, adaptive cut meshes, and modified chunk boundaries. `npm run build` checks TypeScript and bundles the workers.
