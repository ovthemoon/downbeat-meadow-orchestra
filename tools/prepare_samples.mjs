#!/usr/bin/env node
// S-6: download the chosen VCSL (CC0) samples at a pinned commit, measure leading silence and pitch,
// trim / fade / normalize, and write content/audio/*.wav plus manifest.json and LICENSES.md.
// Usage: node tools/prepare_samples.mjs [--config tools/samples.config.json] [--cache tools/.cache/vcsl] [--out content/audio] [--probe]
//   --probe: only download and report measurements (no output files); used to check pitch labels before choosing samples.
// Per source in the config: `note` is the sounding note (checked against the measured pitch: more than 50 cents off stops the run);
//   `unpitched: true` skips the pitch measurement (drums: `note` is a nominal A, tuneCents 0); `fmin` / `fmax` (Hz) change the 80..2500 Hz search range of the pitch measurement.
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { extractSection, NATURE_BEGIN, NATURE_END } from './lib/marked-section.mjs';
import { midiToNote, noteToMidi } from './lib/pitch.mjs';
import { leadingSilence, trimFadeNormalize } from './lib/samples.mjs';
import { assertPitchMatchesLabel, measureSourcePitch } from './lib/source-pitch.mjs';
import { encodeWavPcm16Mono, parseWav, toMono } from './lib/wav.mjs';

const { values } = parseArgs({
  options: {
    config: { type: 'string', default: 'tools/samples.config.json' },
    cache: { type: 'string', default: 'tools/.cache/vcsl' },
    out: { type: 'string', default: 'content/audio' },
    probe: { type: 'boolean', default: false },
  },
});
const repoRoot = resolve(import.meta.dirname, '..');
const config = JSON.parse(readFileSync(resolve(repoRoot, values.config), 'utf8'));
const cacheDir = resolve(repoRoot, values.cache);
const outDir = resolve(repoRoot, values.out);

const PITCH_WINDOW_START_SEC = 0.15;
const PITCH_WINDOW_LENGTH_SEC = 0.3;

const sha256 = (buf) => createHash('sha256').update(buf).digest('hex');

async function download(source) {
  const file = join(cacheDir, basename(source.vcslPath));
  if (existsSync(file)) return readFileSync(file);
  const url = rawUrl(source.vcslPath);
  const response = await fetch(url);
  if (!response.ok) throw new Error(`download failed ${response.status}: ${url}`);
  const buf = Buffer.from(await response.arrayBuffer());
  mkdirSync(cacheDir, { recursive: true });
  writeFileSync(file, buf);
  return buf;
}

function rawUrl(vcslPath) {
  const encoded = vcslPath.split('/').map(encodeURIComponent).join('/');
  return `https://raw.githubusercontent.com/${config.vcslRepo}/${config.vcslCommit}/${encoded}`;
}

function measure(source, buf) {
  const wav = parseWav(buf);
  const mono = toMono(wav.data);
  const silence = leadingSilence(mono, wav.sampleRate);
  const onset = Math.max(0, silence.relMinus40dbFrame);
  const from = onset + Math.round(PITCH_WINDOW_START_SEC * wav.sampleRate);
  const window = mono.subarray(from, from + Math.round(PITCH_WINDOW_LENGTH_SEC * wav.sampleRate));
  return { wav, mono, silence, onset, ...measureSourcePitch(source, window, wav.sampleRate), labelMidi: noteToMidi(source.note) };
}

function describe(source, m) {
  const head = `${basename(source.vcslPath)} | ${m.wav.sampleRate} Hz ${m.wav.channels}ch ${m.wav.bitsPerSample}bit ${(m.wav.frames / m.wav.sampleRate).toFixed(2)}s`
    + ` | lead(-60dBFS)=${m.silence.absMinus60dbfsMs?.toFixed(1)}ms lead(-40dB rel)=${m.silence.relMinus40dbMs?.toFixed(1)}ms half=${m.silence.halfPeakMs?.toFixed(1)}ms`;
  if (source.unpitched) return `${head} | unpitched (nominal ${source.note}, tuneCents 0)`;
  if (m.hz === null) return `${head} | f0=none (expected ${source.note})`;
  return `${head} | f0=${m.hz.toFixed(1)}Hz (${midiToNote(m.measuredMidi)}, expected ${source.note}, off ${Math.round(m.measuredMidi - m.labelMidi)} st, ${m.centsFromLabel.toFixed(0)} cents)`;
}

function buildOutput(source, m, buf) {
  const trimmed = trimFadeNormalize(m.mono, m.wav.sampleRate, { onsetFrame: m.onset, ...config.trim });
  const outName = `${source.outName}.wav`;
  const wavBytes = encodeWavPcm16Mono(trimmed.samples, m.wav.sampleRate);
  const check = leadingSilence(trimmed.samples, m.wav.sampleRate);
  return {
    outName, wavBytes,
    manifest: {
      file: outName,
      instrument: source.instrument,
      sourceNote: source.note,
      fileNameNote: source.labelNote,
      sourceVariant: source.variant,
      ...(source.unpitched ? { unpitched: true } : {}),
      measuredHz: m.hz,
      measuredNote: m.hz === null ? null : midiToNote(m.measuredMidi),
      centsFromLabel: m.centsFromLabel,
      sampleRate: m.wav.sampleRate,
      durationMs: (trimmed.samples.length / m.wav.sampleRate) * 1000,
      original: {
        durationMs: (m.wav.frames / m.wav.sampleRate) * 1000,
        channels: m.wav.channels,
        bitsPerSample: m.wav.bitsPerSample,
        leadingSilenceMs: {
          absMinus60dbfs: m.silence.absMinus60dbfsMs,
          relMinus40db: m.silence.relMinus40dbMs,
          halfPeak: m.silence.halfPeakMs,
        },
        peakDbfs: m.silence.peakDbfs,
        sha256: sha256(buf),
      },
      trimmed: {
        startFrame: trimmed.startFrame,
        removedLeadMs: (trimmed.startFrame / m.wav.sampleRate) * 1000,
        gain: trimmed.gain,
        leadingSilenceMs: { absMinus60dbfs: check.absMinus60dbfsMs, relMinus40db: check.relMinus40dbMs, halfPeak: check.halfPeakMs },
        // Slow attacks sound later than the file start: schedule the sample this much earlier to land the perceived onset on the beat.
        perceivedOnsetOffsetMs: check.halfPeakMs,
        sha256: sha256(wavBytes),
      },
      source: { url: rawUrl(source.vcslPath), path: source.vcslPath, commit: config.vcslCommit, license: 'CC0-1.0' },
    },
  };
}

function buildNoteMap(entries) {
  const byFile = new Map(entries.map((e) => [e.file, e]));
  return config.scale.notes.flatMap((note) => note.targets.map((t) => {
    const source = byFile.get(`${t.source}.wav`);
    if (!source) throw new Error(`note map refers to missing source ${t.source}`);
    const semitones = noteToMidi(t.target) - noteToMidi(source.sourceNote);
    if (semitones !== t.semitones) throw new Error(`${t.target}: config says ${t.semitones} st but ${source.sourceNote} -> ${t.target} is ${semitones}`);
    return { instrument: source.instrument, target: t.target, file: source.file, semitones, playbackRate: 2 ** (semitones / 12) };
  }));
}

function licensesMarkdown(entries) {
  const rows = entries.map((e) => `| ${e.file} | ${e.source.url} | CC0-1.0 | ${e.original.sha256.slice(0, 12)}... | trimmed to ${(e.durationMs / 1000).toFixed(2)} s, -${e.trimmed.removedLeadMs.toFixed(1)} ms lead, fade-out, peak normalized, 16-bit mono |`);
  return [
    '# Audio sample sources',
    '',
    `All files come from VCSL (Versilian Community Sample Library), https://github.com/${config.vcslRepo}, commit ${config.vcslCommit}.`,
    'VCSL is released under CC0 1.0 Universal (public domain dedication): no attribution is required; it is given here anyway.',
    'Edits: leading silence trimmed, fade-out, peak normalization, 16-bit mono WAV. Original SHA-256 values are in manifest.json.',
    '',
    '| File | Source URL | License | Original SHA-256 | Edit |',
    '|---|---|---|---|---|',
    ...rows,
    '',
    'This is a record of provenance, not legal advice.',
    '',
  ].join('\n');
}

/** The part of LICENSES.md that tools/prepare_nature_samples.mjs wrote is kept when this tool rewrites the file. */
function withNatureSection(text, path) {
  const nature = existsSync(path) ? extractSection(readFileSync(path, 'utf8'), NATURE_BEGIN, NATURE_END) : null;
  return nature === null ? text : `${text.trimEnd()}\n\n${nature}\n`;
}

async function main() {
  const entries = [];
  for (const source of config.sources) {
    const buf = await download(source);
    const m = measure(source, buf);
    console.log(describe(source, m));
    if (values.probe) continue;
    assertPitchMatchesLabel(source, m);
    entries.push(buildOutput(source, m, buf));
  }
  if (values.probe) return;

  mkdirSync(outDir, { recursive: true });
  for (const e of entries) writeFileSync(join(outDir, e.outName), e.wavBytes);
  const manifestEntries = entries.map((e) => e.manifest);
  const manifest = {
    generatedBy: 'tools/prepare_samples.mjs',
    scale: config.scale.name,
    trim: config.trim,
    notes: buildNoteMap(manifestEntries),
    files: manifestEntries,
  };
  writeFileSync(join(outDir, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  writeFileSync(join(outDir, 'LICENSES.md'), withNatureSection(licensesMarkdown(manifestEntries), join(outDir, 'LICENSES.md')));
  console.log(`wrote ${entries.length} wav files, manifest.json, LICENSES.md to ${outDir}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
