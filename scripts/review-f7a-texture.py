"""Render the actual imported GLB from seven views and measure paired painted UV seams."""
from pathlib import Path
import argparse
import json
import struct
import sys
import runpy
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.tmp/uv-tools'))
import numpy as np
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', default='v5')
parser.add_argument('--iteration', default='01-initial')
parser.add_argument('--model', type=Path)
parser.add_argument('--texture', type=Path)
parser.add_argument('--source-version', help='Authored DAE kit used to trace unchanged geometry')
parser.add_argument('--output', type=Path)
args = parser.parse_args()
source_version = args.source_version or args.version
output = args.output or ROOT / f'.files/f7a/uv-paint-only/{args.version}/iterations/{args.iteration}'
output.mkdir(parents=True, exist_ok=True)
data = (args.model or ROOT / 'godot/assets/vehicles/emd-f7a-1435.glb').read_bytes()
length = struct.unpack_from('<I', data, 12)[0]
gltf = json.loads(data[20:20+length])
binary = data[28+length:]
texture = np.asarray(Image.open(args.texture or ROOT / f'.files/f7a/f7a {args.version} santa fe.png').convert('RGB'))

def accessor(index):
    a = gltf['accessors'][index]; v = gltf['bufferViews'][a['bufferView']]
    types = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}
    count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[a['type']]
    dtype=np.dtype(types[a['componentType']])
    return np.ndarray((a['count'],count),dtype=dtype,buffer=binary,
                      offset=v.get('byteOffset',0)+a.get('byteOffset',0),
                      strides=(v.get('byteStride',count*dtype.itemsize),dtype.itemsize)).copy()

triangles, texcoords, colors, painted = [], [], [], []
def visit(index, parent):
    node = gltf['nodes'][index]
    local = np.array(node.get('matrix', np.eye(4).T.flatten())).reshape(4, 4).T
    if 'matrix' not in node:
        x,y,z,w=node.get('rotation',[0,0,0,1])
        local[:3,:3]=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
                              [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
                              [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])@np.diag(node.get('scale',[1,1,1]))
        local[:3,3]=node.get('translation',[0,0,0])
    world = parent @ local
    if 'mesh' in node:
        for primitive in gltf['meshes'][node['mesh']]['primitives']:
            if primitive.get('mode',4) != 4: continue
            a = primitive['attributes']; p = accessor(a['POSITION'])
            p = (np.column_stack((p, np.ones(len(p)))) @ world.T)[:, :3]
            indices = accessor(primitive['indices']).flatten() if 'indices' in primitive else np.arange(len(p))
            t = p[indices].reshape(-1, 3, 3)
            m = gltf['materials'][primitive['material']]
            info=m.get('pbrMetallicRoughness',{}).get('baseColorTexture')
            uses = 'TEXCOORD_0' in a and info is not None
            uv = accessor(a['TEXCOORD_0'])[indices].reshape(-1, 3, 2) if uses else np.zeros((len(t), 3, 2))
            if uses:
                transform_uv=info.get('extensions',{}).get('KHR_texture_transform',{})
                uv=uv*np.array(transform_uv.get('scale',[1,1]))
                angle=transform_uv.get('rotation',0);c,s=np.cos(angle),np.sin(angle)
                uv=uv@np.array([[c,s],[-s,c]])+np.array(transform_uv.get('offset',[0,0]))
            color = m['pbrMetallicRoughness'].get('baseColorFactor', [1, 1, 1, 1])[:3]
            color = {'metal-stainless': [.77, .79, .80], 'metal-dark': [.16, .18, .20], 'glass-tinted': [.10, .19, .24]}.get(m.get('name'), color)
            triangles.extend(t); texcoords.extend(uv); colors.extend([color]*len(t)); painted.extend([uses]*len(t))
    for child in node.get('children', []): visit(child, world)
for node in gltf['scenes'][gltf['scene']]['nodes']: visit(node, np.eye(4))
triangles, texcoords, colors, painted = map(np.asarray, (triangles, texcoords, colors, painted))

# Trace painted GLB triangles back to the UV copy's authored material groups.
helpers = runpy.run_path(str(ROOT/'scripts/generate-dash9-uv.py'))
Q, transform, positions = (helpers[k] for k in ('Q', 'transform', 'positions'))
dae = ET.parse(ROOT/f'.files/f7a/uv-paint-only/{source_version}/f7a-{source_version}-uv.dae').getroot()
unit = float(dae.find(f"{Q('asset')}/{Q('unit')}").get('meter', '1'))
geometries = {g.get('id'): g for g in dae.findall(f"{Q('library_geometries')}/{Q('geometry')}")}
names = {m.get('id'): m.get('name', '') for m in dae.findall(f"{Q('library_materials')}/{Q('material')}")}
def key(tri): return tuple(sorted(tuple(p) for p in np.round(tri, 4)))
groups = {}
def source_visit(node, parent):
    world = parent @ transform(node)
    for instance in node.findall(Q('instance_geometry')):
        mesh = geometries[instance.get('url')[1:]].find(Q('mesh'))
        bindings = {b.get('symbol'): b.get('target')[1:] for b in instance.findall(f"{Q('bind_material')}/{Q('technique_common')}/{Q('instance_material')}")}
        for primitive in mesh.findall(Q('triangles')):
            name = names.get(bindings.get(primitive.get('material')), '')
            if not name.startswith('Livery panels'): continue
            inputs = primitive.findall(Q('input')); stride = 1+max(int(i.get('offset', 0)) for i in inputs)
            indices = np.fromstring(primitive.find(Q('p')).text, sep=' ', dtype=int).reshape(-1, stride)
            vertex = next(i for i in inputs if i.get('semantic') == 'VERTEX')
            p = positions(mesh, vertex.get('source'))
            p = ((np.column_stack((p, np.ones(len(p)))) @ world.T)[:, :3]*unit)[:, [0, 2, 1]]*[1, 1, -1]
            for tri in p[indices[:, int(vertex.get('offset', 0))]].reshape(-1, 3, 3): groups[key(tri)] = name
    for child in node.findall(Q('node')): source_visit(child, world)
for scene in dae.findall(f"{Q('library_visual_scenes')}/{Q('visual_scene')}"):
    for node in scene.findall(Q('node')): source_visit(node, np.eye(4))
# Float32 GLB serialization can move coordinates across a four-decimal rounding
# boundary. Resolve only unmatched triangles using a bounded geometric match.
if args.model:
    source_keys=list(groups);source_points=np.asarray(source_keys);centers=source_points.mean(axis=1)
    for tri,uses in zip(triangles,painted):
        if not uses or key(tri) in groups:continue
        candidates=np.flatnonzero(np.linalg.norm(centers-tri.mean(axis=0),axis=1)<.0002)
        for candidate in candidates:
            distance=np.linalg.norm(tri[:,None,:]-source_points[candidate][None,:,:],axis=2)
            if max(distance.min(axis=0).max(),distance.min(axis=1).max())<.00015:
                groups[key(tri)]=groups[source_keys[candidate]];break
face_map = [{'group': groups.get(key(tri), 'UNMATCHED'), 'center': tri.mean(axis=0).tolist(), 'uv': uv.tolist()}
            for tri, uv, uses in zip(triangles, texcoords, painted) if uses]
(output/'paint-face-map.json').write_text(json.dumps(face_map, indent=2)+'\n')
assert all(f['group'] != 'UNMATCHED' for f in face_map), 'Every painted runtime triangle must map to an authored group'

# Check the actual exported coordinates, rather than trusting guide labels.
# Opposite outside views require opposite longitudinal UV handedness so text
# reads normally on both sides of the vehicle.
report = json.loads((ROOT/f'.files/f7a/uv-paint-only/{source_version}/uv-report.json').read_text())
if report.get('unfolded') and source_version == args.version:
    assignments = report['groupViewAssignments']
    checked = {'left': 0, 'right': 0}
    for tri, uv, uses in zip(triangles, texcoords, painted):
        if not uses: continue
        name = groups[key(tri)].removeprefix('Livery panels - ')
        if assignments.get(name) != 'side': continue
        a, b = np.argmin(tri[:, 2]), np.argmax(tri[:, 2])
        if tri[b, 2]-tri[a, 2] < .05: continue
        side = 'left' if tri[:, 0].mean() < 0 else 'right'
        sign = 1 if side == 'left' else -1
        assert (uv[b, 0]-uv[a, 0])*sign > 0, f'Mirrored side UV: {side}, {name}'
        checked[side] += 1
    assert min(checked.values()) > 0, 'Both side projections must be checked'
    (output/'uv-orientation.json').write_text(json.dumps(checked, indent=2)+'\n')

def sample(uv):
    x = np.clip(uv[..., 0]*(texture.shape[1]-1), 0, texture.shape[1]-1).astype(int)
    y = np.clip(uv[..., 1]*(texture.shape[0]-1), 0, texture.shape[0]-1).astype(int)
    return texture[y, x].astype(float)

# Compare independently mapped copies of the same physical edge, sampled slightly
# inside each triangle to avoid measuring the background rather than the surface.
edges = {}
for ti in np.flatnonzero(painted):
    tri, uv = triangles[ti], texcoords[ti]
    for a, b in ((0, 1), (1, 2), (2, 0)):
        va, vb = tuple(np.round(tri[a], 4)), tuple(np.round(tri[b], 4))
        ua, ub = uv[a], uv[b]
        if va > vb: va, vb, ua, ub = vb, va, ub, ua
        edges.setdefault((va, vb), []).append((ti, ua, ub, uv.mean(axis=0)))
seams = []
for edge, entries in edges.items():
    if len(entries) != 2: continue
    a, b = entries
    if np.max(np.abs(np.array(a[1:3])-np.array(b[1:3]))) < 1e-5: continue
    distance = np.linalg.norm(np.array(edge[1])-edge[0])
    if distance < .15: continue
    t = np.linspace(.10, .90, 11)[:, None]
    def points(entry): return (entry[1]*(1-t)+entry[2]*t)*.985+entry[3]*.015
    difference = np.linalg.norm(sample(points(a))-sample(points(b)), axis=1)
    seams.append({'edge': [list(p) for p in edge], 'lengthMetres': float(distance),
                  'meanColorDifference': float(difference.mean()), 'maxColorDifference': float(difference.max())})
seams.sort(key=lambda s: -s['meanColorDifference']*s['lengthMetres'])
(output/'seams.json').write_text(json.dumps(seams, indent=2)+'\n')

width, height = 1600, 650
views = [('left-front', [-22, 8, 25], [0, 2.1, 0], 72), ('right-front', [22, 8, 25], [0, 2.1, 0], 72),
         ('left', [-25, 2.2, 0], [0, 2.2, 0], 95), ('right', [25, 2.2, 0], [0, 2.2, 0], 95),
         ('front', [0, 2.4, 30], [0, 2.2, 0], 128), ('rear', [0, 2.4, -30], [0, 2.2, 0], 128),
         ('roof', [0, 30, .001], [0, 2.2, 0], 95)]
sheet = Image.new('RGB', (width*2, height*4), '#252c34')
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 26)
for vi, (name, eye, target, scale) in enumerate(views):
    eye, target = np.array(eye), np.array(target)
    forward = target-eye; forward /= np.linalg.norm(forward)
    right = np.cross(forward, [1, 0, 0] if name == 'roof' else [0, 1, 0]); right /= np.linalg.norm(right)
    up = np.cross(right, forward)
    basis = np.array([right, up, forward]).T
    screen = np.full((height, width, 3), [37, 44, 52], dtype=np.uint8)
    depth = np.full((height, width), np.inf)
    hits = np.full((height, width), -1, dtype=int)
    for ti, tri in enumerate(triangles):
        projected = (tri-target) @ basis
        xy = projected[:, :2]*[scale, -scale]+[width/2, height/2]
        lo = np.maximum(np.floor(xy.min(axis=0)).astype(int), [0, 0])
        hi = np.minimum(np.ceil(xy.max(axis=0)).astype(int), [width-1, height-1])
        if np.any(hi < lo): continue
        a, b = xy[1]-xy[0], xy[2]-xy[0]; det = a[0]*b[1]-a[1]*b[0]
        if abs(det) < 1e-8: continue
        yy, xx = np.mgrid[lo[1]:hi[1]+1, lo[0]:hi[0]+1]
        p = np.stack((xx+.5-xy[0, 0], yy+.5-xy[0, 1]), axis=-1)
        s = (p[..., 0]*b[1]-p[..., 1]*b[0])/det
        t = (a[0]*p[..., 1]-a[1]*p[..., 0])/det
        weights = np.stack((1-s-t, s, t), axis=-1)
        z = weights @ projected[:, 2]
        visible = (weights.min(axis=-1) >= -1e-6) & (z < depth[yy, xx])
        normal = np.cross(tri[1]-tri[0], tri[2]-tri[0]); normal /= np.linalg.norm(normal)
        shade = .78+.22*abs(normal @ np.array([.3, .8, .52]))
        c = sample(weights @ texcoords[ti]) if painted[ti] else colors[ti]*255
        c = np.broadcast_to(np.asarray(c*shade, dtype=np.uint8), (*xx.shape, 3))
        screen[yy[visible], xx[visible]] = c[visible]; depth[yy[visible], xx[visible]] = z[visible]
        hits[yy[visible], xx[visible]] = ti
    if name == 'rear':
        red = (screen[:, :, 0].astype(float) > screen[:, :, 1]*1.8) & (screen[:, :, 0] > 100)
        ids, counts = np.unique(hits[red & (hits >= 0)], return_counts=True)
        diagnostic = [{'pixels': int(count), 'group': groups.get(key(triangles[ti]), 'solid'),
                       'center': triangles[ti].mean(axis=0).tolist()} for ti, count in zip(ids, counts)]
        (output/'rear-red-pixels.json').write_text(json.dumps(diagnostic, indent=2)+'\n')
    image = Image.fromarray(screen); ImageDraw.Draw(image).text((20, 14), name, font=font, fill='white')
    image.save(output/f'{name}.png'); sheet.paste(image, ((vi%2)*width, (vi//2)*height))
sheet.save(output/'review-sheet.png')
print(f'Review: {output}; {len(seams)} paired UV seams, {int(painted.sum())} painted triangles')
