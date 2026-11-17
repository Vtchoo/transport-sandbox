import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
import * as THREE from 'three';
import { readFile } from 'node:fs/promises';
import { convertColladaVehicle } from './lib/collada-vehicle.mjs';
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
const { catalogDestination, catalogAllows } = await load('src/train/catalog-routing.ts');
const { advanceVehicle } = await load('src/train/motion.ts');
const p = (x, y, z) => new THREE.Vector3(x, y, z);

test('SketchUp COLLADA import honors units, named component pivots and mirrored geometry without stale generated data', async () => {
  const source = await readFile('public/models/locomotives/dash-9/dash9.dae', 'utf8');
  const generated = JSON.parse(await readFile('src/train/assets/dash9.json', 'utf8'));
  const imported = convertColladaVehicle(source);
  assert.deepEqual(imported, generated, 'Run npm run import:dash9 after exporting the source');
  assert.equal(imported.sourceUnitMeters, .0254);
  assert.equal(imported.bogieCenters, 16.1544);
  for (const [actual, expected] of imported.bounds.size.map((value, i) => [value, [3.1242, 4.699, 22.3012][i]]))
    assert.ok(Math.abs(actual - expected) < .00001);
  for (const meshes of Object.values(imported.parts)) for (const mesh of meshes)
    for (let i = 0; i < mesh.positions.length; i += 9) {
      const a = p(...mesh.positions.slice(i, i + 3)), b = p(...mesh.positions.slice(i + 3, i + 6)), c = p(...mesh.positions.slice(i + 6, i + 9));
      const normal = p(...mesh.normals.slice(i, i + 3));
      assert.ok(b.sub(a).cross(c.sub(a)).dot(normal) > 0, 'Baked mirror has inward winding');
    }
  const badPivot = source.replace('0 1 0 -318', '0 1 0 -398').replace('1 0 0 0 0 1 0 -398', '1 0 0 -28.24803 0 1 0 -398');
  assert.throws(() => convertColladaVehicle(badPivot), /Bogie origins/);
  assert.throws(() => convertColladaVehicle(source.replace('name="body"', 'name="missing-body"')), /exactly one body/);
});

test('imported Dash 9 keeps separate rail-level bogies, correctly gauged temporary six-axle gear and independent preview materials', () => {
  const definition = VEHICLES.find(def => def.id === 'ge-dash9');
  const placed = makeVehicle(definition, 1435), preview = makeVehicle(definition, 1435);
  try {
    assert.equal(placed.separateBogies, true);
    assert.equal(placed.bogies[0].position.z, 8.0772);
    assert.equal(placed.bogies[1].position.z, -8.0772);
    assert.equal(placed.bogies[0].position.y, 0);
    assert.ok(placed.bogies[0].getObjectByName('imported-front'));
    assert.ok(placed.bogies[1].getObjectByName('imported-rear'));
    const before = new THREE.Box3().setFromObject(placed.body);
    placed.bogies[0].rotation.y = .3;
    assert.ok(new THREE.Box3().setFromObject(placed.body).equals(before), 'Turning a bogie moved the body');
    let lines = 0; placed.group.traverse(object => { if (object.isLine) lines++; }); assert.equal(lines, 0);
    for (const bogie of placed.bogies) {
      const wheels = bogie.children.filter(object => object.name === 'wheel');
      assert.equal(wheels.length, 6);
      assert.deepEqual([...new Set(wheels.map(wheel => wheel.position.z))], [2.032, 0, -2.032]);
      assert.ok(wheels.every(wheel => wheel.position.y === .5334));
    }
    tintVehicle(preview, false);
    placed.group.traverse(object => { if (object.isMesh) assert.equal(object.material.opacity, 1); });
    disposeVehicle(preview);
    assert.ok(placed.body.children[0].geometry.attributes.position.count > 0);
  } finally { disposeVehicle(placed); disposeVehicle(preview); }
});

test('steam crank pins stay attached to coupling rods and connecting rods constrain the crosshead throughout a revolution', () => {
  for (const gauge of [914, 1435]) {
    const visual = makeVehicle(VEHICLES.find(def => def.id === 'drgw-consolidation'), gauge);
    try {
      const engine = visual.sections[0], wheels = engine.motion.parts.filter(part => part.name === 'driver-wheel');
      const rods = engine.motion.parts.filter(part => part.name === 'coupling-rod');
      const connecting = engine.motion.parts.filter(part => part.name === 'connecting-rod');
      const crossheads = engine.motion.parts.filter(part => part.name === 'crosshead');
      for (let frame = 0; frame < 64; frame++) {
        advanceVehicle(visual, Math.PI * 2 * .65 / 64);
        visual.group.updateMatrixWorld(true);
        for (let side = 0; side < 2; side++) {
          const rod = rods[side];
          for (let axle = 0; axle < 4; axle++) {
            const pin = wheels[axle * 2 + side].getObjectByName('crank-pin').getWorldPosition(p(0, 0, 0));
            const bearing = rod.localToWorld(p(0, 0, [-2.2, -.85, .5, 1.85][axle] + .175));
            assert.ok(pin.distanceTo(bearing) < 1e-8);
          }
          const start = connecting[side].localToWorld(p(0, 0, -.5)), end = connecting[side].localToWorld(p(0, 0, .5));
          const crosshead = crossheads[side].getWorldPosition(p(0, 0, 0));
          assert.ok(end.distanceTo(crosshead) < 1e-8);
          assert.ok(Math.abs(start.distanceTo(end) - 2.1) < 1e-8);
          assert.ok(Math.abs(crossheads[side].position.y - .85) < 1e-8);
        }
        assert.ok(Math.abs(Math.cos(wheels[0].rotation.x - wheels[1].rotation.x)) < 1e-8);
      }
      const before = engine.motion.parts.map(part => ({ position: part.position.clone(), quaternion: part.quaternion.clone() }));
      advanceVehicle(visual, 1.3); advanceVehicle(visual, -1.3);
      engine.motion.parts.forEach((part, i) => {
        assert.ok(part.position.distanceTo(before[i].position) < 1e-8);
        assert.ok(part.quaternion.angleTo(before[i].quaternion) < 1e-7);
      });
    } finally { disposeVehicle(visual); }
  }
});

test('Rio Grande equipment fits both gauges, with a permanent tender and true 2-8-0 wheels', () => {
  const definitions = VEHICLES.filter(def => def.id.startsWith('drgw-'));
  assert.equal(definitions.length, 3);
  assert.equal(definitions[0].sections.length, 2);
  assert.equal(definitions[0].physics.massKg, 93200);
  for (const gauge of [914, 1435]) for (const def of definitions) {
    const visual = makeVehicle(def, gauge);
    try {
      const engine = visual.sections?.[0], tender = visual.sections?.[1];
      if (engine) {
        const drivers = engine.body.children.filter(object => object.name === 'driver-wheel');
        assert.equal(drivers.length, 8);
        assert.equal(engine.body.children.filter(object => object.name === 'leading-wheel').length, 2);
        assert.ok(Math.abs(Math.abs(drivers[0].position.x - drivers[1].position.x) - .12 - gauge / 1000) < 1e-8);
        assert.equal(tender.definition.physics.powerW, 0);
      }
      for (const section of visual.sections ?? [visual]) {
        assert.equal(vehicleBogie(section.definition, gauge).gauge, gauge);
        if (section.definition.runningGear === 'steam') continue;
        for (const bogie of section.bogies) {
          const wheels = bogie.children.filter(object => object.name === 'wheel');
          assert.equal(wheels.length, 4);
          const xs = wheels.map(wheel => wheel.position.x);
          assert.ok(Math.abs(Math.max(...xs) - Math.min(...xs) - .12 - gauge / 1000) < 1e-8);
        }
      }
      assert.throws(() => makeVehicle(def, 1000), /unavailable/);
    } finally { disposeVehicle(visual); }
  }
});

test('engine and tender articulate independently, including reversed placement on a curved grade', () => {
  const def = VEHICLES.find(def => def.id === 'drgw-consolidation');
  for (const gauge of [914, 1435]) {
    const network = new TrackNetwork();
    const a = network.addNode(p(-45, 0, 0), gauge), b = network.addNode(p(45, 6, 0), gauge);
    const edge = network.connect(a, b, gauge); a.axis.set(1, .06, 1).normalize(); b.axis.set(1, .06, -1).normalize(); edge.aLength = edge.bLength = 40;
    const visual = makeVehicle(def, gauge), pose = vehiclePose(network, def, { edge: edge.id, u: .5, facing: 1 });
    try {
      for (const facing of [1, -1]) {
        applyVehiclePose(visual, facing === 1 ? pose : { ...pose, reversed: true, front: pose.rear, rear: pose.front, frontDirection: pose.rearDirection.clone().negate(), rearDirection: pose.frontDirection.clone().negate() });
        const expected = pose.sections(facing);
        visual.group.updateMatrixWorld(true);
        visual.sections.forEach((section, i) => assert.ok(section.group.getWorldPosition(p(0, 0, 0)).distanceTo(expected[i].center) < 1e-6));
        assert.ok(visual.sections[0].body.quaternion.angleTo(visual.sections[1].body.quaternion) > .05);
        for (const section of visual.sections) for (const bogie of section.bogies) assert.ok(bogie.position.toArray().every(Number.isFinite));
      }
    } finally { disposeVehicle(visual); }
  }
});

test('catalog targets only the frontmost editable train tab, with no fallback to a lower editor', () => {
  const builder = new TrainBuilder(), train = builder.create(); builder.add(VEHICLES[0]);
  assert.equal(catalogDestination(undefined), undefined);
  assert.equal(catalogDestination({ train, tab: 'viewport' }), undefined);
  assert.equal(catalogDestination({ train, tab: 'edit' }), train);
  train.placed = true; assert.equal(catalogDestination({ train, tab: 'edit' }), train);
});

test('catalog filtering uses the entire consist gauge intersection and validates bogie mappings', () => {
  const builder = new TrainBuilder(), train = builder.create(); builder.add(VEHICLES[0]); builder.add(VEHICLES[1]);
  assert.equal(catalogAllows(VEHICLES[2], train), true);
  const incompatible = { ...VEHICLES[2], bogiesByGauge: { 1000: 'blomberg-b-1435' } };
  assert.equal(catalogAllows(incompatible, train), false);
  assert.equal(catalogAllows(incompatible), true);
  const multiGauge = { ...VEHICLES[2], bogiesByGauge: { ...VEHICLES[2].bogiesByGauge, 1000: 'not-a-bogie' } };
  assert.equal(catalogAllows(multiGauge, train), true);
  train.items.push({ id: 99, definition: incompatible, facing: 1 });
  assert.equal(catalogAllows(VEHICLES[0], train), false);
});

test('workbench inserts, reorders, flips and transfers vehicles without changing their identities', () => {
  const builder = new TrainBuilder(), first = builder.create();
  const loco = builder.add(VEHICLES[0]), coach = builder.add(VEHICLES[1]);
  const dome = builder.add(VEHICLES[2], 1);
  assert.deepEqual(first.items.map(item => item.id), [loco.id, dome.id, coach.id]);
  assert.equal(builder.moveItem(first.id, coach.id, first.id, 0), true);
  assert.deepEqual(first.items.map(item => item.id), [coach.id, loco.id, dome.id]);
  assert.equal(builder.moveItem(first.id, coach.id, first.id, 1), false);
  builder.flip(coach.id); assert.equal(coach.facing, -1);
  const second = builder.create(); builder.add(VEHICLES[3]);
  builder.moveItem(first.id, coach.id, second.id, 1);
  assert.equal(second.items[1], coach); assert.equal(coach.facing, -1);
  assert.deepEqual(first.items.map(item => item.id), [loco.id, dome.id]);
  assert.equal(builder.selected, second);
  second.placed = true;
  second.speed = 2;
  assert.throws(() => builder.moveItem(second.id, coach.id, first.id, 0), /stopped first/);
  assert.throws(() => builder.flip(coach.id), /stopped first/);
  second.placed = false; second.speed = 0;
  const before = builder.snapshot();
  second.items[0].definition = { ...VEHICLES[3], bogiesByGauge: { 1000: 'not-a-bogie' } };
  assert.throws(() => builder.moveItem(first.id, loco.id, second.id, 0), /compatible gauge/);
  assert.equal(first.items[0], loco); assert.equal(second.items.length, 2);
  builder.restore(before);
});

test('builder snapshots restore separate trains and item orientation without ID reuse or aliasing', () => {
  const builder = new TrainBuilder(), first = builder.create(); builder.add(VEHICLES[0]);
  const second = builder.create(), coach = builder.add(VEHICLES[1]);
  const snapshot = builder.snapshot();
  builder.flip(coach.id); builder.merge(first, second, 'rear'); first.placed = true;
  builder.restore(snapshot);
  assert.equal(builder.trains.length, 2); assert.equal(builder.selected.id, second.id);
  assert.equal(builder.selected.items[0].facing, 1); assert.equal(builder.trains[0].placed, false);
  builder.flip(coach.id); assert.equal(snapshot.trains[1].items[0].facing, 1);
  const extra = builder.create(), item = builder.add(VEHICLES[2]);
  builder.restore(snapshot); assert.ok(builder.create().id > extra.id); assert.ok(builder.add(VEHICLES[0]).id > item.id);
  assert.throws(() => builder.add(VEHICLES[1], -1), /insertion/);
});

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
  train.speed = 2;
  assert.throws(() => builder.remove(train.id, loco.id), /stopped first/);
  assert.throws(() => builder.add(VEHICLES[1]), /stopped first/);
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

test('separate vehicle definitions declare explicit gauge and bogie compatibility', () => {
  assert.equal(VEHICLES.length, 10); assert.equal(new Set(VEHICLES.map(v => v.id)).size, 10);
  assert.deepEqual(VEHICLES.slice(0, 4).map(v => v.kind), ['locomotive', 'coach', 'dome', 'locomotive']);
  for (const def of VEHICLES) {
    assert.deepEqual(Object.keys(def.bogiesByGauge), def.id.startsWith('drgw-') ? ['914', '1435'] : ['1435']);
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

test('TGV power and passenger cars are separate flippable catalog items with different traction and details', () => {
  const power = VEHICLES.find(d => d.id === 'tgv-sud-est-power');
  const coach = VEHICLES.find(d => d.id === 'tgv-sud-est-coach');
  assert.ok(!power.sections && !coach.sections);
  assert.equal(vehicleBogie(power, 1435).powered, true);
  assert.equal(vehicleBogie(coach, 1435).powered, false);
  assert.equal(coach.physics.powerW, 0);
  assert.equal(2 * power.physics.powerW, 6450000);
  assert.equal(2 * power.physics.massKg + 8 * coach.physics.massKg, 385000);
  for (const definition of [power, coach]) assert.throws(() => vehicleBogie(definition, 914), /unavailable/);
  const engine = makeVehicle(power, 1435), passenger = makeVehicle(coach, 1435);
  try {
    assert.equal(engine.body.children.filter(o => o.name === 'windshield').length, 2);
    assert.ok(engine.body.getObjectByName('pantograph'));
    assert.equal(passenger.body.children.filter(o => o.name === 'gangway').length, 2);
    const builder = new TrainBuilder(), train = builder.create();
    builder.add(power); builder.add(coach); const rear = builder.add(power);
    builder.flip(rear.id); assert.equal(rear.facing, -1);
    assert.equal(train.items.length, 3);
  } finally { disposeVehicle(engine); disposeVehicle(passenger); }
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
      for (const section of visual.sections ?? [visual]) for (const bogie of section.definition.runningGear === 'steam' ? [] : section.bogies) {
        assert.equal(bogie.userData.bogieId, section.definition.bogiesByGauge[1435]);
        const wheels = bogie.children.filter(object => object.name === 'wheel');
        assert.equal(wheels.length, (BOGIES[section.definition.bogiesByGauge[1435]].axlePositions?.length ?? 2) * 2);
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
