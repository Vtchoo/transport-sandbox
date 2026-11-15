export interface BogieDefinition {
  id: string; name: string; gauge: number; wheelbase: number; wheelRadius: number;
  frameWidth: number; powered: boolean; frameStyle?: 'fb2';
}
export interface VehicleDefinition {
  id: string; name: string; kind: 'locomotive' | 'coach' | 'dome';
  length: number; bodyLength: number; width: number; height: number; bogieCenters: number;
  bogiesByGauge: Readonly<Record<number, string>>;
  physics: { massKg: number; powerW: number; tractiveEffortN: number; maxSpeedMps: number };
}
export const BOGIES: Readonly<Record<string, BogieDefinition>> = {
  'ge-fb2-1435': { id: 'ge-fb2-1435', name: 'GE FB-2 · standard gauge', gauge: 1435, wheelbase: 2.7432, wheelRadius: 0.508, frameWidth: 2.65, powered: true, frameStyle: 'fb2' },
  'blomberg-b-1435': { id: 'blomberg-b-1435', name: 'Blomberg B · standard gauge', gauge: 1435, wheelbase: 2.7432, wheelRadius: 0.508, frameWidth: 2.55, powered: true },
  'passenger-2axle-1435': { id: 'passenger-2axle-1435', name: 'Passenger two-axle · standard gauge', gauge: 1435, wheelbase: 2.5908, wheelRadius: 0.4572, frameWidth: 2.45, powered: false },
};
export const VEHICLES: readonly VehicleDefinition[] = [
  { id: 'atsf-f7a', name: 'Santa Fe F7A · Warbonnet', kind: 'locomotive', length: 15.4432, bodyLength: 14.4, width: 2.9972, height: 4.5, bogieCenters: 9.144, bogiesByGauge: { 1435: 'blomberg-b-1435' }, physics: { massKg: 104326, powerW: 1118550, tractiveEffortN: 251326, maxSpeedMps: 45.599 } },
  { id: 'atsf-coach', name: 'Santa Fe · Streamlined coach', kind: 'coach', length: 25.908, bodyLength: 24.6, width: 3.048, height: 4.04, bogieCenters: 18.288, bogiesByGauge: { 1435: 'passenger-2axle-1435' }, physics: { massKg: 56800, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 45.599 } },
  { id: 'atsf-pleasure-dome', name: 'Santa Fe · Pleasure Dome lounge', kind: 'dome', length: 25.7048, bodyLength: 24.4, width: 3.048, height: 4.72, bogieCenters: 18.288, bogiesByGauge: { 1435: 'passenger-2axle-1435' }, physics: { massKg: 75000, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 45.599 } },
  { id: 'ge-809', name: 'GE 809 · Wide-cab demonstrator', kind: 'locomotive', length: 20.2184, bodyLength: 19.2, width: 3.0861, height: 4.6863, bogieCenters: 11.9888, bogiesByGauge: { 1435: 'ge-fb2-1435' }, physics: { massKg: 127006, powerW: 2908230, tractiveEffortN: 304704, maxSpeedMps: 31.2928 } },
];
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
