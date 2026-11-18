import * as THREE from 'three';
import { PLATFORM_WIDTH, platformEdgeOffset } from './platform-dimensions';

export const GAUGES = [
  { mm: 914, label: 'Three-foot narrow — 914 mm' },
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
export interface TrackStation { id: number; type: 'platform'; sides: Side[] }
export interface TrackSegment { id: number; a: number; b: number; gauge: number; aSide: Side; bSide: Side; aLength: number; bLength: number; station?: TrackStation }
export class TrackNetwork {
  nodes = new Map<number, TrackNode>();
  segments = new Map<number, TrackSegment>();
  parallelGroups = new Map<number, ParallelGroup>();
  private nextNode = 1;
  private nextSegment = 1;
  private nextGroup = 1;
  private nextStation = 1;
  canPlaceStation(edge: TrackSegment, side?: Side) {
    return this.segments.get(edge.id) === edge && (side === undefined ? (edge.station?.sides.length ?? 0) < 2 : !edge.station?.sides.includes(side)) &&
      this.incident(edge.a).length <= 2 && this.incident(edge.b).length <= 2;
  }
  addStation(edge: TrackSegment, side: Side) {
    if (this.segments.get(edge.id) !== edge) throw new Error('Choose an existing track segment.');
    if (edge.station?.sides.includes(side)) throw new Error('This side already has a platform.');
    if (!this.canPlaceStation(edge, side)) throw new Error('Platforms cannot be placed on junction segments. Split the track beyond the junction first.');
    if (edge.station) return edge.station = { ...edge.station, sides: [...edge.station.sides, side] };
    const neighbours = new Set<number>([edge.a, edge.b]);
    for (const endpoint of [edge.a, edge.b]) {
      const group = this.parallelGroup(this.nodes.get(endpoint)!);
      if (group) for (const id of group.offsets.keys()) neighbours.add(id);
    }
    const stations = new Set([...this.segments.values()].filter(other => other.station &&
      (neighbours.has(other.a) || neighbours.has(other.b))).map(other => other.station!.id));
    const id = stations.size ? Math.min(...stations) : this.nextStation++;
    // A new platform may bridge previously separate station groups.
    for (const other of this.segments.values()) if (other.station && stations.has(other.station.id)) {
      other.station = { ...other.station, id };
    }
    return edge.station = { id, type: 'platform', sides: [side] };
  }
  removePlatform(edge: TrackSegment, side: Side) {
    if (this.segments.get(edge.id) !== edge || !edge.station) return;
    const sides = edge.station.sides.filter(value => value !== side);
    edge.station = sides.length ? { ...edge.station, sides } : undefined;
  }
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
    // A single merged segment cannot represent two different station extents.
    if (!!first.station !== !!second.station) return;
    if (first.station && second.station && first.station.id !== second.station.id) return;
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
      station: first.station && { ...first.station, sides: [...new Set([
        ...first.station.sides.map(side => (side * (first.a === a ? 1 : -1)) as Side),
        ...second.station!.sides.map(side => (side * (second.b === b ? 1 : -1)) as Side),
      ])] },
    };
    // Retain the remaining parallel members, rebasing the anchor if necessary.
    this.deleteNode(id, true); this.segments.set(edge.id, edge);
    return edge;
  }
  directionLocked(node: TrackNode) { return node.parallelGroup !== undefined || this.incident(node.id).length > 0; }
  defaultSpacing(gauge: number) { return gauge / 1000 + 2.6; }
  canExtend(node: TrackNode, side: Side) {
    return !this.incident(node.id).some(edge => edge.station && (edge.a === node.id ? edge.aSide : edge.bSide) === side);
  }
  parallelSpacing(source: TrackNode, side: Side) {
    const hasPlatform = this.incident(source.id).some(edge => {
      // Segment sides follow A→B; parallel buttons follow the node's shared axis.
      const direction = edge.a === source.id ? edge.aSide : -edge.bSide;
      return edge.station?.sides.some(platformSide => platformSide * direction === side);
    });
    const normal = this.defaultSpacing(source.gauge);
    return hasPlatform ? Math.max(normal, 2 * platformEdgeOffset(source.gauge)) + 2 * PLATFORM_WIDTH : normal;
  }
  parallelGroup(node: TrackNode) { return node.parallelGroup === undefined ? undefined : this.parallelGroups.get(node.parallelGroup); }
  addParallel(source: TrackNode, side: Side) {
    let group = this.parallelGroup(source);
    if (!group) {
      group = { id: this.nextGroup++, anchor: source.id, origin: source.position.clone(), axis: source.axis.clone(), offsets: new Map([[source.id, 0]]) };
      this.parallelGroups.set(group.id, group); source.parallelGroup = group.id;
    }
    const spacing = this.parallelSpacing(source, side);
    let offset = group.offsets.get(source.id)! + side * spacing;
    while ([...group.offsets.values()].some(value => Math.abs(value - offset) < 0.05)) offset += side * spacing;
    const node = this.addNode(group.origin.clone().addScaledVector(trackFrame(group.axis).right, offset), source.gauge);
    node.parallelGroup = group.id; node.axis.copy(group.axis); group.offsets.set(node.id, offset);
    return node;
  }
  previewParallel(source: TrackNode, side: Side, point: THREE.Vector3, existing?: TrackNode, endDirection?: THREE.Vector3) {
    const group = this.parallelGroup(source), targetGroup = existing && this.parallelGroup(existing);
    if (!group || group.offsets.size < 2) throw new Error('Choose a node in a parallel group.');
    if ([...group.offsets.keys()].some(id => !this.canExtend(this.nodes.get(id)!, side))) {
      throw new Error('Cannot create a junction on a platform’s side. Build outside the station instead.');
    }
    if (existing && (!targetGroup || targetGroup === group)) throw new Error('Connect to a different, matching parallel group.');
    if (point.distanceTo(source.position) < 1) throw new Error('Choose an end node at least 1 m away.');
    const base = this.preview(source, point, side, existing, endDirection);
    const forward = base.v3.clone().sub(base.v2).normalize(), canonical = forward.clone().multiplyScalar(side);
    const sourceOffset = group.offsets.get(source.id)!;
    const targetSign = targetGroup && canonical.dot(targetGroup.axis) < 0 ? -1 : 1;
    const used = new Set<number>();
    return [...group.offsets].map(([id, value]) => {
      const node = this.nodes.get(id)!;
      if (node.gauge !== source.gauge) throw new Error('Parallel tracks must use the same gauge.');
      const offset = value - sourceOffset;
      const targetId = targetGroup && [...targetGroup.offsets].find(([candidate, targetOffset]) =>
        !used.has(candidate) && Math.abs((targetOffset - targetGroup.offsets.get(existing!.id)!) * targetSign - offset) < 0.05)?.[0];
      const target = targetId === undefined ? undefined : this.nodes.get(targetId)!;
      if (existing && (!target || target.gauge !== source.gauge)) throw new Error('The destination group must match every track’s gauge and spacing.');
      if (target) used.add(target.id);
      const sample = (t: number) => base.getPoint(t).addScaledVector(trackFrame(base.getTangent(t)).right, offset * side);
      const end = target?.position.clone() ?? sample(1);
      const d0 = node.axis.clone().multiplyScalar(side), d1 = forward;
      // Fit independent handle lengths to the true offset curve, keeping shared endpoint directions.
      let aa = 0, ab = 0, bb = 0, ar = 0, br = 0;
      for (let i = 1; i < 64; i++) {
        const t = i / 64, u = 1 - t, b0 = u ** 3, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t ** 3;
        const a = d0.clone().multiplyScalar(b1), b = d1.clone().multiplyScalar(-b2);
        const residual = sample(t).addScaledVector(node.position, -(b0 + b1)).addScaledVector(end, -(b2 + b3));
        aa += a.dot(a); ab += a.dot(b); bb += b.dot(b); ar += a.dot(residual); br += b.dot(residual);
      }
      const determinant = aa * bb - ab * ab;
      const aLength = (ar * bb - br * ab) / determinant, bLength = (br * aa - ar * ab) / determinant;
      if (!Number.isFinite(aLength + bLength) || aLength < 0.25 || bLength < 0.25) throw new Error('This parallel turn is too tight. Use a gentler curve.');
      const curve = new THREE.CubicBezierCurve3(node.position.clone(), node.position.clone().addScaledVector(d0, aLength), end.clone().addScaledVector(d1, -bLength), end);
      for (let i = 0; i <= 128; i++) {
        const t = i / 128;
        if (curve.getPoint(t).distanceTo(sample(t)) > 0.25 || curve.getTangent(t).dot(base.getTangent(t)) <= 0 ||
          i > 0 && sample(t).sub(sample(t - 1 / 128)).dot(base.getTangent(t)) <= 0) {
          throw new Error('Cannot keep parallel spacing on this curve. Use a gentler curve or shorter segments.');
        }
      }
      return { source: node, target, offset, curve, aLength, bLength };
    });
  }
  extendParallel(source: TrackNode, side: Side, point: THREE.Vector3, existing?: TrackNode, endDirection?: THREE.Vector3) {
    const plan = this.previewParallel(source, side, point, existing, endDirection);
    const counters = [this.nextNode, this.nextSegment, this.nextGroup];
    const addedNodes: number[] = [], addedEdges: number[] = [];
    let createdGroup: number | undefined;
    try {
      const reference = plan.find(entry => entry.source.id === source.id)!.curve;
      const axis = reference.v3.clone().sub(reference.v2).normalize().multiplyScalar(side);
      const targets = plan.map(entry => {
        if (entry.target) return entry.target;
        const node = this.addNode(entry.curve.v3, source.gauge); node.axis.copy(axis); addedNodes.push(node.id); return node;
      });
      if (!existing) {
        const anchor = targets[plan.findIndex(entry => entry.source.id === source.id)];
        const group: ParallelGroup = { id: this.nextGroup++, anchor: anchor.id, origin: anchor.position.clone(), axis,
          offsets: new Map(targets.map((node, i) => [node.id, plan[i].offset])) };
        createdGroup = group.id; this.parallelGroups.set(group.id, group);
        for (const node of targets) node.parallelGroup = group.id;
      }
      const edges = plan.map((entry, i) => {
        const edge = this.connect(entry.source, targets[i], source.gauge, side);
        addedEdges.push(edge.id);
        edge.bSide = targets[i].axis.dot(entry.curve.v3.clone().sub(entry.curve.v2)) >= 0 ? -1 : 1;
        edge.aLength = entry.aLength; edge.bLength = entry.bLength; return edge;
      });
      const index = plan.findIndex(entry => entry.source.id === source.id);
      return { node: targets[index], side: -edges[index].bSide as Side, edges };
    } catch (error) {
      addedEdges.forEach(id => this.segments.delete(id)); addedNodes.forEach(id => this.nodes.delete(id));
      if (createdGroup !== undefined) this.parallelGroups.delete(createdGroup);
      [this.nextNode, this.nextSegment, this.nextGroup] = counters;
      throw error;
    }
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
  connect(a: TrackNode, b: TrackNode, gauge: number, aSide?: Side, endDirection?: THREE.Vector3) {
    if (a.id === b.id) throw new Error('Choose a different end node.');
    const chord = b.position.clone().sub(a.position);
    const startSide = aSide ?? (this.directionLocked(a) && a.axis.dot(chord) < 0 ? -1 : 1);
    const endSide = this.directionLocked(b) && b.axis.dot(chord) < 0 ? 1 : -1;
    if (!this.canExtend(a, startSide) || !this.canExtend(b, endSide)) {
      throw new Error('Cannot create a junction on a platform’s side. Build outside the station instead.');
    }
    if (a.gauge !== gauge || b.gauge !== gauge) throw new Error(`Gauge mismatch: both nodes must use ${gauge.toLocaleString()} mm.`);
    if ([...this.segments.values()].some(edge => edge.a === a.id && edge.b === b.id || edge.a === b.id && edge.b === a.id)) throw new Error('These nodes are already connected.');
    if (chord.length() < 1) throw new Error('Nodes must be at least 1 m apart.');
    const length = chord.length() / 3;
    if (!this.directionLocked(a)) a.axis.copy(chord).normalize().multiplyScalar(aSide ?? 1);
    const orientedEnd = !!endDirection && !this.directionLocked(b);
    if (!this.directionLocked(b)) b.axis.copy(endDirection ?? chord).normalize();
    const segment: TrackSegment = { id: this.nextSegment++, a: a.id, b: b.id, gauge,
      aSide: aSide ?? (a.axis.dot(chord) >= 0 ? 1 : -1),
      bSide: orientedEnd || b.axis.dot(chord) >= 0 ? -1 : 1, aLength: length, bLength: length };
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
    const previousLength = nodeId === edge.a ? edge.aLength : edge.bLength;
    const side = nodeId === edge.a ? edge.aSide : edge.bSide;
    this.setDirection(node, offset.clone().normalize().multiplyScalar(side));
    if (nodeId === edge.a) edge.aLength = length; else edge.bLength = length;
    const group = this.parallelGroup(node);
    if (group && Math.abs(length - previousLength) > 1e-6) {
      for (const member of group.offsets.keys()) {
        if (member === nodeId) continue;
        for (const connected of this.incident(member)) {
          const atStart = connected.a === member;
          if ((atStart ? connected.aSide : connected.bSide) !== side) continue;
          if (atStart) connected.aLength = length; else connected.bLength = length;
        }
      }
    }
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
  preview(a: TrackNode, endpoint: THREE.Vector3, side: Side, b?: TrackNode, endDirection?: THREE.Vector3) {
    const chord = endpoint.clone().sub(a.position), length = chord.length() / 3;
    const first = this.directionLocked(a) ? a.position.clone().addScaledVector(a.axis, side * length) : a.position.clone().addScaledVector(chord, 1 / 3);
    const endSide = b && b.axis.dot(chord) >= 0 ? -1 : 1;
    const second = b && this.directionLocked(b) ? b.position.clone().addScaledVector(b.axis, endSide * length) : endDirection ? endpoint.clone().addScaledVector(endDirection.clone().normalize(), -length) : endpoint.clone().addScaledVector(chord, -1 / 3);
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
