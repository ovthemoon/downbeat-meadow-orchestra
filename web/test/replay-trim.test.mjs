import { test } from 'node:test';
import assert from 'node:assert/strict';
import { findRestStretches, trimToLoop } from '../../tools/lib/replay-trim.mjs';

const FRAME_MS = 1000 / 30;
const handAt = (x, y) => ({ id: 0, s: 0.9, p: Array.from({ length: 42 }, (_, i) => (i % 2 === 0 ? x : y)) });

/** rest 1.6 s, move 1 s, rest 1.6 s, move 1 s, rest 1.6 s */
function fakeRecording() {
  const frames = [];
  let t = 0;
  const push = (x, y) => { frames.push({ t, h: [handAt(x, y)] }); t += FRAME_MS; };
  for (let rep = 0; rep < 3; rep++) {
    for (let i = 0; i < 48; i++) push(0.5, 0.45);
    if (rep < 2) for (let i = 0; i < 30; i++) push(0.5 + Math.sin(i / 3) * 0.2, 0.3 + i * 0.01);
  }
  return { format: 'downbeat-hands', version: 1, aspect: 1.3333, mirrored: true, frames };
}

test('long still stretches are found, moving parts are not', () => {
  const stretches = findRestStretches(fakeRecording().frames);
  assert.equal(stretches.length, 3);
  for (const [a, b] of stretches) assert.ok(b - a >= 40);
});

test('a frame without a hand ends a stretch', () => {
  const frames = fakeRecording().frames;
  frames.splice(40, 0, { t: frames[40].t - 1, h: [] });
  assert.equal(findRestStretches(frames).length, 3);
  assert.ok(findRestStretches(frames)[0][1] < 40);
});

test('trimToLoop returns exactly the frames between the middles of two rest stretches, re-timed from zero', () => {
  const trimmed = trimToLoop(fakeRecording());
  assert.equal(trimmed.frames[0].t, 0);
  assert.equal(trimmed.frames.length, 24 + 30 + 24);
  assert.equal(trimmed.version, 1);
});

test('too few rest stretches is an error', () => {
  const frames = fakeRecording().frames.slice(0, 60);
  assert.throws(() => trimToLoop({ frames }), /need 2/);
});
