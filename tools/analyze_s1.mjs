#!/usr/bin/env node
// Offline analysis of one S-1 run folder: detect click onsets in the recorded output, compare with the 96 BPM 1/16 grid.
// Usage: node tools/analyze_s1.mjs measurements/s1/<run>   -> writes analysis.json and summary.csv next to the metadata
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { analyzeGrid, detectOnsets, pointsAgainstFit, summarizeMs, slotCoverage } from './lib/onsets.mjs';
import {
  aheadDelays, ctxToPerfMapper, disturbanceWindow, dspVersusContext, evaluateCriteria, splitByWindow,
} from './lib/s1-analysis.mjs';

const measurementsRoot = resolve(import.meta.dirname, '..', 'measurements');
const MIN_POINTS_FOR_FIT = 20;

function loadRaw(relativeFile) {
  const buf = readFileSync(join(measurementsRoot, relativeFile));
  return new Float32Array(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength));
}

function pickContext(meta) {
  const withSignal = meta.contexts.filter((c) => c.rawFile);
  if (withSignal.length === 0) return null;
  return withSignal.reduce((best, c) => (c.peak > best.peak ? c : best));
}

function onsetsOf(meta, ctx) {
  const samples = loadRaw(ctx.rawFile);
  const { onsets, threshold, peak } = detectOnsets(samples, ctx.sampleRate);
  return {
    timesCtxSec: onsets.map((o) => (ctx.firstFrame + o.frame) / ctx.sampleRate),
    threshold,
    peak,
  };
}

function segmentStats(points) {
  return { ...summarizeMs(points.map((p) => p.residualMs)), slots: points.length ? slotCoverage(points) : null };
}

function analyzeBeat(meta, relName) {
  const ctx = pickContext(meta);
  if (!ctx) return { file: relName, error: 'no audio captured (all contexts silent)' };
  const { timesCtxSec, peak } = onsetsOf(meta, ctx);
  const slotSec = 60 / meta.config.bpm / meta.config.slotsPerBeat;
  if (timesCtxSec.length < 3) return { file: relName, error: `only ${timesCtxSec.length} onsets detected`, peak };

  const whole = analyzeGrid(timesCtxSec, slotSec);
  // Only the scenarios that disturb on purpose get a disturbance window. In calm/stress runs the person using the
  // machine may switch windows (real blur events); Unity runs with runInBackground on, so that is recorded but not split out.
  const scenario = relName.split('__')[1]?.replace('.meta.json', '');
  const intended = ['hidden', 'blur'].includes(scenario);
  const eventsInRun = meta.pageEvents.filter((e) => e.type !== 'start' && e.perfMs >= meta.startedPerfMs);
  const window = intended ? disturbanceWindow(meta.pageEvents, { fromPerfMs: meta.startedPerfMs }) : null;
  const perfOfCtx = ctxToPerfMapper(meta.marks, ctx.id);
  const timesRel = timesCtxSec.map((t) => t - timesCtxSec[0]);

  // Reference grid: fitted on onsets before the disturbance when there was one, otherwise on the whole run.
  const perfOfPoint = (p) => perfOfCtx(timesCtxSec[0] + p.t);
  const referenceTimes = window ? timesCtxSec.filter((t) => perfOfCtx(t) < window.startMs) : timesCtxSec;
  const referenceFit = referenceTimes.length >= MIN_POINTS_FOR_FIT ? analyzeGrid(referenceTimes, slotSec) : whole;
  const points = pointsAgainstFit(timesCtxSec, referenceTimes[0], slotSec, referenceFit);
  const segments = splitByWindow(points, (p) => perfOfCtx(referenceTimes[0] + p.t), window);

  const startMark = meta.marks.find((m) => m.label.startsWith('beat-start'));
  const endMark = meta.marks.find((m) => m.label.startsWith('beat-end'));
  const wallSec = (endMark.perfMs - startMark.perfMs) / 1000;
  const ctxSec = endMark.ctxTimes[ctx.id] - startMark.ctxTimes[ctx.id];

  const dspEvents = (meta.unityEvents ?? []).filter((e) => e.kind === 'dsp').map((e) => e.data);
  const result = {
    file: relName,
    path: meta.config.path,
    scenario,
    pageEventsDuringRun: eventsInRun.map((e) => ({ type: e.type, visibility: e.visibility, blurred: e.blurred })),
    sampleRate: ctx.sampleRate,
    contextTag: ctx.tag,
    outputLatencySecReportedByBrowser: ctx.outputLatency,
    baseLatencySecReportedByBrowser: ctx.baseLatency,
    captureGaps: ctx.gaps,
    onsetCount: timesCtxSec.length,
    expectedSlots: meta.summary.totalSlots,
    issuedByScheduler: meta.summary.issued,
    skippedLateByScheduler: meta.summary.skippedLate,
    maxDspStallSec: meta.summary.maxDspStallSec ?? null,
    wholeRun: {
      offsetMs: whole.offsetMs, driftMsPerMin: whole.driftMsPerMin, slots: whole.slots,
      residual: whole.residual, raw: whole.raw, intervalErrorMs: whole.intervalErrorMs,
    },
    disturbanceWindowPerfMs: window,
    segments: Object.fromEntries(Object.entries(segments).map(([k, v]) => [k, segmentStats(v)])),
    referenceFitDriftMsPerMin: referenceFit.driftMsPerMin,
    clock: { wallSec, ctxSec, ctxMinusWallMs: (ctxSec - wallSec) * 1000 },
    blocks: { count: meta.blocks.length, maxDurationMs: Math.max(0, ...meta.blocks.map((b) => b.durationMs)) },
    rafIntervalsMs: meta.rafIntervalsMs,
    dspVersusContext: dspEvents.length ? dspVersusContext(dspEvents) : null,
    firstOnsetCtxSec: timesCtxSec[0],
    timesRelSecSample: timesRel.slice(0, 3),
  };
  const steadyPoints = segments.all ?? segments.before;
  const steady = summarizeMs(steadyPoints.map((p) => p.residualMs));
  result.criteria = {
    steady: evaluateCriteria(steady, referenceFit.driftMsPerMin),
    afterDisturbance: segments.after?.length ? evaluateCriteria(summarizeMs(segments.after.map((p) => p.residualMs)), referenceFit.driftMsPerMin) : null,
  };
  return { ...result, points: points.map((p) => [Number(p.t.toFixed(5)), p.slot, Number(p.residualMs.toFixed(3))]) };
}

function analyzeAhead(meta, relName) {
  const ctx = pickContext(meta);
  if (!ctx) return { file: relName, error: 'no audio captured' };
  const { timesCtxSec } = onsetsOf(meta, ctx);
  return {
    file: relName,
    onsetsCtxSec: timesCtxSec,
    delays: aheadDelays(meta.summary.records, timesCtxSec),
    dsp0VsCtx0: meta.summary.records.map((r) => ({ dsp0: r.dsp0, ctx0: r.ctx0, offset: r.dsp0 - r.ctx0 })),
  };
}

function csvRow(r) {
  if (r.error) return `${r.file},ERROR,${r.error}`;
  const s = r.segments.all ?? r.segments.before;
  const a = r.segments.after;
  const f = (v) => (v === null || v === undefined ? '' : Number(v).toFixed(3));
  return [
    r.path, r.scenario, r.onsetCount, r.expectedSlots, r.wholeRun.slots.missed, r.wholeRun.slots.duplicates, r.skippedLateByScheduler,
    f(s.stdMs), f(s.p95AbsMs), f(s.maxAbsMs), f(r.wholeRun.driftMsPerMin), f(r.wholeRun.offsetMs),
    a ? f(a.stdMs) : '', a ? f(a.maxAbsMs) : '',
    JSON.stringify(r.criteria.steady).replace(/,/g, ';'),
  ].join(',');
}

function main() {
  const dir = resolve(process.argv[2] ?? '');
  const metas = readdirSync(dir).filter((f) => f.endsWith('.meta.json')).sort();
  const results = { beat: [], ahead: [] };
  for (const name of metas) {
    const meta = JSON.parse(readFileSync(join(dir, name), 'utf8'));
    if (name.startsWith('ahead')) results.ahead.push(analyzeAhead(meta, name));
    else results.beat.push(analyzeBeat(meta, name));
  }
  writeFileSync(join(dir, 'analysis.json'), JSON.stringify(results, null, 1));
  const header = 'path,scenario,onsets,expectedSlots,missedSlots,duplicateSlots,schedulerSkippedLate,std_ms,p95_ms,max_ms,drift_ms_per_min,offset_ms,after_std_ms,after_max_ms,criteria';
  writeFileSync(join(dir, 'summary.csv'), [header, ...results.beat.map(csvRow)].join('\n') + '\n');
  console.log(header);
  results.beat.forEach((r) => console.log(csvRow(r)));
  results.ahead.forEach((r) => console.log(basename(r.file), JSON.stringify(r.delays ?? r.error)));
}

main();
