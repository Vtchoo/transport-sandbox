"""Livery layout selected strictly by the authored material, without geometry IDs."""
import numpy as np
import xatlas
from dash9_livery_uv import LiveryAtlas, intersection_area


class MaterialAtlas(LiveryAtlas):
    def __init__(self, meshes, size):
        self.width = self.height = size
        self.atlas_count = 1
        self.mesh_count = len(meshes)
        self.paint_only = True
        self.labels, self.panels, self.results, self.preview_faces, self.paint_masks = [], [], [], [], []
        self.regions = {
            'left': (90, 100, 3900, 810, 'LEFT SIDE - front at right', 178),
            'right': (90, 1030, 3900, 810, 'RIGHT SIDE - front at left', 178),
            'roof': (90, 1990, 3900, 660, 'ROOF - top view, front at left', 170),
            'front': (90, 2800, 1200, 1150, 'FRONT VIEW - nose and cab', 170),
            'rear': (1410, 2800, 1200, 1150, 'REAR VIEW', 170),
            'extra': (2730, 2800, 1270, 1150, 'SECONDARY PAINTED SURFACES', 150),
        }
        factor = size / 4096
        for name, (x, y, w, h, label, _) in self.regions.items():
            self.labels.append((x * factor, (y - 44) * factor, label))
            self.panels.append({'name': name, 'rectanglePixels': [v * factor for v in (x, y, w, h)], 'label': label})
        per_mesh = []
        for item in meshes:
            points = item['points'][:, [0, 2, 1]] * [1, 1, -1]
            triangles = points[item['faces']]
            uv = np.zeros((len(triangles), 3, 2))
            groups = []
            for fi, tri in enumerate(triangles):
                group = 'detail'
                if item['selected'][fi]:
                    n = np.cross(tri[1] - tri[0], tri[2] - tri[0]); n /= np.linalg.norm(n)
                    center = tri.mean(axis=0)
                    if abs(n[0]) > .999 and abs(center[0]) > .5:
                        group = 'left' if center[0] < 0 else 'right'
                    elif abs(n[1]) > .6:
                        group = 'roof'
                    elif abs(n[2]) > .6 and center[2] > 7:
                        group = 'front'
                    elif abs(n[2]) > .6 and center[2] < -8:
                        group = 'rear'
                    else: group = 'extra'
                    if group != 'extra':
                        x, y, width, _, _, scale = self.regions[group]
                        if group in ('left', 'right'):
                            px = (11.05 - tri[:, 2]) * scale
                            if group == 'left': px = width - px
                            xy = np.column_stack((x + px, y + (4.7 - tri[:, 1]) * scale))
                        elif group == 'roof':
                            xy = np.column_stack((x + (11.05 - tri[:, 2]) * scale, y + (tri[:, 0] + 1.84) * scale))
                        else:
                            horizontal = tri[:, 0] if group == 'front' else -tri[:, 0]
                            xy = np.column_stack((x + 150 + (horizontal + 1.84) * scale, y + (4.7 - tri[:, 1]) * scale))
                        uv[fi] = np.column_stack((xy[:, 0] / 4096, 1 - xy[:, 1] / 4096))
                groups.append(group)
            per_mesh.append((triangles, uv, groups))
        # Keep recognizable orthographic body views. Surfaces hidden behind a
        # primary view receive independent UVs in the secondary area.
        for panel in ('left', 'right', 'roof', 'front', 'rear'):
            candidates = [(mi, fi, tri) for mi, (triangles, _, groups) in enumerate(per_mesh)
                          for fi, tri in enumerate(triangles) if groups[fi] == panel]
            if panel in ('left', 'right'):
                # A side view must prioritize the outer cabinet face over the
                # larger hood faces behind it, just as a camera sees the model.
                sign = -1 if panel == 'left' else 1
                candidates.sort(key=lambda c: (-round(sign * c[2][:, 0].mean(), 4),
                    -np.linalg.norm(np.cross(c[2][1] - c[2][0], c[2][2] - c[2][0]))))
            else:
                axis = 1 if panel == 'roof' else 2
                sign = -1 if panel == 'rear' else 1
                candidates.sort(key=lambda c: -sign * c[2][:, axis].mean())
            accepted = []
            for mi, fi, _ in candidates:
                uv = per_mesh[mi][1][fi]
                depth = per_mesh[mi][0][fi][:, 0].mean()
                # Side-facing surfaces at different depths share the same
                # projected artwork. This places cabinet doors in their actual
                # body location without cutting holes out of the hood layout.
                overlaps = any(intersection_area(uv, old) > 1e-8 and
                    (panel not in ('left', 'right') or abs(depth - old_depth) < .0001)
                    for old, old_depth in accepted)
                if overlaps: per_mesh[mi][2][fi] = 'extra'
                else: accepted.append((uv, depth))
        secondary = [(mi, fi, tri) for mi, (triangles, _, groups) in enumerate(per_mesh)
                     for fi, tri in enumerate(triangles) if groups[fi] == 'extra']
        secondary_charts = 0
        if secondary:
            points = np.concatenate([e[2] for e in secondary]).astype(np.float32)
            points, inverse = np.unique(points, axis=0, return_inverse=True)
            faces = inverse.reshape(-1, 3).astype(np.uint32)
            automatic = xatlas.Atlas(); automatic.add_mesh(points, faces)
            pack = xatlas.PackOptions(); pack.resolution = 1100; pack.padding = 8
            automatic.generate(pack_options=pack)
            assert automatic.atlas_count == 1
            mapping, indices, texcoords = automatic[0]
            assert np.array_equal(mapping[indices], faces)
            extent = np.array([automatic.width, automatic.height])
            pixels = texcoords[indices] * extent / max(extent) * 1100
            pixels[:, :, 0] += 2730
            pixels[:, :, 1] = 3940 - pixels[:, :, 1]
            uv = np.stack((pixels[:, :, 0] / 4096, 1 - pixels[:, :, 1] / 4096), axis=-1)
            for (mi, fi, _), face_uv in zip(secondary, uv): per_mesh[mi][1][fi] = face_uv
            secondary_charts = automatic.chart_count
        self.chart_count = len(set(g for _, _, groups in per_mesh for g in groups if g != 'detail'))
        self.chart_count += max(0, secondary_charts - 1)
        self.utilization = 0
        for item, (triangles, uv, groups) in zip(meshes, per_mesh):
            self.paint_masks.append(item['selected'].copy())
            assert np.array_equal(item['selected'], [g != 'detail' for g in groups]), 'Every marked face must be mapped'
            self.results.append((item['faces'].flatten(), np.arange(len(triangles) * 3, dtype=np.uint32).reshape(-1, 3), uv.reshape(-1, 2)))
            self.preview_faces.extend(zip(triangles, uv, groups))
            self.utilization += abs(np.linalg.det(np.stack((uv[:, 1] - uv[:, 0], uv[:, 2] - uv[:, 0]), axis=1))).sum() / 2
        if not any(mask.any() for mask in self.paint_masks): raise ValueError('No valid faces use the requested paint material')
        self._check_panels(per_mesh)

    def _check_panels(self, per_mesh):
        self.panel_overlap_count = 0
        self.intentional_side_overlap_count = 0
        entries = [(tri, uv, group) for triangles, uvs, groups in per_mesh
                   for tri, uv, group in zip(triangles, uvs, groups) if group != 'detail']
        for i, (tri, uv, group) in enumerate(entries):
            for other_tri, other_uv, other_group in entries[:i]:
                if intersection_area(uv, other_uv) <= 1e-8: continue
                if group == other_group and group in ('left', 'right') and abs(tri[:, 0].mean() - other_tri[:, 0].mean()) > .0001:
                    self.intentional_side_overlap_count += 1
                else: self.panel_overlap_count += 1
        if self.panel_overlap_count: raise ValueError('Unintended overlap in paint layout')
