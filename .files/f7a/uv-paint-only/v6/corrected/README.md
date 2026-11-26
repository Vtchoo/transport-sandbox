# F7A v6 — corrected UVs and Santa Fe texture

Use **f7a-v6-santa-fe.png** with this folder's **f7a-v6-uv-map.json** or **f7a-v6-textured.glb**. The original v6 mapping has mirrored UV handedness and must not be paired with this texture.

- `f7a-v6-textured.glb`: corrected model with the selected texture embedded.
- `f7a-v6-uv.glb`: corrected model with the authoring guide embedded.
- `f7a-v6-uv-map.json`: corrected mapping object, with correction provenance.
- `f7a-v6-atlas.png` and `f7a-v6-uv-guide.svg`: regenerated 4096 × 4096 authoring guides.
- `f7a-v6-santa-fe.png`: AI-generated texture, actual resolution **1254 × 1254**.
- `f7a-v6-review.png`: seven geometry-based preview views of the selected texture.

The correction reflects V globally (`v = 1 - v`) and reflects the atlas vertically. Seam connectivity, island sizes, model geometry, component hierarchy, indices and normals are retained. All original exports in the parent folder are untouched. The GLBs use `KHR_texture_transform` to reconcile bottom-left authoring UVs with glTF image coordinates.

The initial texture was generated using the two supplied Santa Fe No. 49 photographs and the corrected guide. Iteration 02 corrected the cab roof stripe and roof paint boundary; iteration 03 restored continuous red side sills. Each iteration contains its texture, exact generation prompt and model review images. The lettering orientation is correct on both sides and the nose. AI-generated paint boundaries and lettering can still contain small irregularities; these are previews, not a claim of pixel-perfect historical reproduction.

The UV Workshop source and standalone HTML also include the handedness fix for future exports. Its 14 core checks and file-based browser integration pass. `python -B scripts/check-f7a-v6-uv.py` verifies all 948 mapped faces, 81 islands and 28 corrected UV accessors, including unchanged model geometry and embedded texture bytes.

Rebuild the correction with `python -B scripts/prepare-f7a-v6-uv.py`; add `--texture` to embed the selected Santa Fe texture. Review with:

```powershell
python -B scripts/review-f7a-texture.py --version v6 --source-version v5 --model .files/f7a/uv-paint-only/v6/corrected/f7a-v6-textured.glb --output .files/f7a/uv-paint-only/v6/corrected/iterations/03-sill-continuity
```

The game's installed F7A (`emd-f7a`) now uses this v6 mapping and texture. Rebuild with `node scripts/import-godot-f7a.mjs --version=v6`; it normalizes the unchanged v5 authored geometry into the runtime body/bogie hierarchy and transfers all 948 painted triangles from this mapping. See the parent folder's `godot-import-report.json` for hashes and dimensions, and `game-import-review/` for views of the actual game GLB.
