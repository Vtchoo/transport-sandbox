import * as THREE from "three";
import asset from "../assets/dash9.json";
import type { BogieDefinition, VehicleDefinition } from "../types";
import type { VehicleVisual } from "../render";
import { importedVehicleParts } from "../imported";
import { createVehicleParts } from "../model-parts";
import { makeWheelsets } from "../bogies";

export const DASH9: VehicleDefinition = {
  id: "ge-dash9",
  name: "GE Dash 9 · SketchUp prototype",
  kind: "locomotive",
  thumbnailSvg:
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 184 72"><path d="M9 51V16h166v35H9Z" fill="#e0e5e6" stroke="#637781" stroke-width="2"/><path d="M10 37h164" stroke="#879ba3" stroke-width="2"/><path d="M28 52h38v8H28Z M120 52h38v8h-38Z" fill="#aebdc2"/><g fill="#26373e"><circle cx="31" cy="60" r="5"/><circle cx="47" cy="60" r="5"/><circle cx="63" cy="60" r="5"/><circle cx="123" cy="60" r="5"/><circle cx="139" cy="60" r="5"/><circle cx="155" cy="60" r="5"/></g></svg>',
  length: asset.bounds.size[2],
  bodyLength: asset.bounds.size[2],
  width: asset.bounds.size[0],
  height: asset.bounds.max[1],
  bogieCenters: asset.bogieCenters,
  bogiesByGauge: { 1435: "dash9-placeholder-1435" },
  // Placeholder outlet on the authored roof until an exhaust anchor is supplied.
  exhaust: {
    position: [0, asset.bounds.max[1] + 0.05, 0],
    kind: "diesel",
    idleRate: 1.2,
  },
  // Nominal 4400 hp; mass, effort and gearing are temporary gameplay assumptions.
  physics: {
    massKg: 181437,
    powerW: 3281080,
    tractiveEffortN: 600000,
    maxSpeedMps: 31.2928,
  },
};

export function makeDash9(
  definition: VehicleDefinition,
  gauge: number,
  spec: BogieDefinition,
): VehicleVisual {
  const imported = importedVehicleParts(asset),
    group = new THREE.Group();
  group.name = definition.id;
  const body = imported.body;
  body.name = "body";
  const parts = createVehicleParts(body);
  const bogies: [THREE.Group, THREE.Group] = [
    makeWheelsets(spec, parts),
    makeWheelsets(spec, parts),
  ];
  bogies[0].add(imported.front);
  bogies[1].add(imported.rear);
  bogies[0].position.z = definition.bogieCenters / 2;
  bogies[1].position.z = -definition.bogieCenters / 2;
  group.add(body, ...bogies);
  // Only temporary wheelset materials were used from the procedural helpers.
  for (const [name, material] of Object.entries(parts.materials))
    if (name !== "black" && name !== "roof") material.dispose();
  return { group, body, bogies, definition, gauge, separateBogies: true };
}
