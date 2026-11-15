import { vehicleBogie } from './catalog';
import type { VehicleDefinition } from './catalog';
import type { Side, TrackNetwork } from '../track/model';
import { sampleTrack, vehiclePose } from './placement';
import type { TrackPosition } from './placement';

export interface TrainItem { id: number; definition: VehicleDefinition; facing: Side }
export interface TrainConsist { id: number; name: string; items: TrainItem[]; placed: boolean }

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
    const train: TrainConsist = { id, name: `Train ${id}`, items: [], placed: false };
    this.trains.push(train); this.selected = train; return train;
  }
  add(definition: VehicleDefinition) {
    const train = this.selected;
    if (!train || train.placed) throw new Error('Create or select an unplaced train first.');
    if (!compatibleGauges([...train.items.map(item => item.definition), definition]).length) throw new Error('These vehicles have no compatible gauge in common.');
    const item: TrainItem = { id: this.nextItem++, definition, facing: 1 }; train.items.push(item); return item;
  }
  remove(trainId: number, itemId: number) {
    const train = this.selected;
    if (!train || train.id !== trainId || train.placed) return false;
    const index = train.items.findIndex(item => item.id === itemId);
    if (index === -1) return false;
    train.items.splice(index, 1); return true;
  }
  merge(target: TrainConsist, source: TrainConsist, end: 'front' | 'rear', reverseSource = false) {
    if (target === source || source.placed || !this.trains.includes(target) || !this.trains.includes(source)) throw new Error('Choose two different trains, with an unplaced incoming train.');
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
export function consistPoses(network: TrackNetwork, definitions: readonly VehicleDefinition[], position: TrackPosition) {
  if (!definitions.length) throw new Error('Add at least one vehicle to this train.');
  const gauge = network.segments.get(position.edge)?.gauge;
  if (!gauge || !compatibleGauges(definitions).includes(gauge)) throw new Error('This train is incompatible with this track gauge.');
  const { offsets } = consistLayout(definitions);
  return definitions.map((definition, i) => {
    const location = sampleTrack(network, position, offsets[i]).position;
    return { definition, position: location, pose: vehiclePose(network, definition, location) };
  });
}

export function appendPositions(network: TrackNetwork, target: TrackPosition, oldLength: number, incomingLength: number, end: 'front' | 'rear') {
  const sign = end === 'front' ? 1 : -1;
  return {
    incoming: sampleTrack(network, target, sign * (oldLength / 2 + COUPLER_GAP + incomingLength / 2)).position,
    combined: sampleTrack(network, target, sign * (incomingLength + COUPLER_GAP) / 2).position,
  };
}
