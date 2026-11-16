import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
import * as THREE from 'three';
const load = async entry => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
  const code = outputFiles[0].text.replace(/"three(?:\/[^\"]*)?"/g, spec => JSON.stringify(import.meta.resolve(JSON.parse(spec))));
  return import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'));
};
const { TrainSmoke } = await load('src/train/smoke.ts');
const { VEHICLES } = await load('src/train/catalog.ts');
const { makeVehicle, disposeVehicle } = await load('src/train/render.ts');
const { physicsStep, trainPerformance } = await load('src/train/physics.ts');
const locomotive = VEHICLES.find(def => def.id === 'drgw-consolidation');
const coach = VEHICLES.find(def => def.id === 'drgw-wood-coach');

test('traction effort distinguishes acceleration, hill load, downhill coasting and brakes in both modes', () => {
  for (const mode of ['realistic', 'ho']) {
    const definitions = [locomotive, coach, coach];
    const top = trainPerformance(definitions, mode).maxSpeed;
    const cruise = physicsStep(definitions, top, true, 0, mode, .05);
    assert.ok(physicsStep(definitions, 0, true, 0, mode, .05).effort > .95);
    assert.ok(cruise.effort > 0 && cruise.effort < .3);
    assert.ok(physicsStep(definitions, top, true, .025, mode, .05).effort > cruise.effort);
    assert.equal(physicsStep(definitions, top, true, -.04, mode, .05).effort, 0);
    assert.equal(physicsStep(definitions, 5, false, .02, mode, .05).effort, 0);
    assert.equal(physicsStep(definitions, 5, true, 0, mode, .05, 2).effort, 0);
    assert.equal(physicsStep(definitions, 0, true, 0, mode, .05, 0).effort, 0);
    assert.equal(physicsStep([coach], 0, true, 0, mode, .05).effort, 0);
  }
});

test('smoke originates at the articulated engine chimney, preserves floating-origin precision and fades', () => {
  for (const gauge of [914, 1435]) {
    const visual = makeVehicle(locomotive, gauge), smoke = new TrainSmoke();
    const scene = new THREE.Scene(), parent = new THREE.Group(), camera = new THREE.PerspectiveCamera();
    const origin = new THREE.Vector2(1000000, -2000000);
    parent.position.set(-origin.x, 0, -origin.y); scene.add(parent, smoke.points); parent.add(visual.group);
    visual.group.position.set(origin.x + 10, 0, origin.y + 20); visual.group.rotation.y = Math.PI;
    visual.sections[0].body.rotation.y = .3;
    camera.position.set(30, 20, 40);
    try {
      for (let i = 0; i < 20; i++) smoke.update(.05, [{ visual, effort: 0 }], origin, camera);
      assert.equal(smoke.count, 0);
      const outlet = visual.sections[0].body.localToWorld(new THREE.Vector3(...visual.sections[0].definition.exhaust.position));
      for (let i = 0; i < 8; i++) smoke.update(.05, [{ visual, effort: 1 }], origin, camera);
      assert.ok(smoke.count > 0 && smoke.points.visible);
      const attribute = smoke.points.geometry.attributes.position;
      for (let i = 0; i < smoke.count; i++) {
        const point = new THREE.Vector3().fromBufferAttribute(attribute, i);
        assert.ok(point.distanceTo(outlet) < 4, 'Exhaust must start at the chimney, not the tender or item center');
      }
      const before = Array.from(attribute.array.slice(0, smoke.count * 3));
      origin.x += 512; parent.position.x -= 512; camera.position.x -= 512;
      smoke.update(0, [{ visual, effort: 1 }], origin, camera);
      for (let i = 0; i < before.length; i++) assert.ok(Math.abs(attribute.array[i] - before[i] - (i % 3 === 0 ? -512 : 0)) < .0001);
      for (let i = 0; i < 60; i++) smoke.update(.1, [], origin, camera);
      assert.equal(smoke.count, 0); assert.equal(smoke.points.visible, false);
      assert.equal(smoke.points.geometry.drawRange.count, 0);
    } finally { smoke.dispose(); disposeVehicle(visual); }
  }
});

test('the smoke budget is bounded, distant engines and unpowered cars do not emit, and resources release once', () => {
  const smoke = new TrainSmoke(16), visual = makeVehicle(locomotive, 914), passenger = makeVehicle(coach, 914);
  const origin = new THREE.Vector2(), camera = new THREE.PerspectiveCamera();
  try {
    for (let i = 0; i < 20; i++) smoke.update(.1, [{ visual: passenger, effort: 1 }], origin, camera);
    assert.equal(smoke.count, 0);
    visual.group.position.x = 10000;
    for (let i = 0; i < 20; i++) smoke.update(.1, [{ visual, effort: 1 }], origin, camera);
    assert.equal(smoke.count, 0);
    visual.group.position.x = 0;
    for (let i = 0; i < 80; i++) smoke.update(.1, [{ visual, effort: 1 }], origin, camera);
    assert.equal(smoke.count, 16); assert.equal(smoke.points.geometry.drawRange.count, 16);
    assert.ok(smoke.points.geometry.attributes.position.array.every(Number.isFinite));
    let geometry = 0, material = 0;
    smoke.points.geometry.addEventListener('dispose', () => geometry++);
    smoke.points.material.addEventListener('dispose', () => material++);
    smoke.dispose(); smoke.dispose();
    assert.equal(geometry, 1); assert.equal(material, 1); assert.equal(smoke.count, 0);
  } finally { smoke.dispose(); disposeVehicle(visual); disposeVehicle(passenger); }
});
