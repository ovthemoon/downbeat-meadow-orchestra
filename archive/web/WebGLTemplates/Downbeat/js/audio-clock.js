// Pure helpers for relating the audio clock to the page clock. No DOM access, so they run in Node tests with a fake context.

/**
 * Audio time of the sample that is leaving the speakers right now (output latency included), extrapolated to `perfNowMs`.
 * getOutputTimestamp() pairs a context time with the performance.now() time at which that sample is heard.
 * If the browser does not provide it, fall back to currentTime minus the reported output latency.
 */
export function heardAudioTime(ctx, perfNowMs) {
  const ts = typeof ctx.getOutputTimestamp === 'function' ? ctx.getOutputTimestamp() : null;
  if (ts && ts.performanceTime > 0 && ts.contextTime > 0) {
    return ts.contextTime + (perfNowMs - ts.performanceTime) / 1000;
  }
  const latency = Number.isFinite(ctx.outputLatency) && ctx.outputLatency > 0 ? ctx.outputLatency : (ctx.baseLatency ?? 0);
  return ctx.currentTime - latency;
}

/** Page time (performance.now() ms) at which the sample of audio time `audioTime` is heard. */
export function audioTimeToPerfMs(ctx, perfNowMs, audioTime) {
  return perfNowMs + (audioTime - heardAudioTime(ctx, perfNowMs)) * 1000;
}

/**
 * Day 0: after resume() the context clock can sit still for about a second. The watcher says "advancing" only after
 * currentTime has really moved by `minAdvanceSec` while the context was running.
 */
export class ClockWatcher {
  #minAdvanceSec;
  #base = null;

  constructor({ minAdvanceSec = 0.25 } = {}) {
    this.#minAdvanceSec = minAdvanceSec;
  }

  observe(currentTime, running) {
    if (!running) {
      this.#base = null;
      return false;
    }
    if (this.#base === null) this.#base = currentTime;
    return currentTime - this.#base >= this.#minAdvanceSec;
  }
}
