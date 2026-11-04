// The hand buffer: one Float64Array that the tracker overwrites and Unity pulls every frame (Downbeat.jslib, Downbeat_Hands_Read).
// Layout (must match HandBufferLayout in Core/HandFrame.cs; a test in each language pins the numbers):
//   [0]=seq [1]=capture ms [2]=inference end ms [3]=tracker state [4]=hand count
//   then per hand (max 2): [id, score, x0,y0,z0 ... x20,y20,z20] = 65 values
//   [135]=video aspect (width / height)
// x is stored mirrored (0 = left as seen in a mirror), y is 0 at the top.
export const LANDMARKS_PER_HAND = 21;
export const HEADER_LENGTH = 5;
export const HAND_STRIDE = 2 + LANDMARKS_PER_HAND * 3;
export const MAX_HANDS = 2;
export const ASPECT_INDEX = HEADER_LENGTH + MAX_HANDS * HAND_STRIDE;
export const BUFFER_LENGTH = ASPECT_INDEX + 1;

export const TrackerState = Object.freeze({
  OFF: 0,
  STARTING: 1,
  RUNNING: 2,
  CAMERA_DENIED: 3,
  CAMERA_MISSING: 4,
  CAMERA_ERROR: 5,
  MODEL_FAILED: 6,
});

export function createHandBuffer() {
  return new Float64Array(BUFFER_LENGTH);
}

export function writeState(buffer, state) {
  buffer[3] = state;
}

/**
 * Overwrites the buffer with one tracker result. The body is written first and seq last; JS is single threaded, and Unity reads
 * from the same thread, so a reader can never see a half-written frame. Hands beyond MAX_HANDS are ignored.
 * @param {Float64Array} buffer
 * @param {{seq:number, captureMs:number, inferEndMs:number, aspect:number, hands:{id:number, score:number, landmarks:{x:number,y:number,z?:number}[]}[]}} frame
 * @param {{mirror?:boolean}} [options] mirror: x -> 1 - x (the default; the player looks at a mirror)
 */
export function writeFrame(buffer, frame, { mirror = true } = {}) {
  if (buffer.length < BUFFER_LENGTH) throw new RangeError('hand buffer too small');
  const handCount = Math.min(frame.hands.length, MAX_HANDS);
  buffer.fill(0, HEADER_LENGTH, ASPECT_INDEX);
  for (let slot = 0; slot < handCount; slot++) {
    const hand = frame.hands[slot];
    const base = HEADER_LENGTH + slot * HAND_STRIDE;
    buffer[base] = hand.id;
    buffer[base + 1] = hand.score;
    for (let i = 0; i < LANDMARKS_PER_HAND; i++) {
      const p = hand.landmarks[i];
      buffer[base + 2 + i * 3] = mirror ? 1 - p.x : p.x;
      buffer[base + 3 + i * 3] = p.y;
      buffer[base + 4 + i * 3] = p.z ?? 0;
    }
  }
  buffer[1] = frame.captureMs;
  buffer[2] = frame.inferEndMs;
  buffer[ASPECT_INDEX] = frame.aspect;
  buffer[3] = TrackerState.RUNNING;
  buffer[4] = handCount;
  buffer[0] = frame.seq;
}
