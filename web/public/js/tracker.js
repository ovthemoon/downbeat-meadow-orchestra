// Day 0 tracker: webcam -> <video> -> MediaPipe HandLandmarker (VIDEO mode) -> shared hand buffer.
// Runs on the main thread inside requestVideoFrameCallback (what plan 5.1 describes); inference is synchronous.
import { FilesetResolver, HandLandmarker } from '/vendor/mediapipe/vision_bundle.mjs';
import { writeFrame } from './hand-buffer.js';
import { summarize } from './day0-common.js';

const WASM_ROOT = '/vendor/mediapipe/wasm';
const MODEL_URL = '/vendor/models/hand_landmarker.task';

export async function openCamera(video, constraints = { width: 640, height: 480, frameRate: 30 }) {
  const stream = await navigator.mediaDevices.getUserMedia({ video: constraints, audio: false });
  video.srcObject = stream;
  video.muted = true;
  video.playsInline = true;
  await video.play();
  const settings = stream.getVideoTracks()[0].getSettings();
  return { stream, settings };
}

export async function createTracker({ delegate, numHands = 2, video, hands }) {
  const t0 = performance.now();
  const vision = await FilesetResolver.forVisionTasks(WASM_ROOT);
  const tFileset = performance.now();
  const landmarker = await HandLandmarker.createFromOptions(vision, {
    baseOptions: { modelAssetPath: MODEL_URL, delegate },
    runningMode: 'VIDEO',
    numHands,
  });
  const loadInfo = { delegate, filesetMs: tFileset - t0, createMs: performance.now() - tFileset };

  const probe = probeTimestampArgument(landmarker, video);
  const state = { running: false, seq: 0, stats: freshStats() };
  let handle = 0;

  const onFrame = (now, metadata) => {
    if (!state.running) return;
    const start = performance.now();
    const result = landmarker.detectForVideo(video, start);
    const end = performance.now();
    recordFrame(state.stats, { now, metadata, start, end, result });
    state.seq += 1;
    writeFrame(hands, {
      seq: state.seq,
      frameMs: typeof metadata.captureTime === 'number' ? metadata.captureTime : now,
      inferEndMs: end,
      hands: toHands(result),
    });
    handle = video.requestVideoFrameCallback(onFrame);
  };

  return {
    loadInfo,
    probe,
    start() {
      state.stats = freshStats();
      state.running = true;
      handle = video.requestVideoFrameCallback(onFrame);
    },
    stop() {
      state.running = false;
      video.cancelVideoFrameCallback(handle);
      return summarizeStats(state.stats);
    },
    snapshot: () => summarizeStats(state.stats),
    close() {
      state.running = false;
      landmarker.close();
    },
  };
}

/** Plan 2.2 open question: is the timestamp argument of detectForVideo optional in JS? Try without it once. */
function probeTimestampArgument(landmarker, video) {
  const outcome = { withoutTimestamp: null, resultKeys: null };
  try {
    const result = landmarker.detectForVideo(video);
    outcome.withoutTimestamp = 'accepted';
    outcome.resultKeys = Object.keys(result);
  } catch (error) {
    outcome.withoutTimestamp = `threw: ${String(error.message ?? error).slice(0, 120)}`;
  }
  try {
    const result = landmarker.detectForVideo(video, performance.now() + 1);
    outcome.resultKeys = Object.keys(result);
  } catch (error) {
    outcome.withTimestampError = String(error.message ?? error).slice(0, 120);
  }
  return outcome;
}

function toHands(result) {
  const handedness = result.handedness ?? result.handednesses ?? [];
  return (result.landmarks ?? []).map((landmarks, i) => ({ score: handedness[i]?.[0]?.score ?? 0, landmarks }));
}

function freshStats() {
  return {
    inferMs: [],
    frameIntervalMs: [],
    handCounts: [],
    scores: [],
    captureTimeFrames: 0,
    captureAgeMs: [],
    lastNow: null,
    firstLandmarks: null,
  };
}

function recordFrame(stats, { now, metadata, start, end, result }) {
  stats.inferMs.push(end - start);
  if (stats.lastNow !== null) stats.frameIntervalMs.push(now - stats.lastNow);
  stats.lastNow = now;
  const handCount = result.landmarks?.length ?? 0;
  stats.handCounts.push(handCount);
  const handedness = result.handedness ?? result.handednesses ?? [];
  if (handCount > 0) stats.scores.push(handedness[0]?.[0]?.score ?? 0);
  if (handCount > 0 && !stats.firstLandmarks) stats.firstLandmarks = result.landmarks[0].map((p) => [p.x, p.y, p.z]);
  if (typeof metadata.captureTime === 'number') {
    stats.captureTimeFrames += 1;
    stats.captureAgeMs.push(now - metadata.captureTime);
  }
}

function summarizeStats(stats) {
  const frames = stats.inferMs.length;
  const withHand = stats.handCounts.filter((c) => c > 0).length;
  return {
    frames,
    inferMs: summarize(stats.inferMs),
    frameIntervalMs: summarize(stats.frameIntervalMs),
    framesWithHand: withHand,
    handFrameRatio: frames ? withHand / frames : null,
    maxHands: Math.max(0, ...stats.handCounts),
    handednessScore: summarize(stats.scores),
    captureTimeFrames: stats.captureTimeFrames,
    captureAgeMs: summarize(stats.captureAgeMs),
    firstLandmarks: stats.firstLandmarks,
  };
}
