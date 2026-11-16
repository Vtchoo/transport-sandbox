# Electric locomotive SketchUp reference

Use the [shared authoring guide](sketchup-reference.md) and [bogie guide](bogie-sketchup-reference.md). Work in metres at full size, +green Y toward the designated front and +blue Z upward. A double-cab locomotive still needs a consistent front for model orientation and coupling.

## Components

Use sibling `body`, `bogie-front` and `bogie-rear` components. Inside `body`, separate `cab-front`, `cab-rear`, `roof-equipment`, `insulators`, `transformer`, `glazing`, `coupler-front` and `coupler-rear` where applicable. Keep traction motors and axleboxes with their bogies, body-mounted equipment with the body.

Keep each pantograph as `pantograph-front` or `pantograph-rear`, with a root origin at the center of its roof mounting base. Name articulated parts `arm-lower-01`, `arm-upper-01`, `link-01` and `contact-shoe`; place each component origin on its physical hinge. Its local red axis should follow the hinge pin, with the remaining axes forming a right-handed frame. Record the axes and rest angles rather than assuming all pantographs share one mechanism.

Provide one clearly identified rest pose (preferably lowered), plus a drawing or separate reference export showing the raised configuration. Supply lowered and raised heights, hinge positions, arm lengths and shoe travel. Do not combine overlapping raised/lowered assemblies in the runtime GLB. Pantograph animation and wire contact are not implemented.

For third-rail equipment, group shoes under the bogie or body that actually carries them. Mark their mounting pivots and contact height. They do not need pantograph components.

## Materials and scale

Use the shared selectable paint slots for the livery, fixed materials for logos, ceramic/insulator materials for roof insulators and exposed metal for conductor bars. Record which surfaces may change color. Do not model glowing electrical arcs or wires attached to the vehicle. Overhead lines, electrical supply restrictions, lights and sparks will need separate game systems.

Check roof equipment clearance as well as body height. Document both lowered and raised envelopes rather than putting pantograph raised height into a body-shell measurement. Bogies rotate about rail-level pivot datums; wheelsets rotate around their axle centers. Three-axle bogies and articulated bodies need explicit geometry/adapter support.

## Measurement sheet additions

Record operating mass, rated traction power and its measurement basis, starting tractive effort, maximum speed, driven axles, gauge, bogie/axle stations, pantograph type, contact height range and supply voltage/system. Electrical supply metadata is for future integration; the current physics has no electrification requirement or electric-specific catalog kind. Electric traction can later use the common mass/power/effort model, but adding the asset does not supply wiring gameplay.
