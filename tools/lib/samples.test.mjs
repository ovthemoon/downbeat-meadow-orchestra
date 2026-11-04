import { test } from 'node:test';
import assert from 'node:assert/strict';
import { leadingSilence, peakOf, trimFadeNormalize } from './samples.mjs';

const SR = 1000;

function burst(silenceFrames, length) {
  const out = new Float32Array(length);
  for (let i = silenceFrames; i < length; i++) out[i] = 0.8 * Math.sin((2 * Math.PI * 50 * (i - silenceFrames)) / SR);
  return out;
}

test('leadingSilence reports where the sound starts', () => {
  const s = leadingSilence(burst(120, 1000), SR);
  assert.ok(s.absMinus60dbfsMs >= 120 && s.absMinus60dbfsMs < 130, `${s.absMinus60dbfsMs}`);
  assert.ok(s.relMinus40dbMs >= s.absMinus60dbfsMs * 0.9);
  assert.ok(s.halfPeakMs > s.relMinus40dbMs);
  assert.ok(Math.abs(s.peak - 0.8) < 0.01);
});

test('leadingSilence rejects digital silence', () => {
  assert.throws(() => leadingSilence(new Float32Array(100), SR), /silence/);
});

test('trimFadeNormalize removes the lead, fades the end and normalizes the peak', () => {
  const source = burst(300, 2000);
  const { samples, gain, startFrame } = trimFadeNormalize(source, SR, { onsetFrame: 300, preRollSec: 0.002, lengthSec: 1.0, fadeOutSec: 0.2, targetPeak: 0.5 });
  assert.equal(startFrame, 298);
  assert.equal(samples.length, 1000);
  assert.ok(Math.abs(peakOf(samples) - 0.5) < 1e-6);
  assert.ok(gain > 0);
  assert.ok(samples[samples.length - 1] === 0); // -0 counts
  assert.ok(leadingSilence(samples, SR).absMinus60dbfsMs <= 3);
});

test('trimFadeNormalize does not modify its input and clamps to the file end', () => {
  const source = burst(10, 500);
  const copy = Float32Array.from(source);
  const { samples } = trimFadeNormalize(source, SR, { onsetFrame: 10, lengthSec: 5 });
  assert.deepEqual(source, copy);
  assert.equal(samples.length, 500 - 8);
});
