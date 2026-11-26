"""Check that the non-destructive v6 correction changes UV handedness only."""
from pathlib import Path
import hashlib
import json
import struct

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / '.files/f7a/uv-paint-only/v6'
OUT = SOURCE / 'corrected'
original = json.loads((SOURCE / 'f7a-v6-uv-map.json').read_text())
corrected = json.loads((OUT / 'f7a-v6-uv-map.json').read_text())
assert corrected['uvCorrection']['originalSha256'] == hashlib.sha256((SOURCE / 'f7a-v6-uv-map.json').read_bytes()).hexdigest()
mapped = 0
for before, after in zip(original['faces'], corrected['faces'], strict=True):
    expected = dict(before)
    if before['uv'] is not None:
        expected['uv'] = [[u, 1-v] for u, v in before['uv']]
        mapped += 1
    assert expected == after
for before, after in zip(original['islands'], corrected['islands'], strict=True):
    expected = json.loads(json.dumps(before))
    rect = expected['rect']
    rect['y'] = original['atlas']['width'] - rect['y'] - rect['h']
    assert expected == after
assert mapped == 948 and len(corrected['islands']) == 81

def glb(path):
    data = path.read_bytes()
    size = struct.unpack_from('<I', data, 12)[0]
    return json.loads(data[20:20+size]), data[28+size:]

def values(doc, binary, index):
    accessor = doc['accessors'][index]
    view = doc['bufferViews'][accessor['bufferView']]
    width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[accessor['type']]
    fmt = {5121: 'B', 5123: 'H', 5125: 'I', 5126: 'f'}[accessor['componentType']]
    size = struct.calcsize('<'+fmt*width)
    offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    return [struct.unpack_from('<'+fmt*width, binary, offset+i*view.get('byteStride', size)) for i in range(accessor['count'])]

before, source_binary = glb(SOURCE / 'f7a-v6-uv.glb')
after, binary = glb(OUT / 'f7a-v6-textured.glb')
assert before['nodes'] == after['nodes'] and before['meshes'] == after['meshes']
uv_accessors = set()
for mesh in before['meshes']:
    for primitive in mesh['primitives']:
        for attribute, index in primitive['attributes'].items():
            if attribute != 'TEXCOORD_0':
                assert values(before, source_binary, index) == values(after, binary, index)
        if 'indices' in primitive:
            index = primitive['indices']
            assert values(before, source_binary, index) == values(after, binary, index)
        material = before['materials'][primitive['material']]
        if 'baseColorTexture' in material.get('pbrMetallicRoughness', {}):
            uv_accessors.add(primitive['attributes']['TEXCOORD_0'])
for index in uv_accessors:
    for a, b in zip(values(before, source_binary, index), values(after, binary, index), strict=True):
        assert a[0] == b[0] and abs((1-a[1])-b[1]) < 1e-7
for material in after['materials']:
    texture = material.get('pbrMetallicRoughness', {}).get('baseColorTexture')
    if texture:
        assert texture['extensions']['KHR_texture_transform'] == {'offset': [0, 1], 'scale': [1, -1]}
texture = (OUT / 'f7a-v6-santa-fe.png').read_bytes()
assert struct.unpack_from('>II', texture, 16) == (1254, 1254)
view = after['bufferViews'][after['images'][0]['bufferView']]
assert binary[view['byteOffset']:view['byteOffset']+view['byteLength']] == texture
print(f'PASS: {mapped} mapped faces, 81 islands, {len(uv_accessors)} corrected UV accessors; geometry, hierarchy and indices unchanged; selected texture embedded.')
