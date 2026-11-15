import type { VehicleDefinition } from '../types';
import type { VehicleModelParts } from '../model-parts';
import { buildPassengerBody } from './passenger-body';

export const COACH: VehicleDefinition = {
  id: 'atsf-coach',
  name: 'Santa Fe · Streamlined coach',
  kind: 'coach',
  length: 25.908, bodyLength: 24.6,
  width: 3.048, height: 4.04,
  bogieCenters: 18.288,
  bogiesByGauge: { 1435: 'passenger-2axle-1435' },
  physics: { massKg: 56800, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 45.599 },
};

export function buildCoach(parts: VehicleModelParts, definition: VehicleDefinition) {
  buildPassengerBody(parts, definition, 'CHAIR CAR · 3150');
}
