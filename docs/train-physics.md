# Train physics

The world, track and vehicle geometry always use metres at 1:1 scale. Physics mode is a per-game setup option. Start/stop commands do not move vehicles into or out of construction.

## Vehicle inputs

Catalog inputs use kilograms, watts, newtons and metres per second. These are nominal operating values, not a simulation of every gearing option or rebuild.

| Vehicle | Mass | Engine power | Starting traction | Speed limit |
| --- | ---: | ---: | ---: | ---: |
| Santa Fe F7A | 104,326 kg | 1,500 hp | 251,326 N | 102 mph |
| GE 809 adaptation | 127,006 kg | 3,900 hp | 304,704 N | 70 mph |
| Streamlined coach | 56,800 kg | None | None | 102 mph |
| Pleasure Dome | 75,000 kg | None | None | 102 mph |

Sources and scope:

- [EMD F7A/F7B manufacturer specification scan](https://static1.squarespace.com/static/624efffd8ef1885fd8ab5ca1/t/67f0fb0fc36f4c2cecfc6b0c/1758061382489/EMD-SPEC-LOCO-F7A-F7B-1948.pdf): 1,500 hp and approximately 230,000 lb loaded. [EMD's F7 traction/gearing table](https://heritagerail.org/wp-content/uploads/2022/09/EMD-TABLE-7-SERIES-SHORT-TIME-RATINGS.pdf) lists gearing-dependent limits through 102 mph. We select the passenger-speed envelope; the starting-force cap is a simplified adhesion estimate rather than a complete traction-motor curve for that gearing.
- [GE Series-8 handbook GEJ-6368, General Data](https://www.scribd.com/document/283488434/d8-hdbk): B39-8 class, 3,900 hp, 274,000–280,000 lb, starting force around 68,500–70,000 lbf, with 83:20 gearing giving 70 mph. We use a conservative 68,500 lbf cap and 280,000 lb mass. These are B39-8 class proxies for the experimental wide-cab No. 809 visual; the rebuilt prototype's exact operating configuration is not verified.
- The coach's 56.8 t is a representative estimate based on a [1951 Budd car drawing with a 125,200 lb weight](https://img1.wikia.nocookie.net/__cb20141104154859/obts/images/3/32/1951.2xx2~WPxx.AARx_1.435~0025.91_Budd.Coch~PCRC.PDF), not an exact Santa Fe coach measurement.
- The dome's 75 t is a representative estimate informed by [Alaska Railroad's 165,757 lb average for a Budd dome passenger coach](https://www.alaskarailroad.com/sites/default/files/procurement/25-74-213996_Rebuilt_NP_Budd_Dome_Pass_Coach_Trucks-Addendum_1_0.pdf). It is not a verified weight for the Pullman-Standard Pleasure Dome. Both passenger-car speed ratings are game assumptions to match the F7 passenger envelope. Passenger loading and fuel consumption are not yet modelled.

## Force model

Power at the rail is estimated as 82% of summed locomotive engine power. Traction is limited by both power divided by speed and the lower of each locomotive's starting-force rating and 25% adhesion weight. Every car adds mass; rotational inertia adds a 6% effective mass allowance. At a given speed, more unpowered cars reduce acceleration.

Resistance is a simplified Davis-style estimate in newtons: `0.0015 × mass × g + 0.00010968 × mass × speed + 4 × speed²`. The linear coefficient converts the Modified Davis term `0.01 lbf / (US ton · mph)` into SI, following [FRA passenger braking report, equation A14](https://railroads.fra.dot.gov/sites/fra.dot.gov/files/2023-09/PTC%20Braking%20EA.pdf). The previous `0.015` SI coefficient was about 137 times too large and substantially depressed loaded-train speeds even on level track; a regression test now checks level-track and uphill equilibrium speeds. The constant and aerodynamic terms remain simplified estimates rather than a complete equipment-specific Davis fit.

Grade force is `mass × g × sin(slope)`, with each vehicle's local track grade weighted by its mass. Service brakes provide an estimated `mass × 0.65` newtons. All cars are assumed to have working brakes, so their brake contribution grows with mass. A stopped command holds the train at zero speed. Efficiency, adhesion, constant/aerodynamic resistance and brake parameters remain tunable engineering approximations, not equipment-specific test measurements. No brake-pipe delay, thermal effects, slack, wheel slip, derailment or detailed diesel/traction-motor controls yet.

Curve limits follow `v = sqrt(lateralAcceleration × horizontalRadius)`. The realistic lateral allowance is a chosen comfort parameter of 0.65 m/s², with no cant. A numerical tangent/chord estimate measures radius; the occupied consist and braking approach are sampled every 5 m. The train brakes before tighter curves and stays constrained until its trailing vehicles clear them. Extremely short geometry features below the sampling interval are not resolved as a continuous analytical curvature maximum.

HO is the approved gameplay approximation: 9× acceleration/braking response, with model top speeds multiplied by `(60 km/h) / (102 mph)`. Power calculations use the equivalent realistic speed to retain the original power-curve shape. Lateral allowance is also multiplied by 9, giving three times the curve speed for a given radius before the lower model top-speed cap applies. This is a convenient miniature behavior preset, not an electromechanical simulation of an actual HO motor/controller. [NMRA defines HO as approximately 1:87.1](https://www.nmra.org/beginners-glossary); dimensions are not scaled in this game.

## Routing and safety

Each train has a seeded PRNG and a route of directed segment occurrences. Only gauge-compatible connections on the opposite side of a node are eligible. Forward and rear vehicles sample the same recorded itinerary; different visits to the same segment on a loop have distinct indices. Route state and PRNG state are included in undo snapshots. Distant history is trimmed while preserving the occupied length and nearby decisions.

Dead-end clearance includes the complete train length plus a two-metre margin. The service-braking envelope slows the train to rest, then changes its travel direction while preserving car order and facing. A corridor scan anticipates other trains, with a footprint check preventing overlapping movement. This is local collision avoidance, not signalling or a deadlock-resolving dispatcher. Conflicting trains can wait indefinitely. Invalid/deleted supporting track stops the train at its last valid pose. Emergency geometry/occupancy changes can require an immediate safety stop.

Motion uses substeps no longer than 1/60 s and caps elapsed catch-up at 0.1 s; hidden-tab time is not fast-forwarded. Undo restores the whole train-system snapshot, including moving trains, rather than rewinding the world clock or terrain edits. Start/stop toggles themselves are operating commands rather than construction undo entries.
