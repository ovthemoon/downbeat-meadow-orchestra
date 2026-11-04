// What the page keeps for Unity and for measurement scripts (window.Downbeat.page): start-up status, the active input mode's label,
// and a log of every note the scheduler saw.
import { audioTimeToPerfMs } from './audio-clock.js';

export const NOTE_LOG_LIMIT = 4000;

export function createPage({ now = () => performance.now(), getContext = () => null } = {}) {
  const page = {
    isReady: false,
    statusText: '',
    inputLabel: '',
    noteLog: [],
    /** null normally; a measurement script sets it to [] and Unity's loop then pushes performance.now() once per frame. */
    frameTimes: null,
    readyCallbacks: [],
    onStatus: null,
    onInputMode: null,

    ready() {
      page.isReady = true;
      page.readyCallbacks.forEach((fn) => fn());
    },

    status(text) {
      page.statusText = text;
      page.onStatus?.(text);
    },

    setInputMode(label) {
      page.inputLabel = label;
      page.onInputMode?.(label);
    },

    /**
     * One note as Unity logged it: audio times in seconds (requestedAt, when, audioNow), page times in ms (captureMs, inferEndMs,
     * receivedMs, decidedMs, triggerCaptureMs; NaN when the note did not come from the camera). slot -1 = the scheduler dropped it.
     * whenPerfMs converts the audio time the note will sound to the page clock, so it can be compared with captureMs.
     */
    logNote(note) {
      const ctx = getContext();
      const whenPerfMs = ctx && Number.isFinite(note.when) ? audioTimeToPerfMs(ctx, now(), note.when) : null;
      if (page.noteLog.length >= NOTE_LOG_LIMIT) page.noteLog.shift();
      page.noteLog.push({ ...note, whenPerfMs, loggedMs: now() });
    },
  };
  return page;
}
