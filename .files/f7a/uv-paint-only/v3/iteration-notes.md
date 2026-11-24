# F7A v3 texture review

V3 continues from the completed v2 UV layout and the generated Santa Fe texture.
The source geometry is a byte-identical copy of `f7a-v2.dae`; no SKP geometry was edited.
The game keeps catalog ID `emd-f7a`, so existing trains use the revised asset.

## 01 — Initial import

Imported the previously generated texture without rerunning UV generation.
Rendered the actual exported GLB from both sides, both front quarters, front, rear and roof.
Found detached cab-side yellow bars, a mismatched roof/body Warbonnet transition,
and pale island-edge artifacts. Both side texts read correctly; there were no swapped sides.

## 02 — Seam correction

Used the built-in image generator to remove detached cab stripes, taper the nose emblem
wings inside the nose island, and extend the red roof region to match the side transition.
Reimported and rendered the exported GLB and the native Godot vehicle.
The obvious stripe breaks improved, but the edit introduced an unwanted swirled surface pattern.

## 03 — Clean albedo (selected)

Used the built-in image generator to remove swirled patterns and baked rim shading,
and requested matching-color padding beyond island edges. Preserved the corrected stripes.
Reimported and reviewed seven orthographic/quarter views from the GLB and seven native Godot captures.
No obvious reversed text, swapped side panels or disconnected painted stripes were found in these views.

A red sliver visible above the rear roof outline was traced back to the forward roof
triangles, not the rear end or secondary UV islands. It is visible forward roof geometry;
changing the UV layout to hide it would misdiagnose the cause.

The continuous-color seam diagnostic is a review aid, not a pass/fail score: material
changes, panel borders, intended paint boundaries and texture grain produce differences too.
The broad roof/body edges improved from approximately 107–109 RGB-distance units in
the initial sampling to approximately 38–49 after the stripe correction. These figures
are not a claim of mathematically seamless mapping.

## Validation and limits

- All 950 valid paint triangles have finite, in-range UV coordinates.
- No unintended UV overlaps are reported. The original five views plus details remain intact.
- 5,859 solid-material triangles and all four wheelsets remain in the runtime model.
- 177 collapsed export triangles are omitted by the native converter.
- Dimensions, bogie pivots, physics, exhausts, catalog identity and audio are retained.
- Native C# build and F7A import/material/livery/instancing/horn checks pass.
- The selected texture is the supplied/generated 1,254-square image, not an upscaled 4K image.
- Metal grilles, fans and other unpainted parts retain their authored geometry and game materials;
  this pass does not add geometric detail to them. Specular highlights are rendering, not painted seams.
- Review is visual at the captured angles; it does not guarantee identical colors at every UV seam
  or under every lighting/view/distance combination.

## Files and reproduction

`f7a-v3-santa-fe.png` is the selected texture; the top-level authored source is
`../../f7a v3 santa fe.png`. `f7a-v3-textured.dae` references the selected texture.
The basecolor PNG remains the original authoring-color template.
`iterations/` contains texture snapshots, generator prompts, rendered views and seam diagnostics.
`f7a-v3-review.png` is the final GLB contact sheet; `f7a-v3-game-preview.png` is a native Godot capture.

Reimport: `node scripts/import-godot-f7a.mjs --version=v3`.
Inspect GLB: `python -B scripts/review-f7a-texture.py --version v3 --iteration review-name`.
Validate kit: `python -B scripts/check-f7a-uv.py --version v3`.
Native capture: run `tests/runner.tscn -- --suite=f7a-import --f7a-review=f7a-review-name`.
This saves native renders below `godot/.godot/f7a-review-name/`.

Only regenerate the UVs if geometry/group assignments change. The current review found
texture defects, not a UV layout defect requiring repacking, so the v2 coordinates were retained.
All image edits used the built-in image generator; exact prompts are beside the iteration snapshots.
