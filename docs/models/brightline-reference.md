# Brightline Florida equipment

Godot includes three independently selectable, standard-gauge catalog vehicles:

- `brightline-scb40`: Siemens SCB-40 Charger, yellow/silver Brightline Florida livery, unit 101.
- `brightline-venture-smart`: single-level Venture SMART coach, magenta vestibule accents, number 401.
- `brightline-venture-premium`: Venture PREMIUM coach, yellow vestibule accents, number 201.

These are original simplified exterior models inspired by Miami–Orlando equipment. The coaches use representative class colors rather than reproducing every wrap, fleet revision or seating layout. The yellow diagonal fields, gray cab canopy, silver body, plug doors, window bands and wordmarks are authored geometry. Doors, lamps, exhaust outlets and equipment are static details except for the game's existing diesel exhaust. There are no modeled interiors or animated doors. No reference photographs or third-party meshes are distributed.

Add two locomotives and four coaches for the Orlando-launch formation, selecting and flipping the rear locomotive outward. Cars remain separate so longer formations and mixed consists are possible. Coaches are unpowered. Both powered and unpowered bogies follow the rails through the existing placement system; authored body and bogie groups are named `body`, `bogie_0` and `bogie_1` for production instancing.

## Scale and physics

| Input | SCB-40 | Venture coaches |
| --- | ---: | ---: |
| Coupler-to-coupler length | 21.7932 m | 25.908 m |
| Nominal body width | 3.048 m | 3.2004 m |
| Roof height | 4.2672 m (model estimate) | 4.2672 m |
| Bogie-center spacing | 12.8 m (estimate) | 18.1356 m |
| Wheel diameter | 1.1176 m | 0.9144 m (estimate) |
| Mass | 120,000 kg | 50,802 kg (gameplay estimate) |
| Engine power | 2,982,800 W (nominal 4,000 hp) | None |
| Starting traction | 290,000 N (estimate) | None |
| Equipment speed rating | 55.88 m/s (125 mph) | 55.88 m/s |

Small handrails, door frames and lettering project beyond nominal body width. Geometry is in metres without runtime rescaling. Mechanical sections, nose profiles, vents, exhaust placement and bogie castings are visual approximations, not engineering drawings. The SCB-40 roof envelope is photo-derived; the NTSB table's reported 12.5 ft height is not used as the complete roof-equipment envelope. SMART and PREMIUM share the same estimated loaded mass. Game physics applies its standard power/adhesion/resistance rules; this is not a measured Brightline traction curve.

Primary references:

- [Siemens: Orlando expansion trainsets](https://www.mobility.siemens.com/us/en/company/newsroom/press-releases/brightline-and-siemens-mobility-showcase-newest-trainsets-for-orlando-expansion.html): two Charger locomotives and four Venture coaches, plus the manufacturer's exterior photo.
- [Siemens intercity passenger coach sheet](https://preview.thenewsmarket.com/Previews/SIMS/DocumentAssets/345224.pdf): 85 ft length, 10 ft 6 in width, 14 ft height, 59 ft 6 in truck centers and 125 mph rating.
- [NTSB technical reconstruction report, Table 2](https://data.ntsb.gov/Docket/Document/docBLOB?FileExtension=pdf&FileName=%21Delray+Beach+Technical+Reconstruction+Factual+Report-Rel.pdf&ID=16640126): SCB-40 identification, 264,556 lb mass, 71.5 ft length, 10 ft width, 44 in wheels and 9.8 ft truck wheelbase.
- [Brightline: Miami–Orlando service launch](https://www.gobrightline.com/press-room/2023/brightline-history-brightline-opens-between-orlando-and-miami): Florida service context.

## Authoring and validation

The Godot-only authoring source is `godot/tools/BrightlineModels.cs`. It generates native packed scenes under `godot/assets/vehicles/`; the main catalog JSON and SVG thumbnails register the three items. Font triangles are baked when authoring, leaving no dynamic TextMesh or external font dependency in the assets. The web project is unchanged.

From the repository root, substituting your .NET Godot executable:

```powershell
dotnet build "godot/Transport Sandbox.csproj"
& C:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path godot tests/runner.tscn -- --suite=brightline-author
& C:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe --path godot --resolution 1280x720 tests/runner.tscn -- --suite=brightline
```

The second command regenerates the models; the third verifies dimensions, railhead alignment, glazing, eight wheels, static lettering, a bounded triangle count, production instancing, independent formation assembly, rear-cab reversal and equipment speed. A graphical run saves a review image to the ignored `godot/.godot/brightline-review.png`.
