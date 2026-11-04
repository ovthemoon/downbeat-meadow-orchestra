// S-2 page logic: MediaPipe hand tracking in the same page as the Unity player, hand frames pulled by C# each frame.
import {
  day0, saveJson, runtimeInfo, startUnity, sendToUnity, sleep, waitForEvent, watchPageState, createRafSampler, summarize,
} from './day0-common.js';
import { openCamera, createTracker } from './tracker.js';
import { BUFFER_LENGTH } from './hand-buffer.js';

const WARMUP_MS = 3000;
const DEFAULT_WINDOW_SEC = 20;
const BENCH_CALLS = 10000;
let unity = null;
let camera = null;

export async function init({ build, canvas, video, vsync = 1 }) {
  watchPageState();
  const started = await startUnity(build, canvas);
  unity = started.instance;
  await waitForEvent('ready', { timeoutMs: 60000 });
  const applied = waitForEvent('vsync', { timeoutMs: 5000 });
  sendToUnity(unity, 'SetVsync', { count: vsync });
  await applied;
  // The Unity template ships with runInBackground off, which pauses the player whenever the page is not focused.
  const bgApplied = waitForEvent('runInBackground', { timeoutMs: 5000 });
  sendToUnity(unity, 'SetRunInBackground', { value: true });
  await bgApplied;
  camera = await openCamera(video);
  return { bootMs: started.bootMs, vsync, cameraSettings: camera.settings, videoWidth: video.videoWidth, videoHeight: video.videoHeight };
}

async function frameWindow(label, seconds, pullHands) {
  const raf = createRafSampler();
  const done = waitForEvent('frames', { timeoutMs: (seconds + 30) * 1000, predicate: (d) => d.label === label });
  raf.start();
  sendToUnity(unity, 'FramesStart', { label, seconds, pullHands, skipFrames: 10 });
  const event = await done;
  return { unity: event.data, rafIntervalsMs: raf.stop() };
}

async function trackedWindow(delegate, video, seconds) {
  const tracker = await createTracker({ delegate, video, hands: day0.hands });
  tracker.start();
  await sleep(WARMUP_MS);
  tracker.stop();
  tracker.start(); // resets the statistics after warm-up
  const window = await frameWindow(`tracking-${delegate}`, seconds, true);
  const tracking = tracker.stop();
  tracker.close();
  return { loadInfo: tracker.loadInfo, probe: tracker.probe, tracking, ...window };
}

function randomFrame() {
  const frame = new Float64Array(BUFFER_LENGTH);
  frame[0] = 42;
  frame[2] = performance.now();
  frame[3] = 1;
  frame[4] = 2;
  for (let i = 5; i < BUFFER_LENGTH; i++) frame[i] = Math.random();
  return frame;
}

async function bridgeBench(calls) {
  const done = waitForEvent('bridge', { timeoutMs: 120000 });
  sendToUnity(unity, 'BridgeBenchStart', { calls });
  return (await done).data;
}

/** The rejected alternative: JS pushes a JSON string per frame with SendMessage (cost measured on the JS side). */
async function sendMessageBench(calls, parse) {
  sendToUnity(unity, 'SinkConfig', { parse });
  const frame = day0.hands;
  const sample = () => JSON.stringify({ v: Array.from(frame) });
  const stringifyStart = performance.now();
  let bytes = 0;
  for (let i = 0; i < calls; i++) bytes += sample().length;
  const stringifyOnlyMs = performance.now() - stringifyStart;

  const t0 = performance.now();
  for (let i = 0; i < calls; i++) unity.SendMessage('Day0', 'SinkFrame', sample());
  const totalMs = performance.now() - t0;
  const report = waitForEvent('sink', { timeoutMs: 10000 });
  sendToUnity(unity, 'SinkReport');
  const sink = (await report).data;
  return {
    parse, calls, bytesPerCall: bytes / calls, stringifyOnlyMs, totalMs, usPerCall: (totalMs * 1000) / calls,
    usPerCallWithoutStringify: ((totalMs - stringifyOnlyMs) * 1000) / calls, receivedByUnity: sink.calls,
  };
}

/** Tracker only (no Unity frame windows): did MediaPipe load, and does the fake camera video contain a detectable hand? */
export async function trackerProbe({ delegate = 'GPU', seconds = 6, video = document.getElementById('cam') } = {}) {
  const tracker = await createTracker({ delegate, video, hands: day0.hands });
  tracker.start();
  await sleep(seconds * 1000);
  const tracking = tracker.stop();
  tracker.close();
  return { loadInfo: tracker.loadInfo, probe: tracker.probe, tracking };
}

export async function runS2({ label, video = document.getElementById('cam'), windowSec = DEFAULT_WINDOW_SEC, delegates = ['GPU', 'CPU'], runDir = 's2/adhoc' }) {
  const result = { label, runtime: runtimeInfo(), windowSec, windows: {}, bridge: {}, sendMessage: {} };
  // Preflight: if the jslib bridge cannot write into the C# array, stop here instead of halting Unity's main loop in a frame window.
  day0.hands.set(randomFrame());
  const preflight = await bridgeBench(100);
  if (!preflight.writeBackOk || preflight.alignmentError) throw new Error(`bridge preflight failed: ${JSON.stringify(preflight)}`);
  result.preflight = preflight;
  result.windows.baselineBefore = await frameWindow('baseline-before', windowSec, false);
  result.windows.pullOnly = await frameWindow('pull-only-no-tracker', windowSec, true);
  for (const delegate of delegates) {
    try {
      result.windows[`tracking${delegate}`] = await trackedWindow(delegate, video, windowSec);
    } catch (error) {
      result.windows[`tracking${delegate}`] = { error: String(error.message ?? error) };
    }
  }
  result.windows.baselineAfter = await frameWindow('baseline-after', windowSec, false);

  day0.hands.set(randomFrame());
  result.bridge.trackerOff = await bridgeBench(BENCH_CALLS);
  result.sendMessage.noParse = await sendMessageBench(BENCH_CALLS, false);
  result.sendMessage.parse = await sendMessageBench(BENCH_CALLS, true);
  result.pageEvents = day0.state.pageEvents;
  await saveJson(`${runDir}/${label}.json`, result);
  return {
    label,
    fps: Object.fromEntries(Object.entries(result.windows).map(([k, v]) => [k, v.unity ? { fps: v.unity.meanFps, p95: v.unity.p95Ms, max: v.unity.maxMs } : v.error])),
    bridge: result.bridge.trackerOff,
  };
}

async function ping() {
  const reply = waitForEvent('pong', { timeoutMs: 5000 });
  sendToUnity(unity, 'Ping');
  return (await reply).data;
}

Object.assign(day0, { initS2: init, runS2, trackerProbe, summarize, frameWindow, bridgeBench, ping });
