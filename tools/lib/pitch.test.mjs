import { test } from 'node:test';
import assert from 'node:assert/strict';
import { centsFrom, estimateF0, midiToHz, midiToNote, noteToMidi } from './pitch.mjs';

const SR = 44100;

function tone(hz, seconds, harmonics = [1]) {
  const out = new Float32Array(Math.round(SR * seconds));
  harmonics.forEach((gain, h) => {
    for (let i = 0; i < out.length; i++) out[i] += gain * Math.sin((2 * Math.PI * hz * (h + 1) * i) / SR);
  });
  return out;
}

test('note names and MIDI numbers agree both ways', () => {
  assert.equal(noteToMidi('A4'), 69);
  assert.equal(noteToMidi('C4'), 60);
  assert.equal(noteToMidi('A#3'), 58);
  assert.equal(noteToMidi('Bb3'), 58);
  assert.equal(midiToNote(60), 'C4');
  assert.equal(midiToNote(70), 'A#4');
  assert.throws(() => noteToMidi('H4'), /bad note/);
});

test('midiToHz gives 440 Hz for A4 and doubles per octave', () => {
  assert.equal(midiToHz(69), 440);
  assert.ok(Math.abs(midiToHz(81) - 880) < 1e-9);
});

test('estimateF0 finds a sine within a cent', () => {
  const f0 = estimateF0(tone(261.63, 0.3), SR);
  assert.ok(Math.abs(centsFrom(f0, 60)) < 1, `cents ${centsFrom(f0, 60)}`);
});

test('estimateF0 resists octave errors on a harmonic-rich tone with weak fundamental', () => {
  const f0 = estimateF0(tone(220, 0.3, [0.4, 1, 0.8, 0.6]), SR);
  assert.ok(Math.abs(f0 - 220) < 1, `f0 ${f0}`);
});

test('estimateF0 returns null for noise and throws for short windows', () => {
  let seed = 1;
  const noise = Float32Array.from({ length: 12000 }, () => ((seed = (seed * 16807) % 2147483647) / 2147483647) * 2 - 1);
  assert.equal(estimateF0(noise, SR), null);
  assert.throws(() => estimateF0(new Float32Array(100), SR), /too short/);
});
