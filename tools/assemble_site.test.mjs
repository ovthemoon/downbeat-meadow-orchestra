import { test } from 'node:test';
import assert from 'node:assert/strict';
import { checkLimits } from './assemble_site.mjs';

const MIB = 1024 * 1024;

test('a small site passes', () => {
  assert.deepEqual(checkLimits([{ path: 'index.html', bytes: 5000 }, { path: 'Build/a.wasm.br', bytes: 3 * MIB }]).problems, []);
});

test('a file over 5 MiB, a total over 15 MiB, too many files, deep paths and unknown extensions are reported', () => {
  assert.ok(checkLimits([{ path: 'Build/a.wasm.br', bytes: 5 * MIB + 1 }]).problems.some((p) => p.includes('5 MiB')));
  const big = Array.from({ length: 4 }, (_, i) => ({ path: `f${i}.data`, bytes: 4 * MIB }));
  assert.ok(checkLimits(big).problems.some((p) => p.includes('total')));
  const many = Array.from({ length: 81 }, (_, i) => ({ path: `f${i}.js`, bytes: 1 }));
  assert.ok(checkLimits(many).problems.some((p) => p.includes('81 files')));
  assert.ok(checkLimits([{ path: 'a/b/c/d/e.png', bytes: 1 }]).problems.some((p) => p.includes('deeper')));
  assert.ok(checkLimits([{ path: 'x.mjs', bytes: 1 }]).problems.some((p) => p.includes('.mjs')));
});
