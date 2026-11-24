# F7A v4 texture review

V4 keeps the existing source geometry and dimensions. The source DAE is a copy
of v3; this iteration changes UV organization and paint only. V3 remains intact.

The five main views share 200 pixels/metre on a 4096-square authoring guide.
Left side, roof and right side have matching rectangle widths and a common
longitudinal datum. The left side faces right; the roof and right side face
left. This preserves upright, unmirrored lettering in natural outside views.
Doors remain in place. Small parts occupy three named groups with their own
scale: steps/bare paint, lower headlight parts and ladders. Guide-only markers
show 2, 5, 8, 11 and 14 metres from the nose to help align paint boundaries.

All creative texture generation and corrections used built-in imagegen.
Each iteration directory retains its exact prompt, texture, UV copy and
software-rendered seven-view review:

1. **01-first-layout:** rejected. Both side views faced left, which mirrored
   lettering on the left side when mapped. Corrected the UV handedness.
2. **02-oriented-layout:** readable lettering on both sides and added louvers;
   the roof band needed better longitudinal alignment.
3. **03-station-alignment:** added guide station markers and adjusted the roof
   band; the edit also shifted the left-side paint boundary too far rearward.
4. **04-side-consistency:** corrected that side boundary. A generated black
   roof fan circle had shifted beside the actual modeled fan, creating a ghost.
5. **05-fan-bleed:** selected. Removed painted fan holes/slots and continued the
   surrounding paint under the actual 3D hardware. Imported into Godot and
   inspected both front quarters, both sides, nose, rear and roof.

The selected texture is `f7a-v4-santa-fe.png`, also saved outside the generated
kit as `.files/f7a/f7a v4 santa fe.png`. Its actual resolution is **1254×1254**;
the UV guide is **4096×4096**. Normalized UV coordinates support either size.
The textured COLLADA references the selected texture. The original basecolor
template retains authoring colors for future work.

## Validation

- Native Godot F7A import suite: PASS. Build: zero warnings/errors.
- Runtime painted triangles: 950; solid triangles: 5859; four wheelsets.
- Converter omitted 177 degenerate export triangles; dimensions/pivots unchanged.
- UV primary overlap check: zero. Source hash, COLLADA references, UV ranges,
  solid-material bindings and packaged file integrity are checked separately.
- Software review traces every painted runtime triangle to its authored group.
- Actual exported side UV handedness is asserted for both sides to catch
  mirrored lettering layouts. Results are in `uv-orientation.json`.
- Paired UV edge colors are recorded in `seams.json`; intentional paint borders
  mean these values are diagnostic, not an automatic pass/fail threshold.
- Native captures are in `iterations/05-fan-bleed/godot/`; the main game preview
  and software contact sheet are also at the kit root.

No obvious mirrored faces or displaced fan graphics remain in the inspected
views. This is a visual review, not a guarantee of pixel-perfect seams or a
historically exact livery. Small paint-edge differences remain possible. Metal
grilles, fans, glass and running gear retain the game's materials and geometry.

Rebuild import: `node scripts/import-godot-f7a.mjs --version=v4`.
Validate kit: `python -B scripts/check-f7a-uv.py --version v4`.
Rebuild UV templates only when needed; generation overwrites preview bindings.
