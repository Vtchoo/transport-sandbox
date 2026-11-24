# Diesel locomotive SketchUp reference

Follow the [shared authoring guide](sketchup-reference.md) for axes, scale, materials, export and delivery, and the [headlight guide](headlight-sketchup-reference.md) for lamp components and aiming. This guide applies to hood units and cab units; prototype measurements come from the particular locomotive's drawings/manual, not from a generic diesel size.

## Build order

1. Establish coupling faces, bogie pivots, axle stations and railhead height from references. Model a bounding shell first and check these dimensions before adding details.
2. Build the body, cab, underframe, fuel tank, steps and walkways inside `body`. Use separate fixed components such as `cab`, `hood-long`, `hood-short`, `fuel-tank`, `radiator`, `fan-grilles`, `handrails` and `glazing` where applicable. A cab-unit nose belongs to the body, not the front bogie.
3. Add `bogie-front` and `bogie-rear` using the [bogie guide](bogie-sketchup-reference.md). Keep motors, brakes and axleboxes with their bogie; keep the fuel tank with the body. For six-axle locomotives, put three wheelsets inside each bogie and record all axle stations. The current procedural bogie builder makes two-axle trucks; imported three-axle geometry needs its own adapter.
4. Detail the silhouette: nose/cab profile, radiator overhangs, roof fans, horns, headlights, pilots and handrails. Avoid modelling radiator fins and grilles as thousands of separate solids.

## Front, pivots and optional animation

Designated front is SketchUp +green Y, regardless of which cab/hood is longest. State whether your prototype normally operates short-hood or long-hood forward. Body origin is halfway between bogie stations at railhead height. Name couplers `coupler-front` / `coupler-rear`; record their actual coordinates and heights.

Optional moving details should remain separate: `fan-01` with origin at its shaft, `wheelset-01` at its axle, `door-cab-front` at its hinge. Fan rotation, doors and automatic light integration remain future work; no automatic behavior follows from these names. For exhaust, record each outlet in body-local coordinates and its direction; do not model smoke as permanent geometry.

The vehicle definition can optionally configure one exhaust outlet. Coordinates use game axes (X across, Y up, +Z forward), in body-local metres:

```ts
exhaust: {
  position: [0, 4.34, 0.2], // Example: the current F7 model's roof outlet.
  kind: 'diesel',
  idleRate: 1.2, // Particle emissions per second at idle.
},
```

Omit `exhaust` to disable emissions for that model. Omit `idleRate` for the diesel default of 1.2 particles/second, or set it to zero to emit only under effort. Working engines automatically produce more exhaust. The effect is shared by placed trains and menu trains; catalog models and placement ghosts do not emit. Component-name discovery, multiple outlets and configurable outlet directions are not implemented yet.

For the livery UV workflow, apply the exact material **`paint` directly to paintable faces** on the hood, cab, nose, roof, ends and equipment cabinets. Keep glass, handrails, running gear and small fittings in their normal materials. Follow the [shared `paint` material directive](sketchup-reference.md#mark-faces-for-livery-uv-mapping-with-paint), including export checks. The separate proposed color-customization workflow uses `paint-primary`, `paint-secondary` and `paint-accent`; these do not replace the `paint` UV-selection marker. Fixed logos and unpainted metal can retain their own materials.

## Measurement sheet additions

Record locomotive operating mass, rated engine power, whether the supplied power is engine or rail output, starting tractive effort, maximum speed/gearing, fuel load, powered axles and wheel diameter. The game currently uses one power value and one tractive-effort value, not a full engine/transmission map. Do not infer speed from wheel arrangement. Do not bake HO speed/acceleration adjustments into specifications or model size.

For concrete dimensional examples use the [F7A cab reference](references/f7a/f7a-sketchup-reference.md), [separate F7B powered booster reference](references/f7b/f7b-sketchup-reference.md) and [Dash 9 reference](references/dash-9-sketchup-reference.md). They distinguish sourced dimensions, provisional datums and approximate model measurements. A more accurate model can require catalog/placement changes rather than matching an old approximation.
