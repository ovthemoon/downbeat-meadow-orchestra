#!/usr/bin/env node
// Aggregates the same path x scenario cell over several S-1 run folders (repeats) into a Markdown table.
// Usage: node tools/summarize_repeats.mjs measurements/s1/A-vsync2-kiosk measurements/s1/A2-... measurements/s1/A3-...
import { readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { CRITERIA } from './lib/s1-analysis.mjs';

const PATH_LABEL = { 'unity-scheduled': 'Unity PlayScheduled', 'unity-play': 'Unity Play()/frame', webaudio: 'Web Audio start(when)' };
const f = (v, d = 2) => Number(v).toFixed(d);
const range = (values, d = 2) => (values.length === 1 ? f(values[0], d) : `${f(Math.min(...values), d)} - ${f(Math.max(...values), d)}`);
const median = (values) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];

const rows = new Map();
for (const dir of process.argv.slice(2)) {
  const analysis = JSON.parse(readFileSync(join(resolve(dir), 'analysis.json'), 'utf8'));
  for (const r of analysis.beat.filter((x) => !x.error && ['calm', 'stress'].includes(x.scenario))) {
    const key = `${r.scenario}|${r.path}`;
    const steady = r.segments.all;
    rows.set(key, [...(rows.get(key) ?? []), { dir, std: steady.stdMs, p95: steady.p95AbsMs, max: steady.maxAbsMs, drift: r.referenceFitDriftMsPerMin, clock: r.clock.ctxMinusWallMs, missed: r.wholeRun.slots.missed }]);
  }
}

const lines = [
  `Criteria (plan 6.3): std <= ${CRITERIA.stdMs} ms, max <= ${CRITERIA.maxMs} ms, |drift| <= ${CRITERIA.driftMsPerMin} ms/min. Values are min - max over the runs; "pass" counts runs meeting all three.`,
  '',
  '| Scenario | Path | Runs | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Missed slots | Audio clock - wall clock (ms/run) | Pass |',
  '|---|---|---|---|---|---|---|---|---|---|',
];
for (const scenario of ['calm', 'stress']) {
  for (const path of Object.keys(PATH_LABEL)) {
    const cells = rows.get(`${scenario}|${path}`);
    if (!cells) continue;
    const passes = cells.filter((c) => c.std <= CRITERIA.stdMs && c.max <= CRITERIA.maxMs && Math.abs(c.drift) <= CRITERIA.driftMsPerMin).length;
    lines.push(`| ${scenario} | ${PATH_LABEL[path]} | ${cells.length} | ${range(cells.map((c) => c.std))} (median ${f(median(cells.map((c) => c.std)))}) | ${range(cells.map((c) => c.p95))} | ${range(cells.map((c) => c.max))} | ${range(cells.map((c) => c.drift), 1)} | ${range(cells.map((c) => c.missed), 0)} | ${range(cells.map((c) => c.clock), 0)} | ${passes}/${cells.length} |`);
  }
}
const out = lines.join('\n');
console.log(out);
writeFileSync(resolve('measurements/s1/repeats-summary.md'), out + '\n');
