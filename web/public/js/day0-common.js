// Shared helpers for the Day 0 test pages: event store, save-to-server, page-state logging, Unity startup.

const state = {
  events: [],
  waiters: [],
  hands: new Float64Array(135),
  pageEvents: [],
};

export const day0 = (window.day0 = window.day0 ?? {});
day0.hands = state.hands;
day0.state = state;

day0.onUnityEvent = (kind, json) => {
  let data;
  try {
    data = JSON.parse(json);
  } catch (error) {
    data = { parseError: String(error), raw: json };
  }
  const event = { kind, perfMs: performance.now(), data };
  state.events.push(event);
  state.waiters = state.waiters.filter((waiter) => {
    if (waiter.kind !== kind || !waiter.predicate(data)) return true;
    waiter.resolve(event);
    return false;
  });
};

export function waitForEvent(kind, { timeoutMs = 120000, predicate = () => true } = {}) {
  const existing = state.events.find((e) => e.kind === kind && predicate(e.data) && !e.consumed);
  if (existing) {
    existing.consumed = true;
    return Promise.resolve(existing);
  }
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timeout waiting for Unity event "${kind}"`)), timeoutMs);
    state.waiters.push({
      kind,
      predicate,
      resolve: (event) => {
        clearTimeout(timer);
        event.consumed = true;
        resolve(event);
      },
    });
  });
}

export function eventsSince(perfMs, kind) {
  return state.events.filter((e) => e.perfMs >= perfMs && (!kind || e.kind === kind));
}

/** Page visibility and focus log with performance.now() timestamps. */
export function watchPageState() {
  // `blurred` follows blur/focus events (real or synthetic, as the runner dispatches them), because document.hasFocus()
  // does not change when the runner only dispatches events.
  let blurred = false;
  const push = (type) => state.pageEvents.push({ type, perfMs: performance.now(), visibility: document.visibilityState, hasFocus: document.hasFocus(), blurred });
  document.addEventListener('visibilitychange', () => push('visibilitychange'));
  window.addEventListener('blur', () => { blurred = true; push('blur'); });
  window.addEventListener('focus', () => { blurred = false; push('focus'); });
  push('start');
}

export async function saveToServer(name, body) {
  const response = await fetch(`/save?name=${encodeURIComponent(name)}`, {
    method: 'POST',
    headers: { 'x-day0': '1' },
    body,
  });
  if (!response.ok) throw new Error(`save ${name} failed: ${response.status}`);
  return response.json();
}

export function saveJson(name, object) {
  return saveToServer(name, JSON.stringify(object, null, 2));
}

export function runtimeInfo() {
  return {
    userAgent: navigator.userAgent,
    hardwareConcurrency: navigator.hardwareConcurrency,
    devicePixelRatio: window.devicePixelRatio,
    innerWidth: window.innerWidth,
    innerHeight: window.innerHeight,
    crossOriginIsolated: window.crossOriginIsolated,
    secureContext: window.isSecureContext,
    location: location.href,
  };
}

/** Load the Unity loader script for a build and create the instance. Resolves when the first 'ready' event arrives. */
export async function startUnity(buildName, canvas, { devicePixelRatio = 1 } = {}) {
  const base = `/builds/${buildName}`;
  await new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = `${base}/Build/${buildName}.loader.js`;
    script.onload = resolve;
    script.onerror = () => reject(new Error(`could not load ${script.src}`));
    document.head.appendChild(script);
  });
  const config = {
    dataUrl: `${base}/Build/${buildName}.data.br`,
    frameworkUrl: `${base}/Build/${buildName}.framework.js.br`,
    codeUrl: `${base}/Build/${buildName}.wasm.br`,
    streamingAssetsUrl: `${base}/StreamingAssets`,
    companyName: 'Downbeat',
    productName: 'Downbeat Day0',
    productVersion: '0.0.0',
    devicePixelRatio,
    matchWebGLToCanvasSize: true,
  };
  const t0 = performance.now();
  const instance = await window.createUnityInstance(canvas, config, () => {});
  return { instance, bootMs: performance.now() - t0 };
}

export function sendToUnity(instance, method, payload) {
  instance.SendMessage('Day0', method, payload === undefined ? '' : JSON.stringify(payload));
}

export function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function percentile(sortedAsc, p) {
  if (sortedAsc.length === 0) return null;
  if (sortedAsc.length === 1) return sortedAsc[0];
  const rank = p * (sortedAsc.length - 1);
  const lo = Math.floor(rank);
  const hi = Math.ceil(rank);
  return sortedAsc[lo] + (sortedAsc[hi] - sortedAsc[lo]) * (rank - lo);
}

export function summarize(values) {
  if (values.length === 0) return { count: 0 };
  const sorted = [...values].sort((a, b) => a - b);
  const sum = values.reduce((s, v) => s + v, 0);
  return {
    count: values.length,
    mean: sum / values.length,
    p50: percentile(sorted, 0.5),
    p95: percentile(sorted, 0.95),
    p99: percentile(sorted, 0.99),
    max: sorted[sorted.length - 1],
    min: sorted[0],
  };
}

/** Records requestAnimationFrame intervals (ms) between start() and stop(). Independent of Unity's own loop. */
export function createRafSampler() {
  let running = false;
  let last = 0;
  let intervals = [];
  const tick = (now) => {
    if (!running) return;
    if (last > 0) intervals.push(now - last);
    last = now;
    requestAnimationFrame(tick);
  };
  return {
    start() {
      intervals = [];
      last = 0;
      running = true;
      requestAnimationFrame(tick);
    },
    stop() {
      running = false;
      return summarize(intervals);
    },
  };
}
