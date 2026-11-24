# F7A v5: fresh photo-referenced texture

Selected: **iterations/05-photo-reference**. Generated from scratch using
built-in imagegen, the user's two real Santa Fe No. 49 photographs and the v5
UV guide. No prior generated texture was supplied to this generation.
The exact prompt, selected texture, UV copy, guide, software seven-view review
and native Godot captures are saved in that iteration directory.

## History

- 01-adapted: prior v4 artwork edited for v5. Rejected by the user.
- 02-fresh: generated from the guide alone. Rejected: oversimplified ribbon
  and painted roof openings duplicating modeled fans.
- 03-fresh-warbonnet: generated again from the guide alone. Rejected: nose-top
  stripe had the wrong orientation.
- 04-fresh-nose: generated again from the guide alone. Better nose stripe,
  but generic artwork and an asymmetric roof band.
- 05-photo-reference: generated again using only the guide and user-provided
  real photographs. Retained: more faithful horizontal side ribbon, smaller
  spaced side lettering, elongated nose emblem, symmetric roof paint boundary.

Only iteration 01 edited an existing atlas. All subsequent images were fresh
generations. Earlier source files and v4 remain intact. Additional downloaded
research photos and their credits are listed in `.files/f7a/references/README.md`;
the selected generation used the user's attached No. 49 photos directly.

## Import and validation

Installed as F7A (`emd-f7a`) with unchanged gameplay specifications. The UVs use
the corrected v5 source geometry: 948 painted triangles, 5859 solid runtime
triangles, four wheelsets and 9.144 m bogie spacing. The converter omits 177
degenerate export triangles. Model dimensions are not rescaled.

The guide is 4096 square and the generated texture is 1254 square. The textured
COLLADA binds `f7a-v5-santa-fe.png`. Hardware keeps runtime solid materials.
Every painted runtime triangle matches an authored group; exported side UV
handedness passes on 92 triangles per side. Primary UV overlap report is zero.
Godot import checks pass; seven software and native views were captured.
Seam color measurements remain diagnostic because paint borders are intentional.

This improves the resemblance to the reference photos; it is not a guarantee
of exact historical artwork or pixel-perfect seams. Generated small details
and paint-boundary differences still warrant visual review.
