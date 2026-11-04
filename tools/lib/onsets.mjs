// Click onset detection and beat-grid deviation statistics (Day 0, S-1).
// Pure functions: inputs are never mutated, outputs are fresh objects.

const DEFAULTS = Object.freeze({
  thresholdRel: 0.1, // onset threshold as a fraction of the global peak
  envelopeSec: 0.001, // peak-hold time constant used to decide when a click is over
  quietSec: 0.003, // envelope must stay below the re-arm level this long before the next onset
  rearmRel: 0.25, // re-arm level as a fraction of the threshold
  minPeak: 1e-4, // below this the signal is treated as silence
});

/**
 * Find click onsets in a mono signal. Onset = first threshold crossing of |x|,
 * refined by linear interpolation between the two straddling samples.
 * @param {Float32Array} samples
 * @param {number} sampleRate
 * @param {Partial<typeof DEFAULTS>} [options]
 * @returns {{onsets: {frame:number, timeSec:number, peak:number}[], threshold:number, peak:number}}
 */
export function detectOnsets(samples, sampleRate, options = {}) {
  const cfg = { ...DEFAULTS, ...options };
  const peak = maxAbs(samples);
  if (peak < cfg.minPeak) return { onsets: [], threshold: 0, peak };

  const threshold = peak * cfg.thresholdRel;
  const rearmLevel = threshold * cfg.rearmRel;
  const decay = Math.exp(-1 / (sampleRate * cfg.envelopeSec));
  const quietFrames = Math.max(1, Math.round(cfg.quietSec * sampleRate));

  const onsets = [];
  let envelope = 0;
  let armed = true;
  let quietRun = 0;
  let current = null;
  for (let i = 0; i < samples.length; i++) {
    const a = Math.abs(samples[i]);
    envelope = Math.max(a, envelope * decay);
    if (armed) {
      // Only an onset disarms the detector, so a slow ramp that passes the re-arm level first still counts.
      if (a >= threshold) {
        const prev = i > 0 ? Math.abs(samples[i - 1]) : 0;
        const frame = i - 1 + (a === prev ? 1 : (threshold - prev) / (a - prev));
        current = { frame, timeSec: frame / sampleRate, peak: a };
        onsets.push(current);
        armed = false;
        quietRun = 0;
      }
      continue;
    }
    if (a > current.peak) current.peak = a;
    quietRun = envelope < rearmLevel ? quietRun + 1 : 0;
    if (quietRun >= quietFrames) armed = true;
  }
  return { onsets, threshold, peak };
}

/**
 * Compare onset times with an ideal grid of `slotSec` spacing.
 * Slots are assigned by phase unwrapping: each onset's slot is the previous onset's slot plus the rounded number of
 * slots between them. This keeps working when the clicks drift by more than half a slot over a run
 * (rounding against the first onset alone would wrap and hide a large drift).
 * A constant latency is absorbed by the first onset; a linear trend is reported as drift.
 * @param {number[]} timesSec ascending onset times
 * @param {number} slotSec
 */
export function computeGridPoints(timesSec, slotSec) {
  if (timesSec.length < 2) throw new Error('need at least two onsets');
  if (!(slotSec > 0)) throw new Error('slotSec must be positive');
  const t0 = timesSec[0];
  let slot = 0;
  const raw = timesSec.map((t, k) => {
    if (k > 0) slot += Math.max(0, Math.round((t - timesSec[k - 1]) / slotSec));
    const rel = t - t0;
    return { t: rel, slot, errorSec: rel - slot * slotSec };
  });
  const { slope, intercept } = linearFit(raw.map((p) => p.t), raw.map((p) => p.errorSec));
  const points = raw.map((p) => ({
    t: p.t,
    slot: p.slot,
    rawMs: p.errorSec * 1000,
    residualMs: (p.errorSec - (intercept + slope * p.t)) * 1000,
  }));
  return { points, driftMsPerMin: slope * 60 * 1000, offsetMs: intercept * 1000 };
}

/**
 * Residuals of onset times against a grid whose offset and drift were fitted elsewhere
 * (for example on the quiet part of a run), so a later disturbance cannot hide inside the fit.
 * @param {number[]} timesSec
 * @param {number} originSec time of slot 0 of the fitted grid (the first onset used for the fit)
 * @param {number} slotSec
 * @param {{offsetMs:number, driftMsPerMin:number}} fit
 */
export function pointsAgainstFit(timesSec, originSec, slotSec, fit) {
  if (!(slotSec > 0)) throw new Error('slotSec must be positive');
  const offsetSec = fit.offsetMs / 1000;
  const driftPerSec = fit.driftMsPerMin / 60000;
  return timesSec.map((t) => {
    const rel = t - originSec;
    const slot = Math.round((rel - offsetSec - driftPerSec * rel) / slotSec); // expected position includes the fitted drift
    const errorSec = rel - slot * slotSec;
    return { t: rel, slot, rawMs: errorSec * 1000, residualMs: (errorSec - (offsetSec + driftPerSec * rel)) * 1000 };
  });
}

/** Summary statistics (ms) of a list of values. Population std; percentile uses linear interpolation. */
export function summarizeMs(values) {
  if (values.length === 0) return { count: 0, meanMs: null, stdMs: null, p95AbsMs: null, maxAbsMs: null };
  const mean = values.reduce((s, v) => s + v, 0) / values.length;
  const variance = values.reduce((s, v) => s + (v - mean) ** 2, 0) / values.length;
  const abs = values.map(Math.abs).sort((a, b) => a - b);
  return {
    count: values.length,
    meanMs: mean,
    stdMs: Math.sqrt(variance),
    p95AbsMs: percentile(abs, 0.95),
    maxAbsMs: abs[abs.length - 1],
  };
}

/** Slot coverage: how many ideal slots between first and last onset were hit, missed, or hit twice. */
export function slotCoverage(points) {
  const counts = new Map();
  for (const p of points) counts.set(p.slot, (counts.get(p.slot) ?? 0) + 1);
  const first = Math.min(...counts.keys());
  const last = Math.max(...counts.keys());
  const expected = last - first + 1;
  const duplicates = [...counts.values()].filter((c) => c > 1).length;
  return { expected, hit: counts.size, missed: expected - counts.size, duplicates };
}

/** Full report for one recording. */
export function analyzeGrid(timesSec, slotSec) {
  const { points, driftMsPerMin, offsetMs } = computeGridPoints(timesSec, slotSec);
  const intervals = [];
  for (let i = 1; i < timesSec.length; i++) intervals.push((timesSec[i] - timesSec[i - 1]) * 1000);
  const interval = summarizeMs(intervals.map((v) => v - slotSec * 1000));
  return {
    onsetCount: timesSec.length,
    durationSec: timesSec[timesSec.length - 1] - timesSec[0],
    slots: slotCoverage(points),
    offsetMs,
    driftMsPerMin,
    residual: summarizeMs(points.map((p) => p.residualMs)),
    raw: summarizeMs(points.map((p) => p.rawMs)),
    intervalErrorMs: interval,
    points,
  };
}

function maxAbs(samples) {
  let m = 0;
  for (let i = 0; i < samples.length; i++) {
    const a = Math.abs(samples[i]);
    if (a > m) m = a;
  }
  return m;
}

function linearFit(xs, ys) {
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

function percentile(sortedAsc, p) {
  if (sortedAsc.length === 1) return sortedAsc[0];
  const rank = p * (sortedAsc.length - 1);
  const lo = Math.floor(rank);
  const hi = Math.ceil(rank);
  return sortedAsc[lo] + (sortedAsc[hi] - sortedAsc[lo]) * (rank - lo);
}
