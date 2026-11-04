// The bell, low string and hand drum samples in content/audio (docs/conduct-six-cells-samples.md): every pitch step of the song must
// resolve within the suggested maxPitchShift, the same way unity/.../SamplePicker.cs and ContentValidator.cs check it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { noteToMidi } from './pitch.mjs';
import { MAX_TUNE_CENTS } from './source-pitch.mjs';

const audioDir = resolve(import.meta.dirname, '..', '..', 'content', 'audio');
const manifest = JSON.parse(readFileSync(resolve(audioDir, 'manifest.json'), 'utf8'));

const SONG_SCALE_STEPS = [0, 3, 5, 7, 10]; // A minor pentatonic, as content/content.json song.scaleSteps
const SONG_PITCH_STEPS = 10;
const SEMITONES_PER_OCTAVE = 12;
const PITCH_CLASS_A = 9;

const SUGGESTED = {
  bell: { filePrefix: 'bell_', rootMidi: 69, maxPitchShift: 2 },
  lowstring: { filePrefix: 'lowstring_', rootMidi: 57, maxPitchShift: 2 },
  handdrum: { filePrefix: 'handdrum_', rootMidi: 45, maxPitchShift: 5 },
};

const filesOf = (instrument) => manifest.files.filter((f) => f.instrument === instrument);
const midiForStep = (rootMidi, step) => rootMidi + SONG_SCALE_STEPS[step % SONG_SCALE_STEPS.length] + SEMITONES_PER_OCTAVE * Math.floor(step / SONG_SCALE_STEPS.length);

/** The distance in semitones to the sample nearest in sounding pitch (SamplePicker.TryPick: the higher sample wins a tie, the distance is the same). */
function nearestShift(samples, target) {
  return Math.min(...samples.map((s) => Math.abs(target - s)));
}

for (const [instrument, { filePrefix, rootMidi, maxPitchShift }] of Object.entries(SUGGESTED)) {
  test(`${instrument}: every pitch step resolves within a shift of ${maxPitchShift} semitones`, () => {
    const sounding = filesOf(instrument).map((f) => noteToMidi(f.sourceNote));
    assert.ok(sounding.length > 0, `no ${instrument} files in manifest.json`);
    assert.equal(((rootMidi - 69) % SEMITONES_PER_OCTAVE + SEMITONES_PER_OCTAVE) % SEMITONES_PER_OCTAVE, 0, 'root must be the song root pitch class (A)');
    for (let step = 0; step < SONG_PITCH_STEPS; step++) {
      const shift = nearestShift(sounding, midiForStep(rootMidi, step));
      assert.ok(shift <= maxPitchShift, `${instrument} step ${step}: nearest sample is ${shift} semitones away`);
    }
  });

  test(`${instrument}: files exist, carry the ${filePrefix} prefix and a usable tuneCents`, () => {
    for (const f of filesOf(instrument)) {
      assert.ok(f.file.startsWith(filePrefix), f.file);
      assert.ok(existsSync(resolve(audioDir, f.file)), `${f.file} is missing`);
      assert.equal(typeof f.centsFromLabel, 'number', `${f.file}: tuneCents must be a number (content-build rounds it)`);
      assert.ok(Math.abs(f.centsFromLabel) <= MAX_TUNE_CENTS, `${f.file}: ${f.centsFromLabel} cents`);
    }
  });
}

test('the hand drum is unpitched: nominal A labels, no measurement, no tuning correction', () => {
  for (const f of filesOf('handdrum')) {
    assert.equal(f.unpitched, true, f.file);
    assert.equal(noteToMidi(f.sourceNote) % SEMITONES_PER_OCTAVE, PITCH_CLASS_A, f.file);
    assert.equal(f.measuredHz, null, f.file);
    assert.equal(f.centsFromLabel, 0, f.file);
  }
  for (const instrument of ['bell', 'lowstring']) {
    for (const f of filesOf(instrument)) assert.notEqual(f.unpitched, true, f.file);
  }
});

test('the new file prefixes do not collide with the other instruments or the synthetic nature sounds', () => {
  const prefixes = [...Object.values(SUGGESTED).map((s) => s.filePrefix), 'harp_', 'recorder_', 'marimba_', 'firefly_', 'pad_', 'drum_'];
  for (const a of prefixes) {
    for (const b of prefixes) assert.ok(a === b || !a.startsWith(b), `${a} starts with ${b}`);
  }
});
