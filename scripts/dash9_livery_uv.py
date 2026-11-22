"""Deliberate livery panels, with only small hardware automatically packed."""
import math
import numpy as np
import xatlas


def intersection_area(a, b):
    def cross(c, d): return c[0] * d[1] - c[1] * d[0]
    if np.any(np.minimum(a.max(axis=0), b.max(axis=0)) - np.maximum(a.min(axis=0), b.min(axis=0)) <= 1e-8): return 0
    polygon = list(a)
    orientation = cross(b[1] - b[0], b[2] - b[0])
    for edge in range(3):
        p, q = b[edge], b[(edge + 1) % 3]
        clipped = []
        for index, current in enumerate(polygon):
            previous = polygon[index - 1]
            c = float(cross(q - p, current - p)) * orientation
            d = float(cross(q - p, previous - p)) * orientation
            if (c >= -1e-18) != (d >= -1e-18): clipped.append(previous + (current - previous) * (d / (d - c)))
            if c >= -1e-18: clipped.append(current)
        polygon = clipped
        if not polygon: return 0
    polygon = np.asarray(polygon)
    return abs(np.sum(polygon[:, 0] * np.roll(polygon[:, 1], -1) - polygon[:, 1] * np.roll(polygon[:, 0], -1))) / 2


class LiveryAtlas:
    def __init__(self, meshes, size, paint_only=False):
        self.width = self.height = size
        self.atlas_count = 1
        self.mesh_count = len(meshes)
        self.labels = []
        self.panels = []
        self.results = []
        self.preview_faces = []
        self.paint_only = paint_only
        self.paint_masks = []
        # Coordinates below are in the 4096px painting sheet, not physical units.
        factor = size / 4096
        regions = {
            'left': (90, 100, 3900, 810, 'LEFT SIDE - front at left', 178),
            'right': (90, 1030, 3900, 810, 'RIGHT SIDE - front at left', 178),
            'roof': (90, 1990, 3900, 660, 'ROOF - front at left', 178),
            'cabinet-left': (2800, 2340, 550, 300, 'LEFT CABINET - front at left', 150),
            'cabinet-right': (3420, 2340, 550, 300, 'RIGHT CABINET - front at left', 150),
            'nose': (90, 2780, 570, 700, 'NOSE / FRONT', 150),
            'cab-front': (760, 2780, 570, 700, 'CAB FRONT', 150),
            'rear': (1430, 2780, 570, 700, 'REAR END', 150),
            'cab-back': (2100, 2780, 570, 700, 'CAB REAR', 150),
            'detail': (2800, 2790, 1210, 1210, 'SMALL HARDWARE / UNDERFRAME', 1),
        }
        self.regions = regions
        if paint_only:
            del regions['detail']
            regions['left'] = (*regions['left'][:4], 'LEFT SIDE - front at right (lettering reads normally)', 178)
            regions['cabinet-left'] = (*regions['cabinet-left'][:4], 'LEFT CABINET - front at right', 150)
        for name, (x, y, width, height, label, scale) in regions.items():
            self.labels.append((x * factor, (y - 44) * factor, label))
            self.panels.append({'name': name, 'rectanglePixels': [x * factor, y * factor, width * factor, height * factor], 'label': label})

        details = []
        per_mesh = []
        for mesh_index, item in enumerate(meshes):
            points = item['points'][:, [0, 2, 1]] * np.array([1, 1, -1])
            triangles = points[item['faces']]
            cross = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
            length = np.linalg.norm(cross, axis=1)
            normals = cross / np.maximum(length[:, None], 1e-20)
            centers = triangles.mean(axis=1)
            uv = np.zeros((len(triangles), 3, 2))
            groups = []
            shell = item['geometry'].get('id').endswith(('ID298', 'ID593'))
            for face_index, (tri, normal, center) in enumerate(zip(triangles, normals, centers)):
                group = 'detail'
                n = np.abs(normal)
                if item['part'] != 'body':
                    if not paint_only: group = self.bogie(item, tri, normal, uv[face_index], size)
                elif item['geometry'].get('id').endswith(('ID358', 'ID366')) and n[0] > .999 and abs(center[0]) > 1.5:
                    group = 'cabinet-left' if center[0] < 0 else 'cabinet-right'
                elif shell:
                    if n[0] > .999 and abs(center[0]) > .5 and (not paint_only or center[1] > 1.55):
                        group = 'left' if center[0] < 0 else 'right'
                    elif n[1] > .6 and (center[1] > 3.6 or (paint_only and center[2] > 8.5 and center[1] > 2.8)):
                        group = 'roof'
                    elif n[2] > .65 and (not paint_only or center[1] > 1.65):
                        if center[2] > 9.2: group = 'nose'
                        elif center[2] > 7.5: group = 'cab-front'
                        elif center[2] < -9: group = 'rear'
                        elif 5.8 < center[2] < 6.5: group = 'cab-back'
                groups.append(group)
                if group.startswith('bogie-'):
                    continue
                if group == 'detail':
                    details.append((mesh_index, face_index, tri))
                    continue
                x, y, _, _, _, scale = regions[group]
                if group.startswith('cabinet-'):
                    xy = np.column_stack((x + (6.248 - tri[:, 2]) * scale, y + (3.423 - tri[:, 1]) * scale))
                    if paint_only and group == 'cabinet-left': xy[:, 0] = x + 550 - (xy[:, 0] - x)
                elif group in ('left', 'right'):
                    xy = np.column_stack((x + (11.05 - tri[:, 2]) * scale, y + (4.7 - tri[:, 1]) * scale))
                    if paint_only and group == 'left': xy[:, 0] = x + 3900 - (xy[:, 0] - x)
                elif group == 'roof':
                    xy = np.column_stack((x + (11.05 - tri[:, 2]) * scale, y + (tri[:, 0] + 1.84) * scale))
                else:
                    horizontal = tri[:, 0] if group in ('nose', 'cab-front') else -tri[:, 0]
                    xy = np.column_stack((x + (horizontal + 1.84) * scale, y + (4.7 - tri[:, 1]) * scale))
                uv[face_index] = np.column_stack((xy[:, 0] / 4096, 1 - xy[:, 1] / 4096))
            per_mesh.append((triangles, uv, groups))

        # Unfold the multiple roof/end planes into distinct, labeled patches.
        # A simple orthographic projection would stack the surfaces at different depths.
        self.unfold_planes(per_mesh, 'roof', regions['roof'], size)
        self.unfold_planes(per_mesh, 'nose', regions['nose'], size)
        # Keep full side silhouettes aligned. Only small occluded inset caps that
        # would overlap those silhouettes go to the hardware patch.
        for side in ('left', 'right'):
            candidates = [(mi, fi, tri, uv[fi]) for mi, (triangles, uv, groups) in enumerate(per_mesh)
                          for fi, tri in enumerate(triangles) if groups[fi] == side]
            candidates.sort(key=lambda c: -np.linalg.norm(np.cross(c[2][1] - c[2][0], c[2][2] - c[2][0])))
            accepted = []
            for mi, fi, tri, uv in candidates:
                if any(intersection_area(uv, old) > 1e-8 for old in accepted):
                    per_mesh[mi][2][fi] = 'detail'
                    details.append((mi, fi, tri))
                else: accepted.append(uv)

        # One connected unwrap input lets adjacent hardware faces remain together.
        if not paint_only:
            self.pack_details(details, per_mesh, regions)

        self.chart_count = (0 if paint_only else self.hardware_chart_count) + len(set(g for _, _, groups in per_mesh for g in groups if g != 'detail'))
        self.utilization = sum(abs(np.linalg.det(np.stack((uv[:, 1] - uv[:, 0], uv[:, 2] - uv[:, 0]), axis=1))).sum() / 2 for _, uv, _ in per_mesh)
        for item, (triangles, uv, groups) in zip(meshes, per_mesh):
            count = len(item['faces'])
            self.paint_masks.append(np.array([g != 'detail' for g in groups]))
            self.results.append((item['faces'].flatten(), np.arange(count * 3, dtype=np.uint32).reshape(-1, 3), uv.reshape(-1, 2)))
            self.preview_faces.extend(zip(triangles, uv, groups))
        self._check_panels(per_mesh)

    def pack_details(self, details, per_mesh, regions):
        detail_points = np.concatenate([d[2] for d in details], axis=0).astype(np.float32)
        unique, inverse = np.unique(np.round(detail_points, 4), axis=0, return_inverse=True)
        faces = inverse.reshape(-1, 3).astype(np.uint32)
        area = np.linalg.norm(np.cross(unique[faces[:, 1]] - unique[faces[:, 0]], unique[faces[:, 2]] - unique[faces[:, 0]]), axis=1)
        valid = area > 1e-10
        atlas = xatlas.Atlas()
        atlas.add_mesh(unique, faces[valid])
        pack = xatlas.PackOptions()
        pack.resolution = 1024
        pack.padding = 2
        atlas.generate(pack_options=pack)
        if atlas.atlas_count != 1:
            raise ValueError('Hardware did not fit in its reserved patch')
        mapping, indices, texcoords = atlas[0]
        if not np.array_equal(mapping[indices], faces[valid]):
            raise ValueError('Hardware face correspondence changed')
        hardware_uv = np.zeros((len(details), 3, 2))
        hardware_uv[valid] = texcoords[indices]
        x, y, width, height, _, _ = regions['detail']
        aspect = np.array([atlas.width, atlas.height]) / max(atlas.width, atlas.height)
        hardware_uv *= aspect
        hardware_uv[:, :, 0] = (x + hardware_uv[:, :, 0] * width) / 4096
        hardware_uv[:, :, 1] = 1 - (y + (1 - hardware_uv[:, :, 1]) * height) / 4096
        for (mesh_index, face_index, _), face_uv in zip(details, hardware_uv):
            per_mesh[mesh_index][1][face_index] = face_uv

        self.hardware_chart_count = atlas.chart_count

    def bogie(self, item, tri, normal, output, size):
        rear = item['part'] == 'bogie-rear'
        pivot = -7.0485 if rear else 7.0485
        local = tri - [0, 0, pivot]
        axis = int(np.argmax(np.abs(normal)))
        center = local.mean(axis=0)
        if axis == 0:
            face = 0 if center[0] < 0 else 1
            a, b = 2.032 - local[:, 2], 1.067 - local[:, 1]
        elif axis == 1:
            face = 2 if center[1] > .5 else 5
            a, b = 2.032 - local[:, 2], local[:, 0] + .7175
        else:
            face = 3 if center[2] > 0 else 4
            a, b = local[:, 0] + .7175, 1.067 - local[:, 1]
        x = (1430 if rear else 90) + (face % 3) * 370
        y = 3600 + (face // 3) * 210
        xy = np.column_stack((x + a * 72, y + b * 72))
        output[:] = np.column_stack((xy[:, 0] / 4096, 1 - xy[:, 1] / 4096))
        label = ('REAR' if rear else 'FRONT') + ' BOGIE - ' + ['LEFT', 'RIGHT', 'TOP', 'FRONT', 'BACK', 'BOTTOM'][face]
        entry = (x * size / 4096, (y - 27) * size / 4096, label)
        if entry not in self.labels:
            self.labels.append(entry)
        return 'bogie-' + str(int(rear)) + '-' + str(face)

    def unfold_planes(self, per_mesh, group, region, size):
        planes = {}
        for mi, (triangles, uv, groups) in enumerate(per_mesh):
            for fi, tri in enumerate(triangles):
                if groups[fi] != group: continue
                n = np.cross(tri[1] - tri[0], tri[2] - tri[0]); n /= np.linalg.norm(n)
                axis = np.argmax(np.abs(n))
                if n[axis] < 0: n = -n
                key = tuple(np.round(n, 3)) + (round(float(n @ tri.mean(axis=0)), 2),)
                planes.setdefault(key, []).append((mi, fi, tri, n))
        patches = []
        for entries in planes.values():
            normal = entries[0][3]
            horizontal = np.array([0., 0., -1.]) if group == 'roof' else np.array([1., 0., 0.])
            if abs(np.dot(horizontal, normal)) > .99: horizontal = np.array([0., 0., -1.])
            horizontal -= normal * np.dot(horizontal, normal)
            horizontal /= np.linalg.norm(horizontal)
            vertical = np.cross(normal, horizontal)
            if (group == 'roof' and vertical[0] < 0) or (group != 'roof' and vertical[1] > 0): vertical = -vertical
            projected = [np.column_stack((e[2] @ horizontal, e[2] @ vertical)) for e in entries]
            all_points = np.concatenate(projected)
            minimum, maximum = all_points.min(axis=0), all_points.max(axis=0)
            patches.append((entries, projected, minimum, maximum - minimum))
        x, y, width, height, _, desired_scale = region
        gap = 18
        scale = desired_scale
        for attempt in range(60):
            free = [(0, 0, width, height)]; placements = {}
            for index in sorted(range(len(patches)), key=lambda i: -patches[i][3][1]):
                w, h = patches[index][3] * scale + [gap, gap + 30]
                choices = [(fw * fh - w * h, j) for j, (_, _, fw, fh) in enumerate(free) if fw >= w and fh >= h]
                if not choices: break
                _, j = min(choices); px, py, fw, fh = free.pop(j)
                placements[index] = (px + gap / 2, py + 30)
                if fw - w > 0: free.append((px + w, py, fw - w, h))
                if fh - h > 0: free.append((px, py + h, fw, fh - h))
            if len(placements) == len(patches): break
            scale *= .94
        else: raise ValueError('Could not arrange planar ' + group + ' patches')
        for index, (entries, projected, minimum, extent) in enumerate(patches):
            px, py = placements[index]
            for (mi, fi, _, _), points in zip(entries, projected):
                xy = (points - minimum) * scale + [x + px, y + py]
                per_mesh[mi][1][fi] = np.column_stack((xy[:, 0] / 4096, 1 - xy[:, 1] / 4096))
                per_mesh[mi][2][fi] = group + '-plane-' + str(index)
            self.labels.append(((x + px) * size / 4096, (y + py - 25) * size / 4096, group.upper() + ' ' + str(index + 1)))

    def _check_panels(self, per_mesh):
        # Detect stacked primary surfaces instead of silently delivering a bad paint map.
        panels = {}
        for triangles, uv, groups in per_mesh:
            for face, group in zip(uv, groups):
                if group == 'detail': continue
                panels.setdefault(group, []).append(face)
        self.panel_overlap_count = 0
        for name, triangles in panels.items():
            for i, a in enumerate(triangles):
                for b in triangles[:i]:
                    area = intersection_area(a, b)
                    if area > 1e-8:
                        self.panel_overlap_count += 1
                        print('PRIMARY UV OVERLAP', name, area)
        if self.panel_overlap_count:
            raise ValueError('Primary livery panels overlap; layout needs adjustment')

    def __getitem__(self, index):
        return self.results[index]

    def get_mesh_chart_count(self, index):
        return 0  # Semantic panels and the hardware patch are reported separately.
