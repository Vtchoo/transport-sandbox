import type { BogieDefinition, VehicleDefinition } from './types';
export type { BogieDefinition, VehicleDefinition } from './types';
import { F7A } from './locomotives/f7a';
import { COACH } from './cars/coach';
import { PLEASURE_DOME } from './cars/pleasure-dome';
import { GE809 } from './locomotives/ge809';

export const BOGIES: Readonly<Record<string, BogieDefinition>> = {
  'ge-fb2-1435': { id: 'ge-fb2-1435', name: 'GE FB-2 · standard gauge', gauge: 1435, wheelbase: 2.7432, wheelRadius: 0.508, frameWidth: 2.65, powered: true, frameStyle: 'fb2' },
  'blomberg-b-1435': { id: 'blomberg-b-1435', name: 'Blomberg B · standard gauge', gauge: 1435, wheelbase: 2.7432, wheelRadius: 0.508, frameWidth: 2.55, powered: true },
  'passenger-2axle-1435': { id: 'passenger-2axle-1435', name: 'Passenger two-axle · standard gauge', gauge: 1435, wheelbase: 2.5908, wheelRadius: 0.4572, frameWidth: 2.45, powered: false },
};
export const VEHICLES: readonly VehicleDefinition[] = [F7A, COACH, PLEASURE_DOME, GE809];
export function vehicleDefinition(id: string) {
  const definition = VEHICLES.find(item => item.id === id);
  if (!definition) throw new Error('Unknown vehicle model.');
  return definition;
}
export function vehicleBogie(definition: VehicleDefinition, gauge: number) {
  const id = definition.bogiesByGauge[gauge], bogie = id && BOGIES[id];
  if (!bogie || bogie.gauge !== gauge) throw new Error(`${definition.name} is unavailable for ${gauge.toLocaleString()} mm track.`);
  return bogie;
}
