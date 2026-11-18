import * as THREE from 'three';
import { TrackNetwork, GAUGES, trackFrame } from './model';
import type { TrackNode, TrackSegment, Side } from './model';
import { makeTrack, disposeTrack, updateSleeperLOD } from './render';
import { WorldGrid } from './grid';
import type { TerrainWorld } from '../terrain/world';
import { makePlatform, StationPlatforms } from './stations';
import type { WindowManager } from '../ui/windows';
import { icon } from '../ui/icons';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id)! as T;
const ACTION_DISTANCE = 600;
interface Pick { node: TrackNode; edge?: TrackSegment; spacing?: boolean }
interface Drag { pick: Pick; pointer: number; anchor: THREE.Vector3; screenY: number; vertical: boolean; changed: boolean; initialOffset?: number; affected: Set<number> }
export class TrackEditor {
  readonly network = new TrackNetwork();
  readonly group = new THREE.Group();
  readonly stations: StationPlatforms;
  private stationMode = false;
  private rails = new THREE.Group();
  private segmentMeshes = new Map<number, THREE.Group>();
  private gizmos = new THREE.Group();
  private grid = new WorldGrid();
  private gridPoint?: THREE.Vector3;
  private lastGridSpacing = 5;
  private ghost?: THREE.Group;
  private sphere = new THREE.SphereGeometry(1, 12, 8);
  private nodeMaterial = new THREE.MeshBasicMaterial({ color: 0xf7df9e, depthTest: false });
  private handleMaterial = new THREE.MeshBasicMaterial({ color: 0x69d9dd, depthTest: false });
  private selectedMaterial = new THREE.MeshBasicMaterial({ color: 0xffffff, depthTest: false });
  private splitMaterial = new THREE.MeshBasicMaterial({ color: 0x7aefc1, transparent: true, opacity: 0.6, depthTest: false, depthWrite: false });
  private splitMarker = new THREE.Mesh(this.sphere, this.splitMaterial);
  private lineMaterial = new THREE.LineBasicMaterial({ color: 0x69d9dd, depthTest: false });
  private spacingMaterial = new THREE.MeshBasicMaterial({ color: 0xd19aff, depthTest: false });
  private spacingLineMaterial = new THREE.LineBasicMaterial({ color: 0xd19aff, depthTest: false });
  private markers: { object: THREE.Mesh; pick: Pick }[] = [];
  private directionButton = $<HTMLButtonElement>('track-direction');
  private continueParallel = $<HTMLButtonElement>('track-continue-parallel');
  private buildingParallel = false;
  private parallelLeft = $<HTMLButtonElement>('track-parallel-left');
  private parallelRight = $<HTMLButtonElement>('track-parallel-right');
  private straightenButton = $<HTMLButtonElement>('track-straighten');
  private deleteSegmentButton = $<HTMLButtonElement>('track-delete-segment');
  private splitTarget?: { edge: number; t: number; point: THREE.Vector3 };
  private pendingSplit?: { edge: TrackSegment; t: number; point: THREE.Vector3 };
  private splitButton = $<HTMLButtonElement>('track-confirm-split');
  private confirmDeleteSegment?: number;
  private deleteButton = $<HTMLButtonElement>('track-delete');
  private mergeButton = $<HTMLButtonElement>('track-merge');
  private confirmMerge?: number;
  private confirmDelete?: number;
  private hoveredSegment?: number;
  private confirmStraighten?: number;
  private midpoints = new Map<number, THREE.Vector3>();
  private splitSamples = new Map<number, { curve: THREE.CubicBezierCurve3; points: THREE.Vector3[] }>();
  private abort = new AbortController();
  private raycaster = new THREE.Raycaster();
  private drag?: Drag;
  private selected?: number;
  private start?: { node: TrackNode; side: Side };
  private hoverDirection?: { node: TrackNode; side: Side };
  private lastPointer?: PointerEvent;
  private viewSignature = '';
  private dirty = false;
  private earthDirty = false;
  private earthAt = 0;
  private lastBuild = 0;
  private ghostKey = '';
  private endRotation = 0;
  private visible = false;
  gauge = 1435;
  onClose?: () => void;
  constructor(private scene: THREE.Scene, private camera: THREE.PerspectiveCamera, private canvas: HTMLCanvasElement, private world: TerrainWorld, windows?: WindowManager) {
    this.stations = new StationPlatforms(this.network, windows); this.group.add(this.stations.group);
    this.group.add(this.rails, this.gizmos); scene.add(this.group);
    this.group.add(this.grid); this.grid.visible = false;
    for (const [id, station] of [['track-tab', false], ['station-tab', true]] as const) {
      $(id).prepend(icon(station ? 'station' : 'track'));
      $(id).addEventListener('click', () => {
        this.cancelPlacement(); this.endDrag(); this.hideNodeButtons();
        this.stationMode = station; this.gizmos.visible = this.enabled && !station;
        $('track-tools').hidden = station; $('station-tools').hidden = !station;
        $('track-selection').hidden = station;
        document.querySelector<HTMLElement>('#track-panel .track-help:last-child')!.hidden = station;
        $('track-tab').setAttribute('aria-selected', String(!station));
        $('station-tab').setAttribute('aria-selected', String(station));
        this.grid.showAt(undefined, false);
        this.message(station ? 'Hover beside an existing track, then click to build a platform on that side. Junction segments are excluded.' : 'Click terrain to place the first node, or use a node’s + button.');
      }, { signal: this.abort.signal });
    }
    $('station-action').addEventListener('change', () => this.clearGhost(), { signal: this.abort.signal });
    const gridSelect = $<HTMLSelectElement>('track-grid');
    gridSelect.value = '0';
    gridSelect.addEventListener('change', () => {
      this.grid.setSpacing(Number(gridSelect.value));
      if (this.grid.spacing) this.lastGridSpacing = this.grid.spacing;
      this.clearGhost();
      if (this.lastPointer && !this.drag) this.pointerMove(this.lastPointer);
      this.message(this.grid.spacing ? `World grid snap · ${this.grid.spacing} m. Existing nodes and Shift straight snap take priority.` : 'World grid snap off.');
    }, { signal: this.abort.signal });
    this.splitMarker.visible = false; this.splitMarker.renderOrder = 21; this.group.add(this.splitMarker);
    this.splitButton.hidden = true;
    this.splitButton.addEventListener('click', () => {
      const pending = this.pendingSplit;
      if (!this.enabled || this.drag || !pending || !this.actionsInRange(pending.point)) return;
      try {
        const { node } = this.network.split(pending.edge, pending.t);
        this.pendingSplit = undefined; this.selected = node.id;
        this.changed(); this.clearGhost(); this.rebuild(); this.commitEarthworks();
        this.message('Track split. Drag the new node to edit it, or use its + button to build a track.');
      } catch (error) { this.cancelPlacement(); this.message((error as Error).message); }
    }, { signal: this.abort.signal });
    const select = $<HTMLSelectElement>('track-gauge');
    select.replaceChildren(...GAUGES.map(gauge => new Option(gauge.label, String(gauge.mm))));
    select.value = String(this.gauge);
    select.addEventListener('change', () => {
      this.cancelPlacement(); this.gauge = Number(select.value);
      this.message('Click terrain to place the first node, or use a node’s + button.');
    }, { signal: this.abort.signal });
    this.directionButton.addEventListener('click', () => {
      const direction = this.hoverDirection;
      if (!direction) return;
      if (!this.network.canExtend(direction.node, direction.side)) return;
      if (direction.node.gauge !== this.gauge) { this.message(`Gauge mismatch. Select ${direction.node.gauge.toLocaleString()} mm to use this node.`); return; }
      this.cancelPlacement(); this.start = direction; this.selected = direction.node.id;
      this.directionButton.hidden = true; this.refreshGizmos();
      this.message('Choose the next node. Keep clicking to extend; Esc stops placement.');
    }, { signal: this.abort.signal });
    this.continueParallel.replaceChildren(icon('add'), icon('parallel'));
    this.continueParallel.addEventListener('click', () => {
      const direction = this.hoverDirection;
      if (!this.enabled || this.drag || !direction || !this.network.parallelGroup(direction.node) || !this.actionsInRange(direction.node.position)) return;
      if ([...this.network.parallelGroup(direction.node)!.offsets.keys()].some(id => !this.network.canExtend(this.network.nodes.get(id)!, direction.side))) return;
      this.cancelPlacement(); this.start = direction; this.buildingParallel = true; this.selected = direction.node.id;
      this.gauge = direction.node.gauge; select.value = String(this.gauge);
      this.hideNodeButtons(); this.refreshGizmos();
      this.message('Building all parallel tracks. Click to place their next nodes; keep clicking to extend. Esc stops.');
    }, { signal: this.abort.signal });
    this.straightenButton.addEventListener('click', () => {
      if (!this.enabled || this.drag || this.hoveredSegment === undefined) return;
      this.resetMerge();
      this.resetDelete();
      this.hideSplit();
      const edge = this.network.segments.get(this.hoveredSegment);
      if (!edge) return;
      this.resetSegmentDelete();
      if (this.confirmStraighten !== edge.id) {
        this.confirmStraighten = edge.id;
        this.straightenButton.textContent = 'Confirm straightening';
        this.straightenButton.classList.add('confirm');
        this.straightenButton.setAttribute('aria-label', `Confirm straightening track segment ${edge.id}`);
        this.message('Click the same button again to straighten. Endpoint positions stay fixed; connected tracks will follow their updated directions. Esc cancels.');
        return;
      }
      try {
        this.network.straighten(edge);
        this.changed(); this.clearGhost(); this.rebuild(); this.commitEarthworks();
        this.message(`Segment ${edge.id} straightened. Connected tracks remain tangent-aligned.`);
      } catch (error) { this.hideStraighten(); this.message((error as Error).message); }
    }, { signal: this.abort.signal });
    this.deleteSegmentButton.addEventListener('click', () => {
      if (!this.enabled || this.drag || this.hoveredSegment === undefined) return;
      const edge = this.network.segments.get(this.hoveredSegment);
      if (!edge) return;
      this.resetMerge();
      this.resetDelete();
      this.hideSplit();
      if (this.confirmDeleteSegment !== edge.id) {
        this.confirmStraighten = undefined;
        this.straightenButton.textContent = 'Straighten'; this.straightenButton.classList.remove('confirm');
        this.straightenButton.setAttribute('aria-label', `Straighten track segment ${edge.id}`);
        this.confirmDeleteSegment = edge.id;
        this.deleteSegmentButton.textContent = 'Confirm delete track'; this.deleteSegmentButton.classList.add('confirm');
        this.deleteSegmentButton.setAttribute('aria-label', `Confirm deletion of track segment ${edge.id}`);
        this.message(`Delete segment ${edge.id}? Both endpoint nodes will remain. Click again to confirm; Esc cancels.`);
        return;
      }
      this.network.segments.delete(edge.id);
      this.clearGhost(); this.changed(); this.rebuild(new Set([edge.id])); this.commitEarthworks();
      this.message(`Segment ${edge.id} deleted. Both endpoint nodes preserved.`);
    }, { signal: this.abort.signal });
    for (const [button, side] of [[this.parallelLeft, -1], [this.parallelRight, 1]] as const) button.addEventListener('click', () => {
      const source = this.hoverDirection?.node;
      if (!source || !this.enabled || this.drag || !this.actionsInRange(source.position)) return;
      if (this.start && source.gauge !== this.gauge) {
        this.message(`Cannot connect ${this.gauge.toLocaleString()} mm to ${source.gauge.toLocaleString()} mm.`); return;
      }
      const continuing = !!this.start;
      this.hideSplit();
      // Preserve an isolated source node when creating its parallel partner.
      this.clearGhost(); this.hideStraighten(); this.hideNodeButtons();
      const node = this.network.addParallel(source, side);
      if (continuing) {
        if (!this.placeSecond(node.position, node)) this.network.deleteNode(node.id);
        this.refreshGizmos();
        return;
      }
      this.gauge = node.gauge; $<HTMLSelectElement>('track-gauge').value = String(node.gauge);
      this.selected = node.id; this.start = { node, side: 1 };
      this.refreshGizmos();
      this.message(`Parallel node added · ${node.position.distanceTo(source.position).toFixed(3)} m spacing. Drag its purple handle to adjust; click terrain to extend.`);
    }, { signal: this.abort.signal });
    $('track-done').addEventListener('click', () => this.onClose ? this.onClose() : this.setEnabled(false), { signal: this.abort.signal });
    this.deleteButton.addEventListener('click', () => {
      if (!this.enabled || this.drag || this.deleteButton.hidden || !this.hoverDirection) return;
      const node = this.network.nodes.get(this.hoverDirection.node.id);
      if (!node) return;
      this.resetMerge();
      if (this.confirmDelete !== node.id) {
        this.hideSplit();
        this.hideStraighten(); this.confirmDelete = node.id;
        this.deleteButton.textContent = '✓'; this.deleteButton.classList.add('confirm');
        this.deleteButton.title = `Confirm deletion of node ${node.id}`;
        this.deleteButton.setAttribute('aria-label', this.deleteButton.title);
        this.message(`Delete node ${node.id} and its ${this.network.incident(node.id).length} connected tracks? Other nodes will remain. Click again to confirm; Esc cancels.`);
        return;
      }
      if (this.start?.node.id === node.id) { this.start = undefined; this.buildingParallel = false; }
      this.network.deleteNode(node.id);
      if (this.selected === node.id) this.selected = undefined;
      this.hideNodeButtons(); this.clearGhost(); this.changed(); this.rebuild(); this.commitEarthworks();
      this.message(`Node ${node.id} and its connected tracks deleted. Other nodes preserved.`);
    }, { signal: this.abort.signal });
    this.mergeButton.addEventListener('click', () => {
      const node = this.hoverDirection?.node;
      if (!this.enabled || this.drag || this.mergeButton.hidden || !node || !this.actionsInRange(node.position) || !this.network.canMerge(node.id)) return;
      this.resetDelete(); this.hideSplit(); this.hideStraighten();
      if (this.confirmMerge !== node.id) {
        this.confirmMerge = node.id; this.mergeButton.textContent = '✓'; this.mergeButton.classList.add('confirm');
        this.mergeButton.title = `Confirm merging tracks through node ${node.id}`;
        this.mergeButton.setAttribute('aria-label', this.mergeButton.title);
        this.message('Click again to remove this node and merge its two tracks. Outer endpoint handles stay unchanged; the curve may change. Esc cancels.');
        return;
      }
      try {
        const removed = new Set(this.network.incident(node.id).map(edge => edge.id));
        this.network.merge(node.id);
        if (this.start?.node.id === node.id) { this.start = undefined; this.buildingParallel = false; }
        if (this.selected === node.id) this.selected = undefined;
        this.hideNodeButtons(); this.clearGhost(); this.changed(); this.rebuild(removed); this.commitEarthworks();
        this.message(`Node ${node.id} removed and its tracks merged. Outer endpoint handles and remaining parallel nodes preserved.`);
      } catch (error) { this.resetMerge(); this.message((error as Error).message); }
    }, { signal: this.abort.signal });
    this.setEnabled(false);
  }
  get enabled() { return this.visible; }
  get isDragging() { return !!this.drag; }
  toggleGrid() {
    if (!this.enabled || this.stationMode) return;
    const select = $<HTMLSelectElement>('track-grid');
    select.value = String(this.grid.spacing ? 0 : this.lastGridSpacing);
    select.dispatchEvent(new Event('change'));
  }
  setEnabled(enabled: boolean, keepOpen = false) {
    this.hideSplit();
    this.resetDelete();
    this.visible = enabled; this.gizmos.visible = enabled && !this.stationMode;
    this.grid.showAt(this.gridPoint, enabled);
    $('track-panel').hidden = !(enabled || keepOpen);
    $('build-track').setAttribute('aria-pressed', String(enabled));
    this.hideNodeButtons();
    this.hideStraighten();
    if (!enabled) { this.cancelPlacement(); this.endDrag(); this.clearGhost(); }
    else this.message(this.stationMode ? 'Hover beside an existing track, then click to build a platform.' : 'Click terrain to place the first node, or use a node’s + button.');
    this.refreshGizmos();
  }
  private message(text: string) { $('track-status').textContent = text; }
  private actionsInRange(point: THREE.Vector3) {
    const dx = this.camera.position.x + this.world.origin.x - point.x;
    const dy = this.camera.position.y - point.y;
    const dz = this.camera.position.z + this.world.origin.y - point.z;
    return dx * dx + dy * dy + dz * dz <= ACTION_DISTANCE * ACTION_DISTANCE;
  }
  private resetDelete() {
    this.confirmDelete = undefined; this.deleteButton.textContent = '×'; this.deleteButton.classList.remove('confirm');
    this.deleteButton.title = this.hoverDirection ? `Delete node ${this.hoverDirection.node.id}` : 'Delete node';
    this.deleteButton.setAttribute('aria-label', this.deleteButton.title);
  }
  private resetMerge() {
    this.confirmMerge = undefined; this.mergeButton.textContent = '⇄'; this.mergeButton.classList.remove('confirm');
    this.mergeButton.title = this.hoverDirection ? `Merge segments through node ${this.hoverDirection.node.id}` : 'Merge segments';
    this.mergeButton.setAttribute('aria-label', this.mergeButton.title);
  }
  private hideNodeButtons() {
    this.continueParallel.hidden = true;
    this.directionButton.hidden = true; this.parallelLeft.hidden = true; this.parallelRight.hidden = true;
    this.deleteButton.hidden = true; this.hoverDirection = undefined; this.resetDelete();
    this.mergeButton.hidden = true; this.resetMerge();
  }
  private spacingPosition(node: TrackNode) {
    const group = this.network.parallelGroup(node)!;
    const right = trackFrame(group.axis).right;
    const center = this.project(node.position), projectedRight = this.project(node.position.clone().add(right));
    const pixelsPerMetre = Math.hypot(projectedRight.x - center.x, projectedRight.y - center.y);
    const eye = this.camera.position.clone().add(new THREE.Vector3(this.world.origin.x, 0, this.world.origin.y));
    const units = eye.distanceTo(node.position) * 2 * Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2)) / this.canvas.clientHeight;
    const distance = pixelsPerMetre > 0.01 ? 25 / pixelsPerMetre : units * 25;
    return node.position.clone().addScaledVector(right, (Math.sign(group.offsets.get(node.id)!) || 1) * Math.max(0.2, distance));
  }
  private project(point: THREE.Vector3) {
    const projected = point.clone().sub(new THREE.Vector3(this.world.origin.x, 0, this.world.origin.y)).project(this.camera);
    const rect = this.canvas.getBoundingClientRect();
    return { x: rect.left + (projected.x + 1) * rect.width / 2, y: rect.top + (1 - projected.y) * rect.height / 2, visible: projected.z >= -1 && projected.z <= 1 };
  }
  private ray(event: { clientX: number; clientY: number }) {
    const rect = this.canvas.getBoundingClientRect();
    this.camera.updateMatrixWorld();
    this.raycaster.setFromCamera(new THREE.Vector2((event.clientX - rect.left) / rect.width * 2 - 1, 1 - (event.clientY - rect.top) / rect.height * 2), this.camera);
    const ray = this.raycaster.ray.clone();
    ray.origin.x += this.world.origin.x; ray.origin.z += this.world.origin.y;
    return ray;
  }
  private terrainPoint(event: { clientX: number; clientY: number }) {
    const ray = this.ray(event), point = new THREE.Vector3();
    let previous = 0;
    for (let t = 5; t <= 10000; t += 15) {
      ray.at(t, point);
      if (point.y <= this.world.groundHeight(point.x, point.z)) {
        let low = previous, high = t;
        for (let i = 0; i < 18; i++) {
          const middle = (low + high) / 2; ray.at(middle, point);
          if (point.y > this.world.groundHeight(point.x, point.z)) low = middle; else high = middle;
        }
        ray.at((low + high) / 2, point);
        if (!this.world.hasTerrainAt(point.x, point.z)) return undefined;
        point.y = this.world.groundHeight(point.x, point.z) + 0.65;
        return point;
      }
      previous = t;
    }
    return undefined;
  }
  private pick(event: { clientX: number; clientY: number }, nodesOnly = false): Pick | undefined {
    let best = 11, result: Pick | undefined;
    for (const marker of this.markers) {
      if (nodesOnly && (marker.pick.edge || marker.pick.spacing)) continue;
      const p = this.project(marker.object.position);
      const distance = Math.hypot(p.x - event.clientX, p.y - event.clientY);
      if (p.visible && distance < best) { best = distance; result = marker.pick; }
    }
    return result;
  }
  private placementPoint(point: THREE.Vector3, straight: boolean) {
    if (!straight || !this.start) return this.grid ? this.grid.snap(point) : point;
    const { node, side } = this.start;
    const direction = node.axis.clone().normalize();
    const distance = point.clone().sub(node.position).dot(direction);
    // Keep the selected outgoing side.
    const along = side * Math.max(0, distance * side);
    return node.position.clone().addScaledVector(direction, along);
  }
  modifiersChanged(shiftKey: boolean) {
    if (!this.enabled || this.drag || !this.start || !this.lastPointer || this.lastPointer.shiftKey === shiftKey) return;
    const { clientX, clientY, pointerId, buttons } = this.lastPointer;
    this.pointerMove({ clientX, clientY, pointerId, buttons, shiftKey } as PointerEvent);
  }
  rotateEnd(direction: Side) {
    if (!this.enabled || !this.start || this.drag || !this.lastPointer) return;
    if (this.pick(this.lastPointer, true)) { this.message('Existing nodes keep their shared direction. Rotate a new end node instead.'); return; }
    this.endRotation += direction * THREE.MathUtils.degToRad(5);
    this.clearGhost(); this.pointerMove(this.lastPointer);
    if (this.ghost) this.message(`End direction: ${Math.round(THREE.MathUtils.radToDeg(this.endRotation))}° · Z / X rotate · click to place.`);
  }
  private endDirection(point: THREE.Vector3) {
    return this.start && this.endRotation ? point.clone().sub(this.start.node.position).normalize().applyAxisAngle(new THREE.Vector3(0, 1, 0), this.endRotation) : undefined;
  }
  pointerDown(event: PointerEvent) {
    if (!this.enabled || event.button !== 0) return false;
    if (this.stationMode) {
      this.stationHover(event);
      const target = this.splitTarget, edge = target && this.network.segments.get(target.edge);
      if (!edge) { this.message('Choose an existing track segment.'); return true; }
      const side = this.platformSide(event, edge, target!.t);
      if ($<HTMLSelectElement>('station-action').value === 'remove') {
        if (edge.station?.sides.includes(side)) { this.network.removePlatform(edge, side); this.stations.sync(); this.clearGhost(); this.message('Platform removed.'); }
        else this.message('This side has no platform.');
      } else if (edge.station?.sides.includes(side)) this.stations.open(edge.station.id);
      else {
        try {
          const station = this.network.addStation(edge, side);
          this.stations.sync(); this.clearGhost(); this.message(`Platform added to Station ${station.id}. Click a platform to open its window.`);
        } catch (error) { this.message((error as Error).message); }
      }
      return true;
    }
    const cancellingSplit = !!this.pendingSplit;
    if (cancellingSplit) this.cancelPlacement();
    const pick = this.pick(event);
    if (!this.start && !pick) {
      this.hoverSplit(event);
      const target = this.splitTarget;
      if (target) {
        const edge = this.network.segments.get(target.edge)!;
        this.resetMerge(); this.resetDelete(); this.hideStraighten(); this.hideNodeButtons();
        this.pendingSplit = { edge, t: target.t, point: target.point.clone() }; this.selected = undefined;
        this.gauge = edge.gauge; $<HTMLSelectElement>('track-gauge').value = String(this.gauge);
        this.hideSplit(); this.refreshGizmos();
        this.message('Click Confirm split to add this node. Esc or clicking away cancels.');
        return true;
      }
    }
    if (cancellingSplit && !pick) { this.message('Split cancelled.'); return true; }
    this.hideSplit();
    this.resetDelete();
    this.hideStraighten();
    this.hideNodeButtons();
    if (this.start && (!pick || !pick.edge && !pick.spacing && pick.node.id !== this.start.node.id)) {
      const point = pick?.node.position ?? this.terrainPoint(event);
      if (point) this.placeSecond(pick ? point : this.placementPoint(point, event.shiftKey), pick?.node);
      return true;
    }
    if (pick) {
      this.selected = pick.node.id;
      const anchor = pick.edge ? this.network.handle(pick.edge, pick.node.id) : pick.spacing ? this.spacingPosition(pick.node) : pick.node.position.clone();
      this.drag = { pick, pointer: event.pointerId, anchor, screenY: event.clientY, vertical: event.shiftKey && !pick.spacing, changed: false,
        initialOffset: pick.spacing ? this.network.parallelGroup(pick.node)!.offsets.get(pick.node.id) : undefined,
        affected: this.affectedSegments(pick) };
      this.canvas.setPointerCapture(event.pointerId);
      this.rebuild();
      return true;
    }
    const point = this.terrainPoint(event);
    if (point) {
      const node = this.network.addNode(this.placementPoint(point, false), this.gauge);
      this.start = { node, side: 1 }; this.selected = node.id;
      this.refreshGizmos(); this.message('First node placed. Click again to build the track.');
    } else this.message('Point at loaded terrain to place a node.');
    return true;
  }
  private placeSecond(point: THREE.Vector3, existing?: TrackNode) {
    const start = this.start!;
    if (this.buildingParallel) {
      try {
        const next = this.network.extendParallel(start.node, start.side, point, existing, existing ? undefined : this.endDirection(point));
        this.start = { node: next.node, side: next.side }; this.selected = next.node.id; this.endRotation = 0;
        this.clearGhost(); this.changed(); this.rebuild(); this.commitEarthworks();
        this.message(`${next.edges.length} parallel tracks built. Click to extend them together; Esc stops.`);
        return true;
      } catch (error) { this.message((error as Error).message); return false; }
    }
    if (existing && existing.gauge !== this.gauge) { this.message(`Cannot connect ${this.gauge.toLocaleString()} mm to ${existing.gauge.toLocaleString()} mm.`); return false; }
    if (point.distanceTo(start.node.position) < 1) { this.message('Choose an end node at least 1 m away.'); return false; }
    const node = existing ?? this.network.addNode(point, this.gauge);
    try {
      const segment = this.network.connect(start.node, node, this.gauge, start.side, existing ? undefined : this.endDirection(point));
      this.endRotation = 0;
      // Continue away from the incoming handle, preserving the tangent at this node.
      this.start = { node, side: segment.bSide === 1 ? -1 : 1 };
      this.selected = node.id; this.clearGhost();
      this.changed(); this.rebuild(); this.commitEarthworks();
      this.message(`Track built · ${this.gauge.toLocaleString()} mm. Click to place the next node; Esc stops placement.`);
      return true;
    } catch (error) {
      if (!existing) this.network.nodes.delete(node.id);
      this.message((error as Error).message);
      return false;
    }
  }
  pointerMove(event: PointerEvent) {
    if (!this.enabled) return false;
    this.lastPointer = event;
    if (this.stationMode) { this.stationHover(event); return false; }
    if (this.pendingSplit) { this.positionSplitButton(); return false; }
    if (this.drag) {
      const drag = this.drag;
      if (event.pointerId !== drag.pointer) return true;
      if (!(event.buttons & 1)) { this.endDrag(); return true; }
      const current = drag.pick.edge ? this.network.handle(drag.pick.edge, drag.pick.node.id) : drag.pick.spacing ? this.spacingPosition(drag.pick.node) : drag.pick.node.position;
      const vertical = event.shiftKey && !drag.pick.spacing;
      if (drag.vertical !== vertical) {
        drag.vertical = vertical; drag.anchor.copy(current); drag.screenY = event.clientY;
        return true;
      }
      let next: THREE.Vector3 | null;
      if (drag.vertical) {
        const eye = this.camera.position.clone().add(new THREE.Vector3(this.world.origin.x, 0, this.world.origin.y));
        const units = Math.max(1, eye.distanceTo(drag.anchor)) * 2 * Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2)) / this.canvas.clientHeight;
        next = drag.anchor.clone(); next.y += (drag.screenY - event.clientY) * units;
      } else next = this.ray(event).intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), -drag.anchor.y), new THREE.Vector3());
      if (next && next.distanceTo(current) > 1e-6) {
        if (drag.pick.spacing) {
          const group = this.network.parallelGroup(drag.pick.node)!;
          const offset = drag.initialOffset! + next.clone().sub(drag.anchor).dot(trackFrame(group.axis).right);
          this.network.setParallelOffset(drag.pick.node, offset);
        } else if (drag.pick.edge) this.network.moveHandle(drag.pick.edge, drag.pick.node.id, next);
        else this.network.moveNode(drag.pick.node, drag.vertical ? next : this.grid.snap(next));
        this.gridPoint = drag.pick.node.position.clone();
        drag.changed = true; this.changed();
        this.message(drag.pick.spacing ? 'Parallel spacing · lateral movement only' : `${drag.pick.edge ? 'Handle' : 'Node'} · ${drag.vertical ? 'vertical only' : 'horizontal only'} · Shift changes axis`);
      }
      return true;
    }
    this.hoverSegment(event);
    if (this.start) {
      this.hoverNode(event);
      this.directionButton.hidden = true;
      this.continueParallel.hidden = true;
      const node = this.pick(event, true)?.node;
      const terrain = node ? undefined : this.terrainPoint(event);
      const point = node?.position ?? (terrain && this.placementPoint(terrain, event.shiftKey));
      this.gridPoint = point?.clone();
      if (point && point.distanceTo(this.start.node.position) >= 1) {
        const key = `${point.x.toFixed(1)},${point.y.toFixed(1)},${point.z.toFixed(1)},${node?.id ?? ''},${!!event.shiftKey}`;
        if (key !== this.ghostKey) {
          this.clearGhost(); this.ghostKey = key;
          if (this.buildingParallel) {
            try {
              const plan = this.network.previewParallel(this.start.node, this.start.side, point, node, node ? undefined : this.endDirection(point));
              this.ghost = new THREE.Group();
              for (const entry of plan) this.ghost.add(makeTrack(entry.curve, this.gauge, true));
              this.group.add(this.ghost);
              this.message(`${plan.length} parallel tracks · click to ${node ? 'connect the matching group' : 'place all end nodes'}. Shift snaps straight; Z / X rotate; Esc stops.`);
            } catch (error) { this.message((error as Error).message); }
            return false;
          }
          const curve = this.network.preview(this.start.node, point, this.start.side, node, node ? undefined : this.endDirection(point));
          this.ghost = makeTrack(curve, this.gauge, true);
          if (node && node.gauge !== this.gauge) {
            this.ghost.traverse(object => { if (object instanceof THREE.Mesh) object.material.color.set(0xee5555); });
            this.message(`Gauge mismatch: this node uses ${node.gauge.toLocaleString()} mm.`);
          } else this.message(node ? `Click to connect to node ${node.id} · ${this.gauge.toLocaleString()} mm.` : event.shiftKey ? 'Straight snap · click to place on the outgoing tangent. Release Shift for free placement.' : 'Click to place the next node. Hold Shift for straight snap; Esc stops placement.');
          this.group.add(this.ghost);
        }
      } else this.clearGhost();
    } else {
      this.hoverNode(event);
      if (this.grid.spacing) this.gridPoint = this.terrainPoint(event);
    }
    this.hoverSplit(event);
    return false;
  }
  private hideSplit() {
    this.splitTarget = undefined; this.splitMarker.visible = !!this.pendingSplit;
    if (this.pendingSplit) this.splitMarker.position.copy(this.pendingSplit.point);
    this.positionSplitButton();
  }
  openStationAt(event: PointerEvent) {
    if (event.button !== 0 || this.enabled && this.stationMode && $<HTMLSelectElement>('station-action').value === 'remove') return false;
    const id = this.stations.pick(this.ray(event), this.world.origin);
    if (id === undefined) return false;
    this.stations.open(id); return true;
  }
  private platformSide(event: PointerEvent, edge: TrackSegment, t: number): Side {
    const curve = this.network.curve(edge), point = curve.getPoint(t);
    const hit = this.ray(event).intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), -point.y), new THREE.Vector3());
    return hit && hit.sub(point).dot(trackFrame(curve.getTangent(t)).right) < 0 ? -1 : 1;
  }
  private stationHover(event: PointerEvent) {
    this.hoverSplit(event); this.splitMarker.visible = false;
    const target = this.splitTarget, edge = target && this.network.segments.get(target.edge);
    if (!edge) { this.clearGhost(); return; }
    const removing = $<HTMLSelectElement>('station-action').value === 'remove';
    const side = this.platformSide(event, edge, target!.t);
    if (edge.station?.sides.includes(side) && !removing) { this.clearGhost(); this.message(`Station ${edge.station.id} · click its platform to open the station window.`); return; }
    if (!removing && !this.network.canPlaceStation(edge, side)) {
      this.clearGhost(); this.message('No platforms on junction segments. Split the track beyond the junction first.'); return;
    }
    if (removing && !edge.station?.sides.includes(side)) { this.clearGhost(); return; }
    const key = `station:${edge.id}:${side}:${removing}`;
    if (this.ghostKey === key) return;
    this.clearGhost(); this.ghostKey = key;
    this.ghost = makePlatform(this.network.curve(edge), edge.gauge, side, true);
    if (removing) this.ghost.traverse(object => { if (object instanceof THREE.Mesh) object.material.color.set(0xef7864); });
    this.group.add(this.ghost);
    this.message(removing ? 'Click to remove this platform.' : `Click to build a full-segment platform · ${this.network.curve(edge).getLength().toFixed(1)} m. Move across the track to change sides.`);
  }
  private positionSplitButton() {
    const pending = this.pendingSplit, point = pending && this.project(pending.point);
    this.splitButton.hidden = !this.enabled || !pending || !point?.visible || !this.actionsInRange(pending.point);
    if (!this.splitButton.hidden && point) { this.splitButton.style.left = `${point.x}px`; this.splitButton.style.top = `${point.y - 30}px`; }
  }
  private hoverSplit(event: PointerEvent) {
    if (this.pendingSplit) { this.hideSplit(); return; }
    if (this.start || !this.stationMode && this.pick(event)) { this.hideSplit(); return; }
    let best = (this.stationMode ? 45 : 16) ** 2, target: typeof this.splitTarget;
    for (const edge of this.network.segments.values()) {
      let samples = this.splitSamples.get(edge.id);
      if (!samples) {
        const curve = this.network.curve(edge);
        const steps = Math.min(1024, Math.max(32, Math.ceil(curve.getLength() / 4)));
        samples = { curve, points: curve.getPoints(steps) }; this.splitSamples.set(edge.id, samples);
      }
      const { curve, points } = samples, steps = points.length - 1;
      let previous = this.project(points[0]);
      for (let i = 1; i <= steps; i++) {
        const next = this.project(points[i]);
        if (previous.visible && next.visible) {
          const dx = next.x - previous.x, dy = next.y - previous.y;
          const fraction = THREE.MathUtils.clamp(((event.clientX - previous.x) * dx + (event.clientY - previous.y) * dy) / (dx * dx + dy * dy || 1), 0, 1);
          const distance = (previous.x + fraction * dx - event.clientX) ** 2 + (previous.y + fraction * dy - event.clientY) ** 2;
          if (distance < best) {
            const t = (i - 1 + fraction) / steps, point = curve.getPoint(t);
            if (this.actionsInRange(point) && point.distanceTo(curve.v0) >= 1 && point.distanceTo(curve.v3) >= 1) {
              best = distance; target = { edge: edge.id, t, point };
            }
          }
        }
        previous = next;
      }
    }
    if (!target) { this.hideSplit(); return; }
    this.splitTarget = target; this.splitMarker.visible = true; this.splitMarker.position.copy(target.point);
  }
  private midpoint(edge: TrackSegment) {
    let point = this.midpoints.get(edge.id);
    if (!point) { point = this.network.curve(edge).getPointAt(0.5); this.midpoints.set(edge.id, point); }
    return point;
  }
  private hoverSegment(event: PointerEvent) {
    let nearest: TrackSegment | undefined, distance = 60;
    for (const edge of this.network.segments.values()) {
      if (!this.actionsInRange(this.midpoint(edge))) continue;
      const point = this.project(this.midpoint(edge));
      const d = Math.hypot(point.x - event.clientX, point.y - event.clientY);
      if (point.visible && d < distance) { nearest = edge; distance = d; }
    }
    if (!nearest) { this.hideStraighten(); return; }
    if (this.hoveredSegment !== nearest.id) {
      this.hideStraighten(); this.hoveredSegment = nearest.id;
      this.straightenButton.setAttribute('aria-label', `Straighten track segment ${nearest.id}`);
      this.deleteSegmentButton.setAttribute('aria-label', `Delete track segment ${nearest.id}`);
    }
    this.straightenButton.hidden = false; this.deleteSegmentButton.hidden = false;
    this.positionStraightenButton();
  }
  private positionStraightenButton() {
    const edge = this.hoveredSegment === undefined ? undefined : this.network.segments.get(this.hoveredSegment);
    if (!edge) return;
    const point = this.project(this.midpoint(edge));
    if (!point.visible || !this.actionsInRange(this.midpoint(edge))) { this.hideStraighten(); return; }
    this.straightenButton.style.left = `${Math.max(90, Math.min(innerWidth - 90, point.x))}px`;
    this.straightenButton.style.top = `${Math.max(50, Math.min(innerHeight - 80, point.y - 35))}px`;
    this.deleteSegmentButton.style.left = this.straightenButton.style.left;
    this.deleteSegmentButton.style.top = `${Math.max(16, Math.min(innerHeight - 116, point.y - 71))}px`;
  }
  private resetSegmentDelete() {
    this.confirmDeleteSegment = undefined;
    this.deleteSegmentButton.textContent = 'Delete track'; this.deleteSegmentButton.classList.remove('confirm');
    this.deleteSegmentButton.setAttribute('aria-label', this.hoveredSegment === undefined ? 'Delete track segment' : `Delete track segment ${this.hoveredSegment}`);
  }
  private hideStraighten() {
    this.deleteSegmentButton.hidden = true; this.resetSegmentDelete();
    this.straightenButton.hidden = true;
    this.straightenButton.textContent = 'Straighten';
    this.straightenButton.classList.remove('confirm');
    this.confirmStraighten = undefined; this.hoveredSegment = undefined;
  }
  private hoverNode(event: PointerEvent) {
    let nearest: TrackNode | undefined, distance = 100;
    for (const node of this.network.nodes.values()) {
      if (!this.actionsInRange(node.position)) continue;
      const p = this.project(node.position), d = Math.hypot(p.x - event.clientX, p.y - event.clientY);
      if (p.visible && d < distance) { nearest = node; distance = d; }
    }
    if (!nearest) { this.hideNodeButtons(); return; }
    const center = this.project(nearest.position), axis = this.project(nearest.position.clone().addScaledVector(nearest.axis, 10));
    let dx = axis.x - center.x, dy = axis.y - center.y;
    const length = Math.hypot(dx, dy);
    if (length < 0.001) { dx = 1; dy = 0; } else { dx /= length; dy /= length; }
    const side: Side = (event.clientX - center.x) * dx + (event.clientY - center.y) * dy >= 0 ? 1 : -1;
    if (this.hoverDirection?.node.id !== nearest.id) { this.resetDelete(); this.resetMerge(); }
    this.hoverDirection = { node: nearest, side };
    if (this.confirmDelete === undefined) this.resetDelete();
    if (this.confirmMerge === undefined) this.resetMerge();
    this.directionButton.classList.toggle('gauge-mismatch', nearest.gauge !== this.gauge);
    this.positionDirectionButton();
    this.directionButton.title = `Start track from node ${nearest.id} · ${nearest.gauge.toLocaleString()} mm`;
    this.directionButton.setAttribute('aria-label', `Start track from node ${nearest.id} in ${side === 1 ? 'positive' : 'negative'} tangent direction`);
    this.directionButton.hidden = !this.network.canExtend(nearest, side);
    const group = this.network.parallelGroup(nearest);
    this.continueParallel.hidden = !group || group.offsets.size < 2 ||
      [...group.offsets.keys()].some(id => !this.network.canExtend(this.network.nodes.get(id)!, side));
    this.deleteButton.hidden = false;
    this.mergeButton.hidden = !this.network.canMerge(nearest.id);
    this.parallelLeft.hidden = false; this.parallelRight.hidden = false;
  }
  private positionDirectionButton() {
    if (!this.hoverDirection) return;
    const { node, side } = this.hoverDirection;
    const center = this.project(node.position), axis = this.project(node.position.clone().addScaledVector(node.axis, 10));
    if (!center.visible || !this.actionsInRange(node.position)) { this.hideNodeButtons(); return; }
    let dx = axis.x - center.x, dy = axis.y - center.y;
    const length = Math.hypot(dx, dy);
    if (length < 0.001) { dx = 1; dy = 0; } else { dx /= length; dy /= length; }
    this.directionButton.style.left = `${center.x + dx * side * 65}px`;
    this.directionButton.style.top = `${center.y + dy * side * 65}px`;
    this.continueParallel.style.left = `${center.x + dx * side * 65 + dy * 46}px`;
    this.continueParallel.style.top = `${center.y + dy * side * 65 - dx * 46}px`;
    this.deleteButton.style.left = `${center.x + dx * side * 65 - dy * 32}px`;
    this.deleteButton.style.top = `${center.y + dy * side * 65 + dx * 32}px`;
    this.mergeButton.style.left = `${center.x + dx * side * 65 + dy * 82}px`;
    this.mergeButton.style.top = `${center.y + dy * side * 65 - dx * 82}px`;
    const rightPoint = this.project(node.position.clone().addScaledVector(trackFrame(node.axis).right, 10));
    let rx = rightPoint.x - center.x, ry = rightPoint.y - center.y;
    const span = Math.hypot(rx, ry);
    if (span < 0.001) { rx = 1; ry = 0; } else { rx /= span; ry /= span; }
    for (const [button, direction] of [[this.parallelLeft, -1], [this.parallelRight, 1]] as const) {
      button.style.left = `${center.x + rx * direction * 75}px`;
      button.style.top = `${center.y + ry * direction * 75}px`;
      button.title = `Add parallel node ${direction < 0 ? 'left' : 'right'} · ${this.network.parallelSpacing(node, direction).toFixed(3)} m · ${node.gauge.toLocaleString()} mm`;
    }
  }
  pointerUp(event: PointerEvent) {
    if (!this.drag || event.pointerId !== this.drag.pointer) return false;
    this.endDrag(); return true;
  }
  endDrag() {
    const drag = this.drag; this.drag = undefined;
    if (drag && this.canvas.hasPointerCapture(drag.pointer)) this.canvas.releasePointerCapture(drag.pointer);
    // Restore sleepers and ballast even when the pointer was released without moving.
    if (drag) this.rebuild(drag.affected);
    if (drag?.changed) this.commitEarthworks();
  }
  suspend() { if (this.pendingSplit) this.cancelPlacement(); this.hideSplit(); this.resetDelete(); this.endDrag(); this.hideNodeButtons(); this.hideStraighten(); this.clearGhost(); }
  leave(target: EventTarget | null) {
    if (target === this.continueParallel) return;
    if (target === this.mergeButton) return;
    if (this.drag || target === this.directionButton || target === this.deleteButton || target === this.parallelLeft || target === this.parallelRight || target === this.straightenButton || target === this.deleteSegmentButton) return;
    this.hideSplit(); this.hideNodeButtons(); this.hideStraighten(); this.clearGhost();
  }
  escape() {
    if (!this.enabled) return false;
    if (this.pendingSplit) { this.cancelPlacement(); this.message('Split cancelled.'); return true; }
    if (this.confirmMerge !== undefined) { this.resetMerge(); this.message('Merge cancelled.'); return true; }
    if (this.confirmDelete !== undefined) { this.resetDelete(); this.message('Deletion cancelled.'); return true; }
    if (this.confirmDeleteSegment !== undefined) { this.hideStraighten(); this.message('Segment deletion cancelled.'); return true; }
    if (this.confirmStraighten !== undefined) { this.hideStraighten(); this.message('Straightening cancelled.'); return true; }
    if (this.drag) this.endDrag();
    else if (this.start) { this.cancelPlacement(); this.message('Placement stopped. Click terrain to start a new chain, or edit existing nodes.'); }
    else if (this.onClose) this.onClose(); else this.setEnabled(false);
    return true;
  }
  private cancelPlacement() {
    this.buildingParallel = false;
    this.pendingSplit = undefined; this.endRotation = 0;
    this.resetMerge();
    this.hideSplit();
    this.resetDelete();
    this.hideStraighten();
    if (this.start && !this.network.incident(this.start.node.id).length && this.start.node.parallelGroup === undefined) {
      this.network.nodes.delete(this.start.node.id);
      if (this.selected === this.start.node.id) this.selected = undefined;
    }
    this.start = undefined; this.clearGhost(); this.refreshGizmos();
  }
  private selectedInfo() {
    const node = this.selected ? this.network.nodes.get(this.selected) : undefined;
    $('track-selection').textContent = node ? `Node ${node.id} · ${node.gauge.toLocaleString()} mm · ${this.network.incident(node.id).length} connected tracks\nX ${node.position.x.toFixed(2)} · Y ${node.position.y.toFixed(2)} · Z ${node.position.z.toFixed(2)}` : 'No node selected';
    if (node) {
      const group = this.network.parallelGroup(node);
      if (group) $('track-selection').textContent += `\nParallel group · ${group.offsets.size} nodes · offset ${group.offsets.get(node.id)!.toFixed(3)} m`;
    }
  }
  private clearGhost() { if (this.ghost) { this.group.remove(this.ghost); disposeTrack(this.ghost); this.ghost = undefined; } this.ghostKey = ''; }
  private changed() { if (this.pendingSplit) this.cancelPlacement(); this.resetMerge(); this.hideSplit(); this.resetDelete(); this.hideStraighten(); this.midpoints.clear(); this.splitSamples.clear(); this.dirty = true; this.earthDirty = true; this.earthAt = performance.now() + 400; }
  private commitEarthworks() {
    if (!this.earthDirty) return;
    this.earthDirty = false;
    this.world.setEarthworks([...this.network.segments.values()].map(edge => {
      const curve = this.network.curve(edge); curve.arcLengthDivisions = 512;
      const points = curve.getSpacedPoints(Math.max(2, Math.ceil(curve.getLength() / 2)));
      return { points: points.map((p, i) => {
        // The formation follows the underside of pitched ballast, not a vertical-only offset.
        const bed = p.clone().addScaledVector(trackFrame(curve.getTangentAt(i / (points.length - 1))).up, -0.65);
        return { x: bed.x, y: bed.y + 0.65, z: bed.z };
      }) };
    }));
  }
  private affectedSegments(pick: Pick) {
    const group = !pick.spacing && this.network.parallelGroup(pick.node);
    const nodes = new Set(group ? group.offsets.keys() : [pick.node.id]);
    return new Set([...this.network.segments.values()].filter(edge => nodes.has(edge.a) || nodes.has(edge.b)).map(edge => edge.id));
  }
  private rebuild(affected = this.drag?.affected) {
    for (const [id, mesh] of this.segmentMeshes) if (!this.network.segments.has(id) || !affected || affected.has(id)) {
      this.rails.remove(mesh); disposeTrack(mesh); this.segmentMeshes.delete(id);
    }
    for (const edge of this.network.segments.values()) {
      if (this.segmentMeshes.has(edge.id)) continue;
      const mesh = makeTrack(this.network.curve(edge), edge.gauge, false, this.drag?.affected.has(edge.id) ?? false);
      this.segmentMeshes.set(edge.id, mesh); this.rails.add(mesh);
    }
    this.stations.sync(affected);
    this.dirty = false; this.refreshGizmos();
  }
  private refreshGizmos() {
    // Marker materials/geometries are shared and disposed once with the editor.
    for (const child of this.gizmos.children) if (child instanceof THREE.Line) child.geometry.dispose();
    this.gizmos.clear(); this.markers = [];
    const marker = (position: THREE.Vector3, pick: Pick) => {
      const material = pick.spacing ? this.spacingMaterial : pick.edge ? this.handleMaterial : pick.node.id === this.selected ? this.selectedMaterial : this.nodeMaterial;
      const object = new THREE.Mesh(this.sphere, material);
      object.position.copy(position); object.renderOrder = 20;
      this.gizmos.add(object); this.markers.push({ object, pick });
    };
    for (const node of this.network.nodes.values()) marker(node.position, { node });
    const selected = this.selected ? this.network.nodes.get(this.selected) : undefined;
    if (selected) for (const edge of this.network.incident(selected.id)) {
      const handle = this.network.handle(edge, selected.id);
      marker(handle, { node: selected, edge });
      const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), handle.clone().sub(selected.position)]), this.lineMaterial);
      line.position.copy(selected.position);
      line.renderOrder = 19; this.gizmos.add(line);
    }
    const group = selected && this.network.parallelGroup(selected);
    if (group) for (const id of group.offsets.keys()) {
      if (id === group.anchor || selected!.id !== group.anchor && id !== selected!.id) continue;
      const node = this.network.nodes.get(id)!, handle = this.spacingPosition(node);
      marker(handle, { node, spacing: true });
      const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), handle.clone().sub(node.position)]), this.spacingLineMaterial);
      line.position.copy(node.position); line.renderOrder = 19; line.userData.spacingNode = node.id; this.gizmos.add(line);
    }
    this.selectedInfo();
  }
  update(time: number) {
    this.group.position.set(-this.world.origin.x, 0, -this.world.origin.y);
    if (this.dirty && time - this.lastBuild > 50) { this.rebuild(); this.lastBuild = time; }
    if (this.earthDirty && !this.drag && time >= this.earthAt) this.commitEarthworks();
    if (this.enabled && !this.drag) {
      this.camera.updateMatrixWorld();
      if (!this.directionButton.hidden || !this.deleteButton.hidden || !this.parallelLeft.hidden || !this.parallelRight.hidden) this.positionDirectionButton();
      if (!this.straightenButton.hidden) this.positionStraightenButton();
      const signature = [...this.camera.position, this.camera.rotation.x, this.camera.rotation.y, this.world.origin.x, this.world.origin.y].join(',');
      if (signature !== this.viewSignature && this.lastPointer) this.pointerMove(this.lastPointer);
      this.viewSignature = signature;
    }
    const eye = this.camera.position.clone().add(new THREE.Vector3(this.world.origin.x, 0, this.world.origin.y));
    this.grid.showAt(this.gridPoint, this.enabled && !this.stationMode && !!this.gridPoint && this.actionsInRange(this.gridPoint));
    if (this.splitMarker.visible) {
      this.positionSplitButton();
      const units = eye.distanceTo(this.splitMarker.position) * 2 * Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2)) / this.canvas.clientHeight;
      this.splitMarker.scale.setScalar(Math.max(0.08, units * 7));
      if (!this.pendingSplit && !this.actionsInRange(this.splitMarker.position)) this.hideSplit();
    }
    for (const mesh of this.segmentMeshes.values()) updateSleeperLOD(mesh, eye);
    for (const { object, pick } of this.markers) {
      if (pick.spacing) object.position.copy(this.spacingPosition(pick.node));
      const units = eye.distanceTo(object.position) * 2 * Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2)) / this.canvas.clientHeight;
      object.scale.setScalar(Math.max(0.08, units * (pick.edge || pick.spacing ? 5 : 7)));
    }
    for (const object of this.gizmos.children) if (object instanceof THREE.Line && object.userData.spacingNode !== undefined) {
      const node = this.network.nodes.get(object.userData.spacingNode)!;
      object.position.copy(node.position);
      const positions = object.geometry.getAttribute('position');
      const delta = this.spacingPosition(node).sub(node.position);
      positions.setXYZ(1, delta.x, delta.y, delta.z); positions.needsUpdate = true; object.geometry.computeBoundingSphere();
    }
  }
  dispose() {
    this.setEnabled(false); this.abort.abort();
    this.scene.remove(this.group); disposeTrack(this.rails);
    this.stations.dispose();
    this.grid.dispose();
    this.segmentMeshes.clear();
    this.splitSamples.clear();
    for (const child of this.gizmos.children) if (child instanceof THREE.Line) child.geometry.dispose();
    this.sphere.dispose(); this.nodeMaterial.dispose(); this.handleMaterial.dispose(); this.selectedMaterial.dispose(); this.lineMaterial.dispose();
    this.splitMaterial.dispose();
    this.spacingMaterial.dispose(); this.spacingLineMaterial.dispose();
    this.group.clear();
  }
}
