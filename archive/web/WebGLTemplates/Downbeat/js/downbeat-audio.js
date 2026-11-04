// Page side of the Unity <-> Web Audio bridge (window.Downbeat.audio). The Unity jslib (Downbeat.jslib) calls these methods.
import { ClockWatcher, heardAudioTime } from './audio-clock.js';
import { NotePlayer } from './note-player.js';

export const STATE_CONTEXT = 1;
export const STATE_RUNNING = 2;
export const STATE_CLOCK_ADVANCING = 4;
const STAT_LATE = 1;
const STAT_STOLEN = 2;
const STAT_MISSING = 3;
const STAT_SCHEDULED = 4;
const FLASH_LOG_LIMIT = 4000;

/**
 * @param env injectable for tests: { AudioContextClass, fetchImpl, now }
 */
export function createAudioEngine(env = {}) {
  const AudioContextClass = env.AudioContextClass ?? globalThis.AudioContext;
  const fetchImpl = env.fetchImpl ?? globalThis.fetch?.bind(globalThis);
  const now = env.now ?? (() => performance.now());

  let ctx = null;
  let master = null;
  let player = null;
  let masterGain = 0.5;
  let maxVoices = 32;
  let loaded = 0;
  let failed = 0;
  const buffers = [];
  const watcher = new ClockWatcher();
  const flashLog = [];

  function ensurePlayer() {
    if (!ctx || player) return;
    player = new NotePlayer({ ctx, destination: master, buffers, maxVoices });
  }

  return {
    /** Must be called from a user gesture (the start button). Resolves with the context state. */
    async start() {
      if (ctx) return ctx.state;
      ctx = new AudioContextClass({ latencyHint: 'interactive' });
      master = ctx.createGain();
      master.gain.value = masterGain;
      master.connect(ctx.destination);
      await ctx.resume();
      ensurePlayer();
      return ctx.state;
    },

    state() {
      if (!ctx) return 0;
      const running = ctx.state === 'running';
      const advancing = watcher.observe(ctx.currentTime, running);
      return STATE_CONTEXT | (running ? STATE_RUNNING : 0) | (advancing ? STATE_CLOCK_ADVANCING : 0);
    },

    now() {
      return ctx ? ctx.currentTime : 0;
    },

    heardNow() {
      return ctx ? heardAudioTime(ctx, now()) : 0;
    },

    configure(masterLinear, voices) {
      masterGain = masterLinear;
      maxVoices = voices;
      if (master) master.gain.value = masterGain;
      if (player) player.maxVoices = maxVoices;
    },

    async loadSample(index, url) {
      try {
        const response = await fetchImpl(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        buffers[index] = await ctx.decodeAudioData(await response.arrayBuffer());
        loaded += 1;
      } catch (error) {
        failed += 1;
        console.error(`[downbeat] sample ${index} failed: ${url}: ${error}`);
      }
    },

    samplesLoaded: () => loaded,
    samplesFailed: () => failed,

    schedule(sampleIndex, when, rate, gain) {
      ensurePlayer();
      return player ? player.play({ sampleIndex, when, rate, gain }) : when;
    },

    /** Measurement: Unity released the flash of the note scheduled for `when` at heard time `heardAtFlash` (audio seconds). */
    logFlash(when, heardAtFlash) {
      if (flashLog.length >= FLASH_LOG_LIMIT) flashLog.shift();
      flashLog.push({ when, heardAtFlash, perfMs: now() });
    },

    stat(which) {
      const s = player ? player.stats : { scheduled: 0, late: 0, stolen: 0, missingSample: 0 };
      switch (which) {
        case STAT_LATE: return s.late;
        case STAT_STOLEN: return s.stolen;
        case STAT_MISSING: return s.missingSample;
        case STAT_SCHEDULED: return s.scheduled;
        default: return 0;
      }
    },

    /** For measurement scripts: everything the page knows, as plain data. */
    snapshot() {
      return {
        sampleRate: ctx?.sampleRate ?? null,
        baseLatency: ctx?.baseLatency ?? null,
        outputLatency: ctx?.outputLatency ?? null,
        player: player ? player.stats : null,
        loaded,
        failed,
        flashes: flashLog.slice(),
      };
    },

    context: () => ctx,
  };
}
