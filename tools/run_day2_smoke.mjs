#!/usr/bin/env node
// Day 2 smoke test: fake camera video -> product page -> Start -> hand tracking -> notes. Not a measurement; checks that the chain works.
// Usage: node tools/run_day2_smoke.mjs --video <hands1.y4m> [--seconds 20] [--query input=hand]
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { openProduct, sleep, startAndWaitReady, repoRoot } from './lib/day2-browser.mjs';
import { startSiteServer } from './lib/day1-browser.mjs';
import { screenshot } from './lib/day1-browser.mjs';

const { values } = parseArgs({ options: {
  video: { type: 'string' }, seconds: { type: 'string', default: '20' }, query: { type: 'string', default: 'input=hand' }, out: { type: 'string', default: 'measurements/day2/smoke' },
} });
const out = resolve(repoRoot, values.out);
mkdirSync(out, { recursive: true });
const server = startSiteServer({ port: 8790, outDir: out });
await sleep(800);
let session;
try {
  session = await openProduct({ port: 8790, debugPort: 9360, query: values.query, video: values.video });
  const { cdp, consoleMessages } = session;
  await sleep(800);
  await screenshot(cdp, resolve(out, '1-start-screen.png'));
  const ready = await startAndWaitReady(cdp);
  console.log('ready', JSON.stringify(ready));
  await sleep(Number(values.seconds) * 1000);
  await screenshot(cdp, resolve(out, '2-playing.png'));
  const result = await cdp.evaluate(`JSON.stringify({
    tracker: window.Downbeat.hands.snapshot(), label: window.Downbeat.page.inputLabel,
    notes: window.Downbeat.page.noteLog.map((n) => ({ part: n.part, step: n.step, v: +n.velocity.toFixed(2), slot: n.slot, capture: n.captureMs, decided: n.decidedMs, when: n.whenPerfMs })),
  })`);
  writeFileSync(resolve(out, 'result.json'), result);
  const r = JSON.parse(result);
  console.log('label:', r.label);
  console.log('tracker:', JSON.stringify({ state: r.tracker?.state, frames: r.tracker?.frames, inferMs: r.tracker?.inferMs, byHands: r.tracker?.inferMsByHandCount }));
  console.log('notes:', r.notes.length, JSON.stringify(r.notes.slice(0, 20).map((n) => [n.part, n.step, n.v, n.slot])));
  console.log('console:', JSON.stringify(consoleMessages.slice(0, 8)));
} finally {
  await session?.chrome.close();
  server.stop();
}
