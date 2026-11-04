#!/usr/bin/env node
// Software latency of the hand path, per note, from the page's note log: camera frame capture time -> inference end -> Unity read ->
// note decided/scheduled -> time the sample is heard (estimated from getOutputTimestamp). All times are on the page clock
// (performance.now()). With a FAKE camera the capture time is the fake device's frame timestamp, so the real camera's exposure and
// USB/driver delay are NOT in these numbers (that needs the real webcam and a phone recording: left for the user, see docs/day2-results.md).
// Usage: node tools/run_day2_latency.mjs --video <hands1.y4m> [--seconds 150] [--out measurements/day2/latency]
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { openProduct, repoRoot, sleep, startAndWaitReady, summarize, waitTrackerSettled } from './lib/day2-browser.mjs';
import { startSiteServer } from './lib/day1-browser.mjs';

const { values } = parseArgs({ options: { video: { type: 'string' }, seconds: { type: 'string', default: '150' }, out: { type: 'string', default: 'measurements/day2/latency' } } });
const out = resolve(repoRoot, values.out);
mkdirSync(out, { recursive: true });
const server = startSiteServer({ port: 8797, outDir: out });
await sleep(800);
let session;
try {
  session = await openProduct({ port: 8797, debugPort: 9367, query: 'input=hand', video: values.video });
  const { cdp, consoleMessages } = session;
  await startAndWaitReady(cdp);
  if ((await waitTrackerSettled(cdp)) !== 2) throw new Error('camera tracker did not start');
  await sleep(4000);
  const tStart = await cdp.evaluate('performance.now()');
  await sleep(Number(values.seconds) * 1000);
  const all = JSON.parse(await cdp.evaluate('JSON.stringify(window.Downbeat.page.noteLog)'));
  const audioInfo = await cdp.evaluate('JSON.stringify({ outputLatency: window.Downbeat.audio.snapshot().outputLatency, baseLatency: window.Downbeat.audio.snapshot().baseLatency, sampleRate: window.Downbeat.audio.snapshot().sampleRate })');
  const notes = all.filter((n) => n.loggedMs >= tStart);
  writeFileSync(resolve(out, 'notes.json'), JSON.stringify(notes));
  const hand = notes.filter((n) => Number.isFinite(n.captureMs) && n.slot >= 0 && Number.isFinite(n.whenPerfMs));
  const stage = (name, f) => [name, summarize(hand.map(f))];
  const summary = {
    video: values.video, seconds: Number(values.seconds), notesLogged: notes.length, handNotes: hand.length, dropped: notes.filter((n) => n.slot < 0).length,
    late: hand.filter((n) => n.when < n.audioNow).length, audio: JSON.parse(audioInfo), chrome: session.chrome.version.Browser,
    stagesMs: Object.fromEntries([
      stage('waitForSpeedPeak (decision frame capture - trigger frame capture)', (n) => n.captureMs - n.triggerCaptureMs),
      stage('cameraFrameToInferenceEnd (capture -> inference end)', (n) => n.inferEndMs - n.captureMs),
      stage('inferenceEndToUnityRead (delivery)', (n) => n.receivedMs - n.inferEndMs),
      stage('unityReadToDecided (interpretation + scheduling)', (n) => n.decidedMs - n.receivedMs),
      stage('captureToDecided (decision frame capture -> note scheduled)', (n) => n.decidedMs - n.captureMs),
      stage('triggerFrameToDecided (strike first seen -> note scheduled)', (n) => n.decidedMs - n.triggerCaptureMs),
      stage('decidedToHeard (minLead + grid wait + output latency estimate)', (n) => n.whenPerfMs - n.decidedMs),
      stage('captureToHeard (decision frame capture -> estimated sound)', (n) => n.whenPerfMs - n.captureMs),
      stage('requestToSlot (audio time requested -> grid slot)', (n) => (n.when - n.requestedAt) * 1000),
    ]),
    console: consoleMessages.filter((m) => !/gl_context|NORM_RECT/.test(m.text)).slice(0, 4),
  };
  writeFileSync(resolve(out, 'summary.json'), JSON.stringify(summary, null, 2));
  const fmt = (s) => s && { n: s.n, p50: +s.p50.toFixed(1), p95: +s.p95.toFixed(1), max: +s.max.toFixed(1) };
  console.log(JSON.stringify({ ...summary, stagesMs: Object.fromEntries(Object.entries(summary.stagesMs).map(([k, v]) => [k, fmt(v)])) }, null, 1));
} finally {
  await session?.chrome.close();
  server.stop();
}
