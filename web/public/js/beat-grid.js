// Beat grid math (mirror of Downbeat.Core.BeatGrid in C#). Slot time comes from the integer slot index:
// T(n) = origin + n * slotDuration, so error never accumulates.
const EPSILON_SEC = 1e-9;

export function slotDuration(bpm, slotsPerBeat) {
  if (!(bpm > 0)) throw new RangeError('bpm must be positive');
  if (!Number.isInteger(slotsPerBeat) || slotsPerBeat < 1) throw new RangeError('slotsPerBeat must be an integer >= 1');
  return 60 / bpm / slotsPerBeat;
}

export function createGrid(origin, bpm, slotsPerBeat) {
  return Object.freeze({ origin, bpm, slotsPerBeat, slotSec: slotDuration(bpm, slotsPerBeat) });
}

export function slotTime(grid, n) {
  return grid.origin + n * grid.slotSec;
}

export function firstSlotAtOrAfter(grid, time) {
  return Math.ceil((time - grid.origin) / grid.slotSec - EPSILON_SEC / grid.slotSec);
}

export function lastSlotAtOrBefore(grid, time) {
  return Math.floor((time - grid.origin) / grid.slotSec + EPSILON_SEC / grid.slotSec);
}
