# Vehicle support points and articulation

Vehicle definitions and their existing recursive `sections` can optionally define `bodySupports` and `articulatedParts`. Vehicles without these fields retain the normal two-bogie placement behavior.

`sections` represent independently positioned rigid vehicle sections, such as a locomotive and tender. A component fixed to another component belongs under its scene node and shares its pose. A hinged component can use the generic part configuration below. Component count and number of independent poses therefore need not match.

```json
{
  "bodySupports": { "front": -0.5, "rear": -6.05 },
  "articulatedParts": [
    {
      "node": "motion_front_engine",
      "pivot": [0, 0, 0.35],
      "axleStations": [1.35, 3.2, 5.05, 6.9]
    },
    { "node": "motion_pilot", "station": 10.2 },
    { "node": "motion_trailing", "station": -10 }
  ]
}
```

All distances are metres. Longitudinal stations are relative to the containing section's authored origin; positive Z is forward. The body's support stations determine its rigid heading and retain its authored origin even when the support points are asymmetric. Without `bodySupports`, support stations default to half the existing `bogieCenters` distance in each direction.

- A part **with `pivot`** rotates around that body-local pivot. Its heading minimizes the combined positional error of the configured axle stations while preserving the hinge. Axles can lie ahead of, behind or on both sides of the pivot. Use a rail-level pivot/reference frame, and author the part along local +Z. This fits a rigid wheelbase to continuous track; it does not simulate suspension or wheel flanges.
- A part **without `pivot`** follows the sampled position and tangent at `station`, for independent truck poses.
- `node` identifies a unique node in the complete model. Use the existing `motion_` naming convention for independently moving geometry to participate in production instancing without baking it into the parent's static template.

The shared `VehicleArticulation` solver produces named part poses. Straight previews, world placement, render interpolation and origin shifts use the same configuration. The renderer enforces body-local hinge attachment after interpolation. Current hinges attach directly to their containing section's body; arbitrary chains of hinges between child parts are not yet represented by this schema.

The Big Boy's running assemblies are the leading truck, front driving engine, rear driving engine, trailing truck and tender. The boiler/cab shares a rigid pose with the rear engine, which is an explicit fixed child in the model. Its wheels determine the boiler's support alignment. The tender also has its own independently posed leading truck. Steam wheel and rod animation is a separate concern from articulated placement.

The native `big-boy` test suite also checks an unrelated synthetic chassis with different node names, asymmetric support stations, multiple hinges and wheels behind a pivot, ensuring the placement solver does not depend on the Big Boy ID or dimensions.

## Signed-distance running gear

`DistanceRunningGear` discovers mechanism metadata on model nodes. `PosedVehicleVisual` has no vehicle-ID condition for this animation. Models without these tags continue to use their existing `running_gear` animation clips.

- A rotating axle node defines `wheel_radius` in metres, optionally `wheel_phase` in turns, `wheel_axis` (default local +X) and `wheel_rest_rotation` (Euler radians, default zero). Geometry should be a child of this axle node; its origin is the rotation pivot. Radius and phase are cached when the visual is created, not read every frame.
- A rod node defines `rod_kind`: `coupling`, `connecting` or `crosshead`; `crank_origin` in parent-local metres; `driver_radius`; `crank_radius`; `rod_length`; and optional `crank_phase` in turns. A coupling rod's origin is its rest midpoint; connecting/crosshead origins identify the driving crank's axle centre. Left/right quartering and separate engines are expressed as phases, not inferred from names.
- The local slider axis is Z, at the crank-origin Y height; optional `slider_direction` is +1 or -1. A connecting rod mesh is centred on the node and has unit length along local Z; the animator sets its position, orientation and longitudinal scale. Group an inclined mechanism under an appropriately oriented parent.
- The model exporter must retain node metadata (Godot node metadata or a suitable glTF extras importer). Keep independent moving roots in the existing `motion_` instancing convention. Node names themselves are not used to choose animation behavior.

Animation consumes signed cumulative distance in metres. It freezes when distance is unchanged and retraces when reversing. The metadata phase is applied consistently to posed vehicles and packed authoring previews. Do not target the same node with both a baked animation clip and this procedural solver.
