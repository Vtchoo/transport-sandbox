"""Create a non-destructive, shared UV atlas for the authored Dash 9 COLLADA model.

Dependencies: xatlas, numpy, pillow, installed in godot/.godot/uv-tools.
"""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'godot/.godot/uv-tools'))
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import xatlas

NS = 'http://www.collada.org/2005/11/COLLADASchema'
ET.register_namespace('', NS)
Q = lambda name: '{' + NS + '}' + name


def transform(node):
    result = np.eye(4)
    for child in node:
        tag = child.tag.removeprefix('{' + NS + '}')
        values = np.fromstring(child.text or '', sep=' ')
        matrix = np.eye(4)
        if tag == 'matrix':
            matrix = values.reshape(4, 4)
        elif tag == 'translate':
            matrix[:3, 3] = values
        elif tag == 'scale':
            matrix[:3, :3] = np.diag(values)
        elif tag == 'rotate':
            axis = values[:3] / np.linalg.norm(values[:3])
            x, y, z = axis
            cross = np.array([[0, -z, y], [z, 0, -x], [-y, x, 0]])
            angle = math.radians(values[3])
            matrix[:3, :3] = np.eye(3) * math.cos(angle) + (1 - math.cos(angle)) * np.outer(axis, axis) + math.sin(angle) * cross
        else:
            continue
        result = result @ matrix
    return result


def positions(mesh, reference):
    vertices = mesh.find(f"{Q('vertices')}[@id='{reference[1:]}']")
    source_id = next(i.get('source')[1:] for i in vertices.findall(Q('input')) if i.get('semantic') == 'POSITION')
    source = mesh.find(f"{Q('source')}[@id='{source_id}']")
    accessor = source.find(f"{Q('technique_common')}/{Q('accessor')}")
    array = source.find(Q('float_array'))
    data = np.fromstring(array.text, sep=' ')
    stride = int(accessor.get('stride', '1'))
    offset = int(accessor.get('offset', '0'))
    return data[offset:offset + int(accessor.get('count')) * stride].reshape(-1, stride)[:, :3]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT / '.files/dash-9/dash9-v5.dae')
    parser.add_argument('--output', type=Path, default=ROOT / '.files/dash-9/uv-paint-only/v5')
    parser.add_argument('--size', type=int, default=4096)
    parser.add_argument('--inspect', action='store_true')
    parser.add_argument('--automatic', action='store_true', help='Use the old automatic pack instead of the painting layout')
    parser.add_argument('--paint-only', action='store_true', help='Map only large livery panels; keep hardware solid-colored')
    parser.add_argument('--paint-material', default='paint', help='Unwrap exactly the faces assigned to this material')
    parser.add_argument('--name', default='dash9-v5', help='Output filename prefix')
    args = parser.parse_args()
    if args.paint_material: args.paint_only = True
    material_names = {}
    source_bytes = args.source.read_bytes()
    root = ET.fromstring(source_bytes)
    material_names = {m.get('id'): m.get('name', '') for m in root.findall(f"{Q('library_materials')}/{Q('material')}")}
    unit = float(root.find(f"{Q('asset')}/{Q('unit')}").get('meter', '1'))
    definitions = {n.get('id'): n for n in root.findall(f"{Q('library_nodes')}/{Q('node')}")}
    geometries = root.find(Q('library_geometries'))
    original_geometry = {g.get('id'): g for g in geometries}
    meshes = []
    serial = 0

    def expand(node, parent, part='body', stack=()):
        nonlocal serial
        if node.get('name') in ('body', 'bogie_front', 'bogie_rear', 'bogie-front', 'bogie-rear'):
            part = node.get('name').replace('_', '-')
        world = parent @ transform(node)
        if node.get('id'):
            serial += 1
            node.set('id', f'uv-node-{serial}')
        for child in list(node):
            if child.tag == Q('instance_node'):
                ref = child.get('url')[1:]
                if ref in stack:
                    raise ValueError('Cyclic COLLADA component references')
                replacement = copy.deepcopy(definitions[ref])
                node.insert(list(node).index(child), replacement)
                node.remove(child)
                expand(replacement, world, part, (*stack, ref))
            elif child.tag == Q('node'):
                expand(child, world, part, stack)
            elif child.tag == Q('instance_geometry'):
                original = original_geometry[child.get('url')[1:]]
                if not original.findall(f"{Q('mesh')}/{Q('triangles')}"):
                    continue
                serial += 1
                geometry = copy.deepcopy(original)
                # Give every physical instance independent UVs while retaining its positions/materials.
                ids = {e.get('id'): f"uv-{serial}-{e.get('id')}" for e in geometry.iter() if e.get('id')}
                for element in geometry.iter():
                    for key, value in list(element.attrib.items()):
                        if key == 'id':
                            element.set(key, ids[value])
                        elif value.startswith('#') and value[1:] in ids:
                            element.set(key, '#' + ids[value[1:]])
                child.set('url', '#' + geometry.get('id'))
                geometries.append(geometry)
                mesh = geometry.find(Q('mesh'))
                material_bindings = {i.get('symbol'): i.get('target')[1:] for i in child.findall(f"{Q('bind_material')}/{Q('technique_common')}/{Q('instance_material')}")}
                points, faces, primitives = [], [], []
                for primitive in mesh.findall(Q('triangles')):
                    inputs = primitive.findall(Q('input'))
                    stride = max(int(i.get('offset', '0')) for i in inputs) + 1
                    indices = np.fromstring(primitive.find(Q('p')).text, sep=' ', dtype=np.int64).reshape(-1, stride)
                    vertex = next(i for i in inputs if i.get('semantic') == 'VERTEX')
                    local = positions(mesh, vertex.get('source'))
                    xyz = np.column_stack((local, np.ones(len(local)))) @ world.T
                    face = indices[:, int(vertex.get('offset', '0'))].reshape(-1, 3) + len(points)
                    primitives.append({'element': primitive, 'indices': indices, 'count': len(face), 'material': material_bindings.get(primitive.get('material'))})
                    faces.extend(face)
                    points.extend(xyz[:, :3] * unit)
                points, faces = np.asarray(points, dtype=np.float32), np.asarray(faces, dtype=np.uint32)
                # Weld only the temporary unwrap input; never alter the authored COLLADA position data.
                # SketchUp's exported float coordinates can leave tiny cracks at
                # coincident corners. Weld to 0.1 mm for unwrap connectivity only.
                unique, inverse = np.unique(np.round(points, 4), axis=0, return_inverse=True)
                welded = inverse[faces]
                areas = np.linalg.norm(np.cross(unique[welded[:, 1]] - unique[welded[:, 0]], unique[welded[:, 2]] - unique[welded[:, 0]]), axis=1)
                valid = areas > 1e-9
                if not np.any(valid):
                    raise ValueError('An entire exported mesh has zero area')
                selected = np.concatenate([np.full(p['count'], material_names.get(p['material']) == args.paint_material) for p in primitives])
                meshes.append({'geometry': geometry, 'instance': child, 'part': part, 'points': unique, 'faces': welded[valid].astype(np.uint32), 'selected': selected[valid],
                               'valid': valid, 'primitives': primitives, 'all_points': points, 'original_faces': faces})

    scene_id = root.find(f"{Q('scene')}/{Q('instance_visual_scene')}").get('url')[1:]
    scene = root.find(f"{Q('library_visual_scenes')}/{Q('visual_scene')}[@id='{scene_id}']")
    for node in scene.findall(Q('node')):
        expand(node, np.eye(4))
    root.remove(root.find(Q('library_nodes')))
    for geometry in original_geometry.values():
        if geometry.findall(f"{Q('mesh')}/{Q('triangles')}"):
            geometries.remove(geometry)

    if args.inspect:
        for item in meshes:
            points = item['all_points'][:, [0, 2, 1]] * np.array([1, 1, -1])
            bounds = [points.min(axis=0).round(3).tolist(), points.max(axis=0).round(3).tolist()]
            print(item['geometry'].get('id'), item['part'], len(item['original_faces']), bounds)
            if item['geometry'].get('id').endswith(('ID298', 'ID593')):
                triangles = points[item['original_faces']]
                cross = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
                lengths = np.linalg.norm(cross, axis=1)
                groups = {}
                for tri, normal, area in zip(triangles, cross, lengths):
                    if area < 1e-6:
                        continue
                    normal = normal / area
                    axis = int(np.argmax(np.abs(normal)))
                    if normal[axis] < 0:
                        normal = -normal
                    key = tuple(np.round(normal, 2)) + (round(float(np.dot(normal, tri.mean(axis=0))), 2),)
                    groups[key] = groups.get(key, 0) + area / 2
                print('PLANES', sorted(groups.items(), key=lambda entry: -entry[1])[:25])
        return
    if args.paint_material:
        from dash9_material_uv import MaterialAtlas
        atlas = MaterialAtlas(meshes, args.size)
    elif not args.automatic:
        from dash9_livery_uv import LiveryAtlas
        atlas = LiveryAtlas(meshes, args.size, paint_only=args.paint_only)
    else:
        atlas = xatlas.Atlas()
        for item in meshes:
            atlas.add_mesh(item['points'], item['faces'])
        pack = xatlas.PackOptions()
        pack.resolution = args.size
        pack.padding = 16
        pack.bilinear = True
        pack.rotate_charts_to_axis = True
        chart = xatlas.ChartOptions()
        chart.max_iterations = 3
        atlas.generate(chart_options=chart, pack_options=pack)
        for _ in range(5):
            if atlas.atlas_count == 1:
                break
            pack.texels_per_unit = atlas.texels_per_unit * .8
            atlas.generate(chart_options=chart, pack_options=pack)
    if atlas.atlas_count != 1:
        raise ValueError('Could not fit the model into one atlas')

    size = args.size
    wire = Image.new('RGBA', (size, size))
    guide = Image.new('RGB', (size, size), '#20272c')
    paint = Image.new('RGB', (size, size), '#777777')
    mask = Image.new('L', (size, size))
    draw_wire, draw_guide, draw_paint, draw_mask = ImageDraw.Draw(wire), ImageDraw.Draw(guide), ImageDraw.Draw(paint), ImageDraw.Draw(mask)
    report = []
    effects = {e.get('id'): e for e in root.findall(f"{Q('library_effects')}/{Q('effect')}")}
    materials = {}
    painted_materials = {}
    outline_edges = {}
    atlas.preview_colors = []
    for material in root.findall(f"{Q('library_materials')}/{Q('material')}"):
        effect = effects[material.find(Q('instance_effect')).get('url')[1:]]
        color = effect.find(f".//{Q('diffuse')}/{Q('color')}")
        rgba = [float(v) for v in color.text.split()] if color is not None else [1, 1, 1, 1]
        materials[material.get('id')] = tuple(round(max(0, min(1, c)) * 255) for c in rgba[:3])

    def paint_binding(item, symbol, material_id):
        if material_id not in painted_materials:
            original = root.find(f"{Q('library_materials')}/{Q('material')}[@id='{material_id}']")
            material = copy.deepcopy(original)
            material.set('id', 'paint-' + material_id)
            material.set('name', 'Livery panels')
            instance_effect = material.find(Q('instance_effect'))
            effect = copy.deepcopy(effects[instance_effect.get('url')[1:]])
            effect.set('id', 'paint-' + effect.get('id'))
            instance_effect.set('url', '#' + effect.get('id'))
            root.find(Q('library_effects')).append(effect)
            root.find(Q('library_materials')).append(material)
            painted_materials[material_id] = material.get('id')
        bindings = item['instance'].find(f"{Q('bind_material')}/{Q('technique_common')}")
        new_symbol = 'paint-' + symbol
        if not any(b.get('symbol') == new_symbol for b in bindings):
            binding = ET.SubElement(bindings, Q('instance_material'), {'symbol': new_symbol, 'target': '#' + painted_materials[material_id]})
            ET.SubElement(binding, Q('bind_vertex_input'), {'semantic': 'UVSET0', 'input_semantic': 'TEXCOORD', 'input_set': '0'})
        return new_symbol

    for index, item in enumerate(meshes):
        mapping, indices, uvs = atlas[index]
        if not np.array_equal(mapping[indices], item['faces']):
            raise ValueError('Unwrapper reordered faces; refusing to attach mismatched UVs')
        # Preserve chart aspect ratios if xatlas used a rectangular internal pack.
        uvs = uvs * np.array([atlas.width, atlas.height]) / max(atlas.width, atlas.height)
        if not np.isfinite(uvs).all() or uvs.min() < 0 or uvs.max() > 1:
            raise ValueError('Invalid texture coordinates')
        mesh = item['geometry'].find(Q('mesh'))
        prefix = item['geometry'].get('id') + '-uv0'
        source = ET.Element(Q('source'), {'id': prefix})
        array = ET.SubElement(source, Q('float_array'), {'id': prefix + '-array', 'count': str((len(uvs) + 1) * 2)})
        array.text = ' '.join(format(float(v), '.9g') for v in np.vstack((uvs, [0, 0])).flat)
        accessor = ET.SubElement(ET.SubElement(source, Q('technique_common')), Q('accessor'),
                                 {'source': '#' + prefix + '-array', 'count': str(len(uvs) + 1), 'stride': '2'})
        for axis in ('S', 'T'):
            ET.SubElement(accessor, Q('param'), {'name': axis, 'type': 'float'})
        mesh.insert(0, source)
        full_indices = np.full((len(item['valid']), 3), len(uvs), dtype=np.uint32)
        full_indices[item['valid']] = indices
        paint_faces = item['valid'].copy()
        if args.paint_only: paint_faces[item['valid']] = atlas.paint_masks[index]
        offset = 0
        part_color = {'body': (116, 173, 215), 'bogie-front': (218, 166, 86), 'bogie-rear': (123, 191, 138)}[item['part']]
        for primitive in item['primitives']:
            count = primitive['count']
            tex_indices = full_indices[offset:offset + count].reshape(-1, 1)
            existing = primitive['indices']
            uv_input = ET.Element(Q('input'), {'semantic': 'TEXCOORD', 'source': '#' + prefix,
                                              'offset': str(existing.shape[1]), 'set': '0'})
            p = primitive['element'].find(Q('p'))
            primitive['element'].insert(list(primitive['element']).index(p), uv_input)
            p.text = ' '.join(map(str, np.hstack((existing, tex_indices)).flat))
            color = materials.get(primitive['material'], (255, 255, 255))
            atlas.preview_colors.extend([color] * int(item['valid'][offset:offset + count].sum()))
            if args.paint_only:
                selected = paint_faces[offset:offset + count]
                position = list(mesh).index(primitive['element'])
                mesh.remove(primitive['element'])
                for painted in (False, True):
                    keep = selected if painted else ~selected
                    if not keep.any(): continue
                    subset = copy.deepcopy(primitive['element'])
                    subset.set('count', str(int(keep.sum())))
                    if painted:
                        subset.set('material', paint_binding(item, subset.get('material'), primitive['material']))
                        data = np.hstack((existing, tex_indices)).reshape(count, 3, -1)[keep]
                    else:
                        for entry in subset.findall(Q('input')):
                            if entry.get('semantic') == 'TEXCOORD': subset.remove(entry)
                        data = existing.reshape(count, 3, -1)[keep]
                    subset.find(Q('p')).text = ' '.join(map(str, data.flat))
                    mesh.insert(position, subset)
                    position += 1
            for face in full_indices[offset:offset + count][paint_faces[offset:offset + count]]:
                uv = uvs[face]
                xy = [(float(u * (size - 1)), float((1 - v) * (size - 1))) for u, v in uv]
                draw_paint.polygon(xy, fill=color)
                draw_mask.polygon(xy, fill=255)
                draw_guide.polygon(xy, fill=part_color)
                if args.paint_only:
                    for a, b in zip(xy, xy[1:] + xy[:1]):
                        edge = tuple(sorted((tuple(round(v, 2) for v in a), tuple(round(v, 2) for v in b))))
                        outline_edges[edge] = outline_edges.get(edge, 0) + 1
                else:
                    draw_wire.line(xy + [xy[0]], fill=(20, 25, 30, 190), width=1)
                    draw_guide.line(xy + [xy[0]], fill=(38, 47, 54), width=1)
            offset += count
        report.append({'mesh': item['geometry'].get('id'), 'part': item['part'], 'triangles': len(item['original_faces']),
                       'unwrapTriangles': int(paint_faces.sum()), 'charts': atlas.get_mesh_chart_count(index)})

    for edge, count in outline_edges.items():
        if count == 1:
            draw_wire.line(edge, fill=(20, 25, 30, 230), width=2)
            draw_guide.line(edge, fill=(38, 47, 54), width=2)

    # Extend paint colors into chart gutters to reduce texture filtering seams.
    for _ in range(2 if hasattr(atlas, 'panels') else 16):
        expanded = mask.filter(ImageFilter.MaxFilter(3))
        edge = np.asarray(expanded, dtype=np.int16) - np.asarray(mask, dtype=np.int16)
        grown = paint.filter(ImageFilter.MaxFilter(3))
        paint.paste(grown, mask=Image.fromarray(np.where(edge > 0, 255, 0).astype(np.uint8)))
        mask = expanded
    args.output.mkdir(parents=True, exist_ok=True)
    if hasattr(atlas, 'labels'):
        font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', max(12, round(26 * size / 4096)))
        for x, y, label in atlas.labels:
            draw_guide.text((x, y), label, fill=(240, 240, 220), font=font)
            draw_wire.text((x, y), label, fill=(45, 45, 45, 255), font=font)
    stem = 'dash9-v2-uv'
    ET.indent(root, space='  ')
    ET.ElementTree(root).write(args.output / (stem + '.dae'), encoding='utf-8', xml_declaration=True)
    wire.save(args.output / (stem + '-layout.png'))
    guide.save(args.output / (stem + '-guide.png'))
    paint.save(args.output / (stem + '-basecolor.png'))
    if hasattr(atlas, 'preview_faces'):
        from dash9_uv_preview import create_preview
        create_preview(atlas, args.output, size)
    # Keep a UV-only copy for exact geometry/material round-tripping, and provide
    # a separate ready-to-preview version with the paint image actually bound.
    textured = copy.deepcopy(root)
    images = ET.Element(Q('library_images'))
    image = ET.SubElement(images, Q('image'), {'id': 'dash9-atlas-image', 'name': 'Dash9Atlas'})
    ET.SubElement(image, Q('init_from')).text = stem + '-basecolor.png'
    textured.insert(list(textured).index(textured.find(Q('library_effects'))), images)
    for effect in textured.findall(f"{Q('library_effects')}/{Q('effect')}"):
        if args.paint_only and not effect.get('id').startswith('paint-'): continue
        profile = effect.find(Q('profile_COMMON'))
        diffuse = effect.find(f".//{Q('diffuse')}")
        if profile is None or diffuse is None:
            continue
        surface_id = effect.get('id') + '-atlas-surface'
        sampler_id = effect.get('id') + '-atlas-sampler'
        surface_param = ET.Element(Q('newparam'), {'sid': surface_id})
        surface = ET.SubElement(surface_param, Q('surface'), {'type': '2D'})
        ET.SubElement(surface, Q('init_from')).text = 'dash9-atlas-image'
        sampler_param = ET.Element(Q('newparam'), {'sid': sampler_id})
        sampler = ET.SubElement(sampler_param, Q('sampler2D'))
        ET.SubElement(sampler, Q('source')).text = surface_id
        profile.insert(0, surface_param)
        profile.insert(1, sampler_param)
        for child in list(diffuse):
            diffuse.remove(child)
        ET.SubElement(diffuse, Q('texture'), {'texture': sampler_id, 'texcoord': 'UVSET0'})
    ET.indent(textured, space='  ')
    ET.ElementTree(textured).write(args.output / 'dash9-v2-textured.dae', encoding='utf-8', xml_declaration=True)
    if hasattr(atlas, 'preview_faces'):
        images.find(f"{Q('image')}/{Q('init_from')}").text = 'dash9-v2-uv-checker.png'
        ET.ElementTree(textured).write(args.output / 'dash9-v2-checker.dae', encoding='utf-8', xml_declaration=True)
    summary = {'source': str(args.source.resolve().relative_to(ROOT)), 'sourceSha256': hashlib.sha256(source_bytes).hexdigest(),
               'textureSize': [size, size], 'atlasCount': atlas.atlas_count, 'chartCount': atlas.chart_count,
               'internalPackSize': [atlas.width, atlas.height], 'utilization': atlas.utilization,
               'meshes': report, 'triangles': sum(m['triangles'] for m in report)}
    if hasattr(atlas, 'panels'):
        summary.update(layout='livery-panels', primaryPanelOverlaps=atlas.panel_overlap_count, panels=atlas.panels)
    if args.paint_only:
        summary.update(layout='paintable-panels-only', paintedTriangles=sum(m['unwrapTriangles'] for m in report))
        summary['solidColorTriangles'] = summary['triangles'] - summary['paintedTriangles']
    if args.paint_material:
        summary['selectionMaterial'] = args.paint_material
        summary['selectionRule'] = 'Exact exported face-material name; no geometry-ID or position-based selection'
        summary['intentionalSideProjectionOverlaps'] = atlas.intentional_side_overlap_count
    (args.output / 'uv-report.json').write_text(json.dumps(summary, indent=2) + '\n')
    (args.output / 'README.txt').write_text(
        'Dash 9 v2: shared 4096 x 4096 UV atlas\n\n'
        'LIVERY LAYOUT: two large side silhouettes, both upright with the FRONT AT LEFT.\n'
        'The sides share the same scale and vertical alignment: copy horizontal stripes between them.\n'
        'Lettering must be painted separately on each side; UVs are not mirrored or stacked.\n'
        'Below: labeled roof planes, nose panels, cab front, rear end, cab rear, and six faces per bogie.\n'
        'Small hardware, handrails, stairs and underframe are packed in the lower-right area.\n'
        'These tiny parts are intended for solid colors; the main body panels are intended for livery artwork.\n\n'
        'dash9-v2-uv.dae: UV-mapped model copy. Geometry, units, materials, and named body/bogie pivots are preserved.\n'
        'Repeated component geometry is made unique so mirrored/repeated details can be painted independently.\n'
        'dash9-v2-textured.dae: preview/painting copy with the basecolor PNG assigned to the UV atlas. Keep both files together.\n'
        'dash9-v2-uv-layout.png: transparent wireframe; use as a top guide layer in your image editor.\n'
        'dash9-v2-uv-guide.png: blue = body, orange = front bogie, green = rear bogie.\n'
        'dash9-v2-uv-basecolor.png: original material colors; paint here and hide the wireframe before exporting.\n'
        'Image top corresponds to UV V=1. Keep the image at 4096 x 4096.\n'
        'Load dash9-v2-textured.dae in a 3D editor to identify individual surfaces while painting.\n'
        'dash9-v2-checker.dae and dash9-v2-uv-checker.png: texture alignment test, not a finished livery.\n'
        'dash9-v2-uv-preview.png: two model views using that checker and matching-height stripes.\n'
        'This does not change the running game. Its current COLLADA converter ignores UVs and textures; a texture import adapter is still needed.\n'
        'The UV-only copy retains original solid-color materials; the textured copy binds the supplied PNG.\n')
    if args.paint_only:
        (args.output / 'README.txt').write_text(
            'Dash 9 - simplified paintable-panels UV kit (4096 x 4096)\n\n'
            'Only major body sides, cab sides, nose, roof, end panels and equipment cabinet sides use the atlas.\n'
            'Handrails, bogies, wheel areas, glass, steps, underframe and minor details retain solid materials, with no texture binding or face UVs.\n'
            'RIGHT SIDE: front at left. LEFT SIDE: front at right. Paint text normally on both; this avoids mirrored lettering.\n'
            'The two main sides share scale and vertical alignment. Their front directions differ intentionally.\n'
            'UV-only DAE preserves original colors; textured DAE binds the basecolor PNG ONLY to paintable faces.\n'
            'Paint dash9-v2-uv-basecolor.png using dash9-v2-uv-layout.png as a transparent guide layer.\n'
            'The guide shows panel boundaries rather than every triangulation edge.\n'
            'Checker DAE/PNG and the two-view preview demonstrate coverage; small hardware must remain solid.\n'
            'Keep the DAE and PNG together when importing into a modeling tool.\n'
            'This is an authoring kit; the Godot game model is unchanged.\n')
    if args.paint_material:
        (args.output / 'README.txt').write_text(
            f'{args.name}: material-selected paint-only UV atlas, {size} x {size}\n\n'
            f'Exactly the valid faces assigned to material "{args.paint_material}" are mapped.\n'
            'Other faces keep their original solid-color materials, without UVs or texture binding.\n'
            'LEFT SIDE: front at right. RIGHT SIDE: front at left. Paint lettering normally on both.\n'
            'The large sides, roof, front and rear use recognizable orthographic views.\n'
            'Cabinet doors stay in their actual side-view locations. Side faces at different depths intentionally share projected paint coordinates, so stripes align across the body and cabinets.\n'
            'Occluded/secondary marked surfaces are unwrapped separately in the lower-right area; no marked faces are omitted.\n'
            'Sloped surfaces in the main views use projection mapping, so their texel density varies with slope.\n'
            'Paint the basecolor PNG, using the transparent layout PNG as a guide layer.\n'
            'Magenta in the basecolor is the original paint placeholder; replace it with your livery.\n'
            'The textured DAE binds that PNG only to the marked faces. Keep DAE/PNG together.\n'
            'The checker DAE and two-view preview show texture coverage and orientation.\n'
            'This authoring kit does not change the Godot game model. The older Santa Fe texture will need repainting for the new layout.\n')
    names = [stem + '.dae', 'dash9-v2-textured.dae', stem + '-layout.png', stem + '-guide.png', stem + '-basecolor.png', 'uv-report.json', 'README.txt']
    if hasattr(atlas, 'preview_faces'):
        names += ['dash9-v2-checker.dae', stem + '-checker.png', stem + '-preview.png']
    if args.name != 'dash9-v2':
        for name in names.copy():
            path = args.output / name
            if path.suffix == '.dae': path.write_text(path.read_text().replace('dash9-v2', args.name))
            renamed = name.replace('dash9-v2', args.name)
            if renamed != name: path.replace(args.output / renamed)
        names = [name.replace('dash9-v2', args.name) for name in names]
    with zipfile.ZipFile(args.output / (args.name + '-uv-kit.zip'), 'w', zipfile.ZIP_DEFLATED) as archive:
        for name in names:
            archive.write(args.output / name, name)
    print(json.dumps({key: value for key, value in summary.items() if key != 'meshes'}, indent=2))


if __name__ == '__main__':
    main()
