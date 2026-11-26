# UV Workshop

Local, manual UV authoring tool for Transport Sandbox. Double-click
**[uv.html](uv.html)** to open it directly in your browser. No server, installation
or internet connection is needed. The HTML includes Three.js, styling and the
unwrap worker; model files stay on your computer. Drag files onto any part of
the editor or use Open model.

For development, edit `app.mjs`, `core.mjs`, `worker.mjs`, `uv.css` and
`uv.template.html`, then regenerate the standalone file:

```powershell
node .tools/uv/build.mjs
```

The build uses the repository's existing Three.js/esbuild dependencies under
`web/node_modules`; those dependencies are not required to use the finished HTML.

## Workflow

1. **Open model** or drop it on the 3D view. Supported: DAE/COLLADA, OBJ, glTF,
   GLB and STL. Select the `.bin` and images alongside a multi-file glTF.
   COLLADA component transforms and model units are honored by the loader.
   OBJ loads geometry; MTL-based material libraries are not parsed in this version.
2. Select the faces to paint. Material checkboxes select/deselect all faces with
   that material. Left-click toggles a triangle; Shift-click adds and Alt-click
   removes. Double-click selects the connected surface bounded by seams; Alt
   double-click removes it. This is triangle-level editing, not editable CAD faces.
3. In **Seams (E)**, click near an edge to toggle its seam. Orange edges are cuts.
   Shift adds and Alt removes. **Mark sharp edges** proposes seams at the chosen
   angle, optionally at material boundaries, and at open/non-manifold borders.
   Seams are only marked on edges touching the current face selection.
4. If separately exported vertices/edges coincide, use **Collapse coincident
   edges** with an appropriate tolerance in imported model units. This welds the
   unwrap topology across meshes while preserving all geometry positions and
   source faces. It clears existing seams; re-mark them afterward. It does not
   simplify the polygon mesh or dissolve its triangulation.
5. **Generate atlas** solves a least-squares conformal map per seam-connected
   island, aligns its main direction and packs all islands at a common texel
   density with pixel padding. Planar projection is available as an alternative.
   The calculation runs in a cancellable background worker.
6. Inspect checker distortion and warnings. Click an island to highlight its
   faces in 3D. Drag empty atlas space to box-select: **left to right** uses a
   solid blue box and selects only fully enclosed islands; **right to left**
   uses a dashed green box and selects any island whose triangles touch it.
   Vertical drag direction does not matter. Shift adds to the selection;
   Ctrl/Cmd removes. These modifiers also let you start a box over an island.
   Drag a selected island to move the entire selection, or rotate the selected
   group by 90 degrees. Repack restores
   automatic positions. **Snap** is enabled by default: edges and centers align
   with other islands and the atlas; adjacent islands snap with the packer's
   two-padding gutter. Cyan guides show each alignment within 8 screen pixels.
   Hold **Alt** while dragging to bypass snapping, or uncheck Snap. Guides are
   inspection overlays and are not baked into textures or exports.
   Manual moves can overlap islands: warnings report
   overlapping bounding boxes or islands outside the atlas.
7. Export the white **Atlas PNG** or **Guide SVG** for painting, plus **Mapping
   JSON** and/or **UV model GLB**. Load your painted image to check it on the mesh.
   Choose the white template style before exporting if you want an uncolored
   painting base. PNG exports omit the inspection highlight; SVG is an outline.
   The internal triangle-edge checkbox also applies to exported guides. If a
   painted image is loaded, PNG export saves that image at the atlas resolution;
   SVG always remains a separate line guide.
8. **Save project** preserves the model triangles, selection, seams and editable
   island layout in JSON. Keep the painted image separately. Reopened projects
   reconstruct world-space meshes; their GLB exports have flattened hierarchy.
   Export directly from the original model session to preserve component pivots
   and local transforms. The mapping JSON still retains original face addresses.

Camera: right-drag orbit, middle-drag pan, wheel zoom, Home to fit. F selects
faces, E selects seams, Ctrl/Cmd+Z undoes selection/seam/topology/island edits.

## Mapping object

`*-uv-map.json` has `format: "transport-sandbox-uv"`, version 1.

```js
{
  source: "f7a-v5.dae",
  uvOrigin: "bottom-left",
  atlas: { width: 4096, height: 4096, padding: 16, pixelsPerUnit: 200 },
  faces: [{
    id: 0,
    mesh: 0,           // mesh index in the Three.js loader's traversal order
    meshName: "body", // descriptive; not assumed unique
    triangle: 0,      // original geometry's triangle index
    material: 0,
    p: [[0,0,0], [1,0,0], [0,1,0]], // imported world-space corner positions
    selected: true,
    uv: [[0.1,0.2], [0.2,0.2], [0.1,0.3]] // same corner order as p
  }],
  islands: [{ id: 0, faceIds: [0], rect: { x: 16, y: 16, w: 200, h: 200, scale: 200 } }]
}
```

Unmapped faces have `uv: null`. Split shared vertices at seam corners when
applying the mapping: a single original vertex can need several UVs. For an
image-space consumer use `pixelX = u * width`, `pixelY = (1-v) * height`.
`rect` uses a top-left pixel origin. JSON also contains source materials,
selected topology tolerance and seam edge IDs. Edge IDs are local to the
recorded topology construction; use the source triangle addresses and corner
positions to apply UVs in another importer. The GLB exporter already performs
corner splitting and keeps unselected faces' original UVs/materials.

## Limits

This is an initial hard-surface authoring tool, not a replacement for Blender's
full UV workspace. It does not remesh, hide cuts, or guarantee nonoverlap within
a badly cut island. Closed surfaces, flipped triangles, collapsed islands and
solver convergence issues produce errors/warnings. Cut closed surfaces open and
provide more seams around high curvature. Bounding-box overlap checks are
conservative; they do not detect every internal triangle self-overlap. OBJ/STL
unit conventions are unspecified by those formats; imported coordinate values
are used unchanged. Large models can still make picking/selection expensive.

## Checks

```powershell
node --test .tools/uv/test-core.mjs
```

`browser-check.mjs` additionally tests direct `file://` opening, native browser
file drops onto the header and viewport, imports and downloads using Chrome CDP
at port 9336. It checks a demo box and the repository's F7A v5, and saves
screenshots/downloads under `.tmp/`. No HTTP server is used.
