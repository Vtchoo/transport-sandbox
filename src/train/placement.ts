import * as THREE from 'three';
import { trackFrame } from '../track/model';
import type { Side, TrackNetwork, TrackSegment } from '../track/model';
import { vehicleBogie } from './catalog';
import type { VehicleDefinition } from './catalog';
import type { VehicleVisual } from './render';

export interface TrackPosition { edge: number; u: number; facing: Side; routeIndex?: number }
export interface TrackSample { point: THREE.Vector3; tangent: THREE.Vector3; position: TrackPosition }
export type TrackSampler = (position: TrackPosition, distance: number) => TrackSample;
export interface VehiclePose {
  center: THREE.Vector3; front: THREE.Vector3; rear: THREE.Vector3; frontDirection: THREE.Vector3; rearDirection: THREE.Vector3;
  sections?: (facing: Side) => VehiclePose[];
  reversed?: boolean;
}

/** Walk along compatible opposite-side connections; a turnout needs an explicit route. */
export function sampleTrack(network: TrackNetwork, position: TrackPosition, distance: number, route?: TrackSampler): TrackSample {
  if (route) return route(position, distance);
  let edge = network.segments.get(position.edge);
  if (!edge || !Number.isFinite(position.u) || !Number.isFinite(distance) || position.u < 0 || position.u > 1) throw new Error('Choose an existing track segment.');
  const gauge = edge.gauge, sign = distance < 0 ? -1 : 1;
  let direction = sign * position.facing, remaining = Math.abs(distance), u = position.u;
  for (let hop = 0; hop < 256; hop++) {
    const curve = network.curve(edge); curve.arcLengthDivisions = 512;
    const length = curve.getLength(), available = length * (direction > 0 ? 1 - u : u);
    if (!Number.isFinite(length) || length < 1e-6) throw new Error('Track segment has no usable length.');
    if (remaining <= available + 1e-8) {
      u = THREE.MathUtils.clamp(u + direction * remaining / length, 0, 1);
      return { point: curve.getPointAt(u), tangent: curve.getTangentAt(u).multiplyScalar(direction * sign), position: { edge: edge.id, u, facing: direction * sign as Side } };
    }
    remaining -= available;
    const node = direction > 0 ? edge.b : edge.a;
    const side = direction > 0 ? edge.bSide : edge.aSide;
    const next: TrackSegment[] = network.incident(node).filter(candidate => candidate.id !== edge!.id && candidate.gauge === gauge && (candidate.a === node ? candidate.aSide : candidate.bSide) !== side);
    if (next.length === 0) throw new Error('Not enough connected track for the full vehicle.');
    if (next.length > 1) throw new Error('Place clear of a turnout with multiple possible routes.');
    edge = next[0]; direction = edge.a === node ? 1 : -1; u = direction > 0 ? 0 : 1;
  }
  throw new Error('Track route is too complex for placement.');
}

export function vehiclePose(network: TrackNetwork, definition: VehicleDefinition, position: TrackPosition, route?: TrackSampler): VehiclePose {
  const edge = network.segments.get(position.edge);
  if (!edge) throw new Error('Track supporting this vehicle was removed.');
  vehicleBogie(definition, edge.gauge);
  // Couplers and the entire vehicle must fit, not just its bogie centers.
  sampleTrack(network, position, definition.length / 2, route); sampleTrack(network, position, -definition.length / 2, route);
  const center = sampleTrack(network, position, 0, route).point;
  const front = sampleTrack(network, position, definition.bogieCenters / 2, route);
  const rear = sampleTrack(network, position, -definition.bogieCenters / 2, route);
  if (!definition.sections && front.point.distanceTo(rear.point) < definition.bogieCenters * 0.94) throw new Error('This curve is too tight for the vehicle’s bogie spacing.');
  const sections = definition.sections ? (facing: Side) => definition.sections!.map(section => {
    const at = sampleTrack(network, position, section.offset * facing, route).position;
    return vehiclePose(network, section.definition, { ...at, facing: at.facing * facing as Side }, route);
  }) : undefined;
  sections?.(1); // Validate each rigid section rather than treating the pair as one long body.
  return { center, front: front.point, rear: rear.point, frontDirection: front.tangent, rearDirection: rear.tangent, sections };
}

function orientation(direction: THREE.Vector3) {
  const frame = trackFrame(direction);
  return new THREE.Quaternion().setFromRotationMatrix(new THREE.Matrix4().makeBasis(frame.right, frame.up, frame.forward));
}
export function applyVehiclePose(visual: VehicleVisual, pose: VehiclePose) {
  visual.group.position.copy(pose.center);
  visual.body.position.copy(pose.front).add(pose.rear).multiplyScalar(0.5).sub(pose.center);
  visual.body.quaternion.copy(orientation(pose.front.clone().sub(pose.rear)));
  for (const [i, point, direction] of [[0, pose.front, pose.frontDirection], [1, pose.rear, pose.rearDirection]] as const) {
    visual.bogies[i].position.copy(point).sub(pose.center); visual.bogies[i].quaternion.copy(orientation(direction));
  }
  if (visual.sections) {
    const direction = pose.front.clone().sub(pose.rear).normalize();
    const poses = pose.sections?.(pose.reversed ? -1 : 1);
    visual.sections.forEach((section, i) => {
      const spec = visual.definition.sections![i];
      const center = pose.center.clone().addScaledVector(direction, spec.offset);
      applyVehiclePose(section, poses?.[i] ?? {
        center, front: center.clone().addScaledVector(direction, spec.definition.bogieCenters / 2),
        rear: center.clone().addScaledVector(direction, -spec.definition.bogieCenters / 2), frontDirection: direction, rearDirection: direction,
      });
      section.group.position.sub(pose.center);
    });
  }
}

/** Horizontal oriented footprints avoid rejecting trains on neighboring parallel tracks. */
export function vehiclesOverlap(a: VehicleVisual, b: VehicleVisual, margin = 0.08): boolean {
  if (a.sections || b.sections) return (a.sections ?? [a]).some(first => (b.sections ?? [b]).some(second => vehiclesOverlap(first, second, margin)));
  const ca = a.body.getWorldPosition(new THREE.Vector3()), cb = b.body.getWorldPosition(new THREE.Vector3());
  if (Math.abs(ca.y - cb.y) > Math.max(a.definition.height, b.definition.height)) return false;
  const axes = (visual: VehicleVisual) => {
    const forward = new THREE.Vector3(0, 0, 1).applyQuaternion(visual.body.getWorldQuaternion(new THREE.Quaternion())); forward.y = 0; forward.normalize();
    return [new THREE.Vector3(forward.z, 0, -forward.x), forward];
  };
  const aa = axes(a), bb = axes(b), delta = cb.sub(ca);
  for (const axis of [...aa, ...bb]) {
    const ra = Math.abs(aa[0].dot(axis)) * a.definition.width / 2 + Math.abs(aa[1].dot(axis)) * (a.definition.length / 2 + margin);
    const rb = Math.abs(bb[0].dot(axis)) * b.definition.width / 2 + Math.abs(bb[1].dot(axis)) * (b.definition.length / 2 + margin);
    if (Math.abs(delta.dot(axis)) >= ra + rb) return false;
  }
  return true;
}
