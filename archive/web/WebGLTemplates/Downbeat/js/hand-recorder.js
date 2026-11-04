// Records what the tracker produced (hand landmarks, not video) so that a run can be played back later: content/replay/*.json,
// loaded with ?input=replay. The format is read by ReplayDto in Core/ReplayData.cs.
export const REPLAY_FORMAT = 'downbeat-hands';
export const REPLAY_VERSION = 1;
export const MAX_RECORDED_FRAMES = 36000; // 20 minutes at 30 fps
const DECIMALS = 4;

const round = (value) => Number(value.toFixed(DECIMALS));

export class HandRecorder {
  #frames = [];
  #firstMs = null;
  #aspect = 4 / 3;
  #recording = false;

  get isRecording() {
    return this.#recording;
  }

  get frameCount() {
    return this.#frames.length;
  }

  start() {
    this.#frames = [];
    this.#firstMs = null;
    this.#recording = true;
  }

  /**
   * @param {{captureMs:number, aspect:number, hands:{id:number, score:number, landmarks:{x:number,y:number}[]}[]}} frame
   *   landmarks exactly as written to the hand buffer (x already mirrored)
   */
  add(frame) {
    if (!this.#recording || this.#frames.length >= MAX_RECORDED_FRAMES) return;
    this.#firstMs ??= frame.captureMs;
    this.#aspect = frame.aspect;
    this.#frames.push({
      t: round(frame.captureMs - this.#firstMs),
      h: frame.hands.map((hand) => ({
        id: hand.id,
        s: round(hand.score),
        p: hand.landmarks.flatMap((point) => [round(point.x), round(point.y)]),
      })),
    });
  }

  /** Stops and returns the replay object (see ReplayDto). */
  stop(note = '') {
    this.#recording = false;
    return {
      format: REPLAY_FORMAT,
      version: REPLAY_VERSION,
      note,
      aspect: round(this.#aspect),
      mirrored: true,
      frames: this.#frames,
    };
  }
}
