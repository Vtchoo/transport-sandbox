"""Recognizable paint-only F7A views; keep authored hardware out of the texture."""
import numpy as np
import xatlas
from dash9_material_uv import MaterialAtlas
from dash9_livery_uv import intersection_area


class F7AAtlas(MaterialAtlas):
    def __init__(self, meshes, size, group_rules=None, unfolded=False):
        self.width = self.height = size
        self.atlas_count = 1
        self.mesh_count = len(meshes)
        self.paint_only = True
        self.preview_label = 'F7A UNFOLDED' if unfolded else 'F7A V2' if group_rules else 'F7A V1'
        self.unfolded = unfolded
        self.secondary_panels = []
        self.station_marks = []
        self.labels, self.panels, self.results, self.preview_faces, self.paint_masks = [], [], [], [], []
        self.regions = {
            'left': (80, 100, 3936, 940, 'LEFT SIDE - front at right', 1),
            'right': (80, 1170, 3936, 940, 'RIGHT SIDE - front at left', 1),
            'roof': (80, 2240, 3936, 620, 'ROOF - front at left', 1),
            'front': (80, 3000, 1150, 1010, 'FRONT - nose and cab', 1),
            'rear': (1350, 3000, 1150, 1010, 'REAR END', 1),
            'extra': (2620, 3000, 1396, 1010, 'SECONDARY PAINTED SURFACES', 1),
        }
        if unfolded:
            self.regions = {
                'left': (400, 100, 3400, 900, 'LEFT SIDE - FRONT AT RIGHT - UPRIGHT LETTERING', 200),
                'roof': (400, 1100, 3400, 700, 'ROOF - FRONT AT LEFT - SAME LENGTH SCALE', 200),
                'right': (400, 1900, 3400, 900, 'RIGHT SIDE - FRONT AT LEFT', 200),
                'front': (400, 2960, 850, 1040, 'FRONT / NOSE', 200),
                'rear': (2900, 2960, 850, 1040, 'REAR END', 200),
                'extra': (1400, 2960, 1400, 1040, 'SMALL PARTS - GROUPED BELOW', 200),
            }
        converted = [item['points'][:, [0, 2, 1]] * [1, 1, -1] for item in meshes]
        selected = [points[item['faces'][item['selected']]].reshape(-1, 3)
                    for item, points in zip(meshes, converted) if item['selected'].any()]
        if not selected:
            raise ValueError('No valid faces use the requested paint material')
        selected = np.concatenate(selected)
        minimum, maximum = selected.min(axis=0), selected.max(axis=0)
        self.model_minimum, self.model_maximum = minimum, maximum
        self.preview_top = float(maximum[1])
        extent = maximum - minimum
        margin = 24
        for group, (x, y, w, h, label, _) in list(self.regions.items()):
            horizontal = extent[2] if group in ('left', 'right', 'roof') else extent[0]
            vertical = extent[0] if group == 'roof' else extent[1]
            scale = min((w - 2 * margin) / max(horizontal, .001), (h - 2 * margin) / max(vertical, .001))
            if unfolded: scale = 200
            self.regions[group] = (x, y, w, h, label, scale)
            self.labels.append((x * size / 4096, (y - 44) * size / 4096, label))
            self.panels.append({'name': group, 'rectanglePixels': [v * size / 4096 for v in (x, y, w, h)], 'label': label, 'pixelsPerMetre': scale * size / 4096})
            if unfolded and group in ('left', 'roof', 'right'):
                padding = (w - extent[2] * scale) / 2
                for distance in (2, 5, 8, 11, 14):
                    station = extent[2] - distance if group == 'left' else distance
                    self.station_marks.append({'view': group, 'distanceFromFrontMetres': distance,
                                               'linePixels': [v * size / 4096 for v in (x+padding+station*scale, y, y+h)]})
        per_mesh = []
        for item, points in zip(meshes, converted):
            triangles = points[item['faces']]
            uvs = np.zeros((len(triangles), 3, 2))
            groups = []
            for fi, tri in enumerate(triangles):
                group = 'detail'
                if item['selected'][fi]:
                    normal = np.cross(tri[1] - tri[0], tri[2] - tri[0])
                    normal /= np.linalg.norm(normal)
                    centre = tri.mean(axis=0)
                    axis = int(np.argmax(np.abs(normal)))
                    if group_rules is not None:
                        material = str(item['face_materials'][fi])
                        if material not in group_rules:
                            raise ValueError(f'Authored UV group has no view assignment: {material}')
                        group = group_rules[material]
                        if group == 'roof-front':
                            group = 'roof' if abs(normal[1]) >= abs(normal[2]) else 'front'
                        if group == 'side': group = 'left' if centre[0] < 0 else 'right'
                        projection_axis = 0 if group in ('left', 'right') else 1 if group == 'roof' else 2
                        # Keep whole groups in their assigned view. Only genuinely
                        # edge-on surfaces need a fallback to retain usable UV area.
                        if group != 'extra' and abs(normal[projection_axis]) < .08:
                            if abs(normal[0]) > .3 and abs(centre[0]) > .25:
                                group = 'left' if centre[0] < 0 else 'right'
                            elif group == 'roof' and abs(normal[2]) > .3:
                                group = 'front' if centre[2] > 0 else 'rear'
                            elif group in ('front', 'rear') and centre[1] > minimum[1] + extent[1] * .55 and abs(normal[1]) > .3:
                                group = 'roof'
                            else: group = 'extra'
                    elif axis == 0 and abs(centre[0]) > .25:
                        group = 'left' if centre[0] < 0 else 'right'
                    elif axis == 1 and centre[1] > minimum[1] + extent[1] * .55:
                        group = 'roof'
                    elif axis == 2 and centre[2] > minimum[2] + extent[2] * .7:
                        group = 'front'
                    elif axis == 2 and centre[2] < minimum[2] + extent[2] * .15:
                        group = 'rear'
                    else:
                        group = 'extra'
                    if group not in ('detail', 'extra'):
                        x, y, w, h, _, scale = self.regions[group]
                        if group in ('left', 'right'):
                            u = tri[:, 2] - minimum[2] if group == 'left' else maximum[2] - tri[:, 2]
                            v = maximum[1] - tri[:, 1]
                            span = extent[[2, 1]]
                        elif group == 'roof':
                            u, v = maximum[2] - tri[:, 2], tri[:, 0] - minimum[0]
                            span = extent[[2, 0]]
                        else:
                            u = tri[:, 0] - minimum[0] if group == 'front' else maximum[0] - tri[:, 0]
                            v = maximum[1] - tri[:, 1]
                            span = extent[[0, 1]]
                        # Equal physical scale in U/V preserves lettering proportions.
                        padding = (np.array([w, h]) - span * scale) / 2
                        pixels = np.column_stack((u, v)) * scale + [x, y] + padding
                        uvs[fi] = np.column_stack((pixels[:, 0] / 4096, 1 - pixels[:, 1] / 4096))
                groups.append(group)
            per_mesh.append((triangles, uvs, groups))
        # Keep the outside-visible surface in the primary view. Coplanar duplicate
        # or hidden surfaces get their own patch instead of corrupting that view.
        for group in ('left', 'right', 'roof', 'front', 'rear'):
            axis = 0 if group in ('left', 'right') else 1 if group == 'roof' else 2
            sign = -1 if group in ('left', 'rear') else 1
            faces = [(mi, fi, tri) for mi, (triangles, _, groups) in enumerate(per_mesh)
                     for fi, tri in enumerate(triangles) if groups[fi] == group]
            faces.sort(key=lambda entry: -sign * entry[2][:, axis].mean())
            accepted = []
            for mi, fi, tri in faces:
                uv = per_mesh[mi][1][fi]
                depth = tri[:, axis].mean()
                overlap = any(intersection_area(uv, old) > 1e-8 and
                              (group not in ('left', 'right') or abs(depth - old_depth) < .0001)
                              for old, old_depth in accepted)
                if overlap:
                    per_mesh[mi][2][fi] = 'extra'
                else:
                    accepted.append((uv, depth))
        secondary = [(mi, fi, tri) for mi, (triangles, _, groups) in enumerate(per_mesh)
                     for fi, tri in enumerate(triangles) if groups[fi] == 'extra']
        secondary_charts = 0
        if secondary and unfolded:
            import re
            buckets = {}
            for entry in secondary:
                mi, fi, _ = entry
                name = str(meshes[mi]['face_materials'][fi])
                buckets.setdefault(name, []).append(entry)
            x, y, w, h, _, _ = self.regions['extra']
            row_height = (h - 40) / len(buckets)
            for row, (name, entries) in enumerate(sorted(buckets.items())):
                points = np.concatenate([e[2] for e in entries]).astype(np.float32)
                points, inverse = np.unique(points, axis=0, return_inverse=True)
                faces = inverse.reshape(-1, 3).astype(np.uint32)
                automatic = xatlas.Atlas(); automatic.add_mesh(points, faces)
                pack = xatlas.PackOptions(); pack.resolution = 950; pack.padding = 12
                automatic.generate(pack_options=pack)
                if automatic.atlas_count != 1: raise ValueError('Small-parts group needs more than one atlas')
                mapping, indices, coords = automatic[0]
                if not np.array_equal(mapping[indices], faces): raise ValueError('Small-parts group reordered faces')
                dimensions = np.array([automatic.width, automatic.height])
                pixels = coords[indices] * dimensions
                factor = min((w - 48) / dimensions[0], (row_height - 58) / dimensions[1])
                pixels *= factor
                pixels[:, :, 0] += x + 24 + (w - 48 - dimensions[0] * factor) / 2
                pixels[:, :, 1] = y + 40 + row * row_height + row_height - 20 - pixels[:, :, 1]
                uv = np.stack((pixels[:, :, 0] / 4096, 1 - pixels[:, :, 1] / 4096), axis=-1)
                for (mi, fi, _), coords in zip(entries, uv): per_mesh[mi][1][fi] = coords
                label = re.sub(r'^paint-_([^\[\]]+)_$', r'\1', name)
                self.labels.append((x * size / 4096, (y + 40 + row * row_height) * size / 4096, label.upper()))
                self.secondary_panels.append({'material': name, 'label': label, 'triangles': len(entries),
                                             'rectanglePixels': [v * size / 4096 for v in (x, y+40+row*row_height, w, row_height)]})
                secondary_charts += automatic.chart_count
        elif secondary:
            points = np.concatenate([entry[2] for entry in secondary]).astype(np.float32)
            points, inverse = np.unique(points, axis=0, return_inverse=True)
            faces = inverse.reshape(-1, 3).astype(np.uint32)
            automatic = xatlas.Atlas(); automatic.add_mesh(points, faces)
            pack = xatlas.PackOptions(); pack.resolution = 950; pack.padding = 8
            automatic.generate(pack_options=pack)
            if automatic.atlas_count != 1:
                raise ValueError('Secondary paint faces need more than one atlas')
            mapping, indices, texcoords = automatic[0]
            if not np.array_equal(mapping[indices], faces):
                raise ValueError('Secondary unwrap reordered faces')
            dimensions = np.array([automatic.width, automatic.height])
            pixels = texcoords[indices] * dimensions
            pixels *= 950 / max(dimensions)
            pixels[:, :, 0] += 2800
            pixels[:, :, 1] = 3980 - pixels[:, :, 1]
            uv = np.stack((pixels[:, :, 0] / 4096, 1 - pixels[:, :, 1] / 4096), axis=-1)
            for (mi, fi, _), coords in zip(secondary, uv): per_mesh[mi][1][fi] = coords
            secondary_charts = automatic.chart_count
        self.chart_count = 5 + secondary_charts
        self.utilization = 0
        for item, (triangles, uv, groups) in zip(meshes, per_mesh):
            if not np.array_equal(item['selected'], [group != 'detail' for group in groups]):
                raise ValueError('Not all authored paint faces received UVs')
            self.paint_masks.append(item['selected'].copy())
            self.results.append((item['faces'].flatten(), np.arange(len(triangles) * 3, dtype=np.uint32).reshape(-1, 3), uv.reshape(-1, 2)))
            self.preview_faces.extend(zip(triangles, uv, groups))
            self.utilization += abs(np.linalg.det(np.stack((uv[:, 1] - uv[:, 0], uv[:, 2] - uv[:, 0]), axis=1))).sum() / 2
        self._check_panels(per_mesh)
