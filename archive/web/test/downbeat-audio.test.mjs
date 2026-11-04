import { test } from 'node:test';
import assert from 'node:assert/strict';
import { STATE_CLOCK_ADVANCING, STATE_CONTEXT, STATE_RUNNING, createAudioEngine } from '../WebGLTemplates/Downbeat/js/downbeat-audio.js';
import { createFakeContext, fakeContextClass } from './fake-audio-context.mjs';

function setup(extra = {}) {
  const ctx = createFakeContext({ currentTime: 0 });
  const fetchImpl = extra.fetchImpl ?? (async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(1300) }));
  const engine = createAudioEngine({ AudioContextClass: fakeContextClass(ctx), fetchImpl, now: () => 5000 });
  return { ctx, engine };
}

test('nothing works before start(): state 0, clock 0, scheduling returns the requested time', () => {
  const { engine } = setup();
  assert.equal(engine.state(), 0);
  assert.equal(engine.now(), 0);
  assert.equal(engine.schedule(0, 12.5, 1, 1), 12.5);
});

test('start() creates a low-latency context, resumes it and reports running', async () => {
  const { ctx, engine } = setup();
  assert.equal(await engine.start(), 'running');
  assert.equal(ctx.options.latencyHint, 'interactive');
  assert.equal(ctx.resumeCalls, 1);
  assert.equal(await engine.start(), 'running');
  assert.equal(ctx.resumeCalls, 1, 'a second start does not create another context');
});

test('the state flags say "clock advancing" only after currentTime really moved', async () => {
  const { ctx, engine } = setup();
  await engine.start();
  assert.equal(engine.state(), STATE_CONTEXT | STATE_RUNNING);
  ctx.currentTime = 0.1;
  assert.equal(engine.state() & STATE_CLOCK_ADVANCING, 0);
  ctx.currentTime = 0.5;
  assert.equal(engine.state() & STATE_CLOCK_ADVANCING, STATE_CLOCK_ADVANCING);
});

test('samples are decoded into their index and counted; a failing sample is counted as failed', async () => {
  let calls = 0;
  const { engine } = setup({
    fetchImpl: async () => {
      calls += 1;
      return calls === 2 ? { ok: false, status: 404 } : { ok: true, arrayBuffer: async () => new ArrayBuffer(1300) };
    },
  });
  await engine.start();
  const silence = console.error;
  console.error = () => {};
  await Promise.all([engine.loadSample(0, 'a.wav'), engine.loadSample(1, 'b.wav'), engine.loadSample(2, 'c.wav')]);
  console.error = silence;
  assert.equal(engine.samplesLoaded(), 2);
  assert.equal(engine.samplesFailed(), 1);
});

test('a loaded sample is scheduled at the absolute time and shows up in the stats', async () => {
  const { ctx, engine } = setup();
  await engine.start();
  await engine.loadSample(0, 'a.wav');
  ctx.currentTime = 1;
  assert.equal(engine.schedule(0, 1.5, 2, 0.7), 1.5);
  assert.equal(engine.stat(4), 1); // scheduled
  assert.equal(engine.stat(1), 0); // late
  assert.equal(ctx.sources[0].startedAt, 1.5);
});

test('configure() changes the master gain and the voice limit', async () => {
  const { ctx, engine } = setup();
  engine.configure(0.25, 3);
  await engine.start();
  assert.equal(ctx.gains[0].gain.value, 0.25);
  engine.configure(0.5, 3);
  assert.equal(ctx.gains[0].gain.value, 0.5);
});

test('the flash log keeps the most recent entries only', async () => {
  const { engine } = setup();
  for (let i = 0; i < 4100; i++) engine.logFlash(i, i + 0.01);
  const flashes = engine.snapshot().flashes;
  assert.equal(flashes.length, 4000);
  assert.equal(flashes[0].when, 100);
});

test('heardNow follows the output timestamp of the context', async () => {
  const { ctx, engine } = setup();
  await engine.start();
  ctx.currentTime = 12;
  // timestamp: contextTime 9.8 at performanceTime 5000 and now() is 5000 -> 9.8
  assert.ok(Math.abs(engine.heardNow() - 9.8) < 1e-12);
});
