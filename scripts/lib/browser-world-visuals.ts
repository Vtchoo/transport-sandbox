import * as THREE from 'three';
import { TerrainWorld } from '../../src/terrain/world';
import { DEFAULT_SETTINGS } from '../../src/terrain/generator';

/** Sample the actual browser terrain/water materials with the game's lighting. */
export function worldVisualFixtures(skyVertex: string, skyFragment: string) {
  const originalWorker = globalThis.Worker;
  // The source material factory lives on TerrainWorld. No terrain jobs are needed.
  globalThis.Worker = class { terminate() {} } as unknown as typeof Worker;
  let world: TerrainWorld;
  try { world = new TerrainWorld(DEFAULT_SETTINGS); } finally { globalThis.Worker = originalWorker; }
  const renderer = new THREE.WebGLRenderer({ antialias: false, preserveDrawingBuffer: true });
  renderer.setSize(1, 1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0xb5cede);
  scene.fog = new THREE.FogExp2(0xb5cede, .00034);
  scene.add(new THREE.HemisphereLight(0xd6eaff, 0x7b7157, 2.4));
  const sun = new THREE.DirectionalLight(0xffefd3, 2.7);
  sun.position.set(-.7, .8, -.4); scene.add(sun);
  const camera = new THREE.OrthographicCamera(-.01, .01, .01, -.01, .01, 14000);
  camera.up.set(0, 0, -1);
  const geometry = new THREE.PlaneGeometry(20000, 20000);
  geometry.rotateX(-Math.PI / 2);
  const color = [.2, .4, .1];
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(Array.from({ length: 4 }, () => color).flat(), 3));
  const mesh = new THREE.Mesh(geometry, world.material);
  scene.add(mesh);
  const fixtures: Record<string, unknown>[] = [];
  try {
    for (const material of ['terrain', 'water'])
      for (const [x, z, depth] of [[0, 0, 100], [123.25, -456.5, 100], [0, 0, 1500], [0, 0, 6000]]) {
        mesh.material = material === 'water' ? world.water.material : world.material;
        camera.position.set(x, depth, z); camera.lookAt(x, 0, z);
        renderer.render(scene, camera);
        const gl = renderer.getContext(), pixel = new Uint8Array(4);
        gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
        fixtures.push({ material, x, z, depth, color, pixel: [...pixel] });
      }
    mesh.material = world.material;
    camera.position.set(0, 100, 0); camera.lookAt(0, 0, 0);
    const palette: [string, number[]][] = [
      ['meadow', world.generator.color(123.25, -456.5, 200, .1)],
      ['shore', world.generator.color(123.25, -456.5, 0, .1)],
      ['rock', world.generator.color(123.25, -456.5, 800, 1)],
      ['snow', world.generator.color(123.25, -456.5, 2000, .1)],
      ['cut-fill', [.26, .19, .12]],
    ];
    for (const [name, rgb] of palette) {
      geometry.setAttribute('color', new THREE.Float32BufferAttribute(Array.from({ length: 4 }, () => rgb).flat(), 3));
      renderer.render(scene, camera);
      const gl = renderer.getContext(), pixel = new Uint8Array(4);
      gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
      fixtures.push({ material: 'terrain', palette: name, x: 0, z: 0, depth: 100, color: rgb, pixel: [...pixel] });
    }
    scene.remove(mesh);
    const skyGeometry = new THREE.SphereGeometry(12000, 24, 16);
    const skyMaterial = new THREE.ShaderMaterial({
      side: THREE.BackSide, depthWrite: false,
      uniforms: { sunDirection: { value: sun.position.clone().normalize() } },
      vertexShader: skyVertex, fragmentShader: skyFragment,
    });
    const sky = new THREE.Mesh(skyGeometry, skyMaterial);
    scene.add(sky);
    try {
      for (const direction of [[0, 0, -1], [0, 1, 0], [-.7, .8, -.4], [1, .3, 0]]) {
        const eye = new THREE.PerspectiveCamera(65, 1, .5, 14000);
        if (Math.abs(direction[1]) === 1) eye.up.set(0, 0, -1);
        eye.lookAt(new THREE.Vector3().fromArray(direction));
        renderer.render(scene, eye);
        const gl = renderer.getContext(), pixel = new Uint8Array(4);
        gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
        fixtures.push({ material: 'sky', direction, pixel: [...pixel] });
      }
    } finally { skyGeometry.dispose(); skyMaterial.dispose(); }
    return fixtures;
  } finally {
    geometry.dispose(); world.dispose(); renderer.dispose(); renderer.forceContextLoss();
  }
}
