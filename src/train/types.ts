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
