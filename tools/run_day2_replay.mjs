#!/usr/bin/env node
// Replay determinism in the real product: the same recording is played in two fresh page loads (loop off) and the notes the game
// actually scheduled are compared. Also saves a screenshot of replay mode.
// Usage: node tools/run_day2_replay.mjs [--runs 2] [--file replay/demo-hands.json] [--out measurements/day2/replay]
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { openProduct, repoRoot, sleep, startAndWaitReady } from './lib/day2-browser.mjs';
import { screenshot, startSiteServer } from './lib/day1-browser.mjs';

const { values } = parseArgs({ options: {
  runs: { type: 'string', default: '2' }, file: { type: 'string', default: 'replay/demo-hands.json' }, out: { type: 'string', default: 'measurements/day2/replay' },
} });
const out = resolve(repoRoot, values.out);
mkdirSync(out, { recursive: true });
const SLOTS_PER_BAR = 16;
const server = startSiteServer({ port: 8793, outDir: out });
await sleep(800);

async function oneRun(index) {
  const session = await openProduct({ port: 8793, debugPort: 9363, query: `input=replay&loop=0&file=${values.file}` });
  try {
    const { cdp, consoleMessages } = session;
    await startAndWaitReady(cdp);
    // wait until the notes stop coming: the recording is about 16 s long and starts on the next bar line
    let last = -1;
    let stable = 0;
    const started = Date.now();
    while (stable < 12 && Date.now() - started < 60000) {
      await sleep(500);
      const n = await cdp.evaluate('window.Downbeat.page.noteLog.length');
      stable = n === last && n > 0 ? stable + 1 : 0;
      last = n;
      if (index === 0 && n >= 6 && !globalThis.__shot) { globalThis.__shot = true; await screenshot(cdp, resolve(out, 'replay-mode.png')); }
    }
    const notes = await cdp.evaluate('JSON.stringify(window.Downbeat.page.noteLog)');
    const parsed = JSON.parse(notes);
    const label = await cdp.evaluate('window.Downbeat.page.inputLabel');
    writeFileSync(resolve(out, `run-${index + 1}-notes.json`), notes);
    return { label, notes: parsed, console: consoleMessages.filter((m) => !/gl_context|NORM_RECT/.test(m.text)) };
  } finally {
    await session.chrome.close();
  }
}

const runs = [];
try {
  for (let i = 0; i < Number(values.runs); i++) runs.push(await oneRun(i));
} finally {
  server.stop();
}

const key = (n) => [n.part, n.step, n.velocity.toFixed(6)].join('/');
const first = runs[0].notes;
const comparison = runs.slice(1).map((run, i) => {
  const a = first;
  const b = run.notes;
  const sameLength = a.length === b.length;
  const sameSequence = sameLength && a.every((n, k) => key(n) === key(b[k]));
  const relSlots = (list) => list.map((n) => n.slot - list[0].slot);
  const sameRelativeSlots = sameLength && relSlots(a).every((s, k) => s === relSlots(b)[k]);
  const sameBarPosition = sameLength && a.every((n, k) => ((n.slot % SLOTS_PER_BAR) + SLOTS_PER_BAR) % SLOTS_PER_BAR === ((b[k].slot % SLOTS_PER_BAR) + SLOTS_PER_BAR) % SLOTS_PER_BAR);
  const requestOffsets = (list) => list.map((n) => +(n.requestedAt - list[0].requestedAt).toFixed(9));
  const sameRequestTimes = sameLength && requestOffsets(a).every((t, k) => t === requestOffsets(b)[k]);
  return { run: i + 2, notesA: a.length, notesB: b.length, sameSequence, sameRelativeSlots, sameBarPosition, sameRequestTimesRelativeToFirst: sameRequestTimes };
});
const summary = {
  file: values.file, runs: runs.length, label: runs[0].label, notesPerRun: runs.map((r) => r.notes.length), dropped: runs.map((r) => r.notes.filter((n) => n.slot < 0).length),
  late: runs.map((r) => r.notes.filter((n) => Number.isFinite(n.when) && n.when < n.audioNow).length),
  sequence: first.map((n) => `${['harp', 'recorder', 'marimba'][n.part]}:${n.step}@${n.velocity.toFixed(2)}`), comparison,
  console: runs.map((r) => r.console.slice(0, 3)),
};
writeFileSync(resolve(out, 'determinism-summary.json'), JSON.stringify(summary, null, 2));
console.log(JSON.stringify({ ...summary, sequence: summary.sequence.join(' ') }, null, 1));
