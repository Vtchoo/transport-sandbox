import type { VehicleDefinition } from "../types";
import type { VehicleModelParts } from "../model-parts";
import { buildRioGrandeBody } from "./rio-grande-body";

export const RIO_GRANDE_BAGGAGE: VehicleDefinition = {
  id: "drgw-wood-baggage",
  name: "Rio Grande · Wooden baggage car",
  kind: "coach",
  thumbnailSvg: "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 184 72\"><path d=\"M12 52V22h160v30Z\" fill=\"#e6b62f\"/><path d=\"M9 23v-6h166v6Z M21 14V9h142v5Z\" fill=\"#30372f\"/><path d=\"M23 15h138\" stroke=\"#85927c\" stroke-width=\"2\"/><g fill=\"#ce9f26\" stroke=\"#4c4630\"><path d=\"M54 24h27v26H54Z M102 24h27v26h-27Z\"/></g><path d=\"M22 28h20v10H22Z M141 28h20v10h-20Z M59 27h17v9H59Z M107 27h17v9h-17Z\" fill=\"#203d44\"/><path d=\"M14 45h156\" stroke=\"#b28829\" stroke-width=\"2\"/><path d=\"M13 51h161\" stroke=\"#30372f\" stroke-width=\"4\"/><g fill=\"#28332d\" stroke=\"#718071\" stroke-width=\"1.5\"><circle cx=\"31\" cy=\"58\" r=\"6\"/><circle cx=\"46\" cy=\"58\" r=\"6\"/><circle cx=\"138\" cy=\"58\" r=\"6\"/><circle cx=\"153\" cy=\"58\" r=\"6\"/></g></svg>",
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
