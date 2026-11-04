import { test } from 'node:test';
import assert from 'node:assert/strict';
import { HandRecorder, MAX_RECORDED_FRAMES, REPLAY_FORMAT, REPLAY_VERSION } from '../WebGLTemplates/Downbeat/js/hand-recorder.js';

const hand = (id, x) => ({ id, score: 0.93456, landmarks: Array.from({ length: 21 }, (_, i) => ({ x: x + i / 1000, y: 0.5 + i / 2000 })) });

test('nothing is recorded until start', () => {
  const recorder = new HandRecorder();
  recorder.add({ captureMs: 1, aspect: 1, hands: [] });
  assert.equal(recorder.frameCount, 0);
});

test('records frames with times relative to the first, rounded to 4 decimals, as flat x/y pairs', () => {
  const recorder = new HandRecorder();
  recorder.start();
  recorder.add({ captureMs: 5000.123456, aspect: 4 / 3, hands: [hand(0, 0.25)] });
  recorder.add({ captureMs: 5033.5, aspect: 4 / 3, hands: [] });
  const replay = recorder.stop('test');
  assert.equal(replay.format, REPLAY_FORMAT);
  assert.equal(replay.version, REPLAY_VERSION);
  assert.equal(replay.aspect, 1.3333);
  assert.equal(replay.mirrored, true);
  assert.equal(replay.frames.length, 2);
  assert.equal(replay.frames[0].t, 0);
  assert.equal(replay.frames[1].t, 33.3765);
  assert.equal(replay.frames[0].h[0].id, 0);
  assert.equal(replay.frames[0].h[0].s, 0.9346);
  assert.equal(replay.frames[0].h[0].p.length, 42);
  assert.deepEqual(replay.frames[0].h[0].p.slice(0, 4), [0.25, 0.5, 0.251, 0.5005]);
  assert.deepEqual(replay.frames[1].h, []);
  assert.equal(recorder.isRecording, false);
});

test('the result survives JSON and keeps two hands apart', () => {
  const recorder = new HandRecorder();
  recorder.start();
  recorder.add({ captureMs: 0, aspect: 1, hands: [hand(0, 0.2), hand(1, 0.7)] });
  const again = JSON.parse(JSON.stringify(recorder.stop()));
  assert.deepEqual(again.frames[0].h.map((h) => h.id), [0, 1]);
});

test('start clears an earlier recording and the length is capped', () => {
  const recorder = new HandRecorder();
  recorder.start();
  recorder.add({ captureMs: 0, aspect: 1, hands: [] });
  recorder.start();
  assert.equal(recorder.frameCount, 0);
  for (let i = 0; i < MAX_RECORDED_FRAMES + 10; i++) recorder.add({ captureMs: i, aspect: 1, hands: [] });
  assert.equal(recorder.frameCount, MAX_RECORDED_FRAMES);
});
