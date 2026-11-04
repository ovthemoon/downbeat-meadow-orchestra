// Path 3 of S-1: Web Audio. A requestAnimationFrame loop schedules clicks `lookaheadSec` ahead with start(when).
import { createGrid, lastSlotAtOrBefore, slotTime } from './beat-grid.js';

let sharedContext = null;
let sharedBuffer = null;

/** Create (once) the page's own context, tagged so the tap can tell it from Unity's. */
export async function ensureWebAudio() {
  if (sharedContext) return { ctx: sharedContext, buffer: sharedBuffer };
  window.__tap?.tagNextContext('webaudio');
  sharedContext = new AudioContext();
  const bytes = await (await fetch('/audio/click.wav')).arrayBuffer();
  sharedBuffer = await sharedContext.decodeAudioData(bytes);
  await window.__tap?.whenReady();
  if (sharedContext.state !== 'running') await sharedContext.resume();
  return { ctx: sharedContext, buffer: sharedBuffer };
}

export function startWebAudioBeat({ ctx, buffer, bpm, slotsPerBeat = 4, durationSec, lookaheadSec = 0.1, startDelaySec = 0.6, frameDivisor = 1 }) {
  const grid = createGrid(ctx.currentTime + startDelaySec, bpm, slotsPerBeat);
  const totalSlots = Math.floor(durationSec / grid.slotSec);
  const records = [];
  let nextSlot = 0;
  let skippedLate = 0;
  let rafId = 0;
  let stopped = false;
  let frameCount = 0;
  const originPerfMs = performance.now();
  window.__tap?.mark('beat-start:webaudio');

  // frameDivisor 2 runs the scheduler on every second animation frame (models a 60 Hz display on a 120 Hz one).
  const frame = () => {
    if (stopped) return;
    frameCount += 1;
    if (frameCount % frameDivisor !== 0) {
      rafId = requestAnimationFrame(frame);
      return;
    }
    const now = ctx.currentTime;
    const lastSlot = Math.min(totalSlots - 1, lastSlotAtOrBefore(grid, now + lookaheadSec));
    while (nextSlot <= lastSlot) {
      const target = slotTime(grid, nextSlot);
      if (target < now) {
        skippedLate += 1;
      } else {
        const source = ctx.createBufferSource();
        source.buffer = buffer;
        source.connect(ctx.destination);
        source.start(target);
        records.push({ slot: nextSlot, targetCtx: target, ctxAtCall: now, perfMsAtCall: performance.now() });
      }
      nextSlot += 1;
    }
    if (nextSlot < totalSlots) rafId = requestAnimationFrame(frame);
  };
  rafId = requestAnimationFrame(frame);

  return {
    totalSlots,
    stop() {
      stopped = true;
      cancelAnimationFrame(rafId);
      window.__tap?.mark('beat-end:webaudio');
      return {
        path: 'webaudio',
        bpm,
        slotsPerBeat,
        frameDivisor,
        totalSlots,
        issued: records.length,
        skippedLate,
        originClock: grid.origin,
        originPerfMs,
        endPerfMs: performance.now(),
        records,
      };
    },
  };
}
