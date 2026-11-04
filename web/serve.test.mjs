import { test } from 'node:test';
import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { headersFor, resolveMounted, sanitizeSaveName } from './serve.mjs';

const mounts = [
  { prefix: '/', dir: resolve('/srv/public') },
  { prefix: '/builds', dir: resolve('/srv/builds') },
];

test('serves .br files with Content-Encoding and the inner content type', () => {
  assert.deepEqual(headersFor('/x/app.wasm.br'), { 'Content-Type': 'application/wasm', 'Content-Encoding': 'br' });
  assert.deepEqual(headersFor('/x/app.framework.js.br'), { 'Content-Type': 'text/javascript; charset=utf-8', 'Content-Encoding': 'br' });
  assert.deepEqual(headersFor('/x/app.data.gz'), { 'Content-Type': 'application/octet-stream', 'Content-Encoding': 'gzip' });
  assert.equal(headersFor('/x/a.wav')['Content-Encoding'], undefined);
});

test('maps the longest matching mount prefix', () => {
  assert.equal(resolveMounted('/builds/a/b.js', mounts), resolve('/srv/builds/a/b.js'));
  assert.equal(resolveMounted('/s1.html', mounts), resolve('/srv/public/s1.html'));
  assert.equal(resolveMounted('/buildsX/a.js', mounts), resolve('/srv/public/buildsX/a.js'));
});

test('keeps traversal attempts inside the mount and rejects bad encodings', () => {
  const insideAMount = (p) => p === null || mounts.some((m) => p.startsWith(m.dir + '/'));
  for (const attempt of ['/builds/../../etc/passwd', '/%2e%2e/%2e%2e/etc/passwd', '/builds/%2e%2e/%2e%2e/x']) {
    const resolved = resolveMounted(attempt, mounts);
    assert.ok(insideAMount(resolved), `${attempt} -> ${resolved}`);
    assert.ok(resolved === null || !resolved.includes('..'));
  }
  assert.equal(resolveMounted('/a%00b', mounts), null);
  assert.equal(resolveMounted('/%E0%A4%A', mounts), null);
});

test('sanitizeSaveName accepts safe relative names only', () => {
  assert.equal(sanitizeSaveName('s1/run-01.json'), 's1/run-01.json');
  for (const bad of ['', '../x', '/abs', 'a/../b', 'a//b', 'a b', 'a;b', 'x'.repeat(201), null]) {
    assert.equal(sanitizeSaveName(bad), null, String(bad));
  }
});
