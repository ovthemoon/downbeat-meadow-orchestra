#!/usr/bin/env node
// Privacy evidence (plan decision 9): while the camera is running (fake camera video), every network request the page makes is logged
// two ways: the DevTools network log of the page, and Chrome's own net-log (all processes). The claim "camera video is processed only in
// this browser, nothing carries image or video data" is checked as: only GET requests, no request bodies, no WebSocket traffic, and only
// the expected hosts. A positive control (an upload of a camera frame) is made at the end to show the same detector would see one.
// Usage: node tools/run_day2_privacy.mjs --video <hands2.y4m> [--seconds 45] [--out measurements/day2/privacy]
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { openProduct, repoRoot, sleep, startAndWaitReady, summarizeNetwork, waitTrackerSettled } from './lib/day2-browser.mjs';
import { startSiteServer } from './lib/day1-browser.mjs';
import { readNetLog, summarizeNetLog } from './lib/netlog.mjs';

const { values } = parseArgs({ options: { video: { type: 'string' }, seconds: { type: 'string', default: '45' }, out: { type: 'string', default: 'measurements/day2/privacy' } } });
const out = resolve(repoRoot, values.out);
mkdirSync(out, { recursive: true });
const PORT = 8795;
const origin = `http://localhost:${PORT}`;
const netlogPath = resolve(out, 'chrome-netlog.json');
const server = startSiteServer({ port: PORT, outDir: out });
await sleep(800);
let session;
let summary;
try {
  session = await openProduct({ port: PORT, debugPort: 9365, query: 'input=hand', video: values.video, netlogPath });
  const { cdp, requests, webSockets } = session;
  await startAndWaitReady(cdp);
  const state = await waitTrackerSettled(cdp);
  if (state !== 2) throw new Error(`tracker state ${state}`);
  await cdp.evaluate('window.Downbeat.hands.recorder.start()'); // recording must not touch the network either
  await sleep(Number(values.seconds) * 1000);
  const recorded = await cdp.evaluate('window.Downbeat.hands.recorder.stop().frames.length');
  const trackerStats = await cdp.evaluate('JSON.stringify({ frames: window.Downbeat.hands.snapshot().frames, state: window.Downbeat.hands.snapshot().state })');
  const targets = (await session.chrome.browser.send('Target.getTargets')).targetInfos.map((t) => t.type);
  const mainWindowEnd = requests.length;

  // positive control: upload one camera frame, as a leak would
  await cdp.evaluate(`(async () => {
    const canvas = document.getElementById('hand-preview');
    const blob = await new Promise((r) => canvas.toBlob(r, 'image/jpeg'));
    await fetch('/control-upload-of-a-camera-frame', { method: 'POST', body: blob }).catch(() => {});
  })()`);
  await sleep(800);

  const main = requests.slice(0, mainWindowEnd);
  const control = requests.slice(mainWindowEnd);
  summary = {
    video: values.video, seconds: Number(values.seconds), trackerFrames: JSON.parse(trackerStats), recordedFramesDuringRun: recorded,
    chromeVersion: session.chrome.version.Browser,
    devtoolsLog: summarizeNetwork(main, webSockets, origin),
    targetTypesSeen: [...new Set(targets)],
    positiveControl: summarizeNetwork(control, [], origin),
  };
  writeFileSync(resolve(out, 'requests.json'), JSON.stringify(requests.map(({ id, tMs, ...r }) => r), null, 1));
} finally {
  if (session) {
    await session.chrome.browser.send('Browser.close').catch(() => {}); // lets Chrome finish writing the net-log
    await sleep(2500);
    await session.chrome.close();
  }
  server.stop();
}
if (existsSync(netlogPath)) {
  const netlog = summarizeNetLog(readNetLog(netlogPath), origin);
  summary.netlog = netlog;
}
writeFileSync(resolve(out, 'network-summary.json'), JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 1));
