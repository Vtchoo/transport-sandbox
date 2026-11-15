import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
import * as THREE from 'three';

const load = (entry) => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node',
    plugins: undefined, external: ['three'] });
  const code = outputFiles[0].text.replaceAll('"three"', JSON.stringify(import.meta.resolve('three')));
  return import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
};
const { TrackNetwork, trackFrame } = await load('src/track/model.ts');
const { TrackEditor } = await load('src/track/editor.ts');
const { makeTrack, updateSleeperLOD, disposeTrack } = await load('src/track/render.ts');
const { EarthworkField, prepareEarthPaths } = await load('src/track/earthworks.ts');
const { TerrainGenerator, DEFAULT_SETTINGS, buildChunk } = await load('src/terrain/generator.ts');
const p = (x, y, z) => new THREE.Vector3(x, y, z);

test('sleeper LOD reduces draw counts by proximity without moving sleepers or rebuilding rails', () => {
  const curve = new THREE.CubicBezierCurve3(p(0, 0, 0), p(10, 0, 0), p(20, 0, 0), p(30, 0, 0));
  const track = makeTrack(curve, 1435);
  try {
    const batch = track.children.find(mesh => mesh.isInstancedMesh);
    const original = batch.instanceMatrix.array.slice(), bounds = batch.boundingSphere.clone();
    const center = batch.userData.sleeperLOD.center;
    const counts = [];
    for (const height of [0, 250, 600, 1200, 2000]) {
      updateSleeperLOD(track, center.clone().add(p(0, height, 0)));
      counts.push(batch.count);
    }
    assert.ok(counts.slice(1).every((count, i) => count < counts[i]));
    assert.equal(counts.at(-1), 0); assert.equal(batch.visible, false);
    updateSleeperLOD(track, center);
    assert.equal(batch.count, counts[0]); assert.equal(batch.visible, true);
    updateSleeperLOD(track, center.clone().add(p(0, 160, 0)));
    assert.equal(batch.count, counts[0]); // Hysteresis retains the nearby level.
    updateSleeperLOD(track, center.clone().add(p(0, 170, 0)));
    const reduced = batch.count;
    updateSleeperLOD(track, center.clone().add(p(0, 150, 0)));
    assert.equal(batch.count, reduced);
    assert.deepEqual(batch.instanceMatrix.array, original);
    assert.ok(batch.boundingSphere.equals(bounds));
    assert.ok(track.children.filter(mesh => !mesh.isInstancedMesh).every(mesh => mesh.visible));
  } finally { disposeTrack(track); }
});

test('long tracks retain nearby sleepers while distant batches disappear, including after origin shifts', () => {
  const curve = new THREE.CubicBezierCurve3(p(1000000, 0, 0), p(1000700, 0, 0), p(1001400, 0, 0), p(1002100, 0, 0));
  const track = makeTrack(curve, 1435);
  try {
    const batches = track.children.filter(mesh => mesh.isInstancedMesh);
    const eye = p(1002100, 10, 0);
    updateSleeperLOD(track, eye);
    assert.equal(batches[0].count, 0);
    assert.equal(batches.at(-1).count, batches.at(-1).userData.sleeperLOD.counts[0]);
    const counts = batches.map(batch => batch.count);
    const shifted = new THREE.Group(); shifted.position.x = -1002000; shifted.add(track);
    updateSleeperLOD(track, eye);
    assert.deepEqual(batches.map(batch => batch.count), counts);
  } finally { disposeTrack(track); }
});

test('hover deletion targets the correct node or segment, requires confirmation and supports Escape', () => {
  const elements = new Map();
  const saved = new Map(['document', 'Option', 'innerWidth', 'innerHeight'].map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  const element = id => {
    if (!elements.has(id)) elements.set(id, Object.assign(new EventTarget(), {
      hidden: false, textContent: '', style: {}, classList: { add() {}, remove() {}, toggle() {} },
      setAttribute() {}, replaceChildren() {},
    }));
    return elements.get(id);
  };
  let editor;
  try {
    globalThis.document = { getElementById: element };
    globalThis.Option = class { constructor(label, value) { this.label = label; this.value = value; } };
    globalThis.innerWidth = 1200; globalThis.innerHeight = 800;
    let earthworks;
    editor = new TrackEditor(new THREE.Scene(), new THREE.PerspectiveCamera(), {}, {
      origin: new THREE.Vector2(), setEarthworks(paths) { earthworks = paths; },
    });
    editor.setEnabled(true);
    const a = editor.network.addNode(p(0, 0, 0), 1435), b = editor.network.addNode(p(30, 0, 0), 1435);
    const c = editor.network.addNode(p(200, 0, 0), 1435);
    const target = editor.network.connect(a, b, 1435), retained = editor.network.connect(b, c, 1435);
    editor.rebuild();
    const retainedMesh = editor.segmentMeshes.get(retained.id);
    editor.project = point => ({ x: point.x + 400, y: 400, visible: true });
    const hover = () => editor.hoverSegment({ clientX: 415, clientY: 400 });
    const click = () => element('track-delete-segment').dispatchEvent(new Event('click'));
    hover(); assert.equal(element('track-delete-segment').hidden, false);
    click(); assert.equal(editor.network.segments.size, 2);
    editor.escape(); assert.equal(editor.network.segments.size, 2);
    hover(); click();
    // Changing hovered segments must discard the previous confirmation.
    editor.hoverSegment({ clientX: 515, clientY: 400 });
    click(); assert.equal(editor.network.segments.size, 2);
    hover(); click(); assert.equal(editor.network.segments.size, 2);
    click();
    assert.deepEqual([...editor.network.segments.keys()], [retained.id]);
    assert.equal(editor.network.nodes.size, 3);
    assert.equal(editor.segmentMeshes.has(target.id), false);
    assert.equal(editor.segmentMeshes.get(retained.id), retainedMesh);
    assert.equal(earthworks.length, 1);
    assert.equal(element('track-delete-segment').hidden, true);
    assert.equal(element('track-straighten').hidden, true);
    // Node deletion targets the hovered node, independently of the selection.
    editor.selected = a.id; editor.refreshGizmos();
    assert.equal(element('track-delete').hidden, true);
    const hoverNode = node => editor.hoverNode({ clientX: node.position.x + 400, clientY: 400 });
    const deleteNode = () => element('track-delete').dispatchEvent(new Event('click'));
    hoverNode(c);
    assert.equal(element('track-delete').hidden, false);
    const offset = Math.hypot(
      parseFloat(element('track-delete').style.left) - parseFloat(element('track-direction').style.left),
      parseFloat(element('track-delete').style.top) - parseFloat(element('track-direction').style.top),
    );
    assert.equal(offset, 32);
    editor.leave(element('track-delete'));
    assert.equal(element('track-delete').hidden, false);
    deleteNode(); assert.equal(editor.network.nodes.size, 3);
    editor.refreshGizmos(); assert.equal(editor.confirmDelete, c.id);
    hoverNode(b); assert.equal(editor.confirmDelete, undefined);
    hoverNode(c); deleteNode(); editor.escape();
    assert.equal(editor.network.nodes.size, 3);
    deleteNode(); deleteNode();
    assert.equal(editor.network.nodes.has(c.id), false);
    assert.equal(editor.network.nodes.has(a.id), true);
    assert.equal(editor.selected, a.id);
    assert.equal(element('track-delete').hidden, true);
    hoverNode(a); deleteNode(); editor.leave(null);
    assert.equal(element('track-delete').hidden, true);
    assert.equal(editor.confirmDelete, undefined);
  } finally {
    editor?.dispose();
    for (const [key, descriptor] of saved) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
    }
  }
});

test('drag rendering preserves unrelated meshes and restores affected sleepers on release', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(0, 0, 0), 1435), b = network.addNode(p(30, 0, 0), 1435);
  const parallel = network.addParallel(a, 1), end = network.addNode(p(30, 0, 10), 1435);
  const farA = network.addNode(p(100, 0, 0), 1435), farB = network.addNode(p(130, 0, 0), 1435);
  const ab = network.connect(a, b, 1435), parallelEdge = network.connect(parallel, end, 1435), far = network.connect(farA, farB, 1435);
  const editor = Object.create(TrackEditor.prototype);
  Object.assign(editor, { network, rails: new THREE.Group(), segmentMeshes: new Map(), refreshGizmos() {}, canvas: { hasPointerCapture: () => false } });
  editor.rebuild();
  const untouched = editor.segmentMeshes.get(far.id);
  let disposed = false;
  untouched.children[0].geometry.addEventListener('dispose', () => { disposed = true; });
  const hasSleepers = id => editor.segmentMeshes.get(id).children.some(mesh => mesh.isInstancedMesh);
  for (const pick of [{ node: a }, { node: a, edge: ab }, { node: parallel, spacing: true }]) {
    const affected = editor.affectedSegments(pick);
    assert.deepEqual([...affected], pick.spacing ? [parallelEdge.id] : [ab.id, parallelEdge.id]);
    editor.drag = { pick, affected, pointer: 1, changed: false };
    editor.rebuild();
    assert.equal(hasSleepers(ab.id), !!pick.spacing);
    assert.equal(hasSleepers(parallelEdge.id), false);
    assert.equal(hasSleepers(far.id), true);
    if (pick.spacing) network.setParallelOffset(parallel, 8);
    else network.moveNode(a, a.position.clone().add(p(1, 0, 0)));
    editor.rebuild();
    assert.equal(editor.segmentMeshes.get(far.id), untouched);
    editor.endDrag();
    assert.ok(hasSleepers(ab.id) && hasSleepers(parallelEdge.id));
    assert.equal(editor.segmentMeshes.get(far.id), untouched);
  }
  assert.equal(disposed, false);
  for (const mesh of editor.rails.children) mesh.traverse(object => { object.geometry?.dispose(); if (object.material) object.material.dispose(); });
});

test('deleting nodes removes incident tracks and rebases parallel groups without moving survivors', () => {
  const network = new TrackNetwork();
  const anchor = network.addNode(p(0, 5, 0), 1435);
  const second = network.addParallel(anchor, 1), third = network.addParallel(anchor, -1);
  const neighbor = network.addNode(p(50, 5, 0), 1435), beyond = network.addNode(p(100, 5, 0), 1435);
  network.connect(anchor, neighbor, 1435);
  const retained = network.connect(neighbor, beyond, 1435);
  const positions = [second.position.clone(), third.position.clone()];
  network.deleteNode(anchor.id);
  assert.equal(network.nodes.has(anchor.id), false);
  assert.deepEqual([...network.segments.keys()], [retained.id]);
  const group = network.parallelGroup(second);
  assert.equal(group.anchor, second.id); assert.equal(group.offsets.get(second.id), 0);
  assert.ok(second.position.equals(positions[0]) && third.position.equals(positions[1]));
  network.setDirection(second, p(1, 0, 0));
  assert.ok(second.position.equals(positions[0]));
  assert.ok(third.position.distanceTo(second.position.clone().addScaledVector(trackFrame(group.axis).right, group.offsets.get(third.id))) < 1e-8);
  network.deleteNode(second.id);
  assert.equal(third.parallelGroup, undefined); assert.equal(network.parallelGroups.size, 0);
  network.deleteNode(neighbor.id);
  assert.equal(network.segments.size, 0); assert.ok(network.nodes.has(beyond.id));
  network.deleteNode(neighbor.id); // Repeated deletion is harmless.
});

test('segments store gauge and reject mismatches without changing the network', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(0, 0, 0), 1435), b = network.addNode(p(100, 0, 0), 1435), narrow = network.addNode(p(200, 0, 0), 1000);
  const edge = network.connect(a, b, 1435);
  assert.equal(edge.gauge, 1435);
  assert.throws(() => network.connect(b, narrow, 1435), /Gauge mismatch/);
  assert.throws(() => network.connect(a, b, 1435), /already connected/);
  assert.equal(network.segments.size, 1);
});
test('shared nodes maintain a common 3D tangent while handle lengths stay independent', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(-100, 0, 0), 1435), b = network.addNode(p(0, 0, 0), 1435);
  const c = network.addNode(p(100, 5, 30), 1435), d = network.addNode(p(100, 10, -30), 1435);
  const ab = network.connect(a, b, 1435), bc = network.connect(b, c, 1435), bd = network.connect(b, d, 1435);
  const unchangedLengths = [bc.aLength, bd.aLength];
  network.moveHandle(ab, b.id, p(-50, 15, 10));
  assert.equal(bc.aLength, unchangedLengths[0]); assert.equal(bd.aLength, unchangedLengths[1]);
  const tangents = [network.curve(ab).getTangent(1), network.curve(bc).getTangent(0), network.curve(bd).getTangent(0)];
  for (const tangent of tangents) assert.ok(Math.abs(tangent.dot(b.axis)) > 0.999999);
  const handleOffset = network.handle(bc, b.id).sub(b.position);
  b.position.add(p(20, 30, -10));
  assert.ok(network.handle(bc, b.id).sub(b.position).distanceTo(handleOffset) < 1e-8);
});
test('track frames have horizontal lateral axes and upward normals without superelevation', () => {
  for (const tangent of [p(1, 0.4, 1), p(-2, -1, 3), p(0, 1, 0), p(0, 0, 0)]) {
    const frame = trackFrame(tangent);
    assert.ok(Math.abs(frame.right.y) < 1e-8);
    assert.ok(frame.up.y >= 0);
    assert.ok(Math.abs(frame.right.dot(frame.forward)) < 1e-8);
    assert.ok(Math.abs(frame.up.dot(frame.forward)) < 1e-8);
    assert.ok(Math.abs(frame.right.length() - 1) < 1e-8);
  }
});

test('straightening keeps endpoints and gauge fixed, including reversed handle sides', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(100, 0, 0), 1435), b = network.addNode(p(0, 20, 0), 1435);
  const ahead = network.addNode(p(200, 0, 0), 1435), other = network.addNode(p(100, 20, 0), 1435);
  const neighbourA = network.connect(a, ahead, 1435), neighbourB = network.connect(b, other, 1435);
  const edge = network.connect(a, b, 1435);
  assert.equal(edge.aSide, -1); assert.equal(edge.bSide, 1);
  network.moveHandle(edge, a.id, p(50, 30, 40));
  network.moveHandle(edge, b.id, p(30, 50, -20));
  const start = a.position.clone(), end = b.position.clone(), lengths = [neighbourA.aLength, neighbourB.aLength];
  network.straighten(edge);
  assert.ok(a.position.equals(start) && b.position.equals(end)); assert.equal(edge.gauge, 1435);
  for (let i = 0; i <= 20; i++) assert.ok(network.curve(edge).getPoint(i / 20).distanceTo(start.clone().lerp(end, i / 20)) < 1e-8);
  assert.equal(neighbourA.aLength, lengths[0]); assert.equal(neighbourB.aLength, lengths[1]);
  assert.ok(Math.abs(network.curve(neighbourA).getTangent(0).dot(a.axis)) > 0.999999);
});

test('parallel creation inherits gauge, chooses free slots, and preserves a direction before connection', () => {
  const network = new TrackNetwork(), a = network.addNode(p(0, 10, 0), 1600);
  const right = network.addParallel(a, 1), next = network.addParallel(a, 1), left = network.addParallel(a, -1);
  assert.equal(right.gauge, 1600);
  assert.ok(Math.abs(a.position.distanceTo(right.position) - 4.2) < 1e-8);
  assert.ok(Math.abs(a.position.distanceTo(next.position) - 8.4) < 1e-8);
  assert.equal(left.position.y, a.position.y);
  const axis = right.axis.clone(), endpoint = network.addNode(p(40, 20, 50), 1600);
  network.connect(right, endpoint, 1600);
  assert.ok(right.axis.equals(axis) && a.axis.equals(axis));
});

test('direction edits rotate parallel neighbours around the edited node with independent handle lengths', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(0, 0, 0), 1435), b = network.addNode(p(100, 0, 0), 1435);
  const ab = network.connect(a, b, 1435), pa = network.addParallel(a, 1), pb = network.addParallel(b, 1);
  const parallel = network.connect(pa, pb, 1435), originalLength = parallel.aLength;
  const fixed = a.position.clone(), spacing = a.position.distanceTo(pa.position);
  network.moveHandle(ab, a.id, p(30, 12, 20));
  assert.ok(a.position.equals(fixed)); assert.ok(pa.axis.distanceTo(a.axis) < 1e-8);
  assert.ok(Math.abs(pa.position.distanceTo(a.position) - spacing) < 1e-8);
  assert.ok(Math.abs(pa.position.clone().sub(a.position).dot(a.axis)) < 1e-8);
  assert.equal(parallel.aLength, originalLength);
  const pivot = pa.position.clone();
  network.moveHandle(parallel, pa.id, pa.position.clone().add(p(20, 4, -10)));
  assert.ok(pa.position.distanceTo(pivot) < 1e-8);
  assert.ok(pa.axis.distanceTo(a.axis) < 1e-8);
});

test('parallel spacing is independently adjustable while node movement translates the group', () => {
  const network = new TrackNetwork(), a = network.addNode(p(20, 5, 30), 1435);
  const parallel = network.addParallel(a, 1), fixed = a.position.clone();
  network.setParallelOffset(parallel, 7.5);
  assert.ok(a.position.equals(fixed)); assert.ok(Math.abs(parallel.position.distanceTo(a.position) - 7.5) < 1e-8);
  const delta = p(10, 3, -20);
  network.moveNode(parallel, parallel.position.clone().add(delta));
  assert.ok(a.position.distanceTo(fixed.add(delta)) < 1e-8);
  assert.ok(Math.abs(parallel.position.distanceTo(a.position) - 7.5) < 1e-8);
});

test('straightening updates parallel groups, but rejects an incompatible same-group segment', () => {
  const network = new TrackNetwork(), a = network.addNode(p(0, 0, 0), 1435), b = network.addNode(p(100, 10, 30), 1435);
  const edge = network.connect(a, b, 1435), parallel = network.addParallel(a, 1);
  network.moveHandle(edge, a.id, p(0, 5, 30));
  network.straighten(edge);
  assert.ok(parallel.axis.distanceTo(a.axis) < 1e-8);
  assert.ok(Math.abs(parallel.position.clone().sub(a.position).dot(a.axis)) < 1e-8);
  const cross = network.connect(a, parallel, 1435);
  assert.throws(() => network.straighten(cross), /same parallel group/);
  b.position.copy(a.position);
  assert.throws(() => network.straighten(edge), /at least 1 m/);
});
test('earthwork uses a 4 m bed with matching 2H:1V cuts and fill', () => {
  const field = new EarthworkField([{ points: [{ x: 0, y: 0.65, z: 0 }, { x: 100, y: 0.65, z: 0 }] }]);
  assert.equal(field.height(50, 0, 10), 0);
  assert.equal(field.height(50, 2, -10), 0);
  assert.equal(field.height(50, 6, 10), 2);
  assert.equal(field.height(50, 6, -10), -2);
  assert.equal(field.height(50, 100, 10), 10);
  assert.equal(new EarthworkField([]).height(0, 0, 25), 25);
});
test('modified terrain refines narrow cuts and matches across chunk boundaries', () => {
  const generator = new TerrainGenerator(DEFAULT_SETTINGS);
  generator.baseHeight = () => 10;
  generator.color = () => [0.1, 0.2, 0.1];
  generator.earthworks = new EarthworkField([{ points: [{ x: 490, y: 0.65, z: 250 }, { x: 540, y: 0.65, z: 250 }] }]);
  const a = buildChunk(generator, { id: 1, x: 0, z: 0, segments: 16, settings: DEFAULT_SETTINGS, revision: 7 });
  const b = buildChunk(generator, { id: 2, x: 1, z: 0, segments: 32, settings: DEFAULT_SETTINGS, revision: 7 });
  assert.equal(a.revision, 7);
  const boundary = (chunk, x) => {
    const values = new Map();
    for (let i = 0; i < chunk.positions.length; i += 3) if (chunk.positions[i] === x) {
      const z = chunk.positions[i + 2], h = chunk.positions[i + 1];
      values.set(z, Math.max(h, values.get(z) ?? -Infinity));
    }
    return values;
  };
  const left = boundary(a, 512), right = boundary(b, 0);
  for (const [z, h] of left) if (right.has(z)) assert.equal(h, right.get(z));
  assert.equal(left.get(250), 0);
  assert.ok(a.positions.every(Number.isFinite) && a.normals.every(Number.isFinite));
  assert.ok(a.indices.every(index => index < a.positions.length / 3));
  for (let i = 0; i < a.indices.length; i += 3) {
    const [ia, ib, ic] = a.indices.subarray(i, i + 3);
    const ax = a.positions[ib * 3] - a.positions[ia * 3], az = a.positions[ib * 3 + 2] - a.positions[ia * 3 + 2];
    const bx = a.positions[ic * 3] - a.positions[ia * 3], bz = a.positions[ic * 3 + 2] - a.positions[ia * 3 + 2];
    assert.ok(az * bx - ax * bz >= 0);
  }
});
test('side slopes stop at the first terrain intersection, preserving a valley beyond a ridge', () => {
  const natural = (x, z) => Math.abs(z) >= 9 && Math.abs(z) <= 16 ? 20 : 0;
  const paths = prepareEarthPaths([{ points: [{ x: 0, y: 10.65, z: 0 }, { x: 100, y: 10.65, z: 0 }] }], natural, 1800);
  const field = new EarthworkField(paths);
  assert.ok(paths[0].points[0].right < 10);
  assert.equal(field.height(50, 5, 0), 8.5);
  assert.equal(field.height(50, 18, 0), 0);
});
