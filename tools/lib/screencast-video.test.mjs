import { test } from 'node:test';
import assert from 'node:assert/strict';
import { planFrames } from './screencast-video.mjs';

const frame = (epochSec) => ({ epochSec });

test('each frame is shown until the next one and the durations add up to the window', () => {
  const plan = planFrames([frame(10), frame(10.5), frame(10.6), frame(12)], 10.2, 11.9);
  assert.equal(plan[0].index, 0);
  assert.ok(Math.abs(plan.reduce((s, p) => s + p.durationSec, 0) - 1.7) < 1e-9);
  assert.ok(Math.abs(plan[0].durationSec - 0.3) < 1e-9); // clipped to the window start
});

test('frames after the window are ignored and the last one is held to the window end', () => {
  const plan = planFrames([frame(5), frame(6), frame(99)], 5.5, 7);
  assert.equal(plan.length, 2);
  assert.ok(Math.abs(plan[1].durationSec - 1.0) < 1e-9);
});

test('when the window starts before the first frame, the first frame is held back to the window start (no timeline shift)', () => {
  const plan = planFrames([frame(10), frame(11)], 9, 11.5);
  assert.ok(Math.abs(plan[0].durationSec - 2) < 1e-9); // 9 .. 11
  assert.ok(Math.abs(plan.reduce((s, p) => s + p.durationSec, 0) - 2.5) < 1e-9);
});
