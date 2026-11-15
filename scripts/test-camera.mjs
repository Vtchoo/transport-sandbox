import assert from 'node:assert/strict';
import { test } from 'node:test';
import { transformSync } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { Vector3 } from 'three';

const source = (await readFile(new URL('../src/camera.ts', import.meta.url), 'utf8'))
  .replace("'three'", JSON.stringify(import.meta.resolve('three')));
const code = transformSync(source, { loader: 'ts', format: 'esm' }).code;
const { CameraRig } = await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
const flat = () => 100;
const loaded = () => true;
test('train following tracks absolute coordinates while preserving zoom and orientation in both camera modes', () => {
  for(const mode of ['overlook','free']) {
    const rig=new CameraRig();rig.reset(0,0,flat);rig.setMode(mode,flat);
    const first=new Vector3(100000,120,-250000),next=first.clone().add(new Vector3(25,5,10));
    const yaw=rig.yaw,pitch=rig.pitch,distance=rig.distance;rig.followTrain(7,first,flat);const before=rig.position.clone();
    rig.updateFollow(next,flat);assert.equal(rig.following,7);assert.ok(rig.target.distanceTo(next)<1e-6);
    assert.ok(rig.position.clone().sub(before).distanceTo(next.clone().sub(first))<1e-6);assert.equal(rig.yaw,yaw);assert.equal(rig.pitch,pitch);assert.equal(rig.distance,distance);
    rig.look(10,5,flat);rig.scroll(10,flat);assert.equal(rig.following,7);
  }
});
test('manual mouse or keyboard translation cancels following even when terrain blocks panning', () => {
  const rig=new CameraRig();rig.reset(0,0,flat);const point=new Vector3(0,100,0);
  rig.followTrain(1,point,flat);rig.pan(1,0,flat);assert.equal(rig.following,undefined);
  for(const key of ['KeyW','KeyA','KeyS','KeyD','ArrowUp','ArrowDown','ArrowLeft','ArrowRight']) {
    rig.followTrain(1,point,flat);rig.update(new Set([key]),.01,flat,()=>false);assert.equal(rig.following,undefined);
  }
  rig.followTrain(1,point,flat);rig.update(new Set(['KeyQ']),.01,flat,loaded);assert.equal(rig.following,1);
  rig.setMode('free',flat);rig.update(new Set(['KeyE']),.01,flat,loaded);assert.equal(rig.following,undefined);
  rig.followTrain(2,point,flat);rig.reset(0,0,flat);assert.equal(rig.following,undefined);
});

test('overlook starts above a ground focus and pans along the map', () => {
  const rig = new CameraRig();
  rig.reset(500, -1000, flat);
  assert.equal(rig.mode, 'overlook');
  assert.ok(rig.pitch < 0 && rig.position.y > 100);
  assert.ok(Math.abs(rig.position.distanceTo(rig.target) - rig.distance) < 1e-8);
  const target = rig.target.clone();
  rig.update(new Set(['KeyW']), 0.1, flat, loaded);
  assert.ok(rig.target.distanceTo(target) > 1);
  assert.equal(rig.target.y, 100);
});
test('switching camera modes preserves the eye position and viewing direction', () => {
  const rig = new CameraRig();
  rig.reset(0, 0, flat);
  const eye = rig.position.clone(), pitch = rig.pitch, yaw = rig.yaw;
  rig.setMode('free', flat);
  assert.ok(rig.position.distanceTo(eye) < 1e-8);
  assert.equal(rig.pitch, pitch); assert.equal(rig.yaw, yaw);
  rig.setMode('overlook', flat);
  assert.ok(rig.position.distanceTo(eye) < 1e-8);
  assert.ok(Math.abs(rig.target.y - 100) < 0.001);
  assert.ok(Math.abs(rig.distance - 1100) < 0.001);
});
test('overlook zoom/orbit remain bounded and terrain clearance works in both modes', () => {
  const rig = new CameraRig(); rig.reset(0, 0, flat);
  for (let i = 0; i < 100; i++) rig.scroll(-200, flat);
  assert.equal(rig.distance, 50);
  for (let i = 0; i < 100; i++) rig.scroll(200, flat);
  assert.equal(rig.distance, 3500);
  rig.look(20, -100000, flat);
  assert.ok(rig.pitch <= -0.25);
  rig.update(new Set(), 0.1, () => 5000, loaded);
  assert.ok(rig.position.y >= 5012);
  rig.setMode('free', flat); rig.position.y = 0;
  rig.update(new Set(), 0.1, flat, loaded);
  assert.ok(rig.position.y >= 108);
});
test('free camera retains altitude controls; unloaded terrain blocks movement', () => {
  const rig = new CameraRig(); rig.mode = 'free'; rig.reset(0, 0, flat);
  const before = rig.position.clone();
  rig.update(new Set(['KeyE']), 0.5, flat, loaded);
  assert.equal(rig.position.y, before.y + 30);
  rig.update(new Set(['KeyW']), 0.5, flat, () => false);
  assert.equal(rig.position.x, before.x); assert.equal(rig.position.z, before.z);
  rig.update(new Set(['KeyW', 'ShiftLeft']), 0.5, flat, loaded);
  assert.ok(Math.abs(Math.hypot(rig.position.x - before.x, rig.position.z - before.z) - 150) < 1e-8);
});
