import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ClockWatcher, audioTimeToPerfMs, heardAudioTime } from '../WebGLTemplates/Downbeat/js/audio-clock.js';
import { createFakeContext } from './fake-audio-context.mjs';

test('heard time extrapolates the output timestamp to the current page time', () => {
  const ctx = createFakeContext({ timestamp: { contextTime: 9.8, performanceTime: 5000 } });
  assert.ok(Math.abs(heardAudioTime(ctx, 5000) - 9.8) < 1e-12);
  assert.ok(Math.abs(heardAudioTime(ctx, 5100) - 9.9) < 1e-12);
});

test('without an output timestamp the reported output latency is subtracted from currentTime', () => {
  const ctx = createFakeContext({ currentTime: 20, outputLatency: 0.2, timestamp: { contextTime: 0, performanceTime: 0 } });
  assert.ok(Math.abs(heardAudioTime(ctx, 123) - 19.8) < 1e-12);
  const noLatency = createFakeContext({ currentTime: 20, outputLatency: 0, timestamp: { contextTime: 0, performanceTime: 0 } });
  assert.ok(Math.abs(heardAudioTime(noLatency, 1) - (20 - noLatency.baseLatency)) < 1e-12);
});

test('a context without getOutputTimestamp still works', () => {
  const ctx = createFakeContext({ currentTime: 5, outputLatency: 0.1 });
  delete ctx.getOutputTimestamp;
  assert.ok(Math.abs(heardAudioTime(ctx, 0) - 4.9) < 1e-12);
});

test('audioTimeToPerfMs is the inverse of heardAudioTime', () => {
  const ctx = createFakeContext({ timestamp: { contextTime: 9.8, performanceTime: 5000 } });
  const perf = audioTimeToPerfMs(ctx, 5100, 10.1); // 0.2 s after what is heard now (9.9)
  assert.ok(Math.abs(perf - 5300) < 1e-9);
});

test('the clock watcher reports advancing only after the clock really moved while running', () => {
  const watcher = new ClockWatcher({ minAdvanceSec: 0.25 });
  assert.equal(watcher.observe(0.0058, true), false);
  assert.equal(watcher.observe(0.0116, true), false); // stuck near zero (Day 0 observation)
  assert.equal(watcher.observe(0.3, true), true);
});

test('the clock watcher starts over when the context stops running', () => {
  const watcher = new ClockWatcher({ minAdvanceSec: 0.25 });
  watcher.observe(1, true);
  assert.equal(watcher.observe(2, true), true);
  assert.equal(watcher.observe(2.1, false), false);
  assert.equal(watcher.observe(2.2, true), false); // new base
});
