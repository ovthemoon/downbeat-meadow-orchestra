import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ASPECT_INDEX, BUFFER_LENGTH, HAND_STRIDE, HEADER_LENGTH, TrackerState, createHandBuffer, writeFrame, writeState } from '../WebGLTemplates/Downbeat/js/hand-buffer.js';

const landmarks = (offset) => Array.from({ length: 21 }, (_, i) => ({ x: 0.1 + offset / 100 + i / 1000, y: 0.2 + i / 1000, z: -i - 1 }));

test('layout numbers match HandBufferLayout in Core/HandFrame.cs', () => {
  assert.equal(HEADER_LENGTH, 5);
  assert.equal(HAND_STRIDE, 65);
  assert.equal(ASPECT_INDEX, 135);
  assert.equal(BUFFER_LENGTH, 136);
  assert.deepEqual({ ...TrackerState }, { OFF: 0, STARTING: 1, RUNNING: 2, CAMERA_DENIED: 3, CAMERA_MISSING: 4, CAMERA_ERROR: 5, MODEL_FAILED: 6 });
});

test('a new buffer reads as "off, no frame yet"', () => {
  const buffer = createHandBuffer();
  assert.equal(buffer.length, BUFFER_LENGTH);
  assert.equal(buffer[0], 0);
  assert.equal(buffer[3], TrackerState.OFF);
});

test('writeFrame writes the header, the hand, the aspect, and mirrors x', () => {
  const buffer = createHandBuffer().fill(9);
  writeFrame(buffer, { seq: 7, captureMs: 100.5, inferEndMs: 112.25, aspect: 4 / 3, hands: [{ id: 1, score: 0.9, landmarks: landmarks(0) }] });
  assert.deepEqual(Array.from(buffer.slice(0, 5)), [7, 100.5, 112.25, TrackerState.RUNNING, 1]);
  assert.equal(buffer[HEADER_LENGTH], 1);
  assert.equal(buffer[HEADER_LENGTH + 1], 0.9);
  assert.ok(Math.abs(buffer[HEADER_LENGTH + 2] - (1 - 0.1)) < 1e-12, 'x is mirrored');
  assert.ok(Math.abs(buffer[HEADER_LENGTH + 3] - 0.2) < 1e-12, 'y is not');
  assert.equal(buffer[HEADER_LENGTH + 4], -1, 'z is copied');
  assert.equal(buffer[ASPECT_INDEX], 4 / 3);
  assert.ok(buffer.slice(HEADER_LENGTH + HAND_STRIDE, ASPECT_INDEX).every((v) => v === 0), 'the unused hand slot is cleared');
});

test('mirroring can be switched off and z defaults to 0', () => {
  const buffer = createHandBuffer();
  const points = landmarks(0).map(({ x, y }) => ({ x, y }));
  writeFrame(buffer, { seq: 1, captureMs: 0, inferEndMs: 0, aspect: 1, hands: [{ id: 0, score: 1, landmarks: points }] }, { mirror: false });
  assert.equal(buffer[HEADER_LENGTH + 2], points[0].x);
  assert.equal(buffer[HEADER_LENGTH + 4], 0);
});

test('at most two hands are written, and an empty detection reports zero hands', () => {
  const buffer = createHandBuffer();
  const three = [0, 1, 2].map((k) => ({ id: k, score: 0.5, landmarks: landmarks(k) }));
  writeFrame(buffer, { seq: 1, captureMs: 0, inferEndMs: 0, aspect: 1, hands: three });
  assert.equal(buffer[4], 2);
  writeFrame(buffer, { seq: 2, captureMs: 0, inferEndMs: 0, aspect: 1, hands: [] });
  assert.equal(buffer[4], 0);
  assert.equal(buffer[HEADER_LENGTH + 1], 0);
});

test('writeState changes only the state and leaves the last frame alone', () => {
  const buffer = createHandBuffer();
  writeFrame(buffer, { seq: 5, captureMs: 1, inferEndMs: 2, aspect: 1, hands: [] });
  writeState(buffer, TrackerState.CAMERA_ERROR);
  assert.equal(buffer[3], TrackerState.CAMERA_ERROR);
  assert.equal(buffer[0], 5);
});

test('rejects a buffer that is too small', () => {
  assert.throws(() => writeFrame(new Float64Array(10), { seq: 1, captureMs: 0, inferEndMs: 0, aspect: 1, hands: [] }), RangeError);
});
