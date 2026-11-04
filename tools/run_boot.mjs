#!/usr/bin/env node
// S-3 boot check: serve a Unity Web build with Brotli and open it in Chrome; record boot time, delivered sizes,
// whether Chrome asked for br on http://localhost, console errors, and a screenshot.
// Usage: node tools/run_boot.mjs --builds s3-template,s3-lean-size,day0 --run boot-1
import { spawn } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { launchChrome, sleep } from './lib/cdp.mjs';

const { values } = parseArgs({
  options: {
    builds: { type: 'string', default: 'day0' },
    run: { type: 'string', default: 'boot' },
    port: { type: 'string', default: '8768' },
    debugPort: { type: 'string', default: '9336' },
  },
});

const repoRoot = resolve(import.meta.dirname, '..');
const outDir = resolve(repoRoot, 'measurements', 's3', values.run);

async function bootOne(build, server) {
  const url = `http://localhost:${values.port}/boot.html?build=${build}`;
  const chrome = await launchChrome({
    port: Number(values.debugPort),
    args: ['--autoplay-policy=no-user-gesture-required', '--window-position=40,40', '--window-size=900,640',
      '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows'],
    url,
  });
  const consoleMessages = [];
  try {
    const { cdp } = await chrome.connectToPage('boot.html');
    cdp.on('Runtime.consoleAPICalled', (p) => {
      if (['error', 'warning'].includes(p.type)) consoleMessages.push({ type: p.type, text: p.args.map((a) => a.value ?? a.description ?? '').join(' ').slice(0, 300) });
    });
    cdp.on('Runtime.exceptionThrown', (p) => consoleMessages.push({ type: 'exception', text: (p.exceptionDetails.exception?.description ?? p.exceptionDetails.text).slice(0, 300) }));
    await cdp.send('Runtime.enable');
    await cdp.send('Page.enable');
    await cdp.send('Page.bringToFront');
    await cdp.send('Page.reload');
    await sleep(1000);
    await cdp.waitFor('window.bootResult', { timeoutMs: 60000 });
    const result = await cdp.evaluate('window.bootResult', { timeoutMs: 120000 });
    const shot = await cdp.send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(resolve(outDir, `${build}.png`), Buffer.from(shot.data, 'base64'));
    cdp.close();
    return { ...result, consoleMessages };
  } finally {
    await chrome.close();
  }
}

async function main() {
  mkdirSync(outDir, { recursive: true });
  const server = spawn('node', [
    'web/serve.mjs', '--port', values.port, '--mount', '/=web/public', '--mount', '/builds=unity/Downbeat/Builds', '--mount', '/vendor=web/vendor',
    '--out', outDir,
  ], { cwd: repoRoot, stdio: 'ignore' });
  await sleep(800);
  const results = [];
  try {
    for (const build of values.builds.split(',')) {
      console.log(`[boot] ${build}`);
      const r = await bootOne(build, server);
      console.log(`       ok=${r.ok} bootMs=${r.bootMs?.toFixed(0)} errors=${r.consoleMessages.length}`);
      results.push(r);
    }
  } finally {
    server.kill();
  }
  const requests = readFileSync(resolve(outDir, 'requests.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
  const buildRequests = requests.filter((r) => r.path.includes('/Build/'));
  const summary = {
    run: values.run,
    acceptEncodingSeenOnBuildRequests: [...new Set(buildRequests.map((r) => r.acceptEncoding))],
    results,
  };
  writeFileSync(resolve(outDir, 'boot-summary.json'), JSON.stringify(summary, null, 2));
  console.log('Accept-Encoding seen:', JSON.stringify(summary.acceptEncodingSeenOnBuildRequests));
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
