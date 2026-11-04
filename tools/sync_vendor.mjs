#!/usr/bin/env node
// Recreates web/vendor (git-ignored, ~30 MB): MediaPipe tasks-vision files from node_modules (pinned in package.json)
// and the official hand landmarker model, then verifies every file against tools/vendor.lock.json.
// Usage: npm ci && node tools/sync_vendor.mjs
import { createHash } from 'node:crypto';
import { cpSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

const repoRoot = resolve(import.meta.dirname, '..');
const lock = JSON.parse(readFileSync(join(import.meta.dirname, 'vendor.lock.json'), 'utf8'));
const vendorRoot = join(repoRoot, 'web', 'vendor');
const packageRoot = join(repoRoot, 'node_modules', '@mediapipe', 'tasks-vision');

const sha256 = (buf) => createHash('sha256').update(buf).digest('hex');

async function ensureModel() {
  const target = join(vendorRoot, lock.model.target);
  if (existsSync(target)) return;
  const response = await fetch(lock.model.url);
  if (!response.ok) throw new Error(`model download failed: ${response.status}`);
  mkdirSync(dirname(target), { recursive: true });
  writeFileSync(target, Buffer.from(await response.arrayBuffer()));
}

function copyPackageFiles() {
  if (!existsSync(packageRoot)) throw new Error('run `npm ci` first (node_modules/@mediapipe/tasks-vision missing)');
  mkdirSync(join(vendorRoot, 'mediapipe'), { recursive: true });
  cpSync(join(packageRoot, 'vision_bundle.mjs'), join(vendorRoot, 'mediapipe', 'vision_bundle.mjs'));
  cpSync(join(packageRoot, 'wasm'), join(vendorRoot, 'mediapipe', 'wasm'), { recursive: true });
}

function verify() {
  const problems = [];
  for (const [name, expected] of Object.entries(lock.files)) {
    const file = join(vendorRoot, name);
    if (!existsSync(file)) { problems.push(`missing ${name}`); continue; }
    const actual = sha256(readFileSync(file));
    if (actual !== expected.sha256) problems.push(`hash mismatch ${name}`);
  }
  return problems;
}

await ensureModel();
copyPackageFiles();
const problems = verify();
if (problems.length > 0) {
  console.error(problems.join('\n'));
  process.exitCode = 1;
} else {
  console.log(`vendor ok: ${Object.keys(lock.files).length} files verified`);
}
