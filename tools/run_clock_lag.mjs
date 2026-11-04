#!/usr/bin/env node
// Audio clock vs wall clock under main-thread blocks, without Unity: does AudioContext.currentTime fall behind?
// Usage: node tools/run_clock_lag.mjs --run clock-lag-1
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { launchChrome, sleep } from './lib/cdp.mjs';

const { values } = parseArgs({ options: { run: { type: 'string', default: 'clock-lag' }, sec: { type: 'string', default: '60' }, port: { type: 'string', default: '8769' }, debugPort: { type: 'string', default: '9337' } } });
const repoRoot = resolve(import.meta.dirname, '..');
const outDir = resolve(repoRoot, 'measurements', 'clock-lag', values.run);

const CASES = [
  { name: 'plain-calm', query: 'tap=0&block=0' },
  { name: 'plain-blocks-50ms', query: 'tap=0&block=50&every=2000' },
  { name: 'tap-calm', query: 'tap=1&block=0' },
  { name: 'tap-blocks-50ms', query: 'tap=1&block=50&every=2000' },
];

async function runCase(c) {
  const chrome = await launchChrome({
    port: Number(values.debugPort),
    args: ['--autoplay-policy=no-user-gesture-required', '--window-position=40,40', '--window-size=700,500',
      '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows'],
    url: `http://localhost:${values.port}/clock-lag.html?sec=${values.sec}&${c.query}`,
  });
  try {
    const { cdp } = await chrome.connectToPage('clock-lag.html');
    await cdp.send('Runtime.enable');
    await cdp.send('Page.bringToFront');
    await cdp.waitFor('window.clockLagResult');
    return await cdp.evaluate('window.clockLagResult', { timeoutMs: (Number(values.sec) + 60) * 1000 });
  } finally {
    await chrome.close();
  }
}

async function main() {
  mkdirSync(outDir, { recursive: true });
  const server = spawn('node', ['web/serve.mjs', '--port', values.port, '--mount', '/=web/public', '--out', outDir], { cwd: repoRoot, stdio: 'ignore' });
  await sleep(800);
  const results = [];
  try {
    for (const c of CASES) {
      const r = await runCase(c);
      console.log(`${c.name.padEnd(34)} wall ${r.wallSec.toFixed(2)} s  ctx ${r.ctxSec.toFixed(3)} s  ctx-wall ${r.ctxMinusWallMs.toFixed(1)} ms  blocks ${r.blocks}`);
      results.push({ case: c.name, ...r });
    }
  } finally {
    server.kill();
    writeFileSync(resolve(outDir, 'results.json'), JSON.stringify(results, null, 1));
  }
}

main().catch((e) => { console.error(e); process.exitCode = 1; });
