f7a-v2 - authored material groups, one 4096 x 4096 atlas

Paint f7a-v2-uv-basecolor.png. Put f7a-v2-uv-layout.png above it as a guide
layer and hide that layer before exporting your finished texture.
f7a-v2-uv-guide.png labels the groups and preserves your placeholder colors.

All paint and paint-* face materials are included, including bare paint.
SketchUp exported bracketed names as paint-_group_; these are recognized too.
Authored groups are assigned to five large recognizable views: left side,
right side, roof, front and rear. Doors stay in their actual body locations.
Group-to-view assignments are in scripts/f7a-v2-uv-layout.json and the report.
Only edge-on or occluded surfaces need a separate mapping. The small details
are packed together in one secondary area instead of many labeled panels.
LEFT views: front at right. RIGHT views: front at left. TOP: front at left.
Left and right share scale. Other views are scaled to fit their painting area.
Side surfaces at different depths intentionally share the artwork so stripes
align across doors and the shell. Left/right artwork is independent.
Curved areas use projection, so checker size varies with surface slope.
Small unlabelled-magenta group paint patches are the bare paint material.

The UV-only DAE retains the authored group labels and original solid colors.
The textured DAE binds the basecolor image only to selected paint faces.
The checker DAE/PNG and two-view preview demonstrate texture coverage.
Keep each DAE beside its PNG when opening in a modelling application.
Glass, metal, bogies and wheels retain their solid materials.
Only nondegenerate paint triangles receive UVs; zero-area export triangles
remain in the DAE as solid faces and are counted in the report.

The source SKP/DAE, v1 kit and running Godot model are unchanged.
The previous v1 texture does not fit this layout. Runtime import strips group
labels after UV authoring; they are only needed in source files and the guide.
