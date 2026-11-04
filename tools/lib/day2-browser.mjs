// Helpers for driving the Day 2 site in a real Chrome with a FAKE camera (a video file, never the Mac's real webcam):
// network capture of everything the page requests, an animation-frame recorder (Unity's frame times), notes and tracker statistics.
import { resolve } from 'node:path';
import { launchChrome, sleep } from './cdp.mjs';
import { click, elementCenter, repoRoot } from './day1-browser.mjs';

export { click, elementCenter, repoRoot, sleep };

const BACKGROUND_FLAGS = ['--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows'];

/** Chrome flags for a fake camera fed from a y4m file. --use-fake-ui accepts the permission prompt (the camera is the fake one). */
export const fakeCameraArgs = (y4mPath) => ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', `--use-file-for-fake-video-capture=${resolve(y4mPath)}`];

/**
 * Camera situations for the start-flow tests. 'fake' accepts the permission prompt and serves the video file; 'denied' is Chrome's real
 * "deny every permission prompt" mode (the fake device flag is still given, so no real camera could ever be opened); 'missing', 'broken'
 * and 'pending' replace navigator.mediaDevices.getUserMedia before any page script runs (they simulate the API result; the real call is
 * never made). Every mode counts the getUserMedia calls in window.__gumCalls.
 */
export function cameraScenario(mode) {
  const wrap = (body) => `(() => {
    window.__gumCalls = 0;
    const devices = navigator.mediaDevices;
    if (!devices) return;
    const real = devices.getUserMedia.bind(devices);
    devices.getUserMedia = (constraints) => { window.__gumCalls += 1; ${body} };
  })();`;
  switch (mode) {
    case 'missing': return { args: ['--use-fake-device-for-media-stream'], script: wrap("return Promise.reject(Object.assign(new Error('Requested device not found'), { name: 'NotFoundError' }));") };
    case 'broken': return { args: ['--use-fake-device-for-media-stream'], script: wrap("return Promise.reject(Object.assign(new Error('Could not start video source'), { name: 'NotReadableError' }));") };
    case 'pending': return { args: ['--use-fake-device-for-media-stream'], script: wrap('return new Promise(() => {});') };
    case 'denied': return { args: ['--use-fake-device-for-media-stream', '--deny-permission-prompts'], script: wrap('return real(constraints);') };
    default: return { args: null, script: wrap('return real(constraints);') };
  }
}

/** Injected before the page's scripts: records the time of every animation frame callback (Unity's main loop is driven by them). */
export const RAF_RECORDER = `(() => {
  const times = [];
  const original = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = (callback) => original((t) => { times.push(performance.now()); if (times.length > 200000) times.shift(); callback(t); });
  window.__rafTimes = times;
})();`;

/**
 * Opens the product page. Chrome starts on about:blank so that network capture and the injected scripts are in place before the
 * first request.
 * @param {{port:number, debugPort:number, query?:string, video?:string, args?:string[], width?:number, height?:number, netlogPath?:string, injectScripts?:string[], path?:string}} options
 */
export async function openProduct({ port, debugPort, query = '', path = 'index.html', video, camera = 'fake', args = [], width = 1600, height = 700, netlogPath, injectScripts = [] }) {
  const scenario = cameraScenario(camera);
  const chromeArgs = [
    ...BACKGROUND_FLAGS,
    `--window-size=${width + 20},${height + 140}`, '--window-position=30,30',
    ...(scenario.args ?? (video ? fakeCameraArgs(video) : ['--use-fake-device-for-media-stream'])),
    ...(netlogPath ? [`--log-net-log=${netlogPath}`, '--net-log-capture-mode=Default'] : []),
    ...args,
  ];
  const chrome = await launchChrome({ port: debugPort, args: chromeArgs, url: 'about:blank' });
  const { cdp } = await chrome.connectToPage('about:blank');

  const requests = [];
  const webSockets = [];
  const consoleMessages = [];
  const byId = new Map();
  cdp.on('Network.requestWillBeSent', (p) => {
    const entry = { id: p.requestId, url: p.request.url, method: p.request.method, hasPostData: Boolean(p.request.hasPostData), postDataBytes: p.request.postData?.length ?? 0,
      type: p.type ?? null, initiator: p.initiator?.type ?? null, contentType: p.request.headers?.['Content-Type'] ?? null, tMs: Math.round(p.timestamp * 1000) };
    byId.set(p.requestId, entry);
    requests.push(entry);
  });
  cdp.on('Network.responseReceived', (p) => { const e = byId.get(p.requestId); if (e) { e.status = p.response.status; e.mime = p.response.mimeType; e.fromCache = p.response.fromDiskCache || p.response.fromPrefetchCache || false; } });
  cdp.on('Network.loadingFailed', (p) => { const e = byId.get(p.requestId); if (e) e.failed = p.errorText; });
  cdp.on('Network.webSocketCreated', (p) => webSockets.push({ kind: 'created', url: p.url }));
  cdp.on('Network.webSocketFrameSent', (p) => webSockets.push({ kind: 'frameSent', bytes: p.response?.payloadData?.length ?? 0 }));
  cdp.on('Runtime.consoleAPICalled', (p) => {
    const text = p.args.map((a) => a.value ?? a.description ?? '').join(' ');
    if (['error', 'warning'].includes(p.type) || /exception|halting|abort/i.test(text)) consoleMessages.push({ type: p.type, text: text.slice(0, 400) });
  });
  cdp.on('Runtime.exceptionThrown', (p) => consoleMessages.push({ type: 'exception', text: (p.exceptionDetails.exception?.description ?? p.exceptionDetails.text).slice(0, 400) }));

  await cdp.send('Runtime.enable');
  await cdp.send('Page.enable');
  await cdp.send('Network.enable');
  await cdp.send('Network.setCacheDisabled', { cacheDisabled: true }); // every download is visible in the log
  await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
  for (const source of [scenario.script, ...injectScripts]) await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source });
  await cdp.send('Page.bringToFront');
  const loaded = new Promise((resolveLoad) => cdp.on('Page.loadEventFired', resolveLoad));
  await cdp.send('Page.navigate', { url: `http://localhost:${port}/${path}${query ? `?${query}` : ''}` });
  await loaded;
  return { chrome, cdp, requests, webSockets, consoleMessages };
}

/** Clicks Start. Resolves when Unity reports ready (or rejects on a start error). */
export async function startAndWaitReady(cdp, { timeoutMs = 90000 } = {}) {
  const button = await elementCenter(cdp, '#start');
  await click(cdp, button.x, button.y);
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const s = await cdp.evaluate('({ ready: window.Downbeat.page.isReady, status: window.Downbeat.page.statusText, mode: window.Downbeat.page.inputLabel })');
    if (s.ready) return s;
    if (/^(error|could not start)/.test(String(s.status))) throw new Error(`page reported: ${s.status}`);
    await sleep(250);
  }
  throw new Error('Unity did not report ready in time');
}

/** Waits until the camera tracker has produced a state other than "starting" (running or a failure). */
export async function waitTrackerSettled(cdp, { timeoutMs = 90000 } = {}) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const state = await cdp.evaluate('window.Downbeat.hands.tracker ? window.Downbeat.hands.tracker.state : -1');
    if (state >= 2) return state;
    await sleep(250);
  }
  throw new Error('tracker did not settle');
}

export const quantile = (sorted, q) => (sorted.length ? sorted[Math.min(sorted.length - 1, Math.floor(q * (sorted.length - 1) + 0.5))] : null);

export function summarize(values) {
  const v = values.filter((x) => Number.isFinite(x)).sort((a, b) => a - b);
  if (!v.length) return null;
  return { n: v.length, min: v[0], p50: quantile(v, 0.5), p95: quantile(v, 0.95), p99: quantile(v, 0.99), max: v[v.length - 1], mean: v.reduce((a, b) => a + b, 0) / v.length };
}

/** Frame times of Unity's loop from the animation frame recorder, over [fromMs, toMs] page time. */
export function frameSummary(times, fromMs, toMs) {
  const window = times.filter((t) => t >= fromMs && t <= toMs);
  const dt = window.slice(1).map((t, i) => t - window[i]);
  const s = summarize(dt);
  if (!s) return null;
  return { frames: window.length, seconds: (window.at(-1) - window[0]) / 1000, fps: (window.length - 1) / ((window.at(-1) - window[0]) / 1000), frameMs: s, over25ms: dt.filter((x) => x > 25).length, over50ms: dt.filter((x) => x > 50).length };
}

/** Keep only what answers "did anything leave the machine, and where did requests go?" */
export function summarizeNetwork(requests, webSockets, pageOrigin) {
  const hosts = {};
  const types = {};
  for (const r of requests) {
    if (!/^https?:/.test(r.url)) continue;
    const host = new URL(r.url).host;
    hosts[host] = (hosts[host] ?? 0) + 1;
    const type = r.type ?? 'other';
    types[type] = (types[type] ?? 0) + 1;
  }
  const http = requests.filter((r) => /^https?:/.test(r.url));
  return {
    total: http.length,
    hosts,
    resourceTypes: types,
    nonGetRequests: http.filter((r) => r.method !== 'GET' && r.method !== 'HEAD').map((r) => ({ method: r.method, url: r.url, hasPostData: r.hasPostData, postDataBytes: r.postDataBytes })),
    requestsWithBody: http.filter((r) => r.hasPostData).length,
    longestUrl: http.reduce((m, r) => Math.max(m, r.url.length), 0),
    failed: http.filter((r) => r.failed).map((r) => ({ url: r.url, error: r.failed })),
    webSocketsCreated: webSockets.filter((w) => w.kind === 'created').length,
    webSocketFramesSent: webSockets.filter((w) => w.kind === 'frameSent').length,
    pageOrigin,
  };
}
