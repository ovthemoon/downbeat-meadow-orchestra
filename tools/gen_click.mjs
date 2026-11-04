#!/usr/bin/env node
// Generates the test click used by S-1: a 3 kHz sine burst with a fast exponential decay (48 kHz, mono, 16-bit).
// Deterministic: the same bytes every run. Usage: node tools/gen_click.mjs <out.wav> [<out2.wav> ...]
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { encodeWavPcm16Mono } from './lib/wav.mjs';

const SAMPLE_RATE = 48000;
const FREQUENCY_HZ = 3000;
const DURATION_SEC = 0.03;
const DECAY_SEC = 0.003;
const AMPLITUDE = 0.6;

export function renderClick() {
  const frames = Math.round(SAMPLE_RATE * DURATION_SEC);
  const out = new Float32Array(frames);
  for (let i = 0; i < frames; i++) {
    const t = i / SAMPLE_RATE;
    out[i] = AMPLITUDE * Math.sin(2 * Math.PI * FREQUENCY_HZ * t) * Math.exp(-t / DECAY_SEC);
  }
  return out;
}

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].split('/').pop())) {
  const targets = process.argv.slice(2);
  if (targets.length === 0) throw new Error('usage: gen_click.mjs <out.wav> [...]');
  const wav = encodeWavPcm16Mono(renderClick(), SAMPLE_RATE);
  for (const target of targets) {
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, wav);
    console.log(`wrote ${target} (${wav.length} bytes)`);
  }
}
