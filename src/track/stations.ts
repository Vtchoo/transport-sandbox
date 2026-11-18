import * as THREE from 'three';
import { trackFrame, type TrackNetwork, type Side } from './model';
import { disposeTrack } from './render';
import type { WindowManager } from '../ui/windows';
import { SceneViewport } from '../ui/scene-viewport';
import { PLATFORM_WIDTH, platformEdgeOffset } from './platform-dimensions';
import { stationLengthLabel } from './station-layout';

/** A simple side platform, in metres. Heights are relative to the rail head. */
export function makePlatform(curve: THREE.CubicBezierCurve3, gauge: number, side: Side, preview = false) {
  curve.arcLengthDivisions = 512;
  const steps = Math.max(2, Math.ceil(curve.getLength() / 2));
  const points = curve.getSpacedPoints(steps), anchor = points[0];
  const group = new THREE.Group(); group.position.copy(anchor);
  const edge = platformEdgeOffset(gauge), width = PLATFORM_WIDTH;
  const material = (color: number) => new THREE.MeshStandardMaterial({ color: preview ? 0x7aefc1 : color,
    roughness: 0.95, side: THREE.DoubleSide, transparent: preview, opacity: preview ? 0.45 : 1, depthWrite: !preview });
  const slab = (inner: number, outer: number, bottom: number, top: number, color: number) => {
    const positions: number[] = [], indices: number[] = [];
    const profile = [[inner, bottom], [outer, bottom], [outer, top], [inner, top]];
    points.forEach((point, i) => {
      const right = trackFrame(curve.getTangentAt(i / steps)).right;
      for (const [x, y] of profile) {
        const p = point.clone().sub(anchor).addScaledVector(right, x * side); p.y += y;
        positions.push(p.x, p.y, p.z);
      }
    });
    for (let i = 0; i < steps; i++) for (let j = 0; j < 4; j++) {
      const a = i * 4 + j, b = i * 4 + (j + 1) % 4;
      indices.push(a, b, a + 4, b, b + 4, a + 4);
    }
    for (const base of [0, steps * 4]) indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.setIndex(indices); geometry.computeVertexNormals();
    group.add(new THREE.Mesh(geometry, material(color)));
  };
  slab(edge, edge + width, -0.65, 0.45, 0x94918a);
  slab(edge, edge + 0.22, 0.451, 0.465, 0xe9ce75);
  return group;
}

export class StationPlatforms {
  readonly group = new THREE.Group();
  private meshes = new Map<number, THREE.Group>();
  private views = new Map<number, { element: HTMLElement; canvas: HTMLCanvasElement; length: HTMLElement }>();
  private viewport?: SceneViewport;
  private lastViewport = -Infinity;
  constructor(private network: TrackNetwork, private windows?: WindowManager) {}
  sync(affected?: Set<number>) {
    for (const [id, mesh] of this.meshes) if (!this.network.segments.get(id)?.station || !affected || affected.has(id)) {
      this.group.remove(mesh); disposeTrack(mesh); this.meshes.delete(id);
    }
    for (const edge of this.network.segments.values()) if (edge.station && !this.meshes.has(edge.id)) {
      const mesh = new THREE.Group();
      for (const side of edge.station.sides) {
        const platform = makePlatform(this.network.curve(edge), edge.gauge, side);
        platform.userData.stationId = edge.station.id; mesh.add(platform);
      }
      this.meshes.set(edge.id, mesh); this.group.add(mesh);
    }
    for (const [id, view] of this.views) {
      const edges = this.edges(id);
      if (!edges.length) { this.windows?.unregister(`station:${id}`); view.element.remove(); this.views.delete(id); }
      else view.length.textContent = stationLengthLabel(this.network, id);
    }
  }
  private edges(id: number) { return [...this.network.segments.values()].filter(edge => edge.station?.id === id); }
  open(id: number) {
    if (!this.windows || !this.edges(id).length) return;
    if (!this.views.has(id)) {
      const element = document.createElement('section'); element.className = 'station-window'; element.hidden = true;
      element.dataset.stationId = String(id);
      const heading = document.createElement('div'); heading.className = 'track-panel-heading';
      const title = document.createElement('h2'); title.textContent = `Station ${id}`;
      const close = document.createElement('button'); close.type = 'button'; heading.append(title, close);
      const canvas = document.createElement('canvas'); canvas.className = 'station-viewport';
      canvas.width = 384; canvas.height = 216; canvas.setAttribute('aria-label', `Live view of Station ${id}`);
      const length = document.createElement('p'); length.className = 'station-length';
      length.textContent = stationLengthLabel(this.network, id);
      element.append(heading, canvas, length); document.getElementById('app')!.append(element);
      this.windows.register({ id: `station:${id}`, element, titlebar: heading, title: `Station ${id}`, closeButton: close,
        bounds: { x: 380, y: 100, width: 400, height: 280 }, minWidth: 280, minHeight: 170, onClose: () => {} });
      this.views.set(id, { element, canvas, length });
    }
    this.windows.open(`station:${id}`);
  }
  updateViewports(time: number, renderer: THREE.WebGLRenderer, scene: THREE.Scene) {
    if (time - this.lastViewport < 150) return;
    this.lastViewport = time;
    this.group.updateWorldMatrix(true, true);
    for (const [id, view] of this.views) {
      if (!this.windows?.isExpanded(`station:${id}`)) continue;
      const bounds = new THREE.Box3();
      for (const edge of this.edges(id)) {
        const mesh = this.meshes.get(edge.id);
        if (mesh) bounds.union(new THREE.Box3().setFromObject(mesh));
      }
      if (bounds.isEmpty()) continue;
      this.viewport ??= new SceneViewport();
      this.viewport.render(renderer, scene, view.canvas, bounds);
    }
  }
  pick(ray: THREE.Ray, origin: THREE.Vector2) {
    this.group.updateWorldMatrix(true, true);
    const caster = new THREE.Raycaster(); caster.ray.copy(ray);
    caster.ray.origin.sub(new THREE.Vector3(origin.x, 0, origin.y));
    const hit = caster.intersectObjects(this.group.children, true)[0];
    return hit ? (hit.object.parent!.userData.stationId as number) : undefined;
  }
  dispose() {
    this.viewport?.dispose(); this.viewport = undefined;
    disposeTrack(this.group); this.meshes.clear();
    for (const [id, view] of this.views) { this.windows?.unregister(`station:${id}`); view.element.remove(); }
    this.views.clear();
  }
}
