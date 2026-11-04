#!/usr/bin/env node
// Day 1 measurement in a real Chrome. One session, irregular marimba clicks (one pitch, Shift held so every note has the same strength):
//  (a) do irregular clicks land on the 96 BPM 1/16 grid? -> detected onsets in the audio output (in-page tap) minus the scheduled
//      audio time, and the scheduled times against the grid itself
//  (b) does the visual flash follow the sound? -> for every note, audio time released to the flash minus the scheduled audio time
// Usage: node tools/run_day1_measure.mjs [--dense 150] [--isolated 50] [--run day1-measure]
//
// Notes ring for 1.3 s, so a threshold onset detector cannot find overlapping notes. Instead each scheduled note is located in the
// recorded output with a matched filter (the first 25 ms of its own sample) in a +/- 0.9 ms window around the scheduled start.
//  - isolated phase: notes >= 1.4 s apart (nothing rings into the next), clean measure of where the audio really starts
//  - dense phase: clicks 120-970 ms apart; ringing tails bias the estimate by a few frames (documented in tools/lib/matched.test.mjs)
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { sleep } from './lib/cdp.mjs';
import { click, elementCenter, openSite, pressStart, repoRoot, stagePoint, startSiteServer } from './lib/day1-browser.mjs';
import { locateTemplate } from './lib/matched.mjs';
import { summarizeMs } from './lib/onsets.mjs';
import { parseWav, toMono } from './lib/wav.mjs';

const { values } = parseArgs({ options: {
  dense: { type: 'string', default: '150' },
  isolated: { type: 'string', default: '50' },
  run: { type: 'string', default: 'day1-measure' },
} });
const SLOT_SEC = 60 / 96 / 4;
const MARIMBA = 2;
const STEP = 1; // C5, played from the marimba C5 sample
const SAMPLE_ID = 'marimba_C5_med';
const TEMPLATE_SEC = 0.025;
const HALF_WINDOW_FRAMES = 40; // +/- 0.9 ms, less than half a period of the 523 Hz tone, so no wrong-cycle lock
const PHASES = [
  { name: 'dense', count: Number(values.dense), minGapMs: 120, spanMs: 850 },
  { name: 'isolated', count: Number(values.isolated), minGapMs: 1400, spanMs: 1000 },
];

const outDir = resolve(repoRoot, 'measurements', 'day1', values.run);
mkdirSync(outDir, { recursive: true });

function seededRandom(seed) {
  let s = seed >>> 0;
  return () => ((s = (Math.imul(s, 1664525) + 1013904223) >>> 0) / 4294967296);
}
const percentile = (sorted, p) => sorted[Math.min(sorted.length - 1, Math.floor(p * (sorted.length - 1)))];

async function stopAndUploadTap(cdp, name) {
  return cdp.evaluate(`(async () => {
    const recs = await window.__tap.stop();
    const rec = recs.filter((r) => r.samples && r.samples.some((v) => Math.abs(v) > 1e-4))[0];
    if (!rec) return { error: 'no audio captured' };
    const bytes = new Uint8Array(rec.samples.buffer.slice(rec.samples.byteOffset, rec.samples.byteOffset + rec.samples.byteLength));
    const res = await fetch('/save?name=' + encodeURIComponent(${JSON.stringify(`day1/${values.run}/raw-audio/`)} + ${JSON.stringify(name)} + '.f32'), { method: 'POST', headers: { 'x-day0': '1' }, body: bytes });
    return { saved: res.ok, sampleRate: rec.sampleRate, firstFrame: rec.firstFrame, frames: rec.samples.length, gaps: rec.gaps, outputLatency: rec.outputLatency, baseLatency: rec.baseLatency };
  })()`);
}

function analyzePhase({ name, flashes, audio, template, onsetOffsetSec }) {
  const raw = readFileSync(resolve(outDir, 'raw-audio', `${name}.f32`));
  const recording = new Float32Array(raw.buffer.slice(raw.byteOffset, raw.byteOffset + raw.byteLength));
  const when = flashes.map((f) => f.when);
  const expectedFrames = when.map((t) => (t - onsetOffsetSec) * audio.sampleRate - audio.firstFrame);
  const found = locateTemplate(recording, template, expectedFrames, HALF_WINDOW_FRAMES);
  const lagMs = found.map((f) => (f.lagFrames / audio.sampleRate) * 1000);
  const correlations = found.map((f) => f.correlation).sort((a, b) => a - b);
  const gridErrorsMs = when.map((t) => { const k = (t - when[0]) / SLOT_SEC; return Math.abs(k - Math.round(k)) * SLOT_SEC * 1000; });
  const slotGaps = when.slice(1).map((t, i) => Math.round((t - when[i]) / SLOT_SEC));
  const lead = flashes.map((f) => (f.heardAtFlash - f.when) * 1000).sort((a, b) => a - b);
  return {
    notes: when.length,
    scheduledOffGridMaxMs: Math.max(...gridErrorsMs),
    slotGapHistogram: slotGaps.reduce((acc, g) => ({ ...acc, [g]: (acc[g] ?? 0) + 1 }), {}),
    audioStartVsScheduled: { located: found.filter((f) => !f.edge).length, atWindowEdge: found.filter((f) => f.edge).length, ...summarizeMs(lagMs),
      minCorrelation: correlations[0], medianCorrelation: percentile(correlations, 0.5) },
    visualVsAudio: { count: lead.length, medianMs: percentile(lead, 0.5), p05Ms: percentile(lead, 0.05), p95Ms: percentile(lead, 0.95), minMs: lead[0], maxMs: lead[lead.length - 1] },
  };
}

const server = startSiteServer({ port: 8781, outDir: resolve(repoRoot, 'measurements') });
await sleep(800);
let session;
try {
  session = await openSite({ port: 8781, debugPort: 9351, path: 'index.html?tap=1&input=mouse', height: 600 });
  const { cdp, consoleMessages } = session;
  await sleep(800);
  const startState = await pressStart(cdp);
  await sleep(1500);
  const canvas = await elementCenter(cdp, '#unity-canvas');
  const point = stagePoint(canvas, MARIMBA, STEP);

  const content = JSON.parse(readFileSync(resolve(repoRoot, 'content', 'content.json'), 'utf8'));
  const sample = content.instruments.find((i) => i.id === 'marimba').samples.find((s) => s.id === SAMPLE_ID);
  const rate = 2 ** (-sample.tuneCents / 1200);
  const onsetOffsetSec = sample.perceivedOnsetOffsetMs / 1000 / rate;
  const wav = parseWav(readFileSync(resolve(repoRoot, 'content', sample.file)));
  const template = toMono(wav.data).subarray(0, Math.round(TEMPLATE_SEC * wav.sampleRate));

  await cdp.send('Input.dispatchKeyEvent', { type: 'rawKeyDown', key: 'Shift', code: 'ShiftLeft', windowsVirtualKeyCode: 16 });
  const results = {};
  const random = seededRandom(20261029);
  for (const phase of PHASES) {
    const t0 = await cdp.evaluate('window.Downbeat.audio.now()');
    await cdp.evaluate('window.__tap.start(); true');
    for (let i = 0; i < phase.count; i++) {
      await click(cdp, point.x, point.y);
      await sleep(phase.minGapMs + random() * phase.spanMs);
    }
    await sleep(2500);
    const t1 = await cdp.evaluate('window.Downbeat.audio.now()');
    const audio = await stopAndUploadTap(cdp, phase.name);
    if (audio.error) throw new Error(`${phase.name}: ${audio.error}`);
    const snapshot = await cdp.evaluate('JSON.stringify(window.Downbeat.audio.snapshot())').then(JSON.parse);
    const flashes = snapshot.flashes.filter((f) => f.when >= t0 && f.when <= t1);
    results[phase.name] = { clicksSent: phase.count, tapGaps: audio.gaps, ...analyzePhase({ name: phase.name, flashes, audio, template, onsetOffsetSec }) };
    results.audioContext = { sampleRate: audio.sampleRate, baseLatency: audio.baseLatency, outputLatencyEstimate: audio.outputLatency, playerStats: snapshot.player };
    writeFileSync(resolve(outDir, `flashes-${phase.name}.json`), JSON.stringify(flashes));
  }
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Shift', code: 'ShiftLeft', windowsVirtualKeyCode: 16 });

  const summary = { run: values.run, chrome: session.chrome.version.Browser, startState, sample: SAMPLE_ID, rate, onsetOffsetSec,
    matchedFilter: { templateSec: TEMPLATE_SEC, halfWindowFrames: HALF_WINDOW_FRAMES }, results, consoleMessages };
  writeFileSync(resolve(outDir, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify(summary, null, 2));
} finally {
  await session?.chrome.close();
  server.stop();
}
