"""Small orthographic texture preview; no graphics driver or Blender required."""
import numpy as np
from PIL import Image, ImageDraw, ImageFont


def create_preview(atlas, output, size, texture_path=None):
    texture = Image.new('RGB', (size, size), '#555964')
    draw = ImageDraw.Draw(texture)
    step = size // 64
    for y in range(0, size, step):
        for x in range(0, size, step):
            draw.rectangle((x, y, x + step - 1, y + step - 1), fill='#b5bdc8' if (x // step + y // step) % 2 else '#53617a')
    # Same-height stripe and front marker on both sides expose alignment/mirroring errors.
    for name in ('left', 'right'):
        x, y, _, _, _, scale = atlas.regions[name]
        f = size / 4096
        draw.rectangle(tuple(v * f for v in (x, y + (4.7 - 2.9) * scale, x + 3900, y + (4.7 - 2.5) * scale)), fill='#f4bb46')
        marker = 3500 if name == 'left' and 'front at right' in atlas.regions[name][4] else 350
        draw.rectangle(tuple(v * f for v in (x + marker, y, x + marker + 60, y + 810)), fill='#dc554e')
        font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', max(12, round(130 * f)))
        draw.text(((x + 1500) * f, (y + 230) * f), getattr(atlas, 'preview_label', 'DASH 9 4472'), fill='#fff3cd', font=font)
    if texture_path:
        texture = Image.open(texture_path).convert('RGB').resize((size, size))
    else:
        texture.save(output / 'dash9-v2-uv-checker.png')
    pixels = np.asarray(texture)
    width, height = 1400, 680
    sheet = Image.new('RGB', (width, height * 2), '#252c34')
    for view, eye in enumerate(([-26, 15, 33], [26, 15, 33])):
        target = np.array([0., 2., 0.])
        forward = target - eye; forward /= np.linalg.norm(forward)
        right = np.cross(forward, [0, 1, 0]); right /= np.linalg.norm(right)
        up = np.cross(right, forward)
        basis = np.array([right, up, forward]).T
        screen = np.full((height, width, 3), [37, 44, 52], dtype=np.uint8)
        depth = np.full((height, width), np.inf)
        for face_index, (tri, uv, group) in enumerate(atlas.preview_faces):
            projected = (tri - target) @ basis
            xy = projected[:, :2] * [48, -48] + [width / 2, height / 2]
            lo = np.maximum(np.floor(xy.min(axis=0)).astype(int), [0, 0])
            hi = np.minimum(np.ceil(xy.max(axis=0)).astype(int), [width - 1, height - 1])
            if np.any(hi < lo): continue
            a, b = xy[1] - xy[0], xy[2] - xy[0]
            det = a[0] * b[1] - a[1] * b[0]
            if abs(det) < 1e-8: continue
            yy, xx = np.mgrid[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1]
            p = np.stack((xx + .5 - xy[0, 0], yy + .5 - xy[0, 1]), axis=-1)
            s = (p[..., 0] * b[1] - p[..., 1] * b[0]) / det
            t = (a[0] * p[..., 1] - a[1] * p[..., 0]) / det
            weights = np.stack((1 - s - t, s, t), axis=-1)
            z = weights @ projected[:, 2]
            old = depth[yy, xx]
            visible = (weights.min(axis=-1) >= -1e-6) & (z < old)
            coords = weights @ uv
            tx = np.clip((coords[..., 0] * (size - 1)).astype(int), 0, size - 1)
            ty = np.clip(((1 - coords[..., 1]) * (size - 1)).astype(int), 0, size - 1)
            normal = np.cross(tri[1] - tri[0], tri[2] - tri[0]); normal /= np.linalg.norm(normal)
            shade = .65 + .35 * abs(normal @ np.array([.3, .8, .52]))
            colors = (pixels[ty, tx] * shade).astype(np.uint8)
            if group == 'detail' and getattr(atlas, 'paint_only', False) and hasattr(atlas, 'preview_colors'):
                colors[:] = np.array(atlas.preview_colors[face_index]) * shade
            screen[yy[visible], xx[visible]] = colors[visible]
            depth[yy[visible], xx[visible]] = z[visible]
        sheet.paste(Image.fromarray(screen), (0, view * height))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 24)
    for view, label in enumerate(('Left side / front', 'Right side / front')):
        draw.text((28, view * height + 20), label + (' - imported texture' if texture_path else ' - UV checker and stripe test'), fill='white', font=font)
    sheet.save(output / ('dash9-textured-preview.png' if texture_path else 'dash9-v2-uv-preview.png'))
