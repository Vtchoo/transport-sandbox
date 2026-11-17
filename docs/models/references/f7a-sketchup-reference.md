# Santa Fe F7A — SketchUp modelling dimensions

Work at **1:1 scale in metres**. These are F7A dimensions, not the longer FP7.

For shared component names, selectable paint materials, reusable bogies and export checks, read the [SketchUp authoring guide](../sketchup-reference.md), [diesel guide](../diesel-sketchup-reference.md) and [bogie guide](../bogie-sketchup-reference.md). The dimensions below are specific to the F7A; the new guides establish conventions for future imported models.

## Prototype dimensions

The original [EMD F7 operating manual, General Data](https://users.fini.net/~bersano/english-anglais/EMD-F7.pdf) specifies the following. [Another scan of the same manual](https://lctm.info/Secciones/Trenes/LocoMes/GRR_F7/_media/f7-om.pdf) is available.

| Measurement | Metres | Original units |
| --- | ---: | --- |
| Length between coupler pulling faces | 15.4432 | 50 ft 8 in |
| Maximum width outside grab irons | 3.2512 | 10 ft 8 in |
| Height above rail, over horns | 4.55295 | 14 ft 11¼ in |
| Bogie pivot spacing | 9.1440 | 30 ft |
| Axle spacing within each bogie | 2.7432 | 9 ft |
| Wheel tread diameter, nominal/new | 1.0160 | 40 in |
| Wheel tread radius | 0.5080 | Derived from diameter |
| Track gauge | 1.4350 | Standard gauge; between inside rail faces |

The width includes grab irons; **do not make the whole body 3.2512 m wide**. Horn height is not roof height. These dimensions establish the envelope and running gear; they do not define the curved nose, roof sections, windows or Santa Fe-specific fittings. Use photographs or a dimensioned body drawing for those shapes. Horns and other fittings vary between units and dates.

## Pivots and axes for this game

In SketchUp use red X across the locomotive, green Y along it toward the nose, and blue Z up. Put the origin on railhead level, midway between the two bogie pivots. Keep the body and both bogies as separate named components: `body`, `bogie-front`, `bogie-rear`.

| Datum | SketchUp coordinates (X, Y, Z), metres |
| --- | --- |
| Body origin | (0, 0, 0) |
| Front bogie pivot, at rail-level datum | (0, +4.5720, 0) |
| Rear bogie pivot, at rail-level datum | (0, −4.5720, 0) |
| Front bogie axle centers | (0, +3.2004, 0.5080), (0, +5.9436, 0.5080) |
| Rear bogie axle centers | (0, −3.2004, 0.5080), (0, −5.9436, 0.5080) |

The game uses X across, Y up, Z toward the nose. One rotation that preserves handedness maps SketchUp (X, Y, Z) to game (−X, Z, Y). Check your exporter's existing axis conversion before applying another transform. The component origins above are rendering datums, not physical bearing heights. For each bogie component, place its own origin at its rail-level pivot and build the wheels at local height 0.508 m.

Gauge measures the inside rail faces, not the distance between wheel centers. The current wheels have 0.12 m tread width, tread centers at ±0.7775 m laterally, and flanges toward the centerline; these wheel/profile details are game approximations.

## Current procedural model, retained in the catalog

| Game parameter | Current value, metres | Meaning |
| --- | ---: | --- |
| `length` | 15.4432 | Placement/coupler envelope |
| `bodyLength` | 14.4000 | Approximate procedural shell length |
| `width` | 2.9972 | Approximate body/placement width, excludes prototype grab-iron envelope |
| `height` | 4.5000 | Approximate current model envelope |
| `bogieCenters` | 9.1440 | Bogie pivot spacing |
| Coupler locations | longitudinal ±7.7216 | Symmetric game placement convention |
| Coupler center height | 0.8800 | Current game modelling choice |

The shell dimensions and centered coupler convention are **not factory body measurements**. The real body's relationship to its truck centers needs a body drawing; do not infer it from these game approximations. A more accurate replacement may require updating the catalog envelope and coupler/pivot offsets.

For an imported replacement, prepare separate body/bogie components with applied scale and outward-facing normals. GLB/glTF remains a useful future target. The first implemented SketchUp import path uses static solid-color COLLADA for the Dash 9; see its [import workflow](dash-9-sketchup-reference.md#current-game-import-workflow). An F7A import still needs its own asset registration/adapter. Keep your source `.skp` as well as the export.
