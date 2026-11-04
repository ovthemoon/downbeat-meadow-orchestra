import { test } from 'node:test';
import assert from 'node:assert/strict';
import { aheadDelays, ctxToPerfMapper, disturbanceWindow, dspVersusContext, evaluateCriteria, splitByWindow } from './s1-analysis.mjs';

const visible = (perfMs, extra = {}) => ({ type: 'x', perfMs, visibility: 'visible', hasFocus: true, blurred: false, ...extra });

test('disturbanceWindow finds hidden period and the return', () => {
  const events = [visible(0, { type: 'start' }), visible(100, { visibility: 'hidden' }), visible(10100)];
  assert.deepEqual(disturbanceWindow(events), { startMs: 100, endMs: 10100 });
});

test('disturbanceWindow counts a synthetic blur (blurred flag) even when hasFocus stays true', () => {
  const events = [visible(0, { type: 'start' }), visible(50, { type: 'blur', blurred: true }), visible(9050, { type: 'focus', blurred: false })];
  assert.deepEqual(disturbanceWindow(events), { startMs: 50, endMs: 9050 });
});

test('disturbanceWindow ignores hasFocus and events before the run started', () => {
  const events = [visible(10, { hasFocus: false, visibility: 'hidden' }), visible(20, { hasFocus: false })];
  assert.equal(disturbanceWindow(events, { fromPerfMs: 15 }), null);
});

test('disturbanceWindow is null when nothing happened, open-ended when never restored', () => {
  assert.equal(disturbanceWindow([visible(0, { type: 'start' })]), null);
  const stuck = [visible(0, { type: 'start' }), visible(5, { blurred: true })];
  assert.equal(disturbanceWindow(stuck).endMs, Infinity);
});

test('ctxToPerfMapper converts audio time to page time', () => {
  const marks = [{ label: 'beat-start:x', perfMs: 5000, ctxTimes: { 0: 2.0 } }];
  assert.equal(ctxToPerfMapper(marks, 0)(3.5), 6500);
  assert.throws(() => ctxToPerfMapper(marks, 7), /no beat-start mark/);
});

test('splitByWindow partitions without losing points', () => {
  const pts = [{ p: 1 }, { p: 5 }, { p: 9 }];
  const parts = splitByWindow(pts, (x) => x.p, { startMs: 4, endMs: 8 });
  assert.deepEqual([parts.before.length, parts.during.length, parts.after.length], [1, 1, 1]);
  assert.deepEqual(splitByWindow(pts, (x) => x.p, null).all, pts);
});

test('dspVersusContext reports a constant offset with zero drift', () => {
  const samples = Array.from({ length: 10 }, (_, i) => ({ dspSec: i + 0.2, ctxSec: i }));
  const r = dspVersusContext(samples);
  assert.ok(Math.abs(r.medianOffsetSec - 0.2) < 1e-9);
  assert.ok(Math.abs(r.driftMsPerMin) < 1e-6);
});

test('dspVersusContext flags a clock that advances at a different rate', () => {
  const samples = Array.from({ length: 10 }, (_, i) => ({ dspSec: i * 1.001, ctxSec: i }));
  assert.ok(Math.abs(dspVersusContext(samples).driftMsPerMin - 60) < 0.01);
});

test('aheadDelays marks an immediate click', () => {
  const rows = aheadDelays([{ aheadSec: 1, ctx0: 10 }, { aheadSec: 1, ctx0: 20 }], [10.05, 21.02]);
  assert.equal(rows[0].playedImmediately, true);
  assert.equal(rows[1].playedImmediately, false);
  assert.ok(Math.abs(rows[1].errorMs - 20) < 1e-6);
});

test('evaluateCriteria applies the section 6.3 thresholds', () => {
  assert.deepEqual(evaluateCriteria({ stdMs: 2.9, maxAbsMs: 9.9 }, -4.9), { std: true, max: true, drift: true });
  assert.deepEqual(evaluateCriteria({ stdMs: 3.1, maxAbsMs: 10.1 }, 5.1), { std: false, max: false, drift: false });
});
