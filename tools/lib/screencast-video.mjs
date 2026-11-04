// Turns CDP screencast frames (JPEG + epoch timestamps, delivered only when the picture changes) into a constant-rate mp4.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

export function runFfmpeg(args) {
  const result = spawnSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args], { encoding: 'utf8' });
  if (result.status !== 0) throw new Error(`ffmpeg failed: ${result.stderr}`);
}

/**
 * Pure: the concat list for the window [startEpochSec, endEpochSec]. Each frame is shown from its own timestamp until the next one.
 * @param {{epochSec:number}[]} frames sorted ascending
 * @returns {{index:number, durationSec:number}[]}
 */
export function planFrames(frames, startEpochSec, endEpochSec) {
  const inside = frames.filter((f) => f.epochSec <= endEpochSec);
  const firstIndex = Math.max(0, inside.findLastIndex((f) => f.epochSec <= startEpochSec));
  const plan = [];
  for (let i = firstIndex; i < inside.length; i++) {
    // The first frame also covers the time before it arrived: otherwise the whole video would start late and every later
    // picture would show up earlier than its sound.
    const begin = i === firstIndex ? startEpochSec : Math.max(inside[i].epochSec, startEpochSec);
    const end = i + 1 < inside.length ? Math.min(inside[i + 1].epochSec, endEpochSec) : endEpochSec;
    if (end > begin) plan.push({ index: i, durationSec: end - begin });
  }
  return plan;
}

/** Writes the frames of `plan` and a concat list into `dir`, then encodes `dir/video.mp4` at `fps` (no audio). */
export function encodeVideo({ frames, plan, dir, fps, width, height, crf = 20 }) {
  mkdirSync(dir, { recursive: true });
  const lines = [];
  for (const step of plan) {
    const name = `frame_${String(step.index).padStart(6, '0')}.jpg`;
    writeFileSync(join(dir, name), Buffer.from(frames[step.index].data, 'base64'));
    lines.push(`file '${name}'`, `duration ${step.durationSec.toFixed(6)}`);
  }
  lines.push(lines[lines.length - 2]); // the concat demuxer ignores the duration of the last entry unless the file is repeated
  writeFileSync(join(dir, 'frames.txt'), lines.join('\n'));
  // -r 1000 first: read the stills at a rate fine enough that millisecond durations are not rounded to a 25 fps grid
  runFfmpeg(['-f', 'concat', '-safe', '0', '-i', join(dir, 'frames.txt'), '-vf', `fps=${fps},scale=${width}:${height},format=yuv420p`,
    '-c:v', 'libx264', '-crf', String(crf), '-preset', 'medium', join(dir, 'video.mp4')]);
  return join(dir, 'video.mp4');
}
