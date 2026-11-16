# Passenger car SketchUp reference

Use the [shared authoring guide](sketchup-reference.md) and [bogie guide](bogie-sketchup-reference.md). Coaches, sleepers, diners, baggage cars, lounges and dome cars should be **separate vehicle definitions/assets when they are separate car types**. They can reuse underlying components and textures. Do not place an entire passenger train into a single rigid mesh.

## Body and running gear

Put the origin at railhead halfway between bogie stations, with SketchUp +green Y toward the designated front. Keep `body`, `bogie-front` and `bogie-rear` as siblings. Inside the body use `shell`, `roof`, `underframe`, `glazing`, `doors`, `interior`, `diaphragm-front`, `diaphragm-rear`, `coupler-front` and `coupler-rear` as applicable. Open platforms, steps and end railings belong to the body.

Measure the body shell separately from coupling-face-to-face length, and include full-width steps/handrails in clearance notes. Bogie centers are pivot spacing, not the distance between nearest axles. Use actual wheel diameter and bogie wheelbase. For two-axle fixed-wheelbase cars or shared Jacobs bogies, document the configuration: the current ordinary two-bogie placement needs adaptation.

Model a dome as fixed body geometry such as `dome-shell`, `dome-glazing` and `dome-interior`, not as another articulated section. Document its maximum height. Articulated passenger sets are different: each rigid car section and shared bogie needs an explicit assembly diagram.

## Windows, interior and color

Choose opaque tinted windows initially unless the interior is important from the normal camera. For visible interiors, model seats and partitions only where needed; reuse seat components and avoid unseen floor/ceiling layers. Keep any opening door separate, with its origin on the hinge or slide reference; door animation is future work.

For stainless cars use `metal-stainless` on exposed metal, with separate `paint-primary`/`paint-secondary` regions only where paint can change. Do not tint stainless along with a selectable stripe. Use fixed decal materials for railway names and car numbers. Fluting can use restrained geometry or a normal texture, rather than hundreds of separately materialized strips. Wooden cars can use a shared wood texture on selectable paint, provided its base is neutral enough to tint.

## Measurement sheet additions

Record empty/operating mass and the assumed passenger/baggage load, gauge, coupling faces and height, shell and clearance dimensions, bogie pivots, axle stations, wheel radii, car type, seating capacity and rated speed. The current game uses one fixed mass per vehicle; changing occupancy mass/capacity requires additional logic. Unpowered cars have zero power and tractive effort. Their speed rating is their own limit, not necessarily the locomotive's limit.
