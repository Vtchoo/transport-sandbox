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
  deleteNode(id: number) {
    const node = this.nodes.get(id);
    if (!node) return;
    for (const edge of this.incident(id)) this.segments.delete(edge.id);
    const group = this.parallelGroup(node);
    if (group) {
      group.offsets.delete(id);
      if (group.offsets.size < 2) {
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
