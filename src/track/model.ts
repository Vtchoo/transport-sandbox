import * as THREE from 'three';

export const GAUGES = [
  { mm: 1435, label: 'Standard — 1,435 mm' },
  { mm: 1000, label: 'Metre — 1,000 mm' },
  { mm: 1067, label: 'Cape — 1,067 mm' },
  { mm: 1520, label: 'Broad — 1,520 mm' },
  { mm: 1524, label: 'Broad — 1,524 mm' },
  { mm: 1600, label: 'Broad — 1,600 mm' },
  { mm: 1668, label: 'Iberian — 1,668 mm' },
  { mm: 1676, label: 'Indian broad — 1,676 mm' },
  { mm: 762, label: 'Narrow — 762 mm' },
  { mm: 610, label: 'Narrow — 610 mm' },
];
export type Side = 1 | -1;
export interface TrackNode { id: number; position: THREE.Vector3; axis: THREE.Vector3; gauge: number; parallelGroup?: number }
export interface ParallelGroup { id: number; anchor: number; origin: THREE.Vector3; axis: THREE.Vector3; offsets: Map<number, number> }
export interface TrackSegment { id: number; a: number; b: number; gauge: number; aSide: Side; bSide: Side; aLength: number; bLength: number }
export class TrackNetwork {
  nodes = new Map<number, TrackNode>();
  segments = new Map<number, TrackSegment>();
  parallelGroups = new Map<number, ParallelGroup>();
  private nextNode = 1;
  private nextSegment = 1;
  private nextGroup = 1;
  addNode(position: THREE.Vector3, gauge: number) {
    if (!GAUGES.some(item => item.mm === gauge)) throw new Error('Choose a supported gauge.');
    const node: TrackNode = { id: this.nextNode++, position: position.clone(), axis: new THREE.Vector3(0, 0, 1), gauge };
    this.nodes.set(node.id, node);
    return node;
  }
  incident(node: number) { return [...this.segments.values()].filter(edge => edge.a === node || edge.b === node); }
  deleteNode(id: number, keepParallelGroup = false) {
    const node = this.nodes.get(id);
    if (!node) return;
    for (const edge of this.incident(id)) this.segments.delete(edge.id);
    const group = this.parallelGroup(node);
    if (group) {
      group.offsets.delete(id);
      if (group.offsets.size === 0 || group.offsets.size < 2 && !keepParallelGroup) {
        for (const member of group.offsets.keys()) this.nodes.get(member)!.parallelGroup = undefined;
        this.parallelGroups.delete(group.id);
      } else if (group.anchor === id) {
        const [anchor, offset] = group.offsets.entries().next().value!;
        group.anchor = anchor; group.origin.copy(this.nodes.get(anchor)!.position);
        for (const [member, value] of group.offsets) group.offsets.set(member, value - offset);
      }
    }
    this.nodes.delete(id);
  }
  private mergeParts(id: number) {
    const node = this.nodes.get(id), edges = this.incident(id);
    if (!node || edges.length !== 2) return;
    const [first, second] = edges;
    const firstSide = first.a === id ? first.aSide : first.bSide;
    const secondSide = second.a === id ? second.aSide : second.bSide;
    if (firstSide === secondSide) return;
    const a = first.a === id ? first.b : first.a, b = second.a === id ? second.b : second.a;
    if (a === b || first.gauge !== second.gauge || first.gauge !== node.gauge) return;
    if (this.nodes.get(a)!.position.distanceTo(this.nodes.get(b)!.position) < 1) return;
    if ([...this.segments.values()].some(edge => edge.a === a && edge.b === b || edge.a === b && edge.b === a)) return;
    return { first, second, a, b };
  }
  canMerge(id: number) { return this.mergeParts(id) !== undefined; }
  merge(id: number) {
    const parts = this.mergeParts(id);
    if (!parts) throw new Error('Merge requires exactly two compatible tracks on opposite sides, with distinct endpoints that are not already connected.');
    const { first, second, a, b } = parts;
    const edge: TrackSegment = {
      id: this.nextSegment++, a, b, gauge: first.gauge,
      aSide: first.a === a ? first.aSide : first.bSide,
      aLength: first.a === a ? first.aLength : first.bLength,
      bSide: second.a === b ? second.aSide : second.bSide,
      bLength: second.a === b ? second.aLength : second.bLength,
    };
    // Retain the remaining parallel members, rebasing the anchor if necessary.
    this.deleteNode(id, true); this.segments.set(edge.id, edge);
    return edge;
  }
  directionLocked(node: TrackNode) { return node.parallelGroup !== undefined || this.incident(node.id).length > 0; }
  defaultSpacing(gauge: number) { return gauge / 1000 + 2.6; }
  parallelGroup(node: TrackNode) { return node.parallelGroup === undefined ? undefined : this.parallelGroups.get(node.parallelGroup); }
  addParallel(source: TrackNode, side: Side) {
    let group = this.parallelGroup(source);
    if (!group) {
      group = { id: this.nextGroup++, anchor: source.id, origin: source.position.clone(), axis: source.axis.clone(), offsets: new Map([[source.id, 0]]) };
      this.parallelGroups.set(group.id, group); source.parallelGroup = group.id;
    }
    const spacing = this.defaultSpacing(source.gauge);
    let offset = group.offsets.get(source.id)! + side * spacing;
    while ([...group.offsets.values()].some(value => Math.abs(value - offset) < 0.05)) offset += side * spacing;
    const node = this.addNode(group.origin.clone().addScaledVector(trackFrame(group.axis).right, offset), source.gauge);
    node.parallelGroup = group.id; node.axis.copy(group.axis); group.offsets.set(node.id, offset);
    return node;
  }
  setDirection(node: TrackNode, direction: THREE.Vector3) {
    const group = this.parallelGroup(node);
    if (!group) { node.axis.copy(direction).normalize(); return; }
    group.axis.copy(direction).normalize();
    const right = trackFrame(group.axis).right;
    // Keep the edited node fixed and rotate its parallel neighbours around it.
    group.origin.copy(node.position).addScaledVector(right, -group.offsets.get(node.id)!);
    for (const [id, offset] of group.offsets) {
      const member = this.nodes.get(id)!;
      member.axis.copy(group.axis); member.position.copy(group.origin).addScaledVector(right, offset);
    }
  }
  moveNode(node: TrackNode, position: THREE.Vector3) {
    const group = this.parallelGroup(node);
    if (!group) { node.position.copy(position); return; }
    const delta = position.clone().sub(node.position);
    group.origin.add(delta);
    for (const id of group.offsets.keys()) this.nodes.get(id)!.position.add(delta);
  }
  setParallelOffset(node: TrackNode, offset: number) {
    const group = this.parallelGroup(node);
    if (!group || node.id === group.anchor || !Number.isFinite(offset)) return;
    group.offsets.set(node.id, offset);
    node.position.copy(group.origin).addScaledVector(trackFrame(group.axis).right, offset);
  }
  connect(a: TrackNode, b: TrackNode, gauge: number, aSide?: Side) {
    if (a.id === b.id) throw new Error('Choose a different end node.');
    if (a.gauge !== gauge || b.gauge !== gauge) throw new Error(`Gauge mismatch: both nodes must use ${gauge.toLocaleString()} mm.`);
    if ([...this.segments.values()].some(edge => edge.a === a.id && edge.b === b.id || edge.a === b.id && edge.b === a.id)) throw new Error('These nodes are already connected.');
    const chord = b.position.clone().sub(a.position);
    if (chord.length() < 1) throw new Error('Nodes must be at least 1 m apart.');
    const length = chord.length() / 3;
    if (!this.directionLocked(a)) a.axis.copy(chord).normalize().multiplyScalar(aSide ?? 1);
    if (!this.directionLocked(b)) b.axis.copy(chord).normalize();
    const segment: TrackSegment = { id: this.nextSegment++, a: a.id, b: b.id, gauge,
      aSide: aSide ?? (a.axis.dot(chord) >= 0 ? 1 : -1),
      bSide: b.axis.dot(chord) >= 0 ? -1 : 1, aLength: length, bLength: length };
    this.segments.set(segment.id, segment);
    return segment;
  }
  handle(edge: TrackSegment, nodeId: number) {
    const node = this.nodes.get(nodeId)!;
    return node.position.clone().addScaledVector(node.axis, nodeId === edge.a ? edge.aSide * edge.aLength : edge.bSide * edge.bLength);
  }
  curve(edge: TrackSegment) {
    return new THREE.CubicBezierCurve3(this.nodes.get(edge.a)!.position, this.handle(edge, edge.a), this.handle(edge, edge.b), this.nodes.get(edge.b)!.position);
  }
  split(edge: TrackSegment, t: number) {
    if (this.segments.get(edge.id) !== edge || !Number.isFinite(t) || t <= 0 || t >= 1) throw new Error('Choose a point inside an existing segment.');
    const curve = this.curve(edge);
    // De Casteljau subdivision preserves both cubic curves exactly.
    const q0 = curve.v0.clone().lerp(curve.v1, t), q1 = curve.v1.clone().lerp(curve.v2, t), q2 = curve.v2.clone().lerp(curve.v3, t);
    const r0 = q0.clone().lerp(q1, t), r1 = q1.clone().lerp(q2, t);
    const position = r0.clone().lerp(r1, t), direction = r1.clone().sub(r0);
    if (position.distanceTo(curve.v0) < 1 || position.distanceTo(curve.v3) < 1) throw new Error('Split at least 1 m from either endpoint.');
    if (direction.lengthSq() < 1e-12) throw new Error('Cannot split at a point with no track direction.');
    const node = this.addNode(position, edge.gauge); node.axis.copy(direction).normalize();
    const first: TrackSegment = { ...edge, id: this.nextSegment++, b: node.id, bSide: -1, aLength: edge.aLength * t, bLength: position.distanceTo(r0) };
    const second: TrackSegment = { ...edge, id: this.nextSegment++, a: node.id, aSide: 1, aLength: position.distanceTo(r1), bLength: edge.bLength * (1 - t) };
    this.segments.delete(edge.id); this.segments.set(first.id, first); this.segments.set(second.id, second);
    return { node, first, second };
  }
  splitAndConnect(edge: TrackSegment, t: number, endpoint: TrackNode, side: Side) {
    if (this.nodes.get(endpoint.id) !== endpoint) throw new Error('Choose an existing end node.');
    const nextNode = this.nextNode, nextSegment = this.nextSegment, endpointAxis = endpoint.axis.clone();
    const split = this.split(edge, t);
    try {
      const segment = this.connect(split.node, endpoint, edge.gauge, side);
      return { ...split, segment };
    } catch (error) {
      // A rejected branch must leave the original segment completely intact.
      this.nodes.delete(split.node.id); this.segments.delete(split.first.id); this.segments.delete(split.second.id);
      this.segments.set(edge.id, edge); endpoint.axis.copy(endpointAxis);
      this.nextNode = nextNode; this.nextSegment = nextSegment;
      throw error;
    }
  }
  moveHandle(edge: TrackSegment, nodeId: number, position: THREE.Vector3) {
    const node = this.nodes.get(nodeId)!;
    const offset = position.clone().sub(node.position);
    const length = offset.length();
    if (length < 0.25) return;
    const side = nodeId === edge.a ? edge.aSide : edge.bSide;
    this.setDirection(node, offset.clone().normalize().multiplyScalar(side));
    if (nodeId === edge.a) edge.aLength = length; else edge.bLength = length;
  }
  straighten(edge: TrackSegment) {
    const a = this.nodes.get(edge.a)!, b = this.nodes.get(edge.b)!;
    const chord = b.position.clone().sub(a.position);
    const length = chord.length();
    if (length < 1) throw new Error('Nodes must be at least 1 m apart to straighten a segment.');
    if (a.parallelGroup !== undefined && a.parallelGroup === b.parallelGroup) throw new Error('Cannot straighten between nodes in the same parallel group without breaking their alignment.');
    chord.divideScalar(length);
    // Retain endpoint side assignments so all incident tracks share the updated axes.
    this.setDirection(a, chord.clone().multiplyScalar(edge.aSide));
    this.setDirection(b, chord.clone().multiplyScalar(-edge.bSide));
    edge.aLength = length / 3;
    edge.bLength = length / 3;
  }
  preview(a: TrackNode, endpoint: THREE.Vector3, side: Side, b?: TrackNode) {
    const chord = endpoint.clone().sub(a.position), length = chord.length() / 3;
    const first = this.directionLocked(a) ? a.position.clone().addScaledVector(a.axis, side * length) : a.position.clone().addScaledVector(chord, 1 / 3);
    const endSide = b && b.axis.dot(chord) >= 0 ? -1 : 1;
    const second = b && this.directionLocked(b) ? b.position.clone().addScaledVector(b.axis, endSide * length) : endpoint.clone().addScaledVector(chord, -1 / 3);
    return new THREE.CubicBezierCurve3(a.position, first, second, endpoint);
  }
}

/** The lateral axis is always horizontal: no cant or Frenet-frame roll. */
export function trackFrame(tangent: THREE.Vector3) {
  const forward = tangent.clone().normalize();
  if (forward.lengthSq() < 1e-12) forward.set(0, 0, 1);
  const right = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), forward);
  // At a vertical tangent, roll is undefined. Use a finite deterministic fallback.
  if (right.lengthSq() < 1e-12) right.set(1, 0, 0); else right.normalize();
  const up = new THREE.Vector3().crossVectors(forward, right).normalize();
  return { right, up, forward };
}
