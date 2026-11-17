export interface BogieDefinition {
  id: string; name: string; gauge: number; wheelbase: number; wheelRadius: number;
  frameWidth: number; powered: boolean; frameStyle?: 'fb2' | 'archbar';
  /** Explicit longitudinal axle stations; omitted for the usual symmetric two-axle truck. */
  axlePositions?: readonly number[];
}
export interface VehicleDefinition {
  id: string; name: string; kind: 'locomotive' | 'coach' | 'dome';
  /** Side-view SVG owned by the vehicle model, used in catalog and consist lists. */
  thumbnailSvg: string;
  length: number; bodyLength: number; width: number; height: number; bogieCenters: number;
  bogiesByGauge: Readonly<Record<number, string>>;
  runningGear?: 'steam';
  /** Exhaust outlet in body-local metres; only placed, working engines emit. */
  exhaust?: { position: readonly [number, number, number] };
  /** Permanently coupled, independently articulated sections, measured from the item's center. */
  sections?: readonly { definition: VehicleDefinition; offset: number }[];
  physics: { massKg: number; powerW: number; tractiveEffortN: number; maxSpeedMps: number };
}
