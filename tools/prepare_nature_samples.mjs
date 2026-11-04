#!/usr/bin/env node
// Makes the sounds of the nature layers (glass bells for the fireflies, a soft pad for the sky, a low drum for the hills) by synthesis, so no sample
// has to be downloaded or licensed: content/audio/{firefly,pad,drum}_*.wav (16-bit mono), content/audio/nature-manifest.json and a section of
// content/audio/LICENSES.md. The same run always gives the same bytes (tools/lib/nature-synth.mjs has no randomness).
// Usage: node tools/prepare_nature_samples.mjs [--out content/audio]
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { replaceSection, NATURE_BEGIN, NATURE_END } from './lib/marked-section.mjs';
import { NATURE_SAMPLE_RATE, NATURE_SPECS, renderSpec } from './lib/nature-synth.mjs';
import { centsFrom, estimateF0, noteToMidi } from './lib/pitch.mjs';
import { leadingSilence } from './lib/samples.mjs';
import { encodeWavPcm16Mono } from './lib/wav.mjs';

const { values } = parseArgs({ options: { out: { type: 'string', default: 'content/audio' } } });
const outDir = resolve(import.meta.dirname, '..', values.out);
const MAX_PITCH_ERROR_CENTS = 10;

const sha256 = (buf) => createHash('sha256').update(buf).digest('hex');
const round = (v, digits) => Number(v.toFixed(digits));

function describe(spec) {
  const samples = renderSpec(spec);
  const [from, to] = spec.pitchWindowSec;
  const window = samples.subarray(Math.round(from * NATURE_SAMPLE_RATE), Math.round(to * NATURE_SAMPLE_RATE));
  const hz = estimateF0(window, NATURE_SAMPLE_RATE, { fmin: 40, fmax: 2500 });
  if (hz === null) throw new Error(`${spec.file}: no pitch found`);
  const measuredCents = centsFrom(hz, noteToMidi(spec.note));
  if (Math.abs(measuredCents) > MAX_PITCH_ERROR_CENTS) throw new Error(`${spec.file}: measured ${measuredCents.toFixed(1)} cents off ${spec.note}`);

  const silence = leadingSilence(samples, NATURE_SAMPLE_RATE);
  const wav = encodeWavPcm16Mono(samples, NATURE_SAMPLE_RATE);
  return {
    wav,
    entry: {
      file: `${spec.file}.wav`,
      instrument: spec.instrument,
      sourceNote: spec.note,
      synthesized: spec.kind,
      // The pitch is exact by construction (equal temperament), so the tuning correction is 0; the measurement below is only the check that the synthesis is right.
      centsFromLabel: 0,
      measuredCents: round(measuredCents, 1),
      measuredHz: round(hz, 2),
      sampleRate: NATURE_SAMPLE_RATE,
      durationMs: round((samples.length / NATURE_SAMPLE_RATE) * 1000, 1),
      peakDbfs: round(silence.peakDbfs, 2),
      // time until half of the peak is reached: the sample is scheduled this much earlier so that the heard onset (not the file start) lands on the beat
      trimmed: { perceivedOnsetOffsetMs: round(silence.halfPeakMs, 1), sha256: sha256(wav) },
    },
  };
}

function licensesBody(entries) {
  const rows = entries.map((e) => `| ${e.file} | ${e.synthesized} | ${e.sourceNote} | ${(e.durationMs / 1000).toFixed(2)} s | ${e.peakDbfs.toFixed(1)} dBFS | ${e.trimmed.sha256.slice(0, 12)}... |`);
  return [
    '## Synthesized nature sounds',
    '',
    'These files are not recordings and not taken from any library: tools/prepare_nature_samples.mjs computes them (additive synthesis, tools/lib/nature-synth.mjs, no random numbers, so',
    'the same bytes come out every run). They are the project\'s own work under the MIT license of this repository; there is nothing to attribute.',
    'Their pitch is exact by construction and checked by a pitch measurement (within 10 cents); format 16-bit mono WAV, 32 kHz.',
    '',
    '| File | Kind | Note | Length | Peak | SHA-256 |',
    '|---|---|---|---|---|---|',
    ...rows,
  ].join('\n');
}

function main() {
  mkdirSync(outDir, { recursive: true });
  const entries = [];
  for (const spec of NATURE_SPECS) {
    const { wav, entry } = describe(spec);
    writeFileSync(join(outDir, entry.file), wav);
    entries.push(entry);
  }
  const manifest = {
    generatedBy: 'tools/prepare_nature_samples.mjs',
    sampleRate: NATURE_SAMPLE_RATE,
    note: 'Synthesized, not recorded. centsFromLabel is 0 by construction; measuredCents is the check.',
    files: entries,
  };
  writeFileSync(join(outDir, 'nature-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');

  const licensesPath = join(outDir, 'LICENSES.md');
  const old = existsSync(licensesPath) ? readFileSync(licensesPath, 'utf8') : '# Audio sample sources\n';
  writeFileSync(licensesPath, replaceSection(old, NATURE_BEGIN, NATURE_END, licensesBody(entries)));
  console.log(`wrote ${entries.length} wav files, nature-manifest.json and the nature section of LICENSES.md to ${outDir}`);
}

main();
