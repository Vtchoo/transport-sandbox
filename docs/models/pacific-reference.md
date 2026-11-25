# Pennsylvania Railroad K4s Pacific and 110P75 tender

Metre-scale Godot model based on the PRR K4s rather than an invented generic Pacific. One catalog item, `na-pacific-462`, contains engine and tender as independently posed rigid sections, using the same composition structure as the Consolidation. The locomotive carries 1737; the tender carries PENNSYLVANIA.

## Primary references

- K4s engineering elevations published in the 1922 Locomotive Cyclopedia of American Practice, public domain: https://commons.wikimedia.org/wiki/File:PRR-K4s-Drawing.jpg . Provides the locomotive envelope, axle stations, cab, boiler and running gear proportions.
- PRR Form 109-J, May 15, 1948, printed page 12 (PDF page 13): https://jbritton.pennsyrr.com/downloads/Form%20109%20-%20Classification%20and%20Description%20of%20Locomotives%20and%20Tenders/form_109j.pdf . K4s wheel diameters, wheelbases, cylinder dimensions, weight and starting effort. Printed page 45 (PDF page 46) specifies 110P75 tender length, truck centres, wheelbase, wheels, deck and loaded mass.
- PRR tracing E80467, K4s with earlier 110P70 tender: http://prr.railfan.net/diagrams/Steam/K4s_110P70-E80467.gif . Cross-check for class silhouette and arrangement; its earlier tender dimensions are not substituted for the 110P75 dimensions in Form 109-J.

## Dimensions

| Measurement | Prototype | Model metres |
|---|---:|---:|
| Engine overall length, period elevation | 48 ft 2 5/8 in | 14.697075 |
| Maximum height, stack | 15 ft | 4.572 |
| Cab roof width | 10 ft 3 in | 3.1242 |
| Driver diameter | 80 in | 2.032 |
| Driving axle spacing | 6 ft 11 in | 2.1082 |
| Driving wheelbase | 13 ft 10 in | 4.2164 |
| Total engine wheelbase | 36 ft 2 in | 11.0236 |
| Pilot axle spacing | 6 ft 10 in | 2.0828 |
| Pilot wheel diameter | 36 in | 0.9144 |
| Trailing wheel diameter | 50 in | 1.27 |
| Boiler centre above rail | 10 ft 1 in | 3.0734 |
| Smokebox diameter | 82 in | 2.0828 |
| Cylinder centre spread | 89 in | 2.2606 |
| Cylinder bore / stroke | 27 / 28 in | 0.6858 / 0.7112 |
| Tender length, chafing plate to rear coupler | 33 ft 7 1/2 in | 10.2489 |
| Tender truck centres | 20 ft | 6.096 |
| Tender truck wheelbase | 6 ft 5 in | 1.9558 |
| Tender wheel diameter | 36 in | 0.9144 |
| Tender deck plate above rail | 75 1/4 in | 1.91135 |

The tapered barrel, squared Belpaire firebox shoulders, arched cab roof, stack, domes, cylinder blocks, pilot and tender silhouette follow the reference elevations. Small pipes, fittings, tire thicknesses, cab glazing and coal are simplified game geometry. This is a dimensioned game model, not a manufacturing reconstruction. Tender upper outline and width follow the period tender family silhouette; the table fixes its chassis dimensions. Body and coupler gaps follow the existing train placement convention, so the pair's displayed total is not the original permanently coupled drawbar length.

## Integration and verification

Source: `godot/tools/PacificModels.cs`. Rebuild with `--suite=pacific-author`; verify and render with `--suite=pacific` on `tests/runner.tscn`. The catalog loads `na-pacific-462-composition-1435.tscn`; the two individual scenes remain authoring assets. Packed scenes use prefixed section body/bogie nodes and motion nodes supported by production instancing. Symmetric placement pivots include local truck geometry offsets to preserve the asymmetric prototype axle stations. The one-second running_gear animation has quartered drivers, coupling rods, main rods and slider crossheads. Phase uses the real 1.016 m driver radius; 28-inch piston stroke determines crank throw. Small truck wheels remain static, matching the existing vehicle convention.

The 1435 mm gauge composition adds and flips the engine and tender together. The tender is not a separate builder entry. Steam exhaust follows the stack. K4s working mass is 320,000 lb (145,149.56 kg); 110P75 loaded mass is 221,050 lb (100,269.73 kg); starting effort is 44,460 lbf (197,767.76 N), from Form 109-J. The 1.85 MW game power value is an estimate, and the 80 mph equipment speed is a gameplay setting; neither is a dimensional measurement or a claim about a particular historical test. Coal/water consumption is not simulated.
