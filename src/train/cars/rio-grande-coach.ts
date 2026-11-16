import type { VehicleDefinition } from "../types";
import type { VehicleModelParts } from "../model-parts";
import { buildRioGrandeBody } from "./rio-grande-body";

export const RIO_GRANDE_COACH: VehicleDefinition = {
  id: "drgw-wood-coach",
  name: "Rio Grande · Wooden passenger coach",
  kind: "coach",
  length: 15.2,
  bodyLength: 13.2,
  width: 2.65,
  height: 3.85,
  bogieCenters: 10.2,
  bogiesByGauge: { 914: "archbar-914", 1435: "archbar-1435" },
  physics: { massKg: 24000, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 12.5 },
};
export function buildRioGrandeCoach(
  parts: VehicleModelParts,
  definition: VehicleDefinition,
) {
  buildRioGrandeBody(parts, definition, false);
}
