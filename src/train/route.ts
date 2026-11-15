import * as THREE from 'three';
import type { Side, TrackNetwork, TrackSegment } from '../track/model';
import type { TrackPosition, TrackSampler } from './placement';

interface Leg { edge: number; facing: Side }
export interface RouteState { legs: [number, Leg][]; random: number }
export class RouteEnd extends Error {
  constructor(readonly distance: number) { super('End of connected track.'); }
}
/** An ordered itinerary, rather than a switch setting: even repeated visits to a loop are distinct. */
export class TrainRoute {
  private legs = new Map<number, Leg>();
  private random: number;
  private curves = new Map<number, { key: string; curve: THREE.CubicBezierCurve3 }>();
  constructor(private network: TrackNetwork, position: TrackPosition, seed: number, state?: RouteState) {
    this.random = state?.random ?? (seed >>> 0);
    this.legs = new Map(state?.legs.map(([i, leg]) => [i, { ...leg }]) ?? [[position.routeIndex ?? 0, { edge: position.edge, facing: position.facing }]]);
  }
  snapshot(): RouteState { return { legs: [...this.legs].map(([i, leg]) => [i, { ...leg }]), random: this.random }; }
  private curve(id: number) {
    const edge = this.network.segments.get(id);
    if (!edge) throw new Error('Track supporting this train was removed.');
    const a = this.network.nodes.get(edge.a)!, b = this.network.nodes.get(edge.b)!;
    const key = [edge.a, edge.b, ...a.position, ...b.position, ...a.axis, ...b.axis, edge.aSide, edge.bSide, edge.aLength, edge.bLength].join(',');
    let cached = this.curves.get(id);
    if (cached?.key !== key) { const curve = this.network.curve(edge); curve.arcLengthDivisions = 512; cached = { key, curve }; this.curves.set(id, cached); }
    if (cached!.curve.getLength() < 1e-6) throw new Error('Track has no usable length.');
    return cached!.curve;
  }
  readonly sample: TrackSampler = (position, distance) => {
    if (!Number.isFinite(distance) || !Number.isFinite(position.u) || position.u < 0 || position.u > 1) throw new Error('Invalid train route position.');
    let index = position.routeIndex ?? 0, leg = this.legs.get(index);
    if (!leg || leg.edge !== position.edge) throw new Error('Train route no longer matches its track.');
    const orientation = position.facing === leg.facing ? 1 : -1;
    const step = (distance < 0 ? -1 : 1) * orientation;
    let remaining = Math.abs(distance), u = position.u, travelled = 0;
    for (let hop = 0; hop < 4096; hop++) {
      const curve = this.curve(leg.edge), length = curve.getLength(); const direction: number = step * leg.facing;
      const available = length * (direction > 0 ? 1 - u : u);
      if (remaining <= available + 1e-8) {
        u = THREE.MathUtils.clamp(u + direction * remaining / length, 0, 1);
        const facing = leg.facing * orientation as Side;
        return { point: curve.getPointAt(u), tangent: curve.getTangentAt(u).multiplyScalar(facing), position: { edge: leg.edge, u, facing, routeIndex: index } };
      }
      remaining -= available; travelled += available;
      const edge: TrackSegment = this.network.segments.get(leg.edge)!; const node: number = direction > 0 ? edge.b : edge.a, side = direction > 0 ? edge.bSide : edge.aSide;
      const options: TrackSegment[] = this.network.incident(node).filter(next => next.id !== edge.id && next.gauge === edge.gauge && (next.a === node ? next.aSide : next.bSide) !== side).sort((a, b) => a.id - b.id);
      let next = this.legs.get(index + step);
      if (next) {
        const segment = options.find(edge => edge.id === next!.edge);
        if (!segment || (segment.a === node ? 1 : -1) !== next.facing * step) throw new Error('Track changed along the train route.');
      } else {
        if (!options.length) throw new RouteEnd(travelled);
        this.random = (Math.imul(this.random, 1664525) + 1013904223) >>> 0;
        const chosen = options[Math.floor(this.random / 4294967296 * options.length)];
        next = { edge: chosen.id, facing: ((chosen.a === node ? 1 : -1) * step) as Side }; this.legs.set(index + step, next);
      }
      index += step; leg = next; u = step * leg.facing > 0 ? 0 : 1;
    }
    throw new Error('Train route exceeded the traversal limit.');
  };
  clearance(position: TrackPosition, distance: number) {
    try { this.sample(position, distance); return Math.abs(distance); }
    catch (error) { if (error instanceof RouteEnd) return error.distance; throw error; }
  }
  turnRadius(position: TrackPosition) {
    const center = this.sample(position, 0);
    const nearby = (d: number) => { try { return this.sample(position, d); } catch (error) { if (error instanceof RouteEnd) return center; throw error; } };
    const a = nearby(-2), b = nearby(2), ta = a.tangent.clone().setY(0).normalize(), tb = b.tangent.clone().setY(0).normalize();
    const angle = Math.acos(THREE.MathUtils.clamp(ta.dot(tb), -1, 1));
    return angle < 1e-6 ? Infinity : Math.hypot(b.point.x - a.point.x, b.point.z - a.point.z) / (2 * Math.sin(angle / 2));
  }
  /** Keep the occupied route and nearby decisions; discard distant history and its geometry cache. */
  trim(position: TrackPosition, metres: number) {
    const center = position.routeIndex ?? 0;
    for (const direction of [-1, 1]) {
      let distance = 0;
      for (let i = center + direction; this.legs.has(i); i += direction) {
        distance += this.curve(this.legs.get(i)!.edge).getLength();
        if (distance > metres) this.legs.delete(i);
      }
    }
    const used = new Set([...this.legs.values()].map(leg => leg.edge));
    for (const id of this.curves.keys()) if (!used.has(id)) this.curves.delete(id);
  }
}
