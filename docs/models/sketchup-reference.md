# SketchUp vehicle authoring guide

Start here, then use the guide for your equipment:

- [Diesel locomotives](diesel-sketchup-reference.md)
- [Steam locomotives and tenders](steam-skecthup-reference.md)
- [Electric locomotives](electric-sketchup-reference.md)
- [Passenger cars](passenger-car-sketchup-reference.md)
- [Freight cars](freight-car-sketchup-reference.md)
- [Bogies and wheelsets](bogie-sketchup-reference.md)
- [F7A prototype dimensions](f7a-sketchup-reference.md)
- [Current Rio Grande model references](rio-grande-reference.md)

## What is implemented

The game currently builds vehicles in TypeScript. It has independent body/bogie poses, explicit gauge compatibility, permanently coupled articulated sections, instancing, and a custom steam wheel/rod animation. It does **not** load `.skp` or `.glb` vehicles, interpret the names below, select custom paint colors, or automatically select model LODs yet. These documents establish an authoring convention for that integration. Naming a component does not activate a feature.

Keep editable `.skp` sources. Deliver `.glb` as the preferred runtime asset, plus a measurement sheet. An imported vehicle will still need a catalog definition and a rendering adapter. We can integrate an initial exported model before you build a large fleet.

## Scale, axes, and origin

Model at **1:1 in metres**, including vehicles used in HO physics. HO changes behavior, never mesh scale. Set the SketchUp template to metres, use sufficient precision for millimetres, and disable length snapping if it interferes with dimensions.

| Coordinate | SketchUp | Game / intended final GLB |
| --- | --- | --- |
| Across vehicle | Red X | X |
| Forward, toward designated front | Green +Y | +Z |
| Up | Blue +Z | +Y |
| Railhead datum | Blue Z = 0 | Y = 0 |

Use the existing F7A convention: SketchUp `(x, y, z)` becomes game `(-x, z, y)`. This preserves handedness. It changes the sign of the transverse coordinate. For unambiguous side names use `side-x-positive` and `side-x-negative` in **SketchUp coordinates**; record the mapping on the measurement sheet. Do not depend on an ambiguous left/right label.

An exporter may already convert Z-up to Y-up, and may map forward to -Z. Inspect its output before applying another rotation. The final acceptance target is metres, Y-up, +Z-forward, without mirrored geometry. Export a separate 1 m calibration cube and an asymmetric front/side marker once to establish the pipeline; do not ship them with the vehicle.

For a conventional two-bogie vehicle, put the vehicle origin at railhead level, on the centerline, halfway between its bogie pivots. Put each bogie's component origin at its own rail-level pivot. This is a rendering datum, not the physical bearing's height. Geometry retains its actual height above the rail.

The current placement system assumes symmetric bogie stations at `±bogieCenters / 2` and a centered coupling envelope `±length / 2`. Record any asymmetric real vehicle rather than distorting it to fit; asymmetric placement would need code changes.

## Components and names

Use ASCII lowercase kebab-case names and a stable model ID, such as `emd-f7a-atsf`. Give important component **instances** unique names in SketchUp's Outliner; also give definitions meaningful names. Some exporters use definition names, some instance names. Verify the exported node names and include a hierarchy screenshot if names change.

```text
vehicle-root
  body
    shell
    underframe
    roof
    glazing
    fittings
    coupler-front
    coupler-rear
  bogie-front
    frame
    wheelset-01
    wheelset-02
  bogie-rear
    frame
    wheelset-01
    wheelset-02
```

Keep `body`, `bogie-front`, and `bogie-rear` as siblings: bogies must turn independently of the body. Fixed details can remain grouped by purpose inside `body`. A wheelset origin belongs on its axle center; see the bogie guide. Repeat child names only under different uniquely named parents. Keep component axes aligned with model axes at the rest pose, except deliberately oriented hinge components documented in the sheet.

Reuse component definitions for repeated windows, wheels, seats, fans, and identical bogies. Make a component unique before editing a genuinely different variant. Reuse in SketchUp helps authoring; the conversion must still preserve/share geometry appropriately for runtime instancing.

Do not leave a whole-vehicle scale transform to correct dimensions. Avoid negative-scale mirrored instances in the export: bake mirrored geometry in an export copy and fix normals, while retaining the editable source. Do not explode independent moving parts just to flatten the hierarchy.

## Materials and custom colors

Paint actual front faces, rather than relying on inherited group paint. Orient face fronts outward; inspect in Monochrome mode. A reversed face is not repaired by painting its back. Use the same named material wherever its appearance and customization role match.

The following are **proposed semantic material slots**, not existing automatic replacements:

| Material name | Intended use | Suggested metalness / roughness |
| --- | --- | --- |
| `paint-primary` | Main selectable body color | 0.0 / 0.45 |
| `paint-secondary` | Second selectable color | 0.0 / 0.45 |
| `paint-accent` | Selectable stripe or trim color | 0.0 / 0.45 |
| `paint-fixed-yellow` | Historical color which stays fixed | 0.0 / 0.45 |
| `metal-steel` | Bare steel, rods, wheel treads | 1.0 / 0.35 |
| `metal-stainless` | Unpainted passenger body | 1.0 / 0.30 |
| `metal-dark` | Dark exposed metal | 1.0 / 0.65 |
| `rubber` | Hoses and diaphragms | 0.0 / 0.90 |
| `glass-tinted` | Prefer opaque tinted windows initially | 0.0 / 0.15 |
| `interior` | Seats and visible interior surfaces | 0.0 / 0.80 |
| `decal-fixed` | Logos and lettering retaining their colors | 0.0 / 0.60 |
| `lamp-white`, `lamp-red` | Separate lamp lenses | 0.0 / 0.25 |

Values are starting points for runtime PBR setup, not mandatory SketchUp sliders. Painted metal uses a dielectric surface response; exposed metal uses metallic response. Use additional descriptive fixed materials when necessary, and list them in the sheet.

Assign distinct materials to recolorable regions **now**. Do not bake a multicolor livery into a single texture if its regions must be recolored separately. For textured selectable paint, use a neutral white/grayscale base-color texture so later tinting works. Put fixed logos/numbers in separate materials or decal meshes; tinted body paint must not tint them. Supply default colors as hex values in the sheet. If customization is undesired, use `paint-fixed-*` names instead.

Material names alone do not implement color selection. Future recoloring must preserve shared geometry while separating incompatible material/color batches; avoid giving every identical car unique cloned textures. A fixed historical scheme and a customizable scheme can be delivered as separate material assignments using the same geometry.

Use PNG/JPEG textures with packed, non-overlapping UVs where appropriate; start with one 1024 px atlas per vehicle and increase only where close views justify it. Keep source textures separately too. Avoid baking directional sunlight or strong shadows into paint. Use geometry or textures for panel details: SketchUp edges/styles are not reliable runtime surface detail. Keep decals slightly offset to avoid z-fighting. Opaque windows are cheapest; if transparent windows are important, model only visible interior surfaces and document that requirement. Lamp lens names do not currently create lighting.

## Geometry and performance

Prioritize silhouette, wheel spacing, window rhythm, and livery. Use textures for tiny bolts, vents, and distant lettering. Avoid hidden engine internals and fully detailed interiors behind opaque windows. Close visible surfaces; avoid coplanar duplicates. Smooth curved surfaces and preserve sharp panel edges.

Suggested initial budgets, including running gear, are 15–40k triangles for a diesel/electric, 10–25k for a passenger car, 5–20k for freight, and 30–70k for a detailed steam engine plus tender. These are planning targets, **not measured engine limits**. Start with 4–8 materials per vehicle where practical. Instancing reduces draw calls but does not remove triangle cost. Separate animated transforms and extra materials also add batches.

Optional future LOD exports: `model-id-lod0.glb`, `model-id-lod1.glb`, `model-id-lod2.glb`. Preserve coordinate conventions, pivots, attachment stations, silhouette and material roles in every level. Remove small fittings and interiors first. Deliver one good base model before investing in LODs; vehicle LOD switching is not implemented.

## Measurements and delivery

Deliver a folder containing:

```text
model-id/
  model-id.skp
  model-id.glb
  measurements.md
  hierarchy.png
  reference-front.png
  reference-side.png
  textures/             (editable originals, if used)
```

In `measurements.md`, record:

- Identity, designated front, prototype/year/livery, source links, and which details are estimated. Include permissions for third-party assets.
- Metres and axis mapping verified after export. Overall railhead height; shell length/width; full clearance width; coupling-face-to-face length. Distinguish shell dimensions from handrail/step envelopes.
- Front and rear coupler coordinates and heights, all bogie pivot stations, axle stations, wheel rolling radii, and gauge in millimetres. Use signed coordinates relative to the recorded origin.
- Supported gauges and the matching bogie/running-gear model for **each**; distinguish historical configurations from game adaptations.
- Empty and operating/loaded masses, power in kW, starting tractive effort in kN, and maximum speed in km/h, with sources or explicit estimates. State whether tender fuel/water and passengers/cargo are included. Runtime fields use kg, W, N, and m/s; convert once during integration.
- Material slots/default colors; movable parts and their pivot/axis; exhaust outlet position/direction; articulated sections and their own local datums.

The current `VehicleDefinition` stores length, body length, width, height, bogie centers, per-gauge bogie IDs, physics, optional exhaust and sections, plus `thumbnailSvg`. Many authoring measurements above will be integration notes until their runtime fields exist. A GLB or its filename does not determine physics automatically. Thumbnails currently remain SVG illustrations in the vehicle's TypeScript file; a model screenshot is a reference, not an automatic replacement.

## Export and acceptance checklist

1. Save the source, then make an export copy. Remove reference photos, dimensions, people, ground, tracks and calibration meshes. Purge unused components/materials in that copy.
2. Keep hierarchy and important names. Export only the intended vehicle assembly. Do not include a second copy of a reusable bogie beside the vehicle.
3. Where available, use **File → Export → 3D Model → GLTF Binary File (.glb)**. SketchUp version/edition determines availability. If absent, use a compatible glTF exporter or a converter which preserves hierarchy, materials and pivots. Record the tool/version used. See [SketchUp's GLTF instructions](https://help.sketchup.com/en/sketchup/working-gltf-files).
4. Prefer glTF 2.0 metallic-roughness materials, embedded textures and uncompressed geometry for the first integration. Do not introduce extension-dependent compression until the loader supports it. Three.js recommends glTF for runtime assets: [model loading guide](https://threejs.org/manual/pages/loading-3d-models.html).
5. Reopen the GLB in a glTF viewer or conversion tool. Check metre dimensions, Y-up/+Z-forward, front/side asymmetry, outward normals, materials, textures, and component pivots. A rotated wrapper is acceptable if documented and normalized during integration. Do not rotate each child independently to fix a root-axis conversion.
6. Inspect wheel contact at railhead, pivot spacing, coupling clearances and gauge. Rotate each bogie around its own vertical pivot: the body and opposite bogie must remain still. Rotate a wheelset about its axle: it must not orbit around the vehicle.
7. Supply the model and sheet for an in-game adapter. Final checks in the game must include curved track, reversing, both gauges if supported, translucent placement, instancing, and the train/catalog viewports.

Before modelling the whole vehicle, export a crude body with two named bogies and one named paint slot. Checking this small sample catches exporter naming/axis/pivot issues early.
