import * as THREE from "three";

/** Offscreen world view sharing the main renderer, with no additional WebGL context. */
export class SceneViewport {
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

  render(
    renderer: THREE.WebGLRenderer,
    scene: THREE.Scene,
    canvas: HTMLCanvasElement,
    bounds: THREE.Box3,
  ) {
    const ctx = canvas.getContext("2d");
    if (!ctx || bounds.isEmpty()) return;
    const sphere = bounds.getBoundingSphere(new THREE.Sphere());
    const fov = THREE.MathUtils.degToRad(this.camera.fov / 2);
    const distance = Math.max(12, (sphere.radius * 1.12) / Math.sin(fov));
    this.camera.position
      .copy(sphere.center)
      .addScaledVector(new THREE.Vector3(1, 0.85, 1).normalize(), distance);
    this.camera.far = Math.max(14000, distance + sphere.radius * 2);
    this.camera.updateProjectionMatrix();
    this.camera.lookAt(sphere.center);
    const previous = renderer.getRenderTarget(),
      viewport = renderer.getViewport(new THREE.Vector4());
    const scissor = renderer.getScissor(new THREE.Vector4()),
      scissorTest = renderer.getScissorTest();
    try {
      renderer.setRenderTarget(this.target);
      renderer.setScissorTest(false);
      renderer.render(scene, this.camera);
      renderer.readRenderTargetPixels(this.target, 0, 0, 384, 216, this.pixels);
      if (canvas.width !== 384 || canvas.height !== 216) {
        canvas.width = 384;
        canvas.height = 216;
      }
      const frame = ctx.createImageData(384, 216);
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
      renderer.setRenderTarget(previous);
      renderer.setViewport(viewport);
      renderer.setScissor(scissor);
      renderer.setScissorTest(scissorTest);
    }
  }
  dispose() {
    this.target.dispose();
  }
}
