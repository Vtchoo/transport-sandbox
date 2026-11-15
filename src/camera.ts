import * as THREE from 'three';

export type CameraMode = 'overlook' | 'free';
type Ground = (x: number, z: number) => number;
const MIN_PITCH = -Math.PI / 2 + 0.08;
const MAX_PITCH = -0.25;

/** Absolute double-precision world coordinates; rendering applies the floating origin. */
export class CameraRig {
  mode: CameraMode = 'overlook';
  readonly position = new THREE.Vector3();
  readonly target = new THREE.Vector3();
  yaw = -0.5;
  pitch = -0.72;
  distance = 1100;
  speed = 60;
  private direction = new THREE.Vector3();
  private movement = new THREE.Vector3();

  reset(x: number, z: number, ground: Ground) {
    this.yaw = -0.5;
    this.pitch = -0.72;
    this.distance = 1100;
    this.target.set(x, ground(x, z), z);
    this.orbitPosition(ground);
    if (this.mode === 'free') this.pitch = -0.15;
  }
  private forward() {
    return this.direction.set(-Math.sin(this.yaw) * Math.cos(this.pitch), Math.sin(this.pitch), -Math.cos(this.yaw) * Math.cos(this.pitch));
  }
  private orbitPosition(ground: Ground) {
    this.position.copy(this.target).addScaledVector(this.forward(), -this.distance);
    const floor = ground(this.position.x, this.position.z) + 12;
    if (this.position.y < floor) {
      // Lift the whole orbit, keeping its pitch and distance consistent on steep slopes.
      this.target.y += floor - this.position.y;
      this.position.y = floor;
    }
  }
  setMode(mode: CameraMode, ground: Ground) {
    if (mode === this.mode) return;
    this.mode = mode;
    if (mode === 'free') return; // Preserve the current overlook pose exactly.
    this.pitch = THREE.MathUtils.clamp(this.pitch, MIN_PITCH, MAX_PITCH);
    const direction = this.forward();
    // Pick the visible terrain along the viewing ray; binary search the first crossing.
    let previous = 0;
    let hit = 0;
    for (let t = 20; t <= 3500; t += 20) {
      const x = this.position.x + direction.x * t, z = this.position.z + direction.z * t;
      if (this.position.y + direction.y * t <= ground(x, z)) {
        let low = previous, high = t;
        for (let i = 0; i < 18; i++) {
          const mid = (low + high) / 2;
          const h = ground(this.position.x + direction.x * mid, this.position.z + direction.z * mid);
          if (this.position.y + direction.y * mid > h) low = mid; else high = mid;
        }
        hit = (low + high) / 2;
        break;
      }
      previous = t;
    }
    this.distance = THREE.MathUtils.clamp(hit || 1800, 50, 3500);
    this.target.copy(this.position).addScaledVector(direction, this.distance);
    this.orbitPosition(ground);
  }
  look(dx: number, dy: number, ground: Ground) {
    this.yaw -= dx * 0.003;
    this.pitch = THREE.MathUtils.clamp(this.pitch - dy * 0.003,
      this.mode === 'overlook' ? MIN_PITCH : -Math.PI / 2 + 0.02,
      this.mode === 'overlook' ? MAX_PITCH : Math.PI / 2 - 0.02);
    if (this.mode === 'overlook') this.orbitPosition(ground);
  }
  pan(dx: number, dy: number, ground: Ground) {
    const scale = this.distance * 0.0012;
    this.target.x += (-dx * Math.cos(this.yaw) - dy * Math.sin(this.yaw)) * scale;
    this.target.z += (dx * Math.sin(this.yaw) - dy * Math.cos(this.yaw)) * scale;
    this.target.y = ground(this.target.x, this.target.z);
    this.orbitPosition(ground);
  }
  scroll(delta: number, ground: Ground) {
    const amount = THREE.MathUtils.clamp(delta, -200, 200);
    if (this.mode === 'free') this.speed = THREE.MathUtils.clamp(this.speed * Math.exp(-amount * 0.002), 5, 500);
    else {
      this.distance = THREE.MathUtils.clamp(this.distance * Math.exp(amount * 0.0015), 50, 3500);
      this.orbitPosition(ground);
    }
  }
  update(keys: Set<string>, delta: number, ground: Ground, loaded: (x: number, z: number) => boolean) {
    const forward = Number(keys.has('KeyW') || keys.has('ArrowUp')) - Number(keys.has('KeyS') || keys.has('ArrowDown'));
    const right = Number(keys.has('KeyD') || keys.has('ArrowRight')) - Number(keys.has('KeyA') || keys.has('ArrowLeft'));
    const vertical = Number(keys.has('KeyE') || keys.has('Space')) - Number(keys.has('KeyQ'));
    const fast = keys.has('ShiftLeft') || keys.has('ShiftRight');
    if (this.mode === 'overlook') this.yaw += (Number(keys.has('KeyQ')) - Number(keys.has('KeyE'))) * delta;
    this.movement.set(right * Math.cos(this.yaw) - forward * Math.sin(this.yaw), this.mode === 'free' ? vertical : 0,
      -right * Math.sin(this.yaw) - forward * Math.cos(this.yaw));
    if (this.movement.lengthSq() > 0) {
      const speed = this.mode === 'free' ? this.speed : Math.max(30, this.distance * 0.65);
      this.movement.normalize().multiplyScalar(speed * delta * (fast ? (this.mode === 'free' ? 5 : 2.5) : 1));
      if (loaded(this.position.x + this.movement.x, this.position.z + this.movement.z)) {
        if (this.mode === 'free') this.position.add(this.movement);
        else this.target.add(this.movement);
      }
    }
    if (this.mode === 'overlook') {
      const height = ground(this.target.x, this.target.z);
      this.target.y = THREE.MathUtils.lerp(this.target.y, height, 1 - Math.exp(-delta * 7));
      this.orbitPosition(ground);
    } else this.position.y = Math.max(this.position.y, ground(this.position.x, this.position.z) + 8);
  }
}
