"""Explicit material membership, separate projected panels, one consistently scaled atlas."""
import re
import numpy as np
from dash9_livery_uv import LiveryAtlas, intersection_area


class GroupedMaterialAtlas(LiveryAtlas):
    def __init__(self, meshes, size):
        self.width = self.height = size
        self.atlas_count = 1
        self.mesh_count = len(meshes)
        self.paint_only = True
        self.preview_label = 'F7A V2'
        self.labels, self.panels, self.results, self.preview_faces, self.paint_masks = [], [], [], [], []
        self.regions = {}  # Checker preview must not assume legacy side rectangles.
        self.panel_overlap_count = self.intentional_side_overlap_count = 0
        all_triangles, arrays, patches = [], [], {}
        for mi, item in enumerate(meshes):
            triangles = item['points'][item['faces']][:, :, [0, 2, 1]] * [1, 1, -1]
            all_triangles.append(triangles)
            arrays.append(np.zeros((len(triangles), 3, 2)))
            for fi in np.flatnonzero(item['selected']):
                tri = triangles[fi]
                normal = np.cross(tri[1] - tri[0], tri[2] - tri[0]) * item['orientation']
                axis = int(np.argmax(np.abs(normal)))
                sign = 1 if normal[axis] >= 0 else -1
                # SketchUp mirrored components can retain inconsistent winding.
                # Use physical side/end for view orientation, not triangle winding.
                centre = tri.mean(axis=0)
                if axis in (0, 2) and abs(centre[axis]) > .1:
                    sign = 1 if centre[axis] > 0 else -1
                elif axis == 1 and centre[1] > 3:
                    sign = 1
                name = str(item['face_materials'][fi])
                # Membership is exclusively the authored name. Orientation only
                # splits that group to avoid mirrored sides and edge-on faces.
                key = (name, axis, sign)
                if axis == 0:
                    projected = np.column_stack((tri[:, 2] * (-sign), -tri[:, 1]))
                elif axis == 1:
                    projected = np.column_stack((-tri[:, 2], tri[:, 0] * sign))
                else:
                    projected = np.column_stack((tri[:, 0] * sign, -tri[:, 1]))
                patches.setdefault(key, []).append((mi, fi, projected))
        if not patches:
            raise ValueError('No named paint faces found')
        charts = []
        for (name, axis, sign), entries in sorted(patches.items()):
            # Hidden or duplicate projected surfaces get independent layers.
            # They keep their authored group and are never moved to another panel.
            layers = []
            for entry in entries:
                layer = next((layer for layer in layers if all(intersection_area(entry[2], old[2]) < 1e-9 for old in layer)), None)
                if layer is None:
                    layer = []; layers.append(layer)
                layer.append(entry)
            for number, layer in enumerate(layers):
                points = np.concatenate([entry[2] for entry in layer])
                minimum, maximum = points.min(axis=0), points.max(axis=0)
                label = re.sub(r'-_([^\[\]]+)_$', r'-[\1]', name)
                match = re.search(r'\[([^\]]+)\]$', label)
                if match:
                    role = label[:match.start()].removesuffix('-')
                    label = match[1] + (f' ({role})' if role != 'paint' else '')
                direction = {0: {-1: 'LEFT', 1: 'RIGHT'}, 1: {-1: 'UNDERSIDE', 1: 'TOP'}, 2: {-1: 'REAR', 1: 'FRONT'}}[axis][sign]
                charts.append({'name': name, 'label': f'{label} / {direction}' + (f' / layer {number + 1}' if number else ''),
                               'entries': layer, 'min': minimum, 'span': maximum - minimum})
        # Shelf pack labelled rectangles at one physical scale. No island rotation
        # or mirrored/stacked artwork: lettering stays upright in side/end views.
        gutter, heading, margin = 16 * size / 4096, 34 * size / 4096, 28 * size / 4096
        charts.sort(key=lambda c: (-float(c['span'][1]), -float(c['span'][0]), c['label']))
        def pack(scale):
            x = y = margin
            row_height = 0
            positions = []
            for chart in charts:
                w, h = chart['span'] * scale + [2 * gutter, 2 * gutter + heading]
                if w > size - 2 * margin: return None
                if x + w > size - margin:
                    x = margin; y += row_height + gutter; row_height = 0
                if y + h > size - margin: return None
                positions.append((x, y, w, h))
                x += w + gutter; row_height = max(row_height, h)
            return positions
        lo, hi = 0., 1000. * size / 4096
        for _ in range(50):
            mid = (lo + hi) / 2
            if pack(mid) is None: hi = mid
            else: lo = mid
        scale = lo * .995
        positions = pack(scale)
        group_for_face = {}
        for ci, (chart, (x, y, w, h)) in enumerate(zip(charts, positions)):
            self.labels.append((x, y, chart['label']))
            self.panels.append({'name': chart['name'], 'label': chart['label'], 'rectanglePixels': [x, y, w, h],
                                'pixelsPerMetre': scale, 'triangles': len(chart['entries'])})
            for mi, fi, projected in chart['entries']:
                pixels = (projected - chart['min']) * scale + [x + gutter, y + heading + gutter]
                arrays[mi][fi] = np.column_stack((pixels[:, 0] / size, 1 - pixels[:, 1] / size))
                group_for_face[mi, fi] = str(ci)
        self.chart_count = len(charts)
        self.utilization = 0
        for mi, (item, triangles, uv) in enumerate(zip(meshes, all_triangles, arrays)):
            self.paint_masks.append(item['selected'].copy())
            groups = [group_for_face.get((mi, fi), 'detail') for fi in range(len(triangles))]
            if not np.array_equal(item['selected'], [g != 'detail' for g in groups]):
                raise ValueError('Paint face coverage mismatch')
            self.results.append((item['faces'].flatten(), np.arange(len(triangles) * 3, dtype=np.uint32).reshape(-1, 3), uv.reshape(-1, 2)))
            self.preview_faces.extend(zip(triangles, uv, groups))
            self.utilization += abs(np.linalg.det(np.stack((uv[:, 1] - uv[:, 0], uv[:, 2] - uv[:, 0]), axis=1))).sum() / 2
        # Validate final coordinates, not merely the pre-pack projections.
        for ci in range(len(charts)):
            entries = [uv for _, uv, group in self.preview_faces if group == str(ci)]
            for i, a in enumerate(entries):
                for b in entries[:i]:
                    if intersection_area(a, b) > 1e-8: self.panel_overlap_count += 1
        if self.panel_overlap_count:
            raise ValueError('Unexpected overlap in grouped atlas')
