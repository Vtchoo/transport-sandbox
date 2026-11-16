# Freight car SketchUp reference

Follow the [shared authoring guide](sketchup-reference.md) and [bogie guide](bogie-sketchup-reference.md). The naming, axes and metre scale also apply to vans, hoppers, tank cars, flats and service vehicles. Freight assets and cargo simulation have not been integrated yet.

## Components

For ordinary bogie stock use sibling `body`, `bogie-front` and `bogie-rear`, with a rail-level origin midway between pivots. Put `underframe`, `shell`, `brake-equipment`, `coupler-front`, `coupler-rear` and fixed fittings under `body`. Retain cargo as `load` under the body so it can eventually be swapped/hidden independently. Cargo should not contain duplicate running gear or change the vehicle origin.

For moving doors or hatch covers use uniquely named components with origins on their real hinges or slide axes. Keep a tank fixed relative to its frame. Doors, unloading mechanisms and varying load levels will need code; no name automatically animates them.

For fixed-axle two-wheelset stock, model the axle centers and frame accurately and state that it has no swiveling bogies. Do not invent bogie pivots to fit the current two-bogie renderer. Articulated well cars and shared trucks require a section/pivot drawing and placement adapter.

## Materials, envelope and physics

Use selectable paint slots for chosen frame/body regions and fixed materials for safety placards, numbers and logos. Reuse load materials across identical loads where possible. Model tie-downs and large structural members; use textures for small lettering, rivets and shallow corrugations.

Record shell/clearance/coupler dimensions separately, including loads which overhang a flatcar. Record tare mass, maximum gross mass, payload and speed rating with sources. The current physics has a fixed vehicle mass and no cargo loading system, so integration must choose a stated operating mass. Supported gauges must each map to an appropriate bogie; narrowing the gauge must not scale the load or whole body.
