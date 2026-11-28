# Union Pacific Big Boy — procedural coal-fired model

The game entry **Union Pacific Big Boy · Coal**, ID `up-big-boy`, depicts the original 4000-class appearance, numbered 4000. It is one purchased locomotive-and-tender item and supports **1,435 mm** track only. It does not depict the restored, oil-fired 4014 or its auxiliary water tenders.

## Sources and SI specifications

- [Union Pacific — articulation and flexible steam connections](https://www.up.com/news/heritage/steam-update-08-25-2017): front engine swivels beneath the boiler; the rear engine remains fixed to it.
- [Union Pacific — independent engines and exhaust](https://www.up.com/news/heritage/steam-update-1-29-2019): two engine units, four cylinders and separate exhaust cadences. This is simple expansion, rather than compound expansion.
- [Union Pacific — 4014 specification sheet](https://www.up.com/content/dam/upcom/migration/uprr/newsinfo/documents/up-pdf-nativedocs/pdf-up-media-4014-fact-sheet.pdf): wheel arrangement, standard gauge, 1.7272 m driving-wheel diameter and approximately 602 kN nominal tractive effort. The current restoration sheet is used for dimensional context, not its oil-firing equipment.
- [National Park Service — historic Big Boy 4012](https://www.nps.gov/stea/learn/historyculture/history-of-the-union-pacific-big-boy-no-4012.htm): historic loaded engine/tender mass of approximately **539,548 kg**, approximately **4.62 MW** published power, coal tender and water capacity. Steam output varies with speed and operating conditions: the game's scalar power is an approximation.

The catalog uses a roughly **40.48 m** coupling envelope, 3.4 m width and 4.98 m maximum height; the generated geometry measures approximately 40.48 × 3.38 × 4.93 m and meets rail level. Driver radius is **0.8636 m**. The **35.7632 m/s** equipment speed ceiling represents a design rating, not a claim that loaded freight operations routinely reached it. All runtime physics remain SI. Loaded tender mass is included once in the purchased aggregate; section mass values are zero to avoid double counting. Coal and water are presently visual/operating-mass assumptions, not consumable resources.

## Model and rig

`godot/tools/BigBoyModel.cs` is the native procedural authoring source. It packs geometry into `godot/assets/vehicles/up-big-boy-1435.tscn`, so normal gameplay loads a resource instead of regenerating hundreds of primitives.

- The locomotive has 12 axles in a **4-8-8-4** arrangement: two pilot axles, four front driving axles, four rear driving axles and two trailing axles.
- The tender has seven axles: two in its front truck, five beneath the centipede frame. Its coal bunker is open and contains faceted coal.
- The engine section's body includes the boiler, cab and fixed rear engine. `motion_front_engine` is anchored at a single hinge on that body and points toward a forward track sample. The pilot and trailing trucks follow their own track samples. The tender body/front truck are independently posed using the existing section/bogie system.
- The running assemblies are the leading truck, front engine, rear engine, trailing truck and tender. The boiler/cab has a fixed connection to the distinct `rear_engine` component, so they share a rigid pose. `motion_front_engine` swivels at its hinge. The tender's own leading truck is an additional subassembly.
- Body alignment uses the rear engine's outer driving axles (5.55 m apart), rather than fictitious support points across both engine units. The front engine heading minimizes the combined error of its four driving-axle track samples while preserving the fixed hinge. The tight-curve regression uses a 30 m radius and measures every powered axle's offset from the track centreline; checking that parts rotate independently alone would miss this fault. Rigid axles between the support points still approximate the continuous curve.
- The shared `DistanceRunningGear` drives wheel rotation and coupling/main rods from signed travel, using metadata baked into the model. Front and rear engines have different crank phases; each engine's left/right mechanisms are quartered. Stopping freezes the rig; reversing reverses it. Wheels and rods remain independently instanced parts.
- Interpolated poses and floating-origin rebases include articulation targets. Standard train placement, orders, routes and save records use the unchanged vehicle catalog mechanism.
- Placement is data-driven through `bodySupports` and `articulatedParts`; there is no locomotive-specific articulation flag or Big Boy geometry in the shared placement solver. Wheel and rod animation is also selected by metadata, without vehicle-ID branches. See [the generic configuration](../vehicle-articulation.md). Only the procedural authoring source contains this locomotive's dimensions and component names.
- Steam exhaust uses the two common smokebox outlets. Existing test steam recordings and whistle are reused. Configured exhaust phases create two four-beat-per-revolution rhythms, with gain compensation for the additional pulses.

This is a simplified procedural model. Individual detail dimensions, exact linkage proportions, suspension travel, valve gear and steam plumbing are visual approximations, not a fabrication drawing. Articulation follows sampled continuous track with a fixed hinge; there is no full wheel/rail contact, wheel-slip, boiler-pressure or fuel simulation. The two phases remain offset rather than simulating load-dependent drift or independent slipping. The tender's five rear axles follow its rigid frame; axle lateral play is not simulated.

## Rebuilding and checks

```powershell
node scripts/register-big-boy.mjs
dotnet build 'godot/Transport Sandbox.csproj' --no-restore
& 'D:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path godot tests/runner.tscn -- --suite=big-boy-author
& 'D:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path godot tests/runner.tscn -- --suite=big-boy
```

The native checks cover gauge, single-item/section identity, axle counts, mass accounting, hinge attachment on straight/curved/reversed placement, rod animation, stop/reverse behavior, eight exhaust beats, articulation interpolation/rebasing, dimensions, instancing and whistle availability. Running the same suite with a renderer writes `.godot/big-boy-preview.png` and `.godot/big-boy-curve.png`.
