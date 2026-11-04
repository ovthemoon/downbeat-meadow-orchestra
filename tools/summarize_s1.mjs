#!/usr/bin/env node
// Prints Markdown tables (and writes summary.md) from analysis.json of one or more S-1 run folders.
// Usage: node tools/summarize_s1.mjs measurements/s1/A-vsync2-kiosk [more run folders]
import { readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const fmt = (v, digits = 2) => (v === null || v === undefined ? '-' : Number(v).toFixed(digits));
const PATH_LABEL = { 'unity-scheduled': 'Unity PlayScheduled', 'unity-play': 'Unity Play()/frame', webaudio: 'Web Audio start(when)' };
const SCENARIO_ORDER = ['calm', 'stress', 'hidden', 'blur'];

function row(r) {
  const steady = r.segments.all ?? r.segments.before;
  const crit = r.criteria.steady;
  const pass = crit.std && crit.max && crit.drift ? 'PASS' : `FAIL(${['std', 'max', 'drift'].filter((k) => !crit[k]).join(',')})`;
  const after = r.segments.after;
  const during = r.segments.during;
  return `| ${PATH_LABEL[r.path] ?? r.path} | ${r.scenario} | ${r.onsetCount}/${r.expectedSlots} | ${r.wholeRun.slots.missed} | ${fmt(steady.stdMs)} | ${fmt(steady.p95AbsMs)} | ${fmt(steady.maxAbsMs)} | ${fmt(r.referenceFitDriftMsPerMin, 1)} | ${pass} | ${after ? `${fmt(after.stdMs)} / ${fmt(after.maxAbsMs)}` : '-'} | ${during ? `${during.count} on, max ${fmt(during.maxAbsMs)}` : '-'} | ${r.skippedLateByScheduler} | ${fmt(r.clock.ctxMinusWallMs, 0)} |`;
}

function table(run, analysis) {
  const beats = analysis.beat.filter((r) => !r.error).sort((a, b) => SCENARIO_ORDER.indexOf(a.scenario) - SCENARIO_ORDER.indexOf(b.scenario) || a.path.localeCompare(b.path));
  const errors = analysis.beat.filter((r) => r.error);
  return [
    `### ${run}`,
    '',
    '| Path | Scenario | Onsets/slots | Missed slots | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Criteria | After disturbance std / max (ms) | During disturbance | Scheduler skipped late | Audio clock minus wall clock (ms per run) |',
    '|---|---|---|---|---|---|---|---|---|---|---|---|---|',
    ...beats.map(row),
    ...errors.map((e) => `| ${e.file} | ERROR | ${e.error} |`),
    '',
  ].join('\n');
}

const out = process.argv.slice(2).map((dir) => {
  const analysis = JSON.parse(readFileSync(join(resolve(dir), 'analysis.json'), 'utf8'));
  return table(dir.split('/').pop(), analysis);
}).join('\n');
console.log(out);
writeFileSync(join(resolve(process.argv[2]), 'summary.md'), out);
