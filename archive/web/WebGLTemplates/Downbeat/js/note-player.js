// Plays samples at absolute audio-clock times. This is the whole "scheduler" on the page side: Unity decides the grid slot
// and hands over the audio time; the page only executes it and keeps books (late, stolen, missing).
const DEFAULTS = Object.freeze({ maxVoices: 32, minLeadSec: 0.005, stealFadeSec: 0.02 });

export class NotePlayer {
  #ctx;
  #destination;
  #buffers;
  #voices = [];
  #stats = { scheduled: 0, late: 0, stolen: 0, missingSample: 0, maxLateMs: 0 };
  maxVoices;
  minLeadSec;

  constructor({ ctx, destination, buffers, maxVoices = DEFAULTS.maxVoices, minLeadSec = DEFAULTS.minLeadSec }) {
    this.#ctx = ctx;
    this.#destination = destination;
    this.#buffers = buffers;
    this.maxVoices = maxVoices;
    this.minLeadSec = minLeadSec;
  }

  get stats() {
    return { ...this.#stats };
  }

  get activeVoices() {
    this.#prune(this.#ctx.currentTime);
    return this.#voices.length;
  }

  /**
   * @returns {number} the audio time the note will start: `when`, or later if it was requested too late (counted as late).
   *   Returns `when` unchanged and counts a missing sample if the buffer is not loaded.
   */
  play({ sampleIndex, when, rate, gain }) {
    const buffer = this.#buffers[sampleIndex];
    if (!buffer || !Number.isFinite(when) || !(rate > 0) || !Number.isFinite(gain)) {
      this.#stats.missingSample += 1;
      return when;
    }
    const now = this.#ctx.currentTime;
    const earliest = now + this.minLeadSec;
    let start = when;
    if (when < earliest) {
      start = earliest;
      this.#stats.late += 1;
      this.#stats.maxLateMs = Math.max(this.#stats.maxLateMs, (earliest - when) * 1000);
    }

    this.#prune(now);
    if (this.#voices.length >= this.maxVoices) this.#steal(now);

    const source = this.#ctx.createBufferSource();
    source.buffer = buffer;
    source.playbackRate.value = rate;
    const voiceGain = this.#ctx.createGain();
    voiceGain.gain.value = gain;
    source.connect(voiceGain);
    voiceGain.connect(this.#destination);
    const voice = { source, voiceGain, start, end: start + buffer.duration / rate };
    source.onended = () => this.#release(voice);
    source.start(start);
    this.#voices.push(voice);
    this.#stats.scheduled += 1;
    return start;
  }

  #prune(now) {
    this.#voices = this.#voices.filter((v) => v.end > now);
  }

  #steal(now) {
    const oldest = this.#voices.shift();
    this.#stats.stolen += 1;
    // fade out quickly instead of cutting, so stealing does not click
    oldest.voiceGain.gain.setTargetAtTime(0, now, DEFAULTS.stealFadeSec / 3);
    oldest.source.stop(now + DEFAULTS.stealFadeSec);
  }

  #release(voice) {
    voice.source.disconnect();
    voice.voiceGain.disconnect();
    this.#voices = this.#voices.filter((v) => v !== voice);
  }
}
