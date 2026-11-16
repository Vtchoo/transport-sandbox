import type { VehicleDefinition } from "../types";
import type { VehicleModelParts } from "../model-parts";
import { buildRioGrandeBody } from "./rio-grande-body";

export const RIO_GRANDE_BAGGAGE: VehicleDefinition = {
  id: "drgw-wood-baggage",
  name: "Rio Grande · Wooden baggage car",
  kind: "coach",
  length: 15.2,
  bodyLength: 13.2,
  width: 2.65,
  height: 3.85,
  bogieCenters: 10.2,
  bogiesByGauge: { 914: "archbar-914", 1435: "archbar-1435" },
  physics: { massKg: 20000, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 12.5 },
};
export function buildRioGrandeBaggage(
  parts: VehicleModelParts,
  definition: VehicleDefinition,
) {
  buildRioGrandeBody(parts, definition, true);
}
