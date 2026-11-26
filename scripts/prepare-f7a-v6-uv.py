"""Build F7A geometry guides; correct v6 handedness, preserve v7 UVs as authored."""
from pathlib import Path
import json
import struct
import sys
import hashlib
from collections import Counter

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.tmp/uv-tools'))
import numpy as np
from PIL import Image,ImageDraw,ImageFont

VERSION=next((a.split('=',1)[1] for a in sys.argv if a.startswith('--version=')), 'v6')
assert VERSION in ('v6','v7')
SOURCE=ROOT/f'.files/f7a/uv-paint-only/{VERSION}'
OUT=SOURCE/('corrected' if VERSION=='v6' else 'generated')
OUT.mkdir(exist_ok=True)
original=json.loads((SOURCE/f'f7a-{VERSION}-uv-map.json').read_text())
mapping=json.loads(json.dumps(original))
size=mapping['atlas']['width']
for face in mapping['faces'] if VERSION=='v6' else []:
    if face['uv'] is not None:face['uv']=[[u,1-v] for u,v in face['uv']]
for island in mapping['islands'] if VERSION=='v6' else []:
    r=island['rect'];r['y']=size-r['y']-r['h']
if VERSION=='v6':mapping['uvCorrection']={'operation':'reflect V globally: v = 1 - v',
    'reason':'UV Workshop canvas/Y-up handedness bug',
    'originalSha256':hashlib.sha256((SOURCE/'f7a-v6-uv-map.json').read_bytes()).hexdigest(),
    'geometryChanged':False,'packingChanged':'vertical reflection only; relative distances and seam connectivity retained'}
(OUT/f'f7a-{VERSION}-uv-map.json').write_text(json.dumps(mapping,indent=2)+'\n')

# Generate new vector/raster guides from the corrected triangle coordinates.
# The user's original atlas is never resampled or overwritten.
image=Image.new('RGB',(size,size),'#202a32');draw=ImageDraw.Draw(image)
svg=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">',
     '<rect width="100%" height="100%" fill="#202a32"/>']
face_by_id={f['id']:f for f in mapping['faces']}
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',26)
report=[]
for island in mapping['islands']:
    ids=island['faceIds'];colors=[];bounds=[];edges={}
    for id in ids:
        f=face_by_id[id];uv=[(u*size,(1-v)*size)for u,v in f['uv']]
        material=mapping['materials'][f['material']];color=material['color']
        # Distinguishable but restrained authoring colors, not final livery.
        palette={'paint-_side_':'#e7b965','paint-_front_':'#ea7977','paint-_front-top_':'#a28bd3',
            'paint-_roof_':'#63b5ce','paint-_back_':'#b6ca70','paint-_door-front_':'#67c9b0',
            'paint-_door-back_':'#8fc77c','paint-_snowplow_':'#b6a0d3','paint-_headlight-bottom_':'#d58aab','paint-_ladder_':'#d58aab'}
        color=palette.get(material['name'],color);draw.polygon(uv,fill=color)
        svg.append('<polygon fill="'+color+'" points="'+' '.join(f'{x:.4f},{y:.4f}'for x,y in uv)+'"/>')
        for c in range(3):
            key=tuple(sorted((tuple(round(x,4)for x in uv[c]),tuple(round(x,4)for x in uv[(c+1)%3]))))
            edges[key]=edges.get(key,0)+1
    for edge,count in edges.items():
        if count==1:
            draw.line(edge,fill='#34434d',width=2)
            svg.append(f'<path d="M{edge[0][0]},{edge[0][1]} L{edge[1][0]},{edge[1][1]}" fill="none" stroke="#34434d"/>')
    r=island['rect'];draw.text((r['x']+5,r['y']+5),str(island['id']+1),font=font,fill='#203642')
    report.append({'island':island['id']+1,'triangles':len(ids),'materials':dict(Counter(mapping['materials'][face_by_id[id]['material']]['name'] for id in ids)),'rect':r})
svg.append('</svg>')
(OUT/f'f7a-{VERSION}-uv-guide.svg').write_text('\n'.join(svg))
image.save(OUT/f'f7a-{VERSION}-atlas.png')
(OUT/'island-report.json').write_text(json.dumps(report,indent=2)+'\n')

# Correct the UV accessors only; preserve the original nodes, positions, normals,
# hierarchy, wheel components and the user's original GLB file.
data=(SOURCE/f'f7a-{VERSION}-uv.glb').read_bytes();length=struct.unpack_from('<I',data,12)[0]
gltf=json.loads(data[20:20+length]);binary=bytearray(data[28+length:])
accessors=set()
for mesh in gltf['meshes']:
    for primitive in mesh['primitives']:
        material=gltf['materials'][primitive['material']]
        if 'baseColorTexture' in material.get('pbrMetallicRoughness',{}):
            accessors.add(primitive['attributes']['TEXCOORD_0'])
for index in accessors:
    a=gltf['accessors'][index];view=gltf['bufferViews'][a['bufferView']]
    assert a['componentType']==5126 and a['type']=='VEC2'
    start=view.get('byteOffset',0)+a.get('byteOffset',0);stride=view.get('byteStride',8)
    for i in range(a['count']):
        offset=start+i*stride+4;v=struct.unpack_from('<f',binary,offset)[0]
        if VERSION=='v6':struct.pack_into('<f',binary,offset,1-v)
    a.pop('min',None);a.pop('max',None)

# A fresh atlas PNG is embedded without pixel editing. KHR_texture_transform
# converts bottom-left authored UVs to glTF's image-coordinate convention.
texture_path=ROOT/f'.files/f7a/f7a {VERSION} santa fe.png'
png=texture_path.read_bytes() if '--texture' in sys.argv else (OUT/f'f7a-{VERSION}-atlas.png').read_bytes()
while len(binary)%4:binary.append(0)
view_id=len(gltf['bufferViews']);gltf['bufferViews'].append({'buffer':0,'byteOffset':len(binary),'byteLength':len(png)})
binary.extend(png)
gltf['images']=[{'bufferView':view_id,'mimeType':'image/png','name':'F7A v6 atlas'}]
gltf['textures']=[{'source':0,'sampler':0}]
gltf.setdefault('samplers',[{'magFilter':9729,'minFilter':9987}])
for m in gltf['materials']:
    info=m.get('pbrMetallicRoughness',{}).get('baseColorTexture')
    if info is not None:
        info['index']=0;info.setdefault('extensions',{})['KHR_texture_transform']={'offset':[0,1],'scale':[1,-1]}
        m['pbrMetallicRoughness']['baseColorFactor']=[1,1,1,1]
gltf['extensionsUsed']=list(dict.fromkeys(gltf.get('extensionsUsed',[])+['KHR_texture_transform']))
while len(binary)%4:binary.append(0)
gltf['buffers']=[{'byteLength':len(binary)}]
j=json.dumps(gltf,separators=(',',':')).encode();j+=b' '*((-len(j))%4)
out=struct.pack('<III',0x46546c67,2,28+len(j)+len(binary))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(binary),0x004e4942)+binary
(OUT/(f'f7a-{VERSION}-textured.glb'if '--texture'in sys.argv else f'f7a-{VERSION}-uv.glb')).write_bytes(out)
assert all(f['p']==o['p'] for f,o in zip(mapping['faces'],original['faces']))
print(f'{VERSION}: {len(accessors)} texture UV accessors, {sum(f["uv"] is not None for f in mapping["faces"])} mapped triangles; geometry unchanged; '+('V reflected.' if VERSION=='v6' else 'authored UVs unchanged.'))
