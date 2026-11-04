import { test } from 'node:test';
import assert from 'node:assert/strict';
import { HandTracker } from '../WebGLTemplates/Downbeat/js/tracker.js';
import { CameraError, CameraProblem } from '../WebGLTemplates/Downbeat/js/camera.js';
import { ASPECT_INDEX, HAND_STRIDE, HEADER_LENGTH, TrackerState, createHandBuffer } from '../WebGLTemplates/Downbeat/js/hand-buffer.js';
import { HandRecorder } from '../WebGLTemplates/Downbeat/js/hand-recorder.js';

/** A video element whose frame callback the test fires by hand. */
function fakeVideo() {
  const video = {
    videoWidth: 640,
    videoHeight: 480,
    callback: null,
    requestVideoFrameCallback(fn) { video.callback = fn; return 1; },
    fire(now, metadata = {}) { const fn = video.callback; video.callback = null; fn(now, metadata); },
  };
  return video;
}

const handAt = (x, y) => Array.from({ length: 21 }, () => ({ x, y, z: 0 }));
const result = (...hands) => ({ landmarks: hands.map(([x, y]) => handAt(x, y)), handedness: hands.map(() => [{ score: 0.9 }]) });

function setup({ camera, landmarker, detections = [] } = {}) {
  const buffer = createHandBuffer();
  const video = fakeVideo();
  const states = [];
  const frames = [];
  const recorder = new HandRecorder();
  let clock = 1000;
  const stream = { stopped: 0, getTracks() { return [{ stop: () => { stream.stopped += 1; } }]; } };
  const closed = { landmarker: 0 };
  const tracker = new HandTracker({
    buffer,
    video,
    sources: {},
    recorder,
    onState: (state, detail) => states.push([state, detail]),
    onFrame: (frame) => frames.push(frame),
    now: () => (clock += 5),
    openCamera: camera ?? (async () => ({ stream, settings: { width: 640, height: 480 } })),
    createLandmarker: landmarker ?? (async () => ({ detectForVideo: () => detections.shift() ?? result(), close: () => { closed.landmarker += 1; } })),
  });
  return { tracker, buffer, video, states, frames, recorder, stream, closed };
}

test('start asks for the camera and the model together, then runs and writes frames into the buffer', async () => {
  const t = setup({ detections: [result([0.3, 0.4])] });
  const final = await t.tracker.start();

  assert.equal(final, TrackerState.RUNNING);
  assert.deepEqual(t.states.map(([s]) => s), [TrackerState.STARTING, TrackerState.RUNNING]);
  t.video.fire(2000, { captureTime: 1990 });

  assert.equal(t.buffer[0], 1, 'seq');
  assert.equal(t.buffer[1], 1990, 'capture time comes from the video frame metadata');
  assert.ok(t.buffer[2] > 1000, 'inference end time');
  assert.equal(t.buffer[3], TrackerState.RUNNING);
  assert.equal(t.buffer[4], 1);
  assert.equal(t.buffer[HEADER_LENGTH], 0, 'the first hand gets id 0');
  assert.ok(Math.abs(t.buffer[HEADER_LENGTH + 2] - 0.7) < 1e-12, 'x is mirrored in the buffer');
  assert.equal(t.buffer[ASPECT_INDEX], 640 / 480);
  assert.equal(t.frames.length, 1);
  assert.ok(Math.abs(t.frames[0].hands[0].landmarks[0].x - 0.7) < 1e-12, 'the preview gets mirrored coordinates too');
});

test('without captureTime the callback time is used', async () => {
  const t = setup({ detections: [result([0.5, 0.5])] });
  await t.tracker.start();
  t.video.fire(2500, {});
  assert.equal(t.buffer[1], 2500);
  assert.equal(t.tracker.snapshot().captureTimeFrames, 0);
});

test('two hands keep their ids across frames even when the detector swaps their order', async () => {
  const t = setup({ detections: [result([0.2, 0.5], [0.8, 0.5]), result([0.8, 0.5], [0.2, 0.5])] });
  await t.tracker.start();
  t.video.fire(2000);
  const first = [t.buffer[HEADER_LENGTH], t.buffer[HEADER_LENGTH + HAND_STRIDE]];
  t.video.fire(2033);
  const second = [t.buffer[HEADER_LENGTH], t.buffer[HEADER_LENGTH + HAND_STRIDE]];

  assert.equal(t.buffer[4], 2);
  assert.deepEqual(first, [0, 1]);
  assert.deepEqual(second, [0, 1], 'hands are written in id order, so the same person keeps the same slot');
  assert.ok(Math.abs(t.buffer[HEADER_LENGTH + 2] - 0.8) < 1e-12, 'id 0 is still the hand on the left of the picture (x 0.2, mirrored 0.8)');
});

test('the recorder receives what was written, once recording is on', async () => {
  const t = setup({ detections: [result([0.3, 0.4]), result([0.3, 0.5])] });
  await t.tracker.start();
  t.video.fire(2000, { captureTime: 2000 });
  t.recorder.start();
  t.video.fire(2033, { captureTime: 2033 });
  const replay = t.recorder.stop();
  assert.equal(replay.frames.length, 1);
  assert.equal(replay.frames[0].t, 0);
  assert.equal(replay.frames[0].h[0].p[0], 0.7);
});

test('a denied camera ends in CAMERA_DENIED and the already loaded model is released', async () => {
  const t = setup({ camera: async () => { throw new CameraError(CameraProblem.DENIED, 'Permission denied'); } });
  const final = await t.tracker.start();
  assert.equal(final, TrackerState.CAMERA_DENIED);
  assert.equal(t.buffer[3], TrackerState.CAMERA_DENIED);
  assert.equal(t.closed.landmarker, 1);
  assert.equal(t.video.callback, null, 'no frame loop was started');
});

test('no camera and a broken camera are told apart', async () => {
  const missing = setup({ camera: async () => { throw new CameraError(CameraProblem.MISSING, 'none'); } });
  assert.equal(await missing.tracker.start(), TrackerState.CAMERA_MISSING);
  const broken = setup({ camera: async () => { throw new Error('boom'); } });
  assert.equal(await broken.tracker.start(), TrackerState.CAMERA_ERROR);
});

test('a model that fails to load ends in MODEL_FAILED and the camera is switched off again', async () => {
  const t = setup({ landmarker: async () => { throw new Error('network error'); } });
  const final = await t.tracker.start();
  assert.equal(final, TrackerState.MODEL_FAILED);
  assert.equal(t.stream.stopped, 1);
  assert.match(t.states.at(-1)[1], /network error/);
});

test('when both fail the camera problem is reported', async () => {
  const t = setup({
    camera: async () => { throw new CameraError(CameraProblem.DENIED, 'no'); },
    landmarker: async () => { throw new Error('offline'); },
  });
  assert.equal(await t.tracker.start(), TrackerState.CAMERA_DENIED);
});

test('a detection error is counted and the loop keeps going', async () => {
  let calls = 0;
  const t = setup({ landmarker: async () => ({ detectForVideo: () => { calls += 1; if (calls === 1) throw new Error('gpu hiccup'); return result([0.5, 0.5]); }, close() {} }) });
  await t.tracker.start();
  t.video.fire(2000);
  assert.ok(t.video.callback, 'the next frame was requested');
  t.video.fire(2033);
  const snapshot = t.tracker.snapshot();
  assert.equal(snapshot.errors, 1);
  assert.equal(snapshot.frames, 1);
  assert.equal(t.buffer[0], 1);
});

test('stats: inference time is summarized overall and by number of hands visible', async () => {
  const t = setup({ detections: [result(), result([0.5, 0.5]), result([0.2, 0.5], [0.8, 0.5]), result()] });
  await t.tracker.start();
  for (let i = 0; i < 4; i++) t.video.fire(2000 + i * 33, { captureTime: 1995 + i * 33 });
  const s = t.tracker.snapshot({ raw: true });
  assert.equal(s.frames, 4);
  assert.equal(s.inferMsByHandCount[0].n, 2);
  assert.equal(s.inferMsByHandCount[1].n, 1);
  assert.equal(s.inferMsByHandCount[2].n, 1);
  assert.equal(s.captureTimeFrames, 4);
  assert.equal(s.raw.handCounts.join(','), '0,1,2,0');
  assert.equal(s.intervalMs.n, 3);
});

test('stop releases the camera and the model and ends the frame loop', async () => {
  const t = setup();
  await t.tracker.start();
  t.tracker.stop();
  assert.equal(t.stream.stopped, 1);
  assert.equal(t.closed.landmarker, 1);
  assert.equal(t.tracker.state, TrackerState.OFF);
  assert.equal(t.buffer[3], TrackerState.OFF);
});
