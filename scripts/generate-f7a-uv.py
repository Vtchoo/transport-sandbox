"""Generate the F7A paint-only authoring kit; does not modify the source or game."""
from pathlib import Path
import argparse
import runpy
import sys
import zipfile

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / '.tmp/uv-tools'))
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', default='v5')
parser.add_argument('--source', type=Path)
parser.add_argument('--output', type=Path)
parser.add_argument('--name')
options, extra = parser.parse_known_args()
source = options.source or ROOT / f'.files/f7a/f7a-{options.version}.dae'
output = options.output or ROOT / f'.files/f7a/uv-paint-only/{options.version}'
name = options.name or f'f7a-{options.version}'
if options.version != 'v1' and '--material-groups' not in extra: extra.append('--material-groups')
if int(options.version.removeprefix('v')) >= 4 and '--unfolded' not in extra: extra.append('--unfolded')
if options.version != 'v1' and '--group-layout' not in extra:
    extra.extend(['--group-layout', str(ROOT / f'scripts/f7a-{options.version}-uv-layout.json')])
sys.argv = ['generate-dash9-uv.py', '--source', str(source), '--output', str(output),
            '--name', name, '--paint-material', 'paint', '--layout', 'f7a', *extra]
runpy.run_path(str(ROOT / 'scripts/generate-dash9-uv.py'), run_name='__main__')
if '--inspect' in extra: sys.exit(0)
if options.version == 'v1':
    # Preserve the established legacy kit's instructions.
    readme = '''F7A v1 - simplified paint-only UV kit, 4096 x 4096

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
'''
else:
    readme = f'''{name} - authored material groups, one 4096 x 4096 atlas

Paint {name}-uv-basecolor.png. Put {name}-uv-layout.png above it as a guide
layer and hide that layer before exporting your finished texture.
{name}-uv-guide.png labels the groups and preserves your placeholder colors.

All paint and paint-* face materials are included, including bare paint.
SketchUp exported bracketed names as paint-_group_; these are recognized too.
Authored groups are assigned to five large recognizable views: left side,
right side, roof, front and rear. Doors stay in their actual body locations.
Group-to-view assignments are in scripts/f7a-{options.version}-uv-layout.json and the report.
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
'''
if '--unfolded' in extra:
    readme = readme.replace('LEFT views: front at right. RIGHT views: front at left. TOP: front at left.',
                            'LEFT SIDE: front at RIGHT. RIGHT SIDE AND ROOF: front at LEFT.\nThese natural outside views keep lettering upright and unmirrored on the model.')
    readme = readme.replace('Left and right share scale. Other views are scaled to fit their painting area.',
                            'The five main views share 200 pixels per metre on the 4096-square sheet.\nSides and roof share a longitudinal datum. Small details use their own scale.')
    readme += f'\n{options.version.upper()} unfolds the vehicle for AI painting: left side, roof, right side in aligned\nrows; front/rear below the roof/right-side nose/tail ends. Small parts have\nnamed sections retaining authoring colors to identify membership. The UV\nguide is a reference, not the final albedo.\n'
(output / 'README.txt').write_text(readme, encoding='utf-8')
with zipfile.ZipFile(output / f'{name}-uv-kit.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(output.iterdir()):
        if path.is_file() and path.suffix != '.zip': archive.write(path, path.name)
