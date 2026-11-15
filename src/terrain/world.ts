import * as THREE from 'three';
import { CHUNK_SIZE, TerrainGenerator } from './generator';
import type { ChunkData, TerrainSettings } from './generator';
import { wantedTiles, VIEW_RADIUS } from './lod';
import type { Tile } from './lod';
import { EarthworkField, prepareEarthPaths } from '../track/earthworks';
import type { EarthPath } from '../track/earthworks';

interface Chunk { mesh: THREE.Mesh<THREE.BufferGeometry, THREE.MeshStandardMaterial>; segments: number; x: number; z: number; dirty?: boolean }
interface WorkerSlot { worker: Worker; job?: { id: number; key: string } }
export class TerrainWorld {
  readonly group = new THREE.Group();
  readonly generator: TerrainGenerator;
  readonly origin = new THREE.Vector2();
  readonly material: THREE.MeshStandardMaterial;
  readonly water: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshStandardMaterial>;
  private chunks = new Map<string, Chunk>();
  private wanted = new Map<string, Tile>();
  private workers: WorkerSlot[] = [];
  private serial = 0;
  private cell = '';
  private disposed = false;
  private earthPaths: EarthPath[] = [];
  private revision = 0;
  private detailOrigin = { value: new THREE.Vector2() };
  error = '';
  constructor(readonly settings: TerrainSettings) {
    this.generator = new TerrainGenerator(settings);
    this.material = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.96 });
    // Metre-scale procedural ground texture, independent of mesh tessellation.
    this.material.onBeforeCompile = (shader) => {
      shader.uniforms.terrainOrigin = this.detailOrigin;
      shader.vertexShader = 'varying vec3 vTerrainPosition;\n' + shader.vertexShader;
      shader.vertexShader = shader.vertexShader.replace('#include <worldpos_vertex>', '#include <worldpos_vertex>\nvTerrainPosition = (modelMatrix * vec4(transformed, 1.0)).xyz;');
      shader.fragmentShader = `uniform vec2 terrainOrigin; varying vec3 vTerrainPosition;
        float groundNoise(vec2 p) {
          vec2 i = floor(p), f = fract(p); f = f*f*(3.0-2.0*f);
          vec3 q = fract(vec3(i.xyx) * .1031); q += dot(q, q.yzx + 33.33);
          float a = fract((q.x+q.y)*q.z);
          q = fract(vec3((i+vec2(1,0)).xyx)*.1031); q += dot(q,q.yzx+33.33); float b=fract((q.x+q.y)*q.z);
          q = fract(vec3((i+vec2(0,1)).xyx)*.1031); q += dot(q,q.yzx+33.33); float c=fract((q.x+q.y)*q.z);
          q = fract(vec3((i+vec2(1,1)).xyx)*.1031); q += dot(q,q.yzx+33.33); float d=fract((q.x+q.y)*q.z);
          return mix(mix(a,b,f.x),mix(c,d,f.x),f.y);
        }\n` + shader.fragmentShader;
      shader.fragmentShader = shader.fragmentShader.replace('#include <color_fragment>', `#include <color_fragment>
        vec2 p = vTerrainPosition.xz + terrainOrigin;
        float grain = groundNoise(p * 0.7) * 0.13 + groundNoise(p * 0.08) * 0.13;
        diffuseColor.rgb *= 0.87 + grain;
      `);
    };
    this.water = new THREE.Mesh(new THREE.PlaneGeometry(32000, 32000), new THREE.MeshStandardMaterial({
      color: 0x437c85, metalness: 0.25, roughness: 0.28, transparent: true, opacity: 0.88,
    }));
    this.water.rotation.x = -Math.PI / 2;
    this.water.position.y = settings.seaLevel;
    this.group.add(this.water);
    try {
      for (let i = 0; i < Math.min(3, Math.max(1, (navigator.hardwareConcurrency || 4) - 2)); i++) {
        const slot: WorkerSlot = { worker: new Worker(new URL('./terrain.worker.ts', import.meta.url), { type: 'module' }) };
        slot.worker.onmessage = (event: MessageEvent<ChunkData>) => this.receive(slot, event.data);
        slot.worker.onerror = () => { this.error = 'Terrain generation failed. Return to the menu and try again.'; };
        this.workers.push(slot);
      }
    } catch { this.error = 'This browser could not start terrain workers. Try a current desktop browser.'; }
  }
  private key(x: number, z: number) { return `${x},${z}`; }
  setEarthworks(paths: EarthPath[]) {
    const previous = this.generator.earthworks;
    this.earthPaths = prepareEarthPaths(paths, (x, z) => this.generator.baseHeight(x, z), this.settings.mountainHeight);
    this.generator.earthworks = paths.length ? new EarthworkField(this.earthPaths) : undefined;
    this.revision++;
    const natural = (x: number, z: number) => this.generator.baseHeight(x, z);
    for (const chunk of this.chunks.values()) {
      const x = chunk.x * CHUNK_SIZE, z = chunk.z * CHUNK_SIZE;
      if (previous?.affectsBounds(x, z, CHUNK_SIZE, natural) || this.generator.earthworks?.affectsBounds(x, z, CHUNK_SIZE, natural)) chunk.dirty = true;
    }
    this.pump();
  }
  update(worldX: number, worldZ: number, altitude: number) {
    if (this.disposed) return;
    const cx = Math.floor(worldX / CHUNK_SIZE), cz = Math.floor(worldZ / CHUNK_SIZE);
    // Floating origin keeps GPU coordinates small as the world grows.
    this.origin.set(cx * CHUNK_SIZE, cz * CHUNK_SIZE);
    this.detailOrigin.value.copy(this.origin);
    this.water.position.x = worldX - this.origin.x;
    this.water.position.z = worldZ - this.origin.y;
    for (const chunk of this.chunks.values()) chunk.mesh.position.set(chunk.x * CHUNK_SIZE - this.origin.x, 0, chunk.z * CHUNK_SIZE - this.origin.y);
    const cell = `${this.key(cx, cz)},${Math.floor(altitude / 256)}`;
    if (cell !== this.cell) {
      this.cell = cell;
      this.wanted = new Map(wantedTiles(worldX, worldZ, altitude, (x, z) => this.generator.height(x, z)).map(tile => [this.key(tile.x, tile.z), tile]));
      for (const [key, chunk] of this.chunks) {
        if (!this.wanted.has(key)) { this.group.remove(chunk.mesh); chunk.mesh.geometry.dispose(); this.chunks.delete(key); }
      }
    }
    this.pump();
  }
  private pump() {
    if (this.disposed || this.error) return;
    const busy = new Set(this.workers.flatMap(slot => slot.job ? [slot.job.key] : []));
    // Upgrades and newly exposed nearby tiles always precede distant work.
    for (const slot of this.workers) {
      if (slot.job) continue;
      for (const [key, tile] of this.wanted) {
        const chunk = this.chunks.get(key);
        if (busy.has(key) || chunk?.segments === tile.segments && !chunk.dirty) continue;
        const id = ++this.serial;
        slot.job = { id, key };
        busy.add(key);
        slot.worker.postMessage({ id, x: tile.x, z: tile.z, segments: tile.segments, settings: this.settings, earthPaths: this.earthPaths, revision: this.revision });
        break;
      }
    }
  }
  private receive(slot: WorkerSlot, data: ChunkData) {
    if (this.disposed || slot.job?.id !== data.id) return;
    const key = slot.job.key;
    slot.job = undefined;
    if (this.wanted.get(key)?.segments === data.segments && data.revision === this.revision) {
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.BufferAttribute(data.positions, 3));
      geometry.setAttribute('normal', new THREE.BufferAttribute(data.normals, 3));
      geometry.setAttribute('color', new THREE.BufferAttribute(data.colors, 3));
      geometry.setIndex(new THREE.BufferAttribute(data.indices, 1));
      geometry.computeBoundingSphere();
      const previous = this.chunks.get(key);
      const mesh = new THREE.Mesh(geometry, this.material);
      mesh.position.set(data.x * CHUNK_SIZE - this.origin.x, 0, data.z * CHUNK_SIZE - this.origin.y);
      this.group.add(mesh);
      if (previous) { this.group.remove(previous.mesh); previous.mesh.geometry.dispose(); }
      this.chunks.set(key, { mesh, segments: data.segments, x: data.x, z: data.z });
    }
    this.pump();
  }
  get stats() {
    let triangles = 0, nearby = 0, earthworksPending = 0;
    for (const [key, chunk] of this.chunks) {
      triangles += chunk.mesh.geometry.index!.count / 3;
      if (chunk.dirty) earthworksPending++;
      const target = this.wanted.get(key);
      if (target && target.ring <= 1 && chunk.segments === target.segments) nearby++;
    }
    return { chunks: this.chunks.size, total: this.wanted.size, triangles, nearby, earthworksPending, radius: VIEW_RADIUS * CHUNK_SIZE };
  }
  hasTerrainAt(x: number, z: number) {
    return this.chunks.has(this.key(Math.floor(x / CHUNK_SIZE), Math.floor(z / CHUNK_SIZE)));
  }
  /** Height of the actual rendered triangles, for safe free-camera ground clearance. */
  groundHeight(x: number, z: number) {
    if (this.generator.earthworks) return this.generator.height(x, z);
    const chunk = this.chunks.get(this.key(Math.floor(x / CHUNK_SIZE), Math.floor(z / CHUNK_SIZE)));
    const step = CHUNK_SIZE / (chunk?.segments ?? 128);
    const gx = Math.floor(x / step) * step, gz = Math.floor(z / step) * step;
    const u = (x - gx) / step, v = (z - gz) / step;
    const a = this.generator.height(gx, gz), b = this.generator.height(gx + step, gz);
    const c = this.generator.height(gx, gz + step), d = this.generator.height(gx + step, gz + step);
    return u + v <= 1 ? a + (b - a) * u + (c - a) * v : d + (c - d) * (1 - u) + (b - d) * (1 - v);
  }
  dispose() {
    this.disposed = true;
    this.workers.forEach(slot => slot.worker.terminate());
    this.chunks.forEach(chunk => chunk.mesh.geometry.dispose());
    this.chunks.clear();
    this.wanted.clear();
    this.group.clear();
    this.material.dispose();
    this.water.geometry.dispose();
    this.water.material.dispose();
  }
}
