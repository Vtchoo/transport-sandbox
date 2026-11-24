F7A v1 - simplified paint-only UV kit, 4096 x 4096

Paint f7a-v1-uv-basecolor.png. Add f7a-v1-uv-layout.png as a guide layer,
then hide that guide before saving your finished texture.
f7a-v1-uv-guide.png is the labeled reference; do not paint the guide itself.

LEFT SIDE: front at right. RIGHT SIDE: front at left.
Paint text normally on each side. The sides are separate and have equal scale.
ROOF: front at left. FRONT and REAR: separate end views.
Sloped/curved faces use orthographic projection; texel density varies with slope.
Secondary painted surfaces have independent islands in the lower-right area.
Some side surfaces at different depths intentionally share paint coordinates,
so stripes align across doors and the surrounding shell.

Only faces assigned to the authored material named exactly paint are textured.
Hardware, windows, bogies and wheels keep their original materials unless the
source explicitly marks a particular face as paint.
Source geometry, units, named body/bogie nodes and pivots are preserved;
component instances are expanded where independent face UVs are necessary.

f7a-v1-uv.dae: UV-only copy with original material colours.
f7a-v1-textured.dae: binds the basecolor PNG to painted faces.
f7a-v1-checker.dae: binds the checker PNG for inspecting coverage.
Keep each DAE and its referenced PNG together when opening them in a 3D editor.
f7a-v1-uv-preview.png shows both sides with a checker and stripe test.

The source SKP/DAE and the running Godot model are untouched.
Re-exporting changed geometry requires regenerating/checking its UV layout.
The painted texture is not included in your original SketchUp source automatically.
