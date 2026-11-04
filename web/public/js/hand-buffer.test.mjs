import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BUFFER_LENGTH, HAND_STRIDE, HEADER_LENGTH, writeFrame } from './hand-buffer.js';

const landmarks = (offset) => Array.from({ length: 21 }, (_, i) => ({ x: offset + i, y: offset + i + 0.25, z: -i }));

test('buffer length is 135 (5 header + 2 hands x 65)', () => {
  assert.equal(HAND_STRIDE, 65);
  assert.equal(BUFFER_LENGTH, 135);
});

test('writes header and one hand, zeroes the unused hand slot', () => {
  const buf = new Float64Array(BUFFER_LENGTH).fill(9);
  writeFrame(buf, { seq: 7, frameMs: 100.5, inferEndMs: 112.25, hands: [{ score: 0.9, landmarks: landmarks(1) }] });
  assert.deepEqual(Array.from(buf.slice(0, 5)), [7, 100.5, 112.25, 1, 1]);
  assert.equal(buf[HEADER_LENGTH + 1], 0.9);
  assert.equal(buf[HEADER_LENGTH + 2], 1);
  assert.equal(buf[HEADER_LENGTH + 2 + 20 * 3 + 1], 21.25);
  assert.ok(buf.slice(HEADER_LENGTH + HAND_STRIDE).every((v) => v === 0));
});

test('caps at two hands and reports zero hands for an empty detection', () => {
  const buf = new Float64Array(BUFFER_LENGTH);
  const three = [0, 1, 2].map((k) => ({ score: 0.5, landmarks: landmarks(k) }));
  writeFrame(buf, { seq: 1, frameMs: 0, inferEndMs: 0, hands: three });
  assert.equal(buf[4], 2);
  writeFrame(buf, { seq: 2, frameMs: 0, inferEndMs: 0, hands: [] });
  assert.equal(buf[4], 0);
  assert.equal(buf[HEADER_LENGTH + 1], 0);
});

test('rejects a buffer that is too small', () => {
  assert.throws(() => writeFrame(new Float64Array(10), { seq: 1, frameMs: 0, inferEndMs: 0, hands: [] }), RangeError);
});
