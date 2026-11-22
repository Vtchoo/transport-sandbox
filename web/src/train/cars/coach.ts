import type { VehicleDefinition } from '../types';
import type { VehicleModelParts } from '../model-parts';
import { buildPassengerBody } from './passenger-body';

export const COACH: VehicleDefinition = {
  id: 'atsf-coach',
  name: 'Santa Fe · Streamlined coach',
  kind: 'coach',
  thumbnailSvg: "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 184 72\"><path d=\"M10 52V23q0-9 12-9h138q12 0 12 9v29Z\" fill=\"#bdc9ca\"/><path d=\"M17 26h148v11H17Z\" fill=\"#254755\" stroke=\"#b6c8ca\" stroke-width=\"3\" stroke-dasharray=\"12 3\"/><path d=\"M13 43h156m-156 4h156\" stroke=\"#869e9f\" stroke-width=\"2\"/><path d=\"M9 53h164\" stroke=\"#223637\" stroke-width=\"4\"/><g fill=\"#233335\" stroke=\"#8fa3a2\" stroke-width=\"2\"><circle cx=\"34\" cy=\"58\" r=\"7\"/><circle cx=\"50\" cy=\"58\" r=\"7\"/><circle cx=\"134\" cy=\"58\" r=\"7\"/><circle cx=\"150\" cy=\"58\" r=\"7\"/></g></svg>",
  length: 25.908, bodyLength: 24.6,
  width: 3.048, height: 4.04,
  bogieCenters: 18.288,
  bogiesByGauge: { 1435: 'passenger-2axle-1435' },
  physics: { massKg: 56800, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 45.599 },
};

export function buildCoach(parts: VehicleModelParts, definition: VehicleDefinition) {
  buildPassengerBody(parts, definition, 'CHAIR CAR · 3150');
}
