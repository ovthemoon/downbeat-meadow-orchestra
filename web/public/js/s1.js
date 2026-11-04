// S-1 beat clock test page logic. Exposes window.day0.runBeat / runAhead / init for the CDP runner and for manual buttons.
import {
  day0, saveJson, saveToServer, runtimeInfo, startUnity, sendToUnity, sleep, waitForEvent, watchPageState,
  createRafSampler, eventsSince,
} from './day0-common.js';
import { startBlocker } from './stress.js';
import { ensureWebAudio, startWebAudioBeat } from './web-audio-beat.js';

const SILENT_PEAK = 1e-4;
const TAIL_MS = 600;
const UNITY_PATHS = Object.freeze({ 'unity-scheduled': 'scheduled', 'unity-play': 'play' });
let unity = null;

export async function init({ build, canvas, vsync = 1, runInBackground = true }) {
  watchPageState();
  const started = await startUnity(build, canvas);
  unity = started.instance;
  day0.unity = unity; // exposed for debugging from the DevTools runner
  await waitForEvent('ready', { timeoutMs: 60000 });
  const applied = waitForEvent('vsync', { timeoutMs: 5000 });
  sendToUnity(unity, 'SetVsync', { count: vsync });
  await applied;
  const bgApplied = waitForEvent('runInBackground', { timeoutMs: 5000 });
  sendToUnity(unity, 'SetRunInBackground', { value: runInBackground });
  await bgApplied;
  await window.__tap.whenReady();
  await window.__tap.resumeAll();
  await ensureWebAudio(); // create the page's own context now, so no run pays for context start-up
  const clockStartMs = await waitForClocks();
  return { bootMs: started.bootMs, vsync, runInBackground, clockStartMs };
}

/** AudioContext.currentTime can sit still for a while after resume(); wait until every context clock is really advancing. */
async function waitForClocks(minSeconds = 2, timeoutMs = 15000) {
  const t0 = performance.now();
  while (performance.now() - t0 < timeoutMs) {
    if (window.__tap.contexts().every((e) => e.ctx.currentTime >= minSeconds)) return performance.now() - t0;
    await sleep(100);
  }
  throw new Error('an AudioContext clock did not start advancing');
}

function peakOf(samples) {
  let peak = 0;
  for (let i = 0; i < samples.length; i++) {
    const a = Math.abs(samples[i]);
    if (a > peak) peak = a;
  }
  return peak;
}

async function persistRecordings(runDir, label, recordings) {
  const contexts = [];
  for (const rec of recordings) {
    const peak = rec.samples ? peakOf(rec.samples) : 0;
    const info = {
      id: rec.id, tag: rec.tag, sampleRate: rec.sampleRate, state: rec.state, baseLatency: rec.baseLatency,
      outputLatency: rec.outputLatency, firstFrame: rec.firstFrame, gaps: rec.gaps, peak,
      frames: rec.samples ? rec.samples.length : 0, workletError: rec.workletError, rawFile: null,
    };
    if (rec.samples && peak > SILENT_PEAK) {
      const name = `${runDir}/raw-audio/${label}.ctx${rec.id}-${rec.tag}.f32`;
      await saveToServer(name, new Uint8Array(rec.samples.buffer));
      info.rawFile = name;
    }
    contexts.push(info);
  }
  return contexts;
}

async function runUnityBeat(cfg) {
  const t0 = performance.now();
  const args = {
    path: UNITY_PATHS[cfg.path], label: cfg.label, bpm: cfg.bpm, slotsPerBeat: cfg.slotsPerBeat,
    durationSec: cfg.durationSec, lookaheadSec: cfg.lookaheadSec, startDelaySec: cfg.startDelaySec,
  };
  const done = waitForEvent('beat', { timeoutMs: (cfg.durationSec + cfg.startDelaySec + 40) * 1000, predicate: (d) => d.label === cfg.label });
  sendToUnity(unity, 'BeatStart', args);
  const event = await done;
  return { summary: event.data, unityEvents: eventsSince(t0).filter((e) => e.kind === 'dsp' || e.kind === 'error') };
}

async function runWebAudioBeat(cfg) {
  const { ctx, buffer } = await ensureWebAudio();
  const handle = startWebAudioBeat({ ctx, buffer, ...cfg });
  await sleep((cfg.durationSec + cfg.startDelaySec) * 1000 + 500);
  return { summary: { ...handle.stop(), label: cfg.label }, unityEvents: [] };
}

/**
 * Run one 60 s (default) beat test, record the output tap, save raw audio + metadata on the server.
 * @param cfg {label, path, bpm, slotsPerBeat, durationSec, lookaheadSec, startDelaySec, block:{ms,everyMs}|null, runDir}
 */
export async function runBeat(userCfg) {
  const cfg = { bpm: 96, slotsPerBeat: 4, durationSec: 60, lookaheadSec: 0.1, startDelaySec: 0.6, block: null, runDir: 's1/adhoc', frameDivisor: 1, ...userCfg };
  if (!unity && cfg.path !== 'webaudio') throw new Error('Unity not initialised');
  await window.__tap.resumeAll();
  const pageEventsBefore = day0.state.pageEvents.length;
  const raf = createRafSampler();
  const startedPerfMs = performance.now();
  window.__tap.start();
  raf.start();
  const blocker = cfg.block ? startBlocker(cfg.block) : null;

  let result;
  try {
    result = cfg.path === 'webaudio' ? await runWebAudioBeat(cfg) : await runUnityBeat(cfg);
  } finally {
    await sleep(TAIL_MS);
  }
  const blocks = blocker ? blocker.stop() : [];
  const rafStats = raf.stop();
  const recordings = await window.__tap.stop();
  const contexts = await persistRecordings(cfg.runDir, cfg.label, recordings);

  const meta = {
    config: cfg,
    startedPerfMs,
    endedPerfMs: performance.now(),
    runtime: runtimeInfo(),
    tapMuted: window.__tap.muted,
    contexts,
    marks: window.__tap.marks().filter((m) => m.perfMs >= startedPerfMs - 1),
    pageEvents: day0.state.pageEvents.slice(Math.max(0, pageEventsBefore - 1)),
    blocks,
    rafIntervalsMs: rafStats,
    ...result,
  };
  await saveJson(`${cfg.runDir}/${cfg.label}.meta.json`, meta);
  return {
    label: cfg.label,
    issued: result.summary.issued,
    skippedLate: result.summary.skippedLate,
    contexts: contexts.map((c) => ({ id: c.id, tag: c.tag, peak: c.peak, rawFile: c.rawFile })),
    rafIntervalsMs: rafStats,
  };
}

/** PlayScheduled(dspTime + ahead) check: do the clicks come `ahead` seconds later, or immediately? */
export async function runAhead(userCfg) {
  const cfg = { aheadSec: [0.25, 0.5, 1.0, 2.0], spacingSec: 3.0, runDir: 's1/adhoc', label: 'ahead', ...userCfg };
  await window.__tap.resumeAll();
  const startedPerfMs = performance.now();
  window.__tap.start();
  const done = waitForEvent('ahead', { timeoutMs: 60000 });
  sendToUnity(unity, 'AheadStart', { aheadSec: cfg.aheadSec, spacingSec: cfg.spacingSec });
  const event = await done;
  await sleep(max(...cfg.aheadSec) * 1000 + 800);
  const recordings = await window.__tap.stop();
  const contexts = await persistRecordings(cfg.runDir, cfg.label, recordings);
  const meta = {
    config: cfg, startedPerfMs, runtime: runtimeInfo(), contexts,
    marks: window.__tap.marks().filter((m) => m.perfMs >= startedPerfMs - 1),
    summary: event.data,
  };
  await saveJson(`${cfg.runDir}/${cfg.label}.meta.json`, meta);
  return { label: cfg.label, records: event.data.records.length, contexts: contexts.map((c) => ({ id: c.id, tag: c.tag, peak: c.peak })) };
}

function max(...values) {
  return values.reduce((m, v) => (v > m ? v : m), -Infinity);
}

export async function ping() {
  const reply = waitForEvent('pong', { timeoutMs: 5000 });
  sendToUnity(unity, 'Ping');
  return (await reply).data;
}

Object.assign(day0, { init, runBeat, runAhead, ping });
