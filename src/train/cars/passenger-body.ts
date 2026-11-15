import type { VehicleDefinition } from '../types';
import type { VehicleModelParts } from '../model-parts';

/** Common stainless shell, windows, doors and underbody fittings. */
export function buildPassengerBody(parts: VehicleModelParts, definition: VehicleDefinition, nameplate: string) {
  const { body, box, label, shell, materials: { silver, roof, black, glass, rubber } } = parts;
  const half = definition.bodyLength / 2, w = definition.width / 2;
  shell([{ z: -half, w, bottom: 1.25, shoulder: 3.32, top: 4.04 }, { z: half, w, bottom: 1.25, shoulder: 3.32, top: 4.04 }], silver);
  for (const side of [-1, 1]) {
    // Stainless-steel fluting, separate glass panes and vestibule doors.
    for (let rib = 0; rib < 9; rib++) box(body, 0.035, 0.026, definition.bodyLength - 0.6, side * (w + 0.012), 1.4 + rib * 0.095, 0, roof);
    for (const z of [-half + 0.65, half - 0.65]) {
      box(body, 0.025, 1.95, 0.75, side * (w + 0.02), 2.3, z, roof);
      box(body, 0.035, 0.55, 0.43, side * (w + 0.04), 2.86, z, glass);
      box(body, 0.025, 0.035, 0.18, side * (w + 0.03), 2.22, z + 0.18, black);
      for (let step = 0; step < 3; step++) box(body, 0.23, 0.07, 0.8, side * (w - 0.05), 0.57 + step * 0.22, z, black);
    }
    for (let i = 0; i < 13; i++) {
      const z = -9.8 + i * 1.63;
      box(body, 0.025, 0.91, 1.23, side * (w + 0.015), 2.8, z, black);
      box(body, 0.035, 0.8, 1.1, side * (w + 0.03), 2.8, z, glass);
    }
    label('S A N T A   F E', 6.5, 0.28, side * (w + 0.04), 3.47, 0, side * Math.PI / 2);
    label(nameplate, 3.5, 0.2, side * (w + 0.04), 2.07, 0, side * Math.PI / 2);
  }
  for (const end of [-1, 1]) {
    box(body, 1.05, 2.35, 0.35, 0, 2.45, end * (half + 0.1), rubber);
    box(body, 0.76, 1.93, 0.38, 0, 2.4, end * (half + 0.12), roof);
    box(body, 0.43, 0.53, 0.4, 0, 2.83, end * (half + 0.13), glass);
  }
  for (const z of [-6, 5]) box(body, 1.05, 0.44, 2.3, 0, 0.81, z, roof);
}
