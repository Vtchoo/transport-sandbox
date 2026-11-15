import * as THREE from 'three';
import type { Side, TrackNetwork } from '../track/model';
import { vehicleDefinition } from './catalog';
import { makeVehicle, disposeVehicle, tintVehicle } from './render';
import type { VehicleVisual } from './render';
import { applyVehiclePose, vehiclesOverlap, sampleTrack } from './placement';
import type { TrackPosition } from './placement';
import type { VehiclePose } from './placement';
import { TrainBuilder, compatibleGauges, consistLayout, consistPoses, appendPositions, editedConsistPosition, trainIsStopped } from './consist';
import type { TrainConsist, BuilderSnapshot } from './consist';
import type { TerrainWorld } from '../terrain/world';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id)! as T;
type End = 'front' | 'rear';
interface ParkedTrain { train: TrainConsist; visuals: VehicleVisual[]; position: TrackPosition; supported: boolean }
interface AppendTarget { parked: ParkedTrain; end: End; combined: TrackPosition }
interface SavedTransform { position: number[]; quaternion: number[] }
interface EditorSnapshot {
  builder: BuilderSnapshot;
  parked: { train: number; position: TrackPosition; supported: boolean; visuals: { gauge: number; transforms: SavedTransform[] }[] }[];
}
export class TrainEditor {
  readonly group = new THREE.Group();
  readonly builder = new TrainBuilder();
  onClose?: () => void;
  onChange?: (action?: string) => void;
  onMessage?: (train: number | undefined, text: string) => void;
  onWarning?: (text: string) => void;
  private parked: ParkedTrain[] = [];
  private ghosts: VehicleVisual[] = [];
  private ghostKey = '';
  private target?: TrackPosition;
  private appendTarget?: AppendTarget;
  private facing: Side = 1;
  private reverseAppend = false;
  private visible = false;
  private placing = false;
  private valid = false;
  private pointer?: { clientX: number; clientY: number };
  private signature = '';
  private routeTime = 0;
  private history: { label: string; state: EditorSnapshot }[] = [];
  constructor(private scene: THREE.Scene, private camera: THREE.PerspectiveCamera, private canvas: HTMLCanvasElement, private world: TerrainWorld, private network: TrackNetwork) { scene.add(this.group); }
  get enabled() { return this.visible; }
  get isPlacing() { return this.placing; }
  get canUndo() { return this.history.length > 0; }
  isSupported(id: number) { return this.parked.find(parked => parked.train.id === id)?.supported ?? true; }
  trackGauge(id: number) { const parked = this.parked.find(parked => parked.train.id === id); return parked && this.network.segments.get(parked.position.edge)?.gauge; }
  selectTrain(id: number) {
    if (this.builder.selected?.id !== id) this.cancelPreview();
    this.builder.selected = this.builder.trains.find(train => train.id === id);
  }
  addVehicle(id: string, targetId?: number, index?: number) {
    return this.editConsist('Add vehicle', () => {
      const train = targetId === undefined ? this.builder.create() : this.builder.trains.find(train => train.id === targetId);
      if (!train) throw new Error('Open a train in Edit train first.');
      const definition = vehicleDefinition(id), gauge = this.trackGauge(train.id);
      if (!trainIsStopped(train)) throw new Error('Train must be stopped first.');
      if (gauge !== undefined && !compatibleGauges([...train.items.map(item => item.definition), definition]).includes(gauge)) throw new Error(`This vehicle is incompatible with the train’s ${gauge.toLocaleString()} mm track.`);
      this.builder.selected = train; this.builder.add(definition, index ?? train.items.length);
    });
  }
  editConsist(label: string, action: () => boolean | void) {
    const originals = this.parked.map(parked => ({ parked, items: parked.train.items.map(item => ({ ...item })), position: { ...parked.position } }));
    let lifted = false;
    const success = this.perform(label, () => {
      if (action() === false) return false;
      const changed = originals.filter(({ parked, items }) => items.length !== parked.train.items.length || items.some((item, index) => { const next = parked.train.items[index]; return next?.id !== item.id || next.facing !== item.facing || next.definition !== item.definition; }));
      const affected = new Set(changed.map(({ parked }) => parked));
      const created: VehicleVisual[] = [];
      const plans: { parked: ParkedTrain; position?: TrackPosition; visuals?: VehicleVisual[] }[] = [];
      // Shrinking sources are evaluated before growing destinations during transfers.
      changed.sort((a, b) => (consistLayout(a.parked.train.items.map(item => item.definition)).length - consistLayout(a.items.map(item => item.definition)).length) - (consistLayout(b.parked.train.items.map(item => item.definition)).length - consistLayout(b.items.map(item => item.definition)).length));
      try {
        for (const { parked, items, position } of changed) {
          if (!trainIsStopped(parked.train)) throw new Error('Train must be stopped first.');
          if (!parked.train.items.length) { plans.push({ parked }); continue; }
          let visuals: VehicleVisual[] | undefined;
          try {
            const next = editedConsistPosition(this.network, items, parked.train.items, position);
            visuals = this.createVisuals(parked.train, next); created.push(...visuals);
            const others = [...this.parked.filter(other => !affected.has(other)).flatMap(other => other.visuals), ...plans.flatMap(plan => plan.visuals ?? [])];
            if (visuals.some(visual => others.some(other => vehiclesOverlap(visual, other)))) throw new Error('Not enough free track: another train occupies this space.');
            plans.push({ parked, position: next, visuals });
          } catch (error) {
            const message = (error as Error).message;
            if (!message.startsWith('Not enough') && !message.includes('curve is too tight')) throw error;
            if (visuals) { for (const visual of visuals) { this.group.remove(visual.group); disposeVehicle(visual); created.splice(created.indexOf(visual), 1); } }
            plans.push({ parked }); lifted = true;
          }
        }
      } catch (error) { created.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); }); throw error; }
      for (const plan of plans) {
        plan.parked.visuals.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); });
        if (plan.visuals && plan.position) { plan.parked.visuals = plan.visuals; plan.parked.position = plan.position; plan.parked.supported = true; }
        else { plan.parked.train.placed = false; this.parked.splice(this.parked.indexOf(plan.parked), 1); }
      }
    });
    if (success && lifted) { const text = 'Not enough free track for the edited train. It has been lifted for repositioning.'; this.message(text); this.onWarning?.(text); }
    return success;
  }
  startPlacement() {
    const train = this.builder.selected; if (!this.enabled || !train || train.placed || !train.items.length) return;
    this.placing = true; this.reverseAppend = false; this.clearGhosts(); this.builderInfo();
    this.message('Hover track to preview. R reverses; Esc cancels.'); this.canvas.focus({ preventScroll: true });
    if (this.pointer) this.pointerMove(this.pointer);
  }
  reversePreview() {
    if (!this.enabled || !this.placing) return;
    this.facing = this.facing === 1 ? -1 : 1; this.reverseAppend = !this.reverseAppend;
    if (this.pointer) this.pointerMove(this.pointer);
  }
  setEnabled(enabled: boolean) { if (!enabled) this.cancelPreview(); this.visible = enabled; }
  private builderInfo() { this.canvas.classList.toggle('placing-train', this.placing); this.onChange?.(); }
  private message(text: string) {
    this.onMessage?.(this.builder.selected?.id, text);
    const hint = $('train-placement-hint'); hint.hidden = !this.enabled || !this.placing || !this.pointer;
    hint.textContent = `${text}\n${this.valid ? 'Click to place' : 'Choose a track position'} · R reverse · Esc cancel`;
    hint.classList.toggle('invalid', !this.valid);
    if (this.pointer) { hint.style.left = `${Math.max(8, Math.min(this.pointer.clientX + 20, innerWidth - 280))}px`; hint.style.top = `${Math.max(8, Math.min(this.pointer.clientY + 22, innerHeight - 110))}px`; }
  }
  trainFocus(id: number) {
    const parked = this.parked.find(parked => parked.train.id === id);
    if (!parked) return;
    const center = new THREE.Vector3(); parked.visuals.forEach(visual => center.add(visual.group.position)); center.divideScalar(parked.visuals.length);
    return { center: center.sub(new THREE.Vector3(this.world.origin.x, 0, this.world.origin.y)), length: consistLayout(parked.train.items.map(item => item.definition)).length };
  }
  pickTrain(event: { clientX: number; clientY: number }) {
    const rect = this.canvas.getBoundingClientRect(), ray = new THREE.Raycaster();
    ray.setFromCamera(new THREE.Vector2((event.clientX - rect.left) / rect.width * 2 - 1, 1 - (event.clientY - rect.top) / rect.height * 2), this.camera);
    this.group.updateMatrixWorld(true);
    const hit = ray.intersectObjects(this.parked.flatMap(train => train.visuals.map(visual => visual.group)), true)[0];
    if (!hit) return;
    for (const parked of this.parked) for (const visual of parked.visuals) { let object: THREE.Object3D | null = hit.object; while (object) { if (object === visual.group) return parked.train.id; object = object.parent; } }
  }
  private capture(): EditorSnapshot {
    return { builder: this.builder.snapshot(), parked: this.parked.map(parked => ({
      train: parked.train.id, position: { ...parked.position }, supported: parked.supported,
      visuals: parked.visuals.map(visual => ({ gauge: visual.gauge, transforms: [visual.group, visual.body, ...visual.bogies].map(object => ({ position: object.position.toArray(), quaternion: object.quaternion.toArray() })) })),
    })) };
  }
  private restore(state: EditorSnapshot) {
    this.parked.forEach(parked => parked.visuals.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); }));
    this.builder.restore(state.builder);
    this.parked = state.parked.map(saved => {
      const train = this.builder.trains.find(train => train.id === saved.train)!;
      const visuals = train.items.map((item, i) => {
        const visual = makeVehicle(item.definition, saved.visuals[i].gauge);
        [visual.group, visual.body, ...visual.bogies].forEach((object, j) => { const transform = saved.visuals[i].transforms[j]; object.position.fromArray(transform.position); object.quaternion.fromArray(transform.quaternion); });
        this.group.add(visual.group); return visual;
      });
      return { train, visuals, position: { ...saved.position }, supported: saved.supported };
    });
  }
  perform(label: string, action: () => boolean | void) {
    const before = this.capture();
    try {
      if (action() === false) return false;
      this.history.push({ label, state: before }); if (this.history.length > 50) this.history.shift();
      this.cancelPreview(); this.onChange?.(label); this.builderInfo(); this.message(`${label} complete. Ctrl+Z to undo.`); return true;
    } catch (error) { this.restore(before); this.cancelPreview(); this.builderInfo(); this.message((error as Error).message); this.onWarning?.((error as Error).message); return false; }
  }
  undo() {
    const entry = this.history.pop(); if (!entry) return;
    this.cancelPreview(); this.restore(entry.state);
    this.onChange?.('Undo'); this.builderInfo(); this.message(`Undid: ${entry.label}.`);
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
    $('train-coupler').hidden = true;
    let appendError: string | undefined;
    try {
      const append = this.pickAppend(event, consistLayout(definitions).length);
      if (append) {
        this.target = { ...append.incoming, facing: append.incoming.facing * (this.reverseAppend ? -1 : 1) as Side }; this.appendTarget = append;
        const length = consistLayout(append.parked.train.items.map(item => item.definition)).length;
        const point = this.project(sampleTrack(this.network, append.parked.position, (append.end === 'front' ? 1 : -1) * length / 2).point);
        const marker = $('train-coupler'); marker.hidden = false; marker.style.left = `${point.x}px`; marker.style.top = `${point.y}px`;
      }
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
      this.message(this.appendTarget ? `Attach to ${this.appendTarget.end} · ${this.appendTarget.parked.train.name}` : `${train.name} · ${definitions.length} vehicles · ready to place`);
    } catch (error) {
      const raw = (error as Error).message;
      this.message(raw.includes('gauge') ? `Requires ${compatibleGauges(definitions).map(gauge => `${gauge.toLocaleString()} mm`).join(' or ')} track. This track is ${gauge.toLocaleString()} mm.` : raw.includes('Not enough') ? 'Not enough track ahead or behind this point. Move the train farther along the track.' : raw);
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
    const append = this.appendTarget, target = { ...this.target };
    this.perform(append ? 'Attach trains' : 'Place train', () => {
      if (append) {
        const parked = append.parked;
        this.builder.merge(parked.train, train, append.end, this.reverseAppend);
        parked.visuals.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); });
        parked.position = { ...append.combined }; parked.visuals = this.createVisuals(parked.train, parked.position); parked.supported = true;
      } else {
        const position = target, visuals = this.createVisuals(train, position); train.placed = true;
        this.parked.push({ train, visuals, position, supported: true });
      }
    });
    return true;
  }
  leave() { this.pointer = undefined; this.clearGhosts(); }
  suspend() { this.pointer = undefined; this.clearGhosts(); }
  escape() {
    if (!this.enabled) return false;
    if (this.placing) { this.cancelPreview(); this.builderInfo(); this.message('Placement cancelled. Your train is unchanged.'); }
    else if (this.onClose) this.onClose(); else this.setEnabled(false);
    return true;
  }
  private cancelPreview() { this.placing = false; this.canvas.classList.remove('placing-train'); this.clearGhosts(); }
  private clearGhosts(resetTarget = true) {
    $('train-placement-hint').hidden = true; $('train-coupler').hidden = true;
    this.ghosts.forEach(visual => { this.group.remove(visual.group); disposeVehicle(visual); }); this.ghosts = []; this.ghostKey = '';
    if (resetTarget) { this.target = undefined; this.appendTarget = undefined; this.valid = false; }
  }
  update(time: number) {
    this.group.position.set(-this.world.origin.x, 0, -this.world.origin.y);
    if (time - this.routeTime > 250) {
      const previous = this.parked.map(train => train.supported).join(',');
      for (const parked of this.parked) {
        try { const poses = consistPoses(this.network, parked.train.items.map(item => item.definition), parked.position); parked.visuals.forEach((visual, i) => applyVehiclePose(visual, this.itemPose(poses[i].pose, parked.train.items[i].facing))); parked.supported = true; }
        catch { parked.supported = false; }
      }
      this.routeTime = time;
      if (previous !== this.parked.map(train => train.supported).join(',')) this.onChange?.();
    }
    if (this.enabled && this.placing && this.pointer) {
      const signature = [...this.camera.position, this.camera.rotation.x, this.camera.rotation.y, this.world.origin.x, this.world.origin.y, this.network.segments.size].join(',');
      if (signature !== this.signature) this.pointerMove(this.pointer); this.signature = signature;
    }
  }
  dispose() {
    this.setEnabled(false); this.scene.remove(this.group);
    this.parked.forEach(train => train.visuals.forEach(disposeVehicle)); this.parked = []; this.group.clear();
  }
}
