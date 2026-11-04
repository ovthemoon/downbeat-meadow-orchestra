#!/usr/bin/env node
// Drives the S-1 beat clock matrix in a real Chrome: starts the local server, launches Chrome, runs
// path x scenario, applies tab disturbances through DevTools, and leaves raw data under measurements/s1/<run>/.
// Usage: node tools/run_s1.mjs --build day0 --paths unity-scheduled,unity-play,webaudio --scenarios calm,stress,hidden --duration 60
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { loadavg, cpus } from 'node:os';
import { parseArgs } from 'node:util';
import { launchChrome, sleep } from './lib/cdp.mjs';
import { DISTURBANCE_AT_SEC, DISTURBANCE_LENGTH_SEC, scenariosFor } from './lib/s1-scenarios.mjs';

const { values } = parseArgs({
  options: {
    build: { type: 'string', default: 'day0' },
    paths: { type: 'string', default: 'unity-scheduled,unity-play,webaudio' },
    scenarios: { type: 'string', default: 'calm,stress,hidden' },
    duration: { type: 'string', default: '60' },
    port: { type: 'string', default: '8765' },
    debugPort: { type: 'string', default: '9333' },
    run: { type: 'string', default: new Date().toISOString().replace(/[:.]/g, '-') },
    lookahead: { type: 'string', default: '0.1' },
    flags: { type: 'string', default: 'kiosk' },
    vsync: { type: 'string', default: '1' },
    ahead: { type: 'boolean', default: false },
    bg: { type: 'string', default: '1' },
  },
});

const repoRoot = resolve(import.meta.dirname, '..');
const outRoot = resolve(repoRoot, 'measurements');
const runDir = `s1/${values.run}`;
const durationSec = Number(values.duration);
const AHEAD_SEC = [0.25, 0.5, 1.0, 2.0, 0.25, 0.5, 1.0, 2.0, 0.25, 0.5, 1.0, 2.0];
const paths = values.paths.split(',');
const scenarios = scenariosFor(values.scenarios.split(','));

function startServer() {
  const proc = spawn('node', [
    'web/serve.mjs', '--port', values.port,
    '--mount', '/=web/public', '--mount', '/builds=unity/Downbeat/Builds', '--mount', '/vendor=web/vendor',
    '--out', outRoot,
  ], { cwd: repoRoot, stdio: 'ignore' });
  return proc;
}

async function dispatchWindowEvent(cdp, name) {
  await cdp.evaluate(`window.dispatchEvent(new Event('${name}')); true`);
}

async function disturb(chrome, kind) {
  const created = await chrome.browser.send('Target.createTarget', kind === 'hidden'
    ? { url: 'about:blank' }
    : { url: 'about:blank', newWindow: true, left: 1180, top: 560, width: 300, height: 260 });
  await chrome.browser.send('Target.activateTarget', { targetId: created.targetId });
  return created.targetId;
}

async function runOne(chrome, cdp, path, scenario) {
  const label = `${path}__${scenario.name}`;
  const cfg = { label, path, durationSec, lookaheadSec: Number(values.lookahead), block: scenario.block, runDir, frameDivisor: Number(values.vsync) };
  console.log(`[run] ${label}`);
  const pending = cdp.evaluate(`window.day0.runBeat(${JSON.stringify(cfg)})`);
  let disturbanceTarget = null;
  if (scenario.disturbance) {
    await sleep((DISTURBANCE_AT_SEC + 0.6) * 1000);
    const startedAt = Date.now();
    if (scenario.disturbance === 'blur') {
      await dispatchWindowEvent(cdp, 'blur');
      await sleep(DISTURBANCE_LENGTH_SEC * 1000 - (Date.now() - startedAt));
      await dispatchWindowEvent(cdp, 'focus');
    } else {
      disturbanceTarget = await disturb(chrome, scenario.disturbance);
      await sleep(DISTURBANCE_LENGTH_SEC * 1000 - (Date.now() - startedAt));
      await chrome.browser.send('Target.activateTarget', { targetId: (await chrome.pages()).find((p) => p.url.includes('s1.html')).id });
      await chrome.browser.send('Target.closeTarget', { targetId: disturbanceTarget });
    }
  }
  const result = { ...(await pending), loadAvgAtEnd: loadavg(), cpuCount: cpus().length };
  console.log(`      loadavg=${result.loadAvgAtEnd.map((v) => v.toFixed(1)).join('/')} issued=${result.issued} skippedLate=${result.skippedLate} contexts=${JSON.stringify(result.contexts)}`);
  return result;
}

async function main() {
  mkdirSync(resolve(outRoot, runDir), { recursive: true });
  const server = startServer();
  await sleep(800);
  const chromeArgs = [
    '--use-fake-ui-for-media-stream', '--autoplay-policy=no-user-gesture-required',
    '--window-position=40,40', '--window-size=1100,760',
  ];
  if (values.flags === 'kiosk') {
    chromeArgs.push('--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows');
  }
  const url = `http://localhost:${values.port}/s1.html?build=${values.build}&vsync=${values.vsync}&bg=${values.bg}`;
  const chrome = await launchChrome({ port: Number(values.debugPort), args: chromeArgs, url });
  const summary = { run: values.run, chromeVersion: chrome.version.Browser, chromeArgs, vsync: Number(values.vsync), runInBackground: values.bg === '1', paths, scenarios, durationSec, results: [] };
  try {
    const { cdp } = await chrome.connectToPage('s1.html');
    await cdp.send('Runtime.enable');
    await cdp.send('Page.bringToFront');
    await cdp.waitFor('window.day0Ready');
    const init = await cdp.evaluate('window.day0Ready');
    console.log(`[init] Unity boot ${init.bootMs.toFixed(0)} ms`);
    console.log('[ping]', JSON.stringify(await cdp.evaluate('window.day0.ping()')));
    if (values.ahead) {
      console.log('[ahead] PlayScheduled(dspTime + ahead) check');
      summary.ahead = await cdp.evaluate(`window.day0.runAhead(${JSON.stringify({ runDir, label: 'ahead', aheadSec: AHEAD_SEC })})`);
      console.log('      ', JSON.stringify(summary.ahead));
    }
    for (const scenario of values.ahead ? [] : scenarios) {
      for (const path of paths) {
        summary.results.push({ path, scenario: scenario.name, ...(await runOne(chrome, cdp, path, scenario)) });
        await sleep(1500);
      }
    }
    cdp.close();
  } finally {
    writeFileSync(resolve(outRoot, runDir, 'run-summary.json'), JSON.stringify(summary, null, 2));
    await chrome.close();
    server.kill();
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
