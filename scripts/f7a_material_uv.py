"""Recognizable paint-only F7A views; keep authored hardware out of the texture."""
import numpy as np
import xatlas
from dash9_material_uv import MaterialAtlas
from dash9_livery_uv import intersection_area


class F7AAtlas(MaterialAtlas):
    def __init__(self, meshes, size):
        self.width = self.height = size
        self.atlas_count = 1
        self.mesh_count = len(meshes)
        self.paint_only = True
        self.preview_label = 'F7A V1'
        self.labels, self.panels, self.results, self.preview_faces, self.paint_masks = [], [], [], [], []
        self.regions = {
            'left': (80, 100, 3936, 940, 'LEFT SIDE - front at right', 1),
            'right': (80, 1170, 3936, 940, 'RIGHT SIDE - front at left', 1),
            'roof': (80, 2240, 3936, 620, 'ROOF - front at left', 1),
            'front': (80, 3000, 1150, 1010, 'FRONT - nose and cab', 1),
            'rear': (1350, 3000, 1150, 1010, 'REAR END', 1),
            'extra': (2620, 3000, 1396, 1010, 'SECONDARY PAINTED SURFACES', 1),
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
            self.regions[group] = (x, y, w, h, label, scale)
            self.labels.append((x * size / 4096, (y - 44) * size / 4096, label))
            self.panels.append({'name': group, 'rectanglePixels': [v * size / 4096 for v in (x, y, w, h)], 'label': label, 'pixelsPerMetre': scale * size / 4096})
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
                    if axis == 0 and abs(centre[0]) > .25:
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
        if secondary:
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
