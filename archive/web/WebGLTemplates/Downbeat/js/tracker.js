// The hand tracker: camera -> hidden <video> -> MediaPipe HandLandmarker -> hand buffer (hand-buffer.js) that Unity pulls.
// Runs on the main thread inside requestVideoFrameCallback; inference is synchronous (plan 5.1). GPU delegate only (Day 0: CPU halves
// the Unity frame rate). MediaPipe code and model are loaded from the CDNs in tracker-config.js, not bundled.
import { TrackerState, writeFrame, writeState } from './hand-buffer.js';
import { CameraError, CameraProblem, openCamera as defaultOpenCamera } from './camera.js';
import { HandIdAssigner, palmCenter } from './hand-ids.js';

const STAT_LIMIT = 6000;
const PROBLEM_TO_STATE = {
  [CameraProblem.DENIED]: TrackerState.CAMERA_DENIED,
  [CameraProblem.MISSING]: TrackerState.CAMERA_MISSING,
  [CameraProblem.ERROR]: TrackerState.CAMERA_ERROR,
};

/** Loads MediaPipe and creates a hand landmarker. Kept separate so tests can replace it. */
export async function createMediaPipeLandmarker(sources, { numHands = 2, delegate = 'GPU' } = {}) {
  const { FilesetResolver, HandLandmarker } = await import(/* webpackIgnore: true */ sources.bundleUrl);
  const vision = await FilesetResolver.forVisionTasks(sources.wasmRoot);
  return HandLandmarker.createFromOptions(vision, {
    baseOptions: { modelAssetPath: sources.modelUrl, delegate },
    runningMode: 'VIDEO',
    numHands,
  });
}

const summarize = (values) => {
  if (values.length === 0) return null;
  const sorted = values.slice().sort((a, b) => a - b);
  const at = (q) => sorted[Math.min(sorted.length - 1, Math.floor(q * (sorted.length - 1) + 0.5))];
  return { n: sorted.length, min: sorted[0], p50: at(0.5), p95: at(0.95), max: sorted[sorted.length - 1], mean: sorted.reduce((a, b) => a + b, 0) / sorted.length };
};

export class HandTracker {
  /**
   * @param {object} deps
   * @param {Float64Array} deps.buffer
   * @param {HTMLVideoElement} deps.video
   * @param {object} deps.sources {bundleUrl, wasmRoot, modelUrl}
   * @param {(state:number, detail:string)=>void} [deps.onState]
   * @param {import('./hand-recorder.js').HandRecorder} [deps.recorder]
   * @param {(frame:object)=>void} [deps.onFrame] called with the frame that was just written (preview)
   * @param {Function} [deps.openCamera] replaceable in tests
   * @param {Function} [deps.createLandmarker] replaceable in tests
   * @param {()=>number} [deps.now]
   */
  constructor({ buffer, video, sources, onState, recorder, onFrame, openCamera = defaultOpenCamera, createLandmarker = createMediaPipeLandmarker, now = () => performance.now() }) {
    this.buffer = buffer;
    this.video = video;
    this.sources = sources;
    this.onState = onState ?? (() => {});
    this.recorder = recorder;
    this.onFrame = onFrame ?? (() => {});
    this.openCamera = openCamera;
    this.createLandmarker = createLandmarker;
    this.now = now;
    this.state = TrackerState.OFF;
    this.ids = new HandIdAssigner();
    this.seq = 0;
    this.running = false;
    this.landmarker = null;
    this.stream = null;
    this.handle = 0;
    this.loadInfo = null;
    this.stats = this.#freshStats();
  }

  #setState(state, detail = '') {
    this.state = state;
    writeState(this.buffer, state);
    this.onState(state, detail);
  }

  /** Requests the camera and loads the model at the same time; resolves with the final state (RUNNING or a failure). */
  async start() {
    if (this.state === TrackerState.STARTING || this.state === TrackerState.RUNNING) return this.state;
    this.#setState(TrackerState.STARTING, 'asking for the camera and loading the hand model');
    const t0 = this.now();
    const [camera, model] = await Promise.allSettled([this.openCamera(this.video), this.createLandmarker(this.sources)]);
    this.loadInfo = { settledMs: this.now() - t0 };

    if (camera.status === 'rejected') {
      this.#release(model.status === 'fulfilled' ? model.value : null);
      const problem = camera.reason instanceof CameraError ? camera.reason.problem : CameraProblem.ERROR;
      this.#setState(PROBLEM_TO_STATE[problem], String(camera.reason?.message ?? camera.reason));
      return this.state;
    }
    this.stream = camera.value.stream;
    this.loadInfo.cameraSettings = camera.value.settings;
    if (model.status === 'rejected') {
      this.#release(null);
      this.#setState(TrackerState.MODEL_FAILED, String(model.reason?.message ?? model.reason));
      return this.state;
    }
    this.landmarker = model.value;
    this.running = true;
    this.#setState(TrackerState.RUNNING, 'hand tracking is running');
    this.#schedule();
    return this.state;
  }

  stop() {
    this.running = false;
    this.#release(null);
    if (this.state === TrackerState.RUNNING) this.#setState(TrackerState.OFF, 'stopped');
  }

  #release(landmarker) {
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
    (landmarker ?? this.landmarker)?.close?.();
    this.landmarker = null;
  }

  #schedule() {
    if (!this.running) return;
    this.handle = this.video.requestVideoFrameCallback
      ? this.video.requestVideoFrameCallback((now, metadata) => this.#onVideoFrame(now, metadata))
      : requestAnimationFrame((now) => this.#onVideoFrame(now, {}));
  }

  #onVideoFrame(callbackTime, metadata) {
    if (!this.running) return;
    const start = this.now();
    let result = null;
    try {
      result = this.landmarker.detectForVideo(this.video, start);
    } catch (error) {
      this.stats.errors += 1;
      this.stats.lastError = String(error?.message ?? error);
    }
    const end = this.now();
    if (result) this.#publish(callbackTime, metadata, start, end, result);
    this.#schedule();
  }

  #publish(callbackTime, metadata, start, end, result) {
    const hasCaptureTime = typeof metadata.captureTime === 'number';
    const captureMs = hasCaptureTime ? metadata.captureTime : callbackTime; // without captureTime the callback time is an upper bound
    const aspect = this.video.videoWidth && this.video.videoHeight ? this.video.videoWidth / this.video.videoHeight : 4 / 3;
    const handedness = result.handedness ?? result.handednesses ?? [];
    const detections = (result.landmarks ?? []).map((landmarks, i) => ({ landmarks, score: handedness[i]?.[0]?.score ?? 0 }));
    const ids = this.ids.assign(detections.map((d) => palmCenter(d.landmarks)), end);
    const hands = detections.slice(0, ids.length).map((d, i) => ({ id: ids[i], score: d.score, landmarks: d.landmarks }));
    hands.sort((a, b) => a.id - b.id);

    this.seq += 1;
    const frame = { seq: this.seq, captureMs, inferEndMs: end, aspect, hands };
    writeFrame(this.buffer, frame);

    const mirrored = hands.map((h) => ({ id: h.id, score: h.score, landmarks: h.landmarks.map((p) => ({ x: 1 - p.x, y: p.y })) }));
    this.recorder?.add({ captureMs, aspect, hands: mirrored });
    this.#record(callbackTime, metadata, start, end, hands.length, hasCaptureTime, captureMs);
    this.onFrame({ ...frame, hands: mirrored });
  }

  #freshStats() {
    return { frames: 0, errors: 0, lastError: null, inferMs: [], intervalMs: [], handCounts: [], captureAgeMs: [], captureTimeFrames: 0, lastCallback: null };
  }

  #record(callbackTime, metadata, start, end, handCount, hasCaptureTime, captureMs) {
    const s = this.stats;
    s.frames += 1;
    const push = (list, value) => {
      list.push(value);
      if (list.length > STAT_LIMIT) list.shift();
    };
    push(s.inferMs, end - start);
    push(s.handCounts, handCount);
    if (s.lastCallback !== null) push(s.intervalMs, callbackTime - s.lastCallback);
    s.lastCallback = callbackTime;
    if (hasCaptureTime) {
      s.captureTimeFrames += 1;
      push(s.captureAgeMs, end - captureMs);
    }
  }

  resetStats() {
    this.stats = this.#freshStats();
  }

  /** Summary for measurement scripts; raw arrays included when asked. */
  snapshot({ raw = false } = {}) {
    const s = this.stats;
    const byHands = {};
    for (const count of [0, 1, 2]) {
      const values = s.inferMs.filter((_, i) => s.handCounts[i] === count);
      byHands[count] = summarize(values);
    }
    return {
      state: this.state,
      frames: s.frames,
      errors: s.errors,
      lastError: s.lastError,
      loadInfo: this.loadInfo,
      inferMs: summarize(s.inferMs),
      inferMsByHandCount: byHands,
      intervalMs: summarize(s.intervalMs),
      captureTimeFrames: s.captureTimeFrames,
      captureToInferEndMs: summarize(s.captureAgeMs),
      ...(raw ? { raw: { inferMs: s.inferMs.slice(), handCounts: s.handCounts.slice(), intervalMs: s.intervalMs.slice(), captureAgeMs: s.captureAgeMs.slice() } } : {}),
    };
  }
}
