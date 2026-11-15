import * as THREE from 'three';
import type { Side, TrackNetwork } from '../track/model';
import { vehicleDefinition } from './catalog';
import { makeVehicle, disposeVehicle, tintVehicle } from './render';
import type { VehicleVisual } from './render';
import { applyVehiclePose, vehiclesOverlap, sampleTrack } from './placement';
import type { TrackPosition } from './placement';
import type { VehiclePose } from './placement';
import { TrainBuilder, compatibleGauges, consistLayout, consistPoses, appendPositions } from './consist';
import type { TrainConsist } from './consist';
import type { TerrainWorld } from '../terrain/world';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id)! as T;
const DRAG_TYPE = 'application/x-railway-train-item';
type End = 'front' | 'rear';
interface ParkedTrain { train: TrainConsist; visuals: VehicleVisual[]; position: TrackPosition; supported: boolean }
interface AppendTarget { parked: ParkedTrain; end: End; combined: TrackPosition }
export class TrainEditor {
  readonly group = new THREE.Group();
  readonly builder = new TrainBuilder();
  private parked: ParkedTrain[] = [];
  private ghosts: VehicleVisual[] = [];
  private ghostKey = '';
  private target?: TrackPosition;
  private appendTarget?: AppendTarget;
  private catalogAppend?: { train: TrainConsist; end: End };
  private facing: Side = 1;
  private reverseAppend = false;
  private visible = false;
  private placing = false;
  private valid = false;
  private pointer?: { clientX: number; clientY: number };
  private dragged?: { train: number; item: number };
  private signature = '';
  private abort = new AbortController();
  private routeTime = 0;
  constructor(private scene: THREE.Scene, private camera: THREE.PerspectiveCamera, private canvas: HTMLCanvasElement, private world: TerrainWorld, private network: TrackNetwork) {
    scene.add(this.group); const options = { signal: this.abort.signal };
    $('train-new').addEventListener('click', () => {
      this.cancelPreview(); this.catalogAppend = undefined; this.builder.create(); this.builderInfo();
      this.message('Empty train created. Choose its first vehicle from the catalog.');
    }, options);
    $('train-selector').addEventListener('change', () => {
      this.cancelPreview(); this.catalogAppend = undefined;
      this.builder.selected = this.builder.trains.find(train => train.id === Number($<HTMLSelectElement>('train-selector').value));
      this.builderInfo(); this.message('Select Append to front/rear to add catalog vehicles to this draft, or choose a catalog vehicle to create a new train.');
    }, options);
    for (const [id, end] of [['train-append-front', 'front'], ['train-append-rear', 'rear']] as const) $(id).addEventListener('click', () => {
      const train = this.builder.selected; if (!train || train.placed) return;
      this.cancelPreview(); this.catalogAppend = { train, end };
      this.message(`Choose a catalog vehicle to append to the ${end} of ${train.name}.`);
    }, options);
    document.querySelectorAll<HTMLButtonElement>('[data-vehicle]').forEach(button => button.addEventListener('click', () => {
      const append = this.catalogAppend; this.catalogAppend = undefined; this.cancelPreview();
      const existing = this.builder.selected;
      const train = !append && existing && !existing.placed && existing.items.length === 0 ? existing : this.builder.create();
      this.builder.selected = train;
      try {
        this.builder.add(vehicleDefinition(button.dataset.vehicle!));
        if (append) this.builder.merge(append.train, train, append.end);
        this.builderInfo(); this.message(append ? 'Vehicle appended. Append another vehicle or place this train.' : 'New train created. Append more vehicles or choose Place train on track.');
      } catch (error) { this.builderInfo(); this.message((error as Error).message); }
    }, options));
    $('train-place').addEventListener('click', () => {
      const train = this.builder.selected; if (!train || train.placed || !train.items.length) return;
      this.catalogAppend = undefined; this.placing = true; this.clearGhosts(); this.builderInfo();
      this.reverseAppend = false;
      this.message('Hover track to preview the train. R reverses facing. Snap to a parked train’s front/rear to append.');
      if (this.pointer) this.pointerMove(this.pointer); this.canvas.focus({ preventScroll: true });
    }, options);
    $('train-reverse').addEventListener('click', () => this.reversePreview(), options);
    $('train-done').addEventListener('click', () => this.setEnabled(false), options);
    $('train-undo').addEventListener('click', () => this.undoPlacement(), options);
    const trash = $('train-trash');
    trash.addEventListener('dragover', event => {
      if (!this.dragged || !event.dataTransfer?.types.includes(DRAG_TYPE)) return;
      event.preventDefault(); event.dataTransfer.dropEffect = 'move'; trash.classList.add('drop-ready');
    }, options);
    trash.addEventListener('dragleave', () => trash.classList.remove('drop-ready'), options);
    trash.addEventListener('drop', event => {
      event.preventDefault(); trash.classList.remove('drop-ready', 'drag-active');
      try {
        const payload = JSON.parse(event.dataTransfer?.getData(DRAG_TYPE) || '{}');
        if (this.dragged && this.dragged.train === payload.train && this.dragged.item === payload.item) this.removeItem(payload.train, payload.item);
      } catch { /* Ignore unrelated drag payloads. */ }
      this.dragged = undefined;
    }, options);
    this.builderInfo(); this.setEnabled(false);
  }
  get enabled() { return this.visible; }
  reversePreview() {
    if (!this.enabled || !this.placing) return;
    this.facing = this.facing === 1 ? -1 : 1; this.reverseAppend = !this.reverseAppend;
    if (this.pointer) this.pointerMove(this.pointer);
  }
  setEnabled(enabled: boolean) {
    this.visible = enabled; $('train-panel').hidden = !enabled;
    $('place-train').setAttribute('aria-pressed', String(enabled)); this.cancelPreview(); this.catalogAppend = undefined; this.builderInfo();
    if (enabled) this.message('Choose a catalog vehicle to create a train. Use Append to front/rear to build its consist.');
  }
  private message(text: string) { $('train-status').textContent = text; }
  private removeItem(train: number, item: number) {
    if (!this.builder.remove(train, item)) return;
    this.cancelPreview(); this.builderInfo(); this.message('Vehicle removed from the train.');
  }
  private builderInfo() {
    const train = this.builder.selected, editable = !!train && !train.placed;
    const selector = $<HTMLSelectElement>('train-selector');
    selector.replaceChildren(...this.builder.trains.map(item => new Option(`${item.name}${item.placed ? ' · parked' : ' · building'}`, String(item.id))));
    selector.disabled = !this.builder.trains.length; if (train) selector.value = String(train.id);
    const list = $('train-consist'); list.replaceChildren();
    train?.items.forEach((item, index) => {
      const row = document.createElement('li'); row.className = 'consist-item'; row.draggable = editable; row.dataset.itemId = String(item.id);
      const text = document.createElement('span'), title = document.createElement('strong'), detail = document.createElement('small');
      title.textContent = `${index + 1}. ${item.definition.name}`; detail.textContent = `${item.definition.length.toFixed(2)} m${index === 0 ? ' · front' : ''}${item.facing === -1 ? ' · reversed' : ''}`;
      text.append(title, detail); row.append(text);
      if (editable) {
        const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = '×'; remove.title = 'Remove vehicle';
        remove.setAttribute('aria-label', `Remove vehicle ${index + 1}: ${item.definition.name}`);
        remove.addEventListener('click', () => this.removeItem(train!.id, item.id)); row.append(remove);
        row.addEventListener('dragstart', event => {
          this.dragged = { train: train!.id, item: item.id }; event.dataTransfer?.setData(DRAG_TYPE, JSON.stringify(this.dragged));
          if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
          row.classList.add('dragging'); $('train-trash').classList.add('drag-active');
        });
        row.addEventListener('dragend', () => {
          this.dragged = undefined; row.classList.remove('dragging'); $('train-trash').classList.remove('drop-ready', 'drag-active');
        });
      }
      list.append(row);
    });
    $('train-empty').hidden = !!train?.items.length;
    $('train-empty').textContent = train ? 'Choose a catalog vehicle for this empty train.' : 'Choose a catalog vehicle to create a train.';
    $('train-trash').hidden = !editable || !train!.items.length;
    $('train-trash').classList.remove('drop-ready', 'drag-active');
    for (const id of ['train-append-front', 'train-append-rear']) $<HTMLButtonElement>(id).disabled = !editable;
    const definitions = train?.items.map(item => item.definition) ?? [];
    $('train-specs').textContent = definitions.length ? `${definitions.length} vehicles · ${consistLayout(definitions).length.toFixed(2)} m total\nCompatible gauge: ${compatibleGauges(definitions).map(gauge => `${gauge.toLocaleString()} mm`).join(', ')}` : '';
    $<HTMLButtonElement>('train-place').disabled = !editable || !definitions.length || this.placing;
    $<HTMLButtonElement>('train-reverse').disabled = !this.placing;
    $('train-count').textContent = `${this.parked.length} parked train${this.parked.length === 1 ? '' : 's'} · ${this.parked.reduce((sum, item) => sum + item.visuals.length, 0)} vehicles`;
    $<HTMLButtonElement>('train-undo').disabled = this.parked.length === 0;
  }
  private project(point: THREE.Vector3) {
    const p = point.clone().sub(new THREE.Vector3(this.world.origin.x, 0, this.world.origin.y)).project(this.camera), rect = this.canvas.getBoundingClientRect();
    return { x: rect.left + (p.x + 1) * rect.width / 2, y: rect.top + (1 - p.y) * rect.height / 2, visible: p.z > -1 && p.z < 1 };
  }
  private pick(event: { clientX: number; clientY: number }) {
    let best = 20 * 20, target: TrackPosition | undefined;
    for (const edge of this.network.segments.values()) {
      const curve = this.network.curve(edge); curve.arcLengthDivisions = 512;
      const steps = Math.min(512, Math.max(24, Math.ceil(curve.getLength() / 3))); let previous = this.project(curve.getPointAt(0));
      for (let i = 1; i <= steps; i++) {
        const next = this.project(curve.getPointAt(i / steps));
        if (previous.visible && next.visible) {
          const dx = next.x - previous.x, dy = next.y - previous.y;
          const f = THREE.MathUtils.clamp(((event.clientX - previous.x) * dx + (event.clientY - previous.y) * dy) / (dx * dx + dy * dy || 1), 0, 1);
          const distance = (previous.x + f * dx - event.clientX) ** 2 + (previous.y + f * dy - event.clientY) ** 2;
          if (distance < best) { best = distance; target = { edge: edge.id, u: (i - 1 + f) / steps, facing: this.facing }; }
        }
        previous = next;
      }
    }
    return target;
  }
  private pickAppend(event: { clientX: number; clientY: number }, incomingLength: number) {
    let nearest: { parked: ParkedTrain; end: End } | undefined, best = 28;
    for (const parked of this.parked) for (const end of ['front', 'rear'] as const) {
      try {
        const length = consistLayout(parked.train.items.map(item => item.definition)).length;
        const p = this.project(sampleTrack(this.network, parked.position, (end === 'front' ? 1 : -1) * length / 2).point);
        const distance = Math.hypot(p.x - event.clientX, p.y - event.clientY);
        if (p.visible && distance < best) { best = distance; nearest = { parked, end }; }
      } catch { /* Unsupported parked trains cannot accept a connection. */ }
    }
    if (!nearest) return;
    const length = consistLayout(nearest.parked.train.items.map(item => item.definition)).length;
    const positions = appendPositions(this.network, nearest.parked.position, length, incomingLength, nearest.end);
    return { ...nearest, ...positions };
  }
  pointerMove(event: { clientX: number; clientY: number }) {
    if (!this.enabled) return false;
    this.pointer = { clientX: event.clientX, clientY: event.clientY };
    if (!this.placing || !this.builder.selected) return false;
    const train = this.builder.selected, definitions = train.items.map(item => item.definition);
    this.target = this.pick(event); this.appendTarget = undefined; this.valid = false;
    let appendError: string | undefined;
    try {
      const append = this.pickAppend(event, consistLayout(definitions).length);
      if (append) { this.target = { ...append.incoming, facing: append.incoming.facing * (this.reverseAppend ? -1 : 1) as Side }; this.appendTarget = append; }
    } catch (error) { appendError = (error as Error).message; }
    if (!this.target) { this.ghosts.forEach(visual => { visual.group.visible = false; }); this.message(appendError ?? 'Hover track to preview the train. R reverses facing.'); return false; }
    const target = this.target, gauge = this.network.segments.get(target.edge)!.gauge;
    const key = `${train.id}:${train.items.map(item => item.id).join(',')}:${gauge}`;
    if (key !== this.ghostKey) {
      this.clearGhosts(false); this.ghostKey = key;
      this.ghosts = definitions.map(def => makeVehicle(def, def.bogiesByGauge[gauge] ? gauge : Number(Object.keys(def.bogiesByGauge)[0])));
      this.ghosts.forEach(visual => this.group.add(visual.group));
    }
    this.ghosts.forEach(visual => { visual.group.visible = true; });
    try {
      if (appendError) throw new Error(appendError);
      const poses = consistPoses(this.network, definitions, target);
      if (this.appendTarget) {
        const old = this.appendTarget.parked.train.items.map(item => item.definition), incoming = this.reverseAppend ? [...definitions].reverse() : definitions;
        consistPoses(this.network, this.appendTarget.end === 'front' ? [...incoming, ...old] : [...old, ...incoming], this.appendTarget.combined);
      }
      this.ghosts.forEach((visual, i) => applyVehiclePose(visual, this.itemPose(poses[i].pose, train.items[i].facing)));
      if (this.ghosts.some(visual => this.parked.some(parked => parked.visuals.some(other => vehiclesOverlap(visual, other, parked === this.appendTarget?.parked ? 0 : 0.08))))) throw new Error('This train overlaps a parked train.');
      this.valid = true;
      this.message(this.appendTarget ? `Click to append to the ${this.appendTarget.end} of ${this.appendTarget.parked.train.name}.` : `Click to place ${train.name} · ${definitions.length} vehicles. R reverses; Esc cancels.`);
    } catch (error) {
      this.message((error as Error).message);
      const curve = this.network.curve(this.network.segments.get(target.edge)!);
      const center = curve.getPointAt(target.u), direction = curve.getTangentAt(target.u).multiplyScalar(target.facing), { offsets } = consistLayout(definitions);
      this.ghosts.forEach((visual, i) => {
        const point = center.clone().addScaledVector(direction, offsets[i]);
        applyVehiclePose(visual, this.itemPose({ center: point, front: point.clone().addScaledVector(direction, visual.definition.bogieCenters / 2), rear: point.clone().addScaledVector(direction, -visual.definition.bogieCenters / 2), frontDirection: direction, rearDirection: direction }, train.items[i].facing));
      });
    }
    this.ghosts.forEach(visual => tintVehicle(visual, this.valid)); return false;
  }
  private createVisuals(train: TrainConsist, position: TrackPosition) {
    const definitions = train.items.map(item => item.definition), poses = consistPoses(this.network, definitions, position), gauge = this.network.segments.get(position.edge)!.gauge;
    return definitions.map((def, i) => { const visual = makeVehicle(def, gauge); applyVehiclePose(visual, this.itemPose(poses[i].pose, train.items[i].facing)); this.group.add(visual.group); return visual; });
  }
  private itemPose(pose: VehiclePose, facing: Side): VehiclePose {
    return facing === 1 ? pose : { center: pose.center, front: pose.rear, rear: pose.front, frontDirection: pose.rearDirection.clone().negate(), rearDirection: pose.frontDirection.clone().negate() };
  }
  pointerDown(event: PointerEvent) {
    if (!this.enabled || !this.placing || event.button !== 0) return false;
    this.pointerMove(event); const train = this.builder.selected;
    if (!this.valid || !this.target || !train) return true;
    const append = this.appendTarget;
    if (append) {
      const parked = append.parked;
      this.builder.merge(parked.train, train, append.end, this.reverseAppend);
      parked.visuals.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); });
      parked.position = { ...append.combined }; parked.visuals = this.createVisuals(parked.train, parked.position); parked.supported = true;
    } else {
      const position = { ...this.target }, visuals = this.createVisuals(train, position); train.placed = true;
      this.parked.push({ train, visuals, position, supported: true });
    }
    this.cancelPreview(); this.builderInfo(); this.message(append ? `Trains merged into ${append.parked.train.name}.` : `${train.name} placed. Choose a catalog vehicle to create another train.`); return true;
  }
  private undoPlacement() {
    const last = this.parked.pop();
    if (last) { last.visuals.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); }); last.train.placed = false; this.builder.selected = last.train; }
    this.cancelPreview(); this.builderInfo(); this.message('Last parked train returned to the builder.');
  }
  leave() { this.pointer = undefined; this.clearGhosts(); }
  suspend() { this.pointer = undefined; this.clearGhosts(); }
  escape() {
    if (!this.enabled) return false;
    if (this.placing) { this.cancelPreview(); this.builderInfo(); this.message('Placement cancelled. Your train is unchanged.'); }
    else if (this.catalogAppend) { this.catalogAppend = undefined; this.message('Append cancelled.'); }
    else this.setEnabled(false);
    return true;
  }
  private cancelPreview() { this.placing = false; this.clearGhosts(); }
  private clearGhosts(resetTarget = true) {
    this.ghosts.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); }); this.ghosts = []; this.ghostKey = '';
    if (resetTarget) { this.target = undefined; this.appendTarget = undefined; this.valid = false; }
  }
  update(time: number) {
    this.group.position.set(-this.world.origin.x, 0, -this.world.origin.y);
    if (time - this.routeTime > 250) {
      for (const parked of this.parked) {
        try { const poses = consistPoses(this.network, parked.train.items.map(item => item.definition), parked.position); parked.visuals.forEach((visual, i) => applyVehiclePose(visual, this.itemPose(poses[i].pose, parked.train.items[i].facing))); parked.supported = true; }
        catch { parked.supported = false; }
      }
      $('train-support').hidden = this.parked.every(train => train.supported); this.routeTime = time;
    }
    if (this.enabled && this.placing && this.pointer) {
      const signature = [...this.camera.position, this.camera.rotation.x, this.camera.rotation.y, this.world.origin.x, this.world.origin.y, this.network.segments.size].join(',');
      if (signature !== this.signature) this.pointerMove(this.pointer); this.signature = signature;
    }
  }
  dispose() {
    this.setEnabled(false); this.abort.abort(); this.scene.remove(this.group);
    this.parked.forEach(train => train.visuals.forEach(disposeVehicle)); this.parked = []; this.group.clear();
  }
}
