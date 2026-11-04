import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CameraError, CameraProblem, classifyCameraError, openCamera } from '../WebGLTemplates/Downbeat/js/camera.js';

const domError = (name, message = name) => Object.assign(new Error(message), { name });

test('browser errors are sorted into denied, missing and other', () => {
  assert.equal(classifyCameraError(domError('NotAllowedError')), CameraProblem.DENIED);
  assert.equal(classifyCameraError(domError('SecurityError')), CameraProblem.DENIED);
  assert.equal(classifyCameraError(domError('NotFoundError')), CameraProblem.MISSING);
  assert.equal(classifyCameraError(domError('NotReadableError')), CameraProblem.ERROR);
  assert.equal(classifyCameraError(domError('OverconstrainedError')), CameraProblem.ERROR);
  assert.equal(classifyCameraError(null), CameraProblem.ERROR);
});

const fakeVideo = () => ({ play: async () => {}, srcObject: null, muted: false, playsInline: false });
const fakeStream = () => ({ stopped: 0, getTracks() { return [{ stop: () => { this.stopped += 1; } }]; }, getVideoTracks: () => [{ getSettings: () => ({ width: 640, height: 480, frameRate: 30 }) }] });

test('opens the camera, plays it into the video and returns the settings', async () => {
  const video = fakeVideo();
  const stream = fakeStream();
  let asked = null;
  const result = await openCamera(video, { mediaDevices: { getUserMedia: async (constraints) => { asked = constraints; return stream; } } });
  assert.equal(video.srcObject, stream);
  assert.equal(video.muted, true);
  assert.deepEqual(result.settings, { width: 640, height: 480, frameRate: 30 });
  assert.equal(asked.audio, false, 'no microphone');
});

test('a denied permission rejects with problem "denied"', async () => {
  const mediaDevices = { getUserMedia: async () => { throw domError('NotAllowedError', 'Permission denied'); } };
  await assert.rejects(openCamera(fakeVideo(), { mediaDevices }), (error) => error instanceof CameraError && error.problem === CameraProblem.DENIED);
});

test('no camera device rejects with problem "missing"', async () => {
  const mediaDevices = { getUserMedia: async () => { throw domError('NotFoundError'); } };
  await assert.rejects(openCamera(fakeVideo(), { mediaDevices }), (error) => error.problem === CameraProblem.MISSING);
});

test('an insecure page (no mediaDevices) is reported as an error with a reason', async () => {
  await assert.rejects(openCamera(fakeVideo(), { mediaDevices: undefined }), (error) => error.problem === CameraProblem.ERROR && /https|localhost/.test(error.message));
});

test('when the video cannot play, the stream is released', async () => {
  let stopped = 0;
  const stream = { getTracks: () => [{ stop: () => { stopped += 1; } }], getVideoTracks: () => [] };
  const video = { ...fakeVideo(), play: async () => { throw new Error('autoplay blocked'); } };
  await assert.rejects(openCamera(video, { mediaDevices: { getUserMedia: async () => stream } }), CameraError);
  assert.equal(stopped, 1);
});
