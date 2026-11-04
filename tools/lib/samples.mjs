// Sample preparation helpers (S-6): leading silence, attack time, trimming. Pure functions on mono Float32Array.

const DB = (x) => 20 * Math.log10(x);

export function peakOf(samples) {
  let peak = 0;
  for (let i = 0; i < samples.length; i++) peak = Math.max(peak, Math.abs(samples[i]));
  return peak;
}

function firstFrameAtOrAbove(samples, level) {
  for (let i = 0; i < samples.length; i++) if (Math.abs(samples[i]) >= level) return i;
  return -1;
}

/**
 * How long the file is silent before the sound starts, under three definitions (ms):
 *  - absMinus60dbfs: first sample at or above -60 dBFS (what a listener or a player would call "starts")
 *  - relMinus40db:   first sample 40 dB below the file's peak (robust to quiet files)
 *  - half:           first sample at 50% of the peak (the perceived start of a slow attack)
 */
export function leadingSilence(samples, sampleRate) {
  const peak = peakOf(samples);
  if (peak === 0) throw new Error('file is digital silence');
  const toMs = (frame) => (frame < 0 ? null : (frame / sampleRate) * 1000);
  const abs60 = firstFrameAtOrAbove(samples, 10 ** (-60 / 20));
  const rel40 = firstFrameAtOrAbove(samples, peak * 10 ** (-40 / 20));
  const half = firstFrameAtOrAbove(samples, peak * 0.5);
  return {
    peak,
    peakDbfs: DB(peak),
    absMinus60dbfsMs: toMs(abs60),
    relMinus40dbMs: toMs(rel40),
    halfPeakMs: toMs(half),
    absMinus60dbfsFrame: abs60,
    relMinus40dbFrame: rel40,
  };
}

/**
 * Cut `lengthSec` starting `preRollSec` before `onsetFrame`, fade the end with a raised-cosine, scale to `targetPeak`.
 * Returns a new array and the gain applied.
 */
export function trimFadeNormalize(samples, sampleRate, { onsetFrame, preRollSec = 0.002, lengthSec = 1.3, fadeOutSec = 0.15, targetPeak = 0.708 }) {
  const start = Math.max(0, onsetFrame - Math.round(preRollSec * sampleRate));
  const length = Math.min(samples.length - start, Math.round(lengthSec * sampleRate));
  const out = new Float32Array(length);
  const fadeFrames = Math.min(length, Math.round(fadeOutSec * sampleRate));
  for (let i = 0; i < length; i++) {
    const fromEnd = length - 1 - i;
    const fade = fromEnd >= fadeFrames ? 1 : 0.5 * (1 - Math.cos((Math.PI * fromEnd) / fadeFrames));
    out[i] = samples[start + i] * fade;
  }
  const peak = peakOf(out);
  const gain = peak > 0 ? targetPeak / peak : 1;
  return { samples: out.map((v) => v * gain), gain, startFrame: start };
}
