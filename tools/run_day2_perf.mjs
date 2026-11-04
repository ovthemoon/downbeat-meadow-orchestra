#!/usr/bin/env node
// Day 2 performance: tracker inference time with 0, 1 and 2 hands in the picture, and Unity's frame rate with tracking off and on.
// Every run is a fresh Chrome with a FAKE camera video (never the real webcam), the real CDN MediaPipe, the product page and Unity running.
// Usage: node tools/run_day2_perf.mjs --videos0 <hands0.y4m> --videos1 <hands1.y4m> --videos2 <hands2.y4m> [--seconds 30] [--out measurements/day2/perf]
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { frameSummary, openProduct, repoRoot, sleep, startAndWaitReady, summarize, waitTrackerSettled } from './lib/day2-browser.mjs';
import { startSiteServer } from './lib/day1-browser.mjs';

const { values } = parseArgs({ options: {
  videos0: { type: 'string' }, videos1: { type: 'string' }, videos2: { type: 'string' },
  seconds: { type: 'string', default: '30' }, tag: { type: 'string', default: '' }, skipBaseline: { type: 'boolean', default: false }, out: { type: 'string', default: 'measurements/day2/perf' }, query: { type: 'string', default: '' },
} });
const out = resolve(repoRoot, values.out);
mkdirSync(out, { recursive: true });
const seconds = Number(values.seconds);
const server = startSiteServer({ port: 8792, outDir: out });
await sleep(800);

async function oneRun({ label, query, video }) {
  const session = await openProduct({ port: 8792, debugPort: 9362, query, video });
  try {
    const { cdp, consoleMessages } = session;
    await startAndWaitReady(cdp);
    let trackerState = null;
    if (video) trackerState = await waitTrackerSettled(cdp);
    await sleep(4000); // warm-up: shader compile, JIT, GPU delegate
    const window0 = await cdp.evaluate(`(() => { window.Downbeat.hands.tracker?.resetStats(); window.Downbeat.page.frameTimes = []; return performance.now(); })()`);
    await sleep(seconds * 1000);
    const data = await cdp.evaluate(`JSON.stringify({ t1: performance.now(), frames: window.Downbeat.page.frameTimes,
      tracker: window.Downbeat.hands.tracker ? window.Downbeat.hands.snapshot({ raw: true }) : null,
      env: { userAgent: navigator.userAgent, dpr: devicePixelRatio, hardwareConcurrency: navigator.hardwareConcurrency } })`);
    const r = JSON.parse(data);
    const frames = frameSummary(r.frames, window0, r.t1);
    const result = { label, query, video: video ?? null, seconds, trackerState, unity: frames, tracker: r.tracker, env: r.env, console: consoleMessages.filter((m) => !/gl_context|NORM_RECT/.test(m.text)).slice(0, 5) };
    writeFileSync(resolve(out, `${label}.json`), JSON.stringify(result));
    console.log(label, JSON.stringify({ unity: frames && { fps: +frames.fps.toFixed(2), p50: frames.frameMs.p50, p95: frames.frameMs.p95, max: frames.frameMs.max, over25: frames.over25ms },
      infer: r.tracker?.inferMs && { n: r.tracker.inferMs.n, p50: +r.tracker.inferMs.p50.toFixed(2), p95: +r.tracker.inferMs.p95.toFixed(2), max: +r.tracker.inferMs.max.toFixed(2) },
      byHands: r.tracker && Object.fromEntries(Object.entries(r.tracker.inferMsByHandCount).map(([k, v]) => [k, v && { n: v.n, p50: +v.p50.toFixed(2), p95: +v.p95.toFixed(2) }])) }));
    return result;
  } finally {
    await session.chrome.close();
  }
}

try {
  const q = values.query ? `&${values.query}` : '';
  const tag = values.tag ? `-${values.tag}` : '';
  if (!values.skipBaseline) await oneRun({ label: `tracking-off-mouse${tag}`, query: `input=mouse${q}` });
  if (values.videos0) await oneRun({ label: `tracking-on-0hands${tag}`, query: `input=hand${q}`, video: values.videos0 });
  if (values.videos1) await oneRun({ label: `tracking-on-1hand${tag}`, query: `input=hand${q}`, video: values.videos1 });
  if (values.videos2) await oneRun({ label: `tracking-on-2hands${tag}`, query: `input=hand${q}`, video: values.videos2 });
} finally {
  server.stop();
}
