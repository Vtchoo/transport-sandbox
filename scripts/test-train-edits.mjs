import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
import * as THREE from 'three';
const load = async entry => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
  const code = outputFiles[0].text.replaceAll('"three"', JSON.stringify(import.meta.resolve('three')));
  return import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
};
const { TrainEditor } = await load('src/train/editor.ts');
const { TrackNetwork } = await load('src/track/model.ts');
const { VEHICLES } = await load('src/train/catalog.ts');
const { catalogAllows } = await load('src/train/catalog-routing.ts');

function fixture(run, length = 400) {
  const prior = globalThis.document, elements = new Map();
  globalThis.document = { getElementById: id => { if (!elements.has(id)) elements.set(id, { hidden: true, classList: { toggle() {} } }); return elements.get(id); }, createElement: () => ({ getContext: () => null }) };
  const network = new TrackNetwork(), a = network.addNode(new THREE.Vector3(-length / 2, 0, 0), 1435), b = network.addNode(new THREE.Vector3(length / 2, 0, 0), 1435), edge = network.connect(a, b, 1435);
  const editor = new TrainEditor(new THREE.Scene(), new THREE.PerspectiveCamera(), { classList: { remove() {}, toggle() {} } }, { origin: new THREE.Vector2() }, network);
  const warnings = []; editor.onWarning = text => warnings.push(text);
  const park = (definitions, x = 0, facing = 1) => {
    const train = editor.builder.create(); definitions.forEach(def => editor.builder.add(def));
    const position = { edge: edge.id, u: (x + length / 2) / length, facing };
    const visuals = editor.createVisuals(train, position); train.placed = true;
    editor.parked.push({ train, visuals, position, supported: true }); return train;
  };
  try { run({ editor, network, edge, park, warnings }); }
  finally { editor.dispose(); if (prior === undefined) delete globalThis.document; else globalThis.document = prior; }
}

test('stopped trains append in place in either direction, preserving existing car positions and undo', () => {
  for (const facing of [1, -1]) fixture(({ editor, park, warnings }) => {
    const train = park([VEHICLES[0], VEHICLES[1]], 20, facing), before = editor.parked[0].visuals.map(visual => visual.group.position.clone());
    assert.equal(editor.addVehicle('atsf-pleasure-dome', train.id), true);
    assert.equal(train.placed, true); assert.equal(editor.parked[0].visuals.length, 3); assert.deepEqual(warnings, []);
    before.forEach((position, i) => assert.ok(position.distanceTo(editor.parked[0].visuals[i].group.position) < 1e-6));
    editor.undo(); assert.equal(editor.builder.selected.placed, true); assert.equal(editor.parked[0].visuals.length, 2);
    before.forEach((position, i) => assert.ok(position.distanceTo(editor.parked[0].visuals[i].group.position) < 1e-6));
  });
});

test('moving trains reject additions without lifting, changing consist or creating undo entries', () => fixture(({ editor, park, warnings }) => {
  const train = park([VEHICLES[0]]); train.speed = -2;
  const before = editor.parked[0].visuals[0].group.position.clone();
  assert.equal(editor.addVehicle('atsf-coach', train.id), false);
  assert.equal(editor.builder.selected.items.length, 1); assert.equal(editor.builder.selected.placed, true); assert.equal(editor.builder.selected.speed, -2);
  assert.equal(editor.canUndo, false); assert.deepEqual(warnings, ['Train must be stopped first.']);
  assert.ok(before.distanceTo(editor.parked[0].visuals[0].group.position) < 1e-6);
}));

test('insufficient track lifts the edited consist, with a warning, and undo restores its original pose', () => fixture(({ editor, park, warnings }) => {
  const train = park([VEHICLES[0]], -18), before = editor.parked[0].visuals[0].group.position.clone();
  assert.equal(editor.addVehicle('atsf-coach', train.id), true);
  assert.equal(train.placed, false); assert.equal(train.items.length, 2); assert.equal(editor.parked.length, 0);
  assert.match(warnings[0], /Not enough free track/);
  editor.undo(); assert.equal(editor.builder.selected.placed, true); assert.equal(editor.builder.selected.items.length, 1);
  assert.ok(before.distanceTo(editor.parked[0].visuals[0].group.position) < 1e-6);
}, 60));

test('a parked train occupying the added car space triggers repositioning without moving the other train', () => fixture(({ editor, park, warnings }) => {
  const train = park([VEHICLES[0]], 0), obstacle = park([VEHICLES[0]], -34), other = editor.parked[1].visuals[0].group.position.clone();
  assert.equal(editor.addVehicle('atsf-coach', train.id), true);
  assert.equal(train.placed, false); assert.equal(obstacle.placed, true); assert.equal(editor.parked.length, 1);
  assert.ok(other.distanceTo(editor.parked[0].visuals[0].group.position) < 1e-6); assert.match(warnings[0], /Not enough free track/);
}));

test('flipping and removing the leading car keep stopped trains on track and preserve surviving positions', () => fixture(({ editor, park, warnings }) => {
  const train = park([VEHICLES[0], VEHICLES[1]]), coach = editor.parked[0].visuals[1].group.position.clone();
  assert.equal(editor.editConsist('Flip', () => editor.builder.flip(train.items[0].id)), true);
  assert.equal(train.placed, true); assert.equal(train.items[0].facing, -1);
  assert.equal(editor.editConsist('Remove', () => editor.builder.remove(train.id, train.items[0].id)), true);
  assert.equal(train.placed, true); assert.ok(coach.distanceTo(editor.parked[0].visuals[0].group.position) < 1e-6); assert.deepEqual(warnings, []);
  editor.editConsist('Remove final car', () => editor.builder.remove(train.id, train.items[0].id));
  assert.equal(editor.parked.length, 0); assert.equal(train.placed, false);
}));

test('transfers between two stopped trains refit both while keeping original surviving cars stationary', () => fixture(({ editor, park, warnings }) => {
  const source = park([VEHICLES[0], VEHICLES[1]], 70), target = park([VEHICLES[0]], -70);
  const positions = editor.parked.map(parked => parked.visuals[0].group.position.clone());
  assert.equal(editor.editConsist('Transfer', () => editor.builder.moveItem(source.id, source.items[1].id, target.id, target.items.length)), true);
  assert.equal(source.placed, true); assert.equal(target.placed, true); assert.equal(source.items.length, 1); assert.equal(target.items.length, 2);
  editor.parked.forEach((parked, i) => assert.ok(positions[i].distanceTo(parked.visuals[0].group.position) < 1e-6)); assert.deepEqual(warnings, []);
}));

test('placed train catalog filtering honors its actual track gauge', () => {
  const train = { id: 1, name: 'Train', speed: 0, placed: true, items: [{ id: 1, definition: VEHICLES[0], facing: 1 }] };
  assert.equal(catalogAllows(VEHICLES[1], train, 1435), true);
  assert.equal(catalogAllows(VEHICLES[1], train, 1000), false);
});
