#!/usr/bin/env node
// Writes content/content.json from content/audio/manifest.json (S-6 measurements). Hand-edited values in an existing content.json
// (gains, gestures, the meadow block) are kept, new default keys are added, unless --reset is given.
// Usage: node tools/build_content.mjs [--reset]
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { buildContent } from './lib/content-build.mjs';

const root = resolve(import.meta.dirname, '..', 'content');
const reset = process.argv.includes('--reset');
const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'));

const target = resolve(root, 'content.json');
const existing = !reset && existsSync(target) ? readJson(target) : null;
const content = buildContent({
  manifest: readJson(resolve(root, 'audio', 'manifest.json')),
  natureManifest: readJson(resolve(root, 'audio', 'nature-manifest.json')),
  existing,
});
writeFileSync(target, JSON.stringify(content, null, 2) + '\n');
console.log(`wrote ${target} (${content.instruments.reduce((n, i) => n + i.samples.length, 0)} samples + ${content.nature.instruments.reduce((n, i) => n + i.samples.length, 0)} nature samples, ${existing ? 'kept layout values' : 'defaults'})`);
