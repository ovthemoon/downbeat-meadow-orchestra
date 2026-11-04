#!/usr/bin/env node
// Drives the S-2 test (MediaPipe + Unity in one page) in a real Chrome with a fake camera fed from a video file.
// Usage: node tools/run_s2.mjs --video /abs/path/hand.y4m --run s2-gpu-cpu --vsync 2 --window 20
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { launchChrome, sleep } from './lib/cdp.mjs';

const { values } = parseArgs({
  options: {
    video: { type: 'string' },
    build: { type: 'string', default: 'day0' },
    run: { type: 'string', default: `s2-${new Date().toISOString().replace(/[:.]/g, '-')}` },
    vsync: { type: 'string', default: '2' },
    window: { type: 'string', default: '20' },
    delegates: { type: 'string', default: 'GPU,CPU' },
    probe: { type: 'boolean', default: false },
    port: { type: 'string', default: '8767' },
    debugPort: { type: 'string', default: '9335' },
  },
});

const repoRoot = resolve(import.meta.dirname, '..');
const outRoot = resolve(repoRoot, 'measurements');
const runDir = `s2/${values.run}`;

function startServer() {
  return spawn('node', [
    'web/serve.mjs', '--port', values.port,
    '--mount', '/=web/public', '--mount', '/builds=unity/Downbeat/Builds', '--mount', '/vendor=web/vendor',
    '--out', outRoot,
  ], { cwd: repoRoot, stdio: 'ignore' });
}

/** Keep only what is needed to answer "did anything leave the machine?" */
export function summarizeNetwork(requests) {
  const hosts = {};
  for (const r of requests) {
    const host = new URL(r.url).host;
    hosts[host] = (hosts[host] ?? 0) + 1;
  }
  const posts = requests.filter((r) => r.method !== 'GET' && r.method !== 'HEAD').map((r) => ({ method: r.method, url: r.url, hasPostData: r.hasPostData }));
  return { total: requests.length, hosts, nonGetRequests: posts };
}

async function main() {
  mkdirSync(resolve(outRoot, runDir), { recursive: true });
  const server = startServer();
  await sleep(800);
  const chromeArgs = [
    '--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream',
    ...(values.video ? [`--use-file-for-fake-video-capture=${resolve(values.video)}`] : []),
    '--autoplay-policy=no-user-gesture-required',
    '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows',
    '--window-position=40,40', '--window-size=1100,760',
  ];
  const url = `http://localhost:${values.port}/s2.html?build=${values.build}&vsync=${values.vsync}`;
  const chrome = await launchChrome({ port: Number(values.debugPort), args: chromeArgs, url });
  const summary = { run: values.run, chromeVersion: chrome.version.Browser, chromeArgs, video: values.video ?? null };
  const requests = [];
  const consoleMessages = [];
  try {
    const { cdp } = await chrome.connectToPage('s2.html');
    cdp.on('Network.requestWillBeSent', (p) => requests.push({ url: p.request.url, method: p.request.method, hasPostData: Boolean(p.request.hasPostData) }));
    cdp.on('Runtime.consoleAPICalled', (p) => {
      const text = p.args.map((a) => a.value ?? a.description ?? '').join(' ').slice(0, 400);
      if (['error', 'warning'].includes(p.type) || /exception|halting|abort|out of memory/i.test(text)) consoleMessages.push({ type: p.type, text });
    });
    cdp.on('Runtime.exceptionThrown', (p) => consoleMessages.push({ type: 'exception', text: (p.exceptionDetails.exception?.description ?? p.exceptionDetails.text).slice(0, 400) }));
    await cdp.send('Network.enable');
    await cdp.send('Runtime.enable');
    await cdp.send('Page.bringToFront');
    await cdp.send('Page.reload');
    await sleep(1500);
    await cdp.waitFor('window.day0Ready', { timeoutMs: 60000 });
    summary.init = await cdp.evaluate('window.day0Ready');
    console.log('[init]', JSON.stringify(summary.init));
    const delegates = values.delegates.split(',');
    if (values.probe) {
      for (const delegate of delegates) {
        const r = await cdp.evaluate(`window.day0.trackerProbe(${JSON.stringify({ delegate, seconds: 6 })})`);
        (summary.probes ??= {})[delegate] = r;
        console.log(delegate, JSON.stringify({ load: r.loadInfo, probe: r.probe, frames: r.tracking.frames, withHand: r.tracking.framesWithHand, inferMs: r.tracking.inferMs, score: r.tracking.handednessScore }));
      }
      cdp.close();
      return;
    }
    const args = { label: 'result', windowSec: Number(values.window), delegates, runDir };
    summary.result = await cdp.evaluate(`window.day0.runS2(${JSON.stringify(args)})`);
    console.log('[result]', JSON.stringify(summary.result));
    summary.network = summarizeNetwork(requests);
    cdp.close();
  } finally {
    summary.consoleMessages = consoleMessages;
    if (consoleMessages.length) console.log('[console]', JSON.stringify(consoleMessages.slice(0, 6)));
    writeFileSync(resolve(outRoot, runDir, 'run-summary.json'), JSON.stringify(summary, null, 2));
    await chrome.close();
    server.kill();
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
