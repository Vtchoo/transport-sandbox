import { vehicleBogie } from './catalog';
import type { VehicleDefinition } from './catalog';
import type { Side, TrackNetwork } from '../track/model';
import { sampleTrack, vehiclePose } from './placement';
import type { TrackPosition, TrackSampler } from './placement';

export interface TrainItem { id: number; definition: VehicleDefinition; facing: Side }
export interface TrainConsist { id: number; name: string; items: TrainItem[]; placed: boolean; speed: number; running: boolean; travelDirection: Side }
/** Metres per second; sub-millimetre drift counts as stopped. */
export function trainIsStopped(train: TrainConsist) { return !train.running && Number.isFinite(train.speed) && Math.abs(train.speed) <= 0.001; }
export interface BuilderSnapshot { trains: TrainConsist[]; selected?: number }

export function compatibleGauges(definitions: readonly VehicleDefinition[]) {
  if (!definitions.length) return [];
  return Object.keys(definitions[0].bogiesByGauge).map(Number).filter(gauge => definitions.every(def => {
    try { vehicleBogie(def, gauge); return true; } catch { return false; }
  }));
}
export class TrainBuilder {
  readonly trains: TrainConsist[] = [];
  selected?: TrainConsist;
  private nextTrain = 1;
  private nextItem = 1;
  create() {
    const id = this.nextTrain++;
    const train: TrainConsist = { id, name: `Train ${id}`, items: [], placed: false, speed: 0, running: false, travelDirection: 1 };
    this.trains.push(train); this.selected = train; return train;
  }
  add(definition: VehicleDefinition, index = this.selected?.items.length ?? 0) {
    const train = this.selected;
    if (!train) throw new Error('Create or open a train first.');
    if (!trainIsStopped(train)) throw new Error('Train must be stopped first.');
    if (!Number.isInteger(index) || index < 0 || index > train.items.length) throw new Error('Choose a valid insertion position.');
    if (!compatibleGauges([...train.items.map(item => item.definition), definition]).length) throw new Error('These vehicles have no compatible gauge in common.');
    const item: TrainItem = { id: this.nextItem++, definition, facing: 1 }; train.items.splice(index, 0, item); return item;
  }
  /** Index is a gap in the target's current list, before removing the source item. */
  moveItem(sourceId: number, itemId: number, targetId: number, index: number) {
    const source = this.trains.find(train => train.id === sourceId), target = this.trains.find(train => train.id === targetId);
    if (!source || !target) throw new Error('Choose two existing trains.');
    if (!trainIsStopped(source) || !trainIsStopped(target)) throw new Error('Train must be stopped first.');
    const from = source.items.findIndex(item => item.id === itemId), item = source.items[from];
    if (!item || !Number.isInteger(index) || index < 0 || index > target.items.length) throw new Error('Choose a valid vehicle and insertion position.');
    if (source === target && (index === from || index === from + 1)) return false;
    if (source !== target && !compatibleGauges([...target.items.map(item => item.definition), item.definition]).length) throw new Error('These vehicles have no compatible gauge in common.');
    source.items.splice(from, 1);
    target.items.splice(source === target && from < index ? index - 1 : index, 0, item);
    this.selected = target; return true;
  }
  flip(itemId: number) {
    const train = this.selected, item = train?.items.find(item => item.id === itemId);
    if (!train || !item) return false;
    if (!trainIsStopped(train)) throw new Error('Train must be stopped first.');
    item.facing = -item.facing as Side; return true;
  }
  snapshot(): BuilderSnapshot {
    return { trains: this.trains.map(train => ({ ...train, items: train.items.map(item => ({ ...item })) })), selected: this.selected?.id };
  }
  restore(snapshot: BuilderSnapshot) {
    this.trains.splice(0, this.trains.length, ...snapshot.trains.map(train => ({ ...train, items: train.items.map(item => ({ ...item })) })));
    this.selected = this.trains.find(train => train.id === snapshot.selected);
    // IDs stay monotonic even when a snapshot is restored and then edited.
    this.nextTrain = Math.max(this.nextTrain, ...this.trains.map(train => train.id + 1));
    this.nextItem = Math.max(this.nextItem, ...this.trains.flatMap(train => train.items.map(item => item.id + 1)));
  }
  remove(trainId: number, itemId: number) {
    const train = this.selected;
    if (!train || train.id !== trainId) return false;
    if (!trainIsStopped(train)) throw new Error('Train must be stopped first.');
    const index = train.items.findIndex(item => item.id === itemId);
    if (index === -1) return false;
    train.items.splice(index, 1); return true;
  }
  merge(target: TrainConsist, source: TrainConsist, end: 'front' | 'rear', reverseSource = false) {
    if (target === source || source.placed || !this.trains.includes(target) || !this.trains.includes(source)) throw new Error('Choose two different trains, with an unplaced incoming train.');
    if (!trainIsStopped(target) || !trainIsStopped(source)) throw new Error('Train must be stopped first.');
    const incoming = reverseSource ? [...source.items].reverse().map(item => ({ ...item, facing: -item.facing as Side })) : source.items;
    const items = end === 'front' ? [...incoming, ...target.items] : [...target.items, ...incoming];
    if (!items.length || !compatibleGauges(items.map(item => item.definition)).length) throw new Error('These trains have no compatible gauge in common.');
    target.items = items; this.trains.splice(this.trains.indexOf(source), 1); this.selected = target;
    return target;
  }
}

export const COUPLER_GAP = 0.12;
export function consistLayout(definitions: readonly VehicleDefinition[]) {
  const length = definitions.reduce((sum, def) => sum + def.length, 0) + Math.max(0, definitions.length - 1) * COUPLER_GAP;
  let front = length / 2;
  const offsets = definitions.map(def => {
    const offset = front - def.length / 2; front -= def.length + COUPLER_GAP; return offset;
  });
  return { length, offsets };
}

/** Keep the first surviving original vehicle at its existing distance along the route. */
export function editedConsistPosition(network: TrackNetwork, before: readonly TrainItem[], after: readonly TrainItem[], position: TrackPosition, route?: TrackSampler) {
  const original = consistLayout(before.map(item => item.definition)), next = consistLayout(after.map(item => item.definition));
  const oldIndex = before.findIndex(item => after.some(candidate => candidate.id === item.id));
  const offset = oldIndex < 0 ? (original.length - next.length) / 2 : original.offsets[oldIndex] - next.offsets[after.findIndex(item => item.id === before[oldIndex].id)];
  return sampleTrack(network, position, offset, route).position;
}
export function consistPoses(network: TrackNetwork, definitions: readonly VehicleDefinition[], position: TrackPosition, route?: TrackSampler) {
  if (!definitions.length) throw new Error('Add at least one vehicle to this train.');
  const gauge = network.segments.get(position.edge)?.gauge;
  if (!gauge || !compatibleGauges(definitions).includes(gauge)) throw new Error('This train is incompatible with this track gauge.');
  const { offsets } = consistLayout(definitions);
  return definitions.map((definition, i) => {
    const location = sampleTrack(network, position, offsets[i], route).position;
    return { definition, position: location, pose: vehiclePose(network, definition, location, route) };
  });
}

export function appendPositions(network: TrackNetwork, target: TrackPosition, oldLength: number, incomingLength: number, end: 'front' | 'rear', route?: TrackSampler) {
  const sign = end === 'front' ? 1 : -1;
  return {
    incoming: sampleTrack(network, target, sign * (oldLength / 2 + COUPLER_GAP + incomingLength / 2), route).position,
    combined: sampleTrack(network, target, sign * (incomingLength + COUPLER_GAP) / 2, route).position,
  };
}
