// Helpers for driving the Day 1 site in a real Chrome: serve dist/site, open the page at a fixed size, click like a user,
// collect console output, take screenshots. Input goes through DevTools Input.dispatchMouseEvent, which Chrome treats as a
// real user gesture (so the Start button unlocks audio the same way a person's click would).
import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { launchChrome, sleep } from './cdp.mjs';

export const repoRoot = resolve(import.meta.dirname, '..', '..');

export function startSiteServer({ port, outDir, site = 'dist/site' }) {
  const proc = spawn('node', ['web/serve.mjs', '--port', String(port), '--mount', `/=${site}`, '--out', outDir], { cwd: repoRoot, stdio: 'ignore' });
  return { stop: () => proc.kill() };
}

/**
 * @param {{port:number, debugPort:number, path?:string, width?:number, height?:number, args?:string[]}} options
 */
export async function openSite({ port, debugPort, path = 'index.html', width = 1600, height = 700, args = [] }) {
  const chrome = await launchChrome({
    port: debugPort,
    args: ['--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows',
      `--window-size=${width + 20},${height + 140}`, '--window-position=30,30', ...args],
    url: `http://localhost:${port}/${path}`,
  });
  const { cdp } = await chrome.connectToPage(path.split('?')[0]);
  const consoleMessages = [];
  cdp.on('Runtime.consoleAPICalled', (p) => {
    const text = p.args.map((a) => a.value ?? a.description ?? '').join(' ');
    if (['error', 'warning'].includes(p.type) || /exception|halting|abort/i.test(text)) consoleMessages.push({ type: p.type, text: text.slice(0, 300) });
  });
  cdp.on('Runtime.exceptionThrown', (p) => consoleMessages.push({ type: 'exception', text: (p.exceptionDetails.exception?.description ?? p.exceptionDetails.text).slice(0, 300) }));
  await cdp.send('Runtime.enable');
  await cdp.send('Page.enable');
  await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
  await cdp.send('Page.bringToFront');
  return { chrome, cdp, consoleMessages };
}

export async function click(cdp, x, y) {
  const base = { x, y, button: 'left', clickCount: 1 };
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...base });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...base });
}

export async function elementCenter(cdp, selector) {
  return cdp.evaluate(`(() => { const r = document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height, left: r.left, top: r.top }; })()`);
}

export async function screenshot(cdp, file, { width, height } = {}) {
  const clip = width && height ? { x: 0, y: 0, width, height, scale: 1 } : undefined;
  const shot = await cdp.send('Page.captureScreenshot', { format: 'png', ...(clip ? { clip } : {}) });
  writeFileSync(file, Buffer.from(shot.data, 'base64'));
}

/** Click Start and wait until Unity reports ready (content, samples and the running audio clock). */
export async function pressStart(cdp, { timeoutMs = 60000 } = {}) {
  const button = await elementCenter(cdp, '#start');
  await click(cdp, button.x, button.y);
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const state = await cdp.evaluate('({ ready: window.Downbeat.page.isReady, status: window.Downbeat.page.statusText })');
    if (state.ready) return state;
    if (String(state.status).startsWith('error') || String(state.status).startsWith('could not start')) throw new Error(`page reported: ${state.status}`);
    await sleep(200);
  }
  throw new Error('Unity did not report ready in time');
}

/** Pixel position inside the stage view for a part (0..2) and pitch step (0..9), given the canvas rectangle. */
export function stagePoint(canvas, part, step, { stageFraction = 2 / 3, steps = 10, margin = 0.15 } = {}) {
  const stageW = canvas.w * stageFraction;
  const x = canvas.left + stageW * (part + 0.5) / 3;
  const t = (step + 0.5) / steps; // 0 = lowest
  const y = canvas.top + canvas.h * (1 - (margin + t * (1 - 2 * margin)));
  return { x, y };
}
