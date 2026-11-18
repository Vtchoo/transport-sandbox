# Steam locomotive and tender SketchUp reference

Follow the [shared authoring guide](sketchup-reference.md). This is an authoring convention, not a universal set of steam-engine dimensions. Use drawings for the chosen class and wheel arrangement.

## Assembly

For a tender locomotive, build **engine and tender as independent articulated sections**, even if purchased as one catalog item. Each has its own origin, coupling measurements and running gear. Never weld the tender into the engine mesh: it must follow curves separately. The current Rio Grande item already uses two sections; inspect its [reference notes](rio-grande-reference.md) for the existing approximate model.

```text
vehicle-root
  section-engine
    body
      boiler
      smokebox
      firebox
      cab
      running-boards
      cylinders
      driving-frame
      driver-01-side-x-positive
      driver-01-side-x-negative
      coupling-rod-side-x-positive
      coupling-rod-side-x-negative
      connecting-rod-side-x-positive
      connecting-rod-side-x-negative
      crosshead-side-x-positive
      crosshead-side-x-negative
    truck-leading             (if fitted)
    truck-trailing            (if fitted)
  section-tender
    body
    bogie-front
    bogie-rear
```

Add numbered drivers/rods to match the actual mechanism. For a tank engine omit `section-tender` and document tank/fuel masses. Leading/trailing trucks are not interchangeable with a diesel's two main bogies. Articulated locomotives need further independently moving engine groups and a custom placement adapter.

## Datums and wheel contact

SketchUp green +Y points toward the smokebox/front; blue +Z is up. Put the engine origin at railhead on its centerline and document its longitudinal reference, preferably halfway between the first and last coupled driving axles. The two-bogie origin rule does not directly describe fixed-frame drivers. Record every driving, leading and trailing axle coordinate, wheel rolling radius, and any truck swivel pivot independently.

For the tender, use the midpoint of its bogie stations at railhead. Supply the engine/tender section offsets and coupling faces relative to each local origin. The aggregate catalog origin and offsets can then be chosen without moving the source meshes. Record whether the engine-to-tender connection uses a drawbar rather than a normal coupler.

## Animation-ready mechanism

Keep the fixed frame, boiler and cylinders stationary within `body`. Each driving wheel needs a separate component whose origin is at its axle center and whose red X axis lies on the axle. Geometry must rotate about that axle, not about the vehicle origin. For separately moving wheels use unique names for each axle and side; keep crankpins attached to their wheel.

Keep coupling rods, main connecting rods, crossheads and piston rods separate. Put a connecting rod origin at its driving crankpin in the reference pose; put its local green axis along the rod toward the crosshead and blue up in that pose. A coupling rod's reference origin can be its first connected crankpin. Crosshead and piston-rod origins belong on their slide axes. Document any tilted cylinders and rod-axis changes; do not force them horizontal just to fit the diagram.

Deliver a clearly labelled rest pose, with all joint coordinates measured. For conventional quartered two-cylinder engines, document the actual quartering/lead; 90 degrees is an initial convention only if the prototype is unknown. Do not silently bake arbitrary phase differences into left/right meshes. Include a side-view diagram showing crank centers, crank throw, main rod length, cylinder/slide centerline, and attachment points.

The current animation derives wheel rotation from signed distance / rolling radius, and solves a simple slider-crank plus coupling rods. It is a bespoke 2-8-0 rig, not an automatic steam solver. Named imports, valve gear, additional cylinders, articulated engines and baked animation clips require integration. Distance-driven motion should stop and reverse with the locomotive; do not author a permanently looping idle wheel animation.

## Gauge, smoke and materials

For different gauges, supply appropriate frames, wheel spacing, rods and cylinders where necessary. Do not scale the entire locomotive or stretch the boiler. A real conversion may alter more than the wheels. Label fictional gauge adaptations explicitly.

Record the chimney outlet position in engine body-local coordinates, facing upward, and its effective width. The exhaust system reads `exhaust.position`, with optional `kind: 'steam'` and `idleRate` (default 2.2 particles/second). Steam engines emit faint idle puffs and denser smoke under effort, including in menu scenes. Setting `idleRate: 0` disables idle puffs. Outlet width/direction and multiple outlets are authoring notes pending adapter support. Do not include particle puffs in the GLB.

Use `paint-primary` / `paint-secondary` only for selectable paint. Black boiler paint is a paint surface, not necessarily exposed metallic steel. Use `metal-steel` for rods/tires, `metal-dark` for exposed dark machinery, fixed materials for historic lettering, and a separate coal material. Keep coal load and tender water/fuel mass documented. Omit boiler internals unless visible.

## Measurement sheet additions

Supply wheel arrangement, all axle/pivot stations, wheel diameters, crank throw, rod lengths, cylinder orientation, mechanism rest pose and phase, engine mass, tender empty and operating mass, fuel/water capacities, tractive effort, a sourced power estimate and speed rating. State assumptions: steam power varies with operating conditions and is not determined by boiler appearance. Avoid double-counting tender mass when defining both sections and the purchased aggregate item.
