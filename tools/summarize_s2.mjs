#!/usr/bin/env node
// Prints the S-2 result as Markdown tables. Usage: node tools/summarize_s2.mjs measurements/s2/<run>/result.json
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve, join } from 'node:path';

const file = resolve(process.argv[2]);
const r = JSON.parse(readFileSync(file, 'utf8'));
const f = (v, d = 1) => (v === null || v === undefined ? '-' : Number(v).toFixed(d));

const names = { baselineBefore: 'No tracking (before)', pullOnly: 'Pull every frame, no tracker', trackingGPU: 'Tracking, GPU delegate', trackingCPU: 'Tracking, CPU delegate', baselineAfter: 'No tracking (after)' };
const lines = [
  `Unity frame-time window ${r.windowSec} s each (first 10 frames skipped), Unity 6000.3.9f1 Release, Chrome ${r.runtime.userAgent.match(/Chrome\/[\d.]+/)?.[0]}.`,
  '',
  '| Window | Unity fps (mean) | Frame ms p50 | p95 | p99 | max | Frames >25 ms | >50 ms | JS rAF interval p50 / p95 / max (ms) |',
  '|---|---|---|---|---|---|---|---|---|',
];
for (const [key, label] of Object.entries(names)) {
  const w = r.windows[key];
  if (!w?.unity) { lines.push(`| ${label} | ERROR ${w?.error ?? ''} |`); continue; }
  const u = w.unity;
  lines.push(`| ${label} | ${f(u.meanFps)} | ${f(u.p50Ms)} | ${f(u.p95Ms)} | ${f(u.p99Ms)} | ${f(u.maxMs)} | ${u.over25Ms} | ${u.over50Ms} | ${f(w.rafIntervalsMs.p50)} / ${f(w.rafIntervalsMs.p95)} / ${f(w.rafIntervalsMs.max)} |`);
}
lines.push('', '| Tracker (same windows) | Frames | With hand | Inference ms p50 | p95 | p99 | max | Model create (ms) | Hand-frame delivery to Unity, inference end to C# read (ms) p50 / p95 / max |', '|---|---|---|---|---|---|---|---|---|');
for (const key of ['trackingGPU', 'trackingCPU']) {
  const w = r.windows[key];
  if (!w?.tracking) continue;
  const t = w.tracking;
  lines.push(`| ${names[key]} | ${t.frames} | ${t.framesWithHand} (${f(t.handFrameRatio * 100, 0)}%) | ${f(t.inferMs.p50)} | ${f(t.inferMs.p95)} | ${f(t.inferMs.p99)} | ${f(t.inferMs.max)} | ${f(w.loadInfo.createMs, 0)} | ${f(w.unity.deliveryP50Ms)} / ${f(w.unity.deliveryP95Ms)} / ${f(w.unity.deliveryMaxMs)} (n=${w.unity.deliveryCount}) |`);
}
const b = r.bridge.trackerOff;
lines.push('', `Bridge, ${b.calls} calls, tracker off (performance.now resolution is 0.1 ms in this Chrome, so totals are multiples of 0.1 ms):`, '',
  '| Call | Total (ms) | Per call (us) | Managed allocation during loop |', '|---|---|---|---|',
  `| Day0_NowMs() | ${f(b.nowMsLoopMs, 2)} | ${f(b.nowMsUsPerCall, 3)} | - |`,
  `| Day0_ReadHands(double[135]) pull | ${f(b.readHandsLoopMs, 2)} | ${f(b.readHandsUsPerCall, 3)} | ${b.managedBytesDuringReadHands} B |`,
  `| JSON string of the same frame (${b.jsonLength} chars), ${b.calls / 10} calls | ${f(b.jsonLoopMs, 2)} | ${f(b.jsonUsPerCall, 2)} | ${b.managedBytesDuringJson} B (${f(b.managedBytesDuringJson / (b.calls / 10), 0)} B/call) |`,
  `| JS write into the C# double[] visible to C# | ${b.writeBackOk} (after 48 MB allocation / memory growth: ${b.writeBackAfterGrowthOk}) | | |`);
for (const [key, label] of [['noParse', 'SendMessage(JSON string), no parse'], ['parse', 'SendMessage(JSON string) + JsonUtility parse']]) {
  const s = r.sendMessage[key];
  lines.push(`| ${label}, ${s.calls} calls | ${f(s.totalMs, 1)} | ${f(s.usPerCall, 2)} (stringify alone ${f(s.stringifyOnlyMs * 1000 / s.calls, 2)}) | received by Unity: ${s.receivedByUnity} |`);
}
const out = lines.join('\n');
console.log(out);
writeFileSync(join(dirname(file), 'summary.md'), out + '\n');
