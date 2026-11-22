import * as THREE from "three";
import type { TrainWindows } from "./windows";
import { makeVehicle, disposeVehicle } from "./render";
import type { VehicleVisual } from "./render";
import { consistLayout } from "./consist";
import { vehicleDefinition } from "./catalog";
import { VehicleInstances } from "./instances";

/** Shares the game's renderer. No extra WebGL context per train window. */
export class TrainViewports {
  private target = new THREE.WebGLRenderTarget(384, 216);
  private pixels = new Uint8Array(384 * 216 * 4);
  private srgb = Uint8ClampedArray.from({ length: 256 }, (_, value) => {
    const linear = value / 255;
    return (
      255 *
      (linear <= 0.0031308
        ? linear * 12.92
        : 1.055 * linear ** (1 / 2.4) - 0.055)
    );
  });
  private camera = new THREE.PerspectiveCamera(42, 384 / 216, 0.1, 14000);
  private drafts = new Map<
    number,
    {
      key: string;
      scene: THREE.Scene;
      visuals: VehicleVisual[];
      instances: VehicleInstances;
    }
  >();
  private last = 0;
  private lastCatalog = 0;
  private catalog?: {
    id: string;
    scene: THREE.Scene;
    visual: VehicleVisual;
    center: THREE.Vector3;
    radius: number;
    halfHeight: number;
    started: number;
  };
  constructor(
    private renderer: THREE.WebGLRenderer,
    private worldScene: THREE.Scene,
  ) {}
  update(time: number, windows: TrainWindows) {
    this.updateCatalog(time, windows);
    if (time - this.last < 150) return;
    this.last = time;
    for (const [id, draft] of this.drafts)
      if (!windows.editor.builder.trains.some((train) => train.id === id)) {
        draft.instances.dispose();
        draft.visuals.forEach(disposeVehicle);
        this.drafts.delete(id);
      }
    for (const [id, view] of windows.views) {
      if (view.tab !== "viewport" || !windows.manager.isExpanded(`train:${id}`))
        continue;
      const train = windows.editor.builder.trains.find(
        (train) => train.id === id,
      );
      if (!train?.items.length) continue;
      const canvas =
        view.element.querySelector<HTMLCanvasElement>(".train-viewport")!;
      let scene = this.worldScene,
        focus = windows.editor.trainFocus(id);
      if (!train.placed) {
        const key = train.items
          .map((item) => `${item.id}:${item.facing}:${item.definition.id}`)
          .join(",");
        let draft = this.drafts.get(id);
        if (draft?.key !== key) {
          draft?.instances.dispose();
          draft?.visuals.forEach(disposeVehicle);
          const preview = new THREE.Scene();
          preview.background = new THREE.Color(0x304942);
          preview.add(new THREE.HemisphereLight(0xe9f6ff, 0x7b8164, 3));
          const light = new THREE.DirectionalLight(0xffefdc, 3);
          light.position.set(-20, 30, 25);
          preview.add(light);
          const layout = consistLayout(
            train.items.map((item) => item.definition),
          );
          const visuals = train.items.map((item, index) => {
            const visual = makeVehicle(
              item.definition,
              Number(Object.keys(item.definition.bogiesByGauge)[0]),
            );
            visual.group.position.z = layout.offsets[index];
            visual.group.rotation.y = item.facing === -1 ? Math.PI : 0;
            preview.add(visual.group);
            return visual;
          });
          const instances = new VehicleInstances();
          preview.add(instances.group);
          instances.sync(visuals);
          draft = { key, scene: preview, visuals, instances };
          this.drafts.set(id, draft);
        }
        scene = draft!.scene;
        focus = {
          center: draft!.visuals[0].group.position
            .clone()
            .add(new THREE.Vector3(0, 2, 0)),
          length: train.items[0].definition.length,
        };
      }
      if (!focus) continue;
      const distance = Math.max(14, focus.length * 0.75);
      this.camera.position
        .copy(focus.center)
        .add(
          new THREE.Vector3(distance * 0.95, distance * 0.48, distance * 0.45),
        );
      this.camera.lookAt(focus.center);
      this.renderCanvas(scene, canvas);
    }
  }
  private updateCatalog(time: number, windows: TrainWindows) {
    const id = windows.previewVehicle;
    if (this.catalog && this.catalog.id !== id) {
      disposeVehicle(this.catalog.visual);
      this.catalog = undefined;
    }
    if (
      !id ||
      !windows.manager.isExpanded("catalog") ||
      time - this.lastCatalog < 40
    )
      return;
    this.lastCatalog = time;
    if (!this.catalog) {
      const definition = vehicleDefinition(id),
        scene = new THREE.Scene();
      scene.background = new THREE.Color(0x304942);
      scene.add(new THREE.HemisphereLight(0xe9f6ff, 0x7b8164, 3));
      const light = new THREE.DirectionalLight(0xffefdc, 3);
      light.position.set(-20, 30, 25);
      scene.add(light);
      const visual = makeVehicle(
        definition,
        Number(Object.keys(definition.bogiesByGauge)[0]),
      );
      scene.add(visual.group);
      const bounds = new THREE.Box3().setFromObject(visual.group),
        size = bounds.getSize(new THREE.Vector3());
      this.catalog = {
        id,
        scene,
        visual,
        center: bounds.getCenter(new THREE.Vector3()),
        radius: Math.hypot(size.x, size.z) / 2,
        halfHeight: size.y / 2,
        started: time,
      };
    }
    const preview = this.catalog,
      angle = Math.PI / 3 + (time - preview.started) * 0.00035;
    // Fit the full orbit envelope to the wide viewport without excessive vertical padding.
    const elevation = 0.25,
      verticalFov = THREE.MathUtils.degToRad(this.camera.fov / 2),
      horizontalFov = Math.atan(Math.tan(verticalFov) * this.camera.aspect);
    const distance =
      Math.max(
        preview.radius / Math.sin(horizontalFov),
        (preview.radius * Math.sin(elevation) +
          preview.halfHeight * Math.cos(elevation)) /
          Math.sin(verticalFov),
      ) * 1.12;
    this.camera.position
      .copy(preview.center)
      .add(
        new THREE.Vector3(
          Math.sin(angle) * Math.cos(elevation),
          Math.sin(elevation),
          Math.cos(angle) * Math.cos(elevation),
        ).multiplyScalar(distance),
      );
    this.camera.lookAt(preview.center);
    this.renderCanvas(
      preview.scene,
      windows.catalog.viewport,
    );
  }
  private renderCanvas(scene: THREE.Scene, canvas: HTMLCanvasElement) {
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    const previous = this.renderer.getRenderTarget(),
      viewport = this.renderer.getViewport(new THREE.Vector4()),
      scissor = this.renderer.getScissor(new THREE.Vector4()),
      scissorTest = this.renderer.getScissorTest();
    try {
      this.renderer.setRenderTarget(this.target);
      this.renderer.setScissorTest(false);
      this.renderer.render(scene, this.camera);
      this.renderer.readRenderTargetPixels(
        this.target,
        0,
        0,
        384,
        216,
        this.pixels,
      );
      if (canvas.width !== 384 || canvas.height !== 216) {
        canvas.width = 384;
        canvas.height = 216;
      }
      const frame = ctx.createImageData(384, 216);
      // Offscreen targets contain linear RGB; Canvas 2D expects sRGB, with top-down rows.
      for (let y = 0; y < 216; y++)
        for (let x = 0; x < 384; x++) {
          const from = (y * 384 + x) * 4,
            to = ((215 - y) * 384 + x) * 4;
          frame.data[to] = this.srgb[this.pixels[from]];
          frame.data[to + 1] = this.srgb[this.pixels[from + 1]];
          frame.data[to + 2] = this.srgb[this.pixels[from + 2]];
          frame.data[to + 3] = this.pixels[from + 3];
        }
      ctx.putImageData(frame, 0, 0);
    } finally {
      this.renderer.setRenderTarget(previous);
      this.renderer.setViewport(viewport);
      this.renderer.setScissor(scissor);
      this.renderer.setScissorTest(scissorTest);
    }
  }
  clear() {
    for (const draft of this.drafts.values()) {
      draft.instances.dispose();
      draft.visuals.forEach(disposeVehicle);
    }
    this.drafts.clear();
    if (this.catalog) disposeVehicle(this.catalog.visual);
    this.catalog = undefined;
  }
  dispose() {
    this.clear();
    this.target.dispose();
  }
}
