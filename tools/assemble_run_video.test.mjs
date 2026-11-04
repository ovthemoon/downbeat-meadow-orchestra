import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { encodeWavPcm16Mono } from './lib/wav.mjs';

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), 'assemble_run_video.py');
const RATE = 48000;
const FIRST_SAMPLE_TIME = 100; // the audio clock's reading at sample 0
const SOUND_SECONDS = 2; // what an app closed after two seconds of a long recording leaves

function find(command, versionFlag, extra) {
  const probe = spawnSync(command, [versionFlag], { encoding: 'utf8' });
  if (!probe.error && probe.status === 0) return command;
  return extra && existsSync(extra) ? extra : null;
}

const python = find('python3', '--version');
const ffmpeg = process.env.FFMPEG || find('ffmpeg', '-version', '/opt/homebrew/bin/ffmpeg');
const ffprobe = find('ffprobe', '-version', '/opt/homebrew/bin/ffprobe');

/** A run folder as RunRecorder leaves it when the app is closed early: short sound, a few pictures, "endedEarly". */
function makeRun(root, { json = true, rows = ['1,99.98', '2,100.70', '3,101.40'], meta = {} } = {}) {
  const run = join(root, 'run');
  mkdirSync(join(run, 'frames'), { recursive: true });
  const tone = Float32Array.from({ length: RATE * SOUND_SECONDS }, (_, i) => 0.3 * Math.sin((2 * Math.PI * 440 * i) / RATE));
  writeFileSync(join(run, 'audio.wav'), encodeWavPcm16Mono(tone, RATE));
  writeFileSync(join(run, 'frames.csv'), `index,visualNow\n${rows.join('\n')}${rows.length ? '\n' : ''}`);
  if (json) {
    writeFileSync(join(run, 'recording.json'), JSON.stringify({
      audioSampleRate: RATE,
      audioFirstSampleTime: FIRST_SAMPLE_TIME,
      audioFrames: RATE * SOUND_SECONDS,
      frames: rows.length,
      endedEarly: true,
      recordedSeconds: 2.1,
      startedUtc: '2026-11-02T01:23:45.678Z',
      outputLatencySec: 0.0427,
      videoZeroUtc: '2026-11-02T01:23:45.720Z',
      ...meta,
    }));
  }
  return run;
}

function makePictures(run, count) {
  for (let i = 1; i <= count; i++) {
    const made = spawnSync(ffmpeg, ['-y', '-loglevel', 'error', '-f', 'lavfi', '-i', `color=c=0x${(i * 70).toString(16).padStart(2, '0')}4080:s=64x36`, '-frames:v', '1', join(run, 'frames', `f${String(i).padStart(5, '0')}.jpg`)]);
    assert.equal(made.status, 0, String(made.stderr));
  }
}

function assemble(run, out, extra = []) {
  return spawnSync(python, ['-I', SCRIPT, run, out, ...extra], { encoding: 'utf8' });
}

function withTempDir(body) {
  const root = mkdtempSync(join(tmpdir(), 'assemble-run-video-'));
  try {
    return body(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

function durationOf(file) {
  const probe = spawnSync(ffprobe, ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', file], { encoding: 'utf8' });
  return Number(probe.stdout.trim());
}

const canMakeVideo = python && ffmpeg && ffprobe;

test('a recording the app ended early becomes a video as long as its sound, not 30 s long', { skip: !canMakeVideo && 'needs python3, ffmpeg and ffprobe' }, () => {
  withTempDir((root) => {
    const run = makeRun(root);
    makePictures(run, 3);
    const out = join(root, 'out', 'take.mp4');

    const result = assemble(run, out);

    assert.equal(result.status, 0, result.stderr);
    const summary = JSON.parse(result.stdout);
    assert.equal(summary.seconds, SOUND_SECONDS);
    assert.equal(summary.endedEarly, true);
    assert.equal(summary.startedUtc, '2026-11-02T01:23:45.678Z', 'the start time of the recording is passed on');
    assert.equal(summary.outputLatencySec, 0.0427);
    assert.equal(summary.videoZeroUtc, '2026-11-02T01:23:45.720Z');
    assert.equal(summary.videoStartUtc, '2026-11-02T01:23:45.720Z', 'a video made from second 0 starts at videoZeroUtc');
    assert.equal(summary.distinctPictures, 3);
    assert.ok(Math.abs(durationOf(out) - SOUND_SECONDS) < 0.2, `video lasts ${durationOf(out)} s`);
    assert.match(result.stderr, /2\.00 s of sound/, 'it says that it made less than the default 30 s');
  });
});

test('a longer --seconds than the sound is cut to the sound', { skip: !canMakeVideo && 'needs python3, ffmpeg and ffprobe' }, () => {
  withTempDir((root) => {
    const run = makeRun(root);
    makePictures(run, 3);

    const result = assemble(run, join(root, 'take.mp4'), ['--seconds', '60']);

    assert.equal(result.status, 0, result.stderr);
    assert.equal(JSON.parse(result.stdout).seconds, SOUND_SECONDS);
  });
});

test('a shorter --seconds is kept', { skip: !canMakeVideo && 'needs python3, ffmpeg and ffprobe' }, () => {
  withTempDir((root) => {
    const run = makeRun(root);
    makePictures(run, 3);

    const result = assemble(run, join(root, 'take.mp4'), ['--seconds', '1']);

    assert.equal(result.status, 0, result.stderr);
    assert.equal(JSON.parse(result.stdout).seconds, 1);
  });
});

test('the wall-clock time of the video that was made is videoZeroUtc plus --start, across midnight too', { skip: !canMakeVideo && 'needs python3, ffmpeg and ffprobe' }, () => {
  withTempDir((root) => {
    const run = makeRun(root, { meta: { videoZeroUtc: '2026-11-02T23:59:59.720Z' } });
    makePictures(run, 3);

    const result = assemble(run, join(root, 'take.mp4'), ['--start', '1', '--seconds', '1']);

    assert.equal(result.status, 0, result.stderr);
    const summary = JSON.parse(result.stdout);
    assert.equal(summary.videoZeroUtc, '2026-11-02T23:59:59.720Z');
    assert.equal(summary.videoStartUtc, '2026-11-03T00:00:00.720Z');
  });
});

test('an older recording without these times is still assembled, and the summary says null', { skip: !canMakeVideo && 'needs python3, ffmpeg and ffprobe' }, () => {
  withTempDir((root) => {
    const run = makeRun(root, { meta: { startedUtc: undefined, outputLatencySec: undefined, videoZeroUtc: undefined, endedEarly: undefined } });
    makePictures(run, 3);

    const result = assemble(run, join(root, 'take.mp4'));

    assert.equal(result.status, 0, result.stderr);
    const summary = JSON.parse(result.stdout);
    assert.equal(summary.videoZeroUtc, null);
    assert.equal(summary.videoStartUtc, null);
    assert.equal(summary.endedEarly, false);
  });
});

test('a recording with no sound at all (audioFrames 0) is refused as empty, not made 30 s long', { skip: !python && 'needs python3' }, () => {
  withTempDir((root) => {
    const run = makeRun(root, { meta: { audioFrames: 0 } });

    const result = assemble(run, join(root, 'take.mp4'));

    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /audioFrames 0: the recording has no sound/);
  });
});

test('a folder without recording.json (the app was closed by a build from before the early finish) is refused with a plain message', { skip: !python && 'needs python3' }, () => {
  withTempDir((root) => {
    const run = makeRun(root, { json: false });

    const result = assemble(run, join(root, 'take.mp4'));

    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /recording\.json is missing/);
  });
});

test('a recording closed before the first picture was taken is refused with a plain message', { skip: !python && 'needs python3' }, () => {
  withTempDir((root) => {
    const run = makeRun(root, { rows: [] });

    const result = assemble(run, join(root, 'take.mp4'));

    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /has no pictures/);
  });
});
