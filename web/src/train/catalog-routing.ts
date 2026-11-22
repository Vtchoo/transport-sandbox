import type { TrainConsist } from './consist';
import type { VehicleDefinition } from './catalog';
import { compatibleGauges } from './consist';

export type TrainTab = 'viewport' | 'edit';
/** Inspect only the frontmost train window; never fall through to a lower editor. */
export function catalogDestination(frontmost: { train: TrainConsist; tab: TrainTab } | undefined) {
  return frontmost?.tab === 'edit' ? frontmost.train : undefined;
}
export function catalogAllows(definition: VehicleDefinition, target?: TrainConsist, trackGauge?: number) {
  if (!target) return true;
  const gauges = compatibleGauges([...target.items.map(item => item.definition), definition]);
  return trackGauge === undefined ? gauges.length > 0 : gauges.includes(trackGauge);
}
