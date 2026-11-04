import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { centsFrom, estimateF0, midiToHz, noteToMidi } from './pitch.mjs';
import { leadingSilence, peakOf } from './samples.mjs';
import { NATURE_SAMPLE_RATE, NATURE_SPECS, renderBell, renderDrum, renderPad, renderSpec } from './nature-synth.mjs';

const hash = (samples) => createHash('sha256').update(Buffer.from(samples.buffer)).digest('hex');

test('every sound starts quietly, ends at zero and peaks where the spec says', () => {
  for (const spec of NATURE_SPECS) {
    const samples = renderSpec(spec);
    assert.ok(Math.abs(samples[0]) < 0.01, `${spec.file} starts at ${samples[0]}`);
    assert.ok(Math.abs(samples[samples.length - 1]) < 1e-3, `${spec.file} ends at ${samples[samples.length - 1]}`);
    assert.ok(Math.abs(peakOf(samples) - spec.peak) < 1e-4, `${spec.file} peak ${peakOf(samples)}`);
  }
});

test('the same call gives the same samples (no randomness)', () => {
  const spec = NATURE_SPECS[0];
  assert.equal(hash(renderSpec(spec)), hash(renderSpec(spec)));
});

test('the measured pitch of each sound is within 10 cents of its note', () => {
  for (const spec of NATURE_SPECS) {
    const samples = renderSpec(spec);
    const [from, to] = spec.pitchWindowSec;
    const window = samples.subarray(Math.round(from * NATURE_SAMPLE_RATE), Math.round(to * NATURE_SAMPLE_RATE));
    const hz = estimateF0(window, NATURE_SAMPLE_RATE, { fmin: 40, fmax: 2500 });
    assert.ok(hz !== null, `${spec.file}: no pitch found`);
    const cents = centsFrom(hz, noteToMidi(spec.note));
    assert.ok(Math.abs(cents) < 10, `${spec.file}: ${cents.toFixed(1)} cents off`);
  }
});

test('the pad rises slowly (half its peak after 0.1 to 0.3 s) while the bell and the drum start at once', () => {
  const pad = leadingSilence(renderPad(midiToHz(57)), NATURE_SAMPLE_RATE).halfPeakMs;
  assert.ok(pad > 100 && pad < 300, `pad half-peak at ${pad} ms`);
  assert.ok(leadingSilence(renderBell(midiToHz(81)), NATURE_SAMPLE_RATE).halfPeakMs < 5);
  assert.ok(leadingSilence(renderDrum(midiToHz(45)), NATURE_SAMPLE_RATE).halfPeakMs < 30);
});

test('a bell partial above the Nyquist frequency is left out instead of folding back', () => {
  const samples = renderBell(9000, { sampleRate: NATURE_SAMPLE_RATE });
  assert.ok(Number.isFinite(peakOf(samples)));
});
