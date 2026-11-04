import { test } from 'node:test';
import assert from 'node:assert/strict';
import { NotePlayer } from '../WebGLTemplates/Downbeat/js/note-player.js';
import { buffer, createFakeContext } from './fake-audio-context.mjs';

function setup(overrides = {}) {
  const ctx = createFakeContext({ currentTime: 10 });
  const buffers = [buffer(1.3), buffer(0.5)];
  const player = new NotePlayer({ ctx, destination: ctx.destination, buffers, ...overrides });
  return { ctx, player };
}

test('a note with enough lead starts exactly at the requested audio time', () => {
  const { ctx, player } = setup();
  const start = player.play({ sampleIndex: 0, when: 10.25, rate: 1.5, gain: 0.8 });
  assert.equal(start, 10.25);
  assert.equal(ctx.sources[0].startedAt, 10.25);
  assert.equal(ctx.sources[0].playbackRate.value, 1.5);
  assert.equal(ctx.gains[0].gain.value, 0.8);
  assert.equal(ctx.sources[0].buffer.duration, 1.3);
  assert.equal(player.stats.late, 0);
});

test('a late request starts as soon as possible and is counted with how late it was', () => {
  const { ctx, player } = setup({ minLeadSec: 0.005 });
  const start = player.play({ sampleIndex: 0, when: 9.9, rate: 1, gain: 1 });
  assert.ok(Math.abs(start - 10.005) < 1e-12);
  assert.equal(player.stats.late, 1);
  assert.ok(Math.abs(player.stats.maxLateMs - 105) < 1e-6);
  assert.equal(ctx.sources[0].startedAt, start);
});

test('a missing sample or invalid numbers are counted, nothing is scheduled, and the requested time is returned', () => {
  const { ctx, player } = setup();
  assert.equal(player.play({ sampleIndex: 7, when: 11, rate: 1, gain: 1 }), 11);
  assert.equal(player.play({ sampleIndex: 0, when: Number.NaN, rate: 1, gain: 1 }), Number.NaN);
  assert.equal(player.play({ sampleIndex: 0, when: 11, rate: 0, gain: 1 }), 11);
  assert.equal(player.stats.missingSample, 3);
  assert.equal(ctx.sources.length, 0);
});

test('when the voice limit is reached the oldest voice is faded out and stopped', () => {
  const { ctx, player } = setup({ maxVoices: 2 });
  player.play({ sampleIndex: 0, when: 10.1, rate: 1, gain: 1 });
  player.play({ sampleIndex: 0, when: 10.2, rate: 1, gain: 1 });
  player.play({ sampleIndex: 0, when: 10.3, rate: 1, gain: 1 });
  assert.equal(player.stats.stolen, 1);
  assert.equal(player.activeVoices, 2);
  assert.ok(ctx.sources[0].stoppedAt > 10 && ctx.sources[0].stoppedAt < 10.1);
  assert.equal(ctx.gains[0].gain.targets[0][0], 0);
});

test('finished voices free their slot and are disconnected', () => {
  const { ctx, player } = setup({ maxVoices: 1 });
  player.play({ sampleIndex: 1, when: 10.1, rate: 1, gain: 1 });
  ctx.sources[0].onended();
  assert.equal(ctx.sources[0].disconnected, true);
  player.play({ sampleIndex: 1, when: 10.3, rate: 1, gain: 1 });
  assert.equal(player.stats.stolen, 0);
});

test('a voice whose playback is over by now is not counted against the limit', () => {
  const { ctx, player } = setup({ maxVoices: 1 });
  player.play({ sampleIndex: 1, when: 10.1, rate: 1, gain: 1 }); // 0.5 s long -> ends at 10.6
  ctx.currentTime = 11;
  player.play({ sampleIndex: 1, when: 11.2, rate: 1, gain: 1 });
  assert.equal(player.stats.stolen, 0);
});

test('a pitched-down sample lasts longer: its end time uses the playback rate', () => {
  const { ctx, player } = setup({ maxVoices: 1 });
  player.play({ sampleIndex: 1, when: 10.1, rate: 0.5, gain: 1 }); // 0.5 s sample at half speed = 1 s -> ends at 11.1
  ctx.currentTime = 10.9;
  player.play({ sampleIndex: 1, when: 11.0, rate: 1, gain: 1 });
  assert.equal(player.stats.stolen, 1);
});
