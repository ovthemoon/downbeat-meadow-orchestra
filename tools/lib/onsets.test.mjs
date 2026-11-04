import { test } from 'node:test';
import assert from 'node:assert/strict';
import { analyzeGrid, computeGridPoints, detectOnsets, pointsAgainstFit, summarizeMs } from './onsets.mjs';

const SR = 48000;

/** Click = 2 kHz sine burst with exponential decay, placed at fractional-free frame positions. */
function renderClicks(frames, lengthFrames, amp = 0.5) {
  const out = new Float32Array(lengthFrames);
  for (const start of frames) {
    for (let i = 0; i < 480 && start + i < lengthFrames; i++) {
      out[start + i] += amp * Math.sin((2 * Math.PI * 2000 * i) / SR) * Math.exp(-i / 120);
    }
  }
  return out;
}

const slotFrames = Math.round((60 / 96 / 4) * SR); // 7500 frames = 156.25 ms

test('detects every click in a clean 96 BPM / 16th-note train', () => {
  const starts = Array.from({ length: 40 }, (_, k) => 4800 + k * slotFrames);
  const { onsets } = detectOnsets(renderClicks(starts, 4800 + 41 * slotFrames), SR);
  assert.equal(onsets.length, 40);
  onsets.forEach((o, k) => assert.ok(Math.abs(o.frame - starts[k]) < 3, `onset ${k} at ${o.frame} vs ${starts[k]}`));
});

test('returns no onsets for silence and for near-silence', () => {
  assert.equal(detectOnsets(new Float32Array(SR), SR).onsets.length, 0);
  assert.equal(detectOnsets(new Float32Array(SR).fill(1e-6), SR).onsets.length, 0);
});

test('separates two clicks only 20 ms apart', () => {
  const { onsets } = detectOnsets(renderClicks([1000, 1000 + 960], 6000), SR);
  assert.equal(onsets.length, 2);
});

test('does not mutate the input samples', () => {
  const samples = renderClicks([1000], 4000);
  const copy = Float32Array.from(samples);
  detectOnsets(samples, SR);
  assert.deepEqual(samples, copy);
});

test('perfect grid gives zero deviation and zero drift', () => {
  const times = Array.from({ length: 100 }, (_, k) => 0.5 + k * 0.15625);
  const r = analyzeGrid(times, 0.15625);
  assert.ok(r.residual.maxAbsMs < 1e-6);
  assert.ok(Math.abs(r.driftMsPerMin) < 1e-6);
  assert.equal(r.slots.missed, 0);
});

test('constant latency is absorbed, linear skew is reported as drift', () => {
  // 0.1 ms of extra spacing per slot => 0.1 ms * (60/0.15625) = 38.4 ms per minute
  const times = Array.from({ length: 400 }, (_, k) => 0.2 + k * (0.15625 + 0.0001));
  const r = analyzeGrid(times, 0.15625);
  assert.ok(Math.abs(r.driftMsPerMin - 38.4) < 0.05, `drift ${r.driftMsPerMin}`);
  assert.ok(r.residual.maxAbsMs < 1e-6);
});

test('quantizes onsets to slots so frame-quantized triggers show their jitter', () => {
  const frameSec = 1 / 60;
  const slot = 0.15625;
  const times = Array.from({ length: 200 }, (_, k) => Math.ceil((k * slot) / frameSec) * frameSec);
  const r = analyzeGrid(times, slot);
  assert.ok(r.residual.maxAbsMs > 5 && r.residual.maxAbsMs < 17);
});

test('reports missed and duplicated slots', () => {
  const slot = 0.15625;
  const times = [0, 1, 2, 4, 5, 5.001, 6].map((k) => k * slot);
  const { points } = computeGridPoints(times, slot);
  assert.equal(points.length, 7);
  const r = analyzeGrid(times, slot);
  assert.equal(r.slots.missed, 1); // slot 3 missing
  assert.equal(r.slots.duplicates, 1); // slot 5 hit twice
});

test('summarizeMs uses population std and interpolated p95', () => {
  const s = summarizeMs([1, -1, 1, -1]);
  assert.equal(s.stdMs, 1);
  assert.equal(s.maxAbsMs, 1);
  assert.equal(summarizeMs([]).count, 0);
});

test('rejects degenerate input', () => {
  assert.throws(() => computeGridPoints([0], 0.1), /two onsets/);
  assert.throws(() => computeGridPoints([0, 1], 0), /positive/);
});

test('pointsAgainstFit exposes a phase jump that a whole-run fit would partly absorb', () => {
  const slot = 0.15625;
  const before = Array.from({ length: 100 }, (_, k) => 1 + k * slot);
  const fit = analyzeGrid(before, slot);
  // After a stall the scheduler resumed 40 ms late on the same slot numbering.
  const after = Array.from({ length: 20 }, (_, k) => 1 + (120 + k) * slot + 0.04);
  const points = pointsAgainstFit([...before, ...after], before[0], slot, fit);
  assert.ok(points.slice(0, 100).every((p) => Math.abs(p.residualMs) < 1e-6));
  assert.ok(points.slice(100).every((p) => Math.abs(p.residualMs - 40) < 1e-6));
  assert.equal(points[100].slot, 120);
});

test('detects a click whose first sample sits between the re-arm level and the threshold', () => {
  // Regression: a sub-sample-shifted click (Web Audio interpolates start times) begins 0.0394, 0.2666, ...
  const out = new Float32Array(9000);
  [0.0394, 0.2666, 0.4646, 0.5607, 0.568, 0.4639, 0.2854, 0.0543, -0.1807].forEach((v, i) => { out[6000 + i] = v; });
  const { onsets } = detectOnsets(out, SR);
  assert.equal(onsets.length, 1);
  assert.ok(onsets[0].frame > 6000 && onsets[0].frame < 6002);
});

test('a second click arriving while the first still rings is not lost after the first re-arms', () => {
  const { onsets } = detectOnsets(renderClicks([1000, 1000 + 2400, 1000 + 4800], 12000), SR);
  assert.equal(onsets.length, 3);
});

test('a drift larger than half a slot over the run is measured, not wrapped away', () => {
  // clicks come 0.5 ms early per slot: after 400 slots (62.5 s) the error is -200 ms, more than a whole slot
  const times = Array.from({ length: 400 }, (_, k) => 1 + k * (0.15625 - 0.0005));
  const r = analyzeGrid(times, 0.15625);
  assert.ok(Math.abs(r.driftMsPerMin - (-0.5 * 60 / 0.15575)) < 0.01, `drift ${r.driftMsPerMin}`);
  assert.ok(r.residual.maxAbsMs < 1e-6);
  assert.equal(r.slots.missed, 0);
});

test('pointsAgainstFit follows a fitted drift across a gap of missing slots', () => {
  const slot = 0.15625;
  const drift = -0.0005; // seconds per slot
  const all = Array.from({ length: 300 }, (_, k) => 1 + k * (slot + drift));
  const quiet = all.filter((_, k) => k < 100 || k >= 160); // slots 100..159 were never played
  const fit = analyzeGrid(all.slice(0, 100), slot);
  const points = pointsAgainstFit(quiet, quiet[0], slot, fit);
  assert.ok(points.every((p) => Math.abs(p.residualMs) < 0.01), JSON.stringify(points.map((p) => p.residualMs).filter((v) => Math.abs(v) >= 0.01).slice(0, 3)));
  assert.equal(points[100].slot, 160);
});
