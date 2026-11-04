import { test } from 'node:test';
import assert from 'node:assert/strict';
import { assertPitchMatchesLabel, measureSourcePitch, pitchRangeFor } from './source-pitch.mjs';

const SR = 44100;
const WINDOW_SEC = 0.3;

function tone(hz, seconds = WINDOW_SEC) {
  const out = new Float32Array(Math.round(SR * seconds));
  for (let i = 0; i < out.length; i++) out[i] = Math.sin((2 * Math.PI * hz * i) / SR);
  return out;
}

const noise = () => Float32Array.from({ length: Math.round(SR * WINDOW_SEC) }, (_, i) => Math.sin(i * i));

test('pitchRangeFor defaults to 80..2500 Hz and lets a source widen it', () => {
  assert.deepEqual(pitchRangeFor({ note: 'A4' }), { fmin: 80, fmax: 2500 });
  assert.deepEqual(pitchRangeFor({ note: 'C8', fmax: 4500 }), { fmin: 80, fmax: 4500 });
  assert.deepEqual(pitchRangeFor({ note: 'A1', fmin: 40 }), { fmin: 40, fmax: 2500 });
});

test('measureSourcePitch measures a pitched source against its sounding note', () => {
  const pitch = measureSourcePitch({ note: 'A4' }, tone(440), SR);
  assert.ok(Math.abs(pitch.hz - 440) < 1, `hz ${pitch.hz}`);
  assert.equal(pitch.measuredMidi, 69);
  assert.ok(Math.abs(pitch.centsFromLabel) < 2, `cents ${pitch.centsFromLabel}`);
});

test('measureSourcePitch does not measure an unpitched source and reports no tuning error', () => {
  const pitch = measureSourcePitch({ note: 'A2', unpitched: true }, noise(), SR);
  assert.equal(pitch.hz, null);
  assert.equal(pitch.measuredMidi, null);
  assert.equal(pitch.centsFromLabel, 0);
});

test('a sound above the default 2500 Hz limit is refused, and measured correctly once the source widens fmax', () => {
  const source = { note: 'G7', outName: 'x' };
  assert.throws(() => assertPitchMatchesLabel(source, measureSourcePitch(source, tone(3136), SR)), /fmax/);
  const widened = { ...source, fmax: 4000 };
  const pitch = measureSourcePitch(widened, tone(3136), SR);
  assert.doesNotThrow(() => assertPitchMatchesLabel(widened, pitch));
  assert.ok(Math.abs(pitch.centsFromLabel) < 10, `cents ${pitch.centsFromLabel}`); // YIN's whole-sample period steps are ~4 cents wide at 3 kHz
});

test('an octave mistake in the declared note is refused', () => {
  const source = { note: 'A3', outName: 'x' };
  assert.throws(() => assertPitchMatchesLabel(source, measureSourcePitch(source, tone(440), SR)), /1200 cents/);
});

test('a pitched source with no reliable pitch is refused, an unpitched one passes', () => {
  const pitched = { note: 'A4', outName: 'x' };
  assert.throws(() => assertPitchMatchesLabel(pitched, { hz: null, measuredMidi: null, centsFromLabel: null }), /no reliable pitch/);
  const drum = { note: 'A2', unpitched: true, outName: 'y' };
  assert.doesNotThrow(() => assertPitchMatchesLabel(drum, measureSourcePitch(drum, noise(), SR)));
});
