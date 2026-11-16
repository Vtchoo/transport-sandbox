# Bogie and wheelset SketchUp reference

Bogies (also called trucks) are reusable assets. Follow the [shared authoring guide](sketchup-reference.md) for materials and export. Build each distinct gauge/frame combination explicitly. A locomotive or car declares exactly which bogie it uses for each supported gauge; the game must reject unlisted gauges.

## Axes and rail-level pivot

In SketchUp, red X runs across the track, green Y along the bogie and blue Z up. Put the bogie component origin at **railhead level directly below its swivel center**, with X/Y centered on that pivot. The bearing/bolster mesh stays at its real height. An independent bogie source uses this as its model origin; an assembled vehicle places it at the vehicle's signed bogie station.

```text
bogie-root
  frame
    sideframe-side-x-positive
    sideframe-side-x-negative
    bolster
    suspension
    brake-equipment
    traction-equipment       (powered bogies only)
  wheelset-01
    axle
    wheel-side-x-positive
    wheel-side-x-negative
  wheelset-02
    axle
    wheel-side-x-positive
    wheel-side-x-negative
```

For three axles add `wheelset-03`. Number axle stations from the designated front (+green Y) toward the rear. Put each wheelset's component origin at the axle midpoint, at **rolling radius above railhead**, and at its measured longitudinal station. Its red axis is the rotation axis. Within a wheelset, each individual wheel origin may be on its own axle center for future independent animation. All rest axes stay aligned with the bogie axes.

A frame/bolster origin may share the bogie datum; suspension geometry stays at measured heights. Keep attached brakes, motors and shoes with the correct moving part. Detailed spring compression and suspension travel are not currently simulated. A visually detailed frame does not create additional physical suspension.

## Measurement example

For a symmetric standard-gauge bogie with **2.7432 m wheelbase** and **0.508 m rolling radius**, in SketchUp coordinates:

| Datum | X | Y | Z |
| --- | ---: | ---: | ---: |
| Rail-level swivel origin | 0 | 0 | 0 |
| Front axle midpoint | 0 | +1.3716 | 0.508 |
| Rear axle midpoint | 0 | -1.3716 | 0.508 |

These stations illustrate the F7A truck envelope; they are not dimensions for every bogie. An offset swivel requires individually measured axle stations rather than assuming symmetry. A standalone wheelset keeps its own source origin at the axle midpoint; place the assembly at rolling-radius height in the bogie source.

Gauge is the distance **between the inside rail faces**, not wheel center spacing, outside flange spacing or frame width. Use a sourced wheel-profile/back-to-back drawing when available. Flanges face inward and extend below tread contact level. Do not measure contact height from the flange tip.

The procedural game bogies approximate wheels with 0.12 m-wide treads whose centers are at `±(gauge / 2000 + 0.06)` metres, with inward flanges. At 1,435 mm this is ±0.7775 m; at 914 mm it is ±0.517 m. This is a game approximation, **not a manufacturing wheel-profile specification**. Reproduce it only when deliberately matching existing running gear. For a more accurate profile provide the measurements so rail clearance can be checked during integration.

## Reuse and gauge variants

Use IDs such as `blomberg-b-1435`, `passenger-2axle-1435` or `archbar-914`. Include axle arrangement/frame type when needed to distinguish variants. Deliver a standalone `.skp`/`.glb` for each reusable bogie and its measurement sheet. Also include assembled bogies in a vehicle export so the expected arrangement is visible; integration must choose the reusable bogie or the embedded one, never render both.

Use identical component definitions for matching front/rear trucks, with distinct instance names `bogie-front` and `bogie-rear`. If the rear unit is physically oriented differently, document its rest rotation. Do not assume a nonsymmetric truck looks correct simply rotated 180 degrees. Keep mirrored geometry free of negative scale in the export copy.

For multiple gauges, move wheels/axles and alter frame/bolster/brake/rod clearances as appropriate. Keep wheel radii and wheelbase unchanged unless the design calls for a change. Do not nonuniformly scale the whole bogie: that changes wheels, brakes and details together. The vehicle body remains full size. Explicitly label nonhistorical adaptations.

## Materials and animations

Use shared `metal-steel` for tires/axles, `metal-dark` for exposed dark frame metal, `rubber` for applicable hoses, and `paint-fixed-*` for painted frames. Use `paint-primary` only if frame color is intended to follow the vehicle's selectable color. Avoid unique materials on every wheel; repeated geometry/materials help batching.

Wheelset animation requires rotating about local red X in SketchUp, mapped to the final game axle axis. The expected angular displacement is signed travel distance divided by rolling radius. Wheelsets must rotate as a unit unless the prototype explicitly uses independent wheels. Current ordinary procedural bogie wheels are static; steam drivers have a separate custom motion rig. Imported bogie wheel rotation is an integration task.

## Delivery checks

- Record gauge, frame width, full clearance width, swivel/bolster datum, all axle stations, wheel radii, tread/profile dimensions, frame type, powered axles and mass accounting (usually already included in vehicle mass).
- Rotate the bogie about its own vertical origin. It should yaw in place while wheel heights stay unchanged. Rotate each wheelset about its axle; its axle center must remain still.
- Confirm flange orientation and tread contact on the intended rail model, both in the standalone bogie and assembled vehicle.
- Verify named moving parts survive export. Preserve hierarchy while simplifying fixed pieces/materials to reduce runtime batches.
- Record any asymmetric layout, brake clearance, pivot offset or third axle. Existing `BogieDefinition` stores gauge, wheelbase, wheel radius, frame width, powered state and a limited frame style; more detailed geometry/kinematics require a matching adapter.
