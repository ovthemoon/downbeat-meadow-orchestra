import { test } from 'node:test';
import assert from 'node:assert/strict';
import { InputMode, parseInputMode, wantsCamera } from '../WebGLTemplates/Downbeat/js/input-mode.js';

test('?input= is parsed like InputModeParser in Core: mouse and replay are explicit, everything else is the camera', () => {
  assert.equal(parseInputMode('mouse'), InputMode.MOUSE);
  assert.equal(parseInputMode('MOUSE'), InputMode.MOUSE);
  assert.equal(parseInputMode('replay'), InputMode.REPLAY);
  assert.equal(parseInputMode('hand'), InputMode.HAND);
  assert.equal(parseInputMode('camera'), InputMode.HAND);
  assert.equal(parseInputMode(''), InputMode.HAND);
  assert.equal(parseInputMode(null), InputMode.HAND);
  assert.equal(parseInputMode('nonsense'), InputMode.HAND);
});

test('only the camera mode asks for camera permission', () => {
  assert.equal(wantsCamera(InputMode.HAND), true);
  assert.equal(wantsCamera(InputMode.MOUSE), false);
  assert.equal(wantsCamera(InputMode.REPLAY), false);
});
