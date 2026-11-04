#!/usr/bin/env node
// Records the Day 2 demo: a fake-camera hand (a still CC0 photo moved by tools/make_strike_video.py, NOT a person and NOT the real webcam)
// strikes downward in front of the tracker, the page shows the camera inset with the hand skeleton, and the musicians play. Same
// method as Day 1: CDP screencast + the audio that reaches the Web Audio destination (in-page tap, speakers silent), audio shifted by
// the output timestamp so that sound and picture line up as a listener would hear them, muxed with ffmpeg.
// Usage: node tools/record_demo_day2.mjs --video <hands1.y4m> [--out docs/media] [--seconds 14]
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { openProduct, repoRoot, sleep, startAndWaitReady, waitTrackerSettled } from './lib/day2-browser.mjs';
import { startSiteServer } from './lib/day1-browser.mjs';
import { encodeWavPcm16Mono } from './lib/wav.mjs';
import { encodeVideo, planFrames, runFfmpeg } from './lib/screencast-video.mjs';

const { values } = parseArgs({ options: { video: { type: 'string' }, out: { type: 'string', default: 'docs/media' }, seconds: { type: 'string', default: '14' }, shots: { type: 'string', default: '10' } } });
const outDir = resolve(repoRoot, values.out);
mkdirSync(outDir, { recursive: true });
const metaDir = resolve(repoRoot, 'measurements', 'day2', 'demo');
mkdirSync(metaDir, { recursive: true });
const LEAD_IN_SEC = 0.6;
const WINDOW_SEC = Number(values.seconds);
const HEAD_START_MS = 1200;
const VIDEO_FPS = 30;
const PEAK_TARGET = 0.7;
const server = startSiteServer({ port: 8796, outDir: metaDir });
await sleep(800);
let session;
const tmp = mkdtempSync(join(tmpdir(), 'downbeat-demo2-'));
try {
  session = await openProduct({ port: 8796, debugPort: 9366, query: 'input=hand&tap=1', video: values.video, height: 600 });
  const { cdp, consoleMessages } = session;
  await sleep(800);
  await startAndWaitReady(cdp);
  if ((await waitTrackerSettled(cdp)) !== 2) throw new Error('camera tracker did not start');
  await sleep(2500);

  const frames = [];
  cdp.on('Page.screencastFrame', (p) => {
    frames.push({ epochSec: p.metadata.timestamp, data: p.data });
    cdp.send('Page.screencastFrameAck', { sessionId: p.sessionId }).catch(() => {});
  });
  await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 88, maxWidth: 1600, maxHeight: 600, everyNthFrame: 1 });
  await sleep(HEAD_START_MS);
  const mapping = await cdp.evaluate(`(() => {
    window.__tap.start();
    const ctx = window.Downbeat.audio.context();
    const ts = ctx.getOutputTimestamp();
    return { timeOrigin: performance.timeOrigin, heardOffsetMs: ts.performanceTime - ts.contextTime * 1000, sampleRate: ctx.sampleRate, outputLatency: ctx.outputLatency, tapStartPerfMs: performance.now() };
  })()`);

  await sleep((WINDOW_SEC + LEAD_IN_SEC + 3) * 1000);
  const endEpochSec = Date.now() / 1000;
  await cdp.send('Page.stopScreencast');
  const notes = JSON.parse(await cdp.evaluate('JSON.stringify(window.Downbeat.page.noteLog)'));
  const after = notes.filter((n) => n.loggedMs >= mapping.tapStartPerfMs && Number.isFinite(n.whenPerfMs));
  if (after.length < 8) throw new Error(`only ${after.length} notes during the recording`);

  const audio = await cdp.evaluate(`(async () => {
    const recs = await window.__tap.stop();
    const rec = recs.filter((r) => r.samples && r.samples.some((v) => Math.abs(v) > 1e-4))[0];
    if (!rec) return { error: 'no audio captured' };
    window.__demoRec = rec;
    return { firstFrame: rec.firstFrame, sampleRate: rec.sampleRate, length: rec.samples.length };
  })()`);
  if (audio.error) throw new Error(audio.error);
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

  // the window starts a little before the first heard note and lasts WINDOW_SEC
  const firstHeardEpoch = (mapping.timeOrigin + after[0].whenPerfMs) / 1000;
  const w0 = firstHeardEpoch - LEAD_IN_SEC;
  const w1 = w0 + WINDOW_SEC;
  if (w1 > endEpochSec) throw new Error('recording ended before the window did');
  const sorted = [...frames].sort((a, b) => a.epochSec - b.epochSec);
  const plan = planFrames(sorted, w0, w1);
  const videoFile = encodeVideo({ frames: sorted, plan, dir: tmp, fps: VIDEO_FPS, width: 1600, height: 600 });

  const sr = audio.sampleRate;
  const out = new Float32Array(Math.round((w1 - w0) * sr));
  let peak = 0;
  for (let i = 0; i < out.length; i++) {
    const epochMs = (w0 + i / sr) * 1000;
    const ctxTime = (epochMs - mapping.timeOrigin - mapping.heardOffsetMs) / 1000;
    const j = Math.round(ctxTime * sr) - audio.firstFrame;
    const v = j >= 0 && j < samples.length ? samples[j] : 0;
    out[i] = v;
    if (Math.abs(v) > peak) peak = Math.abs(v);
  }
  const gain = peak > 0 ? PEAK_TARGET / peak : 1;
  for (let i = 0; i < out.length; i++) out[i] *= gain;
  writeFileSync(join(tmp, 'audio.wav'), encodeWavPcm16Mono(out, sr));
  runFfmpeg(['-i', videoFile, '-i', join(tmp, 'audio.wav'), '-c:v', 'copy', '-c:a', 'aac', '-b:a', '160k', '-shortest', '-movflags', '+faststart', join(outDir, 'day2-demo.mp4')]);

  // screenshot candidates: a short second pass, one picture shortly after each new note (the flash is on screen then)
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1600, height: 600, deviceScaleFactor: 1, mobile: false });
  let seen = await cdp.evaluate('window.Downbeat.page.noteLog.length');
  let shot = 0;
  const shotDeadline = Date.now() + 20000;
  while (shot < Number(values.shots) && Date.now() < shotDeadline) {
    await sleep(40);
    const n = await cdp.evaluate('window.Downbeat.page.noteLog.length');
    if (n === seen) continue;
    seen = n;
    await sleep(110);
    const img = await cdp.send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(join(metaDir, `screenshot-candidate-${++shot}.png`), Buffer.from(img.data, 'base64'));
  }

  const meta = {
    chrome: session.chrome.version.Browser, video: 'fake camera (tools/make_strike_video.py hands1)', notesDuringRecording: after.length, screencastFrames: plan.length, videoSeconds: w1 - w0,
    audio: { sampleRate: sr, recordedSeconds: samples.length / sr, peakBeforeNormalize: peak, appliedGain: gain }, heardOffsetMs: mapping.heardOffsetMs, outputLatencyEstimate: mapping.outputLatency,
    consoleMessages: consoleMessages.filter((m) => !/gl_context|NORM_RECT/.test(m.text)),
  };
  writeFileSync(join(metaDir, 'demo-meta.json'), JSON.stringify(meta, null, 2));
  console.log(JSON.stringify(meta, null, 2));
} finally {
  await session?.chrome.close();
  server.stop();
  rmSync(tmp, { recursive: true, force: true });
}
