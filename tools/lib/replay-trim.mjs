// Cuts a recorded hand file down to exactly one loop of a looping fake-camera video. The fake video starts and ends with the hand at
// rest, so the seam between two passes shows up as a long still stretch: the recording is cut from the middle of one such stretch to the
// middle of the next. Pure functions (tested in replay-trim.test.mjs).
const PALM = [0, 5, 9, 13, 17];

function palmOf(hand) {
  let x = 0;
  let y = 0;
  for (const index of PALM) {
    x += hand.p[index * 2];
    y += hand.p[index * 2 + 1];
  }
  return { x: x / PALM.length, y: y / PALM.length };
}

/** Frames where a hand is present and almost still, as [startIndex, endIndex] runs lasting at least minRestSec. */
export function findRestStretches(frames, { minRestSec = 1.0, maxSpeed = 0.15 } = {}) {
  const stretches = [];
  let start = null;
  let previous = null;
  const close = (endIndex) => {
    if (start !== null && (frames[endIndex].t - frames[start].t) / 1000 >= minRestSec) stretches.push([start, endIndex]);
    start = null;
  };
  frames.forEach((frame, i) => {
    const hand = frame.h[0];
    if (!hand) { close(Math.max(0, i - 1)); previous = null; return; }
    const here = palmOf(hand);
    const still = previous === null || Math.hypot(here.x - previous.x, here.y - previous.y) / ((frame.t - previous.t) / 1000 || 1) < maxSpeed;
    previous = { ...here, t: frame.t };
    if (still) { start ??= i; } else { close(i - 1); }
  });
  if (start !== null) close(frames.length - 1);
  return stretches;
}

/** One loop, from the middle of the first long rest to the middle of the second; times start at 0 again. */
export function trimToLoop(replay, options) {
  const stretches = findRestStretches(replay.frames, options);
  if (stretches.length < 2) throw new Error(`found ${stretches.length} long rest stretches, need 2 to cut one loop`);
  const middle = ([a, b]) => Math.round((a + b) / 2);
  const from = middle(stretches[0]);
  const to = middle(stretches[1]);
  const t0 = replay.frames[from].t;
  const frames = replay.frames.slice(from, to).map((frame) => ({ ...frame, t: Math.round((frame.t - t0) * 10000) / 10000 }));
  return { ...replay, frames };
}
