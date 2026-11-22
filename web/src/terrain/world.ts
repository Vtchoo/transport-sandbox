import * as THREE from 'three';
import { CHUNK_SIZE, TerrainGenerator } from './generator';
import type { ChunkData, TerrainSettings } from './generator';
import { wantedTiles, VIEW_RADIUS } from './lod';
import type { Tile } from './lod';
import { EarthworkField, prepareEarthPaths } from '../track/earthworks';
import type { EarthPath } from '../track/earthworks';
import { Trees } from './trees';
import type { TreeChunk } from './trees';
import { TerrainLodCache } from './cache';
import { TreeStreaming } from './tree-streaming';
import { prepareTrees, type PreparedTrees } from './tree-data';

interface Chunk { mesh: THREE.Mesh<THREE.BufferGeometry, THREE.MeshStandardMaterial>; trees?: TreeChunk; prepared: PreparedTrees; segments: number; x: number; z: number; revision: number; bytes: number; dirty?: boolean }
interface WorkerSlot { worker: Worker; job?: { id: number; key: string } }
export class TerrainWorld {
  readonly group = new THREE.Group();
  readonly generator: TerrainGenerator;
  readonly origin = new THREE.Vector2();
  readonly material: THREE.MeshStandardMaterial;
  readonly water: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshStandardMaterial>;
  private chunks = new Map<string, Chunk>();
  private cache = new TerrainLodCache<Chunk>(chunk => this.releaseChunk(chunk));
  private wanted = new Map<string, Tile>();
  private workers: WorkerSlot[] = [];
  private serial = 0;
  private cell = '';
  private disposed = false;
  private earthPaths: EarthPath[] = [];
  private revision = 0;
  private detailOrigin = { value: new THREE.Vector2() };
  private contourEnabled = { value: 0 };
  private contourInterval = { value: 10 };
  private trees = new Trees();
  private treeStreaming = new TreeStreaming(this.trees);
  private completed: ChunkData[] = [];
  private retired: Chunk[] = [];
  private frameOperations = 0;
  private frameStarted = 0;
  /** Soft elapsed-time limit plus a hard cap; a single GPU operation cannot be preempted. */
  maxOperationsPerFrame = 2;
  frameBudgetMs = 2;
  private cameraPosition = new THREE.Vector3();
  error = '';
  constructor(readonly settings: TerrainSettings) {
    this.generator = new TerrainGenerator(settings);
    this.material = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.96 });
    // Metre-scale procedural ground texture, independent of mesh tessellation.
    this.material.onBeforeCompile = (shader) => {
      shader.uniforms.terrainOrigin = this.detailOrigin;
      shader.uniforms.contourEnabled = this.contourEnabled;
      shader.uniforms.contourInterval = this.contourInterval;
      shader.vertexShader = 'varying vec3 vTerrainPosition;\n' + shader.vertexShader;
      shader.vertexShader = shader.vertexShader.replace('#include <worldpos_vertex>', '#include <worldpos_vertex>\nvTerrainPosition = (modelMatrix * vec4(transformed, 1.0)).xyz;');
      shader.fragmentShader = `uniform vec2 terrainOrigin; varying vec3 vTerrainPosition;
        uniform float contourEnabled; uniform float contourInterval;
        // Screen-space antialiasing and fading prevent dense distant contours from shimmering.
        float contourLine(float elevation, float width) {
          float pixel = max(fwidth(elevation), 0.00001);
          float distanceToLevel = abs(fract(elevation + 0.5) - 0.5);
          float line = 1.0 - smoothstep(pixel * (width - 0.45), pixel * (width + 0.45), distanceToLevel);
          return line * (1.0 - smoothstep(0.15, 0.45, pixel)) * smoothstep(0.00001, 0.0001, pixel);
        }
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
        if (contourEnabled > 0.5) {
          float elevation = vTerrainPosition.y / contourInterval;
          float minorLine = contourLine(elevation, 0.8);
          float majorLine = contourLine(elevation / 5.0, 1.35);
          diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.075, 0.10, 0.065), max(minorLine * 0.65, majorLine * 0.9));
        }
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
  /** Shader-only overlay: follows rendered terrain and earthworks without rebuilding chunks. */
  setContours(enabled: boolean, interval = 10) {
    this.contourEnabled.value = enabled ? 1 : 0;
    this.contourInterval.value = Number.isFinite(interval) ? Math.max(1, interval) : 10;
    for (const chunk of this.chunks.values()) if (chunk.trees) chunk.trees.group.visible = !enabled;
  }
  setEarthworks(paths: EarthPath[]) {
    const previous = this.generator.earthworks;
    this.earthPaths = prepareEarthPaths(paths, (x, z) => this.generator.baseHeight(x, z), this.settings.mountainHeight);
    this.generator.earthworks = paths.length ? new EarthworkField(this.earthPaths) : undefined;
    this.revision++;
    const natural = (x: number, z: number) => this.generator.baseHeight(x, z);
    const affected = (cx: number, cz: number) => !!(previous?.affectsBounds(cx * CHUNK_SIZE, cz * CHUNK_SIZE, CHUNK_SIZE, natural)
      || this.generator.earthworks?.affectsBounds(cx * CHUNK_SIZE, cz * CHUNK_SIZE, CHUNK_SIZE, natural));
    this.cache.advanceRevision(this.revision, affected);
    for (const chunk of this.chunks.values()) {
      if (affected(chunk.x, chunk.z)) chunk.dirty = true;
      else chunk.revision = this.revision;
    }
    this.pump();
  }
  update(worldX: number, worldZ: number, altitude: number) {
    if (this.disposed) return;
    this.frameOperations = 0; this.frameStarted = performance.now();
    const cx = Math.floor(worldX / CHUNK_SIZE), cz = Math.floor(worldZ / CHUNK_SIZE);
    this.cameraPosition.set(worldX, altitude, worldZ);
    // Floating origin keeps GPU coordinates small as the world grows.
    this.origin.set(cx * CHUNK_SIZE, cz * CHUNK_SIZE);
    this.detailOrigin.value.copy(this.origin);
    this.water.position.x = worldX - this.origin.x;
    this.water.position.z = worldZ - this.origin.y;
    for (const chunk of this.chunks.values()) {
      chunk.mesh.position.set(chunk.x * CHUNK_SIZE - this.origin.x, 0, chunk.z * CHUNK_SIZE - this.origin.y);
      this.updateTrees(chunk);
    }
    const cell = `${this.key(cx, cz)},${Math.floor(altitude / 256)}`;
    if (cell !== this.cell) {
      this.cell = cell;
      this.wanted = new Map(wantedTiles(worldX, worldZ, altitude, (x, z) => this.generator.height(x, z)).map(tile => [this.key(tile.x, tile.z), tile]));
      for (const [key, chunk] of this.chunks) {
        if (!this.wanted.has(key)) { this.retireChunk(chunk); this.chunks.delete(key); }
      }
    }
    this.pump();
  }
  private pump(render = true) {
    if (this.disposed || this.error) return;
    const available = () => render && this.frameOperations < this.maxOperationsPerFrame && performance.now() - this.frameStarted < this.frameBudgetMs;
    // Give retirement a slot even while the camera keeps exposing new chunks.
    if (available()) {
      if (this.retired.length) { this.retired.shift()!.mesh.geometry.dispose(); this.frameOperations++; }
      else if (this.treeStreaming.releaseOne()) this.frameOperations++;
    }
    // Results arrive between frames. Upload on the next update, never inside worker callbacks.
    while (this.completed.length && available()) {
      const data = this.completed.shift()!;
      if (data.revision !== this.revision) continue;
      this.commit(data); this.frameOperations++;
    }
    // Cache hits require no worker slot, even while older LOD jobs are still running.
    for (const [key, tile] of this.wanted) {
      const current = this.chunks.get(key);
      if (current?.segments === tile.segments && !current.dirty) continue;
      if (!available()) break;
      const cached = this.cache.take(tile.x, tile.z, tile.segments, this.revision);
      if (cached) { this.mount(cached); this.frameOperations++; }
    }
    const busy = new Set(this.workers.flatMap(slot => slot.job ? [slot.job.key] : []));
    for (const data of this.completed) busy.add(this.key(data.x, data.z));
    // Upgrades and newly exposed nearby tiles always precede distant work.
    for (const slot of this.workers) {
      if (this.completed.length >= this.workers.length) break;
      if (slot.job) continue;
      for (const [key, tile] of this.wanted) {
        const chunk = this.chunks.get(key);
        if (busy.has(key) || chunk?.segments === tile.segments && !chunk.dirty
          || this.cache.has(tile.x, tile.z, tile.segments, this.revision)) continue;
        const id = ++this.serial;
        slot.job = { id, key };
        busy.add(key);
        slot.worker.postMessage({ id, x: tile.x, z: tile.z, segments: tile.segments, settings: this.settings, earthPaths: this.earthPaths, revision: this.revision });
        break;
      }
    }
    while (available()) {
      if (this.retired.length) this.retired.shift()!.mesh.geometry.dispose();
      else if (!this.treeStreaming.releaseOne()) break;
      this.frameOperations++;
    }
  }
  private receive(slot: WorkerSlot, data: ChunkData) {
    if (this.disposed || slot.job?.id !== data.id) return;
    slot.job = undefined;
    if (data.revision === this.revision) this.completed.push(data);
    // Scheduling is bounded by idle workers plus queued results; rendering waits for update().
    this.pump(false);
  }
  private commit(data: ChunkData) {
    const key = this.key(data.x, data.z);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(data.positions, 3));
    geometry.setAttribute('normal', new THREE.BufferAttribute(data.normals, 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(data.colors, 3));
    geometry.setIndex(new THREE.BufferAttribute(data.indices, 1));
    geometry.computeBoundingSphere();
    const mesh = new THREE.Mesh(geometry, this.material);
    const prepared = data.preparedTrees ?? prepareTrees(data.trees ?? new Float32Array());
    const bytes = 2 * (data.positions.byteLength + data.normals.byteLength + data.colors.byteLength + data.indices.byteLength) + prepared.bytes;
    const chunk = { mesh, prepared, segments: data.segments, x: data.x, z: data.z, revision: this.revision, bytes };
    if (this.wanted.get(key)?.segments === data.segments) this.mount(chunk);
    else this.cache.store(chunk);
  }
  private updateTrees(chunk: Chunk) {
    const centerY = chunk.mesh.geometry.boundingSphere?.center.y ?? 0;
    const distance = Math.hypot(chunk.x * CHUNK_SIZE + CHUNK_SIZE / 2 - this.cameraPosition.x,
      centerY - this.cameraPosition.y, chunk.z * CHUNK_SIZE + CHUNK_SIZE / 2 - this.cameraPosition.z);
    if (chunk.trees) this.trees.update(chunk.trees, distance);
  }
  private mount(chunk: Chunk) {
    const key = this.key(chunk.x, chunk.z), previous = this.chunks.get(key);
    // Reparent the trees before retiring their terrain holder. No empty frame during edits.
    if (chunk.segments >= 32)
      chunk.trees = this.treeStreaming.activate(key, chunk.mesh, chunk.prepared, this.contourEnabled.value > .5);
    else this.treeStreaming.deactivate(key);
    if (previous) { previous.trees = undefined; this.retireChunk(previous, false); }
    chunk.mesh.position.set(chunk.x * CHUNK_SIZE - this.origin.x, 0, chunk.z * CHUNK_SIZE - this.origin.y);
    this.group.add(chunk.mesh); this.updateTrees(chunk); this.chunks.set(key, chunk);
  }
  private retireChunk(chunk: Chunk, deactivateTrees = true) {
    if (deactivateTrees) this.treeStreaming.deactivate(this.key(chunk.x, chunk.z));
    chunk.trees = undefined;
    this.group.remove(chunk.mesh);
    if (chunk.dirty || chunk.revision !== this.revision) this.releaseChunk(chunk);
    else this.cache.store(chunk);
  }
  private releaseChunk(chunk: Chunk) {
    this.group.remove(chunk.mesh);
    this.retired.push(chunk);
  }
  get stats() {
    let triangles = 0, nearby = 0, earthworksPending = 0, trees = 0;
    for (const [key, chunk] of this.chunks) {
      triangles += chunk.mesh.geometry.index!.count / 3;
      trees += chunk.trees?.count ?? 0;
      if (chunk.dirty) earthworksPending++;
      const target = this.wanted.get(key);
      if (target && target.ring <= 1 && chunk.segments === target.segments) nearby++;
    }
    return { chunks: this.chunks.size, total: this.wanted.size, triangles, nearby, earthworksPending, trees, radius: VIEW_RADIUS * CHUNK_SIZE,
      cachedLods: this.cache.count, cacheBytes: this.cache.bytes, cacheHits: this.cache.hits,
      cachedTreeBytes: this.treeStreaming.cachedBytes, treeUploads: this.treeStreaming.uploads, treeReuses: this.treeStreaming.reuses,
      streamingPending: this.completed.length + this.retired.length + this.treeStreaming.pendingDisposals };
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
    this.chunks.forEach(chunk => this.releaseChunk(chunk));
    this.cache.clear();
    this.treeStreaming.dispose();
    for (const chunk of this.retired) chunk.mesh.geometry.dispose();
    this.retired = []; this.completed = [];
    this.trees.dispose();
    this.chunks.clear();
    this.wanted.clear();
    this.group.clear();
    this.material.dispose();
    this.water.geometry.dispose();
    this.water.material.dispose();
  }
}
