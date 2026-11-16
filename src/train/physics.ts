import type { VehicleDefinition } from './catalog';
export type PhysicsMode = 'realistic' | 'ho';
export const GRAVITY = 9.80665;
// Modified Davis linear term: 0.01 lbf / (US ton · mph), converted to N / (kg · m/s).
// FRA passenger braking report, eq. A14: see docs/train-physics.md.
const LINEAR_RESISTANCE_SI = 0.01 * 4.4482216152605 / (907.18474 * 0.44704);
/** No superelevation: v²/r is the lateral acceleration. HO uses the same 9× response allowance. */
export function curveSpeedLimit(radius: number, mode: PhysicsMode) { return Math.sqrt(Math.max(0, radius) * 0.65 * (mode === 'ho' ? 9 : 1)); }
export function trainPerformance(definitions: readonly VehicleDefinition[], mode: PhysicsMode) {
  const mass = definitions.reduce((sum, d) => sum + d.physics.massKg, 0);
  const powered = definitions.filter(d => d.physics.powerW > 0);
  const realMax = Math.min(...definitions.map(d => d.physics.maxSpeedMps));
  return { mass, power: powered.reduce((sum, d) => sum + d.physics.powerW, 0) * 0.82,
    traction: powered.reduce((sum, d) => sum + Math.min(d.physics.tractiveEffortN, d.physics.massKg * GRAVITY * 0.25), 0),
    maxSpeed: powered.length ? realMax * (mode === 'ho' ? (120 / 3.6) / 45.599 : 1) : 0,
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
    // Ideal regulated motors compensate for grade/resistance without a power or adhesion ceiling.
    // Nominal locomotive traction only sets the finite response rate; heavier consists respond slower.
    const target = powered ? Math.max(0, Math.min(p.maxSpeed, speedLimit)) : 0;
    const acceleration = p.traction / (p.mass * 1.06) * p.response;
    const braking = brakingDeceleration(grade, mode);
    let next = v + Math.max(-braking * dt, Math.min(acceleration * dt, target - v));
    if (target === 0 && next < 0.001) next = 0;
    return { speed: next, distance: (v + next) / 2 * dt, effort: effort(next, target > .001 && v <= target + .001) };
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
export function brakingDeceleration(grade: number, mode: PhysicsMode) { return mode === 'ho' ? 0.65 / 1.06 * 9 : Math.max(0.05, (0.65 + GRAVITY * grade) / 1.06); }
