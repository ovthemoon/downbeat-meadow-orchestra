// Pure helpers for the S-1 analysis (metadata handling, segments, clock relations). No file access here.
import { summarizeMs } from './onsets.mjs';

export const CRITERIA = Object.freeze({ stdMs: 3, maxMs: 10, driftMsPerMin: 5 });

/**
 * Window during which the page was hidden or blurred, from the page's own event log (perf ms).
 * `hasFocus` is ignored on purpose: Unity runs with runInBackground on, and the test browser is often not the frontmost app.
 * `blurred` follows blur/focus events, `visibility` follows visibilitychange.
 * Returns null if the page stayed visible and not blurred after `fromPerfMs`.
 */
export function disturbanceWindow(pageEvents, { fromPerfMs = -Infinity } = {}) {
  const normal = (e) => e.visibility === 'visible' && !e.blurred;
  const events = pageEvents.filter((e) => e.type !== 'start' && e.perfMs >= fromPerfMs);
  const first = events.find((e) => !normal(e));
  if (!first) return null;
  const back = events.find((e) => e.perfMs > first.perfMs && normal(e));
  return { startMs: first.perfMs, endMs: back ? back.perfMs : Infinity };
}

/** Map a context time (s) to page performance.now() (ms) using a mark that holds both. */
export function ctxToPerfMapper(marks, ctxId, markPrefix = 'beat-start') {
  const mark = marks.find((m) => m.label.startsWith(markPrefix) && m.ctxTimes[ctxId] !== undefined);
  if (!mark) throw new Error(`no ${markPrefix} mark with a time for context ${ctxId}`);
  return (ctxSec) => mark.perfMs + (ctxSec - mark.ctxTimes[ctxId]) * 1000;
}

/** Split points by perf time into before / during / after the disturbance window. */
export function splitByWindow(points, perfOf, window) {
  if (!window) return { all: points };
  const before = [];
  const during = [];
  const after = [];
  for (const p of points) {
    const perf = perfOf(p);
    if (perf < window.startMs) before.push(p);
    else if (perf < window.endMs) during.push(p);
    else after.push(p);
  }
  return { before, during, after };
}

/** How faithfully AudioSettings.dspTime tracks the AudioContext clock (samples of {dspSec, ctxSec}). */
export function dspVersusContext(samples, skipFirst = 2) {
  // The first samples are taken while the AudioContext is still starting up (its clock is behind), so they are skipped.
  const usable = samples.filter((s) => s.ctxSec >= 0).slice(skipFirst);
  if (usable.length < 3) return { count: usable.length, note: 'too few samples with a context time' };
  const offsets = usable.map((s) => s.dspSec - s.ctxSec);
  const xs = usable.map((s) => s.ctxSec);
  const { slope, intercept } = fitLine(xs, offsets);
  const residuals = usable.map((s, i) => (offsets[i] - (intercept + slope * xs[i])) * 1000);
  const sorted = [...offsets].sort((a, b) => a - b);
  return {
    count: usable.length,
    medianOffsetSec: sorted[Math.floor(sorted.length / 2)],
    driftMsPerMin: slope * 60000,
    residual: summarizeMs(residuals),
    dspAdvancedSec: usable[usable.length - 1].dspSec - usable[0].dspSec,
    ctxAdvancedSec: usable[usable.length - 1].ctxSec - usable[0].ctxSec,
  };
}

/** Pairs scheduled-ahead records with detected onsets (in order) and reports the real delay. */
export function aheadDelays(records, onsetCtxTimes) {
  return records.map((record, i) => {
    const onset = onsetCtxTimes[i];
    const delaySec = onset === undefined ? null : onset - record.ctx0;
    return {
      aheadSec: record.aheadSec,
      delaySec,
      errorMs: delaySec === null ? null : (delaySec - record.aheadSec) * 1000,
      playedImmediately: delaySec !== null && delaySec < record.aheadSec / 2,
    };
  });
}

export function evaluateCriteria(residual, driftMsPerMin) {
  return {
    std: residual.stdMs <= CRITERIA.stdMs,
    max: residual.maxAbsMs <= CRITERIA.maxMs,
    drift: Math.abs(driftMsPerMin) <= CRITERIA.driftMsPerMin,
  };
}

function fitLine(xs, ys) {
  const n = xs.length;
  const mx = xs.reduce((s, v) => s + v, 0) / n;
  const my = ys.reduce((s, v) => s + v, 0) / n;
  let sxx = 0;
  let sxy = 0;
  for (let i = 0; i < n; i++) {
    sxx += (xs[i] - mx) ** 2;
    sxy += (xs[i] - mx) * (ys[i] - my);
  }
  const slope = sxx === 0 ? 0 : sxy / sxx;
  return { slope, intercept: my - slope * mx };
}
