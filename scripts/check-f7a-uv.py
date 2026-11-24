"""Check the generated kit's source hash, face bindings and per-corner UV indices."""
from pathlib import Path
import hashlib
import json
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / '.files/f7a/uv-paint-only/v1'
NS = '{http://www.collada.org/2005/11/COLLADASchema}'
report = json.loads((OUTPUT / 'uv-report.json').read_text())
source = ROOT / '.files/f7a/f7a-v1.dae'
assert hashlib.sha256(source.read_bytes()).hexdigest() == report['sourceSha256'], 'Source changed'
assert report['textureSize'] == [4096, 4096] and report['primaryPanelOverlaps'] == 0
assert all(m['unwrapTriangles'] == 0 for m in report['meshes'] if m['part'] != 'body'), 'Running gear was inadvertently marked paint'

for name in ('f7a-v1-uv.dae', 'f7a-v1-textured.dae', 'f7a-v1-checker.dae'):
    root = ET.parse(OUTPUT / name).getroot()
    assert root.find(NS+'asset/'+NS+'unit').get('meter') == '0.0254'
    ids = [node.get('id') for node in root.iter() if node.get('id')]
    assert len(ids) == len(set(ids)), 'Duplicate COLLADA IDs'
    ids = set(ids)
    for node in root.iter():
        for attr in ('source', 'url', 'target'):
            target = node.get(attr, '')
            if target.startswith('#'): assert target[1:] in ids, (name, target)
    painted = total = 0
    for geometry in root.findall(NS+'library_geometries/'+NS+'geometry'):
        mesh = geometry.find(NS+'mesh')
        for primitive in mesh.findall(NS+'triangles'):
            count = int(primitive.get('count')); total += count
            inputs = primitive.findall(NS+'input')
            stride = 1 + max(int(i.get('offset', 0)) for i in inputs)
            indices = list(map(int, primitive.find(NS+'p').text.split()))
            assert len(indices) == count*3*stride
            uvs = [i for i in inputs if i.get('semantic') == 'TEXCOORD']
            if not primitive.get('material', '').startswith('paint-'):
                assert not uvs, 'Solid faces gained UV bindings'
                continue
            painted += count
            assert len(uvs) == 1
            source_id = uvs[0].get('source')[1:]
            uv_source = mesh.find(f"{NS}source[@id='{source_id}']")
            coords = list(map(float, uv_source.find(NS+'float_array').text.split()))
            assert all(0 <= c <= 1 for c in coords)
            offset = int(uvs[0].get('offset'))
            assert all(0 <= i < len(coords)//2 for i in indices[offset::stride])
    assert total == report['triangles'] and painted == report['paintedTriangles']
    for image in root.findall(NS+'library_images/'+NS+'image'):
        assert (OUTPUT/image.find(NS+'init_from').text).exists()

with zipfile.ZipFile(OUTPUT/'f7a-v1-uv-kit.zip') as archive:
    assert archive.testzip() is None
    for entry in archive.namelist(): assert archive.read(entry) == (OUTPUT/entry).read_bytes()
print(f'F7A kit OK: {painted} painted triangles; source hash, DAE references, UV ranges, solid materials and archive verified.')
