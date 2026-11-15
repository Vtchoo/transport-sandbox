# Railway World

A real-scale terrain foundation for a browser train simulation, built with Vite, TypeScript, and Three.js. One scene unit equals one metre. Start Game generates and loads the world. Track construction and train simulation come later.

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

The world has no fixed gameplay boundary; only surrounding chunks remain in memory. Floating-point coordinate precision is finite, so this is not a mathematically infinite domain. Ground surface detail is procedural shading. Terrain is a heightfield, with no overhangs.

`npm test` checks seed reproducibility, changed settings, mixed-LOD border alignment at positive/negative/distant coordinates, mesh integrity, bounded LOD allocation, altitude-based detail reduction, and regional elevation variety. `npm run build` checks TypeScript and bundles the workers.
