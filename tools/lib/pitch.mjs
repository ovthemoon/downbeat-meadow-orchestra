// Pitch helpers for the sample tool (S-6): note names, MIDI, and a YIN-style fundamental frequency estimate.

const SEMITONE_OF = Object.freeze({ C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 });
const NOTE_NAMES = Object.freeze(['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']);
const A4_MIDI = 69;
const A4_HZ = 440;

export function noteToMidi(name) {
  const match = /^([A-G])([#b]?)(-?\d)$/.exec(name);
  if (!match) throw new Error(`bad note name "${name}"`);
  const accidental = match[2] === '#' ? 1 : match[2] === 'b' ? -1 : 0;
  return 12 * (Number(match[3]) + 1) + SEMITONE_OF[match[1]] + accidental;
}

export function midiToNote(midi) {
  return `${NOTE_NAMES[((midi % 12) + 12) % 12]}${Math.floor(midi / 12) - 1}`;
}

export const midiToHz = (midi) => A4_HZ * 2 ** ((midi - A4_MIDI) / 12);
export const hzToMidiFloat = (hz) => A4_MIDI + 12 * Math.log2(hz / A4_HZ);

/** Cents difference of a measured frequency from an expected MIDI note. */
export function centsFrom(hz, midi) {
  return (hzToMidiFloat(hz) - midi) * 100;
}

/**
 * YIN fundamental frequency estimate for a (mostly) periodic window.
 * Returns null when no reliable period is found.
 */
export function estimateF0(samples, sampleRate, { fmin = 60, fmax = 2500, threshold = 0.15 } = {}) {
  const tauMin = Math.max(2, Math.floor(sampleRate / fmax));
  const tauMax = Math.ceil(sampleRate / fmin);
  const span = samples.length - tauMax;
  if (span < tauMax) throw new Error('window too short for the lowest frequency');

  const diff = new Float64Array(tauMax + 1);
  for (let tau = 1; tau <= tauMax; tau++) {
    let sum = 0;
    for (let j = 0; j < span; j++) {
      const d = samples[j] - samples[j + tau];
      sum += d * d;
    }
    diff[tau] = sum;
  }
  const normalized = new Float64Array(tauMax + 1);
  normalized[0] = 1;
  let running = 0;
  for (let tau = 1; tau <= tauMax; tau++) {
    running += diff[tau];
    normalized[tau] = running === 0 ? 1 : (diff[tau] * tau) / running;
  }

  let best = -1;
  for (let tau = tauMin; tau < tauMax; tau++) {
    if (normalized[tau] < threshold) {
      while (tau + 1 < tauMax && normalized[tau + 1] < normalized[tau]) tau++;
      best = tau;
      break;
    }
  }
  if (best < 0) return null;

  const a = normalized[best - 1];
  const b = normalized[best];
  const c = normalized[best + 1];
  const denominator = a - 2 * b + c;
  const refined = denominator === 0 ? best : best + (a - c) / (2 * denominator);
  return sampleRate / refined;
}
