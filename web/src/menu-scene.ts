import * as THREE from "three";
import { vehicleDefinition } from "./train/catalog";
import {
  makeVehicle,
  disposeVehicle,
  type VehicleVisual,
} from "./train/render";
import { consistLayout } from "./train/consist";
import { VehicleInstances } from "./train/instances";
import { makeTrack, disposeTrack } from "./track/render";
import { makePlatform } from "./track/stations";
import { Trees, type TreeChunk } from "./terrain/trees";
import { TrainSmoke } from "./train/smoke";

export const MENU_TRAINS = [
  {
    gauge: 1435,
    cars: [
      "tgv-sud-est-power",
      "tgv-sud-est-coach",
      "tgv-sud-est-coach",
      "tgv-sud-est-coach",
      "tgv-sud-est-power",
    ],
  },
  {
    gauge: 914,
    cars: [
      "drgw-consolidation",
      "drgw-wood-baggage",
      "drgw-wood-coach",
      "drgw-wood-coach",
    ],
  },
  {
    gauge: 1435,
    cars: ["atsf-f7a", "atsf-coach", "atsf-pleasure-dome", "atsf-coach"],
  },
] as const;

/** A self-contained station diorama; gameplay terrain is not generated behind the menu. */
export class MenuScene {
  readonly scene = new THREE.Scene();
  readonly camera = new THREE.PerspectiveCamera(38, 1, 0.1, 1800);
  private visuals: VehicleVisual[] = [];
  private instances = new VehicleInstances();
  private scenery = new THREE.Group();
  private trees = new Trees();
  private woodland: TreeChunk;
  private length: number;
  private smoke = new TrainSmoke(256);
  private lastTime?: number;
  private origin = new THREE.Vector2();
  constructor(
    readonly preset = Math.floor(Math.random() * MENU_TRAINS.length),
  ) {
    const consist = MENU_TRAINS[preset],
      definitions = consist.cars.map(vehicleDefinition);
    const layout = consistLayout(definitions);
    this.length = layout.length;
    this.scene.background = new THREE.Color(0xb3c9d0);
    this.scene.fog = new THREE.Fog(0xb3c9d0, 170, 580);
    this.scene.add(new THREE.HemisphereLight(0xe6f2ff, 0x6c714d, 2.6));
    const sun = new THREE.DirectionalLight(0xffdda9, 3.2);
    sun.position.set(-60, 90, 45);
    this.scene.add(sun);
    this.scene.add(this.scenery, this.instances.group, this.smoke.points);
    this.visuals = definitions.map((definition, i) => {
      const visual = makeVehicle(definition, consist.gauge);
      visual.group.position.z = layout.offsets[i];
      if (preset === 0 && i === definitions.length - 1)
        visual.group.rotation.y = Math.PI;
      this.scene.add(visual.group);
      return visual;
    });
    this.instances.sync(this.visuals);
    const half = layout.length / 2 + 16;
    const curve = new THREE.CubicBezierCurve3(
      new THREE.Vector3(0, 0, -half),
      new THREE.Vector3(0, 0, -half / 3),
      new THREE.Vector3(0, 0, half / 3),
      new THREE.Vector3(0, 0, half),
    );
    this.scenery.add(
      makeTrack(curve, consist.gauge),
      makePlatform(curve, consist.gauge, 1),
    );
    const ground = new THREE.Mesh(
      new THREE.PlaneGeometry(1800, 1800),
      new THREE.MeshStandardMaterial({ color: 0x6c8050, roughness: 1 }),
    );
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -0.66;
    this.scenery.add(ground);
    const box = (
      w: number,
      h: number,
      d: number,
      x: number,
      y: number,
      z: number,
      color: number,
    ) => {
      const mesh = new THREE.Mesh(
        new THREE.BoxGeometry(w, h, d),
        new THREE.MeshStandardMaterial({ color, roughness: 0.85 }),
      );
      mesh.position.set(x, y, z);
      this.scenery.add(mesh);
    };
    // Platform shelter, paving, benches and lamp posts.
    box(8, 0.08, half * 2 + 12, 8.5, -0.58, 0, 0x9b9584);
    for (let z = -half + 12; z < half - 8; z += 17) {
      box(0.12, 3.5, 0.12, 4, 2.2, z, 0x3e514a);
      box(3.6, 0.18, 14, 3.7, 4, z, 0x535f58);
      box(0.65, 0.14, 2.8, 3.7, 1.1, z + 2, 0x7a5739);
      for (const dz of [-0.95, 0.95])
        box(0.5, 0.55, 0.1, 3.7, 0.75, z + 2 + dz, 0x3e514a);
      box(0.12, 4.5, 0.12, 5.1, 2.65, z - 6, 0x3e514a);
      box(0.5, 0.18, 0.8, 5.1, 4.9, z - 6, 0xe9dfb8);
    }
    const treeData: number[] = [];
    for (let row = 0; row < 8; row++)
      for (let i = 0; i < 32; i++) {
        const jitter = Math.sin(i * 17.13 + row * 8.7);
        treeData.push(
          18 + row * 10 + jitter * 3,
          -0.65,
          (i - 16) * 10 + Math.cos(row * 12 + i) * 4,
          9 + (jitter + 1) * 3,
          3 + (jitter + 1),
          i + row * 0.7,
          row % 2,
        );
      }
    this.woodland = this.trees.create(new Float32Array(treeData));
    this.scene.add(this.woodland.group);
    this.update(0, 16 / 9);
  }
  update(time: number, aspect: number) {
    this.camera.aspect = aspect;
    this.camera.updateProjectionMatrix();
    const front = this.length / 2;
    const portrait = THREE.MathUtils.clamp(1 / aspect, 1, 1.65);
    const drift = Math.sin(time * 0.00004) * 0.3;
    // Platform-height, front three-quarter view; the consist recedes into the station.
    this.camera.position.set(-7 / portrait + drift, 2.8, front + 13 * portrait);
    this.camera.lookAt(-0.8, 2, front - 9 / portrait);
    const delta =
      this.lastTime === undefined ? 0 : (time - this.lastTime) / 1000;
    this.lastTime = time;
    this.smoke.update(
      delta,
      this.visuals.map((visual) => ({ visual, effort: 0 })),
      this.origin,
      this.camera,
    );
  }
  dispose() {
    this.smoke.dispose();
    this.instances.dispose();
    this.visuals.forEach(disposeVehicle);
    this.visuals = [];
    this.trees.release(this.woodland);
    this.trees.dispose();
    disposeTrack(this.scenery);
    this.scene.clear();
  }
}
