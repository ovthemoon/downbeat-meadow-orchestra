// Hand id continuity: MediaPipe returns hands in no particular order from frame to frame, so each detection is matched to the
// hand that was nearest in the previous frames and keeps that id (0 or 1). Pure; the clock is an argument.

/** Palm landmarks: wrist and the four finger base joints (the same five the Unity side averages). */
export const PALM_LANDMARKS = Object.freeze([0, 5, 9, 13, 17]);

export function palmCenter(landmarks) {
  let x = 0;
  let y = 0;
  for (const index of PALM_LANDMARKS) {
    x += landmarks[index].x;
    y += landmarks[index].y;
  }
  return { x: x / PALM_LANDMARKS.length, y: y / PALM_LANDMARKS.length };
}

export class HandIdAssigner {
  /**
   * @param {{maxHands?:number, maxJump?:number, holdMs?:number}} [options]
   *   maxJump: farthest a hand may move between two frames and still be the same hand (normalized video units)
   *   holdMs: how long an id stays reserved for a hand that is not seen (so it gets the same id when it comes back)
   */
  constructor({ maxHands = 2, maxJump = 0.35, holdMs = 400 } = {}) {
    this.maxHands = maxHands;
    this.maxJump = maxJump;
    this.holdMs = holdMs;
    this.tracks = []; // { id, x, y, lastSeen }
  }

  reset() {
    this.tracks = [];
  }

  /**
   * @param {{x:number,y:number}[]} centers palm centers of this frame's detections
   * @param {number} nowMs
   * @returns {number[]} one id per detection, in the same order (never the same id twice)
   */
  assign(centers, nowMs) {
    this.tracks = this.tracks.filter((track) => nowMs - track.lastSeen <= this.holdMs);
    const count = Math.min(centers.length, this.maxHands);
    const matching = this.#bestMatching(centers.slice(0, count));
    const ids = new Array(count).fill(-1);
    const usedIds = new Set();
    matching.forEach((trackIndex, detection) => {
      if (trackIndex === -1) return;
      ids[detection] = this.tracks[trackIndex].id;
      usedIds.add(ids[detection]);
    });
    for (let d = 0; d < count; d++) {
      if (ids[d] === -1) {
        ids[d] = this.#freeId(usedIds);
        usedIds.add(ids[d]);
      }
    }
    for (let d = 0; d < count; d++) {
      this.tracks = this.tracks.filter((track) => track.id !== ids[d]);
      this.tracks.push({ id: ids[d], x: centers[d].x, y: centers[d].y, lastSeen: nowMs });
    }
    return ids;
  }

  /** Detection -> track index (-1 = new hand). Most matches first, then the smallest total distance. Tiny search: at most 2 x 2. */
  #bestMatching(centers) {
    let best = { matches: -1, cost: Infinity, pairs: centers.map(() => -1) };
    const pairs = centers.map(() => -1);
    const search = (d, matches, cost, taken) => {
      if (d === centers.length) {
        if (matches > best.matches || (matches === best.matches && cost < best.cost)) best = { matches, cost, pairs: pairs.slice() };
        return;
      }
      pairs[d] = -1;
      search(d + 1, matches, cost, taken);
      for (let t = 0; t < this.tracks.length; t++) {
        if (taken.has(t)) continue;
        const distance = Math.hypot(centers[d].x - this.tracks[t].x, centers[d].y - this.tracks[t].y);
        if (distance > this.maxJump) continue;
        pairs[d] = t;
        taken.add(t);
        search(d + 1, matches + 1, cost + distance, taken);
        taken.delete(t);
        pairs[d] = -1;
      }
    };
    search(0, 0, 0, new Set());
    return best.pairs;
  }

  /** The smallest id nobody holds; when all are held by unseen hands, the id that has been unseen the longest. */
  #freeId(usedIds) {
    const held = new Set(this.tracks.map((track) => track.id));
    for (let id = 0; id < this.maxHands; id++) {
      if (!usedIds.has(id) && !held.has(id)) return id;
    }
    const stale = this.tracks.filter((track) => !usedIds.has(track.id)).sort((a, b) => a.lastSeen - b.lastSeen);
    return stale.length ? stale[0].id : 0;
  }
}
