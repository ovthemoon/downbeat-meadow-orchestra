#!/usr/bin/env node
// Assembles the deployable static site (dist/site) from the Unity Web build, the content folder and the page's measurement tap,
// then checks it against the OpenDesk limits: 80 files, 15 MiB total, 5 MiB per file, path depth 4, allowed extensions.
// Usage: node tools/assemble_site.mjs [--build unity/Downbeat/Builds/downbeat] [--out dist/site] [--report measurements/day1/site-report.json]
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, extname, join, relative, resolve, sep } from 'node:path';
import { parseArgs } from 'node:util';

const MIB = 1024 * 1024;
export const LIMITS = Object.freeze({ files: 80, totalBytes: 15 * MIB, fileBytes: 5 * MIB, depth: 4 });
export const ALLOWED_EXTENSIONS = new Set(['html', 'css', 'js', 'json', 'xml', 'txt', 'webmanifest', 'png', 'jpg', 'jpeg', 'gif', 'webp', 'svg', 'ico',
  'woff', 'woff2', 'ttf', 'otf', 'mp3', 'ogg', 'wav', 'm4a', 'mp4', 'webm', 'wasm', 'data', 'br', 'gz', 'glb', 'gltf', 'atlas', 'fnt']);
const CONTENT_EXCLUDE = new Set(['manifest.json', 'sprites.json', 'LICENSES.md']);

function listFiles(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? listFiles(join(dir, e.name)) : [join(dir, e.name)]));
}

/** Pure check of a list of {path (relative, '/'), bytes} against the limits. */
export function checkLimits(files) {
  const problems = [];
  const total = files.reduce((s, f) => s + f.bytes, 0);
  if (files.length > LIMITS.files) problems.push(`${files.length} files > ${LIMITS.files}`);
  if (total > LIMITS.totalBytes) problems.push(`total ${total} B > ${LIMITS.totalBytes} B`);
  for (const f of files) {
    if (f.bytes > LIMITS.fileBytes) problems.push(`${f.path}: ${f.bytes} B > 5 MiB`);
    if (f.path.split('/').length > LIMITS.depth) problems.push(`${f.path}: path deeper than ${LIMITS.depth}`);
    const ext = extname(f.path).slice(1).toLowerCase();
    if (!ALLOWED_EXTENSIONS.has(ext)) problems.push(`${f.path}: extension ".${ext}" not allowed`);
  }
  return { problems, total };
}

function main() {
  const { values } = parseArgs({ options: {
    build: { type: 'string', default: 'unity/Downbeat/Builds/downbeat' },
    out: { type: 'string', default: 'dist/site' },
    report: { type: 'string', default: 'measurements/day1/site-report.json' },
  } });
  const root = resolve(import.meta.dirname, '..');
  const build = resolve(root, values.build);
  const out = resolve(root, values.out);
  if (!existsSync(build)) throw new Error(`Unity build not found: ${build}`);

  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  cpSync(build, out, { recursive: true });
  cpSync(join(root, 'content'), join(out, 'content'), { recursive: true, filter: (src) => !CONTENT_EXCLUDE.has(src.split(sep).pop()) });
  mkdirSync(join(out, 'js'), { recursive: true });
  for (const f of ['tap.js', 'tap-worklet.js']) cpSync(join(root, 'web', 'public', 'js', f), join(out, 'js', f));

  const files = listFiles(out).map((f) => ({ path: relative(out, f).split(sep).join('/'), bytes: statSync(f).size })).sort((a, b) => b.bytes - a.bytes);
  const { problems, total } = checkLimits(files);
  const groups = {};
  for (const f of files) {
    const key = f.path.startsWith('Build/') ? 'Build (Unity)' : f.path.startsWith('content/img/') ? 'content/img' : f.path.startsWith('content/audio/') ? 'content/audio' : 'other';
    groups[key] = (groups[key] ?? 0) + f.bytes;
  }
  const report = { limits: LIMITS, fileCount: files.length, totalBytes: total, totalMiB: total / MIB, groups, problems, files };
  mkdirSync(dirname(resolve(root, values.report)), { recursive: true });
  writeFileSync(resolve(root, values.report), JSON.stringify(report, null, 2));
  console.log(`site: ${files.length} files, ${total} B (${(total / MIB).toFixed(2)} MiB)  ${problems.length ? 'PROBLEMS' : 'within OpenDesk limits'}`);
  for (const [k, v] of Object.entries(groups)) console.log(`  ${k.padEnd(14)} ${String(v).padStart(9)} B  ${(v / MIB).toFixed(2)} MiB`);
  for (const f of files.slice(0, 6)) console.log(`  ${f.path.padEnd(46)} ${f.bytes} B (${(f.bytes / MIB).toFixed(3)} MiB)`);
  for (const p of problems) console.log('  PROBLEM:', p);
  if (problems.length) process.exitCode = 1;
}

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].split('/').pop())) main();
