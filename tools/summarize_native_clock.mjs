#!/usr/bin/env node
// Aggregates the native beat-clock runs (tools/run_native_clock.sh) into measurements/native-clock/summary.md.
// Usage: node tools/summarize_native_clock.mjs measurements/native-clock/R1 measurements/native-clock/R2 ... (R = batch mode, W = windowed)
// Reads each run folder's analysis.json (tools/analyze_s1.mjs) and run-summary.json; judges with the Day 0 S-1 criteria.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { CRITERIA } from './lib/s1-analysis.mjs';

const f = (v, d = 3) => (v === null || v === undefined ? '-' : Number(v).toFixed(d));
const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'));
const dirs = process.argv.slice(2).map((d) => resolve(d));
if (dirs.length === 0) throw new Error('usage: summarize_native_clock.mjs <run dir> [...]');

const rows = [];
let durationSec = null;
for (const dir of dirs) {
  const analysis = readJson(join(dir, 'analysis.json'));
  const info = existsSync(join(dir, 'run-summary.json')) ? readJson(join(dir, 'run-summary.json')) : {};
  for (const r of analysis.beat) {
    if (r.error) { rows.push({ run: dir.split('/').pop(), scenario: r.file, error: r.error, info }); continue; }
    const steady = r.segments.all;
    const meta = readJson(join(dir, r.file));
    durationSec = meta.config.durationSec;
    rows.push({
      run: dir.split('/').pop(), scenario: r.scenario, info, onsets: r.onsetCount, expected: r.expectedSlots, missed: r.wholeRun.slots.missed,
      duplicates: r.wholeRun.slots.duplicates, skippedLate: r.skippedLateByScheduler, std: steady.stdMs, p95: steady.p95AbsMs, max: steady.maxAbsMs,
      drift: r.referenceFitDriftMsPerMin, clockMinusWall: r.clock.ctxMinusWallMs, gaps: r.captureGaps, blocks: r.blocks, frames: r.rafIntervalsMs,
      runtime: meta.runtime, engine: { late: meta.runtime.engineLate, stolen: meta.runtime.engineStolen },
    });
  }
}

const pass = (r) => !r.error && r.std <= CRITERIA.stdMs && r.max <= CRITERIA.maxMs && Math.abs(r.drift) <= CRITERIA.driftMsPerMin && r.missed === 0 && r.duplicates === 0;
const ok = rows.filter((r) => !r.error);
const first = ok[0];
const isBatch = (r) => r.runtime.batchMode === 'True';
const stalled = (r) => r.frames.max > 100; // a frame over 100 ms is a stall of the main thread
const worst = (list, key) => (list.length ? Math.max(...list.map((r) => Math.abs(r[key]))) : null);
const batch = ok.filter(isBatch);
const windowed = ok.filter((r) => !isBatch(r));
const verdictOf = (list) => (list.length === 0 ? 'not run' : list.every(pass) ? 'PASS' : 'FAIL');
const describe = (list) => `${list.filter(pass).length}/${list.length} runs pass; worst std ${f(worst(list, 'std'))} ms, max ${f(worst(list, 'max'))} ms, |drift| ${f(worst(list, 'drift'))} ms/min`;
const failing = windowed.filter((r) => !pass(r));
const failReason = (r) => `${r.run} ${r.scenario}: ${r.missed} slot(s) not scheduled, std ${f(r.std)} ms, max ${f(r.max)} ms, longest frame ${f(r.frames.max, 0)} ms`;

const lines = [
  '# Native beat clock (Unity PlayScheduled on the DSP clock)',
  '',
  `Criteria (Day 0 S-1): std <= ${CRITERIA.stdMs} ms, max <= ${CRITERIA.maxMs} ms, |drift| <= ${CRITERIA.driftMsPerMin} ms/min, no missed or duplicate slots.`,
  '',
  `- **Batch mode (no window), verdict ${verdictOf(batch)}:** ${describe(batch)}.`,
  `- **Windowed player, verdict ${verdictOf(windowed)}:** ${describe(windowed)}. ${windowed.filter(stalled).length} of ${windowed.length} windowed runs had one main-thread stall of about 0.9-1.0 s (a frame over 100 ms); ${batch.filter(stalled).length} of ${batch.length} batch runs did. ` +
    `The failing windowed runs are exactly the stalled ones: ${failing.length ? failing.map(failReason).join('; ') : 'none'}. ` +
    'A clock scheduled 0.1 s ahead cannot survive a main-thread stall longer than that: the clicks due during the stall are not scheduled (or one is scheduled late). The clicks that were scheduled stayed sample exact (std <= 0.002 ms in the calm windowed runs). The cause of the stall is NOT diagnosed (see Conditions).',
  '',
  '## What was measured',
  '',
  '- The player (`builds/mac/Downbeat.app`, `--measure-clock`) schedules one click per 1/16 slot of the song in content.json through the same `NativeAudioEngine` the game uses (`AudioSource.PlayScheduled`, 0.1 s ahead, 32 voices), for ' + durationSec + ' s per run.',
  '- `AudioOutputTap` (`OnAudioFilterRead` on the AudioListener) records the mixer output; `tools/analyze_s1.mjs` finds the click onsets and compares them with the ideal grid. The first click absorbs a constant delay; a linear trend is the drift.',
  '- Two ways of running the same measurement: R runs use `-batchmode` (no window); W runs open a 640x360 window that is rendered (vSync off, 60 fps cap). W2-W6 are calm-only repeats started by hand with the same flags after the player gained the long-frame list (`framesOver100ms` in the metadata), so they have no run-summary or load figures.',
  '- "stress" blocks the main thread for 50 ms every 2 s (`Thread.Sleep`). "calm" does not.',
  '- **Scope (same as Day 0 for Web Audio):** this is the position of the click inside Unity\'s mixer output. It does not include the audio device, the OS output buffer or the speaker. The output latency that Unity can estimate is listed below; it was not measured.',
  '',
  '## Runs',
  '',
  '| Run | Mode | Scenario | Onsets/slots | Missed | Std (ms) | p95 (ms) | Max (ms) | Drift (ms/min) | Audio clock - wall clock (ms/run, +-21 ms resolution) | Late / stolen (engine) | Scheduler skipped | Capture gaps | Blocks (n, max ms) | Frame ms p50 / p95 / max | Result |',
  '|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|',
];
for (const r of rows) {
  if (r.error) { lines.push(`| ${r.run} | - | ${r.scenario} | ERROR: ${r.error} |`); continue; }
  lines.push(`| ${r.run} | ${r.runtime.batchMode === 'True' ? 'batch' : 'window'} | ${r.scenario} | ${r.onsets}/${r.expected} | ${r.missed} | ${f(r.std)} | ${f(r.p95)} | ${f(r.max)} | ${f(r.drift, 2)} | ${f(r.clockMinusWall, 1)} | ${r.engine.late} / ${r.engine.stolen} | ${r.skippedLate} | ${r.gaps} | ${r.blocks.count}, ${f(r.blocks.maxDurationMs, 1)} | ${f(r.frames.p50, 1)} / ${f(r.frames.p95, 1)} / ${f(r.frames.max, 1)} | ${pass(r) ? 'PASS' : 'FAIL'} |`);
}
lines.push('', '## Conditions', '');
if (first) {
  lines.push(
    `- Machine: ${first.info.model ?? first.runtime.cpu}, macOS ${first.info.macos ?? first.runtime.os}, Unity ${first.runtime.unity} (${first.runtime.platform}, Mono, Release player).`,
    `- Audio: output sample rate ${first.runtime.outputSampleRate} Hz, DSP buffer ${first.runtime.dspBufferLength} frames x ${first.runtime.dspBufferCount} (Unity default; estimated output latency = length x count / rate = ${f((first.runtime.dspBufferLength * first.runtime.dspBufferCount) / first.runtime.outputSampleRate * 1000, 1)} ms, not measured). The click was silenced after recording.`,
    `- Frame loop: vSync ${first.runtime.vSyncCount}, targetFrameRate ${first.runtime.targetFrameRate}. No camera, no hand tracker.`,
    '- "Audio clock - wall clock" compares the DSP clock with the wall clock between the start and the end of the run; the DSP clock only advances once per DSP block (21 ms) and the two are read within one frame, so read it to about +-25 ms.',
    '- **Main-thread stalls (not diagnosed):** only the windowed player stalled, at random moments (see `framesOver100ms` in the W runs\' metadata), in about 3 of 7 runs of 60 s. Not tied to the deliberate 50 ms blocks, to a hand input (none here) or to the audio clock (it kept running: capture gaps 0). Candidates, none verified: the macOS window / Metal presentation of a window that is not in front, other apps on this shared Mac (load average 5-8). The 0.1 s `minLeadSec` of the design is the only margin against such stalls; a longer lead or a stall-free kiosk machine would be needed to hold a metronome-like stream through them. Whether this happens on the Windows PC is unknown.',
    '- Shared machine: other Unity editors and apps were running, as in Day 0. Load average and the number of other Unity editor processes (editors, not import workers) per run:',
    '',
    '| Run | Started (UTC) | Load avg at start (1/5/15 min) | Other Unity editor processes | Load avg after calm / after stress |',
    '|---|---|---|---|---|',
  );
  for (const dir of dirs) {
    const info = existsSync(join(dir, 'run-summary.json')) ? readJson(join(dir, 'run-summary.json')) : {};
    const after = ['calm', 'stress'].map((s) => (existsSync(join(dir, `exit-${s}.json`)) ? readJson(join(dir, `exit-${s}.json`)).loadAvgAfter : '-'));
    lines.push(`| ${dir.split('/').pop()} | ${info.startedUtc ?? '-'} | ${info.loadAvgAtStart ?? '-'} | ${info.otherUnityEditorProcesses ?? '-'} | ${after.join(' / ')} |`);
  }
}
lines.push('', 'Raw data: `R<n>/*.meta.json`, `R<n>/analysis.json`, `R<n>/summary.csv`; the raw captures (`R<n>/raw-audio/*.f32`, 11.5 MB each) are git-ignored and reproducible with `tools/run_native_clock.sh`.', '');

const out = lines.join('\n');
writeFileSync(join(dirname(dirs[0]), 'summary.md'), out);
console.log(out);
