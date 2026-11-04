#!/usr/bin/env node
// Start flow and mouse fallback: for each camera situation, press Start, record what the tracker and the on-screen badge say, then click
// the stage like a person and check that notes still play. Cameras are always fake or simulated at the getUserMedia API (never the real webcam).
// Usage: node tools/run_day2_startflow.mjs --video <hands1.y4m> [--out measurements/day2/start-flow]
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { click, elementCenter, openProduct, repoRoot, sleep, startAndWaitReady } from './lib/day2-browser.mjs';
import { screenshot, startSiteServer, stagePoint } from './lib/day1-browser.mjs';

const { values } = parseArgs({ options: { video: { type: 'string' }, out: { type: 'string', default: 'measurements/day2/start-flow' } } });
const out = resolve(repoRoot, values.out);
mkdirSync(out, { recursive: true });
const server = startSiteServer({ port: 8794, outDir: out });
await sleep(800);

const CASES = [
  { id: 'camera-ok', camera: 'fake', query: 'input=hand', expectState: 2, expectMode: 'Camera mode', clickPlays: false },
  { id: 'camera-denied', camera: 'denied', query: 'input=hand', expectState: 3, expectMode: 'Mouse mode: camera permission was denied', clickPlays: true },
  { id: 'camera-missing', camera: 'missing', query: 'input=hand', expectState: 4, expectMode: 'Mouse mode: no camera found', clickPlays: true },
  { id: 'camera-broken', camera: 'broken', query: 'input=hand', expectState: 5, expectMode: 'Mouse mode: the camera could not be opened', clickPlays: true },
  { id: 'permission-pending', camera: 'pending', query: 'input=hand', expectState: 1, expectMode: 'Mouse mode for now: camera is starting', clickPlays: true },
  { id: 'model-failed', camera: 'fake', query: 'input=hand&mpbase=/no-such-dir', expectState: 6, expectMode: 'Mouse mode: the hand model could not be loaded', clickPlays: true },
  { id: 'mouse-requested', camera: 'fake', query: 'input=mouse', expectState: null, expectMode: 'Mouse mode: click the stage', clickPlays: true, expectNoCameraRequest: true },
];

async function runCase(c) {
  const session = await openProduct({ port: 8794, debugPort: 9364, query: c.query, video: values.video, camera: c.camera });
  try {
    const { cdp, consoleMessages } = session;
    await sleep(500);
    await startAndWaitReady(cdp);
    // give the tracker time to settle into its final state (the model download is the slow part)
    const deadline = Date.now() + 40000;
    let trackerState = null;
    while (Date.now() < deadline) {
      trackerState = await cdp.evaluate('window.Downbeat.hands.tracker ? window.Downbeat.hands.tracker.state : null');
      if (c.expectState === null || trackerState === c.expectState) break;
      await sleep(300);
    }
    await sleep(800);
    const label = await cdp.evaluate('window.Downbeat.page.inputLabel');
    const cameraLine = await cdp.evaluate("document.getElementById('camera-status').textContent");
    const gumCalls = await cdp.evaluate('window.__gumCalls');
    const before = await cdp.evaluate('window.Downbeat.page.noteLog.length');
    const canvas = await elementCenter(cdp, '#unity-canvas');
    const clicks = [[0, 2], [1, 6], [2, 4]];
    for (const [part, step] of clicks) {
      const p = stagePoint(canvas, part, step);
      await click(cdp, p.x, p.y);
      await sleep(400);
    }
    await sleep(600);
    const notes = await cdp.evaluate('JSON.stringify(window.Downbeat.page.noteLog.slice(' + before + ').map((n) => [n.part, n.step, n.slot, Number.isFinite(n.captureMs)]))');
    const fromClicks = JSON.parse(notes);
    await screenshot(cdp, resolve(out, `${c.id}.png`));
    const result = {
      id: c.id, camera: c.camera, query: c.query, trackerState, cameraLine, label, gumCalls, notesFromClicks: fromClicks,
      pass: {
        state: c.expectState === null ? gumCalls === 0 : trackerState === c.expectState,
        label: label.startsWith(c.expectMode),
        mousePlays: c.clickPlays ? fromClicks.length === clicks.length && fromClicks.every((n, i) => n[0] === clicks[i][0] && n[3] === false) : fromClicks.every((n) => n[3] === true), // camera mode: the clicks play nothing; any note must come from the (fake) hand
        noCameraRequest: c.expectNoCameraRequest ? gumCalls === 0 : true,
      },
      console: consoleMessages.filter((m) => !/gl_context|NORM_RECT|Failed to load resource/.test(m.text)).slice(0, 4),
    };
    result.ok = Object.values(result.pass).every(Boolean);
    return result;
  } finally {
    await session.chrome.close();
  }
}

const results = [];
try {
  for (const c of CASES) {
    const r = await runCase(c);
    results.push(r);
    console.log(r.ok ? 'PASS' : 'FAIL', r.id, JSON.stringify({ state: r.trackerState, label: r.label, gum: r.gumCalls, clickNotes: r.notesFromClicks.length, pass: r.pass }));
  }
} finally {
  server.stop();
}
writeFileSync(resolve(out, 'start-flow-results.json'), JSON.stringify(results, null, 2));
process.exitCode = results.every((r) => r.ok) ? 0 : 1;
