import * as THREE from "three";
import type { VehicleModelParts } from "../model-parts";
import type { VehicleMotion } from "../motion";

/** Fixed-frame 2-8-0 drivers, quartered cranks and a constrained slider-crank on each side. */
export function buildSteamGear(
  parts: VehicleModelParts,
  gauge: number,
  steel: THREE.MeshStandardMaterial,
  tireMaterial: THREE.MeshStandardMaterial,
): VehicleMotion {
  const {
    body,
    mesh,
    box,
    cylinder,
    materials: { silver, black },
  } = parts;
  const driverRadius = 0.65,
    crankRadius = 0.22,
    rodLength = 2.1,
    pistonY = 0.85;
  const wheelX = gauge / 2000 + 0.06;
  const axles = [-2.2, -0.85, 0.5, 1.85];
  const moving: THREE.Group[] = [],
    wheels: { group: THREE.Group; radius: number; quarter: number }[] = [];
  const sideRigs: {
    side: number;
    coupling: THREE.Group;
    connecting: THREE.Group;
    crosshead: THREE.Group;
  }[] = [];
  const component = (name: string) => {
    const group = new THREE.Group();
    group.name = name;
    body.add(group);
    moving.push(group);
    return group;
  };
  const axle = (z: number, radius: number, driver: boolean) => {
    const shaft = cylinder(body, 0.07, wheelX * 2, 0, radius, z, black);
    shaft.rotation.z = Math.PI / 2;
    for (const side of [-1, 1]) {
      const wheel = component(driver ? "driver-wheel" : "leading-wheel");
      wheel.position.set(side * wheelX, radius, z);
      wheel.userData.treadCenter = side * wheelX;
      wheels.push({
        group: wheel,
        radius,
        quarter: driver && side < 0 ? Math.PI / 2 : 0,
      });
      const tire = mesh(
        wheel,
        new THREE.TorusGeometry(radius - 0.065, 0.065, 6, 24),
        tireMaterial,
        0,
        0,
        0,
      );
      tire.rotation.y = Math.PI / 2;
      const flange = cylinder(
        wheel,
        radius + 0.02,
        0.028,
        -side * 0.07,
        0,
        0,
        black,
      );
      flange.rotation.z = Math.PI / 2;
      const hub = cylinder(wheel, driver ? 0.15 : 0.09, 0.15, 0, 0, 0, steel);
      hub.rotation.z = Math.PI / 2;
      for (let i = 0; i < (driver ? 10 : 7); i++) {
        const spoke = box(wheel, 0.1, radius * 1.65, 0.045, 0, 0, 0, steel);
        spoke.rotation.x = (i * Math.PI) / (driver ? 10 : 7);
      }
      if (driver) {
        const pin = cylinder(
          wheel,
          0.075,
          0.18,
          side * 0.14,
          -crankRadius,
          0,
          tireMaterial,
        );
        pin.rotation.z = Math.PI / 2;
        pin.name = "crank-pin";
      }
    }
  };
  axles.forEach((z) => axle(z, driverRadius, true));
  axle(3.93, 0.35, false);
  for (const side of [-1, 1]) {
    const coupling = component("coupling-rod");
    box(coupling, 0.09, 0.12, 4.38, 0, 0, 0, silver);
    for (const z of axles) {
      const bearing = cylinder(
        coupling,
        0.105,
        0.08,
        0,
        0,
        z + 0.175,
        tireMaterial,
      );
      bearing.rotation.z = Math.PI / 2;
    }
    const connecting = component("connecting-rod");
    box(connecting, 0.09, 0.105, 1, 0, 0, 0, silver);
    const crosshead = component("crosshead");
    box(crosshead, 0.18, 0.18, 0.28, 0, 0, 0, tireMaterial);
    const piston = cylinder(crosshead, 0.045, 0.6, 0, 0, 0.25, silver);
    piston.rotation.x = Math.PI / 2;
    for (const y of [pistonY - 0.15, pistonY + 0.15])
      box(body, 0.08, 0.045, 1.05, side * (wheelX + 0.23), y, 2.4, steel);
    sideRigs.push({ side, coupling, connecting, crosshead });
  }
  const reference = wheels.find(
    (wheel) => wheel.radius === driverRadius && wheel.quarter === 0,
  )!.group;
  const motion: VehicleMotion = {
    parts: moving,
    advance(distance) {
      // The reference wheel itself stores phase, so undo restores the animation without a clock.
      const phase = THREE.MathUtils.euclideanModulo(
        reference.rotation.x + distance / driverRadius,
        Math.PI * 2,
      );
      for (const wheel of wheels) {
        if (wheel.radius === driverRadius)
          wheel.group.rotation.x = phase + wheel.quarter;
        else
          wheel.group.rotation.x = THREE.MathUtils.euclideanModulo(
            wheel.group.rotation.x + distance / wheel.radius,
            Math.PI * 2,
          );
      }
      for (const { side, coupling, connecting, crosshead } of sideRigs) {
        const angle = phase + (side < 0 ? Math.PI / 2 : 0);
        const y = driverRadius - crankRadius * Math.cos(angle),
          dz = -crankRadius * Math.sin(angle);
        coupling.position.set(side * (wheelX + 0.14), y, -0.175 + dz);
        const crankZ = 0.5 + dz;
        const pistonZ = crankZ + Math.sqrt(rodLength ** 2 - (pistonY - y) ** 2);
        connecting.position.set(
          side * (wheelX + 0.23),
          (y + pistonY) / 2,
          (crankZ + pistonZ) / 2,
        );
        connecting.rotation.x = -Math.atan2(pistonY - y, pistonZ - crankZ);
        connecting.scale.z = rodLength;
        crosshead.position.set(side * (wheelX + 0.23), pistonY, pistonZ);
      }
    },
  };
  motion.advance(0);
  return motion;
}
