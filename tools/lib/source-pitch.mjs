// Pitch of one source sample for tools/prepare_samples.mjs: measured with YIN against the note the config says it sounds at,
// skipped for unpitched sounds (drums), and refused when it does not agree with that note.
import { centsFrom, estimateF0, noteToMidi } from './pitch.mjs';

export const DEFAULT_FMIN_HZ = 80;
export const DEFAULT_FMAX_HZ = 2500;
/** A measured pitch further than this from the declared note is a wrong note (usually an octave) or a wrong search range, not a tuning error. */
export const MAX_TUNE_CENTS = 50;
const MIN_WINDOW_SEC = 0.1;
const A4_MIDI = 69;
const A4_HZ = 440;

/** The YIN search range for a source: 80..2500 Hz unless the source asks for another (`fmin` / `fmax` in samples.config.json). */
export const pitchRangeFor = (source) => ({ fmin: source.fmin ?? DEFAULT_FMIN_HZ, fmax: source.fmax ?? DEFAULT_FMAX_HZ });

/**
 * @param {{note:string, unpitched?:boolean, fmin?:number, fmax?:number}} source `note` is the sounding note (nominal for an unpitched source)
 * @returns {{hz:number|null, measuredMidi:number|null, centsFromLabel:number|null}}
 *   An unpitched source is not measured: hz and measuredMidi are null and centsFromLabel is 0 (no tuning correction).
 *   A pitched source with no reliable pitch has hz null and centsFromLabel null.
 */
export function measureSourcePitch(source, window, sampleRate) {
  if (source.unpitched) return { hz: null, measuredMidi: null, centsFromLabel: 0 };
  const hz = window.length > sampleRate * MIN_WINDOW_SEC ? estimateF0(window, sampleRate, pitchRangeFor(source)) : null;
  if (hz === null) return { hz: null, measuredMidi: null, centsFromLabel: null };
  return {
    hz,
    measuredMidi: Math.round(12 * Math.log2(hz / A4_HZ) + A4_MIDI),
    centsFromLabel: centsFrom(hz, noteToMidi(source.note)),
  };
}

/** Throws unless a pitched source measured within MAX_TUNE_CENTS of its declared note; an unpitched source always passes. */
export function assertPitchMatchesLabel(source, pitch) {
  if (source.unpitched) return;
  const name = source.outName ?? source.note;
  const { fmin, fmax } = pitchRangeFor(source);
  if (pitch.hz === null) throw new Error(`${name}: no reliable pitch found between ${fmin} and ${fmax} Hz; set "unpitched": true, or widen "fmin" / "fmax" for this source`);
  if (Math.abs(pitch.centsFromLabel) > MAX_TUNE_CENTS) {
    throw new Error(`${name}: measured ${pitch.hz.toFixed(1)} Hz is ${Math.round(pitch.centsFromLabel)} cents from ${source.note} (limit ${MAX_TUNE_CENTS}); check the note, or the search range ${fmin}..${fmax} Hz ("fmin" / "fmax" in the source)`);
  }
}
