import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
import * as THREE from 'three';
const load = entry => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
  const code = outputFiles[0].text.replaceAll('"three"', JSON.stringify(import.meta.resolve('three')));
  return import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
};
const { VEHICLES, BOGIES, vehicleBogie } = await load('src/train/catalog.ts');
const { makeVehicle, disposeVehicle, tintVehicle } = await load('src/train/render.ts');
const { TrainBuilder, compatibleGauges, consistLayout, consistPoses, appendPositions, COUPLER_GAP } = await load('src/train/consist.ts');
const { sampleTrack, vehiclePose, applyVehiclePose, vehiclesOverlap } = await load('src/train/placement.ts');
const { TrackNetwork } = await load('src/track/model.ts');
const p = (x, y, z) => new THREE.Vector3(x, y, z);

test('train builder keeps separate drafts, appends front/rear and removes only the chosen draft item', () => {
  const builder = new TrainBuilder(), train = builder.create();
  const loco = builder.add(VEHICLES[0]), coach = builder.add(VEHICLES[1]);
  const donor = builder.create(); const dome = builder.add(VEHICLES[2]);
  builder.merge(train, donor, 'rear');
  assert.deepEqual(train.items.map(item => item.id), [loco.id, coach.id, dome.id]);
  assert.equal(builder.trains.includes(donor), false); assert.equal(builder.selected, train);
  assert.equal(builder.remove(donor.id, coach.id), false);
  assert.equal(builder.remove(train.id, coach.id), true);
  assert.deepEqual(train.items.map(item => item.id), [loco.id, dome.id]);
  const leading = builder.create(); const extra = builder.add(VEHICLES[0]);
  builder.merge(train, leading, 'front');
  assert.deepEqual(train.items.map(item => item.id), [extra.id, loco.id, dome.id]);
  train.placed = true;
  assert.equal(builder.remove(train.id, loco.id), false);
  assert.throws(() => builder.add(VEHICLES[1]), /unplaced/);
  const other = builder.create(); builder.add(VEHICLES[1]);
  const before = other.items.length;
  assert.throws(() => builder.add({ ...VEHICLES[0], bogiesByGauge: {} }), /no compatible gauge/);
  assert.equal(other.items.length, before);
  assert.deepEqual(compatibleGauges([VEHICLES[0], VEHICLES[1], VEHICLES[2]]), [1435]);
});

test('whole consists fit in order across opposite-side nodes and reverse as a unit', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(-200, 0, 0), 1435), b = network.addNode(p(0, 0, 0), 1435), c = network.addNode(p(200, 0, 0), 1435);
  const first = network.connect(a, b, 1435); network.connect(b, c, 1435);
  const definitions = [VEHICLES[0], VEHICLES[1], VEHICLES[2]], layout = consistLayout(definitions);
  assert.ok(Math.abs(layout.length - definitions.reduce((sum, def) => sum + def.length, 0) - 2 * COUPLER_GAP) < 1e-8);
  const position = { edge: first.id, u: 0.99, facing: 1 };
  const poses = consistPoses(network, definitions, position);
  assert.ok(poses[0].pose.center.x > poses[1].pose.center.x && poses[1].pose.center.x > poses[2].pose.center.x);
  const reverse = consistPoses(network, definitions, { ...position, facing: -1 });
  assert.ok(reverse[0].pose.center.x < reverse[1].pose.center.x && reverse[1].pose.center.x < reverse[2].pose.center.x);
  for (let i = 0; i < definitions.length; i++) {
    assert.ok(Math.abs(poses[i].pose.center.x + reverse[i].pose.center.x - 2 * sampleTrack(network, position, 0).point.x) < 1e-6);
  }
  assert.throws(() => consistPoses(network, definitions, { ...position, u: 0.01 }), /Not enough/);
  assert.throws(() => consistPoses(network, [], position), /at least one/);
});

test('appending either end preserves old vehicle positions and reverses incoming item order when requested', () => {
  for (const end of ['front', 'rear']) for (const facing of [1, -1]) {
    const network = new TrackNetwork();
    const a = network.addNode(p(-300, 0, 0), 1435), b = network.addNode(p(300, 0, 0), 1435);
    const edge = network.connect(a, b, 1435), anchor = { edge: edge.id, u: 0.5, facing };
    const builder = new TrainBuilder(), target = builder.create();
    builder.add(VEHICLES[0]); builder.add(VEHICLES[1]); target.placed = true;
    const old = consistPoses(network, target.items.map(item => item.definition), anchor);
    const oldIds = target.items.map(item => item.id), oldLength = consistLayout(target.items.map(item => item.definition)).length;
    const source = builder.create(); builder.add(VEHICLES[2]); builder.add(VEHICLES[0]);
    const sourceIds = source.items.map(item => item.id), incomingLength = consistLayout(source.items.map(item => item.definition)).length;
    const positions = appendPositions(network, anchor, oldLength, incomingLength, end);
    builder.merge(target, source, end, true);
    const merged = consistPoses(network, target.items.map(item => item.definition), positions.combined);
    oldIds.forEach((id, i) => {
      const index = target.items.findIndex(item => item.id === id);
      assert.ok(merged[index].pose.center.distanceTo(old[i].pose.center) < 1e-6);
    });
    const appended = target.items.filter(item => sourceIds.includes(item.id));
    assert.deepEqual(appended.map(item => item.id), [...sourceIds].reverse());
    assert.ok(appended.every(item => item.facing === -1));
    assert.equal(target.placed, true); assert.equal(builder.trains.length, 1);
  }
});

test('previews are translucent while newly created parked visuals remain opaque', () => {
  const preview = makeVehicle(VEHICLES[0], 1435), placed = makeVehicle(VEHICLES[0], 1435);
  try {
    tintVehicle(preview, true); tintVehicle(preview, false);
    preview.group.traverse(object => {
      if (!object.isMesh) return;
      assert.equal(object.material.transparent, true); assert.equal(object.material.opacity, 0.42); assert.equal(object.material.depthWrite, false);
    });
    assert.equal(placed.body.children[0].material.transparent, false);
    assert.equal(placed.body.children[0].material.opacity, 1);
  } finally { disposeVehicle(preview); disposeVehicle(placed); }
});

test('four separate vehicle definitions declare explicit gauge and bogie compatibility', () => {
  assert.equal(VEHICLES.length, 4); assert.equal(new Set(VEHICLES.map(v => v.id)).size, 4);
  assert.deepEqual(VEHICLES.map(v => v.kind), ['locomotive', 'coach', 'dome', 'locomotive']);
  for (const def of VEHICLES) {
    assert.deepEqual(Object.keys(def.bogiesByGauge), ['1435']);
    assert.equal(vehicleBogie(def, 1435).gauge, 1435);
    assert.throws(() => vehicleBogie(def, 1000), /unavailable/);
    assert.throws(() => makeVehicle(def, 1600), /unavailable/);
  }
  assert.equal(vehicleBogie(VEHICLES[0], 1435).powered, true);
  assert.equal(vehicleBogie(VEHICLES[1], 1435).powered, false);
  assert.equal(vehicleBogie(VEHICLES[3], 1435).id, 'ge-fb2-1435');
  assert.equal(vehicleBogie(VEHICLES[3], 1435).powered, true);
  assert.notEqual(VEHICLES[3].bogiesByGauge[1435], VEHICLES[0].bogiesByGauge[1435]);
  assert.notEqual(VEHICLES[0].bogiesByGauge[1435], VEHICLES[1].bogiesByGauge[1435]);
  assert.throws(() => vehicleBogie({ ...VEHICLES[0], bogiesByGauge: { 1000: 'blomberg-b-1435' } }, 1000), /unavailable/);
});

test('GE 809 has an independent wide cab with three front panes and FB-2 trucks', () => {
  const visual = makeVehicle(VEHICLES.find(v => v.id === 'ge-809'), 1435);
  try {
    assert.equal(visual.body.children.filter(object => object.name === 'windshield').length, 3);
    assert.ok(visual.body.getObjectByName('nose-door'));
    assert.ok(visual.body.getObjectByName('radiator-housing'));
    assert.ok(visual.bogies.every(bogie => bogie.userData.bogieId === 'ge-fb2-1435'));
  } finally { disposeVehicle(visual); }
});

test('vehicle meshes use metre dimensions, correctly gauged wheels and distinct dome geometry', () => {
  for (const def of VEHICLES) {
    const visual = makeVehicle(def, 1435);
    try {
      visual.group.updateMatrixWorld(true);
      const bounds = new THREE.Box3().setFromObject(visual.group), size = bounds.getSize(p(0, 0, 0));
      assert.ok(Math.abs(size.z - def.length) < 0.05, `${def.id}: length ${size.z}`);
      assert.ok(Math.abs(size.x - def.width) < 0.15, `${def.id}: width ${size.x}`);
      assert.ok(Math.abs(bounds.max.y - def.height) < 0.12, `${def.id}: height ${bounds.max.y}`);
      assert.ok(bounds.min.y >= -0.04);
      for (const bogie of visual.bogies) {
        assert.equal(bogie.userData.bogieId, def.bogiesByGauge[1435]);
        const wheels = bogie.children.filter(object => object.name === 'wheel');
        assert.equal(wheels.length, 4);
        const negative = wheels.find(wheel => wheel.position.x < 0), positive = wheels.find(wheel => wheel.position.x > 0);
        assert.ok(Math.abs(positive.position.x - negative.position.x - 0.12 - 1.435) < 1e-8);
      }
      visual.group.traverse(object => {
        if (!object.isMesh) return;
        for (const value of object.geometry.getAttribute('position').array) assert.ok(Number.isFinite(value));
      });
    } finally { disposeVehicle(visual); }
  }
});

test('vehicle fit follows opposite-side track connections, rejects same-side U-turns and ambiguous routes', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(0, 0, 0), 1435), b = network.addNode(p(20, 0, 0), 1435), c = network.addNode(p(60, 0, 0), 1435);
  const first = network.connect(a, b, 1435), second = network.connect(b, c, 1435);
  const position = { edge: first.id, u: 0.7, facing: 1 };
  assert.ok(sampleTrack(network, position, 15).point.distanceTo(p(29, 0, 0)) < 1e-5);
  assert.ok(sampleTrack(network, { ...position, facing: -1 }, -15).point.distanceTo(p(29, 0, 0)) < 1e-5);
  assert.doesNotThrow(() => vehiclePose(network, VEHICLES[1], position));
  assert.throws(() => vehiclePose(network, VEHICLES[1], { ...position, u: 0.05 }), /Not enough/);
  const correct = second.aSide;
  second.aSide = first.bSide;
  assert.throws(() => sampleTrack(network, position, 15), /Not enough/);
  second.aSide = correct;
  const branch = network.addNode(p(60, 0, 10), 1435);
  network.connect(b, branch, 1435, second.aSide);
  assert.throws(() => sampleTrack(network, position, 15), /multiple possible/);
});

test('parked bodies and bogies follow curves without cant, reverse correctly and reject wrong gauges', () => {
  const network = new TrackNetwork();
  const a = network.addNode(p(1000000, 10, 0), 1435), b = network.addNode(p(1000200, 30, 40), 1435);
  const edge = network.connect(a, b, 1435);
  network.moveHandle(edge, a.id, p(1000060, 10, 0));
  const position = { edge: edge.id, u: 0.5, facing: 1 }, visual = makeVehicle(VEHICLES[0], 1435);
  try {
    const pose = vehiclePose(network, VEHICLES[0], position);
    applyVehiclePose(visual, pose);
    assert.ok(visual.group.position.distanceTo(pose.center) < 1e-8);
    for (const [i, point] of [[0, pose.front], [1, pose.rear]]) {
      assert.ok(visual.bogies[i].position.clone().add(visual.group.position).distanceTo(point) < 1e-8);
      assert.ok(Math.abs(p(1, 0, 0).applyQuaternion(visual.bogies[i].quaternion).y) < 1e-8);
    }
    const reverse = vehiclePose(network, VEHICLES[0], { ...position, facing: -1 });
    assert.ok(reverse.front.distanceTo(pose.rear) < 1e-8); assert.ok(reverse.rear.distanceTo(pose.front) < 1e-8);
    edge.gauge = 1000;
    assert.throws(() => vehiclePose(network, VEHICLES[0], position), /unavailable/);
    edge.gauge = 1435; network.segments.delete(edge.id);
    assert.throws(() => vehiclePose(network, VEHICLES[0], position), /removed/);
  } finally { disposeVehicle(visual); }
});

test('overlap checks permit parallel tracks and cleanup disposes shared resources once', () => {
  const a = makeVehicle(VEHICLES[0], 1435), b = makeVehicle(VEHICLES[0], 1435);
  try {
    assert.equal(vehiclesOverlap(a, b), true);
    b.group.position.x = 4.035;
    assert.equal(vehiclesOverlap(a, b), false);
    b.group.position.set(0, 0, 20);
    assert.equal(vehiclesOverlap(a, b), false);
    let materialDisposals = 0;
    const material = a.bogies[0].children[0].material;
    material.addEventListener('dispose', () => materialDisposals++);
    disposeVehicle(a); assert.equal(materialDisposals, 1);
  } finally { disposeVehicle(a); disposeVehicle(b); }
});
