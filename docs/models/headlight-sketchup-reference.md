# Headlights and marker lamps — SketchUp authoring guide

Use the [shared vehicle guide](sketchup-reference.md) for scale, vehicle axes and export. This guide defines an authoring convention for headlights, ditch lights, marker/tail lamps and numberboard illumination. **Lamp activation, emissive switching, beams and automatic light discovery are not implemented yet.** You can model and import the visible fixtures now.

## Build a fixture first

Work at **1:1 in metres**. Put the complete lamp component inside the vehicle's `body`, so it follows the body on curves. Keep it out of the bogie components unless it is actually mounted there.

Build each independently aimed fixture as a separate component:

```text
body
  headlight-front-01
    housing
    reflector                 (optional)
    lens
  headlight-front-02           (if a separate second fixture is fitted)
    housing
    lens
  headlight-rear-01
    housing
    lens
  ditch-light-front-x-positive
    housing
    lens
  ditch-light-front-x-negative
    housing
    lens
  marker-rear-x-positive
    housing
    lens
  marker-rear-x-negative
    housing
    lens
```

This is a menu of possible fixtures, not a required equipment list. Model only the lamps fitted to the chosen prototype and date. Two lamps in one shared housing can use one fixture component containing `lens-01` and `lens-02`; record both lens centers if they need separate light sources. Numberboards should be separate `numberboard-front-01` / `numberboard-rear-01` components, rather than treated as headlight beams.

Use meaningful **instance names and component-definition names** in SketchUp. Keep independently controlled lens surfaces in separate groups/materials. Repeated housings can share a component definition; use Make Unique when material assignment or geometry must differ. Names above describe the intended future adapter, not functioning switches today.

## Origin and aiming axes

Give each lamp its own local coordinate system:

| Lamp-local SketchUp datum | Convention |
| --- | --- |
| Origin | Center of the visible lens, on its outward surface |
| Green **+Y** | Optical axis, pointing **out of the lamp** toward the illuminated track |
| Blue **+Z** | Up relative to the upright vehicle |
| Red **X** | Across the fixture; completes the right-handed axes |
| Lens plane | Local X/Z plane at Y = 0 |
| Housing interior | Behind the lens, toward local **−Y** |

The lamp origin is at the lamp's actual mounting height. Unlike a bogie pivot datum, it does **not** belong at railhead level.

```text
Side section through one fixture

             local +Z (up)
                    ↑
    housing         │ lens center / origin
   [ reflector ]    ● ─────────────→ local +Y (aim)
        behind      │
   local −Y         │ lens lies in the X/Z plane
```

Use **Change Axes** on the fixture component to place its origin and axes while leaving its geometry in place. For a rear lamp, rotate the local orientation so green +Y points outward from the rear. Keep blue up; do not mirror the fixture with a negative scale just to reverse its aim. A lamp on a sloping nose can still point horizontally: aim its component axes according to the lens/optics, and shape the mounting bracket around the sloping body.

Side suffixes `x-positive` and `x-negative` refer to the **vehicle's SketchUp X coordinate**, not the lamp's rotated local axes. Front/rear names refer to the vehicle's designated ends, independently of whether the current train is moving forward or backward.

The current Dash 9 export has its named front toward source **negative green Y**. For that source, the front lamp's local green axis should aim toward source −Y; the rear lamp should aim toward source +Y. A component rotation supplies that orientation. The importer already determines vehicle forward from the named bogies. Do not rotate the whole vehicle solely to mount a light, and do not hard-code game-axis coordinates into the SketchUp source.

## Geometry you actually need

1. Establish the **lens center and diameter** from the chosen drawing/photo. Then establish housing depth, recess and the spacing to any second lamp. Do not use a generic lamp diameter as a verified Dash 9 dimension.
2. Draw the circular lens in the local X/Z plane. A 12–24 segment circle is a reasonable initial modelling choice; use more only when close views show an obvious polygonal outline. These are performance suggestions, not prototype specifications.
3. Add a short housing, bezel and mounting bracket. For a recessed fixture, model the visible opening and rim so the lamp does not appear painted on a flat body panel. Prevent the body shell from covering the lens.
4. Use a single opaque lens face initially, or a shallow lens solid if its rim is visible. Keep the visible face normal pointing outward. Remove duplicate coplanar faces.
5. Model a reflector/interior only if a transparent lens will reveal it. Avoid hidden bulb filaments and deep internal detail for a normal overview camera.

For the selected [ATSF Dash 9 drawing](references/dash-9-sketchup-reference.md#selected-drawing-and-corrected-bogie-dimensions), measure the fixture that is actually shown; do not copy the nose/headlight arrangement of another Dash 9 order. At the original 1:18 pixel convention, a measured lens diameter of `p` pixels corresponds to `p × 0.00635 m`. As an arithmetic example only, 28 px would mean 0.1778 m; **28 px is not a measured dimension for your fixture**. Use another view for housing depth, which a side view cannot establish on a forward-facing lamp.

## Lens materials and independent controls

Use shared body paint on the housing if it should follow the livery. Give lens faces dedicated, clearly named materials. These must be separate from body paint and from glazing materials.

| Material name example | Intended role |
| --- | --- |
| `paint-primary` or `paint-fixed-*` | Painted housing / bracket |
| `metal-steel` | Unpainted bezel or reflector |
| `lamp-headlight-front-white` | Front headlight lenses switched together |
| `lamp-headlight-rear-white` | Rear headlight lenses switched separately from the front |
| `lamp-ditch-front-x-positive-white` | One independently controlled front ditch lamp |
| `lamp-ditch-front-x-negative-white` | The other front ditch lamp |
| `lamp-marker-rear-red` | Rear red markers switched together |
| `lamp-numberboard-front-white` | Numberboard illuminated surface |

These are more specific versions of the shared guide's proposed `lamp-white` / `lamp-red` roles. Keep their meanings in the measurement sheet. Material names are retained by the current solid-color importer, but **none currently creates emissive output or a Three.js light**.

Fixtures with identical appearance and identical switching behavior can share a lens material. Fixtures that must switch independently need distinct material slots or explicit future per-fixture control. For example, alternating ditch lights must not be merged into one undifferentiated lens material. Front and rear markers also need distinct roles even if both are red. This preserves control information for later integration and gives the converter separate material groups; it does not implement flashing now.

Keep all lens materials **opaque and solid-color for the current COLLADA workflow**. Choose a representative lens color; an opaque white lens is a modelling appearance, not an active headlight. Player paint selection must not recolor light-output materials. A later adapter can assign dim/off and emissive/on appearances while retaining your housing geometry.

## The mesh, glow and beam are different things

Prepare the physical fixture in SketchUp. The game will need to add the remaining effects:

| Part | Where it belongs | Purpose |
| --- | --- | --- |
| Housing and lens geometry | SketchUp asset | Visible physical fixture |
| Emissive lens material | Runtime material control | Makes the lens appear bright |
| Bloom / flare / glow sprite | Optional runtime rendering | Appearance around a bright source |
| Spotlight or other light source | Runtime light system | Illuminates terrain, track and nearby vehicles |
| Visible beam through mist | Optional atmosphere effect | Scattering in the air |

An emissive material does not by itself illuminate the track in the current Three.js renderer. Do not draw a permanent opaque cone or a long white triangle in the model to stand in for a beam. Do not depend on a SketchUp renderer extension's lamp object exporting into this game. Supply lamp anchors and aiming information instead.

Distance-dependent effects, which end of a reversing train should show headlights/markers, switching rules and light budgets will be runtime decisions. Many repeated vehicle meshes can be instanced, but adding a shadow-casting spotlight for every fixture would still have a rendering cost. The asset needs clean attachment information rather than baked lighting effects.

## What to record with the export

Add a lamp table to the vehicle's `measurements.md`. For each independently aimed lens, record:

- Component path and role: headlight, ditch light, marker, numberboard, etc.
- Lens-center position **in vehicle/body coordinates**, in metres from the vehicle railhead origin.
- Outward aim as a **unit direction vector in the same vehicle coordinates**, plus which source axes are in use. For example `(0, +1, 0)` is forward along source green Y; `(0, −1, 0)` is the opposite direction. For the current Dash 9 source, the named front uses the latter.
- Lens diameter, housing depth and any recess/offset, with a source or an explicit estimate.
- Lens material name, desired emitted color and which fixtures switch together.
- Any proposed beam angle, range or intensity as a separate runtime setting with units and a source/estimate. A bright preview in SketchUp is not a calibrated intensity measurement.

Keep precision reasonable for the reference. A 1–2 px uncertainty in the selected original Trainiax image corresponds to roughly 6–13 mm; avoid presenting traced fixture dimensions as factory tolerances.

## Current importer and future attachment support

Keep fixtures under `body` in the `.skp` and `.dae`. Re-export and run **`npm run import:dash9`** to see their geometry/materials in the game.

The current converter bakes fixed child meshes into body-local positions. It only explicitly preserves the main `body`, `bogie-front` and `bogie-rear` semantics; it does **not** emit lamp origins, aim vectors or a lamp registry. Empty SketchUp helper components may disappear in export, so an empty component named `light-anchor` is insufficient. The measurement table and your editable component hierarchy are the source for a future attachment adapter.

You can use temporary guide lines to visualize each lamp's green aim axis while modelling. Keep those in a reference/helper group and omit that group from the final export. The runtime lamp anchor should be placed at or slightly in front of the outward lens surface during integration, with its exact offset chosen to avoid self-occlusion.

Before delivering: check each visible lens normal, material assignment, local origin, outward aim and component name. Confirm that rear fixtures aim rearward and that their placement follows the body rather than a bogie. Supply a front/side reference image and the lamp table with the asset.
