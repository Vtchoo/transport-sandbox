import * as THREE from "three";
import type { VehicleVisual } from "./render";

interface Particle {
  position: THREE.Vector3;
  velocity: THREE.Vector3;
  age: number;
  life: number;
  radius: number;
  density: number;
  phase: number;
  distance: number;
  shade: number;
  growth: number;
}
interface Emitter {
  position: THREE.Vector3;
  carry: number;
  effort: number;
}
export interface SmokeSource {
  visual: VehicleVisual;
  effort: number;
}

/** One bounded point-sprite batch for idle and working exhaust; positions remain in world space. */
export class TrainSmoke {
  readonly points: THREE.Points<THREE.BufferGeometry, THREE.ShaderMaterial>;
  private particles: Particle[] = [];
  private pool: Particle[] = [];
  private emitters = new Map<VehicleVisual, Emitter>();
  private positions: Float32Array;
  private sizes: Float32Array;
  private appearances: Float32Array;
  private sequence = 1;
  private viewport = new THREE.Vector4();
  private disposed = false;

  constructor(readonly capacity = 2048) {
    this.positions = new Float32Array(capacity * 3);
    this.sizes = new Float32Array(capacity);
    this.appearances = new Float32Array(capacity * 3);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute(
      "position",
      new THREE.BufferAttribute(this.positions, 3).setUsage(
        THREE.DynamicDrawUsage,
      ),
    );
    geometry.setAttribute(
      "size",
      new THREE.BufferAttribute(this.sizes, 1).setUsage(THREE.DynamicDrawUsage),
    );
    geometry.setAttribute(
      "appearance",
      new THREE.BufferAttribute(this.appearances, 3).setUsage(
        THREE.DynamicDrawUsage,
      ),
    );
    geometry.setDrawRange(0, 0);
    const material = new THREE.ShaderMaterial({
      transparent: true,
      depthWrite: false,
      fog: true,
      uniforms: THREE.UniformsUtils.merge([
        THREE.UniformsLib.fog,
        { viewportHeight: { value: 900 } },
      ]),
      vertexShader: `
        uniform float viewportHeight;
        attribute float size; attribute vec3 appearance; varying vec3 vAppearance;
        #include <fog_pars_vertex>
        void main() {
          vAppearance = appearance;
          vec4 mvPosition = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mvPosition;
          gl_PointSize = min(128.0, size * viewportHeight * projectionMatrix[1][1] / max(0.1, -mvPosition.z));
          #include <fog_vertex>
        }`,
      fragmentShader: `
        varying vec3 vAppearance;
        #include <fog_pars_fragment>
        void main() {
          vec2 p = (gl_PointCoord - 0.5) * 2.0;
          float angle = atan(p.y, p.x);
          float edge = length(p) * (1.0 + 0.10 * sin(angle * 5.0 + vAppearance.z) + 0.07 * sin(angle * 3.0 - vAppearance.z));
          float alpha = (1.0 - smoothstep(0.12, 1.0, edge)) * vAppearance.x;
          if (alpha < 0.003) discard;
          gl_FragColor = vec4(vec3(vAppearance.y), alpha);
          #include <tonemapping_fragment>
          #include <colorspace_fragment>
          #include <fog_fragment>
        }`,
    });
    this.points = new THREE.Points(geometry, material);
    this.points.name = "Locomotive exhaust";
    // Vertex positions are rebased each update; a batch may span multiple separated trains.
    this.points.frustumCulled = false;
    this.points.visible = false;
    this.points.onBeforeRender = (renderer) => {
      renderer.getCurrentViewport(this.viewport);
      material.uniforms.viewportHeight.value = this.viewport.w;
    };
  }

  get count() {
    return this.particles.length;
  }
  private random() {
    this.sequence = (Math.imul(this.sequence, 1664525) + 1013904223) >>> 0;
    return this.sequence / 4294967296;
  }
  update(
    delta: number,
    sources: readonly SmokeSource[],
    origin: THREE.Vector2,
    camera: THREE.Camera,
  ) {
    if (this.disposed) return;
    const dt = Math.max(0, Math.min(delta, 0.1));
    const eye = camera
      .getWorldPosition(new THREE.Vector3())
      .add(new THREE.Vector3(origin.x, 0, origin.y));
    for (let i = this.particles.length - 1; i >= 0; i--) {
      const particle = this.particles[i];
      particle.age += dt;
      if (particle.age >= particle.life) {
        this.pool.push(particle);
        this.particles.splice(i, 1);
        continue;
      }
      particle.velocity.x += (0.65 - particle.velocity.x) * dt * 0.6;
      particle.velocity.z += (0.25 - particle.velocity.z) * dt * 0.6;
      particle.velocity.y += (1.8 - particle.velocity.y) * dt * 0.4;
      particle.position.addScaledVector(particle.velocity, dt);
    }
    const seen = new Set<VehicleVisual>();
    const emit = (visual: VehicleVisual, effort: number) => {
      if (visual.sections) {
        visual.sections.forEach((section) => emit(section, effort));
        return;
      }
      const outlet = visual.definition.exhaust;
      if (!outlet) return;
      const steam = (outlet.kind ?? (visual.definition.runningGear === 'steam' ? 'steam' : 'diesel')) === 'steam';
      const idleRate = Math.max(0, outlet.idleRate ?? (steam ? 2.2 : 1.2));
      seen.add(visual);
      const position = visual.body.localToWorld(
        new THREE.Vector3(...outlet.position),
      );
      position.x += origin.x;
      position.z += origin.y;
      let emitter = this.emitters.get(visual);
      if (!emitter) {
        emitter = { position: position.clone(), carry: 0, effort: 0 };
        this.emitters.set(visual, emitter);
      }
      emitter.effort +=
        (THREE.MathUtils.clamp(effort, 0, 1) - emitter.effort) *
        (1 - Math.exp(-dt * 10));
      const velocity = position
        .clone()
        .sub(emitter.position)
        .divideScalar(Math.max(dt, 0.001));
      if (velocity.lengthSq() > 80 ** 2) velocity.set(0, 0, 0);
      if (
        position.distanceToSquared(eye) < 2500 ** 2
      ) {
        emitter.carry += dt * (idleRate + emitter.effort * (steam ? 31 : 18));
        const amount = Math.floor(emitter.carry);
        emitter.carry -= amount;
        for (
          let i = 0;
          i < amount && this.particles.length < this.capacity;
          i++
        ) {
          const particle = this.pool.pop() ?? {
            position: new THREE.Vector3(),
            velocity: new THREE.Vector3(),
            age: 0,
            life: 0,
            radius: 0,
            density: 0,
            phase: 0,
            distance: 0,
            shade: 0,
            growth: 0,
          };
          particle.position
            .copy(emitter.position)
            .lerp(position, (i + 1) / amount);
          particle.position.x += (this.random() - 0.5) * 0.14;
          particle.position.z += (this.random() - 0.5) * 0.14;
          particle.velocity.copy(velocity).multiplyScalar(0.12);
          particle.velocity.x += (this.random() - 0.5) * 0.5;
          particle.velocity.y += (steam ? 1.4 : 1) + emitter.effort * (steam ? 3 : 1.8) + this.random() * .5;
          particle.velocity.z += (this.random() - 0.5) * 0.5;
          particle.age = 0;
          particle.life = 2.4 + emitter.effort * .7 + this.random() * 1.2;
          particle.radius = 0.12 + emitter.effort * .09 + this.random() * .10;
          particle.density = (steam ? .10 : .065) + emitter.effort * (steam ? .44 : .24);
          particle.shade = (steam ? .43 : .32) - emitter.effort * (steam ? .28 : .14);
          particle.growth = (steam ? .28 : .18) + emitter.effort * .3;
          particle.phase = this.random() * Math.PI * 2;
          this.particles.push(particle);
        }
      } else emitter.carry = 0;
      emitter.position.copy(position);
    };
    sources.forEach((source) => emit(source.visual, source.effort));
    for (const visual of this.emitters.keys())
      if (!seen.has(visual)) this.emitters.delete(visual);
    // Back-to-front ordering gives consistent transparency within the single draw call.
    this.particles.forEach(
      (particle) =>
        (particle.distance = particle.position.distanceToSquared(eye)),
    );
    this.particles.sort((a, b) => b.distance - a.distance);
    this.particles.forEach((particle, i) => {
      const age = particle.age / particle.life;
      this.positions[i * 3] = particle.position.x - origin.x;
      this.positions[i * 3 + 1] = particle.position.y;
      this.positions[i * 3 + 2] = particle.position.z - origin.y;
      this.sizes[i] = particle.radius + particle.age * particle.growth;
      this.appearances[i * 3] =
        particle.density * Math.min(1, particle.age / 0.1) * (1 - age) ** 1.6;
      this.appearances[i * 3 + 1] = particle.shade + age * 0.25;
      this.appearances[i * 3 + 2] = particle.phase + particle.age * 0.3;
    });
    this.points.geometry.setDrawRange(0, this.count);
    for (const attribute of Object.values(this.points.geometry.attributes))
      attribute.needsUpdate = true;
    this.points.visible = this.count > 0;
  }
  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.points.removeFromParent();
    this.points.geometry.dispose();
    this.points.material.dispose();
    this.emitters.clear();
    this.particles.length = 0;
    this.pool.length = 0;
  }
}
