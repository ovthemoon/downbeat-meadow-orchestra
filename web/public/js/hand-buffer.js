// Layout of the shared hand buffer (plan 5.2): Float64Array(135)
//   [0]=seq [1]=t_frame_ms [2]=t_infer_end_ms [3]=trackerState [4]=handCount
//   then per hand (max 2): [id, score, x0,y0,z0 ... x20,y20,z20] = 65 values
export const LANDMARKS_PER_HAND = 21;
export const HEADER_LENGTH = 5;
export const HAND_STRIDE = 2 + LANDMARKS_PER_HAND * 3;
export const MAX_HANDS = 2;
export const BUFFER_LENGTH = HEADER_LENGTH + MAX_HANDS * HAND_STRIDE;
export const TRACKER_STATE_RUNNING = 1;

/**
 * Write one detection result into the shared buffer. The buffer is overwritten in place, the header last,
 * so a reader that sees a new seq always sees the matching body (single-threaded page: JS cannot interleave).
 * @param {Float64Array} buffer
 * @param {{seq:number, frameMs:number, inferEndMs:number, hands:{score:number, landmarks:{x:number,y:number,z:number}[]}[]}} frame
 */
export function writeFrame(buffer, frame) {
  if (buffer.length < BUFFER_LENGTH) throw new RangeError('hand buffer too small');
  const handCount = Math.min(frame.hands.length, MAX_HANDS);
  buffer.fill(0, HEADER_LENGTH);
  for (let h = 0; h < handCount; h++) {
    const hand = frame.hands[h];
    const base = HEADER_LENGTH + h * HAND_STRIDE;
    buffer[base] = h;
    buffer[base + 1] = hand.score;
    for (let i = 0; i < LANDMARKS_PER_HAND; i++) {
      const p = hand.landmarks[i];
      buffer[base + 2 + i * 3] = p.x;
      buffer[base + 3 + i * 3] = p.y;
      buffer[base + 4 + i * 3] = p.z;
    }
  }
  buffer[1] = frame.frameMs;
  buffer[2] = frame.inferEndMs;
  buffer[3] = TRACKER_STATE_RUNNING;
  buffer[4] = handCount;
  buffer[0] = frame.seq;
}
