#!/usr/bin/env node
// Day 1 smoke test: start screen -> Start -> Unity boots with content + samples -> clicks play notes. Saves screenshots.
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { elementCenter, click, openSite, pressStart, repoRoot, screenshot, stagePoint, startSiteServer } from './lib/day1-browser.mjs';
import { sleep } from './lib/cdp.mjs';

const out = resolve(repoRoot, 'measurements', 'day1', 'smoke');
mkdirSync(out, { recursive: true });
const server = startSiteServer({ port: 8780, outDir: out });
await sleep(800);
let session;
try {
  session = await openSite({ port: 8780, debugPort: 9350, path: 'index.html?input=mouse' });
  const { cdp, consoleMessages } = session;
  await sleep(1000);
  await screenshot(cdp, resolve(out, '1-start-screen.png'));
  const state = await pressStart(cdp);
  console.log('ready', JSON.stringify(state));
  await sleep(1500);
  const canvas = await elementCenter(cdp, '#unity-canvas');
  console.log('canvas', JSON.stringify(canvas));
  for (const [part, step] of [[0, 2], [1, 6], [2, 4], [0, 8], [2, 1]]) {
    const p = stagePoint(canvas, part, step);
    await click(cdp, p.x, p.y);
    await sleep(350);
  }
  await screenshot(cdp, resolve(out, '2-after-clicks.png'));
  const snap = await cdp.evaluate('JSON.stringify(window.Downbeat.audio.snapshot())');
  console.log('audio', snap.slice(0, 600));
  console.log('console', JSON.stringify(consoleMessages.slice(0, 8)));
} finally {
  await session?.chrome.close();
  server.stop();
}
