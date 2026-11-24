f7a-v4 - authored material groups, one 4096 x 4096 atlas

Paint f7a-v4-uv-basecolor.png. Put f7a-v4-uv-layout.png above it as a guide
layer and hide that layer before exporting your finished texture.
f7a-v4-uv-guide.png labels the groups and preserves your placeholder colors.

All paint and paint-* face materials are included, including bare paint.
SketchUp exported bracketed names as paint-_group_; these are recognized too.
Authored groups are assigned to five large recognizable views: left side,
right side, roof, front and rear. Doors stay in their actual body locations.
Group-to-view assignments are in scripts/f7a-v4-uv-layout.json and the report.
Only edge-on or occluded surfaces need a separate mapping. The small details
are packed together in one secondary area instead of many labeled panels.
LEFT SIDE: front at RIGHT. RIGHT SIDE AND ROOF: front at LEFT.
These natural outside views keep lettering upright and unmirrored on the model.
The five main views share 200 pixels per metre on the 4096-square sheet.
Sides and roof share a longitudinal datum. Small details use their own scale.
Side surfaces at different depths intentionally share the artwork so stripes
align across doors and the shell. Left/right artwork is independent.
Curved areas use projection, so checker size varies with surface slope.
Small unlabelled-magenta group paint patches are the bare paint material.

The UV-only DAE retains the authored group labels and original solid colors.
The textured DAE binds the selected f7a-v4-santa-fe.png only to paint faces.
The checker DAE/PNG and two-view preview demonstrate texture coverage.
Keep each DAE beside its PNG when opening in a modelling application.
Glass, metal, bogies and wheels retain their solid materials.
Only nondegenerate paint triangles receive UVs; zero-area export triangles
remain in the DAE as solid faces and are counted in the report.

The source SKP and earlier kits are unchanged. V4 is installed as F7A in Godot.
The previous v1 texture does not fit this layout. Runtime import strips group
labels after UV authoring; they are only needed in source files and the guide.

V4 unfolds the vehicle for AI painting: left side, roof, right side in aligned
rows; front below the roof/right-side nose end, rear below their tail end. Small parts have three
labeled sections (paint/steps, headlight-bottom, ladder), retaining authoring
colors to identify membership. The UV guide is a reference, not the final albedo.

Selected texture: iteration 05-fan-bleed, generated with built-in imagegen.
The guide is 4096 square; the generated texture is 1254 square, not native 4K.
Guide station markers are not part of the final texture. See iteration-notes.md.
