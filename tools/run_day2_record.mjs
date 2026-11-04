#!/usr/bin/env node
// Records the hand tracking of a fake-camera video into a replay file (the page's own recorder, hand-recorder.js), then cuts it to one
// loop of the video. The result is what ships as content/replay/demo-hands.json.
// Usage: node tools/run_day2_record.mjs --video <hands1.y4m> [--seconds 40] [--out content/replay/demo-hands.json] [--raw measurements/day2/replay/raw-recording.json]
import { mkdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { openProduct, repoRoot, sleep, startAndWaitReady, waitTrackerSettled } from './lib/day2-browser.mjs';
import { startSiteServer } from './lib/day1-browser.mjs';
import { trimToLoop } from './lib/replay-trim.mjs';

const { values } = parseArgs({ options: {
  video: { type: 'string' }, seconds: { type: 'string', default: '40' },
  out: { type: 'string', default: 'content/replay/demo-hands.json' }, raw: { type: 'string', default: 'measurements/day2/replay/raw-recording.json' },
} });
const NOTE = 'Recorded from a FAKE camera: one CC0 photo of a hand moved by tools/make_strike_video.py (not a person), tracked by MediaPipe hand_landmarker float16 v1 in Chrome. One loop of the video, cut at the still moment between two passes.';

const server = startSiteServer({ port: 8791, outDir: resolve(repoRoot, 'measurements', 'day2', 'record') });
await sleep(800);
let session;
try {
  session = await openProduct({ port: 8791, debugPort: 9361, query: 'input=hand&preview=0', video: values.video });
  const { cdp } = session;
  await startAndWaitReady(cdp);
  const state = await waitTrackerSettled(cdp);
  if (state !== 2) throw new Error(`tracker state ${state}`);
  await sleep(1500);
  await cdp.evaluate('window.Downbeat.hands.recorder.start()');
  await sleep(Number(values.seconds) * 1000);
  const json = await cdp.evaluate(`JSON.stringify(window.Downbeat.hands.recorder.stop(${JSON.stringify(NOTE)}))`);
  const raw = JSON.parse(json);
  const rawPath = resolve(repoRoot, values.raw);
  mkdirSync(dirname(rawPath), { recursive: true });
  writeFileSync(rawPath, json);
  const trimmed = trimToLoop(raw);
  const outPath = resolve(repoRoot, values.out);
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, JSON.stringify(trimmed));
  const handFrames = trimmed.frames.filter((f) => f.h.length > 0).length;
  console.log(JSON.stringify({
    rawFrames: raw.frames.length, rawBytes: statSync(rawPath).size,
    loopFrames: trimmed.frames.length, loopSeconds: trimmed.frames.at(-1).t / 1000, framesWithHand: handFrames, bytes: statSync(outPath).size,
  }));
} finally {
  await session?.chrome.close();
  server.stop();
}
