// Start screen: one button. The click is the user gesture that unlocks audio; then the camera is requested (hand mode) while Unity loads.
// A camera that is denied, missing or broken does not stop the page: Unity switches to mouse play and says so in the badge.
import { createAudioEngine } from './downbeat-audio.js';
import { createPage } from './page-state.js';
import { createHandBuffer } from './hand-buffer.js';
import { HandTracker } from './tracker.js';
import { HandRecorder } from './hand-recorder.js';
import { drawPreview } from './hand-preview.js';
import { externalHosts, resolveSources } from './tracker-config.js';
import { parseInputMode, wantsCamera } from './input-mode.js';

const params = new URLSearchParams(window.location.search);
const audio = createAudioEngine();
const page = createPage({ getContext: () => audio.context() });
const recorder = new HandRecorder();
const hands = {
  buffer: createHandBuffer(),
  recorder,
  tracker: null,
  mode: parseInputMode(params.get('input')),
  sources: resolveSources(params),
  /** For measurement scripts: tracker statistics (inference time, frame interval, hands seen). */
  snapshot: (options) => hands.tracker?.snapshot(options) ?? null,
};
window.Downbeat = { audio, page, hands };

const CAMERA_STATE_TEXT = {
  1: 'camera: waiting for permission, loading the hand model...',
  2: 'camera: hand tracking is running',
  3: 'camera: permission was denied, using the mouse',
  4: 'camera: none found, using the mouse',
  5: 'camera: could not be opened, using the mouse',
  6: 'camera: the hand model could not be loaded, using the mouse',
};

export function wireStartScreen({ button, overlay, statusLine, cameraLine, badge, video, previewCanvas, privacyHosts, startUnity }) {
  const show = (text) => { statusLine.textContent = text; };
  const showPreview = params.get('preview') !== '0';
  const previewCtx = previewCanvas?.getContext('2d');

  page.onStatus = show;
  page.onInputMode = (label) => {
    badge.textContent = label;
    badge.hidden = false;
  };
  page.readyCallbacks.push(() => overlay.classList.add('hidden'));
  privacyHosts.textContent = externalHosts(hands.sources, window.location.origin).join(', ') || '(none: local copies)';

  function startTracking() {
    hands.tracker = new HandTracker({
      buffer: hands.buffer,
      video,
      sources: hands.sources,
      recorder,
      onState: (state, detail) => {
        cameraLine.textContent = CAMERA_STATE_TEXT[state] ?? '';
        if (state === 2 && showPreview) previewCanvas.hidden = false;
        if (state >= 3) console.warn(`[downbeat] camera: ${detail}`);
      },
      onFrame: (frame) => { if (previewCtx && !previewCanvas.hidden) drawPreview(previewCtx, video, frame.hands); },
    });
    return hands.tracker.start();
  }
  hands.startTracking = startTracking;

  button.addEventListener('click', async () => {
    button.disabled = true;
    try {
      show('starting audio...');
      const state = await audio.start();
      if (state !== 'running') throw new Error(`audio context is ${state}`);
      if (wantsCamera(hands.mode)) startTracking(); // not awaited: the camera prompt and the model download run while Unity loads
      show('loading...');
      await startUnity();
    } catch (error) {
      button.disabled = false;
      show(`could not start: ${error.message ?? error}`);
      console.error(error);
    }
  }, { once: false });

  document.addEventListener('keydown', (event) => {
    if (event.key.toLowerCase() !== 'r' || event.ctrlKey || event.metaKey || event.altKey) return;
    if (!hands.tracker || hands.tracker.state !== 2) return;
    toggleRecording(badge);
  });
}

/** R starts and stops recording the hand motion; stopping saves a replay file (play it back with ?input=replay&file=...). */
function toggleRecording(badge) {
  if (!recorder.isRecording) {
    recorder.start();
    badge.textContent = `${page.inputLabel}  [REC]`;
    return;
  }
  const replay = recorder.stop('recorded in the browser with the R key');
  badge.textContent = page.inputLabel;
  const blob = new Blob([JSON.stringify(replay)], { type: 'application/json' });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = `downbeat-hands-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
  link.click();
  URL.revokeObjectURL(link.href);
}
