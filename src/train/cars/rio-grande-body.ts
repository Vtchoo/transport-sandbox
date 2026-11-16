import type { VehicleDefinition } from "../types";
import type { VehicleModelParts } from "../model-parts";

/** Painted wooden sides, clerestory roof and open end platforms of the pictured set. */
export function buildRioGrandeBody(
  parts: VehicleModelParts,
  definition: VehicleDefinition,
  baggage: boolean,
) {
  const {
    body,
    box,
    shell,
    label,
    materials: { yellow, black, glass },
  } = parts;
  const half = definition.bodyLength / 2,
    w = definition.width / 2 - 0.15;
  box(
    body,
    definition.width - 0.3,
    2.12,
    definition.bodyLength,
    0,
    2.12,
    0,
    yellow,
  );
  shell(
    [
      { z: -half - 0.15, w: w + 0.06, bottom: 3.13, shoulder: 3.16, top: 3.57 },
      { z: half + 0.15, w: w + 0.06, bottom: 3.13, shoulder: 3.16, top: 3.57 },
    ],
    black,
  );
  box(body, 1.23, 0.26, definition.bodyLength - 0.5, 0, 3.55, 0, yellow);
  shell(
    [
      { z: -half + 0.12, w: 0.76, bottom: 3.66, shoulder: 3.68, top: 3.85 },
      { z: half - 0.12, w: 0.76, bottom: 3.66, shoulder: 3.68, top: 3.85 },
    ],
    black,
  );
  for (const side of [-1, 1]) {
    for (let z = -half + 0.14; z < half; z += 0.19)
      box(body, 0.014, 0.77, 0.012, side * (w + 0.012), 1.55, z, black);
    for (const y of [1.08, 1.98, 2.99, 3.14])
      box(
        body,
        0.045,
        0.055,
        definition.bodyLength + 0.04,
        side * (w + 0.025),
        y,
        0,
        yellow,
      );
    const windows = baggage
      ? [-half + 1.05, -half + 2.1, half - 2.1, half - 1.05]
      : Array.from(
          { length: 12 },
          (_, i) => -half + 0.7 + (i * (definition.bodyLength - 1.4)) / 11,
        );
    for (const z of windows) {
      box(body, 0.025, 0.95, 0.8, side * (w + 0.02), 2.5, z, black);
      box(body, 0.035, 0.82, 0.68, side * (w + 0.04), 2.5, z, glass);
      box(body, 0.045, 0.035, 0.72, side * (w + 0.065), 2.58, z, yellow);
      box(body, 0.05, 1.02, 0.045, side * (w + 0.055), 2.49, z - 0.43, yellow);
    }
    if (baggage)
      for (const z of [-2.15, 2.15]) {
        box(body, 0.065, 1.97, 1.65, side * (w + 0.04), 2.05, z, black).name =
          "baggage-door";
        box(body, 0.075, 1.83, 1.48, side * (w + 0.06), 2.05, z, yellow);
        for (const dz of [-0.39, 0.39])
          box(body, 0.085, 0.54, 0.53, side * (w + 0.07), 2.57, z + dz, glass);
        for (const dy of [-0.38, -0.03])
          box(body, 0.09, 0.025, 1.25, side * (w + 0.09), 1.8 + dy, z, black);
        box(body, 0.105, 0.23, 0.045, side * (w + 0.09), 2.03, z + 0.55, black);
        box(body, 0.09, 0.045, 1.9, side * (w + 0.07), 3.06, z, black);
      }
    for (let z = -half + 0.65; z < half - 0.2; z += 0.8)
      box(body, 0.025, 0.13, 0.5, side * 0.63, 3.57, z, glass);
    label(
      "D E N V E R   &   R I O   G R A N D E",
      8.8,
      0.2,
      side * (w + 0.06),
      3.045,
      0,
      (side * Math.PI) / 2,
      "#342819",
    );
    label(
      baggage ? "BAGGAGE · EXPRESS" : "PASSENGER",
      2.8,
      0.16,
      side * (w + 0.06),
      1.78,
      0,
      (side * Math.PI) / 2,
      "#342819",
    );
  }
  for (const end of [-1, 1]) {
    const platform = end * (half + 0.5);
    box(body, definition.width - 0.05, 0.15, 0.96, 0, 1.04, platform, black);
    box(body, 0.82, 1.9, 0.03, 0, 2.04, end * (half + 0.025), black);
    box(body, 0.68, 1.78, 0.045, 0, 2.04, end * (half + 0.04), yellow);
    box(body, 0.49, 0.61, 0.055, 0, 2.5, end * (half + 0.06), glass);
    for (const side of [-1, 1]) {
      for (const x of [side * 0.46, side * (w - 0.1)])
        box(body, 0.035, 0.85, 0.035, x, 1.55, end * (half + 0.93), black);
      box(
        body,
        w - 0.46,
        0.04,
        0.04,
        (side * (w + 0.36)) / 2,
        1.97,
        end * (half + 0.93),
        black,
      );
      for (let i = 0; i < 3; i++)
        box(
          body,
          0.45,
          0.07,
          0.67,
          side * (w - 0.12),
          0.42 + i * 0.23,
          platform,
          black,
        );
    }
  }
  for (const z of [-3, 3]) box(body, 0.65, 0.24, 1.15, 0, 0.83, z, black);
}
