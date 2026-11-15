import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
const { outputFiles } = buildSync({ entryPoints: ['src/ui/windows.ts'], bundle: true, write: false, format: 'esm', platform: 'node' });
const { constrainWindow } = await import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString('base64')}`);

test('window bounds keep the whole window and toolbar clearance inside the viewport', () => {
  const viewport = { width: 1440, height: 900 };
  const result = constrainWindow({ x: 2000, y: -300, width: 570, height: 650 }, viewport);
  assert.deepEqual(result, { x: 862, y: 8, width: 570, height: 650 });
  assert.deepEqual(constrainWindow({ x: -50, y: 2000, width: 9000, height: 9000 }, viewport), { x: 8, y: 8, width: 1424, height: 787 });
  assert.deepEqual(constrainWindow({ x: 30, y: 40, width: 1, height: 1 }, viewport), { x: 30, y: 40, width: 260, height: 200 });
});

test('small viewports override window minimums and repeated constraints preserve a valid layout', () => {
  const viewport = { width: 320, height: 300 };
  const result = constrainWindow({ x: 380, y: 85, width: 570, height: 650 }, viewport, { width: 340, height: 430 });
  assert.deepEqual(result, { x: 8, y: 8, width: 304, height: 187 });
  assert.deepEqual(constrainWindow(result, viewport, { width: 340, height: 430 }), result);
});
