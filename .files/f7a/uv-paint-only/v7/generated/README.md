# F7A v7 Santa Fe texture

- `f7a-v7-santa-fe.png`: selected Santa Fe Warbonnet texture, generated with the built-in image-generation tool using the user's two No. 49 photos and the geometry-derived color guide. Actual image resolution: 1254 × 1254.
- `f7a-v7-textured.glb`: standalone model with the texture embedded.
- `review/review-sheet.png`: seven views rendered from that textured GLB; all 948 painted triangles trace back to the authored geometry.
- `f7a-v7-uv-map.json`: unchanged copy of the user's v7 mapping, with 948 painted faces and 80 islands.
- `f7a-v7-atlas.png` / `f7a-v7-uv-guide.svg`: 4096-square geometry-derived authoring guides, colored by material role.
- `prompt.txt`: exact image-generation prompt.

The user's original v7 exports remain untouched. V7 already uses the corrected UV orientation: no reflection, repacking, geometry edits or seam changes were applied. The standalone GLB uses KHR_texture_transform to translate the bottom-left authoring UV convention into glTF image coordinates. AI-generated surface detail and paint shapes remain approximations of the reference livery.

The selected texture is also copied to `.files/f7a/f7a v7 santa fe.png`. The game's installed F7A remains v6; this task generated the v7 texture and preview only.

To rebuild the preview binding:

```powershell
python -B scripts/prepare-f7a-v6-uv.py --version=v7 --texture
python -B scripts/review-f7a-texture.py --version v7 --source-version v5 --model .files/f7a/uv-paint-only/v7/generated/f7a-v7-textured.glb --output .files/f7a/uv-paint-only/v7/generated/review
```
