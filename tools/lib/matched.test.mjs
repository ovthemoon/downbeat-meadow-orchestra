import { test } from 'node:test';
import assert from 'node:assert/strict';
import { locateTemplate } from './matched.mjs';

const SR = 44100;
// Half a period of a 520 Hz tone is 42 frames: a search window of +/- 40 frames cannot lock onto the wrong cycle.
const WINDOW = 40;

function attack(length = 400) {
  return Float32Array.from({ length }, (_, i) => Math.sin((2 * Math.PI * 520 * i) / SR) * Math.exp(-i / 150) * (1 - Math.exp(-i / 6)));
}

function mix(length, starts, template, tailGain = 0.6) {
  const out = new Float32Array(length);
  for (const start of starts) {
    for (let i = 0; i < length - start; i++) {
      const t = i < template.length ? template[i] : tailGain * Math.sin((2 * Math.PI * 520 * i) / SR) * Math.exp(-i / 9000);
      out[start + i] += t;
    }
  }
  return out;
}

test('finds notes at the expected frames with zero lag', () => {
  const t = attack();
  const starts = [5000, 20000, 40000]; // isolated: nothing rings into the next note
  const found = locateTemplate(mix(60000, starts, t, 0), t, starts, WINDOW);
  for (const f of found) {
    assert.ok(Math.abs(f.lagFrames) < 0.3, `lag ${f.lagFrames}`); // parabolic refinement is good to about 0.2 frame (0.005 ms)
    assert.ok(f.correlation > 0.9);
    assert.equal(f.edge, false);
  }
});

test('measures a note that is a few frames late, also when earlier notes are still ringing', () => {
  const t = attack();
  const starts = [5000, 5300, 5900]; // overlapping, each rings into the next
  const expected = [5000, 5300, 5893]; // the last one is 7 frames late
  const found = locateTemplate(mix(40000, starts, t), t, expected, WINDOW);
  // the ringing tails of earlier notes bias the estimate by a few frames (0.1 ms); the lag is still clearly found
  assert.ok(Math.abs(found[2].lagFrames - 7) < 4, `lag ${found[2].lagFrames}`);
  assert.ok(Math.abs(found[0].lagFrames) < 4);
});

test('reports sub-frame lag', () => {
  const t = attack();
  const half = new Float32Array(60000);
  // a note starting half a frame later: average of two integer positions
  const a = mix(60000, [8000], t);
  const b = mix(60000, [8001], t);
  for (let i = 0; i < half.length; i++) half[i] = 0.5 * (a[i] + b[i]);
  const found = locateTemplate(half, t, [8000], WINDOW);
  assert.ok(Math.abs(found[0].lagFrames - 0.5) < 0.3, `lag ${found[0].lagFrames}`);
});

test('flags a note that is missing or outside the window (edge or low correlation)', () => {
  const t = attack();
  const found = locateTemplate(mix(30000, [5000], t), t, [20000], WINDOW);
  assert.ok(found[0].correlation < 0.9, `correlation ${found[0].correlation}`);
});

test('rejects a silent template and handles windows outside the recording', () => {
  assert.throws(() => locateTemplate(new Float32Array(1000), new Float32Array(10), [100], 5), /silent/);
  const t = attack(50);
  const found = locateTemplate(new Float32Array(100).fill(0.1), t, [10000], 5);
  assert.equal(found[0].edge, true);
});
