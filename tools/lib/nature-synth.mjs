// Synthesized sounds of the nature layers (fireflies = glass bells, sky = soft pad, hills = low drum). Pure functions, no randomness: the same call always gives the
// same samples, so the WAV files and their hashes are reproducible. All of them return a mono Float32Array that starts at sample 0 with silence-level amplitude and
// ends faded to zero, scaled to `peak`.
import { midiToHz, noteToMidi } from './pitch.mjs';

export const NATURE_SAMPLE_RATE = 32000;

const TWO_PI = 2 * Math.PI;
const raisedCosine = (x) => 0.5 * (1 - Math.cos(Math.PI * Math.min(1, Math.max(0, x))));

function scaleToPeak(samples, peak) {
  let max = 0;
  for (let i = 0; i < samples.length; i++) max = Math.max(max, Math.abs(samples[i]));
  if (max === 0) throw new Error('rendered silence');
  const gain = peak / max;
  for (let i = 0; i < samples.length; i++) samples[i] *= gain;
  return samples;
}

/** The end of the sound: a raised-cosine fade over the last `fadeSec`. */
function fadeEnd(samples, sampleRate, fadeSec) {
  const frames = Math.round(fadeSec * sampleRate);
  for (let i = 0; i < frames; i++) samples[samples.length - 1 - i] *= raisedCosine(i / frames);
  return samples;
}

/** A glass-bell strike: the fundamental plus four partials that die faster the higher they are (one slightly inharmonic, like a small bell). */
export function renderBell(hz, { sampleRate = NATURE_SAMPLE_RATE, durationSec = 2.4, attackSec = 0.002, fadeOutSec = 0.3, peak = 0.55 } = {}) {
  const partials = [
    { ratio: 1, amp: 1, tauSec: 1.0 },
    { ratio: 2, amp: 0.38, tauSec: 0.55 },
    { ratio: 2.76, amp: 0.1, tauSec: 0.4 },
    { ratio: 3.01, amp: 0.16, tauSec: 0.32 },
    { ratio: 4.97, amp: 0.07, tauSec: 0.18 },
  ];
  const out = new Float32Array(Math.round(durationSec * sampleRate));
  for (let i = 0; i < out.length; i++) {
    const t = i / sampleRate;
    let v = 0;
    for (const p of partials) {
      if (p.ratio * hz >= sampleRate / 2) continue;
      v += p.amp * Math.exp(-t / p.tauSec) * Math.sin(TWO_PI * hz * p.ratio * t);
    }
    out[i] = v * Math.min(1, t / attackSec);
  }
  return scaleToPeak(fadeEnd(out, sampleRate, fadeOutSec), peak);
}

/** A soft pad: three slightly detuned voices of a fundamental with two quiet overtones, a slow attack and a long fade, so a chord of them breathes in and out. */
export function renderPad(hz, { sampleRate = NATURE_SAMPLE_RATE, durationSec = 5.6, attackSec = 0.4, fadeOutSec = 1.4, peak = 0.4 } = {}) {
  // one main voice and two quieter ones a few cents away: the slow beating between them is the "breathing"; kept weak so the loudest moment is not much louder than the rest
  const voices = [{ cents: 0, amp: 1, phase: 0 }, { cents: -4, amp: 0.35, phase: 2.1 }, { cents: 4, amp: 0.35, phase: 4.2 }];
  const overtones = [{ ratio: 1, amp: 1 }, { ratio: 2, amp: 0.3 }, { ratio: 3, amp: 0.1 }];
  const out = new Float32Array(Math.round(durationSec * sampleRate));
  for (let i = 0; i < out.length; i++) {
    const t = i / sampleRate;
    let v = 0;
    for (const voice of voices) {
      const voiceHz = hz * 2 ** (voice.cents / 1200);
      for (const o of overtones) v += voice.amp * o.amp * Math.sin(TWO_PI * voiceHz * o.ratio * t + voice.phase);
    }
    out[i] = v * raisedCosine(t / attackSec);
  }
  return scaleToPeak(fadeEnd(out, sampleRate, fadeOutSec), peak);
}

/** A low drum: a body that falls from twice its pitch to the pitch in 60 ms, a quieter octave above, and a short knock. */
export function renderDrum(hz, { sampleRate = NATURE_SAMPLE_RATE, durationSec = 1.0, fadeOutSec = 0.2, peak = 0.708 } = {}) {
  const dropSec = 0.03;
  const out = new Float32Array(Math.round(durationSec * sampleRate));
  let bodyPhase = 0;
  for (let i = 0; i < out.length; i++) {
    const t = i / sampleRate;
    const body = Math.exp(-t / 0.25) * Math.sin(bodyPhase);
    bodyPhase += (TWO_PI * hz * (1 + Math.exp(-t / dropSec))) / sampleRate;
    const octave = 0.35 * Math.exp(-t / 0.12) * Math.sin(TWO_PI * hz * 2 * t);
    const knock = 0.3 * Math.exp(-t / 0.04) * Math.sin(TWO_PI * hz * 3 * t);
    out[i] = body + octave + knock;
  }
  return scaleToPeak(fadeEnd(out, sampleRate, fadeOutSec), peak);
}

/** Which sounds to make: one entry per file. */
export const NATURE_SPECS = Object.freeze([
  ...['A5', 'C6', 'D6', 'E6', 'G6'].map((note) => ({ file: `firefly_${note}`, instrument: 'firefly-bell', kind: 'bell', note, peak: 0.55, pitchWindowSec: [0.1, 0.4] })),
  ...['A3', 'C4', 'E4', 'G4'].map((note) => ({ file: `pad_${note}`, instrument: 'sky-pad', kind: 'pad', note, peak: 0.4, pitchWindowSec: [0.8, 1.4] })),
  { file: 'drum_A2', instrument: 'hill-drum', kind: 'drum', note: 'A2', peak: 0.708, pitchWindowSec: [0.3, 0.6] },
]);

const RENDERERS = { bell: renderBell, pad: renderPad, drum: renderDrum };

export function renderSpec(spec, sampleRate = NATURE_SAMPLE_RATE) {
  return RENDERERS[spec.kind](midiToHz(noteToMidi(spec.note)), { sampleRate, peak: spec.peak });
}
