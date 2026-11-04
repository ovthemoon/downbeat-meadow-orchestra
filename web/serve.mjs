#!/usr/bin/env node
// Local static server for the Day 0 tests (Node standard library only).
//  - serves pre-compressed Unity Web files (.br / .gz) with Content-Encoding and the inner file's Content-Type
//  - sends no COOP/COEP headers, like the OpenDesk worker (plan section 2.1)
//  - logs every request (including Accept-Encoding) as JSON lines
//  - POST /save?name=<relative path> stores a body under --out (requires the x-day0 header)
// Usage: node web/serve.mjs --port 8080 --mount /=web/public --mount /builds=unity/Downbeat/Builds --out measurements/_run
import { createServer } from 'node:http';
import { createReadStream, existsSync, mkdirSync, statSync, writeFileSync, appendFileSync } from 'node:fs';
import { dirname, extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

const MAX_SAVE_BYTES = 256 * 1024 * 1024;
const HOST = '127.0.0.1';

const CONTENT_TYPES = Object.freeze({
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.wasm': 'application/wasm',
  '.wav': 'audio/wav',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.ico': 'image/x-icon',
  '.task': 'application/octet-stream',
  '.data': 'application/octet-stream',
  '.map': 'application/json',
});
const ENCODINGS = Object.freeze({ '.br': 'br', '.gz': 'gzip' });

/** Resolve a request path inside one of the mounts. Returns an absolute path or null (unknown / traversal). */
export function resolveMounted(urlPath, mounts) {
  const decoded = safeDecode(urlPath);
  if (decoded === null || decoded.includes('\0')) return null;
  const sorted = [...mounts].sort((a, b) => b.prefix.length - a.prefix.length);
  for (const { prefix, dir } of sorted) {
    const matches = prefix === '/' || decoded === prefix || decoded.startsWith(prefix + '/');
    if (!matches) continue;
    const rest = prefix === '/' ? decoded : decoded.slice(prefix.length);
    const candidate = resolve(join(dir, normalize('/' + rest)));
    if (candidate === dir || candidate.startsWith(dir + sep)) return candidate;
    return null;
  }
  return null;
}

/** Content headers for a file name; .br/.gz use the inner extension for the type. */
export function headersFor(filePath) {
  const outer = extname(filePath).toLowerCase();
  const encoding = ENCODINGS[outer];
  const inner = encoding ? extname(filePath.slice(0, -outer.length)).toLowerCase() : outer;
  const headers = { 'Content-Type': CONTENT_TYPES[inner] ?? 'application/octet-stream' };
  return encoding ? { ...headers, 'Content-Encoding': encoding } : headers;
}

/** Sanitise a save name: relative, [A-Za-z0-9._-/] only, no traversal. Returns null if invalid. */
export function sanitizeSaveName(name) {
  if (typeof name !== 'string' || name.length === 0 || name.length > 200) return null;
  if (!/^[A-Za-z0-9._\-/]+$/.test(name)) return null;
  if (name.startsWith('/') || name.split('/').some((seg) => seg === '..' || seg === '')) return null;
  return name;
}

function safeDecode(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return null;
  }
}

function parseMounts(values) {
  return values.map((spec) => {
    const eq = spec.indexOf('=');
    if (eq < 1) throw new Error(`bad --mount "${spec}", expected /prefix=dir`);
    return { prefix: spec.slice(0, eq).replace(/(.)\/$/, '$1'), dir: resolve(spec.slice(eq + 1)) };
  });
}

function startServer({ port, mounts, outDir, cache }) {
  mkdirSync(outDir, { recursive: true });
  const requestLog = join(outDir, 'requests.jsonl');

  const server = createServer((req, res) => {
    const url = new URL(req.url ?? '/', `http://${HOST}`);
    appendFileSync(requestLog, JSON.stringify({
      t: Date.now(), method: req.method, path: url.pathname,
      acceptEncoding: req.headers['accept-encoding'] ?? null,
    }) + '\n');

    if (req.method === 'POST' && url.pathname === '/save') return handleSave(req, res, url, outDir);
    if (req.method !== 'GET' && req.method !== 'HEAD') return send(res, 405, 'method not allowed');
    if (url.pathname === '/ping') return send(res, 200, 'pong');

    const target = resolveMounted(url.pathname === '/' ? '/index.html' : url.pathname, mounts);
    if (!target || !existsSync(target) || !statSync(target).isFile()) return send(res, 404, 'not found');

    const headers = {
      ...headersFor(target),
      'Content-Length': statSync(target).size,
      'Cache-Control': cache ? 'max-age=3600' : 'no-store',
    };
    res.writeHead(200, headers);
    if (req.method === 'HEAD') return res.end();
    createReadStream(target).pipe(res);
  });

  server.listen(port, HOST, () => {
    console.log(`day0 server http://${HOST}:${port}  out=${outDir}`);
    for (const m of mounts) console.log(`  ${m.prefix} -> ${m.dir}`);
  });
  return server;
}

function handleSave(req, res, url, outDir) {
  if (req.headers['x-day0'] !== '1') return send(res, 403, 'missing x-day0 header');
  const name = sanitizeSaveName(url.searchParams.get('name'));
  if (!name) return send(res, 400, 'bad name');
  const chunks = [];
  let size = 0;
  req.on('data', (chunk) => {
    size += chunk.length;
    if (size > MAX_SAVE_BYTES) {
      send(res, 413, 'too large');
      req.destroy();
      return;
    }
    chunks.push(chunk);
  });
  req.on('end', () => {
    if (res.writableEnded) return;
    const dest = join(outDir, name);
    mkdirSync(dirname(dest), { recursive: true });
    writeFileSync(dest, Buffer.concat(chunks));
    send(res, 200, JSON.stringify({ saved: name, bytes: size }), 'application/json');
  });
  req.on('error', () => send(res, 400, 'request error'));
}

function send(res, status, body, type = 'text/plain; charset=utf-8') {
  if (res.headersSent) return;
  res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store' });
  res.end(body);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const { values } = parseArgs({
    options: {
      port: { type: 'string', default: '8080' },
      mount: { type: 'string', multiple: true, default: [] },
      out: { type: 'string', default: 'measurements/_run' },
      cache: { type: 'boolean', default: false },
    },
  });
  const port = Number(values.port);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('bad --port');
  const mounts = parseMounts(values.mount.length ? values.mount : ['/=web/public']);
  startServer({ port, mounts, outDir: resolve(values.out), cache: values.cache });
}
