import type { VehicleDefinition } from './catalog';
export type PhysicsMode = 'realistic' | 'ho';
export const GRAVITY = 9.80665;
// Adapted from OpenLoco Vehicle2::update. Copyright (c) 2018 OpenLoco developers.
// MIT license: godot/third-party/OpenLoco-LICENSE.txt. Continuous grade replaces pitch tables.
// All state and coefficients use SI: kg, W, m, s. Converted once from the source controller.
export const HO_MINIMUM_SPEED = 2.2352; // m/s (8.04672 km/h)
const HO_MAX_ACCELERATION = 7.210322580645161; // m/s^2
const HO_POWER_FACTOR = .6043251158594368; // acceleration per W/kg
const HO_GRADE_FACTOR = .00005633064516129032; // acceleration per kg * slope sine
const HO_DRAG_FACTOR = .001101062549248678; // acceleration per (m/s)^2
const HO_DRAG_SPEED_STEP = .44704; // m/s; source drag lookup resolution
const HO_BRAKE_CONSTANT = 2.640564335483871; // m/s^2
const HO_BRAKE_LINEAR = .5040322580645161; // 1/s
// Modified Davis linear term converted to N / (kg * m/s); docs/train-physics.md.
const LINEAR_RESISTANCE_SI = .0001096842564423765;
/** No superelevation: v²/r is the lateral acceleration. HO uses the same 9× response allowance. */
export function curveSpeedLimit(radius: number, mode: PhysicsMode) { return Math.sqrt(Math.max(0, radius) * 0.65 * (mode === 'ho' ? 9 : 1)); }
export function trainPerformance(definitions: readonly VehicleDefinition[], mode: PhysicsMode) {
  const mass = definitions.reduce((sum, d) => sum + d.physics.massKg, 0);
  const powered = definitions.filter(d => d.physics.powerW > 0);
  const realMax = Math.min(...definitions.map(d => d.physics.maxSpeedMps));
  return { mass, power: powered.reduce((sum, d) => sum + d.physics.powerW, 0) * (mode === 'ho' ? 1 : .82),
    traction: powered.reduce((sum, d) => sum + Math.min(d.physics.tractiveEffortN, d.physics.massKg * GRAVITY * 0.25), 0),
    maxSpeed: powered.length ? realMax : 0,
    response: mode === 'ho' ? 9 : 1,
    brakeForce: mass * 0.65 };
}
/** SI forces. Grade is mass-weighted sin(slope), positive uphill in the direction of travel. */
export function physicsStep(definitions: readonly VehicleDefinition[], speed: number, powered: boolean, grade: number, mode: PhysicsMode, dt: number, speedLimit = Infinity) {
  const p = trainPerformance(definitions, mode), v = Math.abs(speed);
  if (!p.mass || dt <= 0) return { speed: v, distance: 0, effort: 0 };
  const resistance = p.mass * GRAVITY * 0.0015 + p.mass * LINEAR_RESISTANCE_SI * v + 4 * v ** 2;
  const gravity = p.mass * GRAVITY * grade;
  // Exhaust uses delivered positive traction, including the load held at governed speed.
  const effort = (next: number, pulling: boolean) => {
    if (!pulling || !p.traction || !p.power) return 0;
    const force = (next - v) / dt / p.response * p.mass * 1.06 + resistance + gravity;
    const nominal = Math.min(p.traction, p.power / Math.max(v, .5));
    return Math.max(0, Math.min(1, force / nominal));
  };
  if (mode === 'ho') {
    const target = powered ? Math.max(0, Math.min(p.maxSpeed, speedLimit)) : 0;
    let next: number;
    if (v > target) next = Math.max(target, v - (HO_BRAKE_LINEAR * v + HO_BRAKE_CONSTANT) * dt);
    else if (target <= 0) next = 0;
    else {
      const drive = HO_POWER_FACTOR * p.power / p.mass;
      const dragSpeed = Math.floor(v / HO_DRAG_SPEED_STEP) * HO_DRAG_SPEED_STEP;
      const acceleration = Math.min(HO_MAX_ACCELERATION, drive - HO_GRADE_FACTOR * p.mass * grade - HO_DRAG_FACTOR * dragSpeed ** 2);
      const floor = Math.min(target, HO_MINIMUM_SPEED);
      const startAcceleration = Math.min(HO_MAX_ACCELERATION, drive);
      next = Math.min(target, v < floor ? v + Math.max(acceleration, startAcceleration) * dt : Math.max(v + acceleration * dt, floor));
    }
    if (target === 0 && next < .001) next = 0;
    const pulling = powered && target > .001 && v <= target + .001 && p.traction > 0 && p.power > 0;
    const load = Math.max(0, Math.min(1, (target - v) / Math.max(target, 1) + .1 + grade * 10));
    return { speed: next, distance: (v + next) / 2 * dt, effort: pulling ? load : 0 };
  }
  const equivalentSpeed = v;
  const limit = Math.min(p.maxSpeed, speedLimit), braking = !powered || v > limit;
  const drive = braking ? -p.brakeForce : Math.min(p.traction, p.power / Math.max(equivalentSpeed, 0.5));
  const acceleration = (drive - resistance - gravity) / (p.mass * 1.06) * p.response;
  let next = Math.max(0, v + acceleration * dt);
  // Speed governor holds the target with service brakes, including downhill sections.
  if (powered && v <= limit && next > limit) next = limit;
  if (!powered && next < 0.001) next = 0;
  return { speed: next, distance: (v + next) / 2 * dt, effort: effort(next, powered && limit > .001 && !braking) };
}
/** Conservative net service braking deceleration, allowing for downhill gravity. */
export function brakingDeceleration(grade: number, mode: PhysicsMode) { return mode === 'ho' ? HO_BRAKE_CONSTANT : Math.max(0.05, (0.65 + GRAVITY * grade) / 1.06); }
