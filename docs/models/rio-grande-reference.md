# Rio Grande / Frateschi 6508 adaptation

The user-supplied box photograph is the visual reference: black/graphite 2-8-0, yellow cab and coal tender, yellow wooden baggage car, and two matching wooden passenger coaches with black clerestory roofs and open platforms. Add the passenger coach twice to recreate the pictured consist.

This is a full-size game adaptation, not a documented reconstruction of a particular historical D&RGW locomotive or car. All three catalog items support **914 mm** and **1,435 mm**. Body dimensions stay the same; drivers, leading wheels and arch-bar trucks match the track gauge. Three-foot gauge uses the game's rounded 914 mm value (three feet is 914.4 mm).

## Geometry

| Catalog item / section | Length over couplers | Maximum width | Height above rail | Support spacing |
| --- | ---: | ---: | ---: | ---: |
| Locomotive + tender, one catalog item | 18.30 m | 2.75 m | 3.95 m | independently articulated |
| Engine section | 11.30 m | 2.75 m | 3.95 m | 5.10 m |
| Tender section | 6.80 m | 2.70 m | 2.85 m | 4.10 m |
| Wooden baggage car | 15.20 m | 2.65 m | 3.85 m | 10.20 m |
| Wooden passenger coach | 15.20 m | 2.65 m | 3.85 m | 10.20 m |

Engine/tender centers are +3.50 m and −5.75 m relative to the item's center, leaving a 0.20 m drawbar gap on straight track. Eight 1.30 m spoked drivers are fixed to the engine frame, with one 0.70 m leading axle and no trailing axle. Tender and cars use two independent two-axle arch-bar trucks with 1.65 m wheelbase and 0.73 m wheels.

Engine wheel rotation follows accepted travel distance divided by wheel radius, including reversed vehicle orientation and backwards travel. All four coupled axles share crank phase; the two sides are quartered by 90°. Coupling rods remain attached to the crank pins, and constant-length connecting rods drive crossheads along fixed piston guides. The mechanism freezes at rest and its phase survives pickup/undo. Moving parts have independent instanced transforms, so batching does not freeze or duplicate the mechanism. Leading wheels rotate at their smaller radius. This is a kinematic visual mechanism, without detailed valve gear, wheel slip or suspension dynamics.

The pair is purchased, flipped, moved, picked up and restored by undo as one item. Its sections have independent track poses and instanced render transforms. The tender is internal to the locomotive and cannot be purchased separately. Geometry resides in `src/train/locomotives/` and `src/train/cars/`, with shared wooden-car construction in `rio-grande-body.ts`.

Placed engines emit chimney smoke in proportion to delivered traction effort in both physics modes. Acceleration and climbing produce stronger exhaust; stopping, braking and downhill coasting fade it. Exhaust follows the articulated engine's body transform, then drifts independently in world space and dissipates. Drafts, catalog previews and unpowered cars do not emit. A shared, bounded particle batch renders the smoke in the world and train viewport; this is a visual approximation rather than combustion simulation.

## Sources and assumptions

- [Frateschi's Consolidation description](https://frateschi.com.br/locomotivas/locomotiva-consolidation-frateschi) identifies its underlying prototype as Baldwin / EFCB No. 557 and lists 18.3 m overall length, 60,341 kg engine mass, 32,659 kg loaded tender mass and 14,542 kgf traction. These are **Brazilian prototype proxies**, not verified specifications for the Rio Grande paint scheme or gauge adaptations. The game counts the combined **93,200 kg** once and converts traction to approximately **142,608 N**.
- [Frateschi's set listing](https://frateschi.com.br/trem-eletrico) identifies set 6508 with one baggage car and two identical coaches. The supplied older package photo governs colors and details.
- Section dimensions, widths/heights, axle spacing and wooden-car dimensions are visual/engineering estimates. Steam power **700 kW**, speed limit **45 km/h**, loaded coach mass **24,000 kg**, and loaded baggage mass **20,000 kg** are tunable estimates; no matching historical performance sheet was verified.
- Both physics modes use the existing force/controller models. Boiler pressure, firing, coal/water consumption, valve-gear dynamics and a steam-specific power curve are not simulated.

The livery and road number are inspired by the supplied set. Neither gauge option certifies that this exact locomotive identity operated historically in that configuration.
