#!/usr/bin/env node
// Records a short scripted performance of the Day 1 build: screenshots mid-play, a CDP screencast of the page, and the audio that
// reaches the Web Audio destination (in-page tap), muxed with ffmpeg into one mp4. The speakers stay silent (the tap is muted).
// The audio is shifted by the browser's output timestamp so that sound and picture line up the way a listener would hear them
// (the page releases each flash at the time the note is HEARD, i.e. after the output latency).
// Usage: node tools/record_demo.mjs [--out docs/media]
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { sleep } from './lib/cdp.mjs';
import { click, elementCenter, openSite, pressStart, repoRoot, stagePoint, startSiteServer } from './lib/day1-browser.mjs';
import { encodeWavPcm16Mono } from './lib/wav.mjs';
import { encodeVideo, planFrames, runFfmpeg } from './lib/screencast-video.mjs';

const { values } = parseArgs({ options: { out: { type: 'string', default: 'docs/media' } } });
const outDir = resolve(repoRoot, values.out);
mkdirSync(outDir, { recursive: true });

const LEAD_IN_SEC = 0.6;
const TAIL_SEC = 1.8;
const VIDEO_FPS = 30;
const SCREENCAST_HEAD_START_MS = 1200; // start recording well before the window so that a frame exists for its first instant
const SCREENSHOT_AFTER_EVENT = new Set([11, 17, 23, 30]); // event indexes
const SCREENSHOT_DELAYS_MS = [140, 90]; // gaps between consecutive screenshots after a click
const SCREENSHOT_STEP_MS = 0;
const PEAK_TARGET = 0.7;
// [seconds after start, part (0 harp, 1 recorder, 2 marimba), pitch step 0..9]. Timing is human-ish on purpose; the grid decides the beats.
const PERFORMANCE = [
  [0.00, 0, 0], [0.65, 0, 3], [1.28, 0, 5], [1.92, 0, 7], [2.55, 0, 4], [3.20, 0, 2], [3.84, 0, 0], [4.50, 0, 3], [5.12, 0, 5], [5.78, 0, 8], [6.44, 0, 6], [7.10, 0, 4],
  [1.30, 2, 1], [1.93, 2, 1], [3.15, 2, 3], [3.78, 2, 1], [4.45, 2, 4], [5.10, 2, 1], [5.75, 2, 3], [6.42, 2, 1], [7.08, 2, 4], [7.72, 2, 3], [8.35, 2, 1],
  [2.52, 1, 5], [3.10, 1, 6], [4.40, 1, 7], [5.70, 1, 8], [6.35, 1, 6], [7.50, 1, 5], [8.80, 1, 7],
  // an irregular burst, to show every click still lands on a slot
  [8.00, 0, 2], [8.13, 1, 6], [8.31, 2, 4], [8.42, 0, 5], [8.80, 2, 2], [9.10, 0, 7], [9.18, 1, 9], [9.55, 2, 3], [9.95, 0, 4],
].sort((a, b) => a[0] - b[0]);

const server = startSiteServer({ port: 8782, outDir: resolve(repoRoot, 'measurements', 'day1', 'demo') });
await sleep(800);
let session;
const tmp = mkdtempSync(join(tmpdir(), 'downbeat-demo-'));
try {
  session = await openSite({ port: 8782, debugPort: 9352, path: 'index.html?tap=1&input=mouse', height: 600 });
  const { cdp, consoleMessages } = session;
  await sleep(800);
  await pressStart(cdp);
  await sleep(1500);
  const canvas = await elementCenter(cdp, '#unity-canvas');

  const frames = [];
  cdp.on('Page.screencastFrame', (p) => {
    frames.push({ epochSec: p.metadata.timestamp, data: p.data });
    cdp.send('Page.screencastFrameAck', { sessionId: p.sessionId }).catch(() => {});
  });
  await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 88, maxWidth: 1600, maxHeight: 600, everyNthFrame: 1 });
  await sleep(SCREENCAST_HEAD_START_MS);

  // tap start, plus the mapping from audio time to "heard" page time at this moment
  const mapping = await cdp.evaluate(`(() => {
    window.__tap.start();
    const ctx = window.Downbeat.audio.context();
    const ts = ctx.getOutputTimestamp();
    return { timeOrigin: performance.timeOrigin, heardOffsetMs: ts.performanceTime - ts.contextTime * 1000, sampleRate: ctx.sampleRate, outputLatency: ctx.outputLatency };
  })()`);
  const tStart = Date.now();
  const startEpochSec = tStart / 1000;
  let shot = 0;
  const events = PERFORMANCE.map(([t, part, step]) => ({ t, part, step }));
  let eventIndex = 0;
  for (const e of events) {
    const wait = tStart + e.t * 1000 - Date.now();
    if (wait > 0) await sleep(wait);
    const p = stagePoint(canvas, e.part, e.step);
    await click(cdp, p.x, p.y);
    if (SCREENSHOT_AFTER_EVENT.has(eventIndex)) {
      for (const delay of SCREENSHOT_DELAYS_MS) {
        await sleep(Math.max(0, delay - (Date.now() - Date.now()) - SCREENSHOT_STEP_MS));
        const img = await cdp.send('Page.captureScreenshot', { format: 'png' });
        writeFileSync(join(outDir, `day1-screenshot-candidate-${++shot}.png`), Buffer.from(img.data, 'base64'));
      }
    }
    eventIndex += 1;
  }
  const endEpochSec = Date.now() / 1000 + TAIL_SEC;
  await sleep(TAIL_SEC * 1000);
  await cdp.send('Page.stopScreencast');

  const audio = await cdp.evaluate(`(async () => {
    const recs = await window.__tap.stop();
    const rec = recs.filter((r) => r.samples && r.samples.some((v) => Math.abs(v) > 1e-4))[0];
    if (!rec) return { error: 'no audio captured' };
    window.__demoRec = rec;
    return { firstFrame: rec.firstFrame, sampleRate: rec.sampleRate, length: rec.samples.length };
  })()`);
  if (audio.error) throw new Error(audio.error);
  // pull the samples in chunks (CDP return values are JSON; keep each message small)
  const samples = new Float32Array(audio.length);
  const CHUNK = 200000;
  for (let from = 0; from < audio.length; from += CHUNK) {
    const part = await cdp.evaluate(`(() => {
      const s = window.__demoRec.samples.subarray(${from}, ${from + CHUNK});
      let bin = ''; const u8 = new Uint8Array(s.buffer, s.byteOffset, s.byteLength);
      for (let i = 0; i < u8.length; i += 8192) bin += String.fromCharCode(...u8.subarray(i, i + 8192));
      return btoa(bin);
    })()`);
    const bytes = Buffer.from(part, 'base64');
    samples.set(new Float32Array(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)), from);
  }

  // ---- build the video window ----
  const firstEventEpoch = startEpochSec + events[0].t;
  const w0 = firstEventEpoch - LEAD_IN_SEC;
  const w1 = endEpochSec;
  const sorted = [...frames].sort((a, b) => a.epochSec - b.epochSec);
  const plan = planFrames(sorted, w0, w1);
  const videoFile = encodeVideo({ frames: sorted, plan, dir: tmp, fps: VIDEO_FPS, width: 1600, height: 600 });

  // ---- audio window, shifted to heard time ----
  const durationSec = w1 - w0;
  const sr = audio.sampleRate;
  const out = new Float32Array(Math.round(durationSec * sr));
  let peak = 0;
  for (let i = 0; i < out.length; i++) {
    const epochMs = (w0 + i / sr) * 1000;
    const ctxTime = (epochMs - mapping.timeOrigin - mapping.heardOffsetMs) / 1000; // audio time heard at this epoch
    const j = Math.round(ctxTime * sr) - audio.firstFrame;
    const v = j >= 0 && j < samples.length ? samples[j] : 0;
    out[i] = v;
    if (Math.abs(v) > peak) peak = Math.abs(v);
  }
  const gain = peak > 0 ? PEAK_TARGET / peak : 1;
  for (let i = 0; i < out.length; i++) out[i] *= gain;
  writeFileSync(join(tmp, 'audio.wav'), encodeWavPcm16Mono(out, sr));

  runFfmpeg(['-i', videoFile, '-i', join(tmp, 'audio.wav'), '-c:v', 'copy', '-c:a', 'aac', '-b:a', '160k', '-shortest', '-movflags', '+faststart', join(outDir, 'day1-demo.mp4')]);

  const snapshot = await cdp.evaluate('JSON.stringify(window.Downbeat.audio.snapshot())').then(JSON.parse);
  const meta = {
    videoStartEpochSec: w0,
    timeOriginMs: mapping.timeOrigin,
    heardOffsetMs: mapping.heardOffsetMs,
    scheduledAudioTimes: snapshot.flashes.map((f) => f.when),
    flashReleasePerfMs: snapshot.flashes.map((f) => f.perfMs),
    chrome: session.chrome.version.Browser, events: events.length, screencastFrames: plan.length, videoSeconds: durationSec,
    audio: { sampleRate: sr, firstFrame: audio.firstFrame, recordedSeconds: samples.length / sr, peakBeforeNormalize: peak, appliedGain: gain },
    audioShiftedByHeardOffsetMs: mapping.heardOffsetMs, outputLatencyEstimate: mapping.outputLatency, consoleMessages,
  };
  writeFileSync(join(repoRoot, 'measurements', 'day1', 'demo', 'demo-meta.json'), JSON.stringify(meta, null, 2));
  console.log(JSON.stringify(meta, null, 2));
} finally {
  await session?.chrome.close();
  server.stop();
  rmSync(tmp, { recursive: true, force: true });
}
