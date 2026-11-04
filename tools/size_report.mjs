#!/usr/bin/env node
// S-3: sizes of the files of Unity Web builds against the OpenDesk limits (5 MiB per file, 15 MiB total, 80 files).
// Usage: node tools/size_report.mjs <name>=<buildDir> ... [--recompress] [--out measurements/s3/size-report.json]
//   --recompress: also re-encode each .br file with Brotli quality 11 / 24-bit window (slow, about 40 s per large file)
import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { brotliCompressSync, brotliDecompressSync, constants } from 'node:zlib';
import { parseArgs } from 'node:util';

const MIB = 1024 * 1024;
const LIMITS = Object.freeze({ perFileBytes: 5 * MIB, totalBytes: 15 * MIB, fileCount: 80 });

const { values, positionals } = parseArgs({
  options: { recompress: { type: 'boolean', default: false }, out: { type: 'string', default: 'measurements/s3/size-report.json' } },
  allowPositionals: true,
});

function listFiles(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? listFiles(join(dir, e.name)) : [join(dir, e.name)]));
}

function brotliQ11(file) {
  const raw = brotliDecompressSync(readFileSync(file));
  const out = brotliCompressSync(raw, { params: {
    [constants.BROTLI_PARAM_QUALITY]: 11, [constants.BROTLI_PARAM_LGWIN]: 24, [constants.BROTLI_PARAM_SIZE_HINT]: raw.length,
  } });
  return { decompressedBytes: raw.length, q11Bytes: out.length };
}

function reportFor(name, dir) {
  const root = resolve(dir);
  const files = listFiles(root).map((f) => {
    const bytes = statSync(f).size;
    const rel = relative(root, f);
    const entry = { file: rel, bytes, mib: bytes / MIB, overPerFileLimit: bytes > LIMITS.perFileBytes };
    if (values.recompress && f.endsWith('.br')) Object.assign(entry, brotliQ11(f));
    return entry;
  }).sort((a, b) => b.bytes - a.bytes);
  const total = files.reduce((s, f) => s + f.bytes, 0);
  const buildOnly = files.filter((f) => f.file.startsWith('Build/')).reduce((s, f) => s + f.bytes, 0);
  return {
    name, dir: root, fileCount: files.length, totalBytes: total, totalMib: total / MIB, buildFolderBytes: buildOnly,
    overTotalLimit: total > LIMITS.totalBytes, files,
  };
}

const reports = positionals.map((p) => {
  const [name, dir] = p.split('=');
  return reportFor(name, dir);
});
writeFileSync(resolve(values.out), JSON.stringify({ limits: LIMITS, generatedBy: 'tools/size_report.mjs', reports }, null, 2));

for (const r of reports) {
  console.log(`\n${r.name}: ${r.fileCount} files, total ${r.totalBytes} B (${r.totalMib.toFixed(2)} MiB)${r.overTotalLimit ? '  OVER 15 MiB' : ''}`);
  for (const f of r.files.filter((x) => x.bytes > 100000)) {
    const extra = f.q11Bytes ? `  | q11/24: ${f.q11Bytes} B (${(f.q11Bytes / MIB).toFixed(3)} MiB)` : '';
    console.log(`  ${f.file.padEnd(42)} ${String(f.bytes).padStart(9)} B  ${f.mib.toFixed(3)} MiB${f.overPerFileLimit ? '  OVER 5 MiB' : ''}${extra}`);
  }
}
